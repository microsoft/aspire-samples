"""Contoso Outdoor order-support agent built with FastAPI, OpenAI tool calling, and OpenTelemetry."""

import asyncio
import json
import logging
import os
import secrets
from collections import OrderedDict
from contextlib import asynccontextmanager
from http import HTTPStatus
from pathlib import Path as FilePath
from time import monotonic
from typing import Any, Literal

import openai
from fastapi import Depends, FastAPI, HTTPException, Path, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse
from fastapi.security import APIKeyHeader
from fastapi.staticfiles import StaticFiles
from opentelemetry import trace
from opentelemetry.trace import Status, StatusCode
from pydantic import BaseModel, ConfigDict, Field
from scalar_fastapi import get_scalar_api_reference
from starlette.exceptions import HTTPException as StarletteHTTPException

import support
from telemetry import (
    INSTRUMENTATION_NAME,
    capture_message_content,
    configure_opentelemetry,
    instrument_app,
)

# Tracers/meters obtained earlier (e.g. in support.py) are proxies that bind to these providers.
configure_opentelemetry()

logger = logging.getLogger("support_agent")
tracer = trace.get_tracer(INSTRUMENTATION_NAME)

STATIC_DIR = FilePath(__file__).parent / "static"
AGENT_NAME = "Ember"
DEFAULT_MODEL = "gpt-4o-mini"

# Defense-in-depth caps. These keep the sample from becoming an unbounded
# OpenAI cost-burn or memory-growth vector when the AppHost wires the agent
# with `withExternalHttpEndpoints`.
MAX_COMPLETION_TOKENS = 600
MAX_TOOL_ROUNDS = 3
MAX_MESSAGE_LENGTH = 4000
MAX_SESSIONS = 100
MAX_HISTORY_PER_SESSION = 20
RATE_LIMIT_CAPACITY = 30
RATE_LIMIT_REFILL_PER_SEC = 0.5  # ~30 requests per minute per client


class Session:
    def __init__(self) -> None:
        self.history: list[dict[str, Any]] = []
        self.turns = 0
        # Serializes turns within one conversation; other sessions aren't blocked during model calls.
        self.lock = asyncio.Lock()


_openai_client: openai.AsyncOpenAI | None = None
_model = DEFAULT_MODEL
# OrderedDict so we can FIFO-evict the oldest session once the cap is hit.
_sessions: "OrderedDict[str, Session]" = OrderedDict()
_sessions_lock = asyncio.Lock()
_rate_buckets: dict[str, tuple[float, float]] = {}
_rate_lock = asyncio.Lock()

# Optional shared-secret auth. When AGENT_API_KEY is set, chat and session
# endpoints require callers to send the same value via the X-API-Key header.
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
        "type": "about:blank", "title": "Too Many Requests", "status": 429,
        "detail": "Rate limit exceeded. Please slow down.", "instance": "/chat",
    }})


class ChatRequest(BaseModel):
    """A customer message for the support agent."""

    message: str = Field(..., min_length=1, max_length=MAX_MESSAGE_LENGTH, description="The customer's message.")
    session_id: str = Field(
        default="default", min_length=1, max_length=128, pattern=r"^[A-Za-z0-9._:-]+$",
        description="Conversation identifier. Reuse it to keep context across turns.",
    )

    model_config = ConfigDict(json_schema_extra={"examples": [
        {"message": "Can I still return the jacket from order 1002?", "session_id": "session-42"},
        {"message": "Where is my order 1001?", "session_id": "session-42"},
    ]})


class ToolCall(BaseModel):
    """A tool (function) the agent executed while answering."""

    name: str = Field(..., description="Tool name, e.g. `get_order_status` or `lookup_return_policy`.")
    arguments: str = Field(..., description="JSON-encoded arguments supplied by the model.")


class Usage(BaseModel):
    """Token usage summed across all model calls in the turn."""

    input_tokens: int = 0
    output_tokens: int = 0


