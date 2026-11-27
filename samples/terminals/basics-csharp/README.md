# Terminal basics: C# AppHost and API

Run [Slumber](https://github.com/LucasPickering/slumber), an interactive REST client, inside the Aspire dashboard. Create a note through an ASP.NET Core API, edit it directly in Redis's authenticated REPL, then retrieve the changed value in Slumber. Every request Slumber sends is also documented in Scalar and traced, measured, and logged in the dashboard.

![Aspire dashboard Terminal view running Slumber, whose Read welcome request returns "Edited from Redis", above a docked redis-cli REPL that changed the note with HSET (light theme)](./images/basics-csharp-roundtrip-light.png#gh-light-mode-only)
![Aspire dashboard Terminal view running Slumber, whose Read welcome request returns "Edited from Redis", above a docked redis-cli REPL that changed the note with HSET (dark theme)](./images/basics-csharp-roundtrip-dark.png#gh-dark-mode-only)

## Prerequisites and startup

- Aspire CLI **17.0.0**, installed using the [root installation instructions](../../../README.md#aspire-version).
- The .NET 10 SDK selected by [`samples/global.json`](../../global.json).
- Docker or another supported container runtime, running with Linux containers.

See the collection's [Aspire version requirements](../README.md#aspire-version).

From this directory:

```sh
aspire restore --apphost apphost.cs
aspire start --apphost apphost.cs --isolated
aspire wait --apphost apphost.cs
```

Open the dashboard URL printed by Aspire. Wait for `redis` and `api` to be healthy and `slumber` to be running. The first start builds the Slumber image. Ports and the container-reachable API address are supplied by Aspire; do not enter a URL into Slumber.

The non-root container packages checksum-verified Slumber **5.3.0** binaries for Linux ARM64/AMD64 and includes Vim. No host Slumber or Vim installation is needed.

## What the AppHost adds

[`apphost.cs`](./apphost.cs) declares three resources:

```csharp
var redis = builder.AddRedis("redis").WithRepl();

var api = builder.AddDotnetProject("api", "./api/Notes.Api.csproj")
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Notes API")
    .WithUrls(context => { /* "API reference (Scalar)" and "OpenAPI JSON" links */ })
    .WithHttpCommand("/dev/notes/seed", "Seed sample notes", commandName: "seed-notes", /* ... */);

builder.AddDockerfile("slumber", "./slumber")
    .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api)
    .WithTerminal();
```

`WithTerminal()` attaches to Slumber's foreground process. Redis's `WithRepl()` instead adds a resource action that starts a separate, already authenticated `redis-cli`. The AppHost suppresses the terminal APIs' experimental diagnostic locally.

The API uses `Aspire.StackExchange.Redis` and sample-local ServiceDefaults for health checks and telemetry. Slumber receives only the API endpoint, not Redis credentials. Its [saved requests](./slumber/slumber.yml) read that endpoint with `{{ env('BASE_URL') }}`. HTTP is intentional for this local container-to-API connection; no certificate validation is disabled.

The API's links and its **Seed sample notes** command are dashboard conveniences: Scalar, the OpenAPI document, and the `POST /dev/notes/seed` endpoint behind the command are mapped only in Development, which is how the AppHost runs the API.

The optional `http` profile in [`apphost.run.json`](./apphost.run.json) also uses unencrypted local dashboard, telemetry, and resource-service connections. This is separate from the API's HTTP traffic and does not disable dashboard authentication. Keep these loopback endpoints private; do not expose or forward this profile to untrusted networks.

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

![Aspire dashboard resources table with redis, slumber and api running, and the api actions menu open on the Seed sample notes command, its description, and the Notes API, API reference (Scalar) and OpenAPI JSON links (light theme)](./images/basics-csharp-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources table with redis, slumber and api running, and the api actions menu open on the Seed sample notes command, its description, and the Notes API, API reference (Scalar) and OpenAPI JSON links (dark theme)](./images/basics-csharp-dashboard-resources-dark.png#gh-dark-mode-only)

- **slumber** runs the REST client as an interactive terminal; **redis** has the **REPL** action.
- On **api**, run **Seed sample notes**. It writes five team notes (`standup`, `release-checklist`, `oncall-handoff`, `design-review` and `retro-actions`) and returns a JSON summary such as `{"created":5,"replaced":0,"notes":[...]}`, so **02 - List notes** in Slumber has something to show. The `welcome` note used by the walkthrough is left alone.
- **api** also links **API reference (Scalar)** and **OpenAPI JSON**.

### Traces

![Aspire dashboard trace of Slumber's PUT /notes/{id} request, with the notes.write span selected to show note.id, note.text.length and the created outcome, a note.created event, and the Redis HSET child span (light theme)](./images/basics-csharp-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of Slumber's PUT /notes/{id} request, with the notes.write span selected to show note.id, note.text.length and the created outcome, a note.created event, and the Redis HSET child span (dark theme)](./images/basics-csharp-trace-dark.png#gh-dark-mode-only)

Each request Slumber sends is a trace: the ASP.NET Core server span (its `user_agent.original` is `slumber/5.3.0`), a domain span from the `Contoso.Notes` activity source, and the Redis command.

| Span | Attributes | Events |
| --- | --- | --- |
| `notes.list` | `notes.count` | |
| `notes.read` | `note.id`, `note.text.length` | |
| `notes.write` | `note.id`, `note.text.length` | `note.created` or `note.replaced` |
| `notes.delete` | `note.id` | `note.deleted` |
| `notes.seed` | `notes.count` | one `note.created` / `note.replaced` per sample note, with its `note.id` |

Every domain span also has `notes.operation` and `notes.outcome` (`ok`, `created`, `replaced`, `deleted`, `not_found` or `invalid`). A failed Redis call marks the span as an error. The health check's background Redis `PING`s are filtered out so they don't crowd the trace list.

### Metrics

![Aspire dashboard metrics page charting the Contoso.Notes notes.text.length histogram P50, P90 and P99 with exemplar dots that link to the traces of individual writes, under steady note traffic (light theme)](./images/basics-csharp-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting the Contoso.Notes notes.text.length histogram P50, P90 and P99 with exemplar dots that link to the traces of individual writes, under steady note traffic (dark theme)](./images/basics-csharp-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `Contoso.Notes` | `notes.operations` | Counter of note operations, by `notes.operation` (`list`, `read`, `write`, `delete`, `seed`) and `notes.outcome` |
| `Contoso.Notes` | `notes.text.length` | Histogram of the text length written through the API, by whether the note was `created` or `replaced` |
| `Contoso.Notes` | `notes.stored` | Gauge of the notes in the Redis hash as last seen by the API: each list (and seed) sets it, then creates and deletes adjust it |

Note IDs never become metric tags. Because REPL edits bypass the API, `notes.stored` catches up with them on the next **02 - List notes**. The histogram records inside the `notes.write` span, so each exemplar dot links to the trace of the write that produced it.

### Structured logs

The `Notes.Api.NotesApi` category logs each operation with [`[LoggerMessage]`](./api/Log.cs), for example `Created note welcome (19 characters)`, `Read note welcome (17 characters)` (after the REPL edit), `Note missing was not found`, and `Seeded 5 sample notes (5 created, 0 replaced); 6 notes stored`. Each entry has `NoteId`, `TextLength` or `NoteCount` as properties and links to the span that wrote it. Rejected requests log a warning with the validation message.

### API reference

![Scalar API reference for the Terminal Notes API showing the Create or replace a note operation with its documented ID pattern, required text body, 200/201/400/415/503 responses and a 400 validation problem example (light theme)](./images/basics-csharp-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Terminal Notes API showing the Create or replace a note operation with its documented ID pattern, required text body, 200/201/400/415/503 responses and a 400 validation problem example (dark theme)](./images/basics-csharp-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/notes` | Every note, ordered by ID |
| `GET` | `/notes/{id}` | One note, straight from Redis |
| `PUT` | `/notes/{id}` | Create (`201`) or replace (`200`) a note's text |
| `DELETE` | `/notes/{id}` | Delete a note (`204`) |
| `POST` | `/dev/notes/seed` | Development only: write the sample notes |

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with a `traceId`: a `400` validation problem with `errors.id` or `errors.text`, `404` for a missing note, `415` for a non-JSON body and `503` when Redis is unavailable. Scalar's request samples default to `curl`.

## Data and lifecycle

The API stores plain text in the Redis hash `notes`, with note IDs as fields. IDs accept 1-40 lowercase ASCII letters, digits, or hyphens. A JSON `text` value must contain 1-1000 UTF-16 characters and cannot be whitespace-only. PUT creates (201) or replaces (200); missing reads/deletes return 404, invalid input returns 400, and Redis failures return 503. Request bodies are limited to 10 KiB.

There is no persistent volume or API-local cache. To reset only this demo's data, run `DEL notes` in the REPL, then resend the create request (or run **Seed sample notes** to repopulate the sample notes). Do not use `FLUSHALL`.

Type `quit` in `redis-cli` before closing its tab. Hiding a terminal panel is not a process stop. Slumber exits with **q** (or **Ctrl+C**); use its resource **Start** or **Restart** action to launch it again. Reloading the dashboard reconnects to a still-running Slumber process. Use **Fit** or the terminal size selector to adjust its layout.

Stop this sample explicitly:

```sh
aspire stop --apphost apphost.cs
```

## Checks and troubleshooting

```sh
dotnet test tests/Notes.Tests.csproj
```

The focused tests cover the API contract, the seed endpoint, and the generated OpenAPI document with an in-memory store; the walkthrough exercises real Redis and the interactive terminals. `TerminalNotes.slnx` includes the API, ServiceDefaults, and tests in the repository build.

If **REPL** is missing, check `aspire --version` and restart with the matching CLI. If Slumber is waiting, inspect `api` and `redis` health first. If requests cannot connect, verify that `BASE_URL` is populated by the AppHost; do not replace it with `localhost` (which means the Slumber container itself). Changes to the Dockerfile or saved collection require rebuilding/restarting the sample.

Keep the dashboard private to trusted users: its Redis REPL grants authenticated datastore access. The unauthenticated HTTP notes API allows anyone who can reach it to read, replace, and delete every demo note. Use only disposable, non-sensitive data; this is a local demonstration, not production.
