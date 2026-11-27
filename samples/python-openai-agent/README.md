# Python OpenAI Support Agent

**An order-support AI agent built with FastAPI and OpenAI tool calling, orchestrated and observed with Aspire.**

*Ember* is the customer-support assistant for *Contoso Outdoor*, a fictional online outdoor gear store. Customers ask about their orders and returns. The agent calls two functions, `get_order_status` and `lookup_return_policy`, against an in-memory order and policy store, then answers in plain language. Every turn is instrumented with OpenTelemetry: GenAI semantic-convention spans and metrics, custom domain metrics, and structured logs. The Aspire dashboard shows the whole agent loop, including prompts, tool calls, and tool results, in its GenAI visualizer.

![Aspire dashboard GenAI visualizer for Ember's final chat gpt-4o-mini call: the conversation tree on the left (system prompt, user question, two tool calls with their tool responses, and the output), and on the right the lookup_return_policy tool call, its JSON result, and Ember's answer about returning the rain jacket from order 1002 (light theme)](./images/python-openai-agent-genai-light.png#gh-light-mode-only)
![Aspire dashboard GenAI visualizer for Ember's final chat gpt-4o-mini call: the conversation tree on the left (system prompt, user question, two tool calls with their tool responses, and the output), and on the right the lookup_return_policy tool call, its JSON result, and Ember's answer about returning the rain jacket from order 1002 (dark theme)](./images/python-openai-agent-genai-dark.png#gh-dark-mode-only)

## Quick Start

### Prerequisites

- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- [Node.js](https://nodejs.org/) (for the TypeScript AppHost)
- [Docker](https://docs.docker.com/get-docker/) (for deployment)
- [Python 3.11+](https://www.python.org/)
- [uv](https://github.com/astral-sh/uv)
- OpenAI API key (or any OpenAI-compatible endpoint, see below)

### Commands

```bash
aspire run      # Run locally
aspire deploy   # Deploy to Docker Compose
aspire do docker-compose-down-dc  # Teardown deployment
```

When you run the app for the first time, Aspire prompts you for your OpenAI API key.

Try these in the chat UI: *"Where is my order 1001?"*, *"Can I still return the jacket from order 1002?"*, and *"What's the return policy for electronics?"*. The demo orders are `1001` (shipped), `1002` (delivered), `1003` (processing), and `1004` (cancelled).

### Using an OpenAI-compatible endpoint

To send requests through a gateway or proxy that speaks the OpenAI Chat Completions API with tool calling (for example LiteLLM or a local server), set the `OpenAI:Endpoint` configuration value before you start:

```bash
OpenAI__Endpoint=http://localhost:4000/v1 aspire run
```

## Overview

The application consists of:

- **Aspire AppHost** (`apphost.mts`): models the OpenAI resource and its `chat` model, runs the Python agent with uv, and adds a health check, dashboard links, and commands.
- **Support agent** (`agent/`): a FastAPI service. `main.py` runs the tool-calling loop and serves the API, `support.py` holds the demo data, tool definitions, tool execution, and domain metrics, and `telemetry.py` bootstraps OpenTelemetry.
- **Chat UI** (`agent/static/index.html`): a static web page that calls `POST /chat` and shows which tools the agent used for each answer.

![Ember support chat UI with a three-turn conversation: order 1001's shipping status and tracking number, whether the jacket from order 1002 can still be returned, and when order 1003 will ship, each answer labeled with the tools Ember used (light theme)](./images/python-openai-agent-app-light.png#gh-light-mode-only)
![Ember support chat UI with a three-turn conversation: order 1001's shipping status and tracking number, whether the jacket from order 1002 can still be returned, and when order 1003 will ship, each answer labeled with the tools Ember used (dark theme)](./images/python-openai-agent-app-dark.png#gh-dark-mode-only)

## Key Code

The `apphost.mts` wires the model into the agent, and in local runs only it turns on GenAI message capture:

```ts
const openai = await builder.addOpenAI("openai").withApiKey(openAiApiKey);
const chat = await openai.addModel("chat", "gpt-4o-mini");

const agent = await builder.addUvicornApp("ai-agent", "./agent", "main:app")
    .withUv()
    .withExternalHttpEndpoints()
    .withReference(chat)                          // CHAT_URI, CHAT_KEY, CHAT_MODELNAME
    .withHttpHealthCheck({ path: "/health" })
    .withUrls(/* "API reference (Scalar)" and "OpenAPI JSON" links */)
    .withHttpCommand("/chat", "Ask a sample question", { /* POST a canned question */ })
    .withHttpCommand("/sessions", "End all conversations", { methodName: "DELETE" });

if (await executionContext.isRunMode()) {
    await agent.withEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "span_only");
}
```

`agent/telemetry.py` configures OpenTelemetry from the `OTEL_*` variables that Aspire injects. It exports traces, metrics, and logs over OTLP and instruments FastAPI and the OpenAI SDK (`opentelemetry-instrumentation-openai-v2`):

```python
configure_opentelemetry()      # OTLP providers, logging handler, OpenAIInstrumentor
app = FastAPI(...)
instrument_app(app)            # FastAPI server spans + http.server.* metrics
```

Each chat turn runs inside an `invoke_agent Ember` span. The OpenAI instrumentation emits a `chat gpt-4o-mini` span for every model call, and `support.execute_tool()` wraps every function the model asks for in an `execute_tool <name>` span:

```python
with tracer.start_as_current_span(f"execute_tool {name}") as span:
    span.set_attributes({"gen_ai.operation.name": "execute_tool", "gen_ai.tool.name": name,
                         "gen_ai.tool.call.id": call_id, "gen_ai.tool.type": "function"})
    result, outcome = handler(span, **json.loads(arguments))
    tool_invocations.add(1, {"gen_ai.tool.name": name, "support.tool.outcome": outcome})
```

Key features:

- **OpenAI resource + model reference**: `addOpenAI(...).addModel(...)` with a secret API key parameter. The agent reads the endpoint, key, and model from the reference.
- **Tool calling**: two JSON-schema tools that the model invokes. The agent allows at most 3 tool rounds per turn, then asks the model to answer.
- **GenAI observability**: [GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/) spans and metrics from `opentelemetry-instrumentation-openai-v2`, plus custom `invoke_agent` and `execute_tool` spans and a `SupportAgent` meter.
- **OpenAPI + Scalar**: a documented API with tags, examples, and RFC 9457 `ProblemDetails` errors, browsable at `/scalar`.
- **Health check and dashboard commands**: `/health` drives the resource's health. The dashboard can send a sample question or end all conversations.
- **Docker Compose deployment**: `aspire deploy` generates a Dockerfile and compose file. Message capture isn't enabled in deployments.

## What to explore in the Aspire dashboard

- **Resources**: `ai-agent` reports *Healthy* once `/health` responds. Its URLs include **Support chat UI**, **API reference (Scalar)**, and **OpenAPI JSON**. From the resource's actions menu, run **Ask a sample question** to call the agent without opening the UI, or **End all conversations** to clear sessions and record the conversation-length histogram.

  ![Aspire dashboard resources page with the ai-agent actions menu open, listing the Ask a sample question and End all conversations commands and the Support chat UI, API reference (Scalar), and OpenAPI JSON links (light theme)](./images/python-openai-agent-dashboard-resources-light.png#gh-light-mode-only)
  ![Aspire dashboard resources page with the ai-agent actions menu open, listing the Ask a sample question and End all conversations commands and the Support chat UI, API reference (Scalar), and OpenAPI JSON links (dark theme)](./images/python-openai-agent-dashboard-resources-dark.png#gh-dark-mode-only)

- **Traces**: open a `POST /chat` trace to see the agent loop:

  ```text
  POST /chat
  └─ invoke_agent Ember                 (gen_ai.conversation.id, token totals, tool definitions)
     ├─ chat gpt-4o-mini                (model asks for get_order_status)
     ├─ execute_tool get_order_status   (order.id, order.status)
     ├─ chat gpt-4o-mini                (model asks for lookup_return_policy)
     ├─ execute_tool lookup_return_policy
     └─ chat gpt-4o-mini                (final answer)
  ```

  Unknown orders add an `order.not_found` event to the tool span. Model-provider failures mark spans with `error.type`.

  ![Aspire dashboard trace for a POST /chat request: invoke_agent Ember with three chat gpt-4o-mini spans and execute_tool spans for get_order_status and lookup_return_policy, with the execute_tool get_order_status span details showing its gen_ai.tool attributes, arguments, and result plus order.id 1002 and order.status delivered (light theme)](./images/python-openai-agent-trace-light.png#gh-light-mode-only)
  ![Aspire dashboard trace for a POST /chat request: invoke_agent Ember with three chat gpt-4o-mini spans and execute_tool spans for get_order_status and lookup_return_policy, with the execute_tool get_order_status span details showing its gen_ai.tool attributes, arguments, and result plus order.id 1002 and order.status delivered (dark theme)](./images/python-openai-agent-trace-dark.png#gh-dark-mode-only)

- **GenAI visualizer**: select the **GenAI details** (sparkle) button on an `invoke_agent` or `chat` span to see the system prompt, the user message, the assistant's tool calls with arguments, the tool responses, the final answer, and token usage. Use *Previous* and *Next* to step through every model call in the turn.
- **Metrics** (`ai-agent`):
  - `gen_ai.client.token.usage` and `gen_ai.client.operation.duration`: input and output tokens and latency per model call.
  - `support_agent.conversations.started`, `support_agent.sessions.active`, and `support_agent.conversation.turns`: conversation volume and length.
  - `support_agent.tool.invocations` (by `gen_ai.tool.name` and `support.tool.outcome`) and `support_agent.tool.duration`: tool usage and latency.
  - `http.server.request.duration`: API latency by route.

  ![Aspire dashboard metrics page charting gen_ai.client.token.usage P50, P90, and P99 tokens with exemplar dots for the ai-agent resource over five minutes of support conversations (light theme)](./images/python-openai-agent-metrics-light.png#gh-light-mode-only)
  ![Aspire dashboard metrics page charting gen_ai.client.token.usage P50, P90, and P99 tokens with exemplar dots for the ai-agent resource over five minutes of support conversations (dark theme)](./images/python-openai-agent-metrics-dark.png#gh-dark-mode-only)

- **Structured logs**: each log is correlated to its trace and carries fields such as `gen_ai.conversation.id`, `gen_ai.tool.name`, `gen_ai.tool.call.id`, `support.tool.outcome`, `support.tool_call.count`, and token counts. Filter on `gen_ai.conversation.id` to follow one customer.

  ![Aspire dashboard structured logs for ai-agent with tool and agent-turn entries, an Order 1010 not found warning, and the details of an Agent turn completed entry showing gen_ai.conversation.id, support.tool_call.count, token counts, and its trace and span links (light theme)](./images/python-openai-agent-logs-light.png#gh-light-mode-only)
  ![Aspire dashboard structured logs for ai-agent with tool and agent-turn entries, an Order 1010 not found warning, and the details of an Agent turn completed entry showing gen_ai.conversation.id, support.tool_call.count, token counts, and its trace and span links (dark theme)](./images/python-openai-agent-logs-dark.png#gh-dark-mode-only)

> [!IMPORTANT]
> Message content capture (`OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`) records prompts, tool arguments, tool results, and answers in telemetry. The AppHost enables it only for local runs. Leave it off wherever conversations may contain personal data.

## Accessing the Application

### 1. Web UI

Open the **Support chat UI** link for `ai-agent` in the dashboard. Pick a suggestion or type a question. Under each answer, the UI lists the tools the agent used.

### 2. REST API

Browse and try the API in **Scalar** at `/scalar` (raw document at `/openapi.json`):

- `POST /chat`: send a message and get the agent's answer, tool calls, and token usage
- `GET /orders`: list the demo orders
- `GET /sessions`: list active conversations
- `DELETE /sessions`: end all conversations
- `DELETE /sessions/{id}`: end one conversation
- `GET /health`: health check (reports whether a model is configured)
- `GET /api`: service information

![Scalar API reference for the Contoso Outdoor Support Agent API showing the POST /chat operation with its request body, ProblemDetails error responses, a curl example, and an example response with tool calls and token usage (light theme)](./images/python-openai-agent-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Outdoor Support Agent API showing the POST /chat operation with its request body, ProblemDetails error responses, a curl example, and an example response with tool calls and token usage (dark theme)](./images/python-openai-agent-scalar-dark.png#gh-dark-mode-only)

## Security Notes

This sample is instructional and is not production-ready; see the repository's
[security disclaimer](../../README.md#security-disclaimer). By default, `/chat`
is anonymous. If you configure `AGENT_API_KEY`, `/chat`, `GET /sessions`,
`DELETE /sessions`, and `DELETE /sessions/{id}` require the same value in the
`X-API-Key` header.

The code includes basic OpenAI quota and cost controls: message length,
session/history caps, response token caps, a limit of 3 tool rounds per turn,
and per-client rate limiting. The model is fixed by the AppHost, so callers
can't pick a more expensive one. Tool results come from trusted demo data; if
you connect tools to real systems, validate their arguments and authorize every
lookup for the signed-in customer. Production deployments should add real
authentication and authorization (see [FastAPI security](https://fastapi.tiangolo.com/tutorial/security/)),
abuse monitoring, persistent session storage if conversations must survive
process restarts, and prompt/content safety controls aligned with
[OpenAI production best practices](https://developers.openai.com/api/docs/guides/production-best-practices),
[OpenAI safety best practices](https://developers.openai.com/api/docs/guides/safety-best-practices),
and [OWASP LLM Prompt Injection](https://genai.owasp.org/llmrisk/llm01-prompt-injection/)
guidance.

## Example API Usage

You can also interact with the agent programmatically via its REST API:

```bash
# Ask about an order
curl -X POST http://localhost:<port>/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Can I still return the jacket from order 1002?", "session_id": "session-42"}'

# Check health
curl http://localhost:<port>/health

# List active sessions
curl http://localhost:<port>/sessions
```

The response includes the answer, the tools the agent called, and the token usage for the turn:

```json
{
  "response": "Yes! Your Summit Shell rain jacket (order 1002) was delivered 12 days ago, and apparel has a 30-day return window...",
  "session_id": "session-42",
  "model": "gpt-4o-mini",
  "tool_calls": [
    { "name": "get_order_status", "arguments": "{\"order_id\": \"1002\"}" },
    { "name": "lookup_return_policy", "arguments": "{\"category\": \"apparel\"}" }
  ],
  "usage": { "input_tokens": 1184, "output_tokens": 96 }
}
```

## How It Works

1. **Virtual Environment**: Aspire automatically creates a `.venv` directory for the Python agent
2. **Fast Dependency Installation**: uv installs dependencies from `pyproject.toml` and `uv.lock`
3. **Agent Startup**: FastAPI configures OpenTelemetry and creates an OpenAI client from the `chat` model reference
4. **Agent Loop**: each `POST /chat` sends the conversation and tool definitions to the model, runs any tools it asks for, and sends the results back until the model answers
5. **Web UI**: The root endpoint (`/`) serves a static HTML chat interface
6. **Session Management**: The agent keeps conversation history per session for context-aware responses

## VS Code Integration

After `aspire run` creates `agent/.venv`, select it as the Python interpreter in VS Code (**Python: Select Interpreter**) for IntelliSense and debugging support.

## Deployment

Deploy to Docker Compose:

```bash
aspire deploy
```

This will:

1. Generate a Dockerfile for the Python application
2. Install Python dependencies and build container image
3. Generate Docker Compose configuration
4. Deploy the application stack

## Development Tips

- The Python app uses automatic dependency installation via `pyproject.toml` with uv (much faster than pip)
- Hot reload is enabled for local development
- Use the Aspire Dashboard to view traces, metrics, and logs for every agent turn
- uv keeps `uv.lock` in sync for reproducible builds