class ChatResponse(BaseModel):
    """The agent's answer plus the tools it used."""

    response: str = Field(..., description="The assistant's reply.")
    session_id: str
    model: str = Field(..., description="Model that produced the reply.")
    tool_calls: list[ToolCall] = Field(default_factory=list)
    usage: Usage = Field(default_factory=Usage)

    model_config = ConfigDict(json_schema_extra={"example": {
        "response": "Yes! Your Summit Shell rain jacket (order 1002) was delivered 12 days ago, and apparel "
                    "has a 30-day return window. Send it back unworn with the tags attached using the free "
                    "prepaid label from your order page.",
        "session_id": "session-42", "model": "gpt-4o-mini",
        "tool_calls": [
            {"name": "get_order_status", "arguments": "{\"order_id\": \"1002\"}"},
            {"name": "lookup_return_policy", "arguments": "{\"category\": \"apparel\"}"},
        ],
        "usage": {"input_tokens": 1184, "output_tokens": 96},
    }})


class HealthResponse(BaseModel):
    status: Literal["healthy", "degraded"]
    openai_available: bool
    model: str


class SessionsResponse(BaseModel):
    active_sessions: list[str]
    total_sessions: int


class MessageResponse(BaseModel):
    message: str


class OrderSummary(BaseModel):
    order_id: str
    status: str
    item: str
    category: str


class ApiInfo(BaseModel):
    message: str
    endpoints: list[str]
    version: str


def _problem(description: str) -> dict[str, Any]:
    return {"model": ProblemDetails, "description": description, "content": {"application/problem+json": {}}}


PROTECTED_ERRORS: dict[int | str, dict[str, Any]] = {
    401: _problem("Missing or invalid `X-API-Key` (only enforced when AGENT_API_KEY is set)."),
}
CHAT_ERRORS: dict[int | str, dict[str, Any]] = {
    400: _problem("The request body failed validation."),
    **PROTECTED_ERRORS,
    429: _problem("Per-client rate limit exceeded, or the model provider is throttling."),
    502: _problem("The model provider returned an error or could not be reached."),
    503: _problem("No OpenAI connection is configured."),
}


# ---------------------------------------------------------------------------
# Configuration and dependencies
# ---------------------------------------------------------------------------
def _openai_settings() -> tuple[str | None, str | None, str]:
    """Endpoint/key/model from the `chat` model reference (CHAT_URI, CHAT_KEY, CHAT_MODELNAME).

    Falls back to the OpenAI SDK's own OPENAI_BASE_URL / OPENAI_API_KEY when run outside Aspire.
    """
    return (
        os.getenv("CHAT_URI") or None,
        os.getenv("CHAT_KEY") or os.getenv("OPENAI_API_KEY"),
        os.getenv("CHAT_MODELNAME") or DEFAULT_MODEL,
    )


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


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _openai_client, _api_key_required, _model

    endpoint, api_key, _model = _openai_settings()
    if api_key:
        _openai_client = openai.AsyncOpenAI(api_key=api_key, base_url=endpoint)
        logger.info("OpenAI client initialized",
                    extra={"gen_ai.request.model": _model, "server.address": _openai_client.base_url.host})
    else:
        logger.warning("No OpenAI key configured; chat is unavailable.")

    _api_key_required = os.getenv("AGENT_API_KEY") or None
    if _api_key_required is None:
        logger.warning(
            "AGENT_API_KEY not set. The /chat and /sessions endpoints are anonymous; "
            "set AGENT_API_KEY before exposing this sample on a public network."
        )
    yield
    if _openai_client:
        await _openai_client.close()


