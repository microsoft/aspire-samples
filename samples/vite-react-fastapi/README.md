# Vite React + FastAPI

![Screenshot of the Todo App UI for the Vite React + FastAPI sample](./images/vite-react-fastapi-primary-page.png)

Todo app with React frontend and Python FastAPI backend using YARP for unified routing.

## Architecture

**Run Mode:**
```mermaid
flowchart LR
    Browser --> YARP
    YARP -->|/api/*| FastAPI[FastAPI Backend]
    YARP --> Vite[Vite Dev Server<br/>HMR enabled]
```

**Publish Mode:**
```mermaid
flowchart LR
    Browser --> YARP[YARP serving<br/>Vite build output<br/>'npm run build']
    YARP -->|/api/*| FastAPI[FastAPI Backend]
```

## What This Demonstrates

- **addUvicornApp**: Python FastAPI backend with uv package manager
- **addViteApp**: React + TypeScript frontend with Vite
- **addYarp**: Single endpoint with path-based routing
- **withTransformPathRemovePrefix**: Strip `/api` prefix before forwarding
- **publishWithStaticFiles**: Frontend embedded in YARP for publish mode
- **Dual-Mode Operation**: Vite HMR in run mode, Vite build output in publish mode
- **Polyglot Fullstack**: JavaScript + Python working together

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

**YARP with Path Transform** - Strip `/api` prefix before forwarding to FastAPI:
```ts
const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withHttpHealthCheck({ path: "/health" });

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api);

await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await yarp.addClusterFromResource(api);
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (await executionContext.isRunMode.get())
        {
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .publishWithStaticFiles(frontend);
```

**Path Transform Example**:
- Client: `GET /api/todos`
- YARP receives: `/api/todos`
- Transform strips: `/api`
- FastAPI receives: `GET /todos`

## Security Notes

This sample intentionally keeps the FastAPI todo endpoints public and unauthenticated for instructional use. The `/api/todos` endpoints are demo endpoints, not a production API security model.

- Todo data is stored in process memory and is lost when the API restarts. The API caps the in-memory list at 100 todos, limits todo titles to 200 characters, requires non-empty titles, and bounds list responses with `skip` and `limit` query parameters.
- The sample does not include authentication, authorization, CSRF protection, or rate limiting.
- For production, add an appropriate identity provider and authorization checks, persistent storage, CSRF protections if browser credentials are used, rate limiting/quotas, request logging/monitoring, and deployment-specific network protections.

Relevant references:

- [FastAPI security](https://fastapi.tiangolo.com/tutorial/security/)
- [FastAPI request body and field validation](https://fastapi.tiangolo.com/tutorial/body-fields/)
- [OWASP API Security Top 10](https://owasp.org/API-Security/editions/2023/en/0x11-t10/)
