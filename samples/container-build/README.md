# Working with container-built resources in an Aspire application

This sample demonstrates integrating applications into an Aspire app via Dockerfiles and container-based builds. This is especially helpful to integrate applications written in languages that Aspire does not have a native integration for, or to reduce the prerequisites required to run the application. You don't need Go installed: Docker builds the app.

![Aspire dashboard resources page with the ginapp container built from a Dockerfile, its Feature flags API, API reference and OpenAPI links, and the actions menu open on the highlighted Simulate traffic command (light theme)](./images/container-build-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with the ginapp container built from a Dockerfile, its Feature flags API, API reference and OpenAPI links, and the actions menu open on the highlighted Simulate traffic command (dark theme)](./images/container-build-dashboard-resources-dark.png#gh-dark-mode-only)

The sample integrates an app written using [Go](https://go.dev/) and the [Gin Web Framework](https://gin-gonic.com/) by using a [Dockerfile](./ginapp/Dockerfile):

- **ginapp**: The feature flag service behind the **Contoso Outfitters** storefront. It stores flags with variants, targeting rules, and percentage rollouts, and evaluates them for shoppers with [OpenFeature](https://openfeature.dev/)-style results (`variant`, `value`, and `reason`). It documents its API with OpenAPI and [Scalar](https://scalar.com/), and sends traces, metrics, and structured logs to the Aspire dashboard with OpenTelemetry (see [OTEL_README.md](./ginapp/OTEL_README.md)).

The AppHost ([apphost.cs](./apphost.cs)) is a file-based C# app. It models `ginapp` as a container resource built from a Dockerfile, passes the `goversion` parameter as the `GO_VERSION` build argument, and adds dashboard links and commands:

```csharp
ginapp = builder.AddDockerfile("ginapp", "./ginapp", "Dockerfile.dev")
    .WithBuildArg("GO_VERSION", goVersion)
    .WithBindMount("./ginapp", "/app");

ginapp
    .WithHttpEndpoint(targetPort: 5555, env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithOtlpExporter()
    .WithDeveloperCertificateTrust(trust: true)
    .WithUrls(context =>
    {
        var endpoint = context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    })
    .WithHttpCommand("/admin/simulate", "Simulate traffic", commandName: "simulate-traffic", commandOptions: new()
    {
        IconName = "Play",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });
```

## Development Features

This sample includes **hot reload** for local development! The Go application uses:

- **Bind mounts** - Your local source code is mounted directly into the container at `/app`
- **[Air](https://github.com/air-verse/air)** - A live reload tool for Go that watches for file changes and rebuilds automatically
- **Polling-based file watching** - Configured to work reliably with Docker bind mounts on Windows

When you edit a `.go` file, `openapi.json`, or `scalar.html` in the `ginapp` directory, Air automatically detects the change, rebuilds the Go binary, and restarts the app in just a few seconds—without rebuilding the entire container. Air sends `SIGINT` first, so the app drains in-flight requests and flushes its telemetry before it restarts. This provides a much faster development feedback loop compared to full container rebuilds.

### How it works

- **Development mode** (default): Uses `Dockerfile.dev` with Air for hot reload
  - Source files are bind-mounted from your local machine
  - Changes to Go, JSON, and HTML files trigger automatic rebuilds
  - Go modules are downloaded into the image when it's built, so the container starts without fetching dependencies
  - The AppHost enables development-only admin endpoints (`FLAGS_ADMIN_ENABLED=true`) that back the dashboard commands

- **Production mode** (`aspire publish`): Uses the standard `Dockerfile`
  - Multi-stage build for optimized images
  - No bind mounts, development tools, or admin endpoints
  - Minimal, non-root runtime container based on [Azure Linux distroless](https://github.com/microsoft/azurelinux) images

## Pre-requisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/), including a container runtime such as Docker Desktop or Podman
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `apphost.cs` file using either the Aspire or C# debuggers.

If using the .NET CLI, run `dotnet run apphost.cs` from this directory.

The first run builds the `ginapp` image, so it takes a minute longer. From the Aspire dashboard, run the **Simulate traffic** command on the `ginapp` resource, then open **API reference (Scalar)** to browse and try the API.

## The feature flags API

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/evaluate` | Evaluates every flag, or the keys listed in `flags`, for one shopper. Unknown keys come back with the reason `ERROR`. |
| `POST` | `/flags/{key}/evaluate` | Evaluates one flag |
| `GET` | `/flags` | Lists the flag catalog (filter with `?enabled=true` or `false`) |
| `POST` | `/flags` | Creates a flag |
| `GET`, `PATCH`, `DELETE` | `/flags/{key}` | Reads, partially updates (for example the `enabled` kill switch), or deletes a flag |
| `POST` | `/flags/{key}/rollout/advance` | Moves a two-variant rollout to its next stage: 5, 10, 25, 50, then 100% |
| `POST` | `/admin/simulate`, `/admin/reset` | Development only: generates storefront traffic or restores the seeded flags (used by the dashboard commands) |

A flag resolves in this order: a disabled flag serves its `offVariant` (`DISABLED`), the first matching targeting rule wins (`TARGETING_MATCH`), a percentage rollout hashes the flag key and the shopper's `targetingKey` into a sticky bucket (`SPLIT`), and otherwise the flag serves its `defaultVariant` (`DEFAULT` or `STATIC`). The seeded catalog has eight storefront flags, such as `checkout.express-pay` (a 10% rollout of express checkout), `catalog.recommendations` (an A/B/n experiment), and `loyalty.points-multiplier` (targeted by plan and country).

```http
POST /evaluate
Content-Type: application/json

{
  "context": {
    "targetingKey": "shopper-48213",
    "attributes": { "plan": "premium", "country": "US", "platform": "ios" }
  }
}
```

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details (`application/problem+json`): `400` with per-field `errors`, `404` for an unknown flag, `409` for a duplicate key or a rollout that can't advance, plus `413` and `415`. Each one includes the `traceId` of the failing request. The root URL redirects to the API reference at `/scalar`, and the OpenAPI 3.1 document is at `/openapi.json`.

![Scalar API reference for the Contoso Feature Flags API on the Evaluate flags for a shopper operation, with the evaluation context schema, request examples and an example response (light theme)](./images/container-build-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Feature Flags API on the Evaluate flags for a shopper operation, with the evaluation context schema, request examples and an example response (dark theme)](./images/container-build-scalar-dark.png#gh-dark-mode-only)

## What to explore in the Aspire dashboard

### Resources and commands

- **ginapp** is a **Container** resource. Its image is built from `ginapp/Dockerfile.dev` and tagged with a content hash, and the **goversion** parameter is listed under **Parameters**.
- Its URLs are **Feature flags API**, **API reference (Scalar)**, and **OpenAPI JSON**. The health check probes `/health`.
- Commands (run mode only):
  - **Simulate traffic** sends 40 storefront shoppers (home, product, and checkout page views from the US, Europe, Japan, Brazil, and Australia, on web, iOS, and Android) through the API and returns a JSON summary of the variants served. Some shoppers use an outdated iOS app that still asks for the retired `checkout.gift-wrap-upsell` flag, which produces realistic `404` errors.
  - **Advance express-pay rollout** moves `checkout.express-pay` to its next rollout stage, after a confirmation.
  - **Reset flags** restores the seeded catalog, after a confirmation.

### Console logs

![Aspire dashboard console logs for ginapp showing Air detecting a change to catalog.go, rebuilding the Go binary, and the restarted feature flag service listening again (light theme)](./images/container-build-hot-reload-light.png#gh-light-mode-only)
![Aspire dashboard console logs for ginapp showing Air detecting a change to catalog.go, rebuilding the Go binary, and the restarted feature flag service listening again (dark theme)](./images/container-build-hot-reload-dark.png#gh-dark-mode-only)

Edit a file under `ginapp` and watch the **Console logs** page: Air reports the change, rebuilds, gracefully stops the old process, and starts the new one in the same container.

### Traces

![Aspire dashboard trace of one Simulate traffic run, from the simulate storefront traffic span through storefront visits, HTTP client and Gin server spans, to evaluate flags spans, with the span details showing the shopper context and feature_flag.evaluation events (light theme)](./images/container-build-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of one Simulate traffic run, from the simulate storefront traffic span through storefront visits, HTTP client and Gin server spans, to evaluate flags spans, with the span details showing the shopper context and feature_flag.evaluation events (dark theme)](./images/container-build-trace-dark.png#gh-dark-mode-only)

- A **Simulate traffic** run is one trace: `POST /admin/simulate` → `simulate storefront traffic` → one `storefront visit` per shopper → `POST /evaluate` (an `otelhttp` client span that propagates the W3C `traceparent` header) → `POST /evaluate` (the `otelgin` server span) → `evaluate flags`.
- `evaluate flags` and `evaluate flag` spans carry the evaluation context (`feature_flag.context.id`, `featureflags.context.plan`, `.country`, and `.platform`) and one `feature_flag.evaluation` event per flag with the [feature flag semantic conventions](https://opentelemetry.io/docs/specs/semconv/feature-flags/): `feature_flag.key`, `feature_flag.result.variant`, `feature_flag.result.reason`, and `feature_flag.result.value`.
- `create flag`, `update flag`, `advance rollout`, and `delete flag` spans record what changed. Requests for the retired flag show up as error spans, and the `traceId` in a problem details response finds its trace.

### Metrics

![Aspire dashboard metrics page for the Contoso.FeatureFlags meter charting featureflags.rollout.percentage for checkout.express-pay as the rollout steps from 10 to 25, 50 and 100 percent (light theme)](./images/container-build-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page for the Contoso.FeatureFlags meter charting featureflags.rollout.percentage for checkout.express-pay as the rollout steps from 10 to 25, 50 and 100 percent (dark theme)](./images/container-build-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `Contoso.FeatureFlags` | `featureflags.evaluations` | Evaluations served, by `feature_flag.key`, `feature_flag.result.variant`, `feature_flag.result.reason`, and `error.type` |
| `Contoso.FeatureFlags` | `featureflags.evaluation.duration` | Histogram of the time to resolve one request's flags, in milliseconds, by `featureflags.request.kind` (`single` or `bulk`) |
| `Contoso.FeatureFlags` | `featureflags.changes` | Catalog changes, by `featureflags.change.type` (`created`, `updated`, `deleted`, `rollout_advanced`, `rejected`, or `reset`) |
| `Contoso.FeatureFlags` | `featureflags.rollout.percentage` | Observable gauge of the traffic share each flag's rollout sends to a non-default variant, by `feature_flag.key` |
| `otelgin`, `otelhttp` | `http.server.*`, `http.client.*` | Standard HTTP server and client metrics |

Metric attributes have bounded cardinality: shopper IDs only appear on spans, and a request for an unknown flag is counted without its key.

### Structured logs

![Aspire dashboard structured logs for ginapp filtered to the flag catalog audit trail (flag created, invalid definition rejected, rollouts advanced, a kill-switch warning, catalog resets), with the details of a Rollout advanced entry from 50 to 100 percent linked to its trace (light theme)](./images/container-build-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs for ginapp filtered to the flag catalog audit trail (flag created, invalid definition rejected, rollouts advanced, a kill-switch warning, catalog resets), with the details of a Rollout advanced entry from 50 to 100 percent linked to its trace (dark theme)](./images/container-build-logs-dark.png#gh-dark-mode-only)

The app logs with `log/slog` through the [`otelslog`](https://pkg.go.dev/go.opentelemetry.io/contrib/bridges/otelslog) bridge, so every key-value pair is a filterable field and every entry links to its trace. The logs form an audit trail of the flag catalog: `Feature flag created`, `Feature flag updated` (with `changed_fields` and `version`), `Rollout advanced` (with `from_percentage` and `to_percentage`), a warning when a kill switch disables a flag, `Rejected invalid flag definition` (with `invalid_fields`), `Unknown feature flag requested`, and a `Simulated storefront traffic` summary per command run. To see just the audit trail, add filters that exclude messages containing `Unknown` and `Simulated`.
