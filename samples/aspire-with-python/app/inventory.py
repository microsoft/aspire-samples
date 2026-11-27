"""Contoso Outdoors inventory: the catalog, a simulated warehouse system, and the Redis cache-aside read path.

Availability reads check Redis first. On a miss the (deliberately slow) warehouse system is
queried and the answer is cached for CACHE_TTL_SECONDS. Reservations change stock, so they
invalidate the SKU's cache entry. Stock lives in process memory and resets when the API restarts.
"""

import json
import logging
import os
import random
import secrets
import threading
import time
from datetime import UTC, date, datetime, timedelta

import redis
from opentelemetry.instrumentation.utils import suppress_instrumentation
from opentelemetry.metrics import CallbackOptions, Observation
from opentelemetry.trace import Span, Status, StatusCode

from models import (
    Availability,
    CacheFlushResult,
    CacheInfo,
    CacheResult,
    CacheStats,
    CacheWarmResult,
    Category,
    DistributionCenter,
    DistributionCenterStock,
    Product,
    Reservation,
    ReservationRequest,
    SimulationResult,
    StockStatus,
)
from telemetry import inventory_metrics, tracer

logger = logging.getLogger("contoso.inventory")

CACHE_KEY_PREFIX = "inventory:availability:"
CACHE_TTL_SECONDS = int(os.getenv("INVENTORY_CACHE_TTL_SECONDS", "30"))
RESTOCK_PALLET_UNITS = 24

DISTRIBUTION_CENTERS = {
    DistributionCenter.SEA: "Seattle, WA",
    DistributionCenter.DEN: "Denver, CO",
    DistributionCenter.ATL: "Atlanta, GA",
}

# (product, units at SEA/DEN/ATL, restock ETA in days for sold-out items, shopper popularity weight)
_CATALOG = [
    (Product(sku="TNT-2P-TRL", name="Trailhead 2P Backpacking Tent", category=Category.tents, price_usd=329, reorder_point=12), (6, 3, 0), None, 8),
    (Product(sku="TNT-4P-BAS", name="Basecamp 4P Dome Tent", category=Category.tents, price_usd=449, reorder_point=8), (0, 0, 0), 8, 3),
    (Product(sku="PCK-65L-SUM", name="Summit 65L Trekking Pack", category=Category.packs, price_usd=279, reorder_point=15), (24, 18, 11), None, 7),
    (Product(sku="PCK-28L-RDG", name="Ridgeline 28L Daypack", category=Category.packs, price_usd=119, reorder_point=20), (46, 31, 38), None, 9),
    (Product(sku="SLP-15F-ALP", name="Alpine 15°F Down Sleeping Bag", category=Category.sleep, price_usd=389, reorder_point=10), (14, 9, 4), None, 4),
    (Product(sku="SLP-R5-INS", name="Insulated Sleeping Pad R5", category=Category.sleep, price_usd=169, reorder_point=12), (3, 5, 2), None, 5),
    (Product(sku="FTW-HKB-TAL", name="Talus Waterproof Hiking Boot", category=Category.footwear, price_usd=189, reorder_point=18), (22, 27, 19), None, 8),
    (Product(sku="FTW-TRN-SWB", name="Switchback Trail Runner", category=Category.footwear, price_usd=139, reorder_point=18), (35, 12, 29), None, 7),
    (Product(sku="APP-SHL-STM", name="Stormline Rain Shell", category=Category.apparel, price_usd=229, reorder_point=15), (0, 7, 4), None, 6),
    (Product(sku="APP-FLC-CSC", name="Cascade Grid Fleece", category=Category.apparel, price_usd=99, reorder_point=20), (58, 44, 61), None, 6),
    (Product(sku="CKN-STV-TI", name="Titanium Canister Stove", category=Category.camp_kitchen, price_usd=79, reorder_point=10), (17, 21, 13), None, 3),
    (Product(sku="CKN-FLT-4L", name="Gravity Water Filter 4L", category=Category.camp_kitchen, price_usd=109, reorder_point=10), (9, 0, 6), None, 3),
]

PRODUCTS: dict[str, Product] = {product.sku: product for product, *_ in _CATALOG}
POPULARITY: dict[str, int] = {product.sku: weight for product, _, _, weight in _CATALOG}
BACKORDERED_SKU = "TNT-4P-BAS"


