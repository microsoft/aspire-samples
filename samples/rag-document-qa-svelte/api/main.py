"""Contoso handbook Q&A API: retrieval-augmented generation with FastAPI, Qdrant, OpenAI, and OpenTelemetry."""

import asyncio
import logging
import os
import secrets
from contextlib import asynccontextmanager
from http import HTTPStatus
from pathlib import Path
from time import monotonic
from typing import Annotated, Any, Literal

import openai
from fastapi import Depends, FastAPI, File, HTTPException, Request, UploadFile, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from fastapi.security import APIKeyHeader
from fastapi.staticfiles import StaticFiles
from opentelemetry import trace
from pydantic import BaseModel, ConfigDict, Field
from qdrant_client import AsyncQdrantClient
from scalar_fastapi import get_scalar_api_reference

import rag
from qdrant_setup import COLLECTION_NAME, create_qdrant_client, ensure_collection
from telemetry import configure_opentelemetry, instrument_app

# Tracers/meters obtained earlier (e.g. in rag.py) are proxies that bind to these providers.
configure_opentelemetry()

logger = logging.getLogger("document_qa")

DEFAULT_CHAT_MODEL = "gpt-4.1-mini"
DEFAULT_EMBEDDINGS_MODEL = "text-embedding-3-small"

# Defense-in-depth caps so the upload and ask endpoints can't be turned into an unbounded
# OpenAI cost-burn or memory-growth vector when the AppHost wires the api with
# `withExternalHttpEndpoints`.
MAX_FILE_SIZE = 1_000_000      # 1 MB hard ceiling on uploaded payload size
MAX_CHUNKS_PER_DOC = 200
MAX_QUESTION_LENGTH = 2000
MAX_COMPLETION_TOKENS = 500
RATE_LIMIT_CAPACITY = 20
RATE_LIMIT_REFILL_PER_SEC = 0.33  # ~20 requests per minute per client

_openai_client: openai.AsyncOpenAI | None = None
_qdrant: AsyncQdrantClient | None = None
_chat_model = DEFAULT_CHAT_MODEL
_embeddings_model = DEFAULT_EMBEDDINGS_MODEL
_rate_buckets: dict[str, tuple[float, float]] = {}
_rate_lock = asyncio.Lock()

# Optional shared-secret auth. When RAG_API_KEY is set, mutating endpoints and /api/ask
# require callers to send the same value via the X-API-Key header.
_api_key_required: str | None = None
_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


# ---------------------------------------------------------------------------
# Schemas
# ---------------------------------------------------------------------------
class ProblemDetails(BaseModel):
    """RFC 9457 (formerly RFC 7807) problem details returned for every error response."""

    type: str = Field(default="about:blank", description="URI reference identifying the problem type.")
    title: str = Field(..., description="Short, human-readable summary of the problem type.")
    status: int = Field(..., description="HTTP status code.")
    detail: str | None = Field(default=None, description="Explanation specific to this occurrence.")
    instance: str | None = Field(default=None, description="Request path that produced the problem.")
    errors: list[dict[str, Any]] | None = Field(default=None, description="Validation errors, when applicable.")

    model_config = ConfigDict(json_schema_extra={"example": {
        "type": "about:blank", "title": "Conflict", "status": 409,
        "detail": "The knowledge base is empty. Index the sample documents or upload a .txt file first.",
        "instance": "/api/ask",
    }})


class AskRequest(BaseModel):
    """An employee question about the indexed documents."""

    question: str = Field(..., min_length=1, max_length=MAX_QUESTION_LENGTH, description="The question to answer.")
    top_k: int = Field(default=4, ge=1, le=8, description="How many chunks to retrieve as context.")

    model_config = ConfigDict(json_schema_extra={"examples": [
        {"question": "How much is the home office stipend and what can I buy with it?", "top_k": 4},
        {"question": "How many PTO days can I carry over into next year?"},
    ]})


