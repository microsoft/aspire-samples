from datetime import datetime
from enum import Enum
from typing import Any, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

MAX_PAGE_SIZE = 100
EMAIL_PATTERN = r"^[^@\s]+@[^@\s]+\.[^@\s]+$"
COUNTRY_PATTERN = r"^[A-Za-z]{2}$"


class Segment(str, Enum):
    enterprise = "enterprise"
    mid_market = "mid_market"
    smb = "smb"


class CustomerStatus(str, Enum):
    lead = "lead"
    active = "active"
    churned = "churned"


def _strip_required(value: str) -> str:
    value = value.strip()
    if not value:
        raise ValueError("must not be blank")
    return value


class _CustomerFields(BaseModel):
    """Normalization shared by create and update payloads."""

    @field_validator("name", "company", check_fields=False)
    @classmethod
    def _text(cls, value: Optional[str]) -> Optional[str]:
        return None if value is None else _strip_required(value)

    @field_validator("email", check_fields=False)
    @classmethod
    def _email(cls, value: Optional[str]) -> Optional[str]:
        return None if value is None else value.strip().lower()

    @field_validator("country", check_fields=False)
    @classmethod
    def _country(cls, value: Optional[str]) -> Optional[str]:
        return None if value is None else value.upper()


class CustomerCreate(_CustomerFields):
    """A new customer account."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "name": "Jordan Lee",
                    "email": "jordan.lee@fabrikam.com",
                    "company": "Fabrikam, Inc.",
                    "segment": "enterprise",
                    "status": "lead",
                    "country": "US",
                    "lifetime_value": 0,
                }
            ]
        }
    )

    name: str = Field(min_length=1, max_length=100, description="Primary contact's full name.")
    email: str = Field(
        min_length=3, max_length=254, pattern=EMAIL_PATTERN,
        description="Primary contact's email. Unique across customers (case-insensitive).",
    )
    company: str = Field(min_length=1, max_length=120, description="Company or account name.")
    segment: Segment = Field(default=Segment.smb, description="Sales segment.")
    status: CustomerStatus = Field(default=CustomerStatus.lead, description="Lifecycle status.")
    country: str = Field(pattern=COUNTRY_PATTERN, description="ISO 3166-1 alpha-2 country code.")
    lifetime_value: float = Field(default=0, ge=0, le=10_000_000, description="Revenue to date, in USD.")


class CustomerUpdate(_CustomerFields):
    """Partial update: only the supplied fields change."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {"status": "active", "lifetime_value": 48000},
                {"segment": "mid_market", "company": "Fabrikam Holdings"},
            ]
        }
    )

    name: Optional[str] = Field(default=None, min_length=1, max_length=100)
    email: Optional[str] = Field(default=None, min_length=3, max_length=254, pattern=EMAIL_PATTERN)
    company: Optional[str] = Field(default=None, min_length=1, max_length=120)
    segment: Optional[Segment] = None
    status: Optional[CustomerStatus] = Field(
        default=None, description="Move the customer through the lifecycle (recorded as a status change)."
    )
    country: Optional[str] = Field(default=None, pattern=COUNTRY_PATTERN)
    lifetime_value: Optional[float] = Field(default=None, ge=0, le=10_000_000)

    @model_validator(mode="after")
    def _not_empty(self) -> "CustomerUpdate":
        if not self.model_dump(exclude_none=True):
            raise ValueError("Provide at least one field to update.")
        return self


class Customer(BaseModel):
    """A customer account."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "id": 42,
                    "name": "Jordan Lee",
                    "email": "jordan.lee@fabrikam.com",
                    "company": "Fabrikam, Inc.",
                    "segment": "enterprise",
                    "status": "active",
                    "country": "US",
                    "lifetime_value": 182500.0,
                    "created_at": "2025-03-14T09:12:44Z",
                    "updated_at": "2026-11-20T16:05:10Z",
                }
            ]
        }
    )

    id: int = Field(description="Customer id.")
    name: str
    email: str
    company: str
    segment: Segment
    status: CustomerStatus
    country: str
    lifetime_value: float = Field(description="Revenue to date, in USD.")
    created_at: datetime
    updated_at: datetime


class CustomerPage(BaseModel):
    """One page of customers plus the total number of matches."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "items": [Customer.model_config["json_schema_extra"]["examples"][0]],
                    "total": 37,
                    "limit": 1,
                    "offset": 0,
                }
            ]
        }
    )

    items: list[Customer]
    total: int = Field(description="Customers matching the filters, across all pages.")
    limit: int
    offset: int


class CustomerSummary(BaseModel):
    """Headline numbers for the customer base."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "total": 24,
                    "by_status": {"lead": 7, "active": 14, "churned": 3},
                    "by_segment": {"enterprise": 6, "mid_market": 9, "smb": 9},
                    "lifetime_value": 2875400.0,
                }
            ]
        }
    )

    total: int
    by_status: dict[CustomerStatus, int]
    by_segment: dict[Segment, int]
    lifetime_value: float = Field(description="Sum of lifetime value across all customers, in USD.")


class DemoResult(BaseModel):
    """Outcome of a dashboard demo command."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {"seeded": 0, "queries": 10, "created": 3, "status_changes": 4, "rejected": 1, "not_found": 1,
                 "total": 27}
            ]
        }
    )

    seeded: int = 0
    queries: int = 0
    created: int = 0
    status_changes: int = 0
    rejected: int = Field(default=0, description="Duplicate-email creates rejected with 409.")
    not_found: int = Field(default=0, description="Lookups of customers that don't exist (404).")
    total: int = Field(description="Customers in the database afterwards.")


class HealthStatus(BaseModel):
    status: str = Field(examples=["healthy"])
    database: str = Field(examples=["connected"])


class ProblemDetails(BaseModel):
    """RFC 9457 (formerly 7807) problem details."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "type": "about:blank",
                    "title": "Not Found",
                    "status": 404,
                    "detail": "Customer 42 was not found.",
                    "instance": "/customers/42",
                }
            ]
        }
    )

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
                    "instance": "/customers",
                    "errors": {"body.email": ["String should match pattern '^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$'"]},
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
