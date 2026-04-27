# RAG Document Q&A with Svelte

![Screenshot of the RAG Document Q&A sample UI](./images/rag-document-qa-svelte-primary-page.png)

Upload documents and ask questions using Retrieval Augmented Generation with vector search.

## Architecture

**Run Mode:**
```mermaid
flowchart LR
    User --> Svelte[Vite Dev Server<br/>HMR enabled]
    Svelte -->|Proxy /api| API[FastAPI]
    API --> Qdrant[Qdrant Vector DB]
    API --> OpenAI[OpenAI API]
```

**Publish Mode:**
```mermaid
flowchart LR
    User --> API[FastAPI serving<br/>Vite build output<br/>'npm run build']
    API --> Qdrant[Qdrant Vector DB]
    API --> OpenAI[OpenAI API]
```

## What This Demonstrates

- **RAG Pattern**: Document upload → chunk → embed → vector search → GPT answer
- **addUvicornApp**: Python FastAPI backend with uv package manager
- **addViteApp**: Svelte 5 frontend with Vite
- **addQdrant**: Vector database for semantic search
- **addOpenAI**: Secure API key management
- **publishWithContainerFiles**: Frontend embedded in API for publish mode

## Running

```bash
aspire run
```

Aspire will prompt for your OpenAI API key on first run.

## Commands

```bash
aspire run      # Run locally
aspire deploy   # Deploy to Docker Compose
aspire do docker-compose-down-dc  # Teardown deployment
```

## Key Aspire Patterns

**Static File Embedding** - Frontend proxied in run mode, embedded in publish mode:
```ts
const openAiApiKey = await builder.addParameter("openai-api-key", { secret: true });
const qdrant = await builder.addQdrant("qdrant");

await builder.addOpenAI("openai")
    .withApiKey(openAiApiKey);

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withUv()
    .waitFor(qdrant)
    .withReference(qdrant)
    .withEnvironment("OPENAI_APIKEY", openAiApiKey);

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withUrl("", { displayText: "RAG UI" });

await api.publishWithContainerFiles(frontend, "public");
```

**Python + uv** - Fast dependency installation from `pyproject.toml`

**Vector Database** - `addQdrant()` for semantic search

**OpenAI Integration** - `addOpenAI()` prompts for API key on first run