class Citation(BaseModel):
    """A retrieved chunk that was given to the model as a numbered source."""

    number: int = Field(..., description="Source number used in the answer, e.g. `[1]`.")
    chunk_id: str = Field(..., description="Stable chunk id: `<document>#<n>`.")
    document: str = Field(..., description="Source file name.")
    title: str = Field(..., description="Document title.")
    section: str = Field(..., description="Section heading the chunk came from.")
    score: float = Field(..., description="Cosine similarity between the question and the chunk.")
    text: str = Field(..., description="Chunk text.")
    cited: bool = Field(..., description="Whether the answer references this source.")


class Usage(BaseModel):
    """Token usage reported by the chat model."""

    input_tokens: int = 0
    output_tokens: int = 0


class AskResponse(BaseModel):
    """A grounded answer with citations to the source chunks."""

    answer: str = Field(..., description="Answer text with inline `[n]` citation markers.")
    citations: list[Citation]
    model: str = Field(..., description="Chat model that produced the answer.")
    usage: Usage
    retrieval_ms: float = Field(..., description="Time spent embedding the question and searching Qdrant.")

    model_config = ConfigDict(json_schema_extra={"example": {
        "answer": "New hybrid and remote employees get a one-time $600 home office stipend for a desk, chair, "
                  "monitor, or other ergonomic equipment, claimed in Concur within 90 days of your start date [1].",
        "citations": [{
            "number": 1, "chunk_id": "hybrid-work-policy#2", "document": "hybrid-work-policy.txt",
            "title": "Contoso Hybrid and Remote Work Policy", "section": "Home office stipend",
            "score": 0.71, "text": "New hybrid and remote employees receive a one-time home office stipend of $600...",
            "cited": True,
        }],
        "model": "gpt-4.1-mini",
        "usage": {"input_tokens": 812, "output_tokens": 74},
        "retrieval_ms": 142.6,
    }})


class DocumentSummary(BaseModel):
    """A document in the vector index."""

    name: str = Field(..., description="File name, e.g. `hybrid-work-policy.txt`.")
    title: str = Field(..., description="Title taken from the document's first `# ` heading.")
    chunks: int = Field(..., description="Number of chunks stored for the document.")


class DocumentsResponse(BaseModel):
    documents: list[DocumentSummary]
    total_chunks: int

    model_config = ConfigDict(json_schema_extra={"example": {
        "documents": [
            {"name": "employee-handbook-time-off.txt", "title": "Contoso Employee Handbook: Time Off and Leave",
             "chunks": 6},
            {"name": "hybrid-work-policy.txt", "title": "Contoso Hybrid and Remote Work Policy", "chunks": 6},
        ],
        "total_chunks": 12,
    }})


class IndexResponse(BaseModel):
    """Result of indexing one or more documents."""

    message: str
    documents: list[DocumentSummary]

    model_config = ConfigDict(json_schema_extra={"example": {
        "message": "Indexed 5 sample document(s) into 28 chunk(s).",
        "documents": [{"name": "hybrid-work-policy.txt", "title": "Contoso Hybrid and Remote Work Policy",
                       "chunks": 6}],
    }})


class MessageResponse(BaseModel):
    message: str


class HealthResponse(BaseModel):
    status: Literal["healthy", "degraded"]
    openai_available: bool
    chat_model: str
    embeddings_model: str


def _problem(description: str) -> dict[str, Any]:
    return {"model": ProblemDetails, "description": description, "content": {"application/problem+json": {}}}


PROTECTED_ERRORS: dict[int | str, dict[str, Any]] = {
    401: _problem("Missing or invalid `X-API-Key` (only enforced when RAG_API_KEY is set)."),
    429: _problem("Per-client rate limit exceeded, or the model provider is throttling."),
}
MODEL_ERRORS: dict[int | str, dict[str, Any]] = {
    502: _problem("The model provider returned an error or could not be reached."),
    503: _problem("No OpenAI connection is configured."),
}


