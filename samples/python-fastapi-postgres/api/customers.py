"""Customer repository: SQL plus domain spans, metrics, and structured logs.

Every operation runs inside a `customers.*` span, so each SQL statement the psycopg
instrumentation records appears as its child. Emails never reach span or metric
attributes, and logs only carry a masked form.
"""

import logging
from typing import Optional

from psycopg import errors, sql

from database import connection
from models import (
    Customer,
    CustomerCreate,
    CustomerPage,
    CustomerStatus,
    CustomerSummary,
    CustomerUpdate,
    Segment,
)
from telemetry import customer_metrics, tracer

logger = logging.getLogger("contoso.customers")

FIELDS = ("id", "name", "email", "company", "segment", "status", "country", "lifetime_value", "created_at",
          "updated_at")
COLUMNS = sql.SQL(", ".join(FIELDS))
UPDATED_COLUMNS = sql.SQL(", ".join(f"c.{field}" for field in FIELDS))


class CustomerNotFound(Exception):
    def __init__(self, customer_id: int):
        super().__init__(f"Customer {customer_id} was not found.")


class DuplicateEmail(Exception):
    def __init__(self, email: str):
        super().__init__(f"Email {mask_email(email)} is already in use.")


def mask_email(email: str) -> str:
    """`j***@fabrikam.com`: enough to recognize an account without logging the address."""
    local, _, domain = email.partition("@")
    return f"{local[:1]}***@{domain}"


def _escape_like(value: str) -> str:
    return value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _attributes(row: dict) -> dict:
    return {
        "customer.id": row["id"],
        "customer.segment": row["segment"],
        "customer.status": row["status"],
        "customer.country": row["country"],
    }


def _record_revenue(amount: float, segment: str) -> None:
    if amount > 0:
        customer_metrics.revenue.record(amount, {"customer.segment": segment})


# --- Queries -----------------------------------------------------------------------


async def list_customers(
    status: Optional[CustomerStatus],
    segment: Optional[Segment],
    country: Optional[str],
    search: Optional[str],
    limit: int,
    offset: int,
) -> CustomerPage:
    with tracer.start_as_current_span("customers.list") as span:
        conditions: list[sql.Composable] = []
        params: list = []
        if status:
            conditions.append(sql.SQL("status = %s"))
            params.append(status.value)
        if segment:
            conditions.append(sql.SQL("segment = %s"))
            params.append(segment.value)
        if country:
            conditions.append(sql.SQL("country = %s"))
            params.append(country.upper())
        if search:
            conditions.append(sql.SQL("(name ILIKE %s OR company ILIKE %s OR email ILIKE %s)"))
            params += [f"%{_escape_like(search)}%"] * 3
        where = sql.SQL(" WHERE ") + sql.SQL(" AND ").join(conditions) if conditions else sql.SQL("")

        span.set_attributes({
            "customers.filter.status": status.value if status else "",
            "customers.filter.segment": segment.value if segment else "",
            "customers.filter.country": country.upper() if country else "",
            "customers.filter.search": bool(search),
            "customers.page.limit": limit,
            "customers.page.offset": offset,
        })

        async with connection() as conn:
            count = await conn.execute(sql.SQL("SELECT count(*) AS total FROM customers{}").format(where), params)
            total = (await count.fetchone())["total"]
            page = await conn.execute(
                sql.SQL("SELECT {} FROM customers{} ORDER BY id LIMIT %s OFFSET %s").format(COLUMNS, where),
                [*params, limit, offset],
            )
            rows = await page.fetchall()

        span.set_attributes({"customers.page.returned": len(rows), "customers.page.total": total})
        return CustomerPage(items=[Customer(**row) for row in rows], total=total, limit=limit, offset=offset)


async def get_customer(customer_id: int) -> Customer:
    with tracer.start_as_current_span("customers.get") as span:
        span.set_attribute("customer.id", customer_id)
        async with connection() as conn:
            cur = await conn.execute(sql.SQL("SELECT {} FROM customers WHERE id = %s").format(COLUMNS), (customer_id,))
            row = await cur.fetchone()
        if row is None:
            raise CustomerNotFound(customer_id)
        span.set_attributes(_attributes(row))
        return Customer(**row)


async def summarize() -> CustomerSummary:
    with tracer.start_as_current_span("customers.summary") as span:
        async with connection() as conn:
            cur = await conn.execute(
                "SELECT status, segment, count(*) AS customers, coalesce(sum(lifetime_value), 0) AS value "
                "FROM customers GROUP BY status, segment"
            )
            rows = await cur.fetchall()
        by_status = {s: 0 for s in CustomerStatus}
        by_segment = {s: 0 for s in Segment}
        for row in rows:
            by_status[CustomerStatus(row["status"])] += row["customers"]
            by_segment[Segment(row["segment"])] += row["customers"]
        total = sum(by_status.values())
        span.set_attribute("customers.total", total)
        return CustomerSummary(
            total=total,
            by_status=by_status,
            by_segment=by_segment,
            lifetime_value=float(sum(row["value"] for row in rows)),
        )


