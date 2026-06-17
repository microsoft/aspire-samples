"""AI Agent using FastAPI and OpenAI."""

import asyncio
import logging
import os
import secrets
from collections import OrderedDict
from contextlib import asynccontextmanager
from time import monotonic

import openai
from fastapi import Depends, FastAPI, HTTPException, Request, status
from fastapi.responses import FileResponse
from fastapi.security import APIKeyHeader
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

# Configure logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

# Global state
_openai_client: openai.OpenAI | None = None
# OrderedDict so we can FIFO-evict the oldest session entry once the cap is hit.
_session_histories: "OrderedDict[str, list[dict[str, str]]]" = OrderedDict()
_sessions_lock = asyncio.Lock()
_rate_buckets: dict[str, tuple[float, float]] = {}
_rate_lock = asyncio.Lock()

# Constants
DEFAULT_MODEL = "gpt-3.5-turbo"
DEFAULT_TEMPERATURE = 0.7
DEFAULT_MAX_TOKENS = 1000

# Defense-in-depth caps. These keep the sample from becoming an unbounded
# OpenAI cost-burn or memory-growth vector when the AppHost wires the agent
# with `withExternalHttpEndpoints`.
MAX_TOKENS_HARD_CAP = 1000
MAX_MESSAGE_LENGTH = 4000
MAX_SESSIONS = 100
MAX_HISTORY_PER_SESSION = 20
RATE_LIMIT_CAPACITY = 30
RATE_LIMIT_REFILL_PER_SEC = 0.5  # ~30 requests per minute per client

# Model allow-list. Extend with care; arbitrary model values let callers
# steer the request to the most expensive options.
ALLOWED_MODELS: frozenset[str] = frozenset({
    "gpt-3.5-turbo",
    "gpt-4o-mini",
    "gpt-4o",
})

# Optional shared-secret auth. When AGENT_API_KEY is set, state-changing and
# session-disclosing endpoints require callers to send the same value via the
# X-API-Key header. When it is not set the sample stays anonymous (the other
# guards still apply) and a loud warning is logged on startup so it's obvious
# in deployment logs.
_api_key_required: str | None = None
_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


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


# Request/Response models
class ChatRequest(BaseModel):
    """Request model for chat endpoint."""

    message: str = Field(..., min_length=1, max_length=MAX_MESSAGE_LENGTH, description="The user's message")
    session_id: str = Field(default="default", min_length=1, max_length=128, description="Session identifier")
    model: str = Field(default=DEFAULT_MODEL, description="OpenAI model to use")
    temperature: float = Field(default=DEFAULT_TEMPERATURE, ge=0.0, le=2.0)
    max_tokens: int = Field(default=DEFAULT_MAX_TOKENS, ge=1, le=MAX_TOKENS_HARD_CAP, description="Maximum response tokens")


class ChatResponse(BaseModel):
    """Response model for chat endpoint."""

    response: str = Field(..., description="The AI assistant's response")
    session_id: str = Field(..., description="Session identifier")


async def initialize_openai() -> None:
    """Initialize OpenAI client from environment."""
    global _openai_client, _api_key_required

    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        logger.warning("OPENAI_API_KEY not set. AI features will be unavailable.")
        _openai_client = None
    else:
        try:
            _openai_client = openai.OpenAI(api_key=api_key)
            logger.info("✓ OpenAI client initialized")
        except Exception as e:
            logger.error(f"✗ Failed to initialize OpenAI: {e}")
            _openai_client = None

    _api_key_required = os.getenv("AGENT_API_KEY") or None
    if _api_key_required is None:
        logger.warning(
            "AGENT_API_KEY not set. The /chat and /sessions endpoints are "
            "anonymous; set AGENT_API_KEY before exposing this sample on a "
            "public network."
        )
    else:
        logger.info(
            "AGENT_API_KEY configured; /chat and /sessions require the "
            "X-API-Key header."
        )


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
    health_status = "healthy" if _openai_client else "degraded"
    return {
        "status": health_status,
        "openai_available": _openai_client is not None,
    }


async def _get_or_create_history(session_id: str) -> list[dict[str, str]]:
    """Return the conversation history for a session, evicting oldest if full."""
    async with _sessions_lock:
        history = _session_histories.get(session_id)
        if history is None:
            if len(_session_histories) >= MAX_SESSIONS:
                # FIFO eviction so unique session ids can't unbound memory.
                _session_histories.popitem(last=False)
            history = []
            _session_histories[session_id] = history
        else:
            # Touch the entry so it becomes "most recently used".
            _session_histories.move_to_end(session_id)
        return history


@app.post(
    "/chat",
    response_model=ChatResponse,
    dependencies=[Depends(_enforce_rate_limit), Depends(_require_api_key)],
)
async def chat(request: ChatRequest) -> ChatResponse:
    """Handle chat requests with OpenAI."""
    if not _openai_client:
        raise HTTPException(
            status_code=503,
            detail="OpenAI client not available. Check API key configuration.",
        )

    if request.model not in ALLOWED_MODELS:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Model not allowed. Allowed models: {sorted(ALLOWED_MODELS)}",
        )

    # Get session history and append user message
    history = await _get_or_create_history(request.session_id)
    history.append({"role": "user", "content": request.message})

    # Bound per-session history so a single session can't grow without limit.
    if len(history) > MAX_HISTORY_PER_SESSION:
        drop = len(history) - MAX_HISTORY_PER_SESSION
        # Keep an even count so user/assistant role pairs stay aligned.
        if drop % 2:
            drop += 1
        del history[:drop]

    try:
        # Call OpenAI API
        response = _openai_client.chat.completions.create(
            model=request.model,
            messages=history,
            temperature=request.temperature,
            max_tokens=min(request.max_tokens, MAX_TOKENS_HARD_CAP),
        )

        assistant_message = response.choices[0].message.content
        history.append({"role": "assistant", "content": assistant_message or ""})

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


@app.get("/sessions", dependencies=[Depends(_require_api_key)])
def list_sessions():
    """List all active sessions."""
    return {
        "active_sessions": list(_session_histories.keys()),
        "total_sessions": len(_session_histories),
    }


@app.delete("/sessions/{session_id}", dependencies=[Depends(_require_api_key)])
def clear_session(session_id: str):
    """Clear conversation history for a session."""
    if session_id in _session_histories:
        del _session_histories[session_id]
        return {"message": f"Session {session_id} cleared"}

    raise HTTPException(status_code=404, detail="Session not found")
