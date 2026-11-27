# Aspire Shop

![Aspire Shop storefront showing the product catalog with hoodies, mugs and T-shirts, and a cart badge holding four items (light theme)](./images/aspire-shop-app-light.png#gh-light-mode-only)
![Aspire Shop storefront showing the product catalog with hoodies, mugs and T-shirts, and a cart badge holding four items (dark theme)](./images/aspire-shop-app-dark.png#gh-dark-mode-only)

Browse a product catalog served by the Catalog service, fill a cart held by the Basket service, and check out. Built with [Aspire](https://aspire.dev).

The sample also shows what Aspire gives you while you build: the Catalog API is documented with OpenAPI and Scalar and linked from the dashboard, every service emits domain traces, metrics, and structured logs, and dashboard commands reset the catalog database or simulate a stream of shoppers so the telemetry pages fill up in seconds.

![Aspire Shop order confirmation listing the ordered hoodie, mugs and T-shirt next to an order summary with the order number, item count and a $102.00 total (light theme)](./images/aspire-shop-checkout-light.png#gh-light-mode-only)
![Aspire Shop order confirmation listing the ordered hoodie, mugs and T-shirt next to an order summary with the order number, item count and a $102.00 total (dark theme)](./images/aspire-shop-checkout-dark.png#gh-dark-mode-only)

The app consists of four .NET services:

- **AspireShop.Frontend**: An ASP.NET Core Blazor app that displays a paginated catalog of products, lets users add products to a shopping cart, and checks the cart out into an order.
- **AspireShop.CatalogService**: An HTTP API that provides access to the catalog of products stored in a PostgreSQL database. It's documented with OpenAPI and [Scalar](https://scalar.com) and returns [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details for errors.
- **AspireShop.CatalogDbManager**: An HTTP API that applies the EF Core migrations to the catalog database and seeds it with products.
- **AspireShop.BasketService**: A gRPC service that provides access to the shopping cart stored in Redis and turns it into an order at checkout.

The app also includes a .NET class library project, **AspireShop.ServiceDefaults**, that contains the code-based defaults used by the .NET service projects, including OpenTelemetry registration for every `AspireShop.*` meter and activity source.

## Prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `AspireShop.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `AspireShop.slnx` and launch/debug the `AspireShop.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `AspireShop.AppHost` directory.

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with every AspireShop resource running and the frontend actions menu open on the Simulate shopper traffic command and its description (light theme)](./images/aspire-shop-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with every AspireShop resource running and the frontend actions menu open on the Simulate shopper traffic command and its description (dark theme)](./images/aspire-shop-dashboard-resources-dark.png#gh-dark-mode-only)

- On the **frontend** resource, run **Simulate shopper traffic**. It starts 20 simulated shoppers who browse one or two catalog pages, add items to a basket, and then check out, abandon the basket, or leave it open. The command result is a JSON summary such as `{"shoppers":20,"ordersPlaced":15,"basketsAbandoned":4,"basketsLeftOpen":1,"failed":0,"revenue":1264.0}`. The endpoint behind it, `POST /dev/simulate-traffic`, is only mapped in development.
- On the **catalogservice** resource, open **API reference (Scalar)** to try the Catalog API, or **OpenAPI JSON** to see the raw document. The raw endpoint URLs are listed on the resource details page.
- On the **catalogdbmanager** resource, run **Reset database** to return the catalog to its seeded state. After you confirm, the development-only `POST /reset-db` endpoint reverts every EF Core migration, and the command then restarts the resource through `ResourceCommandService`, so the manager re-applies the migrations and re-seeds the products on startup.
- The **postgres** resource has pgAdmin and the **basketcache** resource has Redis Commander, for inspecting the underlying data.

### API reference

![Scalar API reference for the Aspire Shop Catalog API showing the List catalog items operation with its documented query parameters, 200 and 400 responses, and an example page of products (light theme)](./images/aspire-shop-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Aspire Shop Catalog API showing the List catalog items operation with its documented query parameters, 200 and 400 responses, and an example page of products (dark theme)](./images/aspire-shop-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/v1/catalog/items/type/all` | A page of catalog items, using keyset pagination (`before`, `after`, and `pageSize` from 1 to 50) |
| `GET` | `/api/v1/catalog/items/type/all/brand/{catalogBrandId}` | A page of catalog items for one brand |
| `GET` | `/api/v1/catalog/items/{catalogItemId}` | A single catalog item, including its brand and type |
| `GET` | `/api/v1/catalog/items/{catalogItemId}/image` | The product image as a PNG |

Invalid paging (for example, both `before` and `after`) returns a `400` validation problem, and an unknown item returns a `404` problem. `AspireShop.CatalogService/CatalogService.http` has a request for each case.

### Traces

![Aspire dashboard trace of a simulated shopper session that spans the frontend, the catalog service and PostgreSQL, and the basket service and Redis, with the checkout basket span selected to show its order attributes and events (light theme)](./images/aspire-shop-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of a simulated shopper session that spans the frontend, the catalog service and PostgreSQL, and the basket service and Redis, with the checkout basket span selected to show its order attributes and events (dark theme)](./images/aspire-shop-trace-dark.png#gh-dark-mode-only)

- **simulated shopper session**: each simulated shopper gets its own trace, linked to the command request that started the simulation. It shows the shopper browsing the catalog (frontend → catalog service → PostgreSQL), adding items to the cart (frontend → gRPC → basket service → Redis), and checking out.
- **checkout**: the frontend `checkout` span wraps the `BasketApi.Basket/CheckoutBasket` gRPC call. In the basket service, the `checkout basket` span carries `aspireshop.buyer.id`, `aspireshop.order.id`, `aspireshop.order.item_count`, `aspireshop.order.total`, and `aspireshop.checkout.outcome`, and records the `basket.loaded`, `order.created`, and `basket.cleared` events around the Redis `GET` and `UNLINK` commands.
- **add to cart**: the frontend span covers reading the basket and the gRPC `UpdateBasket` call. The basket service's `update basket` span records a `basket.changed` event with the number of items added and removed.
- **Catalog requests**: the catalog service tags its request spans with `aspireshop.catalog.filter`, `aspireshop.catalog.page.direction`, `aspireshop.catalog.page.size`, and `aspireshop.catalog.page.item_count`.
- **initialize catalog database**: the catalog manager's startup trace covers the EF Core migrations and the `seed catalog` span.

### Metrics

![Aspire dashboard metrics page charting aspireshop.basket.order.value P50, P90 and P99 in USD with exemplars under simulated shopper traffic (light theme)](./images/aspire-shop-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting aspireshop.basket.order.value P50, P90 and P99 in USD with exemplars under simulated shopper traffic (dark theme)](./images/aspire-shop-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `AspireShop.Catalog` | `aspireshop.catalog.pages.served` | Catalog pages served, by filter and page direction |
| `AspireShop.Catalog` | `aspireshop.catalog.page.items` | Histogram of items per page |
| `AspireShop.Catalog` | `aspireshop.catalog.item.lookups` | Single-item lookups, by whether the item was found |
| `AspireShop.Basket` | `aspireshop.basket.items.added` / `aspireshop.basket.items.removed` | Units added to and removed from baskets |
| `AspireShop.Basket` | `aspireshop.basket.active` | Up-down counter of baskets created by the service that haven't been checked out or deleted |
| `AspireShop.Basket` | `aspireshop.basket.checkouts` | Checkout attempts, by outcome (`completed`, `empty`, `not_found`) |
| `AspireShop.Basket` | `aspireshop.basket.order.value` | Histogram of order totals, in USD |
| `AspireShop.Frontend` | `aspireshop.frontend.catalog.page_views` | Catalog pages requested by the storefront, by page direction |
| `AspireShop.Frontend` | `aspireshop.frontend.checkout.duration` | Histogram of end-to-end checkout time, in seconds, by outcome |
| `AspireShop.Frontend` | `aspireshop.frontend.shopper_sessions.active` | Simulated shopper sessions in progress |

Metric tags are limited to bounded values such as the page direction and checkout outcome. Buyer and order IDs only appear on spans and logs. The histograms record inside the request's span, so their exemplars link to the trace that produced each point.

### Structured logs

![Aspire dashboard structured logs for the basket service filtered to created orders, with an OrderCreated entry open to show its OrderId, BuyerId, ItemCount and OrderTotal properties and trace link (light theme)](./images/aspire-shop-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs for the basket service filtered to created orders, with an OrderCreated entry open to show its OrderId, BuyerId, ItemCount and OrderTotal properties and trace link (dark theme)](./images/aspire-shop-logs-dark.png#gh-dark-mode-only)

Each service logs its key events with source-generated `[LoggerMessage]` methods (`Log.cs` in each project), so values such as `OrderId`, `BuyerId`, `ItemCount`, `OrderTotal`, `CatalogFilter`, `PageDirection`, and `ShopperOutcome` arrive as properties you can filter on. Each entry links to the trace that produced it.
