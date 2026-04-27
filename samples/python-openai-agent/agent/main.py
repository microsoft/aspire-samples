"""AI Agent using FastAPI and OpenAI."""

import logging
import os
from collections import defaultdict
from contextlib import asynccontextmanager

import openai
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

# Configure logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

# Global state
_openai_client: openai.OpenAI | None = None
_session_histories: dict[str, list[dict[str, str]]] = defaultdict(list)

# Constants
DEFAULT_MODEL = "gpt-3.5-turbo"
DEFAULT_TEMPERATURE = 0.7
MAX_TOKENS = 1000


# Request/Response models
class ChatRequest(BaseModel):
    """Request model for chat endpoint."""

    message: str = Field(..., description="The user's message")
    session_id: str = Field(default="default", description="Session identifier")
    model: str = Field(default=DEFAULT_MODEL, description="OpenAI model to use")
    temperature: float = Field(default=DEFAULT_TEMPERATURE, ge=0.0, le=2.0)
    max_tokens: int = Field(default=MAX_TOKENS, description="Maximum response tokens")


class ChatResponse(BaseModel):
    """Response model for chat endpoint."""

    response: str = Field(..., description="The AI assistant's response")
    session_id: str = Field(..., description="Session identifier")


async def initialize_openai() -> None:
    """Initialize OpenAI client from environment."""
    global _openai_client

    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        logger.warning("OPENAI_API_KEY not set. AI features will be unavailable.")
        _openai_client = None
        return

    try:
        _openai_client = openai.OpenAI(api_key=api_key)
        logger.info("✓ OpenAI client initialized")
    except Exception as e:
        logger.error(f"✗ Failed to initialize OpenAI: {e}")
        _openai_client = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Manage application lifecycle."""
    logger.info("Starting AI Agent...")
    await initialize_openai()
    yield
    logger.info("Shutting down AI Agent...")


# Create FastAPI application
app = FastAPI(
    title="AI Chat Agent",
    description="An AI agent powered by OpenAI and FastAPI",
    version="1.0.0",
    lifespan=lifespan,
)

# Mount static files
app.mount("/static", StaticFiles(directory="static"), name="static")


@app.get("/")
def read_root():
    """Serve the chat UI."""
    return FileResponse("static/index.html")


@app.get("/api")
def api_info():
    """API information endpoint."""
    return {
        "message": "AI Chat Agent API",
        "endpoints": ["/chat", "/health", "/sessions"],
        "version": "1.0.0",
    }


@app.get("/health")
def health_check():
    """Health check endpoint."""
    status = "healthy" if _openai_client else "degraded"
    return {
        "status": status,
        "openai_available": _openai_client is not None,
    }


@app.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest) -> ChatResponse:
    """Handle chat requests with OpenAI."""
    if not _openai_client:
        raise HTTPException(
            status_code=503,
            detail="OpenAI client not available. Check API key configuration.",
        )

    # Get session history and append user message
    history = _session_histories[request.session_id]
    history.append({"role": "user", "content": request.message})

    try:
        # Call OpenAI API
        response = _openai_client.chat.completions.create(
            model=request.model,
            messages=history,
            temperature=request.temperature,
            max_tokens=request.max_tokens,
        )

        assistant_message = response.choices[0].message.content
        history.append({"role": "assistant", "content": assistant_message})

        return ChatResponse(
            response=assistant_message or "",
            session_id=request.session_id,
        )

    except Exception as e:
        logger.error(f"Error in chat: {e}")
        # Remove user message on error
        if history and history[-1]["role"] == "user":
            history.pop()

        raise HTTPException(status_code=500, detail=f"Error: {str(e)}")


@app.get("/sessions")
def list_sessions():
    """List all active sessions."""
    return {
        "active_sessions": list(_session_histories.keys()),
        "total_sessions": len(_session_histories),
    }


@app.delete("/sessions/{session_id}")
def clear_session(session_id: str):
    """Clear conversation history for a session."""
    if session_id in _session_histories:
        del _session_histories[session_id]
        return {"message": f"Session {session_id} cleared"}

    raise HTTPException(status_code=404, detail="Session not found")
