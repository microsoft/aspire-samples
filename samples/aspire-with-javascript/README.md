# Integrating Angular, React, and Vue with Aspire

This sample demonstrates using the Aspire JavaScript hosting integration to configure and run client-side applications, and to trace a click in the browser all the way through a .NET API.

The scenario is **Contoso shipment tracking**: a .NET API serves shipments (origin, destination, carrier, status, ETA, weight, route progress, and the last scan), and four front ends render the same tracking board with status filters and a shipment detail view.

The app consists of five services:

- **shipmentsapi** (`AspireJavaScript.MinimalApi`): An HTTP API that exposes `GET /api/shipments?status=…` and `GET /api/shipments/{id}`, with RFC 9457 problem details for unknown (`404`) and malformed (`400`) requests. It has full OpenAPI metadata, a Scalar API reference, a `Contoso.Shipments` meter, domain spans, and source-generated `[LoggerMessage]` logs.
- **angular** (`AspireJavaScript.Angular`): An Angular tracking board.
- **react** (`AspireJavaScript.React`): A React (Webpack) parcel manifest.
- **vue** (`AspireJavaScript.Vue`): A Vue tracking board.
- **reactvite** (`AspireJavaScript.Vite`): A React + Vite + TypeScript tracking board, bundled into the API's `wwwroot` on publish.

The four front ends render the **same** shipment data, but each one wears a **completely different design identity**, so you can compare the frameworks side by side:

| Front end | Design identity | CSS approach | Icon set |
| --- | --- | --- | --- |
| **Angular** | Material 3 "expressive": filter chips with counts, shipment cards, a gradient route hero, and a scan timeline | Angular Material + SCSS | Material Symbols |
| **React** | Neo-brutalism: a manifest table and a printed shipping label with a rubber-stamped status | CSS Modules | Phosphor |
| **Vue** | Soft cards: route progress bars and a journey stepper | Scoped CSS + custom properties | Lucide |
| **Vite** | Retro synthwave: neon status chips and a scan log over an 80s grid horizon | Tailwind CSS | Tabler |

Every front end filters by status (Label created, In transit, Out for delivery, Delivered, Exception), has a responsive layout, supports light and dark mode through `prefers-color-scheme`, is keyboard operable, and announces async state with `aria-live`.

## Pre-requisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org) - at least version 24.x

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `AspireJavaScript.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `AspireJavaScript.slnx` and launch/debug the `AspireJavaScript.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `AspireJavaScript.AppHost` directory.

### Experiencing the app

Once the app is running, the Aspire dashboard launches in your browser. Each front end has a named link on the **Resources** page (for example, **Angular tracking board**). Select a shipment in any of them to see its scan history.

**Angular**: Material 3 expressive