# ---------------------------------------------------------------------------
# Application
# ---------------------------------------------------------------------------
app = FastAPI(
    title="Contoso Outdoor Support Agent API",
    summary="Order-support AI agent built with FastAPI, OpenAI tool calling, and Aspire.",
    description=(
        "Ember answers customer questions about **order status** and **returns**. Each `/chat` turn "
        "runs an agent loop: the model may call the `get_order_status` and `lookup_return_policy` "
        "tools (backed by in-memory demo data) before replying.\n\n"
        "Every model call, tool execution, and HTTP request is traced with OpenTelemetry using the "
        "GenAI semantic conventions, so the Aspire dashboard's GenAI visualizer can show prompts, "
        "tool definitions, tool calls, and token usage.\n\n"
        "Demo orders: `1001` (shipped), `1002` (delivered), `1003` (processing), `1004` (cancelled)."
    ),
    version="2.0.0",
    lifespan=lifespan,
    openapi_tags=[
        {"name": "Chat", "description": "Talk to the support agent."},
        {"name": "Sessions", "description": "Inspect and reset in-memory conversations."},
        {"name": "Orders", "description": "Read-only demo data the agent's tools use."},
        {"name": "Service", "description": "Health and service metadata."},
    ],
)
instrument_app(app)
_default_openapi = app.openapi


def _openapi() -> dict[str, Any]:
    """Validation failures are returned as 400 ProblemDetails, so drop FastAPI's default 422 docs,
    and document error bodies only as `application/problem+json`."""
    if app.openapi_schema is None:
        schema = _default_openapi()
        for operations in schema["paths"].values():
            for operation in operations.values():
                responses = operation.get("responses", {})
                responses.pop("422", None)
                for response in responses.values():
                    content = response.get("content", {})
                    if "application/problem+json" in content and "application/json" in content:
                        content["application/problem+json"] = content.pop("application/json")
        for name in ("HTTPValidationError", "ValidationError"):
            schema.get("components", {}).get("schemas", {}).pop(name, None)
    return app.openapi_schema


app.openapi = _openapi
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")


# Starlette's base class also covers framework-raised errors (unknown routes, 405s, missing static files),
# not only the HTTPException subclass that FastAPI handlers raise.
@app.exception_handler(StarletteHTTPException)
async def _http_problem(request: Request, exc: StarletteHTTPException) -> JSONResponse:
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


@app.get("/", include_in_schema=False)
def read_root() -> FileResponse:
    return FileResponse(STATIC_DIR / "index.html")


@app.get("/scalar", include_in_schema=False)
def scalar_html():
    return get_scalar_api_reference(openapi_url=app.openapi_url, title=app.title, show_developer_tools="never")


@app.get("/api", tags=["Service"], summary="Service information", response_model=ApiInfo)
def api_info() -> ApiInfo:
    """Lists the main endpoints. The full contract is at `/openapi.json` and `/scalar`."""
    return ApiInfo(message=app.title, endpoints=["/chat", "/sessions", "/orders", "/health", "/scalar"],
                   version=app.version)


@app.get("/health", tags=["Service"], summary="Health check", response_model=HealthResponse)
def health_check() -> HealthResponse:
    """Returns 200 while the service is up; `status` is `degraded` when no OpenAI connection is configured."""
    available = _openai_client is not None
    return HealthResponse(status="healthy" if available else "degraded", openai_available=available, model=_model)


@app.get("/orders", tags=["Orders"], summary="List demo orders", response_model=list[OrderSummary])
def list_orders() -> list[OrderSummary]:
    """The in-memory orders that `get_order_status` can find. Ask the agent about one of them."""
    return [OrderSummary(order_id=oid, status=o["status"], item=o["item"], category=o["category"])
            for oid, o in support.ORDERS.items()]


def _end_conversation(session: Session, reason: str) -> None:
    support.active_sessions.add(-1)
    support.turns_per_conversation.record(session.turns, {"support.conversation.end_reason": reason})