class SkuNotFound(Exception):
    def __init__(self, sku: str):
        super().__init__(f"SKU {sku} is not in the Contoso Outdoors catalog.")


class InsufficientStock(Exception):
    """The reservation is valid but the distribution center doesn't have enough units."""


class CacheUnavailable(Exception):
    """Redis is not configured or not reachable."""


def cache_key(sku: str) -> str:
    return f"{CACHE_KEY_PREFIX}{sku}"


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(microsecond=0)


_redis: redis.Redis | None = None
_redis_lock = threading.Lock()


def redis_client() -> redis.Redis | None:
    """Return the shared Redis client, or None when the AppHost didn't reference a cache."""
    global _redis
    if _redis is None and (uri := os.getenv("CACHE_URI")):
        with _redis_lock:
            if _redis is None:
                _redis = redis.Redis.from_url(
                    uri, decode_responses=True, socket_connect_timeout=2, socket_timeout=2, health_check_interval=30
                )
    return _redis


class InventoryService:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        today = date.today()
        self._stock: dict[str, dict[str, int]] = {
            product.sku: dict(zip((dc.value for dc in DISTRIBUTION_CENTERS), units))
            for product, units, _, _ in _CATALOG
        }
        self._restock_eta: dict[str, date | None] = {
            product.sku: today + timedelta(days=eta) if eta else None for product, _, eta, _ in _CATALOG
        }
        self._hits = 0
        self._misses = 0

        meter = inventory_metrics.meter
        meter.create_observable_gauge(
            "inventory.cache.entries",
            callbacks=[self._observe_cache_entries],
            unit="{entry}",
            description="SKUs whose availability is currently cached in Redis.",
        )
        meter.create_observable_gauge(
            "inventory.units_on_hand",
            callbacks=[self._observe_units_on_hand],
            unit="{unit}",
            description="Units on hand across the catalog, by distribution center.",
        )

    # --- Catalog -----------------------------------------------------------------

    def products(self, category: Category | None = None) -> list[Product]:
        return [p for p in PRODUCTS.values() if category is None or p.category == category]

    def product(self, sku: str) -> Product:
        product = PRODUCTS.get(sku)
        if product is None:
            logger.warning("Lookup for unknown SKU %s", sku, extra={"inventory.sku": sku})
            raise SkuNotFound(sku)
        return product

    # --- Cache-aside read path ----------------------------------------------------

    def availability(self, sku: str) -> Availability:
        product = self.product(sku)
        started = time.perf_counter()
        with tracer.start_as_current_span("inventory.lookup") as span:
            span.set_attributes({"inventory.sku": sku, "inventory.category": product.category.value})
            client = redis_client()
            record = None
            result = CacheResult.bypass
            if client is not None:
                try:
                    payload = client.get(cache_key(sku))
                    result = CacheResult.hit if payload else CacheResult.miss
                    record = json.loads(payload) if payload else None
                except redis.RedisError as exc:
                    client = None
                    span.record_exception(exc)
                    logger.warning("Redis is unavailable; serving %s straight from the warehouse system", sku,
                                   extra={"inventory.sku": sku, "error.type": type(exc).__name__})

            if record is None:
                span.add_event(f"cache.{result.value}", {"inventory.cache.key": cache_key(sku)})
                record = self._query_warehouse([sku])[sku]
                if client is not None:
                    try:
                        client.set(cache_key(sku), json.dumps(record), ex=CACHE_TTL_SECONDS)
                        span.add_event("cache.populated", {"inventory.cache.ttl_seconds": CACHE_TTL_SECONDS})
                    except redis.RedisError as exc:
                        span.record_exception(exc)

            elapsed_ms = (time.perf_counter() - started) * 1000
            availability = self._to_availability(product, record, result, elapsed_ms)
            if result is CacheResult.hit:
                span.add_event("cache.hit", {"inventory.cache.age_seconds": availability.cache.age_seconds})
            span.set_attributes({
                "inventory.cache.result": result.value,
                "inventory.units_available": availability.units_available,
                "inventory.stock_status": availability.status.value,
            })
            # Recorded inside the span so histogram exemplars link back to this trace.
            attributes = {"inventory.cache.result": result.value}
            inventory_metrics.lookups.add(1, attributes)
            inventory_metrics.lookup_duration.record(elapsed_ms, attributes)
            with self._lock:
                if result is CacheResult.hit:
                    self._hits += 1
                else:
                    self._misses += 1

            log_extra = {
                "inventory.sku": sku,
                "inventory.cache.result": result.value,
                "inventory.units_available": availability.units_available,
                "inventory.lookup_ms": round(elapsed_ms, 1),
            }
            if result is CacheResult.hit:
                logger.debug("Served %s from cache in %.1f ms", sku, elapsed_ms, extra=log_extra)
            else:
                logger.info(
                    "Cache %s for %s: loaded %d units from %d distribution centers in %.0f ms%s",
                    result.value, sku, availability.units_available, len(DISTRIBUTION_CENTERS), elapsed_ms,
                    f" and cached them for {CACHE_TTL_SECONDS} s" if result is CacheResult.miss else "",
                    extra=log_extra,
                )
                if availability.status is StockStatus.out_of_stock:
                    span.add_event("inventory.out_of_stock", {"inventory.restock_eta": str(availability.restock_eta)})
                    logger.warning("%s (%s) is out of stock at every distribution center; restock expected %s",
                                   product.name, sku, availability.restock_eta, extra=log_extra)
                elif availability.status is StockStatus.low_stock:
                    span.add_event("inventory.low_stock", {"inventory.reorder_point": product.reorder_point})
                    logger.warning("Low stock: %s (%s) has %d units left (reorder point %d)",
                                   product.name, sku, availability.units_available, product.reorder_point,
                                   extra={**log_extra, "inventory.reorder_point": product.reorder_point})
            return availability

    def _query_warehouse(self, skus: list[str]) -> dict[str, dict]:
        """Read stock from the warehouse management system (simulated: a slow, authoritative source)."""
        with tracer.start_as_current_span("inventory.warehouse.query") as span:
            span.set_attributes({
                "inventory.sku_count": len(skus),
                "inventory.distribution_center.count": len(DISTRIBUTION_CENTERS),
            })
            time.sleep(random.uniform(0.09, 0.16) + 0.004 * len(skus))
            as_of = _utcnow().isoformat()
            with self._lock:
                return {
                    sku: {
                        "units": dict(self._stock[sku]),
                        "restock_eta": eta.isoformat() if (eta := self._restock_eta[sku]) else None,
                        "as_of": as_of,
                    }
                    for sku in skus
                }

    def _to_availability(self, product: Product, record: dict, result: CacheResult, elapsed_ms: float) -> Availability:
        units = record["units"]
        total = sum(units.values())
        if total == 0:
            status = StockStatus.out_of_stock
        elif total <= product.reorder_point:
            status = StockStatus.low_stock
        else:
            status = StockStatus.in_stock
        as_of = datetime.fromisoformat(record["as_of"])
        age = max(0.0, (datetime.now(UTC) - as_of).total_seconds())
        cached = result is not CacheResult.bypass
        return Availability(
            sku=product.sku,
            name=product.name,
            category=product.category,
            status=status,
            units_available=total,
            reorder_point=product.reorder_point,
            distribution_centers=[
                DistributionCenterStock(code=dc, name=name, units=units[dc.value])
                for dc, name in DISTRIBUTION_CENTERS.items()
            ],
            restock_eta=date.fromisoformat(record["restock_eta"]) if record["restock_eta"] and total == 0 else None,
            as_of=as_of,
            cache=CacheInfo(
                result=result,
                age_seconds=round(age, 1),
                ttl_seconds=CACHE_TTL_SECONDS,
                expires_in_seconds=round(max(0.0, CACHE_TTL_SECONDS - age), 1) if cached else 0,
            ),
            lookup_ms=round(elapsed_ms, 1),
        )

    # --- Writes invalidate the cache ---------------------------------------------

    def reserve(self, sku: str, request: ReservationRequest) -> Reservation:
        product = self.product(sku)
        with tracer.start_as_current_span("inventory.reserve") as span:
            span.set_attributes({"inventory.sku": sku, "inventory.reservation.quantity": request.quantity})
            with self._lock:
                stock = self._stock[sku]
                dc = request.distribution_center or DistributionCenter(max(stock, key=stock.get))
                on_hand = stock[dc.value]
                span.set_attribute("inventory.distribution_center", dc.value)
                outcome = "confirmed" if on_hand >= request.quantity else "rejected"
                if outcome == "confirmed":
                    stock[dc.value] = remaining = on_hand - request.quantity
                eta = self._restock_eta[sku]
            inventory_metrics.reservations.add(
                1, {"inventory.reservation.outcome": outcome, "inventory.distribution_center": dc.value}
            )
            log_extra = {"inventory.sku": sku, "inventory.distribution_center": dc.value,
                         "inventory.reservation.quantity": request.quantity}

            if outcome == "rejected":
                span.add_event("reservation.rejected", {"inventory.units_on_hand": on_hand})
                span.set_status(Status(StatusCode.ERROR, "insufficient stock"))
                logger.warning("Rejected reservation of %d x %s at %s: only %d on hand",
                               request.quantity, product.name, dc.value, on_hand,
                               extra={**log_extra, "inventory.units_on_hand": on_hand})
                detail = f"Only {on_hand} units of {product.name} ({sku}) are on hand at {dc.value}; {request.quantity} requested."
                if eta and on_hand == 0:
                    detail += f" Restock expected {eta.isoformat()}."
                raise InsufficientStock(detail)

            reservation = Reservation(
                reservation_id=f"RSV-{secrets.token_hex(3).upper()}",
                sku=sku,
                quantity=request.quantity,
                distribution_center=dc,
                units_remaining=remaining,
                created_at=_utcnow(),
            )
            span.set_attribute("inventory.reservation.id", reservation.reservation_id)
            self._invalidate(sku, span)
            logger.info("Reserved %d x %s at %s (%s); %d left there",
                        request.quantity, product.name, dc.value, reservation.reservation_id, remaining,
                        extra={**log_extra, "inventory.reservation.id": reservation.reservation_id})
            return reservation

    def _invalidate(self, sku: str, span: Span) -> None:
        client = redis_client()
        if client is None:
            return
        try:
            removed = client.delete(cache_key(sku))
            span.add_event("cache.invalidated", {"inventory.cache.key": cache_key(sku), "inventory.cache.removed": removed})
        except redis.RedisError as exc:
            span.record_exception(exc)

    # --- Cache administration (dashboard commands) -----------------------------

    def _require_cache(self) -> redis.Redis:
        client = redis_client()
        if client is None:
            raise CacheUnavailable("No Redis cache is configured (CACHE_URI is not set).")
        return client

    def warm(self) -> CacheWarmResult:
        started = time.perf_counter()
        with tracer.start_as_current_span("inventory.cache.warm") as span:
            client = self._require_cache()
            records = self._query_warehouse(list(PRODUCTS))
            try:
                with client.pipeline(transaction=False) as pipe:
                    for sku, record in records.items():
                        pipe.set(cache_key(sku), json.dumps(record), ex=CACHE_TTL_SECONDS)
                    pipe.execute()
            except redis.RedisError as exc:
                raise CacheUnavailable(f"Redis is unavailable: {exc}") from exc
            duration_ms = (time.perf_counter() - started) * 1000
            span.set_attribute("inventory.cache.entries_written", len(records))
            logger.info("Warmed the availability cache with %d SKUs in %.0f ms (TTL %d s)",
                        len(records), duration_ms, CACHE_TTL_SECONDS,
                        extra={"inventory.cache.entries_written": len(records)})
            return CacheWarmResult(warmed=len(records), ttl_seconds=CACHE_TTL_SECONDS, duration_ms=round(duration_ms, 1))

    def flush(self) -> CacheFlushResult:
        with tracer.start_as_current_span("inventory.cache.flush") as span:
            client = self._require_cache()
            try:
                flushed = client.delete(*(cache_key(sku) for sku in PRODUCTS))
            except redis.RedisError as exc:
                raise CacheUnavailable(f"Redis is unavailable: {exc}") from exc
            span.set_attribute("inventory.cache.entries_removed", flushed)
            logger.info("Flushed %d cached availability entries; the next lookups will miss", flushed,
                        extra={"inventory.cache.entries_removed": flushed})
            return CacheFlushResult(flushed=flushed)

    def stats(self) -> CacheStats:
        client = self._require_cache()
        try:
            cached = client.exists(*(cache_key(sku) for sku in PRODUCTS))
        except redis.RedisError as exc:
            raise CacheUnavailable(f"Redis is unavailable: {exc}") from exc
        with self._lock:
            hits, misses = self._hits, self._misses
        return CacheStats(
            catalog_skus=len(PRODUCTS),
            cached_skus=cached,
            ttl_seconds=CACHE_TTL_SECONDS,
            hits=hits,
            misses=misses,
            hit_ratio=round(hits / (hits + misses), 3) if hits + misses else 0.0,
        )

    # --- Demo traffic -----------------------------------------------------------

    def simulate(self, shoppers: int) -> SimulationResult:
        """Shoppers check popular SKUs; some reserve units, and empty bins are restocked now and then."""
        rng = random.Random()
        result = SimulationResult(shoppers=shoppers)
        skus = list(PRODUCTS)
        weights = [POPULARITY[sku] for sku in skus]
        with tracer.start_as_current_span("inventory.simulate") as span:
            span.set_attribute("inventory.simulate.shoppers", shoppers)
            result.restocked = self._receive_pallets(rng)
            for index in range(shoppers):
                # The last shopper always wants the sold-out tent, so every run shows one 409.
                sku = BACKORDERED_SKU if index == shoppers - 1 else rng.choices(skus, weights)[0]
                availability = self.availability(sku)
                if availability.cache.result is CacheResult.hit:
                    result.hits += 1
                else:
                    result.misses += 1
                if sku == BACKORDERED_SKU or rng.random() < 0.2:
                    try:
                        self.reserve(sku, ReservationRequest(quantity=rng.randint(1, 2)))
                        result.reservations += 1
                    except InsufficientStock:
                        result.rejected += 1
            result.hit_ratio = round(result.hits / shoppers, 3)
            span.set_attributes({
                "inventory.simulate.hits": result.hits,
                "inventory.simulate.misses": result.misses,
                "inventory.simulate.rejected": result.rejected,
            })
            logger.info("Simulated %d shoppers: %d cache hits, %d misses, %d reservations, %d rejected",
                        shoppers, result.hits, result.misses, result.reservations, result.rejected,
                        extra={"inventory.simulate.shoppers": shoppers, "inventory.simulate.hit_ratio": result.hit_ratio})
            return result

    def _receive_pallets(self, rng: random.Random) -> int:
        """Now and then refill empty bins (except backordered SKUs) so simulated demand never drains the catalog."""
        restocked = []
        with self._lock:
            for sku, stock in self._stock.items():
                if self._restock_eta[sku]:
                    continue
                for dc, units in stock.items():
                    if units == 0 and rng.random() < 0.2:
                        stock[dc] = units + RESTOCK_PALLET_UNITS
                        restocked.append((sku, dc))
        for sku, dc in restocked:
            with tracer.start_as_current_span("inventory.restock") as span:
                span.set_attributes({"inventory.sku": sku, "inventory.distribution_center": dc,
                                     "inventory.restock.units": RESTOCK_PALLET_UNITS})
                self._invalidate(sku, span)
            logger.info("Received a pallet of %d x %s at %s", RESTOCK_PALLET_UNITS, PRODUCTS[sku].name, dc,
                        extra={"inventory.sku": sku, "inventory.distribution_center": dc})
        return len(restocked)

    # --- Observable gauges ------------------------------------------------------

    def _observe_cache_entries(self, _: CallbackOptions):
        client = redis_client()
        if client is None:
            return []
        try:
            # The metric reader polls on a timer; don't turn each poll into a Redis trace.
            with suppress_instrumentation():
                return [Observation(client.exists(*(cache_key(sku) for sku in PRODUCTS)))]
        except redis.RedisError:
            return []

    def _observe_units_on_hand(self, _: CallbackOptions):
        with self._lock:
            totals = {dc.value: sum(stock[dc.value] for stock in self._stock.values()) for dc in DISTRIBUTION_CENTERS}
        return [Observation(units, {"inventory.distribution_center": dc}) for dc, units in totals.items()]


inventory = InventoryService()
