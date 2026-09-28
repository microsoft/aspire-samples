# Aspire terminals

Use interactive tools alongside your application without installing them on the host, copying connection strings, or leaving the Aspire dashboard.

| Sample | AppHost | API | Terminal features |
| --- | --- | --- | --- |
| [C# basics](./basics-csharp/) | File-based C# | ASP.NET Core | `WithTerminal()` and Redis `WithRepl()` |
| [TypeScript basics](./basics-typescript/) | TypeScript | Express/TypeScript | `withTerminal()` and Redis `withRepl()` |
| [Terminal automation](./automation-csharp/) | File-based C# | ASP.NET Core | Drive Slumber with `SendKeyAsync` / `WaitForTextAsync`, verify CRUD, handle repeat runs |
| [Custom docked SQL REPL](./docked-repl-csharp/) | File-based C# | rqlite HTTP API | Custom container resource and `TerminalService` / `TerminalPlacement.Dock` |

Each basic sample is standalone: its own API, Redis database, Dockerfile, and saved request collection. Both demonstrate the same round trip: create a note in a REST terminal UI, edit its underlying Redis value in a REPL, then read the changed value through the API.

The automation sample reuses the C# basic sample's API and Slumber image source, but launches its own resources and supplies a separate request collection. The custom REPL sample is independent and uses rqlite's official image and SQL shell. Each sample has its own AppHost and can run separately.

## Two different terminal experiences

**Slumber** runs as the main process of a container. `WithTerminal()` / `withTerminal()` makes that process interactive in the dashboard's console view, including keyboard input and terminal resizing.

**Redis** continues running as a normal server. `WithRepl()` / `withRepl()` adds an on-demand **REPL** resource action that launches an authenticated `redis-cli` terminal. It does not attach interactive input to the Redis server process.

The REST client is [Slumber](https://github.com/LucasPickering/slumber), an MIT-licensed Rust application with YAML request collections and environment substitution. The basic samples package checksum-verified Slumber 5.3.0 binaries for Linux ARM64/AMD64 in a non-root container, with Vim as the default editor. No host Slumber or Vim installation is required.

## Development version

> These samples use staged **13.6.0** packages and CLI from build **13.6.0-preview.1.26475.12**, commit `34db30a7d3733229da64a403dfc4e4e4d7a1a43b`, matching the repository.

Install the CLI with the [repository's pinned installer](../../build/install-aspire.sh) using the [root installation instructions](../../README.md#aspire-version), not the public stable installer or an older 13.6 preview. The package version is `13.6.0`, but these are build-specific staging artifacts rather than a NuGet.org release.

The exact public DARC feed is `https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-microsoft-aspire-34db30a7/nuget/v3/index.json`. Each sample pins it in `nuget.config` and in `aspire.config.json` via `channel: staging` and `overrideStagingFeed`. The latter also routes the TypeScript AppHost's bundled package restore. Keep both files when copying a sample; no feed authentication is required.

The older `13.6.0-preview.1.26474.10` packages on the shared `dotnet9` feed do not include Redis `WithRepl()` / `withRepl()`. When updating, align the CLI artifact, package versions, and build-specific feed across the repository and repeat the interactive walkthroughs; changing the version alone is insufficient.

These are local-development examples, not deployment samples. Only give trusted users dashboard access: the Redis and SQL REPLs grant database access. Data is disposable, and the HTTP APIs deliberately have no application authentication.
