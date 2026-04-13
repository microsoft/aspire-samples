# YARP Serving Static Files

YARP reverse proxy serving a Vite frontend with dual-mode operation (dev HMR + production static files).

## Architecture

**Run Mode:**
```mermaid
flowchart LR
    Browser --> YARP
    YARP --> Vite[Vite Dev Server<br/>HMR enabled]
```

**Publish Mode:**
```mermaid
flowchart LR
    Browser --> YARP[YARP serving<br/>Vite build output<br/>'npm run build']
```

## What This Demonstrates

- **AddViteApp**: Vite-based frontend application
- **AddYarp**: Reverse proxy with dual-mode routing
- **PublishWithStaticFiles**: Automatic static file serving in production
- **ExecutionContext.IsRunMode**: Different behavior for dev vs production
- **Minimal AppHost**: Single-file orchestration

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

**Dual-Mode YARP** - Run mode proxies to Vite, publish mode serves static files:
```csharp
var frontend = builder.AddViteApp("frontend", "./frontend");

builder.AddYarp("app")
    .WithConfiguration(c =>
    {
        if (builder.ExecutionContext.IsRunMode)
            c.AddRoute("{**catch-all}", frontend); // Run: proxy to Vite HMR
    })
    .PublishWithStaticFiles(frontend); // Publish: serve static files
```

**Single Entry Point** - YARP provides one external endpoint for the entire application
