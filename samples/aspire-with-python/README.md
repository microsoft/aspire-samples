# Integrating a FastAPI (Python) app within an Aspire application

This sample integrates a FastAPI (Python) API, a Redis cache, and a Vite + React frontend using Aspire. It's themed as **Contoso Outdoors stock availability**: shoppers and store associates check how many units of a tent, pack, or pair of boots are on hand at the Seattle, Denver, and Atlanta distribution centers.

Availability reads use the **cache-aside** pattern. The API checks Redis first; on a miss it queries the (deliberately slow, simulated) warehouse system and caches the answer for 30 seconds. Reservations change stock, so they remove the SKU's cache entry. Everything is instrumented with OpenTelemetry, so one click in the browser becomes one trace that crosses the frontend, the API, and Redis in the Aspire dashboard.

![Contoso Outdoors stock availability board listing 12 SKUs with units per distribution center, in-stock, low-stock and out-of-stock badges, and a HIT or MISS cache badge with the lookup time on every row, under session totals for cache hit ratio and median hit and miss latency (light theme)](./images/aspire-with-python-app-light.png#gh-light-mode-only)
![Contoso Outdoors stock availability board listing 12 SKUs with units per distribution center, in-stock, low-stock and out-of-stock badges, and a HIT or MISS cache badge with the lookup time on every row, under session totals for cache hit ratio and median hit and miss latency (dark theme)](./images/aspire-with-python-app-dark.png#gh-dark-mode-only)

The sample consists of three resources:

- **app**: a FastAPI inventory API (`app/main.py`, `app/inventory.py`) run with uv and Uvicorn. It documents itself with OpenAPI and [Scalar](https://github.com/scalar/scalar), returns RFC 9457 problem details for errors, and exports traces, metrics, and logs to the dashboard (`app/telemetry.py`).
- **cache**: a Redis container that holds availability records for 30 seconds.
- **frontend**: a Vite-based React app that shows availability for the whole catalog and sends browser traces to the dashboard.

## Prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [Python](https://www.python.org/) - at least version 3.13
- [Node.js](https://nodejs.org) - at least version 22.21.1
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `apphost.cs` C# file using either the Aspire or C# debuggers.

Open **Availability board** on the `frontend` resource. Every row has a **Check availability** button, and **Check all** re-checks the whole catalog. Use the **Warm cache** and **Flush cache** commands on the `app` resource to switch between cache hits and misses.

## API

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/products` | The catalog (12 SKUs), optionally filtered by `category`. |
| `GET` | `/api/inventory/{sku}` | Units per distribution center and an `in_stock`, `low_stock`, or `out_of_stock` status, served cache-aside. The `cache` object and the `X-Cache` header say whether Redis answered (`hit`) or the warehouse system did (`miss`). |
| `POST` | `/api/inventory/{sku}/reservations` | Reserves units and invalidates the SKU's cache entry. Returns `409 Conflict` when there aren't enough units. |
| `GET` | `/api/cache` | Cached SKU count, TTL, and this instance's hit and miss counts. |
| `POST` | `/api/cache/warm` | Loads every SKU into Redis (run mode only). |
| `DELETE` | `/api/cache` | Removes every cached availability entry (run mode only). |
| `POST` | `/api/demo/simulate` | Simulates a burst of shoppers (run mode only). |

Unknown SKUs return `404`, malformed SKUs and bodies return `422`, and cache administration returns `503` when Redis is unreachable, all as `application/problem+json`. When Redis is down, availability reads still work: they report `cache.result: bypass` and go straight to the warehouse system.

## What to explore in the Aspire dashboard

- **Resources and commands**: the `app` resource links to **API reference (Scalar)** and **OpenAPI JSON**, and the `frontend` resource links to the **Availability board**. In run mode the `app` resource adds three commands, each returning a JSON summary:
  - **Warm cache** loads all 12 SKUs from the warehouse system in one query and caches them.
  - **Flush cache** asks for confirmation, then removes every cached entry, so the next lookups miss.
  - **Simulate shoppers** sends 40 shoppers to popular SKUs (a mix of hits and misses). Some reserve units, empty bins are restocked now and then, and the last shopper tries to reserve the sold-out Basecamp 4P tent (409).

  ![Aspire dashboard resources page with the app actions menu open, showing the Warm cache, Flush cache and Simulate shoppers commands next to the healthy cache, app and frontend resources (light theme)](./images/aspire-with-python-dashboard-resources-light.png#gh-light-mode-only)
  ![Aspire dashboard resources page with the app actions menu open, showing the Warm cache, Flush cache and Simulate shoppers commands next to the healthy cache, app and frontend resources (dark theme)](./images/aspire-with-python-dashboard-resources-dark.png#gh-dark-mode-only)

- **Traces**: run **Flush cache**, then click **Check availability** on a row. The `check availability` trace starts in the browser (`frontend`), crosses the fetch span into the FastAPI `GET /api/inventory/{sku}` span, and continues into the `inventory.lookup` domain span: a Redis `GET` that misses, `inventory.warehouse.query`, and a Redis `SET` that repopulates the cache (the dashboard attributes both Redis spans to the `cache` resource). Click the same row again and the trace ends at a single Redis `GET`. `inventory.lookup` carries `inventory.sku`, `inventory.cache.result`, `inventory.units_available`, and `inventory.stock_status`, with `cache.miss`, `cache.populated`, `cache.hit`, `inventory.low_stock`, and `inventory.out_of_stock` events. Reservations add an `inventory.reserve` span with a `cache.invalidated` event.

  ![Aspire dashboard trace for one check availability click that flows from the frontend's browser span through the fetch and FastAPI spans into inventory.lookup, with a Redis GET miss, the warehouse query and a Redis SET, and the inventory.lookup span open to show its attributes and cache events (light theme)](./images/aspire-with-python-trace-light.png#gh-light-mode-only)
  ![Aspire dashboard trace for one check availability click that flows from the frontend's browser span through the fetch and FastAPI spans into inventory.lookup, with a Redis GET miss, the warehouse query and a Redis SET, and the inventory.lookup span open to show its attributes and cache events (dark theme)](./images/aspire-with-python-trace-dark.png#gh-dark-mode-only)

- **Metrics**: the `Contoso.Inventory` meter on the `app` resource publishes:
  - `inventory.lookups`: availability lookups, by `inventory.cache.result` (`hit`, `miss`, `bypass`)
  - `inventory.lookup.duration`: histogram of lookup time in milliseconds, by `inventory.cache.result`, with exemplars that link to traces
  - `inventory.reservations`: reservation attempts, by `inventory.reservation.outcome` and `inventory.distribution_center`
  - `inventory.cache.entries`: gauge of SKUs currently cached in Redis
  - `inventory.units_on_hand`: gauge of units on hand, by `inventory.distribution_center`

  ![Aspire dashboard metrics page for the Contoso.Inventory meter charting inventory.lookup.duration over five minutes of simulated shoppers: P50 hugs a few milliseconds (cache hits) while P90 and P99 sit at the 150-200 ms warehouse-query latency of misses, with exemplar dots that link to traces (light theme)](./images/aspire-with-python-metrics-light.png#gh-light-mode-only)
  ![Aspire dashboard metrics page for the Contoso.Inventory meter charting inventory.lookup.duration over five minutes of simulated shoppers: P50 hugs a few milliseconds (cache hits) while P90 and P99 sit at the 150-200 ms warehouse-query latency of misses, with exemplar dots that link to traces (dark theme)](./images/aspire-with-python-metrics-dark.png#gh-dark-mode-only)

- **Structured logs**: the `contoso.inventory` logger writes entries such as `Cache miss for PCK-65L-SUM: loaded 50 units from 3 distribution centers in 131 ms and cached them for 30 s`, low-stock and out-of-stock warnings, reservations, restocks, and a warning for each rejected reservation. The `extra={...}` fields (`inventory.sku`, `inventory.cache.result`, `inventory.lookup_ms`, `inventory.distribution_center`, and others) become log attributes, and each entry links to its trace. Cache hits are logged at debug level, so they don't flood the page.

  ![Aspire dashboard structured logs for the app resource, with cache-miss and reservation entries, an out-of-stock warning, and a rejected reservation for the sold-out Basecamp 4P tent open to show its inventory attributes and trace link (light theme)](./images/aspire-with-python-logs-light.png#gh-light-mode-only)
  ![Aspire dashboard structured logs for the app resource, with cache-miss and reservation entries, an out-of-stock warning, and a rejected reservation for the sold-out Basecamp 4P tent open to show its inventory attributes and trace link (dark theme)](./images/aspire-with-python-logs-dark.png#gh-dark-mode-only)

- **API reference**: open **API reference (Scalar)** on the `app` resource. Operations are grouped by tag (Catalog, Availability, Reservations, Cache, System, Demo) and have summaries, descriptions, typed schemas, examples, and `application/problem+json` error responses.

  ![Scalar API reference for the Contoso Outdoors Inventory API open on Check availability, describing the cache-aside behavior with the Availability response schema and an example whose cache result is a hit (light theme)](./images/aspire-with-python-scalar-light.png#gh-light-mode-only)
  ![Scalar API reference for the Contoso Outdoors Inventory API open on Check availability, describing the cache-aside behavior with the Availability response schema and an example whose cache result is a hit (dark theme)](./images/aspire-with-python-scalar-dark.png#gh-dark-mode-only)

### Browser telemetry

In run mode the frontend resource receives the dashboard's OTLP/HTTP endpoint (`ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` in `apphost.run.json`) and API key through `WithOtlpExporter(OtlpProtocol.HttpProtobuf)`. The browser posts spans to the same-origin `/otlp` path, and the Vite dev server proxies them to the dashboard, adding the API key server-side (see `frontend/vite.config.ts`). Because `/api` is also same-origin, the fetch instrumentation adds the `traceparent` header without any CORS setup. Browser telemetry is disabled in the published build, where FastAPI serves the static frontend.

## Security notes

The inventory endpoints are public and unauthenticated for instructional use. Stock lives in process memory and resets when the API restarts. The cache administration and demo endpoints (`/api/cache/warm`, `DELETE /api/cache`, `/api/demo/simulate`) exist only when the AppHost sets `ENABLE_DEMO_ENDPOINTS` in run mode. For production, add authentication and authorization, rate limiting, and a real system of record.
