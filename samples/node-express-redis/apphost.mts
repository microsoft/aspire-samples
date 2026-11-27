import {
    createBuilder,
    HttpCommandResultMode,
    OtlpProtocol,
    refExpr,
} from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();
const isRunMode = await executionContext.isRunMode();

await builder.addDockerComposeEnvironment("dc");

const redis = await builder.addRedis("redis")
    .withRedisInsight();

// The cart API keeps each shopping cart in a Redis hash with a 30-minute sliding expiry.
// Preload the OpenTelemetry SDK so it can patch Express, node-redis and pino before the
// app imports them; telemetry goes to the dashboard over OTLP/HTTP (see api/instrumentation.js).
const api = await builder.addNodeApp("api", "./api", "index.js")
    .withEnvironment("NODE_OPTIONS", "--import ./instrumentation.js")
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withHttpEndpoint({ env: "PORT" })
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(redis)
    .withReference(redis)
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "API";
    });

// The API sits behind YARP at /api, so link its own endpoint for the docs.
const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" });

if (isRunMode)
{
    // A dev-only endpoint plus a dashboard button that fills carts and checks most of them
    // out, so traces, metrics and structured logs light up without touching the UI.
    await api
        .withEnvironment("ENABLE_SHOPPER_SIMULATION", "true")
        .withHttpCommand("/simulate/shoppers", "Simulate shoppers", {
            commandName: "simulate-shoppers",
            description: "POST /simulate/shoppers: 8 shoppers fill carts; most check out, one abandons its cart and one hits the $500 order limit.",
            methodName: "POST",
            iconName: "Cart",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        });
}

// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint and API key,
// and proxies same-origin `/otlp/*` requests from the browser to it (see frontend/vite.config.ts).
const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "Vite dev server";
    });

const app = await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await yarp.addClusterFromResource(api);
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (isRunMode)
        {
            // Everything else (the app, Vite HMR and `/otlp`) goes to the Vite dev server.
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withExternalHttpEndpoints()
    .publishWithStaticFiles(frontend)
    .withExplicitStart()
    .withUrlForEndpoint("https", async (url) => {
        url.displayText = "Storefront";
    })
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "Storefront (HTTP)";
    });

if (isRunMode)
{
    // The YARP container doesn't trust the dashboard's local development certificate, so its
    // proxy spans would be dropped. Local dashboard only; without this the trace still flows
    // browser -> API, just without the YARP hop.
    await app.withEnvironment("YARP_UNSAFE_OLTP_CERT_ACCEPT_ANY_SERVER_CERTIFICATE", "true");
}

await builder.build().run();