# ---------------------------------------------------------------------------
# Configuration and dependencies
# ---------------------------------------------------------------------------
def _openai_settings() -> tuple[str | None, str | None, str, str]:
    """Endpoint/key/models from the `chat` and `embeddings` model references Aspire injects
    (CHAT_URI, CHAT_KEY, CHAT_MODELNAME, EMBEDDINGS_MODELNAME).

    Falls back to the OpenAI SDK's own OPENAI_BASE_URL / OPENAI_API_KEY when run outside Aspire.
    """
    return (
        os.getenv("CHAT_URI") or os.getenv("OPENAI_BASE_URL") or None,
        os.getenv("CHAT_KEY") or os.getenv("OPENAI_API_KEY"),
        os.getenv("CHAT_MODELNAME") or DEFAULT_CHAT_MODEL,
        os.getenv("EMBEDDINGS_MODELNAME") or DEFAULT_EMBEDDINGS_MODEL,
    )


def _require_openai() -> openai.AsyncOpenAI:
    if _openai_client is None:
        raise HTTPException(status.HTTP_503_SERVICE_UNAVAILABLE,
                            "OpenAI is not configured. Check the AppHost `openai-api-key` parameter.")
    return _openai_client


def _store() -> AsyncQdrantClient:
    assert _qdrant is not None
    return _qdrant


async def _require_api_key(api_key: str | None = Depends(_api_key_header)) -> None:
    if _api_key_required is None:
        return
    if api_key is None or not secrets.compare_digest(api_key, _api_key_required):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Invalid or missing API key")


async def _enforce_rate_limit(request: Request) -> None:
    client_id = request.client.host if request.client else "unknown"
    now = monotonic()
    async with _rate_lock:
        tokens, last = _rate_buckets.get(client_id, (float(RATE_LIMIT_CAPACITY), now))
        tokens = min(float(RATE_LIMIT_CAPACITY), tokens + (now - last) * RATE_LIMIT_REFILL_PER_SEC)
        if tokens < 1:
            _rate_buckets[client_id] = (tokens, now)
            logger.warning("Rate limit exceeded", extra={"client.address": client_id})
            raise HTTPException(status.HTTP_429_TOO_MANY_REQUESTS, "Rate limit exceeded. Please slow down.")
        _rate_buckets[client_id] = (tokens - 1, now)


PROTECTED = [Depends(_enforce_rate_limit), Depends(_require_api_key)]


def _model_error(ex: openai.OpenAIError) -> HTTPException:
    trace.get_current_span().record_exception(ex)
    logger.error("Model call failed: %s", ex, extra={"error.type": type(ex).__name__})
    if isinstance(ex, openai.RateLimitError):
        return HTTPException(status.HTTP_429_TOO_MANY_REQUESTS,
                             "The model provider is throttling requests. Try again shortly.")
    return HTTPException(status.HTTP_502_BAD_GATEWAY, "The model provider returned an error.")


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _openai_client, _qdrant, _api_key_required, _chat_model, _embeddings_model

    endpoint, api_key, _chat_model, _embeddings_model = _openai_settings()
    if api_key:
        _openai_client = openai.AsyncOpenAI(api_key=api_key, base_url=endpoint)
        logger.info("OpenAI client initialized", extra={
            "gen_ai.request.model": _chat_model, "rag.embeddings.model": _embeddings_model,
            "server.address": str(_openai_client.base_url),
        })
    else:
        logger.warning("No OpenAI key configured; indexing and answering are unavailable.")

    _qdrant = create_qdrant_client()
    await ensure_collection(_qdrant)
    await rag.refresh_index_stats(_qdrant)

    _api_key_required = os.getenv("RAG_API_KEY") or None
    if _api_key_required is None:
        logger.warning(
            "RAG_API_KEY not set. Upload, ask, and index endpoints are anonymous; "
            "set RAG_API_KEY before exposing this sample on a public network."
        )
    yield
    await _qdrant.close()
    if _openai_client:
        await _openai_client.close()


