import { HttpCommandResultMode, createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

await builder.addDockerComposeEnvironment("dc");

const openAiApiKey = await builder.addParameter("openai-api-key", { secret: true });

const openai = await builder.addOpenAI("openai")
    .withApiKey(openAiApiKey);

// Optional: route through any OpenAI-compatible gateway (e.g. a proxy or LiteLLM)
// by setting the "OpenAI:Endpoint" configuration value.
const customEndpoint = await (await builder.getConfiguration()).getConfigValue("OpenAI:Endpoint");
if (customEndpoint) {
    await openai.withEndpoint(customEndpoint);
}

// Injected into the agent as CHAT_URI, CHAT_KEY, and CHAT_MODELNAME.
const chat = await openai.addModel("chat", "gpt-4o-mini");

const sampleQuestion = "Can I still return the jacket from order 1002?";

const agent = await builder.addUvicornApp("ai-agent", "./agent", "main:app")
    .withUv()
    .withExternalHttpEndpoints()
    .withReference(chat)
    .withHttpHealthCheck({ path: "/health" })
    .withUrl("", { displayText: "Support chat UI" })
    .withUrls(async (ctx) => {
        const endpoint = ctx.getEndpoint("http");
        const baseUrl = await endpoint.url();
        const urls = await ctx.urls();
        await urls.addForEndpoint(endpoint, `${baseUrl}/scalar`, { displayText: "API reference (Scalar)" });
        await urls.addForEndpoint(endpoint, `${baseUrl}/openapi.json`, { displayText: "OpenAPI JSON" });
    })
    .withHttpCommand("/chat", "Ask a sample question", {
        commandName: "ask-sample-question",
        description: `Asks Ember "${sampleQuestion}" (runs the model and its order and return-policy tools).`,
        iconName: "ChatSparkle",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
        prepareRequest: async () => ({
            methodName: "POST",
            contentType: "application/json",
            content: JSON.stringify({ message: sampleQuestion, session_id: "dashboard-demo" }),
        }),
    })
    .withHttpCommand("/sessions", "End all conversations", {
        commandName: "end-conversations",
        description: "Clears in-memory chat history and records conversation-length metrics.",
        iconName: "DeleteDismiss",
        methodName: "DELETE",
        confirmationMessage: "End every active conversation?",
        resultMode: HttpCommandResultMode.Json,
    });

if (await executionContext.isRunMode()) {
    // Local dev only: record prompts, completions, and tool arguments on gen_ai spans
    // so the dashboard's GenAI visualizer can show them. Keep this off in production.
    await agent.withEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "span_only");
}

await builder.build().run();
