import { createBuilder } from './.aspire/modules/aspire.mjs';

const builder = await createBuilder();

// Real weather comes from Open-Meteo — a free forecast API that needs no key.
// Model it as an external service so the dashboard shows the dependency and polls
// its health, then inject its base URL into the API for on-demand, server-side calls.
const openMeteoUrl = "https://api.open-meteo.com";

const weather = await builder
    .addExternalService("open-meteo", openMeteoUrl)
    .withHttpHealthCheck({ 
        statusCode: 200,
        path: "/v1/forecast?latitude=0&longitude=0&current=temperature_2m",
    });

// Run the Express API and expose its HTTP endpoint externally.
// The API is written in TypeScript; load the tsx runtime so Node can execute
// `.ts` files directly (native type stripping is only unflagged in Node >= 22.18).
const api = await builder
    .addNodeApp("api", "./api", "src/index.ts")
    .withEnvironment("NODE_OPTIONS", "--import tsx")
    .withReference(weather)
    .withEnvironment("OPEN_METEO_URL", openMeteoUrl)
    .withHttpEndpoint({ env: "PORT" })
    .withExternalHttpEndpoints()
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "API";
    });

// Run the Vite frontend after the API and inject the API URL for local proxying.
const frontend = await builder
    .addViteApp("frontend", "./frontend")
    .withReference(api)
    .waitFor(api)
    .withBrowserDebugger();

// Bundle the frontend build output into the API container for publish/deploy.
await api.publishWithContainerFiles(frontend, "./static");

await builder.build().run();
