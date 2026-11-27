# Contributing to Aspire Samples

Thanks for helping improve the samples. Before you start a new sample or a large change, [open an issue](https://github.com/dotnet/aspire-samples/issues/new/choose) so the team can discuss it with you.

## The sample quality bar

Every sample should show what Aspire does for a real app, not just that the app runs. Use a pragmatic, industry-realistic scenario instead of a toy (weather, counters, polls), and apply the parts of this bar that fit the sample. If you skip an item, say why in the pull request.

### API reference

- Publish an OpenAPI document with a title, description, and version, plus tags, per-operation summaries and descriptions, typed request and response schemas, and realistic examples.
- Return [RFC 7807 problem details](https://www.rfc-editor.org/rfc/rfc7807) for 400, 404, 409, and 5xx responses.
- Serve [Scalar](https://scalar.com) at `/scalar`, and surface it in the dashboard as **API reference (Scalar)** next to an **OpenAPI JSON** link.

| Stack | Use |
| --- | --- |
| .NET | `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `TypedResults`, `WithSummary`/`WithDescription`/`WithTags`/`ProducesProblem` |
| Python | FastAPI metadata and `scalar-fastapi` |
| Node.js | An OpenAPI document and `@scalar/express-api-reference` |
| Go | An `openapi.json` document served with Scalar |
| Java | springdoc annotations |

### Telemetry

- **Traces:** add domain spans (`ActivitySource` or an OpenTelemetry tracer) with semantic-convention attributes and events. Propagate W3C trace context across HTTP calls, queues, and caches so one user action produces one trace.
- **Metrics:** give each service one domain meter named `<Company>.<Service>` with at least a counter, a histogram with a unit, and an up-down counter or observable gauge. Keep tag cardinality bounded: never use IDs, emails, or other unbounded values as metric tags.
- **Structured logs:** export logs over OTLP, correlated with traces. Use `[LoggerMessage]` source generation in .NET, `logging` with the OpenTelemetry handler in Python, pino or the OpenTelemetry log bridge in Node.js, `otelslog` in Go, and the logback bridge in Java. Never log secrets or full email addresses.
- **GenAI:** samples that call models emit `gen_ai.*` semantic-convention spans plus the `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` metrics, so the dashboard's GenAI visualizer works. Prefer official instrumentation such as `opentelemetry-instrumentation-openai-v2`.
- **Browser:** JavaScript frontends that call APIs send OpenTelemetry web traces (document-load and fetch) to the dashboard, so a click traces from the browser to the database.

### AppHost

- Add health checks and `WaitFor` dependencies, and give URLs friendly display names.
- Add domain commands with `WithHttpCommand`/`withHttpCommand`, such as **Seed sample data**, **Reset**, or **Simulate traffic**, so the dashboard's telemetry pages fill up right away. Ask for confirmation before destructive commands, and expose dev-only endpoints in run mode only.
- Use secret parameters for keys, and keep existing publish and deploy targets working.

### README and screenshots

- Keep the sample README's structure, and add a **What to explore in the Aspire dashboard** section that names the resources and commands, the traces to open, the metric names, the structured log fields, and the Scalar link.
- Capture screenshots from the running sample in both themes, at 1440×1024 with a device scale factor of 2 (2880×2048), and name them `<sample>-<page>-{light,dark}.png` in `samples/<sample>/images/`. Show realistic, populated state: no empty tables, spinners, or error toasts. Pair each image in the README:

  ```md
  ![Description of what the image shows (light theme)](./images/<sample>-<page>-light.png#gh-light-mode-only)
  ![Description of what the image shows (dark theme)](./images/<sample>-<page>-dark.png#gh-dark-mode-only)
  ```

- Optimize images losslessly (for example with `oxipng`). Aim for about 400 KB each; pages full of photos or map tiles can be larger. Delete images that no longer match the UI, and don't leave dangling references.
- [aspire.dev](https://aspire.dev) builds its samples gallery from each README: the first `#` heading is the title, the first paragraph is the description, and the first image (with its `#gh-light-mode-only` / `#gh-dark-mode-only` pair) is the thumbnail. Keep all three meaningful.

## Engineering rules

- Builds use `TreatWarningsAsErrors` and `NuGetAuditMode=direct`, so new package references must be warning-free and free of known vulnerabilities. Use current stable versions.
- Don't bump the Aspire packages or the preview-pinned Java hosting and PostgreSQL Entity Framework Core integrations outside a release update. Keep all samples on the same OpenTelemetry package baseline. Dependabot ignores `OpenTelemetry.*`, so update those by hand.
- Keep sample folder names stable so inbound links keep working. If you rename resources or change routes, update the sample's entry in [`tests/SamplesIntegrationTests/AppHostTests.cs`](./tests/SamplesIntegrationTests/AppHostTests.cs) and check [aspire.dev](https://github.com/microsoft/aspire.dev) for pages that reference them.
- Keep code idiomatic and concise, and comment only code that isn't obvious. Don't commit secrets.

## Validate your change

Run the same checks as CI before you open a pull request:

```bash
# Restore TypeScript AppHosts and run npm ci and builds for every committed package-lock.json
bash ./build/validate-javascript.sh

# Build Go AppHosts
bash ./build/validate-go.sh

# Build every solution, file-based AppHost, TypeScript AppHost, and standalone project
cd build && ./build.sh -p:SkipTypeScriptRestore=true   # Windows: .\build.cmd

# Run the integration tests (Linux with Docker); filter to your sample's AppHost when iterating
cd tests && dotnet test ./SamplesTests.slnx

# Spring Petclinic only
cd samples/spring-petclinic/api && ./mvnw test
```

Then run the sample with `aspire run`, exercise its endpoints and commands, and check its traces, metrics, and structured logs in the dashboard.
