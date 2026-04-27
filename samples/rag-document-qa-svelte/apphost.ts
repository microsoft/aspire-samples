import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();

const openAiApiKey = await builder.addParameter("openai-api-key", { secret: true });

const qdrant = await builder.addQdrant("qdrant");

await builder.addOpenAI("openai")
    .withApiKey(openAiApiKey);

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withUv()
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(qdrant)
    .withReference(qdrant)
    .withEnvironment("OPENAI_APIKEY", openAiApiKey)
    .withExternalHttpEndpoints();

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withUrl("", { displayText: "RAG UI" });

await api.publishWithContainerFiles(frontend, "public");

await builder.build().run();
