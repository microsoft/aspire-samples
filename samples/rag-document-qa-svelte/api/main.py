import asyncio
import logging
import os
import secrets
import uuid
from pathlib import Path
from time import monotonic

import tiktoken
from fastapi import Depends, FastAPI, File, HTTPException, Request, UploadFile, status
from fastapi.security import APIKeyHeader
from fastapi.staticfiles import StaticFiles
from openai import OpenAI
from pydantic import BaseModel
from qdrant_client.models import PointStruct
from qdrant_setup import initialize_qdrant, COLLECTION_NAME

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

app = FastAPI()

# Check if public directory exists (for production with built frontend)
public_dir = Path(__file__).parent / "public"
has_static_files = public_dir.exists() and public_dir.is_dir()

# Initialize clients
openai_client = OpenAI(api_key=os.environ.get("OPENAI_APIKEY"))
qdrant_client = initialize_qdrant()

EMBEDDING_MODEL = "text-embedding-3-small"
CHAT_MODEL = "gpt-4.1"

# Defense-in-depth caps so the /upload and /ask endpoints can't be turned into
# an unbounded OpenAI cost-burn or memory-growth vector when the AppHost wires
# the api with `withExternalHttpEndpoints`.
MAX_FILE_SIZE = 1_000_000      # 1 MB hard ceiling on uploaded payload size
MAX_CHUNKS_PER_DOC = 200
MAX_QUESTION_LENGTH = 2000
RATE_LIMIT_CAPACITY = 20
RATE_LIMIT_REFILL_PER_SEC = 0.33  # ~20 requests per minute per client

# Optional shared-secret auth. When RAG_API_KEY is set the protected endpoints
# require callers to send the same value via the X-API-Key header. When it is
# not set the sample stays anonymous (the other guards still apply) and a loud
# warning is logged so deployments without auth are obvious.
_api_key_required: str | None = os.getenv("RAG_API_KEY") or None
_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)

_rate_buckets: dict[str, tuple[float, float]] = {}
_rate_lock = asyncio.Lock()

if _api_key_required is None:
    logger.warning(
        "RAG_API_KEY not set. /upload, /ask, and /documents are anonymous; "
        "set RAG_API_KEY before exposing this sample on a public network."
    )


async def _require_api_key(api_key: str | None = Depends(_api_key_header)) -> None:
    if _api_key_required is None:
        return
    if api_key is None or not secrets.compare_digest(api_key, _api_key_required):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid or missing API key",
        )


async def _enforce_rate_limit(request: Request) -> None:
    client_id = request.client.host if request.client else "unknown"
    now = monotonic()
    async with _rate_lock:
        tokens, last = _rate_buckets.get(client_id, (float(RATE_LIMIT_CAPACITY), now))
        tokens = min(
            float(RATE_LIMIT_CAPACITY),
            tokens + (now - last) * RATE_LIMIT_REFILL_PER_SEC,
        )
        if tokens < 1:
            _rate_buckets[client_id] = (tokens, now)
            raise HTTPException(
                status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                detail="Rate limit exceeded. Please slow down.",
            )
        _rate_buckets[client_id] = (tokens - 1, now)


def _validate_text_upload(file: UploadFile) -> None:
    filename = file.filename or ""
    if Path(filename).suffix.lower() != ".txt":
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Only .txt text uploads are supported.",
        )

    content_type = (file.content_type or "").split(";", maxsplit=1)[0].strip().lower()
    if content_type and not (
        content_type.startswith("text/")
        or content_type == "application/octet-stream"
    ):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Uploaded file must be text content.",
        )


class QuestionRequest(BaseModel):
    question: str


def chunk_text(text: str, chunk_size: int = 500, overlap: int = 50) -> list[str]:
    """Split text into overlapping chunks."""
    encoding = tiktoken.encoding_for_model(EMBEDDING_MODEL)
    tokens = encoding.encode(text)

    chunks = []
    for i in range(0, len(tokens), chunk_size - overlap):
        chunk_tokens = tokens[i:i + chunk_size]
        chunk_str = encoding.decode(chunk_tokens)
        chunks.append(chunk_str)

    return chunks


