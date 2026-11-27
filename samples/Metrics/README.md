# Aspire metrics sample app

This sample shows how to collect OpenTelemetry metrics from a .NET app with Aspire, export them through an OpenTelemetry Collector to Prometheus, and chart them in Grafana, while the same metrics, traces, and logs also show up in the Aspire dashboard.

The instrumented app is the checkout service for **Contoso Outfitters**, a small outdoor-gear store. Every cart and checkout records domain metrics (orders, revenue, checkout latency, open carts) next to the built-in ASP.NET Core metrics, and a Grafana dashboard turns them into a live business view.

![Contoso Outfitters checkout console with order, revenue, average order, declined payment and open cart totals above a live table of recent checkouts with their payment method, total and checkout time (light theme)](./images/metrics-app-light.png#gh-light-mode-only)
![Contoso Outfitters checkout console with order, revenue, average order, declined payment and open cart totals above a live table of recent checkouts with their payment method, total and checkout time (dark theme)](./images/metrics-app-dark.png#gh-dark-mode-only)

The app is a Blazor WebAssembly client hosted by an ASP.NET Core API. **Live orders** shows the checkout totals and the latest checkouts, refreshed every two seconds. **Shop** lets you fill a cart, pick shipping and payment, and place an order. Carts are anonymous, but checkout calls a protected endpoint, so you sign in first with the demo customer (`test@contoso.com` / `Password1!`, created in Development only). The UI supports light and dark themes.

![Contoso Outfitters shop with outdoor gear product cards next to an order confirmation listing a rain shell, a backpack and two headlamps with subtotal, express shipping, sales tax and the total charged (light theme)](./images/metrics-checkout-light.png#gh-light-mode-only)
![Contoso Outfitters shop with outdoor gear product cards next to an order confirmation listing a rain shell, a backpack and two headlamps with subtotal, express shipping, sales tax and the total charged (dark theme)](./images/metrics-checkout-dark.png#gh-dark-mode-only)

## Demonstrates

- How to record domain metrics with `IMeterFactory` (a counter, histograms with units and explicit buckets, and an up-down counter) and register every `MetricsApp.*` meter and activity source in ServiceDefaults
- How to add a custom OpenTelemetry Collector resource that forwards telemetry to the Aspire dashboard and exports metrics to Prometheus over OTLP
- How to add Prometheus and Grafana containers to an Aspire app, with health checks, `WaitFor`, and provisioned dashboards, including the [.NET Grafana dashboards](https://aka.ms/dotnet/grafana-source)
- How histogram exemplars link a Grafana or Aspire dashboard data point to the trace that produced it
- How to document a minimal API with OpenAPI and Scalar, link it from the dashboard, and add dashboard commands with `WithHttpCommand`

## Sample prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- This sample is written in C# and targets .NET 10. It requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

## Running the sample

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `MetricsApp.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `Metrics.slnx` and launch/debug the `MetricsApp.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `MetricsApp.AppHost` directory.

1. On the **Resources** page, run **Simulate checkout traffic** on the `app` resource a few times. Each run sends 24 simulated shoppers through the checkout API.
1. Open **Checkout console** on the `app` resource to watch the orders arrive, or shop and check out yourself.
1. Open **Checkout dashboard** on the `grafana` resource (or click **Open Grafana dashboard** in the console) to see the same activity in Grafana.
1. Run **Simulate card issuer outage**, then **Simulate checkout traffic** again, and watch card latency and declined payments climb in Grafana and on the Aspire dashboard's **Metrics** page.

For more information about using Grafana dashboards, see the [Grafana documentation](https://grafana.com/docs/grafana/latest/dashboards/use-dashboards/).

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with the app, Grafana, Prometheus and OpenTelemetry Collector resources running, and the app actions menu open on the Simulate checkout traffic command and its description (light theme)](./images/metrics-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with the app, Grafana, Prometheus and OpenTelemetry Collector resources running, and the app actions menu open on the Simulate checkout traffic command and its description (dark theme)](./images/metrics-dashboard-resources-dark.png#gh-dark-mode-only)

- **app**: the checkout service. Its links are **Checkout console**, **API reference (Scalar)**, and **OpenAPI JSON**; the raw endpoint URLs are on the resource details page. It has two commands, backed by development-only endpoints:
  - **Simulate checkout traffic** (`POST /dev/simulate-checkouts`) signs in as the demo customer and runs 24 shoppers through the public API: each one browses the catalog, fills a cart, and then checks out, abandons the cart, or leaves it open. The result is a JSON summary such as `{"shoppers":24,"ordersPlaced":18,"paymentsDeclined":1,"cartsAbandoned":3,"cartsLeftOpen":2,"failed":0,"revenue":6328.41}`.
  - **Simulate card issuer outage** (`POST /dev/card-issuer-outage`) asks for confirmation, then makes card payments take 1–3 seconds and declines about a third of them for two minutes.
- **otelcollector**: the custom OpenTelemetry Collector resource (see `MetricsApp.AppHost/OpenTelemetryCollector`). Every resource's `OTEL_EXPORTER_OTLP_ENDPOINT` points at it; it forwards traces, logs, and metrics to the Aspire dashboard and also exports metrics to Prometheus. Its health check uses the collector's `health_check` extension.
- **prometheus**: stores the metrics it receives on its OTLP endpoint (`--web.enable-otlp-receiver`), including histogram exemplars (`--enable-feature=exemplar-storage`).
- **grafana**: provisioned with the Prometheus data source and the **Checkout**, **ASP.NET Core**, and **ASP.NET Core Endpoint** dashboards. **Checkout dashboard** opens the checkout dashboard.

### API reference

![Scalar API reference for the Contoso Outfitters Checkout API showing the Place an order operation with its bearer authentication, payment and shipping enums, 201 to 409 responses and an example order confirmation (light theme)](./images/metrics-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Outfitters Checkout API showing the Place an order operation with its bearer authentication, payment and shipping enums, 201 to 409 responses and an example order confirmation (dark theme)](./images/metrics-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/catalog` | The products you can add to a cart |
| `POST` | `/api/carts` | Create an anonymous cart |
| `GET` | `/api/carts/{cartId}` | A cart's lines and subtotal |
| `PUT` | `/api/carts/{cartId}/items/{sku}` | Set a product's quantity (0–10; 0 removes it) |
| `DELETE` | `/api/carts/{cartId}` | Abandon a cart |
| `POST` | `/api/carts/{cartId}/checkout` | Place an order (requires a bearer token from `POST /api/login`) |
| `GET` | `/api/orders/overview` | Totals since the service started and the latest checkouts |

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details: `400` for an invalid quantity, `402` for a declined payment, `404` for an unknown cart or product, and `409` for checking out an empty cart or touching a cart while it's being checked out. The ASP.NET Core Identity endpoints (`/api/login` and others) are listed under **Account**.

### Traces

![Aspire dashboard trace of a simulated shopper that browses the catalog, creates a cart, adds three products and checks out during the card issuer outage, with a 3-second authorize payment span and the place order span selected to show its cart, order and payment attributes and its cart.priced and payment.authorized events (light theme)](./images/metrics-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of a simulated shopper that browses the catalog, creates a cart, adds three products and checks out during the card issuer outage, with a 3-second authorize payment span and the place order span selected to show its cart, order and payment attributes and its cart.priced and payment.authorized events (dark theme)](./images/metrics-trace-dark.png#gh-dark-mode-only)

- **simulated shopper**: each simulated shopper gets its own trace, linked to the command request that started the simulation. It contains the HTTP client and server spans for every API call the shopper makes.
- **place order**: the checkout span carries `checkout.cart.id`, `checkout.customer.id`, `checkout.order.id`, `checkout.order.item_count`, `checkout.order.total`, `checkout.payment.method`, `checkout.shipping.method`, and `checkout.outcome`, and records `cart.priced`, `payment.authorized` or `payment.declined`, and `order.confirmed` events.
- **authorize payment**: the simulated payment provider call, with the payment method, amount, outcome, and decline reason. During a card issuer outage, declined card payments are marked as errors.

### Metrics

![Aspire dashboard metrics page charting checkout.duration P50, P90 and P99 for the MetricsApp.Checkout meter, with exemplar points and a latency spike during the simulated card issuer outage (light theme)](./images/metrics-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting checkout.duration P50, P90 and P99 for the MetricsApp.Checkout meter, with exemplar points and a latency spike during the simulated card issuer outage (dark theme)](./images/metrics-metrics-dark.png#gh-dark-mode-only)

The `MetricsApp.Checkout` meter (`MetricsApp/Checkout/CheckoutTelemetry.cs`) has these instruments:

| Instrument | Type | Description |
| --- | --- | --- |
| `checkout.orders` | Counter (`{order}`) | Checkout attempts, by `checkout.outcome` (`completed`, `payment_declined`, `empty_cart`) and `checkout.payment.method` |
| `checkout.order.value` | Histogram (`USD`) | Value of placed orders, by payment and shipping method |
| `checkout.duration` | Histogram (`s`) | Server-side checkout time, by outcome and payment method |
| `checkout.carts.active` | Up-down counter (`{cart}`) | Carts that haven't been checked out or abandoned yet |
| `checkout.cart.items.added` | Counter (`{item}`) | Units added to carts, by `checkout.product.category` |

Tags are limited to bounded values. Cart, order, and customer IDs only appear on spans and logs. The histograms are recorded inside the checkout span, so their exemplars link back to the trace. The app also exports the built-in ASP.NET Core metrics, including the .NET 10 authentication, authorization, and Identity meters (`aspnetcore.authorization.attempts`, `aspnetcore.identity.sign_in.sign_ins`).

### Structured logs

The app logs its key events with source-generated `[LoggerMessage]` methods (`MetricsApp/Log.cs`), so values such as `OrderId`, `CartId`, `ItemCount`, `OrderTotal`, `PaymentMethod`, `ShippingMethod`, and `DeclineReason` arrive as properties you can filter on. For example, filter the **Structured logs** page to `Placed order` or `Payment declined`, then open the trace linked from an entry.

## Explore the metrics in Grafana

![Grafana Checkout dashboard with orders, revenue, average order value, decline rate, open carts and p95 latency stats above charts of orders per minute and p95 checkout latency by payment method, revenue per minute, declined payments, the order value distribution, open carts, units by category and sign-ins (light theme)](./images/metrics-grafana-light.png#gh-light-mode-only)
![Grafana Checkout dashboard with orders, revenue, average order value, decline rate, open carts and p95 latency stats above charts of orders per minute and p95 checkout latency by payment method, revenue per minute, declined payments, the order value distribution, open carts, units by category and sign-ins (dark theme)](./images/metrics-grafana-dark.png#gh-dark-mode-only)

The **Checkout** dashboard (`grafana/dashboards/checkout.json`) is Grafana's home dashboard. It queries the metrics that the collector exported to Prometheus, where the OpenTelemetry names become Prometheus names such as `checkout_orders_total`, `checkout_order_value_USD_bucket`, and `checkout_duration_seconds_bucket`.

- The stats at the top are totals since the checkout service started, so they match the console's **Live orders** page.
- **p95 checkout latency by payment method** shows exemplars as small diamonds. Hover over one and select **View trace in the Aspire dashboard** to open the trace that recorded it. The link uses the dashboard URL that the AppHost is configured with, so it doesn't work when you start the AppHost with `aspire run --isolated`, which picks random ports.
- The **ASP.NET Core** link opens the .NET team's ASP.NET Core dashboard for the same app, including request duration, error rate, and the busiest endpoints.

Change the time range, enable auto-refresh, or edit the panels to try your own PromQL queries.
