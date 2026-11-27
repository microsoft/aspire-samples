"""PostgreSQL connection pool and schema bootstrap.

The AppHost references both the server and the database, so Aspire injects:
- POSTGRES_URI: the server, used once at startup to create the database when it's missing.
- DB_URI and DB_DATABASENAME: the `db` database, used by the connection pool.
"""

import logging
import os
from typing import Optional

from opentelemetry import trace
from opentelemetry.instrumentation.utils import suppress_instrumentation
from psycopg import AsyncConnection, sql
from psycopg.rows import dict_row
from psycopg_pool import AsyncConnectionPool

logger = logging.getLogger("contoso.customers")

CREATE_TABLE = """
CREATE TABLE IF NOT EXISTS customers (
    id             INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name           VARCHAR(100)  NOT NULL,
    email          VARCHAR(254)  NOT NULL UNIQUE,
    company        VARCHAR(120)  NOT NULL,
    segment        VARCHAR(20)   NOT NULL,
    status         VARCHAR(20)   NOT NULL,
    country        CHAR(2)       NOT NULL,
    lifetime_value NUMERIC(12, 2) NOT NULL DEFAULT 0,
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ   NOT NULL DEFAULT now()
)
"""
CREATE_INDEX = "CREATE INDEX IF NOT EXISTS ix_customers_status_segment ON customers (status, segment)"

_pool: Optional[AsyncConnectionPool] = None


def _require_env(name: str) -> str:
    value = os.getenv(name)
    if not value:
        raise RuntimeError(f"{name} is not set. Run the API through the Aspire AppHost (`aspire run`).")
    return value


async def open_pool() -> None:
    """Create the database and schema if needed, then open the connection pool."""
    global _pool
    db_uri = _require_env("DB_URI")
    db_name = os.getenv("DB_DATABASENAME", "db")

    server_uri = os.getenv("POSTGRES_URI")
    if server_uri:
        async with await AsyncConnection.connect(server_uri, autocommit=True) as conn:
            cur = await conn.execute("SELECT 1 FROM pg_database WHERE datname = %s", (db_name,))
            if await cur.fetchone() is None:
                await conn.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(db_name)))
                trace.get_current_span().add_event("database.created", {"db.namespace": db_name})
                logger.info("Created database %s", db_name, extra={"db.namespace": db_name})

    _pool = AsyncConnectionPool(
        db_uri,
        min_size=1,
        max_size=10,
        open=False,
        name="customers",
        kwargs={"autocommit": True, "row_factory": dict_row},
    )
    await _pool.open(wait=True, timeout=30)

    async with _pool.connection() as conn:
        await conn.execute(CREATE_TABLE)
        await conn.execute(CREATE_INDEX)
    logger.info("Customers database %s is ready", db_name, extra={"db.namespace": db_name})


async def close_pool() -> None:
    if _pool is not None:
        await _pool.close()


def connection():
    """Borrow a pooled connection: `async with connection() as conn: ...`."""
    if _pool is None:
        raise RuntimeError("The connection pool is not open.")
    return _pool.connection(timeout=10)


async def check_health() -> None:
    """Round-trip to PostgreSQL. Health probes run every few seconds, so keep them out of traces."""
    with suppress_instrumentation():
        async with connection() as conn:
            await conn.execute("SELECT 1")
