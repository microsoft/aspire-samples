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

> These samples require Aspire CLI, SDK, and packages **14.0.0-preview.1.26475.14** from the daily channel. They are an exception to the repository's 13.6 prerelease pin.

The published **13.6.0-preview.1.26474.10** build does not contain Redis `WithRepl()` / `withRepl()`. Using that version makes the C# and TypeScript basics fail to compile, although the custom rqlite docked REPL works. Keep the working pins until a 13.6 build containing the Redis REPL APIs is published; do not remove that feature merely to downgrade.

**Before merging**, align all four samples and the CI CLI with a compatible 13.6 release and repeat the build and interactive walkthroughs. Retargeting onto the repository's 13.6 upgrade branch does not by itself resolve this mismatch. The samples retain their daily channel and public Azure Artifacts `dotnet9` feed; no feed authentication is required.

These are local-development examples, not deployment samples. Only give trusted users dashboard access: the Redis and SQL REPLs grant database access. Data is disposable, and the HTTP APIs deliberately have no application authentication.