# ---------------------------------------------------------------------------
# Application
# ---------------------------------------------------------------------------
app = FastAPI(
    title="Contoso Handbook Q&A API",
    summary="Retrieval-augmented generation over company policy documents with Qdrant, OpenAI, and Aspire.",
    description=(
        "Employees ask questions about the Contoso handbook (time off, hybrid work, travel and expenses, "
        "IT security, onboarding) and get answers grounded in **cited source chunks**.\n\n"
        "**Indexing:** each `.txt` document is split into section-aware chunks (one per `## ` heading), "
        "embedded in a single batched call, and upserted into the Qdrant `documents` collection.\n\n"
        "**Answering:** the question is embedded, the top-k chunks are retrieved by cosine similarity, and "
        "the chat model answers using only those numbered sources, citing them as `[n]`.\n\n"
        "Every step is traced with OpenTelemetry: GenAI semantic-convention spans for embeddings and chat, "
        "a `rag.retrieve` span with the scores of each hit, and Qdrant HTTP calls, so the Aspire dashboard "
        "shows one trace from the browser to the vector store and the model."
    ),
    version="2.0.0",
    lifespan=lifespan,
    openapi_tags=[
        {"name": "Questions", "description": "Ask grounded questions about the indexed documents."},
        {"name": "Documents", "description": "Manage the documents in the vector index."},
        {"name": "Service", "description": "Health and service metadata."},
    ],
)
instrument_app(app)
_default_openapi = app.openapi


def _openapi() -> dict[str, Any]:
    """Validation failures are returned as 400 ProblemDetails, so drop FastAPI's default 422 docs."""
    if app.openapi_schema is None:
        schema = _default_openapi()
        for operations in schema["paths"].values():
            for operation in operations.values():
                operation.get("responses", {}).pop("422", None)
        for name in ("HTTPValidationError", "ValidationError"):
            schema.get("components", {}).get("schemas", {}).pop(name, None)
    return app.openapi_schema


app.openapi = _openapi


@app.exception_handler(HTTPException)
async def _http_problem(request: Request, exc: HTTPException) -> JSONResponse:
    problem = ProblemDetails(
        title=HTTPStatus(exc.status_code).phrase, status=exc.status_code,
        detail=str(exc.detail) if exc.detail else None, instance=request.url.path,
    )
    return JSONResponse(problem.model_dump(exclude_none=True), status_code=exc.status_code,
                        media_type="application/problem+json", headers=exc.headers)


@app.exception_handler(RequestValidationError)
async def _validation_problem(request: Request, exc: RequestValidationError) -> JSONResponse:
    errors = [{"loc": list(e["loc"]), "msg": e["msg"], "type": e["type"]} for e in exc.errors()]
    problem = ProblemDetails(
        title="Bad Request", status=400, detail="One or more validation errors occurred.",
        instance=request.url.path, errors=errors,
    )
    return JSONResponse(problem.model_dump(exclude_none=True), status_code=400, media_type="application/problem+json")


@app.get("/scalar", include_in_schema=False)
def scalar_html():
    return get_scalar_api_reference(openapi_url=app.openapi_url, title=app.title)


@app.get("/health", tags=["Service"], summary="Health check", response_model=HealthResponse)
def health_check() -> HealthResponse:
    """Returns 200 while the service is up; `status` is `degraded` when no OpenAI connection is configured."""
    available = _openai_client is not None
    return HealthResponse(status="healthy" if available else "degraded", openai_available=available,
                          chat_model=_chat_model, embeddings_model=_embeddings_model)


