from datetime import date, datetime
from enum import Enum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

MAX_RESERVATION_QUANTITY = 20


class Category(str, Enum):
    tents = "Tents"
    packs = "Packs"
    sleep = "Sleep systems"
    footwear = "Footwear"
    apparel = "Apparel"
    camp_kitchen = "Camp kitchen"


class DistributionCenter(str, Enum):
    SEA = "SEA"
    DEN = "DEN"
    ATL = "ATL"


class StockStatus(str, Enum):
    in_stock = "in_stock"
    low_stock = "low_stock"
    out_of_stock = "out_of_stock"


class CacheResult(str, Enum):
    hit = "hit"
    miss = "miss"
    bypass = "bypass"


class Product(BaseModel):
    """A catalog item."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "sku": "TNT-2P-TRL",
                    "name": "Trailhead 2P Backpacking Tent",
                    "category": "Tents",
                    "price_usd": 329.0,
                    "reorder_point": 12,
                }
            ]
        }
    )

    sku: str = Field(description="Stock keeping unit.")
    name: str
    category: Category
    price_usd: float = Field(description="List price in US dollars.")
    reorder_point: int = Field(description="At or below this many units the SKU is reported as `low_stock`.")


class DistributionCenterStock(BaseModel):
    code: DistributionCenter
    name: str = Field(examples=["Seattle, WA"])
    units: int = Field(ge=0, description="Units on hand at this distribution center.")


class CacheInfo(BaseModel):
    """How the availability record was served."""

    result: CacheResult = Field(
        description="`hit`: read from Redis. `miss`: loaded from the warehouse system and cached. "
        "`bypass`: Redis was unavailable, so the warehouse system answered directly."
    )
    age_seconds: float = Field(description="Seconds since the record was read from the warehouse system.")
    ttl_seconds: int = Field(description="Cache entry lifetime.")
    expires_in_seconds: float = Field(description="Seconds until the cache entry expires (0 for `bypass`).")


class Availability(BaseModel):
    """Units available for a SKU across the Contoso Outdoors distribution centers."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "sku": "TNT-2P-TRL",
                    "name": "Trailhead 2P Backpacking Tent",
                    "category": "Tents",
                    "status": "low_stock",
                    "units_available": 9,
                    "reorder_point": 12,
                    "distribution_centers": [
                        {"code": "SEA", "name": "Seattle, WA", "units": 6},
                        {"code": "DEN", "name": "Denver, CO", "units": 3},
                        {"code": "ATL", "name": "Atlanta, GA", "units": 0},
                    ],
                    "restock_eta": None,
                    "as_of": "2026-11-26T18:04:12Z",
                    "cache": {"result": "hit", "age_seconds": 11.6, "ttl_seconds": 30, "expires_in_seconds": 18.4},
                    "lookup_ms": 1.3,
                }
            ]
        }
    )

    sku: str
    name: str
    category: Category
    status: StockStatus
    units_available: int = Field(description="Total units across all distribution centers.")
    reorder_point: int
    distribution_centers: list[DistributionCenterStock]
    restock_eta: date | None = Field(default=None, description="Expected restock date for out-of-stock SKUs.")
    as_of: datetime = Field(description="When the warehouse system reported these numbers (UTC).")
    cache: CacheInfo
    lookup_ms: float = Field(description="Server-side lookup time in milliseconds.")


class ReservationRequest(BaseModel):
    """Hold units for an order while it's being paid for."""

    model_config = ConfigDict(json_schema_extra={"examples": [{"quantity": 2, "distribution_center": "DEN"}]})

    quantity: int = Field(ge=1, le=MAX_RESERVATION_QUANTITY, description="Units to reserve.")
    distribution_center: DistributionCenter | None = Field(
        default=None, description="Fulfil from this distribution center. Defaults to the one with the most units."
    )


class Reservation(BaseModel):
    """A confirmed reservation. The SKU's cached availability is invalidated."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {
                    "reservation_id": "RSV-7F3A21",
                    "sku": "PCK-65L-SUM",
                    "quantity": 2,
                    "distribution_center": "DEN",
                    "units_remaining": 16,
                    "created_at": "2026-11-26T18:05:40Z",
                }
            ]
        }
    )

    reservation_id: str
    sku: str
    quantity: int
    distribution_center: DistributionCenter
    units_remaining: int = Field(description="Units left at the distribution center after this reservation.")
    created_at: datetime


class CacheStats(BaseModel):
    """Cache state and this API instance's hit/miss counts since it started."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {"catalog_skus": 12, "cached_skus": 9, "ttl_seconds": 30, "hits": 412, "misses": 88, "hit_ratio": 0.824}
            ]
        }
    )

    catalog_skus: int
    cached_skus: int
    ttl_seconds: int
    hits: int
    misses: int
    hit_ratio: float = Field(ge=0, le=1)


class CacheWarmResult(BaseModel):
    model_config = ConfigDict(json_schema_extra={"examples": [{"warmed": 12, "ttl_seconds": 30, "duration_ms": 162.4}]})

    warmed: int = Field(description="SKUs loaded into Redis.")
    ttl_seconds: int
    duration_ms: float


class CacheFlushResult(BaseModel):
    model_config = ConfigDict(json_schema_extra={"examples": [{"flushed": 12}]})

    flushed: int = Field(description="Availability entries removed from Redis.")


class SimulationResult(BaseModel):
    """Outcome of a simulated burst of shoppers."""

    model_config = ConfigDict(
        json_schema_extra={
            "examples": [
                {"shoppers": 40, "hits": 31, "misses": 9, "reservations": 7, "rejected": 1, "restocked": 1, "hit_ratio": 0.775}
            ]
        }
    )

    shoppers: int
    hits: int = 0
    misses: int = 0
    reservations: int = 0
    rejected: int = Field(default=0, description="Reservations rejected with 409 because the SKU was out of stock.")
    restocked: int = Field(default=0, description="Distribution centers that received a restock pallet.")
    hit_ratio: float = 0.0


class HealthStatus(BaseModel):
    status: str = Field(examples=["healthy"])
    cache: str = Field(examples=["connected"], description="`connected`, `unavailable`, or `not_configured`.")


class ProblemDetails(BaseModel):
    """RFC 9457 (formerly 7807) problem details."""

    type: str = "about:blank"
    title: str
    status: int
    detail: str | None = None
    instance: str | None = None


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
                    "instance": "/api/inventory/tent-2p",
                    "errors": {"path.sku": ["String should match pattern '^[A-Z]{3}-[A-Z0-9]{2,4}-[A-Z0-9]{2,4}$'"]},
                }
            ]
        }
    )

    errors: dict[str, list[str]] = Field(default_factory=dict)


def problem_response(
    description: str, model: type[BaseModel] = ProblemDetails, example: dict[str, Any] | None = None
) -> dict[str, Any]:
    response: dict[str, Any] = {"model": model, "description": description}
    if example:
        response["content"] = {"application/json": {"example": example}}
    return response
