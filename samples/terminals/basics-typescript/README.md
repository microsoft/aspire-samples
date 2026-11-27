# Terminal basics: TypeScript AppHost and API

Run [Slumber](https://github.com/LucasPickering/slumber), an interactive REST client, inside the Aspire dashboard. Create a note through an Express/TypeScript API, edit it directly in Redis's authenticated REPL, then retrieve the changed value in Slumber. Every request Slumber sends is also documented in Scalar and traced, measured, and logged in the dashboard.

![Aspire dashboard Terminal view running Slumber, whose Read welcome request returns "Edited from Redis", above a docked redis-cli REPL that changed the note with HSET (light theme)](./images/basics-typescript-roundtrip-light.png#gh-light-mode-only)
![Aspire dashboard Terminal view running Slumber, whose Read welcome request returns "Edited from Redis", above a docked redis-cli REPL that changed the note with HSET (dark theme)](./images/basics-typescript-roundtrip-dark.png#gh-dark-mode-only)

## Prerequisites and startup

- Aspire CLI **17.0.0**, installed using the [root installation instructions](../../../README.md#aspire-version).
- Node.js **24.18.0** (the repository CI version) and npm.
- Docker or another supported container runtime, running with Linux containers.

See the collection's [Aspire version requirements](../README.md#aspire-version).

From this directory:

```sh
aspire restore --apphost apphost.mts
npm --prefix api ci
aspire start --apphost apphost.mts --isolated
aspire wait --apphost apphost.mts
```

Open the dashboard URL printed by Aspire. Wait for `redis` and `api` to be healthy and `slumber` to be running. The first start builds the Slumber image. Ports and the container-reachable API address are supplied by Aspire; do not enter a URL into Slumber. `aspire restore` generates the AppHost's `.aspire/modules/` bindings; do not edit or commit them.

The non-root container packages checksum-verified Slumber **5.3.0** binaries for Linux ARM64/AMD64 and includes Vim. No host Slumber or Vim installation is needed.

## What the AppHost adds

[`apphost.mts`](./apphost.mts) declares three resources:

```typescript
const redis = await builder.addRedis('redis').withRepl();

const api = await builder.addNodeApp('api', './api', 'src/index.ts')
    .withEnvironment('NODE_OPTIONS', '--import tsx --import ./src/instrumentation.ts')
    .withHttpEndpoint({ env: 'PORT' })
    .withHttpHealthCheck({ path: '/health' })
    .withReference(redis)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .waitFor(redis);
// Plus "API reference (Scalar)" / "OpenAPI JSON" links and, in run mode, a "Seed sample notes" command.

await builder.addDockerfile('slumber', './slumber')
    .withEnvironment('BASE_URL', api.getEndpoint('http'))
    .waitFor(api)
    .withTerminal();
```

`withTerminal()` attaches to Slumber's foreground process. Redis's `withRepl()` instead adds a resource action that starts a separate, already authenticated `redis-cli`.

The API uses the Node Redis client with Aspire's `REDIS_URI` and preloads OpenTelemetry before Express. Slumber receives only the API endpoint, not Redis credentials. Its [saved requests](./slumber/slumber.yml) read that endpoint with `{{ env('BASE_URL') }}`. HTTP is intentional for this local container-to-API connection; no certificate validation is disabled.

[`api/src/instrumentation.ts`](./api/src/instrumentation.ts) starts the OpenTelemetry Node SDK with the HTTP, Express, node-redis, pino and runtime instrumentations, and exports traces, metrics and logs over OTLP/HTTP (protobuf). The gRPC exporter can't verify the dashboard's development certificate because `@grpc/grpc-js` ignores `NODE_EXTRA_CA_CERTS`, so the AppHost asks for `OtlpProtocol.HttpProtobuf`, and [`aspire.config.json`](./aspire.config.json) gives the dashboard an OTLP/HTTP endpoint.

## Try the round trip

1. Select `slumber` in the dashboard and open its **Terminal** view (on the **Console logs** page, switch to **Terminal** if the log view is shown). Focus the terminal and press **r** to focus the request list. Use **Up/Down** to select **01 - Create a note**, then **Enter** to send. Expect **201 Created** (or **200 OK** if `welcome` already exists).
2. Select **02 - List notes** or **03 - Read welcome** and press **Enter**. The JSON includes `"text": "Hello from Slumber!"`.
3. On the Resources page, open `redis`'s **Actions** menu and select **REPL**. No password entry is needed. Run:

   ```text
   HGETALL notes
   HSET notes welcome "Edited from Redis"
   HGET notes welcome
   ```

4. Hide the terminal panel and return to Slumber. Select **03 - Read welcome** and press **Enter**. Expect **200 OK** and `"text": "Edited from Redis"` without restarting the API.
5. Send **04 - Update welcome** for `"text": "Updated from Slumber!"`, then **05 - Delete welcome** for **204 No Content**. Reading the deleted note returns **404**. **06 - API and Redis health** checks readiness.

**Tab/Shift+Tab** moves between panes, **r** returns to the request list, and **?** shows help. Selecting a request loads it immediately; **Enter** sends it.

Vim is configured as the default editor. In Vim, press **i** to edit, then **Esc** and **:wq** to save and return to Slumber (**:q!** discards changes). Collection edits inside the container are disposable; edit `slumber/slumber.yml` in the repository to retain them across rebuilds.

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources table with redis, slumber and the Node.js api running, and the api actions menu open on the Seed sample notes command, its description, and the Notes API, API reference (Scalar) and OpenAPI JSON links (light theme)](./images/basics-typescript-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources table with redis, slumber and the Node.js api running, and the api actions menu open on the Seed sample notes command, its description, and the Notes API, API reference (Scalar) and OpenAPI JSON links (dark theme)](./images/basics-typescript-dashboard-resources-dark.png#gh-dark-mode-only)

- **slumber** runs the REST client as an interactive terminal; **redis** has the **REPL** action.
- On **api**, run **Seed sample notes**. It writes five team notes (`standup`, `release-checklist`, `oncall-handoff`, `design-review` and `retro-actions`) and returns a JSON summary such as `{"created":5,"replaced":0,"notes":[...]}`, so **02 - List notes** in Slumber has something to show. The `welcome` note used by the walkthrough is left alone. The AppHost adds the command, and sets `ENABLE_DEMO_ENDPOINTS` to map `POST /dev/notes/seed`, in run mode only.
- **api** also links **API reference (Scalar)** and **OpenAPI JSON**.

### Traces

![Aspire dashboard trace of Slumber's PUT /notes/:id request through the Express request handler, with the notes.write span selected to show note.id, note.text.length and the created outcome, a note.created event, and the redis-HSET child span (light theme)](./images/basics-typescript-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of Slumber's PUT /notes/:id request through the Express request handler, with the notes.write span selected to show note.id, note.text.length and the created outcome, a note.created event, and the redis-HSET child span (dark theme)](./images/basics-typescript-trace-dark.png#gh-dark-mode-only)

Each request Slumber sends is a trace: the HTTP server span (its `user_agent.original` is `slumber/5.3.0`), the Express request handler, a domain span from the `Contoso.Notes` tracer, and the Redis command with its `db.query.text`.

| Span | Attributes | Events |
| --- | --- | --- |
| `notes.list` | `notes.count` | |
| `notes.read` | `note.id`, `note.text.length` | |
| `notes.write` | `note.id`, `note.text.length` | `note.created` or `note.replaced` |
| `notes.delete` | `note.id` | `note.deleted` |
| `notes.seed` | `notes.count` | one `note.created` / `note.replaced` per sample note, with its `note.id` |

Every domain span also has `notes.operation` and `notes.outcome` (`ok`, `created`, `replaced`, `deleted`, `not_found` or `invalid`). A failed Redis call records the exception and marks the span as an error. Health-check `PING`s have no parent span, so the Redis instrumentation skips them.

### Metrics

![Aspire dashboard metrics page charting the Contoso.Notes notes.text.length histogram P50, P90 and P99 under steady note traffic (light theme)](./images/basics-typescript-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting the Contoso.Notes notes.text.length histogram P50, P90 and P99 under steady note traffic (dark theme)](./images/basics-typescript-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `Contoso.Notes` | `notes.operations` | Counter of note operations, by `notes.operation` (`list`, `read`, `write`, `delete`, `seed`) and `notes.outcome` |
| `Contoso.Notes` | `notes.text.length` | Histogram of the text length written through the API, by whether the note was `created` or `replaced` |
| `Contoso.Notes` | `notes.stored` | Gauge of the notes in the Redis hash as last seen by the API: each list (and seed) sets it, then creates and deletes adjust it |

Note IDs never become metric tags. Because REPL edits bypass the API, `notes.stored` catches up with them on the next **02 - List notes**. The Node.js runtime instrumentation adds event loop, GC and heap metrics.

### Structured logs

[`api/src/logger.ts`](./api/src/logger.ts) is a pino logger. The pino instrumentation sends each record to the dashboard's **Structured logs** with its fields as attributes and the active trace and span IDs, for example `Created note welcome (19 characters)` (`noteId`, `textLength`), `Read note welcome (17 characters)` after the REPL edit, `Note missing was not found`, and `Seeded 5 sample notes (5 created, 0 replaced); 6 notes stored`. Rejected requests log a warning with the validation message; Redis failures log an error.

### API reference

![Scalar API reference for the Terminal Notes API showing the Create or replace a note operation with its documented ID pattern, required text body, 200/201/400/413/415/503 responses and a 400 validation problem example (light theme)](./images/basics-typescript-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Terminal Notes API showing the Create or replace a note operation with its documented ID pattern, required text body, 200/201/400/413/415/503 responses and a 400 validation problem example (dark theme)](./images/basics-typescript-scalar-dark.png#gh-dark-mode-only)

[`api/src/openapi.ts`](./api/src/openapi.ts) is a hand-written OpenAPI 3.1 document, served at `/openapi.json` and rendered by `@scalar/express-api-reference` at `/scalar`.

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/notes` | Every note, ordered by ID |
| `GET` | `/notes/{id}` | One note, straight from Redis |
| `PUT` | `/notes/{id}` | Create (`201`) or replace (`200`) a note's text |
| `DELETE` | `/notes/{id}` | Delete a note (`204`) |
| `POST` | `/dev/notes/seed` | Run mode only: write the sample notes |

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with an `instance` and a `traceId`: a `400` validation problem with `errors.id`, `errors.text` or `errors.body`, `404` for a missing note, `413` for a body over 10 KiB, `415` for a non-JSON body and `503` when Redis is unavailable.

## Data and lifecycle

The API stores plain text in the Redis hash `notes`, with note IDs as fields. IDs accept 1-40 lowercase ASCII letters, digits, or hyphens. A JSON `text` value must contain 1-1000 UTF-16 characters and cannot be whitespace-only. PUT creates (201) or replaces (200); missing reads/deletes return 404, invalid input returns 400, and Redis failures return 503. Request bodies are limited to 10 KiB.

There is no persistent volume or API-local cache. To reset only this demo's data, run `DEL notes` in the REPL, then resend the create request (or run **Seed sample notes** to repopulate the sample notes). Do not use `FLUSHALL`.

Type `quit` in `redis-cli` before closing its tab. Hiding a terminal panel is not a process stop. Slumber exits with **q** (or **Ctrl+C**); use its resource **Start** or **Restart** action to launch it again. Reloading the dashboard reconnects to a still-running Slumber process. Use **Fit** or the terminal size selector to adjust its layout.

Stop this sample explicitly:

```sh
aspire stop --apphost apphost.mts
```

## Checks and troubleshooting

```sh
npm run build
npm --prefix api run build
npm --prefix api test
```

The focused tests cover the API contract, the seed endpoint, and the OpenAPI document with an in-memory Redis substitute; the walkthrough exercises real Redis and the interactive terminals. The repository JavaScript validation discovers the AppHost and API lockfiles.

If **REPL** is missing, check `aspire --version` and restart with the matching CLI. If Slumber is waiting, inspect `api` and `redis` health first. If requests cannot connect, verify that `BASE_URL` is populated by the AppHost; do not replace it with `localhost` (which means the Slumber container itself). Changes to the Dockerfile or saved collection require rebuilding/restarting the sample.

Keep the dashboard private to trusted users: its Redis REPL grants authenticated datastore access. The unauthenticated HTTP notes API allows anyone who can reach it to read, replace, and delete every demo note. Use only disposable, non-sensitive data; this is a local demonstration, not production.
