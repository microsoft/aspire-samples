import {
    ContainerLifetime,
    HttpCommandResultMode,
    OtlpProtocol,
    UrlDisplayLocation,
    createBuilder,
    refExpr,
} from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();
const isRunMode = await executionContext.isRunMode();

await builder.addDockerComposeEnvironment("dc");

const rabbitmq = await builder.addRabbitMQ("messaging")
    .withManagementPlugin()
    .withLifetime(ContainerLifetime.Persistent)
    .withUrlForEndpoint("tcp", async (url) =>
    {
        url.displayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .withUrlForEndpoint("management", async (url) =>
    {
        url.displayText = "RabbitMQ Management UI";
    });

// The Express API publishes each task to the RabbitMQ work queue for its type and keeps the task
// board. Preload the OpenTelemetry SDK so it can patch Express and pino before the app imports
// them; Node.js telemetry goes to the dashboard over OTLP/HTTP (see api/instrumentation.js).
const api = await builder.addNodeApp("api", "./api", "index.js")
    .withEnvironment("NODE_OPTIONS", "--import ./instrumentation.js")
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withHttpEndpoint({ env: "PORT" })
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(rabbitmq)
    .withReference(rabbitmq)
    .withUrlForEndpoint("http", async (url) =>
    {
        url.displayText = "API";
    });

const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" });

if (isRunMode)
{
    // Dashboard buttons that queue work without touching the UI, so traces, metrics and
    // structured logs light up for all three workers. The burst endpoint is dev-only.
    await api
        .withHttpCommand("/tasks/samples", "Submit sample tasks", {
            commandName: "submit-sample-tasks",
            description: "POST /tasks/samples: queue a sales CSV for Python, operations notes for C# and an emailed invoice for Node.js, in one trace.",
            methodName: "POST",
            iconName: "DocumentQueueAdd",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        })
        .withEnvironment("ENABLE_BURST_SIMULATION", "true")
        .withHttpCommand("/simulate/burst", "Simulate burst", {
            commandName: "simulate-burst",
            description: "POST /simulate/burst: queue 24 tasks at once across the three workers; every 8th document is one its worker rejects.",
            methodName: "POST",
            iconName: "Flash",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        });
}

// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint and API key, and
// proxies same-origin `/otlp/*` requests from the browser to it (see frontend/vite.config.ts).
const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .waitFor(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withUrlForEndpoint("http", async (url) =>
    {
        url.displayText = "Task board";
    });

// One worker per task type, each in the language that suits the job. They all continue the
// API's trace from the RabbitMQ message headers and report back on the task_status and results queues.
await builder.addPythonApp("worker-python", "./worker-python", "main.py")
    .withUv()
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

await builder.addDotnetProject("worker-csharp", "./worker-csharp/worker-csharp.csproj")
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

await builder.addNodeApp("worker-node", "./worker-node", "index.js")
    .withEnvironment("NODE_OPTIONS", "--import ./instrumentation.js")
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

// Publish mode: the API serves the frontend's build output.
await api.publishWithContainerFiles(frontend, "public");

await builder.build().run();
