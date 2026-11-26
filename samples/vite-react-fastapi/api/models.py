from datetime import date, datetime
from enum import Enum
from typing import Any, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator

MAX_TASKS = 100
MAX_TITLE_LENGTH = 200


class Priority(str, Enum):
    low = "low"
    medium = "medium"
    high = "high"
    urgent = "urgent"


class Status(str, Enum):
    todo = "todo"
    in_progress = "in_progress"
    blocked = "blocked"
    done = "done"


def _strip_required(value: str) -> str:
    value = value.strip()
    if not value:
        raise ValueError("must not be blank")
    return value


class TaskCreate(BaseModel):
    """A new task for the team board."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "title": "Rotate TLS certificates on the edge gateway",
                    "assignee": "Priya Patel",
                    "priority": "urgent",
                    "status": "todo",
                    "due_date": "2026-11-25",
                }
            ]
        }
    )

    title: str = Field(min_length=1, max_length=MAX_TITLE_LENGTH, description="Short, actionable summary.")
    assignee: Optional[str] = Field(default=None, min_length=1, max_length=60, description="Owner's display name.")
    priority: Priority = Field(default=Priority.medium, description="Triage priority.")
    status: Status = Field(default=Status.todo, description="Initial board column.")
    due_date: Optional[date] = Field(default=None, description="Target completion date (ISO 8601).")

    @field_validator("title")
    @classmethod
    def _title(cls, value: str) -> str:
        return _strip_required(value)


class TaskUpdate(BaseModel):
    """Partial update: only the supplied fields change."""

    model_config = ConfigDict(
        json_schema_extra={"examples": [{"status": "in_progress"}, {"priority": "high", "assignee": "Marcus Chen"}]}
    )

    title: Optional[str] = Field(default=None, min_length=1, max_length=MAX_TITLE_LENGTH)
    assignee: Optional[str] = Field(default=None, min_length=1, max_length=60)
    priority: Optional[Priority] = None
    status: Optional[Status] = Field(
        default=None, description="Move the task to another column. `blocked` -> `done` is rejected with 409."
    )
    due_date: Optional[date] = None

    @field_validator("title")
    @classmethod
    def _title(cls, value: Optional[str]) -> Optional[str]:
        return None if value is None else _strip_required(value)


class Task(BaseModel):
    """A task on the board."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "id": 7,
                    "title": "Rotate TLS certificates on the edge gateway",
                    "assignee": "Priya Patel",
                    "priority": "urgent",
                    "status": "in_progress",
                    "due_date": "2026-11-25",
                    "overdue": False,
                    "created_at": "2026-11-20T09:12:44Z",
                    "updated_at": "2026-11-23T08:41:02Z",
                    "completed_at": None,
                }
            ]
        }
    )

    id: int = Field(description="Board-unique task id.")
    title: str
    assignee: Optional[str] = None
    priority: Priority
    status: Status
    due_date: Optional[date] = None
    overdue: bool = Field(description="True when the due date has passed and the task is not done.")
    created_at: datetime
    updated_at: datetime
    completed_at: Optional[datetime] = None


class BoardSummary(BaseModel):
    """Headline numbers for the board."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "total": 12,
                    "by_status": {"todo": 4, "in_progress": 4, "blocked": 1, "done": 3},
                    "overdue": 2,
                    "urgent_open": 1,
                }
            ]
        }
    )

    total: int
    by_status: dict[Status, int]
    overdue: int = Field(description="Open tasks past their due date.")
    urgent_open: int = Field(description="Open tasks with `urgent` priority.")


class DemoResult(BaseModel):
    """Outcome of a dashboard demo command."""

    model_config = ConfigDict(
        json_schema_extra={"examples": [{"created": 3, "moved": 5, "completed": 2, "rejected": 1, "deleted": 0, "total": 14}]}
    )

    created: int = 0
    moved: int = 0
    completed: int = 0
    rejected: int = 0
    deleted: int = 0
    total: int


class HealthStatus(BaseModel):
    status: str = Field(examples=["healthy"])


class ProblemDetails(BaseModel):
    """RFC 9457 (formerly 7807) problem details."""

    type: str = "about:blank"
    title: str
    status: int
    detail: Optional[str] = None
    instance: Optional[str] = None


class ValidationProblemDetails(ProblemDetails):
    """Problem details with per-field validation errors."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "type": "about:blank",
                    "title": "Unprocessable Content",
                    "status": 422,
                    "detail": "One or more validation errors occurred.",
                    "instance": "/tasks",
                    "errors": {"body.title": ["String should have at least 1 character"]},
                }
            ]
        }
    )

    errors: dict[str, list[str]] = Field(default_factory=dict)


def problem_response(
    description: str, model: type[BaseModel] = ProblemDetails, example: Optional[dict[str, Any]] = None
) -> dict[str, Any]:
    response: dict[str, Any] = {"model": model, "description": description}
    if example:
        response["content"] = {"application/json": {"example": example}}
    return response
