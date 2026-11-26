import logging
import os
from http import HTTPStatus
from typing import Annotated, Optional

from telemetry import configure_opentelemetry, instrument_app

# Configure providers before anything creates loggers or the app.
configure_opentelemetry()

from fastapi import FastAPI, HTTPException, Path, Query, Request, status
from fastapi.exceptions import RequestValidationError
from fastapi.openapi.utils import get_openapi
from fastapi.responses import HTMLResponse, JSONResponse
from scalar_fastapi import Layout, get_scalar_api_reference

from board import BoardConflict, TaskNotFound, board, seed_sample_tasks, simulate_traffic
from models import (
    MAX_TASKS,
    BoardSummary,
    DemoResult,
    HealthStatus,
    Priority,
    Status,
    Task,
    TaskCreate,
    TaskUpdate,
    ValidationProblemDetails,
    problem_response,
)

logger = logging.getLogger("contoso.taskboard")

# Demo-only endpoints (seed, simulate) are enabled by the AppHost in run mode.
DEMO_ENDPOINTS = os.getenv("ENABLE_DEMO_ENDPOINTS", "").strip().lower() in {"1", "true", "yes", "on"}

app = FastAPI(
    title="Contoso Task Board API",
    summary="A lightweight task board for a platform engineering team.",
    description=(
        "Create, triage, assign, and move tasks across the **To do → In progress → Blocked → Done** "
        "columns. Served by FastAPI behind YARP at `/api`, orchestrated and observed with **Aspire**.\n\n"
        "Errors use [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) "
        "(`application/problem+json`). Data is kept in memory and resets when the API restarts."
    ),
    version="2.0.0",
    openapi_tags=[
        {"name": "Tasks", "description": "Create, update, move, and delete tasks."},
        {"name": "Board", "description": "Board-wide views."},
        {"name": "System", "description": "Service health."},
        {"name": "Demo", "description": "Local development helpers (enabled by the AppHost in run mode)."},
    ],
)
instrument_app(app)

NOT_FOUND = {
    404: problem_response(
        "No task exists with the given id.",
        example={
            "type": "about:blank",
            "title": "Not Found",
            "status": 404,
            "detail": "Task 42 was not found.",
            "instance": "/tasks/42",
        },
    )
}
BOARD_FULL = {
    409: problem_response(
        "The board is full.",
        example={
            "type": "about:blank",
            "title": "Conflict",
            "status": 409,
            "detail": f"The board already holds the maximum of {MAX_TASKS} tasks.",
            "instance": "/tasks",
        },
    )
}
BLOCKED_TO_DONE = {
    409: problem_response(
        "The status change isn't allowed from the task's current column.",
        example={
            "type": "about:blank",
            "title": "Conflict",
            "status": 409,
            "detail": "Task 5 is blocked; move it to in_progress before marking it done.",
            "instance": "/tasks/5",
        },
    )
}
INVALID = {422: problem_response("The request failed validation.", ValidationProblemDetails)}


# --- Problem details ---------------------------------------------------------

def _problem(request: Request, status_code: int, detail: Optional[str], **extra) -> JSONResponse:
    body = {
        "type": "about:blank",
        "title": HTTPStatus(status_code).phrase,
        "status": status_code,
        "detail": detail,
        "instance": request.url.path,
        **extra,
    }
    return JSONResponse(body, status_code=status_code, media_type="application/problem+json")


@app.exception_handler(TaskNotFound)
async def not_found_handler(request: Request, exc: TaskNotFound):
    return _problem(request, 404, str(exc))


@app.exception_handler(BoardConflict)
async def conflict_handler(request: Request, exc: BoardConflict):
    return _problem(request, 409, str(exc))


@app.exception_handler(HTTPException)
async def http_exception_handler(request: Request, exc: HTTPException):
    return _problem(request, exc.status_code, str(exc.detail))


@app.exception_handler(RequestValidationError)
async def validation_handler(request: Request, exc: RequestValidationError):
    errors: dict[str, list[str]] = {}
    for error in exc.errors():
        errors.setdefault(".".join(str(p) for p in error["loc"]), []).append(error["msg"])
    return _problem(request, 422, "One or more validation errors occurred.", errors=errors)


# --- System ------------------------------------------------------------------

@app.get("/", include_in_schema=False)
def read_root():
    return {
        "service": app.title,
        "version": app.version,
        "documentation": {"scalar": "/scalar", "openapi": app.openapi_url},
    }


@app.get("/scalar", include_in_schema=False)
def scalar_reference():
    reference = get_scalar_api_reference(
        openapi_url=app.openapi_url,
        title="Contoso Task Board API reference",
        layout=Layout.MODERN,
        default_open_all_tags=True,
    )
    # scalar-fastapi emits a bare <html> tag; add a language for accessibility.
    return HTMLResponse(reference.body.decode("utf-8").replace("<html>", '<html lang="en">', 1))