async def _get_or_create_session(session_id: str) -> tuple[Session, bool]:
    async with _sessions_lock:
        session = _sessions.get(session_id)
        if session is not None:
            _sessions.move_to_end(session_id)
            return session, False
        if len(_sessions) >= MAX_SESSIONS:
            # FIFO eviction so unique session ids can't grow memory without bound.
            _, evicted = _sessions.popitem(last=False)
            _end_conversation(evicted, "evicted")
        session = Session()
        _sessions[session_id] = session
        support.conversations_started.add(1)
        support.active_sessions.add(1)
        return session, True


def _fail_turn(span: trace.Span, history: list[dict[str, Any]], ex: Exception) -> None:
    span.record_exception(ex)
    span.set_status(Status(StatusCode.ERROR, type(ex).__name__))
    span.set_attribute("error.type", type(ex).__name__)
    logger.error("Model call failed: %s", ex, extra={"error.type": type(ex).__name__})
    if history and history[-1]["role"] == "user":
        history.pop()


@app.post(
    "/chat",
    tags=["Chat"],
    summary="Send a message to the support agent",
    response_model=ChatResponse,
    responses=CHAT_ERRORS,
    dependencies=[Depends(_enforce_rate_limit), Depends(_require_api_key)],
)
async def chat(request: ChatRequest) -> ChatResponse:
    """Runs one agent turn: the model can call the order-status and return-policy tools (up to
    3 rounds) before answering. Conversation history is kept in memory per `session_id`."""
    if _openai_client is None:
        raise HTTPException(status.HTTP_503_SERVICE_UNAVAILABLE,
                            "OpenAI is not configured. Check the AppHost `openai-api-key` parameter.")

    session, is_new = await _get_or_create_session(request.session_id)
    if is_new:
        logger.info("Conversation started", extra={"gen_ai.conversation.id": request.session_id})

    # Concurrent requests for the same session_id run one complete turn at a time (history update,
    # model and tool calls, then commit or rollback), so they can't interleave or roll back each other.
    async with session.lock:
        return await _run_turn(session, request)


