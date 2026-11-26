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

// The FastAPI task board API exports traces, metrics and logs to the dashboard (see api/telemetry.py).
const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withHttpHealthCheck({ path: "/health" });

// The API sits behind YARP at /api, so link its own endpoint for the docs.
const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" });

if (isRunMode)
{
    // Dev-only demo endpoints plus dashboard buttons that fill the board and generate activity,
    // so traces, metrics and structured logs light up without touching the UI.
    await api
        .withEnvironment("ENABLE_DEMO_ENDPOINTS", "true")
        .withHttpCommand("/demo/seed", "Seed sample data", {
            commandName: "seed-sample-data",
            description: "POST /demo/seed: replace the board with a dozen realistic platform-team tasks.",
            methodName: "POST",
            iconName: "DatabaseArrowUp",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        })
        .withHttpCommand("/demo/simulate", "Simulate traffic", {
            commandName: "simulate-traffic",
            description: "POST /demo/simulate: new tasks arrive, get picked up, blocked and completed (one blocked -> done move is rejected).",
            methodName: "POST",
            iconName: "ArrowSync",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        });
}

// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint and API key,
// and proxies same-origin `/otlp/*` requests from the browser to it (see frontend/vite.config.js).
const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf });

const app = await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await (await yarp.addClusterWithDestination("api", "https://api"))
            .withHttpClientConfig({ dangerousAcceptAnyServerCertificate: true });
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (isRunMode)
        {
            // Everything else (the app, Vite HMR and `/otlp`) goes to the Vite dev server.
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withReference(api)
    .withExternalHttpEndpoints()
    .publishWithStaticFiles(frontend)
    .withExplicitStart();

if (isRunMode)
{
    // The YARP container doesn't trust the dashboard's local development certificate, so its
    // proxy spans would be dropped. Local dashboard only; without this the trace still flows
    // browser -> API, just without the YARP hop.
    await app.withEnvironment("YARP_UNSAFE_OLTP_CERT_ACCEPT_ANY_SERVER_CERTIFICATE", "true");
}

await builder.build().run();