async def track_existing_customers() -> None:
    """Start the customers.count up-down counter from what's already in the database."""
    async with connection() as conn:
        cur = await conn.execute("SELECT status, count(*) AS customers FROM customers GROUP BY status")
        rows = await cur.fetchall()
    for row in rows:
        customer_metrics.count.add(row["customers"], {"customer.status": row["status"]})
    total = sum(row["customers"] for row in rows)
    logger.info("Tracking %d existing customers", total, extra={"customers.total": total})


# --- Commands ----------------------------------------------------------------------


async def create_customer(request: CustomerCreate) -> Customer:
    with tracer.start_as_current_span("customers.create") as span:
        values = request.model_dump(mode="json")
        span.set_attributes({"customer.segment": values["segment"], "customer.status": values["status"],
                             "customer.country": values["country"]})
        try:
            async with connection() as conn:
                cur = await conn.execute(
                    sql.SQL(
                        "INSERT INTO customers (name, email, company, segment, status, country, lifetime_value) "
                        "VALUES (%(name)s, %(email)s, %(company)s, %(segment)s, %(status)s, %(country)s, "
                        "%(lifetime_value)s) RETURNING {}"
                    ).format(COLUMNS),
                    values,
                )
                row = await cur.fetchone()
        except errors.UniqueViolation:
            span.add_event("customer.duplicate_email", {"customer.segment": values["segment"]})
            logger.warning(
                "Rejected duplicate email %s (%s)", mask_email(values["email"]), values["company"],
                extra={"customer.email_masked": mask_email(values["email"]), "customer.segment": values["segment"],
                       "customer.country": values["country"]},
            )
            raise DuplicateEmail(values["email"]) from None

        span.set_attributes(_attributes(row))
        customer_metrics.created.add(1, {"customer.segment": row["segment"]})
        customer_metrics.count.add(1, {"customer.status": row["status"]})
        _record_revenue(float(row["lifetime_value"]), row["segment"])
        logger.info(
            "Created %s customer %d %s (%s, %s)",
            row["segment"], row["id"], row["company"], mask_email(row["email"]), row["country"],
            extra={"customer.id": row["id"], "customer.segment": row["segment"], "customer.status": row["status"],
                   "customer.country": row["country"]},
        )
        return Customer(**row)


async def update_customer(customer_id: int, changes: CustomerUpdate) -> Customer:
    with tracer.start_as_current_span("customers.update") as span:
        span.set_attribute("customer.id", customer_id)
        fields = changes.model_dump(mode="json", exclude_none=True)
        span.set_attribute("customers.update.fields", sorted(fields))
        assignments = sql.SQL(", ").join(sql.SQL("{} = %s").format(sql.Identifier(name)) for name in fields)
        query = sql.SQL(
            "UPDATE customers AS c SET {}, updated_at = now() "
            "FROM (SELECT id, status, lifetime_value FROM customers WHERE id = %s FOR UPDATE) AS previous "
            "WHERE c.id = previous.id "
            "RETURNING {}, previous.status AS previous_status, previous.lifetime_value AS previous_value"
        ).format(assignments, UPDATED_COLUMNS)
        try:
            async with connection() as conn:
                cur = await conn.execute(query, [*fields.values(), customer_id])
                row = await cur.fetchone()
        except errors.UniqueViolation:
            span.add_event("customer.duplicate_email", {"customer.id": customer_id})
            logger.warning(
                "Rejected email change for customer %d: %s is already in use", customer_id,
                mask_email(fields["email"]), extra={"customer.id": customer_id},
            )
            raise DuplicateEmail(fields["email"]) from None
        if row is None:
            raise CustomerNotFound(customer_id)

        span.set_attributes(_attributes(row))
        log_extra = {"customer.id": row["id"], "customer.segment": row["segment"], "customer.status": row["status"]}
        previous_status, status = row["previous_status"], row["status"]
        if status != previous_status:
            change = {"customer.status.from": previous_status, "customer.status.to": status}
            span.add_event("customer.status_changed", change)
            customer_metrics.status_changes.add(1, change)
            customer_metrics.count.add(-1, {"customer.status": previous_status})
            customer_metrics.count.add(1, {"customer.status": status})
            logger.info("Customer %d %s moved from %s to %s", row["id"], row["company"], previous_status, status,
                        extra={**log_extra, **change})

        booked = float(row["lifetime_value"] - row["previous_value"])
        if booked > 0:
            span.add_event("customer.revenue_booked", {"customer.revenue.amount": booked})
            _record_revenue(booked, row["segment"])
            logger.info("Booked $%s with customer %d %s", f"{booked:,.0f}", row["id"], row["company"],
                        extra={**log_extra, "customer.revenue.amount": booked})
        return Customer(**row)


async def delete_customer(customer_id: int) -> None:
    with tracer.start_as_current_span("customers.delete") as span:
        span.set_attribute("customer.id", customer_id)
        async with connection() as conn:
            cur = await conn.execute(
                "DELETE FROM customers WHERE id = %s RETURNING id, company, segment, status, country", (customer_id,)
            )
            row = await cur.fetchone()
        if row is None:
            raise CustomerNotFound(customer_id)
        span.set_attributes(_attributes(row))
        customer_metrics.count.add(-1, {"customer.status": row["status"]})
        logger.info("Deleted customer %d %s", row["id"], row["company"],
                    extra={"customer.id": row["id"], "customer.status": row["status"]})
