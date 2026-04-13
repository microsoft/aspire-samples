# Python FastAPI + PostgreSQL Sample

**FastAPI REST API with PostgreSQL database using Aspire Python support.**

This sample demonstrates Aspire 13's polyglot platform support for Python applications, showcasing a simple CRUD API built with FastAPI and PostgreSQL.

## Quick Start

### Prerequisites

- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- [Docker](https://docs.docker.com/get-docker/)
- [Python 3.8+](https://www.python.org/)

### Commands

```bash
aspire run      # Run locally
aspire deploy   # Deploy to Docker Compose
aspire do docker-compose-down-dc  # Teardown deployment
```

## Overview

The application consists of:

- **Aspire AppHost** - Orchestrates the Python API and PostgreSQL database
- **FastAPI API** - Python web API with CRUD operations for users
- **PostgreSQL** - Database for storing user data
- **pgAdmin** - Web-based PostgreSQL administration tool

## Key Code

The AppHost configuration demonstrates Aspire 13's Python support:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

var postgres = builder.AddPostgres("postgres")
                      .WithPgAdmin()
                      .AddDatabase("db");

builder.AddUvicornApp("api", "./api", "main:app")
       .WithExternalHttpEndpoints()
       .WaitFor(postgres)
       .WithReference(postgres);

builder.Build().Run();
```

Key features:

- **Python Polyglot Support**: Uses `AddUvicornApp` to run FastAPI applications
- **PgAdmin Integration**: `.WithPgAdmin()` adds a web-based database management tool
- **Startup Dependencies**: `.WaitFor(postgres)` ensures the database is ready before starting the API
- **Database Connection**: Aspire provides connection properties via `POSTGRES_*` environment variables
- **External HTTP Endpoints**: Enables external access to the API
- **Docker Compose Deployment**: Ready for containerized deployment

## API Endpoints

The FastAPI application provides the following endpoints:

- `GET /` - API information
- `GET /health` - Health check with database connectivity test
- `GET /users` - List all users
- `GET /users/{id}` - Get a specific user
- `POST /users` - Create a new user
- `DELETE /users/{id}` - Delete a user

## How It Works

1. **Virtual Environment**: Aspire automatically creates a `.venv` directory and installs dependencies from `requirements.txt`
2. **Modular Architecture**: The API is organized into separate modules:
   - `models.py` - Pydantic models for data validation
   - `database.py` - Database connection and repository pattern
   - `main.py` - FastAPI routes and application setup
3. **Database Initialization**: On startup, the API creates the `users` table if it doesn't exist
4. **Connection Management**: Aspire provides PostgreSQL connection via `POSTGRES_*` environment variables (non-.NET connection property pattern)
5. **Startup Dependencies**: The API waits for PostgreSQL to be ready before starting
6. **Development Experience**: Zero configuration - Aspire handles virtual environment, dependency installation, and process management

## VS Code Integration

This sample includes VS Code configuration for Python development:

- **`.vscode/settings.json`**: Configures the Python interpreter to use the Aspire-created virtual environment
- After running `aspire run`, open the sample in VS Code for full IntelliSense and debugging support
- The virtual environment at `api/.venv` will be automatically detected

## Deployment

The sample uses Docker Compose for deployment. Run:

```bash
aspire deploy
```

This will:

1. Generate a Dockerfile for the Python application
2. Install dependencies and create a container image
3. Generate Docker Compose files with PostgreSQL
4. Deploy the complete application stack

To teardown:

```bash
aspire do docker-compose-down-dc
```
