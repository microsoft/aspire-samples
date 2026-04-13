# Go API with In-Memory Storage

REST API built with Go and chi router, using in-memory storage with thread-safe operations.

This sample demonstrates a **custom Go integration** that automatically downloads Go modules, runs Go applications in development, and builds Go containers for production deployment.

## Architecture

```mermaid
flowchart LR
    Browser --> API[Go API<br/>chi router]
    API --> Store[In-Memory Store<br/>sync.RWMutex]
```

## What This Demonstrates

- **Custom Go Integration**: `AddGoApp` builds a custom integration that:
  - Automatically runs `go mod download` to install dependencies
  - Executes Go applications during development with `go run`
  - Builds containerized Go applications for production deployment
- **WithHttpEndpoint**: HTTP endpoint with PORT environment variable
- **WithHttpHealthCheck**: Health check endpoint at `/health`
- **In-Memory Storage**: Thread-safe CRUD operations with sync.RWMutex
- **Chi Router**: Lightweight, idiomatic HTTP router for Go

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

**Go Application** - Automatic `go mod download` and build:
```csharp
builder.AddGoApp("api", "./api")
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();
```

**Environment Variables** - Aspire injects `PORT` for HTTP endpoint configuration

## API Endpoints

- `GET /` - API information
- `GET /health` - Health check
- `GET /items` - List all items
- `GET /items/{id}` - Get item by ID
- `POST /items` - Create new item
- `PUT /items/{id}` - Update item
- `DELETE /items/{id}` - Delete item