async def _list_documents() -> list[DocumentSummary]:
    points, _ = await _store().scroll(COLLECTION_NAME, limit=10_000, with_payload=["filename", "title"])
    docs: dict[str, DocumentSummary] = {}
    for point in points:
        name = point.payload["filename"]
        doc = docs.setdefault(name, DocumentSummary(name=name, title=point.payload.get("title", name), chunks=0))
        doc.chunks += 1
    return sorted(docs.values(), key=lambda d: d.title)


@app.get("/api/documents", tags=["Documents"], summary="List indexed documents", response_model=DocumentsResponse)
async def list_documents() -> DocumentsResponse:
    """Documents currently in the vector index with their chunk counts.

    Not gated by `X-API-Key` so the UI can render the library without shipping a key to the browser."""
    documents = await _list_documents()
    return DocumentsResponse(documents=documents, total_chunks=sum(d.chunks for d in documents))


def _validate_text_upload(file: UploadFile) -> None:
    if Path(file.filename or "").suffix.lower() != ".txt":
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "Only .txt text uploads are supported.")
    content_type = (file.content_type or "").split(";", maxsplit=1)[0].strip().lower()
    if content_type and not (content_type.startswith("text/") or content_type == "application/octet-stream"):
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "Uploaded file must be text content.")


async def _index(filename: str, text: str, source: str) -> DocumentSummary:
    if len(rag.chunk_document(filename, text)) > MAX_CHUNKS_PER_DOC:
        raise HTTPException(status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                            f"Document produces too many chunks (limit {MAX_CHUNKS_PER_DOC}).")
    client = _require_openai()
    try:
        chunks = await rag.index_document(client, _embeddings_model, _store(), filename, text, source)
    except openai.OpenAIError as ex:
        raise _model_error(ex) from ex
    if not chunks:
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "The document is empty.")
    return DocumentSummary(name=filename, title=chunks[0].title, chunks=len(chunks))


@app.post(
    "/api/documents", tags=["Documents"], summary="Upload and index a document",
    status_code=status.HTTP_201_CREATED, response_model=IndexResponse,
    responses={400: _problem("Not a UTF-8 `.txt` file, or the file is empty."),
               413: _problem("The file is larger than 1 MB or produces more than 200 chunks."),
               **PROTECTED_ERRORS, **MODEL_ERRORS},
    dependencies=PROTECTED,
)
async def upload_document(
    file: Annotated[UploadFile, File(description="A UTF-8 `.txt` document, up to 1 MB.")],
) -> IndexResponse:
    """Chunks the document by `## ` section, embeds the chunks in one batched call, and upserts them into
    Qdrant. Uploading a file with the same name replaces the previous version."""
    _validate_text_upload(file)
    # Stream in fixed-size reads so oversized payloads are rejected before they all sit in memory.
    buffered: list[bytes] = []
    size = 0
    while chunk := await file.read(64 * 1024):
        size += len(chunk)
        if size > MAX_FILE_SIZE:
            raise HTTPException(status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                                f"Uploaded file exceeds {MAX_FILE_SIZE} byte limit.")
        buffered.append(chunk)
    try:
        text = b"".join(buffered).decode("utf-8")
    except UnicodeDecodeError:
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "Uploaded file is not valid UTF-8 text.")

    name = Path(file.filename).name
    document = await _index(name, text, "upload")
    return IndexResponse(message=f"Indexed {name} into {document.chunks} chunk(s).", documents=[document])


@app.post(
    "/api/documents/samples", tags=["Documents"], summary="Index the sample handbook documents",
    response_model=IndexResponse, responses={**PROTECTED_ERRORS, **MODEL_ERRORS}, dependencies=PROTECTED,
)
async def index_samples() -> IndexResponse:
    """Indexes the bundled Contoso policy documents (time off, hybrid work, travel and expenses, IT security,
    onboarding). Safe to run repeatedly: chunk ids are deterministic, so documents are replaced, not duplicated."""
    documents = [await _index(name, text, "sample") for name, text in rag.load_sample_documents()]
    total = sum(d.chunks for d in documents)
    return IndexResponse(message=f"Indexed {len(documents)} sample document(s) into {total} chunk(s).",
                         documents=documents)


