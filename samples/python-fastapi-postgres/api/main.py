import logging
import os
from contextlib import asynccontextmanager
from http import HTTPStatus
from typing import Annotated, Optional

from telemetry import configure_opentelemetry, instrument_app, tracer

# Configure providers before anything creates loggers, connections or the app.
configure_opentelemetry()

import psycopg
from fastapi import FastAPI, HTTPException, Path, Query, Request, Response, status
from fastapi.exceptions import RequestValidationError
from fastapi.openapi.utils import get_openapi
from fastapi.responses import HTMLResponse, JSONResponse
from psycopg_pool import PoolTimeout
from scalar_fastapi import Layout, Theme, get_scalar_api_reference

import customers
import database
from customers import CustomerNotFound, DuplicateEmail
from demo import seed_sample_customers, simulate_traffic
from models import (
    COUNTRY_PATTERN,
    MAX_PAGE_SIZE,
    Customer,
    CustomerCreate,
    CustomerPage,
    CustomerStatus,
    CustomerSummary,
    CustomerUpdate,
    DemoResult,
    HealthStatus,
    Segment,
    ValidationProblemDetails,
    problem_response,
)
from scalar_theme import SCALAR_CUSTOM_CSS, SCALAR_FAVICON_URL, SCALAR_TITLE

logger = logging.getLogger("contoso.customers")

# Demo-only endpoints (seed, simulate) are enabled by the AppHost in run mode.
DEMO_ENDPOINTS = os.getenv("ENABLE_DEMO_ENDPOINTS", "").strip().lower() in {"1", "true", "yes", "on"}


@asynccontextmanager
async def lifespan(app: FastAPI):
    with tracer.start_as_current_span("customers.startup"):
        await database.open_pool()
        await customers.track_existing_customers()
    yield
    await database.close_pool()


app = FastAPI(
    title="Contoso Customers API",
    summary="The customer directory behind Contoso's CRM.",
    description=(
        "Search, page through, create, update, and retire customer accounts stored in **PostgreSQL**. "
        "Served by FastAPI, orchestrated and observed with **Aspire**.\n\n"
        "Errors use [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) "
        "(`application/problem+json`): `404` for unknown customers, `409` when an email is already "
        "registered, `422` for invalid input, and `503` when the database is unreachable."
    ),
    version="2.0.0",
    lifespan=lifespan,
    openapi_tags=[
        {"name": "Customers", "description": "Search, create, update, and delete customer accounts."},
        {"name": "Reports", "description": "Aggregates across the customer base."},
        {"name": "System", "description": "Service health."},
        {"name": "Demo", "description": "Local development helpers (enabled by the AppHost in run mode)."},
    ],
)
instrument_app(app)

NOT_FOUND = {
    404: problem_response(
        "No customer exists with the given id.",
        example={
            "type": "about:blank",
            "title": "Not Found",
            "status": 404,
            "detail": "Customer 42 was not found.",
            "instance": "/customers/42",
        },
    )
}
DUPLICATE_EMAIL = {
    409: problem_response(
        "Another customer already uses this email address.",
        example={
            "type": "about:blank",
            "title": "Conflict",
            "status": 409,
            "detail": "Email j***@fabrikam.com is already in use.",
            "instance": "/customers",
        },
    )
}
INVALID = {422: problem_response("The request failed validation.", ValidationProblemDetails)}
UNAVAILABLE = {
    503: problem_response(
        "PostgreSQL is unreachable or the connection pool is exhausted.",
        example={
            "type": "about:blank",
            "title": "Service Unavailable",
            "status": 503,
            "detail": "The customer database is unavailable. Try again shortly.",
            "instance": "/customers",
        },
    )
}


# --- Problem details ---------------------------------------------------------------

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


@app.exception_handler(CustomerNotFound)
async def not_found_handler(request: Request, exc: CustomerNotFound):
    return _problem(request, 404, str(exc))


@app.exception_handler(DuplicateEmail)
async def conflict_handler(request: Request, exc: DuplicateEmail):
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


@app.exception_handler(psycopg.OperationalError)
@app.exception_handler(PoolTimeout)
async def database_unavailable_handler(request: Request, exc: Exception):
    logger.error("Customer database unavailable: %s", type(exc).__name__, extra={"error.type": type(exc).__name__})
    return _problem(request, 503, "The customer database is unavailable. Try again shortly.")


# --- System ------------------------------------------------------------------------

@app.get("/", include_in_schema=False)
def read_root():
    return {
        "service": app.title,
        "version": app.version,
        "documentation": {"scalar": "/scalar", "openapi": app.openapi_url},
    }


@app.get("/scalar", include_in_schema=False)
def scalar_reference():
    """Serve the branded Scalar API reference."""
    reference = get_scalar_api_reference(
        openapi_url=app.openapi_url,
        title=SCALAR_TITLE,
        theme=Theme.NONE,
        custom_css=SCALAR_CUSTOM_CSS,
        scalar_favicon_url=SCALAR_FAVICON_URL,
        layout=Layout.MODERN,
        default_open_all_tags=True,
        show_developer_tools="never",
    )
    # scalar-fastapi emits a bare <html> tag; add a language for accessibility.
    return HTMLResponse(reference.body.decode("utf-8").replace("<html>", '<html lang="en">', 1))


