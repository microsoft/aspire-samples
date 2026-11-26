"""Retrieval-augmented generation domain: chunking, indexing, retrieval, and domain telemetry."""

import logging
import re
import uuid
from dataclasses import dataclass
from pathlib import Path
from time import perf_counter

from openai import AsyncOpenAI
from opentelemetry import metrics, trace
from qdrant_client import AsyncQdrantClient
from qdrant_client.http.models import FieldCondition, Filter, FilterSelector, MatchValue, PointStruct

from qdrant_setup import COLLECTION_NAME
from telemetry import INSTRUMENTATION_NAME, capture_message_content

logger = logging.getLogger("document_qa.rag")
tracer = trace.get_tracer(INSTRUMENTATION_NAME)
meter = metrics.get_meter(INSTRUMENTATION_NAME)

SAMPLE_DOCS_DIR = Path(__file__).parent / "sample_docs"
MAX_CHUNK_CHARS = 1500

SYSTEM_PROMPT = (
    "You are the Contoso handbook assistant. Answer employee questions using only the numbered "
    "sources provided. Cite every fact with the source number in square brackets, e.g. [2]. "
    "Be concise and specific (amounts, deadlines, tools). If the sources don't answer the "
    "question, say so and suggest contacting People Operations."
)

# Domain metrics. Tags stay bounded (source, grounded) - never document names, chunk ids, or questions.
questions_answered = meter.create_counter(
    "document_qa.questions.answered",
    unit="{question}",
    description="Questions answered by the RAG pipeline.",
)
retrieval_duration = meter.create_histogram(
    "document_qa.retrieval.duration",
    unit="s",
    description="Time to embed the question and search the vector index.",
    explicit_bucket_boundaries_advisory=[0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5],
)
retrieval_top_score = meter.create_histogram(
    "document_qa.retrieval.top_score",
    unit="1",
    description="Cosine similarity of the best-matching chunk for each question.",
    explicit_bucket_boundaries_advisory=[0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9],
)
documents_indexed = meter.create_counter(
    "document_qa.documents.indexed",
    unit="{document}",
    description="Documents chunked, embedded, and written to the vector index.",
)

_index_chunks = 0


def _observe_index_chunks(_options: metrics.CallbackOptions):
    yield metrics.Observation(_index_chunks)


meter.create_observable_gauge(
    "document_qa.index.chunks",
    callbacks=[_observe_index_chunks],
    unit="{chunk}",
    description="Chunks currently stored in the vector index.",
)


@dataclass
class Chunk:
    id: str
    document: str
    title: str
    section: str
    index: int
    text: str


@dataclass
class RetrievedChunk:
    chunk: Chunk
    score: float


def _split_long(text: str) -> list[str]:
    """Group paragraphs into pieces of at most MAX_CHUNK_CHARS (hard-splitting oversized paragraphs)."""
    pieces: list[str] = []
    current = ""
    for para in (p.strip() for p in re.split(r"\n\s*\n", text) if p.strip()):
        while len(para) > MAX_CHUNK_CHARS:
            pieces.append(para[:MAX_CHUNK_CHARS])
            para = para[MAX_CHUNK_CHARS:]
        if current and len(current) + len(para) + 2 > MAX_CHUNK_CHARS:
            pieces.append(current)
            current = para
        else:
            current = f"{current}\n\n{para}" if current else para
    if current:
        pieces.append(current)
    return pieces


def chunk_document(filename: str, text: str) -> list[Chunk]:
    """Split a document into section-aware chunks.

    `# Title` names the document and each `## Heading` starts a section, so citations can point
    at "Hybrid work policy > Home office stipend". Plain text without headings is split by paragraph.
    """
    stem = Path(filename).stem
    title = stem.replace("-", " ").replace("_", " ").capitalize()
    sections: list[tuple[str, str]] = []
    heading, body = "", []
    for line in text.replace("\r\n", "\n").split("\n"):
        if line.startswith("# ") and not sections and not body:
            title = line[2:].strip()
        elif line.startswith("## "):
            if "".join(body).strip():
                sections.append((heading, "\n".join(body)))
            heading, body = line[3:].strip(), []
        else:
            body.append(line)
    if "".join(body).strip():
        sections.append((heading, "\n".join(body)))

    chunks: list[Chunk] = []
    for heading, body_text in sections:
        for piece in _split_long(body_text):
            index = len(chunks)
            chunks.append(Chunk(id=f"{stem}#{index + 1}", document=filename, title=title,
                                section=heading, index=index, text=piece))
    return chunks


def _point_id(chunk_id: str) -> str:
    # Deterministic ids make re-indexing a document idempotent.
    return str(uuid.uuid5(uuid.NAMESPACE_URL, f"contoso-handbook:{chunk_id}"))


async def refresh_index_stats(qdrant: AsyncQdrantClient) -> int:
    global _index_chunks
    _index_chunks = (await qdrant.count(COLLECTION_NAME, exact=True)).count
    return _index_chunks


