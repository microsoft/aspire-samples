import logging
import os
from http import HTTPStatus
from typing import Annotated

from telemetry import configure_opentelemetry, instrument_app

# Configure providers before anything creates loggers or the app.
configure_opentelemetry()

import fastapi.staticfiles
import redis
from fastapi import FastAPI, HTTPException, Path, Query, Request, Response, status
from fastapi.exceptions import RequestValidationError
from fastapi.openapi.utils import get_openapi
from fastapi.responses import HTMLResponse, JSONResponse
from scalar_fastapi import Layout, get_scalar_api_reference

from inventory import (
    CACHE_TTL_SECONDS,
    CacheUnavailable,
    InsufficientStock,
    SkuNotFound,
    inventory,
    redis_client,
)
from models import (
    Availability,
    CacheFlushResult,
    CacheStats,
    CacheWarmResult,
    Category,
    HealthStatus,
    Product,
    Reservation,
    ReservationRequest,
    SimulationResult,
    ValidationProblemDetails,
    problem_response,
)

logger = logging.getLogger("contoso.inventory")

# Cache administration and demo endpoints are enabled by the AppHost in run mode only.
DEMO_ENDPOINTS = os.getenv("ENABLE_DEMO_ENDPOINTS", "").strip().lower() in {"1", "true", "yes", "on"}

app = FastAPI(
    title="Contoso Outdoors Inventory API",
    summary="Real-time stock availability for the Contoso Outdoors catalog.",
    description=(
        "Answers \"can I buy this today?\" for every SKU across the Seattle, Denver, and Atlanta distribution "
        "centers. Reads use the **cache-aside** pattern: Redis first, and on a miss the warehouse system is "
        f"queried and the answer cached for {CACHE_TTL_SECONDS} seconds. Reservations change stock and "
        "invalidate the SKU's cache entry. Orchestrated and observed with **Aspire**.\n\n"
        "Errors use [RFC 9457 problem details](https://www.rfc-editor.org/rfc/rfc9457) "
        "(`application/problem+json`). Stock is kept in memory and resets when the API restarts."
    ),
    version="2.0.0",
    openapi_tags=[
        {"name": "Catalog", "description": "Products sold by Contoso Outdoors."},
        {"name": "Availability", "description": "Units on hand per SKU, served cache-aside from Redis."},
        {"name": "Reservations", "description": "Hold stock for an order. Invalidates the SKU's cached availability."},
        {"name": "Cache", "description": "Inspect, warm, and flush the Redis availability cache."},
        {"name": "System", "description": "Service health."},
        {"name": "Demo", "description": "Local development helpers (enabled by the AppHost in run mode)."},
    ],
)
instrument_app(app)

SKU_PATTERN = r"^[A-Z]{3}-[A-Z0-9]{2,4}-[A-Z0-9]{2,4}$"
Sku = Annotated[str, Path(pattern=SKU_PATTERN, description="Stock keeping unit.", examples=["TNT-2P-TRL"])]

UNKNOWN_SKU = {
    404: problem_response(
        "The SKU isn't in the catalog.",
        example={
            "type": "about:blank",
            "title": "Not Found",
            "status": 404,
            "detail": "SKU TNT-6P-XPD is not in the Contoso Outdoors catalog.",
            "instance": "/api/inventory/TNT-6P-XPD",
        },
    )
}
OUT_OF_STOCK = {
    409: problem_response(
        "The distribution center doesn't have enough units.",
        example={
            "type": "about:blank",
            "title": "Conflict",
            "status": 409,
            "detail": "Only 0 units of Basecamp 4P Dome Tent (TNT-4P-BAS) are on hand at SEA; 2 requested. "
            "Restock expected 2026-12-04.",
            "instance": "/api/inventory/TNT-4P-BAS/reservations",
        },
    )
}
CACHE_DOWN = {
    503: problem_response(
        "Redis isn't configured or isn't reachable.",
        example={
            "type": "about:blank",
            "title": "Service Unavailable",
            "status": 503,
            "detail": "Redis is unavailable: Error 10061 connecting to localhost:6379.",
            "instance": "/api/cache/warm",
        },
    )
}
INVALID = {422: problem_response("The request failed validation.", ValidationProblemDetails)}


# --- Problem details ---------------------------------------------------------

def _problem(request: Request, status_code: int, detail: str | None, **extra) -> JSONResponse:
    body = {
        "type": "about:blank",
        "title": HTTPStatus(status_code).phrase,
        "status": status_code,
        "detail": detail,
        "instance": request.url.path,
        **extra,
    }
    return JSONResponse(body, status_code=status_code, media_type="application/problem+json")


@app.exception_handler(SkuNotFound)
async def not_found_handler(request: Request, exc: SkuNotFound):
    return _problem(request, 404, str(exc))


@app.exception_handler(InsufficientStock)
async def conflict_handler(request: Request, exc: InsufficientStock):
    return _problem(request, 409, str(exc))