@app.get(
    "/health",
    tags=["System"],
    summary="Health check",
    description="Returns 200 while the API is serving requests. Used by the Aspire health check.",
    response_model=HealthStatus,
)
def health_check():
    return HealthStatus(status="healthy")


# --- Tasks -------------------------------------------------------------------

TaskId = Annotated[int, Path(ge=1, description="Task id.", examples=[7])]


@app.get(
    "/tasks",
    tags=["Tasks"],
    summary="List tasks",
    description="Returns tasks in creation order. Filter by `status`, `priority`, and/or `assignee` "
    "(case-insensitive exact match) and page with `skip`/`limit`.",
    response_model=list[Task],
    responses=INVALID,
)
def list_tasks(
    status_filter: Annotated[Optional[Status], Query(alias="status", description="Only tasks in this column.")] = None,
    priority: Annotated[Optional[Priority], Query(description="Only tasks with this priority.")] = None,
    assignee: Annotated[Optional[str], Query(max_length=60, description="Only tasks owned by this person.",
                                             examples=["Priya Patel"])] = None,
    skip: Annotated[int, Query(ge=0, description="Tasks to skip.")] = 0,
    limit: Annotated[int, Query(ge=1, le=MAX_TASKS, description="Page size.")] = MAX_TASKS,
):
    return board.list(status_filter, priority, assignee, skip, limit)


@app.get(
    "/tasks/{task_id}",
    tags=["Tasks"],
    summary="Get a task",
    description="Returns a single task by id.",
    response_model=Task,
    responses={**NOT_FOUND, **INVALID},
)
def get_task(task_id: TaskId):
    return board.get(task_id)


@app.post(
    "/tasks",
    tags=["Tasks"],
    summary="Create a task",
    description=f"Adds a task to the board. The board holds at most {MAX_TASKS} tasks; "
    "creating one more returns `409 Conflict`.",
    response_model=Task,
    status_code=status.HTTP_201_CREATED,
    responses={**BOARD_FULL, **INVALID},
)
def create_task(task: TaskCreate):
    return board.create(task)


@app.patch(
    "/tasks/{task_id}",
    tags=["Tasks"],
    summary="Update or move a task",
    description="Partially updates a task: only the supplied fields change. Changing `status` moves the task "
    "to another column and is recorded as a `taskboard.transition` span. Blocked tasks can't jump straight "
    "to `done` (`409 Conflict`).",
    response_model=Task,
    responses={**NOT_FOUND, **BLOCKED_TO_DONE, **INVALID},
)
def update_task(task_id: TaskId, changes: TaskUpdate):
    return board.update(task_id, changes)


@app.delete(
    "/tasks/{task_id}",
    tags=["Tasks"],
    summary="Delete a task",
    description="Permanently removes a task from the board.",
    status_code=status.HTTP_204_NO_CONTENT,
    responses={**NOT_FOUND, **INVALID},
)
def delete_task(task_id: TaskId):
    board.delete(task_id)


@app.get(
    "/board/summary",
    tags=["Board"],
    summary="Board summary",
    description="Task counts per column plus the number of overdue and urgent open tasks.",
    response_model=BoardSummary,
)
def board_summary():
    return board.summary()


if DEMO_ENDPOINTS:

    @app.post(
        "/demo/seed",
        tags=["Demo"],
        summary="Seed sample data",
        description="Replaces the board with a dozen realistic platform-team tasks. "
        "Invoked by the **Seed sample data** command in the Aspire dashboard.",
        response_model=DemoResult,
    )
    def seed():
        return seed_sample_tasks()

    @app.post(
        "/demo/simulate",
        tags=["Demo"],
        summary="Simulate traffic",
        description="Runs rounds of team activity (new tasks, pick-ups, blocks, completions, and a rejected "
        "blocked → done move) so traces, metrics, and logs populate. Invoked by the **Simulate traffic** command.",
        response_model=DemoResult,
    )
    def simulate(rounds: Annotated[int, Query(ge=1, le=20, description="Rounds of activity.")] = 6):
        return simulate_traffic(rounds)


def custom_openapi():
    """Advertise error responses as application/problem+json."""
    if not app.openapi_schema:
        schema = get_openapi(
            title=app.title,
            version=app.version,
            summary=app.summary,
            description=app.description,
            routes=app.routes,
            tags=app.openapi_tags,
        )
        for path in schema["paths"].values():
            for operation in path.values():
                for code, response in operation.get("responses", {}).items():
                    content = response.get("content", {})
                    if code.startswith(("4", "5")) and "application/json" in content:
                        content["application/problem+json"] = content.pop("application/json")
        app.openapi_schema = schema
    return app.openapi_schema


app.openapi = custom_openapi