def get_embedding(text: str) -> list[float]:
    """Get OpenAI embedding for text."""
    response = openai_client.embeddings.create(
        model=EMBEDDING_MODEL,
        input=text
    )
    return response.data[0].embedding


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.post(
    "/upload",
    dependencies=[Depends(_enforce_rate_limit), Depends(_require_api_key)],
)
async def upload_document(file: UploadFile = File(...)):
    """Upload and index a document."""
    try:
        _validate_text_upload(file)

        # Stream the upload in fixed-size chunks so we can reject oversized
        # payloads before they all sit in memory.
        buffered: list[bytes] = []
        size = 0
        while True:
            chunk = await file.read(64 * 1024)
            if not chunk:
                break
            size += len(chunk)
            if size > MAX_FILE_SIZE:
                raise HTTPException(
                    status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                    detail=f"Uploaded file exceeds {MAX_FILE_SIZE} byte limit.",
                )
            buffered.append(chunk)
        content = b"".join(buffered)

        try:
            text = content.decode("utf-8")
        except UnicodeDecodeError:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="Uploaded file is not valid UTF-8 text.",
            )

        # Chunk the document
        chunks = chunk_text(text)
        if len(chunks) > MAX_CHUNKS_PER_DOC:
            raise HTTPException(
                status_code=status.HTTP_413_REQUEST_ENTITY_TOO_LARGE,
                detail=f"Document produces too many chunks (limit {MAX_CHUNKS_PER_DOC}).",
            )
        print(f"📄 Processing {len(chunks)} chunks from {file.filename}")

        # Create embeddings and store in Qdrant
        points = []
        for idx, chunk in enumerate(chunks):
            embedding = get_embedding(chunk)
            point_id = str(uuid.uuid4())

            points.append(PointStruct(
                id=point_id,
                vector=embedding,
                payload={
                    "text": chunk,
                    "filename": file.filename,
                    "chunk_index": idx
                }
            ))

        qdrant_client.upsert(
            collection_name=COLLECTION_NAME,
            points=points
        )

        return {
            "message": f"Uploaded and indexed {file.filename}",
            "chunks": len(chunks)
        }

    except HTTPException:
        raise
    except Exception as e:
        print(f"❌ Error in upload_document: {e}")
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=str(e))


@app.post(
    "/ask",
    dependencies=[Depends(_enforce_rate_limit), Depends(_require_api_key)],
)
async def ask_question(request: QuestionRequest):
    """Answer a question using RAG."""
    if not request.question or not request.question.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Question is required.",
        )
    if len(request.question) > MAX_QUESTION_LENGTH:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Question exceeds {MAX_QUESTION_LENGTH} character limit.",
        )
    try:
        # Get embedding for question
        question_embedding = get_embedding(request.question)

        # Search for relevant chunks
        search_results = qdrant_client.search(
            collection_name=COLLECTION_NAME,
            query_vector=question_embedding,
            limit=3
        )

        if not search_results:
            return {
                "answer": "I don't have any documents to answer this question. Please upload some documents first.",
                "sources": []
            }

        # Build context from search results
        context_chunks = []
        sources = []

        for result in search_results:
            context_chunks.append(result.payload["text"])
            sources.append({
                "filename": result.payload["filename"],
                "chunk_index": result.payload["chunk_index"],
                "score": result.score,
                "text": result.payload["text"][:200] + "..."  # Preview
            })

        context = "\n\n".join(context_chunks)

        # Generate answer using GPT
        messages = [
            {
                "role": "system",
                "content": "You are a helpful assistant that answers questions based on the provided context. "
                          "If the context doesn't contain enough information to answer the question, say so."
            },
            {
                "role": "user",
                "content": f"Context:\n{context}\n\nQuestion: {request.question}"
            }
        ]

        completion = openai_client.chat.completions.create(
            model=CHAT_MODEL,
            messages=messages
        )

        answer = completion.choices[0].message.content

        return {
            "answer": answer,
            "sources": sources
        }

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/documents")
async def list_documents():
    """List all indexed documents.

    Intentionally not gated by ``_require_api_key`` so the Svelte frontend
    can render the document list on load without exposing the API key to
    client-side JavaScript. Mutating endpoints (``/upload``, ``/ask``)
    remain gated when ``RAG_API_KEY`` is configured.
    """
    try:
        # Scroll through collection to get unique filenames
        scroll_result = qdrant_client.scroll(
            collection_name=COLLECTION_NAME,
            limit=1000
        )

        filenames = set()
        for point in scroll_result[0]:
            filenames.add(point.payload["filename"])

        return {"documents": sorted(list(filenames))}

    except Exception as e:
        print(f"❌ Error in list_documents: {e}")
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=str(e))


# Mount static files and serve frontend (for production)
# This must be last so API routes take precedence
if has_static_files:
    app.mount("/", StaticFiles(directory=public_dir, html=True), name="static")
