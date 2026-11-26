import { HttpCommandResultMode, OtlpProtocol, createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

const openAiApiKey = await builder.addParameter("openai-api-key", { secret: true });

const qdrant = await builder.addQdrant("qdrant");

const openai = await builder.addOpenAI("openai")
    .withApiKey(openAiApiKey);

// Optional: route through any OpenAI-compatible gateway (e.g. a proxy or LiteLLM)
// by setting the "OpenAI:Endpoint" configuration value.
const customEndpoint = await (await builder.getConfiguration()).getConfigValue("OpenAI:Endpoint");
if (customEndpoint) {
    await openai.withEndpoint(customEndpoint);
}

// Injected into the API as CHAT_* and EMBEDDINGS_* (URI, KEY, MODELNAME).
const chat = await openai.addModel("chat", "gpt-4.1-mini");
const embeddings = await openai.addModel("embeddings", "text-embedding-3-small");

const sampleQuestion = "How much is the home office stipend, and what can I buy with it?";

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withUv()
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(qdrant)
    .withReference(qdrant)
    .withReference(chat)
    .withReference(embeddings)
    .withExternalHttpEndpoints()
    .withUrls(async (ctx) => {
        const endpoint = ctx.getEndpoint("http");
        const baseUrl = await endpoint.url();
        const urls = await ctx.urls();
        await urls.addForEndpoint(endpoint, `${baseUrl}/scalar`, { displayText: "API reference (Scalar)" });
        await urls.addForEndpoint(endpoint, `${baseUrl}/openapi.json`, { displayText: "OpenAPI JSON" });
    })
    .withHttpCommand("/api/documents/samples", "Index sample documents", {
        commandName: "index-sample-documents",
        description: "Chunks, embeds, and indexes the bundled Contoso handbook documents into Qdrant.",
        methodName: "POST",
        iconName: "DocumentDatabase",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    })
    .withHttpCommand("/api/ask", "Ask a sample question", {
        commandName: "ask-sample-question",
        description: `POST /api/ask: "${sampleQuestion}"`,
        iconName: "ChatSparkle",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
        prepareRequest: async () => ({
            methodName: "POST",
            contentType: "application/json",
            content: JSON.stringify({ question: sampleQuestion }),
        }),
    })
    .withHttpCommand("/api/documents", "Clear index", {
        commandName: "clear-index",
        description: "Deletes every document from the Qdrant collection.",
        methodName: "DELETE",
        iconName: "Delete",
        confirmationMessage: "Remove every document from the vector index?",
        resultMode: HttpCommandResultMode.Json,
    });

if (await executionContext.isRunMode()) {
    // Local dev only: record prompts and completions on gen_ai spans so the dashboard's
    // GenAI visualizer can show them. Keep this off in production.
    await api.withEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "span_only");
}

// Browser telemetry: send the dashboard's OTLP/HTTP endpoint and API key to the Vite dev
// server, which proxies same-origin `/otlp/*` requests from the browser to the dashboard
// (see frontend/vite.config.js).
const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .waitFor(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withUrl("", { displayText: "Handbook Q&A" });

await api.publishWithContainerFiles(frontend, "public");

await builder.build().run();
