import {
    ContainerLifetime,
    HttpCommandResultMode,
    OtlpProtocol,
    UrlDisplayLocation,
    createBuilder,
    refExpr,
} from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

await builder.addDockerComposeEnvironment("dc");

const postgres = await builder.addPostgres("postgres")
    .withDataVolume()
    .withLifetime(ContainerLifetime.Persistent)
    .withPgAdmin({
        configureContainer: async (pgAdmin) =>
        {
            await pgAdmin.withLifetime(ContainerLifetime.Persistent);
        }
    });

const db = await postgres.addDatabase("servicedesk");

// The service desk API: EF Core + Npgsql spans, the Contoso.ServiceDesk meter and [LoggerMessage] structured logs.
const api = await builder.addDotnetProject("api", "./api/Api.csproj", { launchProfileName: "https" })
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .waitFor(db)
    .withReference(db)
    .withUrlForEndpoint("http", async (url) =>
    {
        url.displayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .withUrlForEndpoint("https", async (url) =>
    {
        url.displayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .publishAsDockerComposeService(async (_, service) =>
    {
        await service.restart.set("always");
    });

// Scalar, the OpenAPI document and the demo endpoints behind these commands are mapped in Development (run mode) only.
const apiEndpoint = await api.getEndpoint("https");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi/v1.json`, { displayText: "OpenAPI JSON" })
    .withHttpCommand("/api/demo/seed", "Seed sample data", {
        commandName: "seed-sample-data",
        description: "POST /api/demo/seed: replace the queue with 17 realistic IT requests, some overdue and some recently resolved.",
        confirmationMessage: "Replace every request in the queue with the sample data?",
        methodName: "POST",
        iconName: "DatabaseArrowUp",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    })
    .withHttpCommand("/api/demo/simulate", "Simulate traffic", {
        commandName: "simulate-traffic",
        description: "POST /api/demo/simulate: employees raise requests, agents pick up and resolve them, and one invalid move is rejected (409).",
        methodName: "POST",
        iconName: "ArrowSync",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    });

// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint and API key, and proxies
// same-origin `/otlp/*` requests from the browser to it (see frontend/vite.config.ts).
const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .waitFor(api)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .withUrl("", { displayText: "Service desk UI" });

await api.publishWithContainerFiles(frontend, "wwwroot");

await builder.build().run();