@app.exception_handler(CacheUnavailable)
async def cache_unavailable_handler(request: Request, exc: CacheUnavailable):
    return _problem(request, 503, str(exc))


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

if not os.path.exists("static"):

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
        title="Contoso Outdoors Inventory API reference",
        layout=Layout.MODERN,
        default_open_all_tags=True,
    )
    # scalar-fastapi emits a bare <html> tag; add a language for accessibility.
    return HTMLResponse(reference.body.decode("utf-8").replace("<html>", '<html lang="en">', 1))


@app.get(
    "/health",
    tags=["System"],
    summary="Health check",
    description="Returns 200 while the API is serving requests. Redis is a cache, not a hard dependency: "
    "when it's down the API still answers from the warehouse system and reports `cache: unavailable`.",
    response_model=HealthStatus,
)
def health_check():
    client = redis_client()
    if client is None:
        return HealthStatus(status="healthy", cache="not_configured")
    try:
        client.ping()
        return HealthStatus(status="healthy", cache="connected")
    except redis.RedisError:
        return HealthStatus(status="degraded", cache="unavailable")


# --- Catalog and availability -------------------------------------------------

@app.get(
    "/api/products",
    tags=["Catalog"],
    summary="List products",
    description="Returns the catalog in display order. Filter by `category`.",
    response_model=list[Product],
    responses=INVALID,
)
def list_products(
    category: Annotated[Category | None, Query(description="Only products in this category.")] = None,
):
    return inventory.products(category)


@app.get(
    "/api/inventory/{sku}",
    tags=["Availability"],
    summary="Check availability",
    description=f"Returns units on hand per distribution center and an overall `status` "
    f"(`in_stock`, `low_stock` at or below the reorder point, or `out_of_stock` with a `restock_eta`).\n\n"
    f"Cache-aside: the answer comes from Redis when cached (`cache.result: hit`, about a millisecond). "
    f"Otherwise the warehouse system is queried (100+ ms) and the result is cached for {CACHE_TTL_SECONDS} s "
    f"(`miss`). The `X-Cache` response header repeats the result.",
    response_model=Availability,
    responses={**UNKNOWN_SKU, **INVALID},
)
def get_availability(sku: Sku, response: Response):
    availability = inventory.availability(sku)
    response.headers["X-Cache"] = availability.cache.result.value.upper()
    return availability


@app.post(
    "/api/inventory/{sku}/reservations",
    tags=["Reservations"],
    summary="Reserve units",
    description="Holds units for an order at one distribution center (the one with the most units unless "
    "`distribution_center` is given) and removes the SKU's cached availability, so the next lookup reads "
    "fresh stock. Returns `409 Conflict` when there aren't enough units on hand.",
    response_model=Reservation,
    status_code=status.HTTP_201_CREATED,
    responses={**UNKNOWN_SKU, **OUT_OF_STOCK, **INVALID},
)
def reserve(sku: Sku, request: ReservationRequest):
    return inventory.reserve(sku, request)


@app.get(
    "/api/cache",
    tags=["Cache"],
    summary="Cache statistics",
    description="How many catalog SKUs are cached right now, the TTL, and this instance's hit/miss counts.",
    response_model=CacheStats,
    responses=CACHE_DOWN,
)
def cache_stats():
    return inventory.stats()


if DEMO_ENDPOINTS:

    @app.post(
        "/api/cache/warm",
        tags=["Cache"],
        summary="Warm the cache",
        description="Loads every SKU from the warehouse system in one query and caches it, so the next "
        f"{CACHE_TTL_SECONDS} seconds of lookups are hits. Invoked by the **Warm cache** dashboard command.",
        response_model=CacheWarmResult,
        responses=CACHE_DOWN,
    )
    def warm_cache():
        return inventory.warm()

    @app.delete(
        "/api/cache",
        tags=["Cache"],
        summary="Flush the cache",
        description="Removes every cached availability entry, so the next lookup for each SKU is a miss. "
        "Invoked by the **Flush cache** dashboard command.",
        response_model=CacheFlushResult,
        responses=CACHE_DOWN,
    )
    def flush_cache():
        return inventory.flush()

    @app.post(
        "/api/demo/simulate",
        tags=["Demo"],
        summary="Simulate shoppers",
        description="Shoppers check availability for popular SKUs (a mix of cache hits and misses), some "
        "reserve units, empty bins are restocked now and then, and the last shopper tries to reserve the sold-out "
        "Basecamp 4P tent (409). Invoked by the **Simulate shoppers** dashboard command.",
        response_model=SimulationResult,
        responses=INVALID,
    )
    def simulate(shoppers: Annotated[int, Query(ge=1, le=200, description="Shoppers to simulate.")] = 40):
        return inventory.simulate(shoppers)


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

# In publish mode the AppHost copies the frontend build into ./static; serve it from the root.
if os.path.exists("static"):
    app.mount("/", fastapi.staticfiles.StaticFiles(directory="static", html=True), name="static")
