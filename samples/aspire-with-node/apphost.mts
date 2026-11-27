import {
    createBuilder,
    HttpCommandResultMode,
    OtlpProtocol,
    refExpr,
} from './.aspire/modules/aspire.mjs';

const builder = await createBuilder();

// Forecasts come from Open-Meteo — a free weather API that needs no key.
// Model it as an external service so the dashboard shows the dependency and polls
// its health, then inject its base URL into the API for server-side calls.
const openMeteoUrl = "https://api.open-meteo.com";

const openMeteo = await builder
    .addExternalService("open-meteo", openMeteoUrl)
    .withHttpHealthCheck({
        statusCode: 200,
        path: "/v1/forecast?latitude=0&longitude=0&current=temperature_2m",
    });

// The Express API scores each leg of a delivery route for weather risk and recommends
// go / delay / reroute. It is written in TypeScript: load the tsx runtime so Node can run
// `.ts` files directly, then preload the OpenTelemetry SDK so it can patch Express, fetch
// and pino before the app imports them. Telemetry goes to the dashboard over OTLP/HTTP
// (see api/src/instrumentation.ts).
const api = await builder
    .addNodeApp("api", "./api", "src/index.ts")
    .withEnvironment("NODE_OPTIONS", "--import tsx --import ./src/instrumentation.ts")
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withReference(openMeteo)
    .withEnvironment("OPEN_METEO_URL", openMeteoUrl)
    .withHttpEndpoint({ env: "PORT" })
    .withExternalHttpEndpoints()
    .withHttpHealthCheck({ path: "/health" })
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "API";
    });

// Deep links to the API docs, shown next to the API's endpoint in the dashboard.
const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" })
    // A dashboard button that assesses every predefined route, so traces, metrics and
    // structured logs light up without touching the UI.
    .withHttpCommand("/api/routes/assessments", "Assess all routes", {
        commandName: "assess-all-routes",
        description: "POST /api/routes/assessments: score every fleet route for the next 24 hours and return a go / delay / reroute summary.",
        methodName: "POST",
        iconName: "VehicleTruckProfile",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    });

// Run the Vite frontend after the API and inject the API URL for local proxying.
// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint (not the
// default gRPC one) and API key, and proxies same-origin `/otlp/*` requests from the
// browser to the dashboard (see frontend/vite.config.ts).
const frontend = await builder
    .addViteApp("frontend", "./frontend")
    .withReference(api)
    .waitFor(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "Dispatch board";
    });

// Bundle the frontend build output into the API container for publish/deploy.
await api.publishWithContainerFiles(frontend, "./static");

await builder.build().run();
