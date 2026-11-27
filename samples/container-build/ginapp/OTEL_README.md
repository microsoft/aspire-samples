# OpenTelemetry in the Gin feature flag service

The `ginapp` service sends traces, metrics, and structured logs to the Aspire dashboard over OTLP. This page explains how it's wired; the [sample README](../README.md#what-to-explore-in-the-aspire-dashboard) shows what to look at.

## Setup ([telemetry.go](./telemetry.go))

- **Configuration comes from the environment.** Aspire's `WithOtlpExporter()` sets `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` (the dashboard API key), `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, and short export intervals (`OTEL_BSP_SCHEDULE_DELAY`, `OTEL_BLRP_SCHEDULE_DELAY`, `OTEL_METRIC_EXPORT_INTERVAL`). The Go SDK and the OTLP gRPC exporters read all of them, so there's no hard-coded endpoint.
- **TLS is verified.** The dashboard's OTLP endpoint uses the ASP.NET Core development certificate. `WithDeveloperCertificateTrust(trust: true)` copies that certificate into the container and sets `SSL_CERT_DIR`, so Go's TLS stack trusts it. Don't skip certificate verification.
- **Telemetry is optional.** Without `OTEL_EXPORTER_OTLP_ENDPOINT` (or with `OTEL_SDK_DISABLED=true`) the app logs to stdout only, which is handy for `go run .` outside Aspire.
- **Shutdown flushes.** On `SIGINT`/`SIGTERM` the server drains in-flight requests and then flushes the tracer, meter, and logger providers. Air sends `SIGINT` before each hot-reload restart (`send_interrupt = true` in [.air.toml](./.air.toml)), so no telemetry is lost while you edit.

## Traces

- [`otelgin`](https://pkg.go.dev/go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin) creates a server span per request, named `METHOD /route` (for example `POST /flags/:key/evaluate`), and extracts the incoming W3C `traceparent`. `/health` probes are filtered out.
- Domain spans come from the `Contoso.FeatureFlags` tracer: `evaluate flags`, `evaluate flag`, `create flag`, `update flag`, `delete flag`, `advance rollout`, `reset flags`, `simulate storefront traffic`, and `storefront visit`.
- Every evaluation adds a `feature_flag.evaluation` span event with the [feature flag semantic conventions](https://opentelemetry.io/docs/specs/semconv/feature-flags/): `feature_flag.key`, `feature_flag.result.variant`, `feature_flag.result.reason`, `feature_flag.result.value`, `feature_flag.version`, and `feature_flag.provider.name`. The span also carries the evaluation context (`feature_flag.context.id` and `featureflags.context.*`).
- The traffic simulator calls the API through an [`otelhttp`](https://pkg.go.dev/go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp) transport, which creates client spans and injects `traceparent`, so one command produces one end-to-end trace.

## Metrics

| Instrument | Type | Unit | Attributes |
|---|---|---|---|
| `featureflags.evaluations` | Counter | `{evaluation}` | `feature_flag.key`, `feature_flag.result.variant`, `feature_flag.result.reason`, `error.type` |
| `featureflags.evaluation.duration` | Histogram | `ms` | `featureflags.request.kind` (`single`, `bulk`) |
| `featureflags.changes` | Counter | `{change}` | `featureflags.change.type` (`created`, `updated`, `deleted`, `rollout_advanced`, `rejected`, `reset`) |
| `featureflags.rollout.percentage` | Observable gauge | `%` | `feature_flag.key` |

Attributes have bounded cardinality: flag keys come from the catalog (a request for an unknown key is counted without its key), and variants and reasons come from flag definitions and a fixed list. Shopper IDs only appear on spans. `otelgin` and `otelhttp` add the standard `http.server.*` and `http.client.*` metrics, and histograms carry exemplars that link to traces.

## Logs

`log/slog` writes to two handlers: a text handler on stdout (the dashboard's **Console logs**) and the [`otelslog`](https://pkg.go.dev/go.opentelemetry.io/contrib/bridges/otelslog) bridge (the dashboard's **Structured logs**). Calls such as `logger.InfoContext(ctx, "Rollout advanced", "feature_flag.key", key, "from_percentage", from, "to_percentage", to)` keep their key-value pairs as attributes and carry the active trace and span IDs.

## Environment variables

| Variable | Set by | Purpose |
|---|---|---|
| `PORT` | AppHost (`WithHttpEndpoint(targetPort: 5555, env: "PORT")`) | Port to listen on. Required. |
| `OTEL_*`, `SSL_CERT_DIR` | AppHost (`WithOtlpExporter`, `WithDeveloperCertificateTrust`) | OTLP export and certificate trust. |
| `FLAGS_ADMIN_ENABLED` | AppHost, run mode only | Enables `POST /admin/simulate` and `POST /admin/reset`, which back the dashboard commands. |
| `GIN_MODE`, `TRUSTED_PROXIES` | AppHost, publish mode only | Release mode, and which reverse proxies may set `X-Forwarded-*` headers (`all`, or networks separated by `;`). |

To run the service without Aspire:

```bash
cd ginapp
PORT=8080 FLAGS_ADMIN_ENABLED=true go run .
curl -X POST localhost:8080/evaluate -H 'Content-Type: application/json' \
  -d '{"context":{"targetingKey":"shopper-48213","attributes":{"plan":"premium","country":"US","platform":"ios"}}}'
```