async def index_document(openai: AsyncOpenAI, embeddings_model: str, qdrant: AsyncQdrantClient,
                         filename: str, text: str, source: str) -> list[Chunk]:
    """Chunk, embed (one batched call), and upsert a document, replacing any previous version."""
    with tracer.start_as_current_span("rag.index_document") as span:
        chunks = chunk_document(filename, text)
        span.set_attributes({
            "rag.document.name": filename,
            "rag.document.title": chunks[0].title if chunks else filename,
            "rag.index.source": source,
            "rag.chunk.count": len(chunks),
            "db.system.name": "qdrant",
            "db.collection.name": COLLECTION_NAME,
        })
        if not chunks:
            return chunks

        # Prefix each chunk with its title and section so headings contribute to the embedding.
        response = await openai.embeddings.create(
            model=embeddings_model, input=[f"{c.title} > {c.section}\n{c.text}" for c in chunks])
        await qdrant.delete(COLLECTION_NAME, points_selector=FilterSelector(filter=Filter(
            must=[FieldCondition(key="filename", match=MatchValue(value=filename))])))
        await qdrant.upsert(COLLECTION_NAME, points=[
            PointStruct(id=_point_id(c.id), vector=item.embedding, payload={
                "chunk_id": c.id, "filename": c.document, "title": c.title,
                "section": c.section, "chunk_index": c.index, "text": c.text,
            })
            for c, item in zip(chunks, response.data)
        ])
        span.add_event("rag.document.indexed", {"rag.chunk.ids": [c.id for c in chunks]})

    documents_indexed.add(1, {"rag.index.source": source})
    await refresh_index_stats(qdrant)
    logger.info("Indexed %s into %d chunk(s)", filename, len(chunks), extra={
        "rag.document.name": filename, "rag.chunk.count": len(chunks), "rag.index.source": source,
        "gen_ai.request.model": embeddings_model,
    })
    return chunks


async def retrieve(openai: AsyncOpenAI, embeddings_model: str, qdrant: AsyncQdrantClient,
                   question: str, top_k: int) -> tuple[list[RetrievedChunk], float]:
    """Embed the question and return the top-k most similar chunks plus the elapsed seconds."""
    start = perf_counter()
    with tracer.start_as_current_span("rag.retrieve") as span:
        span.set_attributes({
            "db.system.name": "qdrant",
            "db.collection.name": COLLECTION_NAME,
            "db.operation.name": "query",
            "rag.retrieval.top_k": top_k,
            "gen_ai.request.model": embeddings_model,
        })
        if capture_message_content():
            span.set_attribute("rag.retrieval.query", question)

        embedding = await openai.embeddings.create(model=embeddings_model, input=question)
        result = await qdrant.query_points(COLLECTION_NAME, query=embedding.data[0].embedding,
                                           limit=top_k, with_payload=True)
        hits = [
            RetrievedChunk(
                chunk=Chunk(id=p.payload["chunk_id"], document=p.payload["filename"], title=p.payload["title"],
                            section=p.payload["section"], index=p.payload["chunk_index"], text=p.payload["text"]),
                score=p.score,
            )
            for p in result.points
        ]
        for rank, hit in enumerate(hits, start=1):
            span.add_event("rag.retrieval.hit", {
                "rag.chunk.rank": rank,
                "rag.chunk.id": hit.chunk.id,
                "rag.chunk.score": round(hit.score, 4),
                "rag.document.name": hit.chunk.document,
                "rag.document.section": hit.chunk.section,
            })
        span.set_attributes({
            "rag.retrieval.result_count": len(hits),
            "rag.retrieval.chunk_ids": [h.chunk.id for h in hits],
            "rag.retrieval.scores": [round(h.score, 4) for h in hits],
        })
        if hits:
            span.set_attribute("rag.retrieval.top_score", round(hits[0].score, 4))
            retrieval_top_score.record(hits[0].score)

    elapsed = perf_counter() - start
    retrieval_duration.record(elapsed, {"db.system.name": "qdrant"})
    return hits, elapsed


def build_messages(question: str, hits: list[RetrievedChunk]) -> list[dict[str, str]]:
    sources = "\n\n".join(
        f"[{n}] {h.chunk.title} > {h.chunk.section or 'Overview'}\n{h.chunk.text}"
        for n, h in enumerate(hits, start=1)
    )
    return [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": f"Sources:\n{sources}\n\nQuestion: {question}"},
    ]


def cited_numbers(answer: str, source_count: int) -> list[int]:
    """Source numbers referenced as [n] in the answer, in first-cited order."""
    seen: list[int] = []
    for match in re.finditer(r"\[(\d+)\]", answer):
        n = int(match.group(1))
        if 1 <= n <= source_count and n not in seen:
            seen.append(n)
    return seen


def load_sample_documents() -> list[tuple[str, str]]:
    return [(p.name, p.read_text(encoding="utf-8")) for p in sorted(SAMPLE_DOCS_DIR.glob("*.txt"))]