![Angular tracking board with the CTS-48214101 customs exception selected, showing shipment cards, status filter chips with counts, the gradient route hero, and the scan timeline (light theme)](./images/aspire-with-javascript-angular-light.png#gh-light-mode-only)
![Angular tracking board with the CTS-48214101 customs exception selected, showing shipment cards, status filter chips with counts, the gradient route hero, and the scan timeline (dark theme)](./images/aspire-with-javascript-angular-dark.png#gh-dark-mode-only)

**React**: neo-brutalist parcel manifest

![React parcel manifest table with CTS-48213957 selected and its printed shipping label, stamped Out for delivery, showing the barcode, route, and tracking log (light theme)](./images/aspire-with-javascript-react-light.png#gh-light-mode-only)
![React parcel manifest table with CTS-48213957 selected and its printed shipping label, stamped Out for delivery, showing the barcode, route, and tracking log (dark theme)](./images/aspire-with-javascript-react-dark.png#gh-dark-mode-only)

**Vue**: soft cards and a journey stepper

![Vue tracking board with route progress bars on every card and CTS-48213922 selected, showing its journey stepper at In transit and its scan history (light theme)](./images/aspire-with-javascript-vue-light.png#gh-light-mode-only)
![Vue tracking board with route progress bars on every card and CTS-48213922 selected, showing its journey stepper at In transit and its scan history (dark theme)](./images/aspire-with-javascript-vue-dark.png#gh-dark-mode-only)

**Vite**: retro synthwave

![React and Vite tracking board with neon status chips and CTS-48214387 selected, showing a weather-delay exception at the top of its scan log (light theme)](./images/aspire-with-javascript-vite-light.png#gh-light-mode-only)
![React and Vite tracking board with neon status chips and CTS-48214387 selected, showing a weather-delay exception at the top of its scan log (dark theme)](./images/aspire-with-javascript-vite-dark.png#gh-dark-mode-only)

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with every resource running, the front ends' named tracking board links, and the shipmentsapi actions menu open on the Simulate tracking lookups command and its description (light theme)](./images/aspire-with-javascript-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with every resource running, the front ends' named tracking board links, and the shipmentsapi actions menu open on the Simulate tracking lookups command and its description (dark theme)](./images/aspire-with-javascript-dashboard-resources-dark.png#gh-dark-mode-only)

- On the **shipmentsapi** resource, run **Simulate tracking lookups**. The [HTTP command](https://aspire.dev/fundamentals/custom-resource-commands/) calls `POST /dev/simulate-lookups`, which is mapped in Development only. It simulates 10 customers who each open the board (some with a status filter) and look up 3 shipments, including mistyped tracking numbers and shipments in the `Exception` status. Each customer gets its own trace, linked to the command request. The result is a JSON summary such as `{"boardQueries":10,"lookups":30,"notFound":3,"exceptionsObserved":3,"durationMs":2868.3}`.
- The **shipmentsapi** resource links to **API reference (Scalar)** and **OpenAPI JSON**. Its raw endpoint URLs are on the resource details page.
- Each front end has a named link (**Angular tracking board**, **React tracking board**, **Vue tracking board**, and **React + Vite tracking board**) and waits for the API to be healthy before it starts.

### API reference

![Scalar API reference for the Contoso Shipment Tracking API showing the Track a shipment operation with its documented path parameter, 200, 400 and 404 responses, an example shipment, and a JavaScript fetch snippet (light theme)](./images/aspire-with-javascript-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Shipment Tracking API showing the Track a shipment operation with its documented path parameter, 200, 400 and 404 responses, an example shipment, and a JavaScript fetch snippet (dark theme)](./images/aspire-with-javascript-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/shipments` | The tracking board, with exceptions first, and counts per status. Filter with `?status=label-created`, `in-transit`, `out-for-delivery`, `delivered`, or `exception`. An unknown status returns a `400` validation problem. |
| `GET` | `/api/shipments/{id}` | A shipment and its scan history, newest first, for a tracking number such as `CTS-48214101`. An unknown tracking number returns a `404` problem, and a malformed one returns a `400`. |

Every operation has a summary, description, tag, typed responses, and examples. Scalar's code samples default to JavaScript `fetch`. `AspireJavaScript.MinimalApi/AspireJavaScript.MinimalApi.http` has a request for each case.

### Traces: browser → API

![Aspire dashboard trace that starts with the Angular track shipment span, continues through the browser fetch and the API's GET /api/shipments/{id} request, and ends in the shipments.track and carrier.track spans, with the shipments.track span selected to show its contoso.shipment attributes and the shipment.exception_observed event (light theme)](./images/aspire-with-javascript-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace that starts with the Angular track shipment span, continues through the browser fetch and the API's GET /api/shipments/{id} request, and ends in the shipments.track and carrier.track spans, with the shipments.track span selected to show its contoso.shipment attributes and the shipment.exception_observed event (dark theme)](./images/aspire-with-javascript-trace-dark.png#gh-dark-mode-only)

Select a shipment in any front end, then open **Traces**. One click produces one trace that spans the browser and the server:

```
track shipment                      (browser: UI span)
└─ HTTP GET                         (browser: fetch instrumentation)
   └─ GET /api/shipments/{id}       (shipmentsapi: ASP.NET Core)
      └─ shipments.track            (shipmentsapi: domain span)
         └─ carrier.track           (shipmentsapi: simulated carrier tracking API)
```

- `shipments.track` carries `contoso.shipment.id`, `contoso.shipment.carrier`, and `contoso.shipment.status`, and records a `shipment.not_found` or `shipment.exception_observed` event.
- `carrier.track` records a `carrier.scan_received` event with the scan's time, location, and description.
- Loading the board or choosing a status filter produces a `load shipment board` trace that ends in a `shipments.board` span tagged with `contoso.shipment.status_filter` and `contoso.shipment.result_count`.
- The browser also reports `documentLoad`, `documentFetch`, and `resourceFetch` spans for each page load.

### Metrics

![Aspire dashboard metrics page charting contoso.shipments.carrier_lookup.duration P50, P90 and P99 with exemplars from simulated tracking lookups (light theme)](./images/aspire-with-javascript-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting contoso.shipments.carrier_lookup.duration P50, P90 and P99 with exemplars from simulated tracking lookups (dark theme)](./images/aspire-with-javascript-metrics-dark.png#gh-dark-mode-only)

The `Contoso.Shipments` meter publishes:

| Instrument | Type | Description |
| --- | --- | --- |
| `contoso.shipments.queried` | Counter | Shipments returned to callers, by operation (`board` or `lookup`) |
| `contoso.shipments.status_lookups` | Counter | Board queries filtered by a status, by status |
| `contoso.shipments.exceptions_observed` | Counter | Lookups that found a shipment in the `Exception` status, by carrier |
| `contoso.shipments.carrier_lookup.duration` | Histogram (seconds) | Time spent fetching the latest scan from the carrier, by carrier |
| `contoso.shipments.active` | Observable gauge | Shipments that haven't been delivered yet, by status |

Metric tags are limited to bounded values (status, operation, and carrier); tracking numbers only appear on spans and logs. The histogram records inside the `carrier.track` span, so each exemplar links to the trace that produced it.

### Structured logs

![Aspire dashboard structured logs for shipmentsapi with a ShipmentExceptionObserved warning open to show its ShipmentId, Carrier, ScanLocation and ScanDescription properties and its trace link (light theme)](./images/aspire-with-javascript-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs for shipmentsapi with a ShipmentExceptionObserved warning open to show its ShipmentId, Carrier, ScanLocation and ScanDescription properties and its trace link (dark theme)](./images/aspire-with-javascript-logs-dark.png#gh-dark-mode-only)

The API logs board queries, lookups, unknown tracking numbers, rejected requests (warning), and shipments in the `Exception` status (warning) with source-generated `[LoggerMessage]` methods in `Shipments/Log.cs`. Each property, such as `ShipmentId`, `Carrier`, `ShipmentStatus`, `ScanLocation`, and `StatusFilter`, can be filtered on the **Structured logs** page, and every entry links to its trace.

## How browser telemetry works

Each front end loads the OpenTelemetry web SDK with **document-load** and **fetch** instrumentation, plus manual `track shipment` and `load shipment board` spans around its API calls:

- The AppHost calls `WithOtlpExporter(OtlpProtocol.HttpProtobuf)` on each front end, because browsers can't send OTLP over gRPC. The AppHost's `Properties/launchSettings.json` sets `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` so the dashboard also listens for OTLP/HTTP.
- The dev server proxies `/otlp/*` to the dashboard's OTLP/HTTP endpoint and adds the `OTEL_EXPORTER_OTLP_HEADERS` API key **server-side**, so the key never reaches the browser and the dashboard needs no CORS configuration.
- `/api/*` is also proxied to `shipmentsapi`, so the API is same-origin. The fetch instrumentation's `traceparent` header therefore reaches ASP.NET Core, and the server spans join the browser's trace.
- The SDK uses `StackContextManager`, which keeps a span active only synchronously, so each UI span starts its `fetch` before its first `await`.
- Browser telemetry only runs from the dev server under Aspire. Production builds never load the SDK, and the published containers don't proxy `/otlp`.

Each framework gets its (non-secret) settings differently:

- **Vite and Vue**: `vite.config.ts` injects them with `define` and configures the proxy.
- **React (Webpack)**: `DefinePlugin` injects them and `devServer.proxy` configures the proxy.
- **Angular**: `angular.json` can't read environment variables, so `proxy.conf.js` serves `/otel-config.json` and proxies `/otlp`. `main.ts` fetches the config in dev mode and lazy-loads `telemetry.ts`. `HttpClient` uses `withFetch()`, so the fetch instrumentation parents each request under the `track shipment` span without `zone.js` context propagation.