@app.delete(
    "/api/documents", tags=["Documents"], summary="Clear the index", response_model=MessageResponse,
    responses=PROTECTED_ERRORS, dependencies=PROTECTED,
)
async def clear_documents() -> MessageResponse:
    """Deletes and recreates the Qdrant collection, removing every document."""
    store = _store()
    removed = await rag.refresh_index_stats(store)
    await store.delete_collection(COLLECTION_NAME)
    await ensure_collection(store)
    await rag.refresh_index_stats(store)
    logger.info("Cleared %d chunk(s) from the index", removed, extra={"rag.chunk.count": removed})
    return MessageResponse(message=f"Removed {removed} chunk(s) from the index.")


@app.post(
    "/api/ask", tags=["Questions"], summary="Ask a question", response_model=AskResponse,
    responses={400: _problem("The request body failed validation."),
               409: _problem("The knowledge base is empty."),
               **PROTECTED_ERRORS, **MODEL_ERRORS},
    dependencies=PROTECTED,
)
async def ask(request: AskRequest) -> AskResponse:
    """Embeds the question, retrieves the `top_k` most similar chunks from Qdrant, and asks the chat model
    to answer using only those numbered sources. The answer cites sources inline as `[n]`; each citation
    includes the chunk id, section, similarity score, and text."""
    question = request.question.strip()
    if not question:
        raise HTTPException(status.HTTP_400_BAD_REQUEST, "Question is required.")
    client = _require_openai()
    try:
        hits, retrieval_s = await rag.retrieve(client, _embeddings_model, _store(), question, request.top_k)
        if not hits:
            raise HTTPException(status.HTTP_409_CONFLICT,
                                "The knowledge base is empty. Index the sample documents or upload a .txt file first.")
        completion = await client.chat.completions.create(
            model=_chat_model,
            messages=rag.build_messages(question, hits),
            max_completion_tokens=MAX_COMPLETION_TOKENS,
        )
    except openai.OpenAIError as ex:
        raise _model_error(ex) from ex

    answer = completion.choices[0].message.content or "Sorry, I couldn't produce an answer. Please rephrase."
    cited = rag.cited_numbers(answer, len(hits))
    usage = Usage(input_tokens=completion.usage.prompt_tokens, output_tokens=completion.usage.completion_tokens) \
        if completion.usage else Usage()
    grounded = bool(cited)
    rag.questions_answered.add(1, {"rag.answer.grounded": grounded})

    span = trace.get_current_span()
    span.set_attributes({"rag.answer.grounded": grounded, "rag.citation.count": len(cited)})
    logger.info("Answered question with %d citation(s)", len(cited), extra={
        "rag.retrieval.top_k": request.top_k,
        "rag.retrieval.top_score": round(hits[0].score, 4),
        "rag.citation.chunk_ids": [hits[n - 1].chunk.id for n in cited],
        "rag.answer.grounded": grounded,
        "gen_ai.request.model": _chat_model,
        "gen_ai.usage.input_tokens": usage.input_tokens,
        "gen_ai.usage.output_tokens": usage.output_tokens,
    })
    return AskResponse(
        answer=answer,
        citations=[
            Citation(number=n, chunk_id=h.chunk.id, document=h.chunk.document, title=h.chunk.title,
                     section=h.chunk.section, score=round(h.score, 4), text=h.chunk.text, cited=n in cited)
            for n, h in enumerate(hits, start=1)
        ],
        model=completion.model or _chat_model,
        usage=usage,
        retrieval_ms=round(retrieval_s * 1000, 1),
    )


# Serve the built Svelte frontend in publish mode. Mounted last so API routes take precedence.
public_dir = Path(__file__).parent / "public"
if public_dir.is_dir():
    app.mount("/", StaticFiles(directory=public_dir, html=True), name="static")
