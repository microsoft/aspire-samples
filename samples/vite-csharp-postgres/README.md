# ASP.NET Core Minimal API + PostgreSQL + Vite

![Screenshot of the Todo List UI for the ASP.NET Core Minimal API + PostgreSQL + Vite sample](./images/vite-csharp-postgres-primary-page.png)

Todo app with C# Minimal APIs, Entity Framework Core, PostgreSQL, and React TypeScript frontend.

## Architecture

**Run Mode:**
```mermaid
flowchart LR
    Browser --> Vite[Vite Dev Server<br/>HMR enabled]
    Vite -->|Proxy /api| API[Minimal API]
    API --> PostgreSQL
```

**Publish Mode:**
```mermaid
flowchart LR
    Browser --> API[Minimal API serving<br/>Vite build output<br/>'npm run build']
    API --> PostgreSQL
```

## What This Demonstrates

- **addCSharpApp**: ASP.NET Core Minimal API with EF Core
- **addViteApp**: React + TypeScript frontend with Vite
- **addPostgres**: PostgreSQL database with pgAdmin
- **publishWithContainerFiles**: Frontend embedded in API's wwwroot for publish mode
- **AddNpgsqlDbContext**: Automatic PostgreSQL connection injection
- **withUrlForEndpointFactory**: Custom dashboard links (Scalar API docs, Todo UI)
- **waitFor**: Ensures PostgreSQL starts before API

## Running

```bash
aspire run
```

## Commands

```bash
aspire run      # Run locally
aspire deploy   # Deploy to Docker Compose
aspire do docker-compose-down-dc  # Teardown deployment
```

## Key Aspire Patterns

**Container Files Publishing** - Frontend embedded in API's wwwroot:
```ts
const postgres = await builder.addPostgres("postgres")
    .withDataVolume()
    .withLifetime(ContainerLifetime.Persistent)
    .withPgAdmin({
        configureContainer: async (pgAdmin) =>
        {
            await pgAdmin.withLifetime(ContainerLifetime.Persistent);
        }
    });

const db = await postgres.addDatabase("db");

const api = await builder.addCSharpApp("api", "./api")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .waitFor(db)
    .withReference(db)
    .withUrlForEndpointFactory("https", async (endpoint) => ({
        url: `${await endpoint.url.get()}/scalar`,
        displayText: "API Reference"
    }));

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withUrl("", { displayText: "Todo UI" });

await api.publishWithContainerFiles(frontend, "wwwroot");
```

**EF Core Integration** - Automatic connection string injection:
```csharp
builder.AddNpgsqlDbContext<TodoDbContext>("db");
```

**Static File Serving** - API serves embedded frontend in publish mode:
```csharp
app.UseFileServer(); // Serves wwwroot/index.html and assets
```