@app.get(
    "/health",
    tags=["System"],
    summary="Health check",
    description="Returns 200 when the API can reach PostgreSQL. Used by the Aspire health check.",
    response_model=HealthStatus,
    responses=UNAVAILABLE,
)
async def health_check():
    await database.check_health()
    return HealthStatus(status="healthy", database="connected")


# --- Customers ---------------------------------------------------------------------

CustomerId = Annotated[int, Path(ge=1, le=2_147_483_647, description="Customer id.", examples=[42])]


@app.get(
    "/customers",
    tags=["Customers"],
    summary="List customers",
    description="Returns one page of customers ordered by id, plus the total number of matches. Combine "
    "`status`, `segment`, `country`, and `q` (case-insensitive match on name, company, or email) to filter, "
    "and page with `limit`/`offset`.",
    response_model=CustomerPage,
    responses={**INVALID, **UNAVAILABLE},
)
async def list_customers(
    status_filter: Annotated[
        Optional[CustomerStatus], Query(alias="status", description="Only customers in this lifecycle status.")
    ] = None,
    segment: Annotated[Optional[Segment], Query(description="Only customers in this sales segment.")] = None,
    country: Annotated[
        Optional[str], Query(pattern=COUNTRY_PATTERN, description="ISO 3166-1 alpha-2 country code.", examples=["US"])
    ] = None,
    q: Annotated[
        Optional[str],
        Query(min_length=2, max_length=100, description="Search name, company, or email.", examples=["fabrikam"]),
    ] = None,
    limit: Annotated[int, Query(ge=1, le=MAX_PAGE_SIZE, description="Page size.")] = 25,
    offset: Annotated[int, Query(ge=0, le=100_000, description="Customers to skip.")] = 0,
):
    return await customers.list_customers(status_filter, segment, country, q, limit, offset)


@app.get(
    "/customers/summary",
    tags=["Reports"],
    summary="Customer summary",
    description="Customer counts by lifecycle status and sales segment, plus total lifetime value.",
    response_model=CustomerSummary,
    responses=UNAVAILABLE,
)
async def customer_summary():
    return await customers.summarize()


@app.get(
    "/customers/{customer_id}",
    tags=["Customers"],
    summary="Get a customer",
    description="Returns a single customer by id.",
    response_model=Customer,
    responses={**NOT_FOUND, **INVALID, **UNAVAILABLE},
)
async def get_customer(customer_id: CustomerId):
    return await customers.get_customer(customer_id)


@app.post(
    "/customers",
    tags=["Customers"],
    summary="Create a customer",
    description="Adds a customer account. Emails are unique (case-insensitive): registering an email that's "
    "already on file returns `409 Conflict`. The response's `Location` header points at the new customer.",
    response_model=Customer,
    status_code=status.HTTP_201_CREATED,
    responses={**DUPLICATE_EMAIL, **INVALID, **UNAVAILABLE},
)
async def create_customer(customer: CustomerCreate, response: Response):
    created = await customers.create_customer(customer)
    response.headers["Location"] = f"/customers/{created.id}"
    return created


@app.patch(
    "/customers/{customer_id}",
    tags=["Customers"],
    summary="Update a customer",
    description="Partially updates a customer: only the supplied fields change. Changing `status` is recorded "
    "as a `customer.status_changed` span event, and raising `lifetime_value` books the difference as revenue. "
    "Changing `email` to one that's already registered returns `409 Conflict`.",
    response_model=Customer,
    responses={**NOT_FOUND, **DUPLICATE_EMAIL, **INVALID, **UNAVAILABLE},
)
async def update_customer(customer_id: CustomerId, changes: CustomerUpdate):
    return await customers.update_customer(customer_id, changes)


@app.delete(
    "/customers/{customer_id}",
    tags=["Customers"],
    summary="Delete a customer",
    description="Permanently removes a customer.",
    status_code=status.HTTP_204_NO_CONTENT,
    responses={**NOT_FOUND, **INVALID, **UNAVAILABLE},
)
async def delete_customer(customer_id: CustomerId):
    await customers.delete_customer(customer_id)


if DEMO_ENDPOINTS:

    @app.post(
        "/demo/seed",
        tags=["Demo"],
        summary="Seed sample customers",
        description="Replaces every customer with 24 realistic accounts across segments, statuses, and countries. "
        "Invoked by the **Seed sample customers** command in the Aspire dashboard.",
        response_model=DemoResult,
    )
    async def seed():
        return await seed_sample_customers()

    @app.post(
        "/demo/simulate",
        tags=["Demo"],
        summary="Simulate traffic",
        description="Runs rounds of CRM activity (searches, inbound leads, conversions, upsells, churn, a "
        "duplicate sign-up rejected with 409, and a stale 404 lookup) so traces, metrics, and logs populate. "
        "Invoked by the **Simulate traffic** command.",
        response_model=DemoResult,
        responses=INVALID,
    )
    async def simulate(rounds: Annotated[int, Query(ge=1, le=20, description="Rounds of activity.")] = 3):
        return await simulate_traffic(rounds)


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
