# Python FastAPI + PostgreSQL Sample

**Contoso Customers: a FastAPI CRM API backed by PostgreSQL, orchestrated and observed with Aspire.**

Account managers search, page through, create, update, and retire customer accounts. Each customer has a sales segment (enterprise, mid-market, small business), a lifecycle status (lead → active → churned), a country, and a lifetime value. Emails are unique, so registering one that's already on file returns `409 Conflict` with an RFC 9457 problem details body.

The API is fully instrumented with OpenTelemetry. FastAPI server spans, `customers.*` domain spans, and one PostgreSQL client span per SQL statement form a single trace. The `Contoso.Customers` meter and the `contoso.customers` structured logs land in the Aspire dashboard, correlated by trace id. The API also ships a themed [Scalar](https://github.com/scalar/scalar) API reference at `/scalar`.

![Scalar API reference for the Contoso Customers API open on Create a customer, showing the CustomerCreate body schema and the 409 Conflict problem details example for a duplicate email (light theme)](./images/python-fastapi-postgres-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Customers API open on Create a customer, showing the CustomerCreate body schema and the 409 Conflict problem details example for a duplicate email (dark theme)](./images/python-fastapi-postgres-scalar-dark.png#gh-dark-mode-only)

## Quick Start

### Prerequisites

- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- [Docker](https://docs.docker.com/get-docker/)
- [Python 3.11+](https://www.python.org/)

### Commands

```bash
aspire run      # Run locally
aspire deploy   # Deploy to Docker Compose
aspire do docker-compose-down-dc  # Teardown deployment
```

After `aspire run`, choose **Seed sample customers** on the `api` resource in the dashboard to load 24 realistic accounts, then **Simulate traffic** to generate activity.

## Overview

The application consists of:

- **Aspire AppHost** - Orchestrates the Python API and PostgreSQL database
- **FastAPI API** - The Contoso Customers API, with OpenAPI, problem details, and OpenTelemetry traces, metrics, and logs
- **PostgreSQL** - Stores the customer accounts
- **pgAdmin** - Web-based PostgreSQL administration tool for local/demo use

## Key Code

The `apphost.mts` configuration demonstrates Aspire's Python support:

```ts
const postgres = await builder.addPostgres("postgres")
    .withPgAdmin();
const db = await postgres.addDatabase("db");

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withExternalHttpEndpoints()
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(db)
    .withReference(db)
    .withReference(postgres);

const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" });

if (await executionContext.isRunMode())
{
    await api
        .withEnvironment("ENABLE_DEMO_ENDPOINTS", "true")
        .withHttpCommand("/demo/seed", "Seed sample customers", {
            methodName: "POST",
            confirmationMessage: "Replace all customers with the 24 sample accounts?",
            resultMode: HttpCommandResultMode.Json,
        })
        .withHttpCommand("/demo/simulate", "Simulate traffic", {
            methodName: "POST",
            resultMode: HttpCommandResultMode.Json,
        });
}
```

Key features:

- **Python Polyglot Support**: Uses `addUvicornApp` to run FastAPI applications
- **PgAdmin Integration**: `.withPgAdmin()` adds a web-based database management tool for local/demo use
- **Health Checks and Startup Dependencies**: `.withHttpHealthCheck()` probes `/health` (a database round trip), and `.waitFor(db)` starts the API only once the database is ready
- **Database Connection**: Aspire provides connection properties via `DB_*` and `POSTGRES_*` environment variables (for example `DB_URI` and `DB_DATABASENAME`)
- **Dashboard Links and Commands**: `withUrl` adds **API reference (Scalar)** and **OpenAPI JSON** links, and `withHttpCommand` adds the **Seed sample customers** and **Simulate traffic** buttons (run mode only)
- **External HTTP Endpoints**: Enables external access to the API
- **Docker Compose Deployment**: Ready for containerized deployment

## API Endpoints

The FastAPI application provides the following endpoints:

- `GET /customers?status=&segment=&country=&q=&limit=25&offset=0` - List customers with filters and paging (returns `items` plus the `total` number of matches)
- `GET /customers/{id}` - Get a customer (`404` if it doesn't exist)
- `POST /customers` - Create a customer (`201` with a `Location` header, `409` for a duplicate email, `422` for invalid input)
- `PATCH /customers/{id}` - Update some fields, for example move a lead to `active` or raise its lifetime value
- `DELETE /customers/{id}` - Delete a customer
- `GET /customers/summary` - Counts by status and segment, plus total lifetime value
- `GET /health` - Health check with a database round trip (`503` when PostgreSQL is unreachable)
- `POST /demo/seed` and `POST /demo/simulate` - Demo helpers behind the dashboard commands (run mode only)

Every error is returned as `application/problem+json`. Interactive API documentation is available at:

- `GET /scalar` - Themed [Scalar](https://github.com/scalar/scalar) API reference
- `GET /openapi.json` - The OpenAPI document
- `GET /docs` - Swagger UI
- `GET /redoc` - ReDoc

## What to explore in the Aspire dashboard

- **Resources**: the `api` resource links to **API reference (Scalar)** and **OpenAPI JSON**, and `postgres` links to **Manage (pgAdmin)**. The `api` actions include **Seed sample customers** (replace every customer with 24 accounts, after a confirmation) and **Simulate traffic** (searches, inbound leads, conversions, upsells, and churn, plus one duplicate sign-up rejected with `409` and one stale `404` lookup). Both return a JSON summary.

  ![Aspire dashboard resources page with the api actions menu open on the Seed sample customers and Simulate traffic commands, next to the healthy api, postgres and db resources (light theme)](./images/python-fastapi-postgres-dashboard-resources-light.png#gh-light-mode-only)
  ![Aspire dashboard resources page with the api actions menu open on the Seed sample customers and Simulate traffic commands, next to the healthy api, postgres and db resources (dark theme)](./images/python-fastapi-postgres-dashboard-resources-dark.png#gh-dark-mode-only)

- **Traces**: run **Simulate traffic** and open the `POST /demo/simulate` trace. Under the FastAPI server span, each `customers.list`, `customers.create`, `customers.update`, and `customers.get` domain span has its PostgreSQL `SELECT`, `INSERT`, or `UPDATE` client spans, with the parameterized SQL in `db.statement`. The duplicate sign-up and the stale lookup are marked as errors. Domain spans carry `customer.id`, `customer.segment`, `customer.status`, and `customer.country`, plus `customer.status_changed`, `customer.revenue_booked`, and `customer.duplicate_email` events. Calls to the real endpoints produce the same spans.

  ![Aspire dashboard trace for POST /demo/simulate with customers.list, create, update and get spans, each with its PostgreSQL client spans, and the customers.update span open to show its customer attributes and its lead-to-active customer.status_changed event (light theme)](./images/python-fastapi-postgres-trace-light.png#gh-light-mode-only)
  ![Aspire dashboard trace for POST /demo/simulate with customers.list, create, update and get spans, each with its PostgreSQL client spans, and the customers.update span open to show its customer attributes and its lead-to-active customer.status_changed event (dark theme)](./images/python-fastapi-postgres-trace-dark.png#gh-dark-mode-only)

- **Metrics**: the `Contoso.Customers` meter publishes:
  - `customers.created`: accounts created, by `customer.segment`
  - `customers.status_changes`: lifecycle moves, by `customer.status.from` and `customer.status.to`
  - `customers.revenue`: histogram of revenue booked per deal in dollars (new accounts and lifetime value increases), by segment, with exemplars that link to traces
  - `customers.count`: up-down counter of customers in the database, by `customer.status`

  The psycopg instrumentation also reports `db.client.operation.duration`, and FastAPI reports `http.server.request.duration`.

  ![Aspire dashboard metrics page for the Contoso.Customers meter, with cards for customers in the database, accounts created, median deal size and lifecycle changes, each with a sparkline of recent activity (light theme)](./images/python-fastapi-postgres-metrics-overview-light.png#gh-light-mode-only)
  ![Aspire dashboard metrics page for the Contoso.Customers meter, with cards for customers in the database, accounts created, median deal size and lifecycle changes, each with a sparkline of recent activity (dark theme)](./images/python-fastapi-postgres-metrics-overview-dark.png#gh-dark-mode-only)

  ![Aspire dashboard metrics page for the Contoso.Customers meter, charting the customers.revenue histogram's P50, P90 and P99 deal sizes over five minutes of simulated traffic, with exemplar dots linking to traces (light theme)](./images/python-fastapi-postgres-metrics-light.png#gh-light-mode-only)
  ![Aspire dashboard metrics page for the Contoso.Customers meter, charting the customers.revenue histogram's P50, P90 and P99 deal sizes over five minutes of simulated traffic, with exemplar dots linking to traces (dark theme)](./images/python-fastapi-postgres-metrics-dark.png#gh-dark-mode-only)

- **Structured logs**: the `contoso.customers` logger writes entries such as `Customer 14 Blue Yonder Airlines moved from lead to active` and `Booked $48,200 with customer 3 Fabrikam, Inc.`, and a warning for each rejected duplicate. Emails appear only in masked form (`j***@fabrikam.com`). The `extra={...}` fields (`customer.id`, `customer.segment`, `customer.status.from`/`to`, and others) become log attributes, and each entry links to its trace.

  ![Aspire dashboard structured logs for the api, with customer created, moved and revenue booked entries, and a duplicate-email warning open to show its masked email, segment and trace link (light theme)](./images/python-fastapi-postgres-logs-light.png#gh-light-mode-only)
  ![Aspire dashboard structured logs for the api, with customer created, moved and revenue booked entries, and a duplicate-email warning open to show its masked email, segment and trace link (dark theme)](./images/python-fastapi-postgres-logs-dark.png#gh-dark-mode-only)

- **API reference**: open **API reference (Scalar)** on the `api` resource (shown at the top of this page). Every operation has a summary, description, typed request and response schemas, and `application/problem+json` examples for 404, 409, 422, and 503.

## How It Works

1. **Virtual Environment**: Aspire automatically creates a `.venv` directory and installs dependencies from `requirements.txt`
2. **Modular Architecture**: The API is organized into separate modules:
   - `main.py` - FastAPI routes, OpenAPI metadata, and problem details handlers
   - `models.py` - Pydantic models for validation, examples, and problem details
   - `customers.py` - SQL for each operation, wrapped in domain spans, metrics, and logs
   - `database.py` - Database creation, schema, and the psycopg connection pool
   - `telemetry.py` - OpenTelemetry setup (OTLP traces, metrics, and logs; FastAPI and psycopg instrumentation) and the `Contoso.Customers` instruments
   - `demo.py` - Sample customers and simulated traffic for the dashboard commands
   - `scalar_theme.py` - Custom teal/Python theming for the Scalar API reference
3. **Database Initialization**: On startup, the API creates the database named by `DB_DATABASENAME` if it's missing (using the server connection in `POSTGRES_URI`), then creates the `customers` table
4. **Connection Management**: A `psycopg_pool.AsyncConnectionPool` built from `DB_URI` serves every request
5. **Telemetry**: Aspire sets the `OTEL_*` environment variables, and `telemetry.py` exports traces, metrics, and logs over OTLP to the dashboard. Health probes are kept out of traces
6. **Development Experience**: Zero configuration - Aspire handles virtual environment, dependency installation, and process management

## Security Notes

This sample is intentionally simple demo code. The API exposes public, unauthenticated CRUD endpoints and does not implement CSRF protection, rate limiting, authorization, or user/session management. Before using this pattern in production, add authentication and authorization, apply rate limits, review CSRF requirements for browser clients, and follow the [FastAPI security documentation](https://fastapi.tiangolo.com/tutorial/security/) and [OWASP API Security Top 10](https://owasp.org/API-Security/editions/2023/en/0x11-t10/).

Customer payloads use Pydantic field constraints (lengths, a two-letter country code, a non-negative lifetime value), and `GET /customers` uses bounded `limit` and `offset` query parameters to avoid returning an unbounded result set. The email check is a lightweight constrained-string pattern to avoid adding the optional `email-validator` package required by Pydantic `EmailStr`; use `EmailStr` or stricter domain-specific validation if your application needs full email address validation. See the [FastAPI request body field documentation](https://fastapi.tiangolo.com/tutorial/body-fields/) and [Pydantic field validation documentation](https://docs.pydantic.dev/latest/concepts/fields/) for more validation options.

All SQL uses bound parameters, and dynamic pieces (filters, updated columns, the database name) are composed with `psycopg.sql` instead of string interpolation. In this Aspire sample, `DB_DATABASENAME` is generated by the AppHost and trusted for local development, but production code should still prefer least-privilege credentials that cannot create arbitrary databases. See the [Psycopg SQL composition documentation](https://www.psycopg.org/psycopg3/docs/api/sql.html).

Customer emails are personal data. They never appear in span or metric attributes, and logs and problem details only show a masked form. Traces record SQL statements with placeholders, not parameter values.

The `/demo/seed` and `/demo/simulate` endpoints exist only when the AppHost sets `ENABLE_DEMO_ENDPOINTS` (run mode). Seeding deletes every customer.

pgAdmin is included as local/demo tooling through `.withPgAdmin()`. Do not expose pgAdmin publicly without strong authentication, network restrictions, TLS, secret management, and operational monitoring.

## VS Code Integration

This sample includes VS Code configuration for Python development:

- **`.vscode/settings.json`**: Configures the Python interpreter to use the Aspire-created virtual environment
- After running `aspire run`, open the sample in VS Code for full IntelliSense and debugging support
- The virtual environment at `api/.venv` will be automatically detected

## Deployment

The sample uses Docker Compose for deployment. Run:

```bash
aspire deploy
```

This will:

1. Generate a Dockerfile for the Python application
2. Install dependencies and create a container image
3. Generate Docker Compose files with PostgreSQL
4. Deploy the complete application stack

The demo endpoints and dashboard commands are not part of the deployment.

To teardown:

```bash
aspire do docker-compose-down-dc
```
