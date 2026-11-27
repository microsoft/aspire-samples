# Working with database containers in an Aspire application

This sample demonstrates working with database containers in an Aspire app, using the features of the underlying container image to create and seed the database during container startup. This is especially helpful when not using an ORM like Entity Framework Core that can run migrations on application startup (e.g., [as in the Aspire Shop sample](../aspire-shop/AspireShop.CatalogDbManager)) and handle cases when the database configured in the AppHost is not yet created.

The sample is the store API for Contoso Outfitters, an outdoor gear retailer that keeps each part of its business in a different database container: the product catalog in PostgreSQL, customer accounts in MySQL, and orders in SQL Server. Placing an order uses all three, and the Aspire dashboard shows the whole thing in a single trace.

![Aspire dashboard trace of a POST /orders request that verifies the customer in MySQL, prices the lines from the PostgreSQL catalog and saves the order to SQL Server, with the place order span selected to show its customer, tier, order ID, item count and total attributes (light theme)](./images/database-containers-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of a POST /orders request that verifies the customer in MySQL, prices the lines from the PostgreSQL catalog and saves the order to SQL Server, with the place order span selected to show its customer, tier, order ID, item count and total attributes (dark theme)](./images/database-containers-trace-dark.png#gh-dark-mode-only)

The app uses the following database container types:

- [PostgreSQL](https://hub.docker.com/_/postgres/) holds the product catalog in the `catalog` database. The AppHost sets `POSTGRES_DB` so that the image creates the database, and `WithInitFiles` copies [`data/postgres/init.sql`](./DatabaseContainers.ApiService/data/postgres/init.sql) into `/docker-entrypoint-initdb.d`, where the image runs it to create and seed the `products` table.
- [MySQL](https://hub.docker.com/_/mysql) holds customer accounts and loyalty tiers in the `customers` database. It works the same way, with `MYSQL_DATABASE` and [`data/mysql/init.sql`](./DatabaseContainers.ApiService/data/mysql/init.sql).
- [Microsoft SQL Server](https://mcr.microsoft.com/en-us/product/mssql/server/about) holds orders and their lines in the `orders` database. The SQL Server image can't create databases or run scripts on startup, so the AppHost passes [`data/sqlserver/init.sql`](./DatabaseContainers.ApiService/data/sqlserver/init.sql) to `WithCreationScript`, and Aspire runs it when the server is ready.

Each container stores its data in a volume (`WithDataVolume`) and keeps running between AppHost sessions (`ContainerLifetime.Persistent`). The PostgreSQL and MySQL images only run their init scripts when they start with an empty data directory, so the seed data is created once and survives restarts. Aspire runs the SQL Server creation script every time the server starts, so that script is written to be idempotent. To start over completely, stop the containers and delete their data volumes.

The app consists of an API service:

- **DatabaseContainers.ApiService**: An HTTP API that reads products from PostgreSQL, customers from MySQL, and orders from SQL Server with [Dapper](https://github.com/DapperLib/Dapper), and places orders across all three databases. It's documented with OpenAPI and [Scalar](https://scalar.com) and returns [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details for errors.

The app also includes a .NET class library project, **DatabaseContainers.ServiceDefaults**, that contains the code-based defaults used by the API service, including OpenTelemetry registration for every `ContosoOutfitters.*` meter and activity source.

## Prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `DatabaseContainers.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `DatabaseContainers.slnx` and launch/debug the `DatabaseContainers.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `DatabaseContainers.AppHost` directory.

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with the postgres, mysql and sqlserver containers and their catalog, customers and orders databases running, and the apiservice actions menu open on the Simulate orders command and its description (light theme)](./images/database-containers-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with the postgres, mysql and sqlserver containers and their catalog, customers and orders databases running, and the apiservice actions menu open on the Simulate orders command and its description (dark theme)](./images/database-containers-dashboard-resources-dark.png#gh-dark-mode-only)

- Each database container is listed with its database as a child resource: **postgres** → **catalog**, **mysql** → **customers**, and **sqlserver** → **orders**. The **apiservice** waits until all three databases are healthy before it starts. The **postgres** resource also links to [pgweb](https://github.com/sosedoff/pgweb) (**Manage (pgweb)**) for browsing the catalog.
- On the **apiservice** resource, run **Simulate orders**. It places 24 orders for random customers and returns a JSON summary such as `{"orders":24,"placed":20,"rejected":4,"revenue":8460.00}`. Some orders are rejected because they include the out-of-stock tent, so the rejection metrics and warning logs fill in too.
- Run **Reset orders** to return the order history to its seeded state. After you confirm, the API drops the order tables and runs the SQL Server init script again. Both commands call development-only `/dev/...` endpoints and are only added in run mode.
- Open **API reference (Scalar)** or **OpenAPI JSON** from the **apiservice** resource. The raw endpoint URLs are listed on the resource details page.

### Database initialization

![Aspire dashboard console logs for the postgres container showing the image create the catalog database, run /docker-entrypoint-initdb.d/init.sql, and report CREATE TABLE and INSERT 0 14 before PostgreSQL starts (light theme)](./images/database-containers-init-light.png#gh-light-mode-only)
![Aspire dashboard console logs for the postgres container showing the image create the catalog database, run /docker-entrypoint-initdb.d/init.sql, and report CREATE TABLE and INSERT 0 14 before PostgreSQL starts (dark theme)](./images/database-containers-init-dark.png#gh-dark-mode-only)

The **Console logs** page shows each container initializing itself on its first start. For **postgres**, look for `running /docker-entrypoint-initdb.d/init.sql`, followed by `CREATE TABLE` and `INSERT 0 14` for the 14 seeded products. For **mysql**, look for `Creating database customers` and the same `running ... init.sql` line. On later runs the data volume already holds the data, so these lines don't appear again.

### API reference

![Scalar API reference for the Contoso Outfitters API showing the Place an order operation with its request body, a curl example, the 201, 400, 404 and 409 responses, and an example order with its customer and lines (light theme)](./images/database-containers-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Outfitters API showing the Place an order operation with its request body, a curl example, the 201, 400, 404 and 409 responses, and an example order with its customer and lines (dark theme)](./images/database-containers-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Database | Description |
| --- | --- | --- | --- |
| `GET` | `/products` | PostgreSQL | The product catalog, optionally filtered by `category` |
| `GET` | `/products/{productId}` | PostgreSQL | A single product, including its price and the units in stock |
| `GET` | `/customers` | MySQL | Every customer account |
| `GET` | `/customers/{customerId}` | MySQL | A single customer account, including the loyalty tier |
| `GET` | `/orders` | SQL Server | The most recent orders, optionally for one `customerId` (`limit` from 1 to 100) |
| `GET` | `/orders/{orderId}` | SQL Server, MySQL | An order and its lines, together with the customer's account |
| `POST` | `/orders` | All three | Places an order: verifies the customer, prices the lines from the catalog, and saves the order |

An invalid order (for example, no lines or a quantity above 10) returns a `400` validation problem, an unknown product, customer, or order returns a `404` problem, and ordering more units than are in stock returns a `409` problem. `DatabaseContainers.ApiService/DatabaseContainers.ApiService.http` has a request for each case.

### Traces

The trace at the top of this page is a single `POST /orders` request. Its `place order` span has a child span for each database the order touches, and each of those wraps the client integration's database span with the SQL statement:

- **verify customer**: the MySqlConnector `Open` and `Execute` spans against the **customers** database.
- **price order lines**: the Npgsql span against the **catalog** database, which loads every ordered product in one `WHERE id = ANY(@ids)` query.
- **save order**: the SqlClient `INSERT dbo.Orders` and `INSERT dbo.OrderLines` spans against the **orders** database. The lines are inserted in one statement with `OPENJSON`, inside a transaction.

The `place order` span carries `outfitters.customer.id`, `outfitters.customer.tier`, `outfitters.order.id`, `outfitters.order.line_count`, `outfitters.order.item_count`, `outfitters.order.total`, and `outfitters.order.outcome`, and records the `customer.verified`, `order.priced`, and `order.saved` events. Rejected orders record `outfitters.order.rejection_reason` and an `order.rejected` event instead. Each order placed by **Simulate orders** gets its own `simulated order` trace, linked to the command request that started the simulation.

### Metrics

![Aspire dashboard metrics page charting outfitters.order.value P50, P90 and P99 in USD with exemplars under simulated order traffic (light theme)](./images/database-containers-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting outfitters.order.value P50, P90 and P99 in USD with exemplars under simulated order traffic (dark theme)](./images/database-containers-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `ContosoOutfitters.Orders` | `outfitters.orders.placed` | Orders placed, by the customer's loyalty tier |
| `ContosoOutfitters.Orders` | `outfitters.orders.rejected` | Orders rejected, by reason (`invalid_request`, `customer_not_found`, `product_not_found`, `insufficient_stock`) |
| `ContosoOutfitters.Orders` | `outfitters.order.value` | Histogram of order totals, in USD, by loyalty tier |
| `ContosoOutfitters.Orders` | `outfitters.orders.in_progress` | Up-down counter of orders being placed right now |

Metric tags are limited to the loyalty tier and the rejection reason. Customer and order IDs only appear on spans and logs. The histogram records inside the `place order` span, so its exemplars link to the trace that produced each point. The client integrations add their own meters too: **Npgsql** records `db.client.operation.duration` and connection metrics for PostgreSQL, and **MySqlConnector** records connection pool metrics such as `db.client.connections.usage` for MySQL.

### Structured logs

![Aspire dashboard structured logs for the apiservice filtered to the OrderService category, with placed and rejected orders listed and an OrderPlaced entry open to show its OrderId, CustomerId, LoyaltyTier, ItemCount and OrderTotal properties and trace link (light theme)](./images/database-containers-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs for the apiservice filtered to the OrderService category, with placed and rejected orders listed and an OrderPlaced entry open to show its OrderId, CustomerId, LoyaltyTier, ItemCount and OrderTotal properties and trace link (dark theme)](./images/database-containers-logs-dark.png#gh-dark-mode-only)

The API logs its key events with source-generated `[LoggerMessage]` methods (`DatabaseContainers.ApiService/Log.cs`), so values such as `OrderId`, `CustomerId`, `LoyaltyTier`, `ItemCount`, `OrderTotal`, `RejectionReason`, and `RejectionDetail` arrive as properties you can filter on. Each entry links to the trace that produced it. Npgsql also logs every command it runs, with the SQL in the `CommandText` property.
