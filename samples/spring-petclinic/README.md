# Spring Petclinic with Angular, Spring Boot, and PostgreSQL

![Screenshot of the Spring Petclinic Angular frontend](./images/spring-petclinic.png)

A compact Petclinic-style application demonstrating the official Aspire Java hosting
integration from a TypeScript AppHost.

## Architecture

```mermaid
flowchart LR
    Browser --> Angular[Angular frontend]
    Angular -->|/api| API[Spring Boot API]
    API --> PostgreSQL
```

The frontend lists owners, pets, and veterinarians and can create owners through the API.
The API persists data in an Aspire-managed PostgreSQL database.

## What this demonstrates

- `addJavaApp` with Maven and Spring Boot
- `withWrapperPath` for a cross-platform Maven wrapper
- PostgreSQL JDBC URL and credentials supplied through Aspire expressions
- Angular-to-API endpoint wiring without hardcoded service URLs
- Resource references, startup ordering, health checks, and external endpoints
- A TypeScript AppHost orchestrating Java, TypeScript, and a container resource

## Prerequisites

- Aspire CLI **13.6.0** from the [repository's pinned staging installer](../../build/install-aspire.sh), matching this sample's SDK/packages
- Java Development Kit 21 or later
- Node.js 20.19, 22.13, or 24 or later
- Docker Desktop or another Docker-compatible container runtime

Follow the [root installation instructions](../../README.md#aspire-version) to install
the matching staging CLI. The CLI/SDK and other integrations use `13.6.0`, while the
Java integration uses `13.6.0-preview.1.26475.12` from the same build. These are staging
artifacts, not a GA release, and the public version installer does not select this
build. If copying this sample outside the repository, also copy the root `nuget.config`
so its staging packages can be restored.

## Run

From this sample's directory:

```bash
aspire start
```

Open the `frontend` endpoint from the Aspire dashboard. The AppHost starts PostgreSQL,
waits for it to become ready, starts the Spring Boot API, and then starts Angular.

## Project layout

- `apphost.mts` - TypeScript AppHost and resource graph
- `api` - Spring Boot REST API using Spring Data JPA
- `frontend` - Angular single-page application

On Windows, `api/tools/run-mvnw.cmd` preserves the wrapper's directory-qualified path.
This works around a defect in the pinned staging build where the Java integration
launches `mvnw.cmd` without `.\`, which Windows does not resolve from the working directory.

## Security notes

This sample is intentionally small and demo-focused. Its CRUD endpoints are public and
unauthenticated, and PostgreSQL uses Aspire-generated development credentials. Do not
expose the sample directly in production without adding authentication, authorization,
request-rate controls, and production secret management.
