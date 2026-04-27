import os
from pathlib import Path
from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel
from openai import OpenAI
from qdrant_client.models import PointStruct
import tiktoken
import uuid
from qdrant_setup import initialize_qdrant, COLLECTION_NAME

app = FastAPI()

# Check if public directory exists (for production with built frontend)
public_dir = Path(__file__).parent / "public"
has_static_files = public_dir.exists() and public_dir.is_dir()

# Initialize clients
openai_client = OpenAI(api_key=os.environ.get("OPENAI_APIKEY"))
qdrant_client = initialize_qdrant()

EMBEDDING_MODEL = "text-embedding-3-small"
CHAT_MODEL = "gpt-4.1"


class QuestionRequest(BaseModel):
    question: str


def chunk_text(text: str, chunk_size: int = 500, overlap: int = 50) -> list[str]:
    """Split text into overlapping chunks."""
    encoding = tiktoken.encoding_for_model(EMBEDDING_MODEL)
    tokens = encoding.encode(text)

    chunks = []
    for i in range(0, len(tokens), chunk_size - overlap):
        chunk_tokens = tokens[i:i + chunk_size]
        chunk_text = encoding.decode(chunk_tokens)
        chunks.append(chunk_text)

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


@app.post("/upload")
async def upload_document(file: UploadFile = File(...)):
    """Upload and index a document."""
    try:
        # Read file content
        content = await file.read()
        text = content.decode("utf-8")

        # Chunk the document
        chunks = chunk_text(text)
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

    except Exception as e:
        print(f"❌ Error in upload_document: {e}")
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/ask")
async def ask_question(request: QuestionRequest):
    """Answer a question using RAG."""
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

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/documents")
async def list_documents():
    """List all indexed documents."""
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