async def _run_turn(session: Session, request: ChatRequest) -> ChatResponse:
    session.turns += 1
    history = session.history
    history.append({"role": "user", "content": request.message})
    if len(history) > MAX_HISTORY_PER_SESSION:
        # Drop an even count so user/assistant pairs stay aligned.
        drop = len(history) - MAX_HISTORY_PER_SESSION
        del history[: drop + (drop % 2)]

    # GenAI semconv `invoke_agent` span groups the model calls and tool executions of one turn.
    with tracer.start_as_current_span(f"invoke_agent {AGENT_NAME}") as span:
        span.set_attributes({
            "gen_ai.operation.name": "invoke_agent",
            "gen_ai.agent.name": AGENT_NAME,
            "gen_ai.agent.description": "Contoso Outdoor order-support assistant",
            "gen_ai.provider.name": "openai",
            "gen_ai.request.model": _model,
            "gen_ai.conversation.id": request.session_id,
        })
        if capture_message_content():
            span.set_attributes({
                "gen_ai.system_instructions": json.dumps([{"type": "text", "content": support.SYSTEM_PROMPT}]),
                "gen_ai.input.messages": json.dumps(
                    [{"role": "user", "parts": [{"type": "text", "content": request.message}]}]),
                "gen_ai.tool.definitions": json.dumps(support.TOOLS),
            })
        messages: list[Any] = [{"role": "system", "content": support.SYSTEM_PROMPT}, *history]
        tool_calls: list[ToolCall] = []
        usage = Usage()
        try:
            for round_number in range(MAX_TOOL_ROUNDS + 1):
                completion = await _openai_client.chat.completions.create(
                    model=_model,
                    messages=messages,
                    tools=support.TOOLS,
                    # After the last allowed tool round, the model must answer with what it has.
                    tool_choice="none" if round_number == MAX_TOOL_ROUNDS else "auto",
                    max_completion_tokens=MAX_COMPLETION_TOKENS,
                )
                if completion.usage:
                    usage.input_tokens += completion.usage.prompt_tokens
                    usage.output_tokens += completion.usage.completion_tokens
                reply = completion.choices[0].message
                if not reply.tool_calls:
                    break
                messages.append({
                    "role": "assistant",
                    "content": reply.content,
                    "tool_calls": [
                        {"id": c.id, "type": "function",
                         "function": {"name": c.function.name, "arguments": c.function.arguments}}
                        for c in reply.tool_calls
                    ],
                })
                for call in reply.tool_calls:
                    tool_calls.append(ToolCall(name=call.function.name, arguments=call.function.arguments))
                    result = support.execute_tool(call.function.name, call.id, call.function.arguments)
                    messages.append({"role": "tool", "tool_call_id": call.id, "content": result})
            answer = reply.content or "Sorry, I couldn't complete that request. Please try rephrasing."
        except openai.RateLimitError as ex:
            _fail_turn(span, history, ex)
            raise HTTPException(status.HTTP_429_TOO_MANY_REQUESTS,
                                "The model provider is throttling requests. Try again shortly.") from ex
        except openai.OpenAIError as ex:
            _fail_turn(span, history, ex)
            raise HTTPException(status.HTTP_502_BAD_GATEWAY, "The model provider returned an error.") from ex

        span.set_attributes({
            "gen_ai.usage.input_tokens": usage.input_tokens,
            "gen_ai.usage.output_tokens": usage.output_tokens,
        })
        if capture_message_content():
            span.set_attribute("gen_ai.output.messages", json.dumps([{
                "role": "assistant",
                "parts": [{"type": "text", "content": answer}],
                "finish_reason": "stop",
            }]))

    history.append({"role": "assistant", "content": answer})
    logger.info(
        "Agent turn completed with %d tool call(s)", len(tool_calls),
        extra={
            "gen_ai.conversation.id": request.session_id,
            "support.conversation.turn": session.turns,
            "support.tool_call.count": len(tool_calls),
            "gen_ai.usage.input_tokens": usage.input_tokens,
            "gen_ai.usage.output_tokens": usage.output_tokens,
        },
    )
    return ChatResponse(response=answer, session_id=request.session_id, model=_model,
                        tool_calls=tool_calls, usage=usage)


@app.get("/sessions", tags=["Sessions"], summary="List active conversations", response_model=SessionsResponse,
         responses=PROTECTED_ERRORS, dependencies=[Depends(_require_api_key)])
def list_sessions() -> SessionsResponse:
    """Conversation ids currently held in memory (most recently used last)."""
    return SessionsResponse(active_sessions=list(_sessions), total_sessions=len(_sessions))


@app.delete("/sessions", tags=["Sessions"], summary="End all conversations", response_model=MessageResponse,
            responses=PROTECTED_ERRORS, dependencies=[Depends(_require_api_key)])
async def clear_sessions() -> MessageResponse:
    """Ends every conversation, recording `support_agent.conversation.turns` for each one."""
    async with _sessions_lock:
        count = len(_sessions)
        for session in _sessions.values():
            _end_conversation(session, "cleared")
        _sessions.clear()
    logger.info("Cleared %d conversation(s)", count, extra={"support.sessions.cleared": count})
    return MessageResponse(message=f"Cleared {count} conversation(s)")


@app.delete("/sessions/{session_id}", tags=["Sessions"], summary="End one conversation",
            response_model=MessageResponse,
            responses={**PROTECTED_ERRORS, 404: _problem("No conversation with that id.")},
            dependencies=[Depends(_require_api_key)])
async def clear_session(session_id: str = Path(..., examples=["session-42"])) -> MessageResponse:
    """Ends a single conversation and discards its history."""
    async with _sessions_lock:
        session = _sessions.pop(session_id, None)
        if session is None:
            raise HTTPException(status.HTTP_404_NOT_FOUND, "Session not found")
        _end_conversation(session, "cleared")
    return MessageResponse(message=f"Session {session_id} cleared")
