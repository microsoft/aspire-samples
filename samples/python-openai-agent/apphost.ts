import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();

await builder.addDockerComposeEnvironment("dc");

const openAiApiKey = await builder.addParameter("openai-api-key", { secret: true });

await builder.addOpenAI("openai")
    .withApiKey(openAiApiKey);

await builder.addUvicornApp("ai-agent", "./agent", "main:app")
    .withUv()
    .withExternalHttpEndpoints()
    .withEnvironment("OPENAI_API_KEY", openAiApiKey);

await builder.build().run();
