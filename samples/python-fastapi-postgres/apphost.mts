import { HttpCommandResultMode, createBuilder, refExpr } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

await builder.addDockerComposeEnvironment("dc");

const postgres = await builder.addPostgres("postgres")
    .withPgAdmin();
const db = await postgres.addDatabase("db");

// The FastAPI customers API exports traces (including one span per SQL statement), metrics
// and logs to the dashboard (see api/telemetry.py).
const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withExternalHttpEndpoints()
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(db)
    .withReference(db)
    .withReference(postgres);

const apiEndpoint = await api.getEndpoint("http");
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: "OpenAPI JSON" });

if (await executionContext.isRunMode())
{
    // Dev-only demo endpoints plus dashboard buttons that fill the database and generate CRM
    // activity, so traces, metrics and structured logs light up without writing a client.
    await api
        .withEnvironment("ENABLE_DEMO_ENDPOINTS", "true")
        .withHttpCommand("/demo/seed", "Seed sample customers", {
            commandName: "seed-sample-customers",
            description: "POST /demo/seed: replace every customer with 24 realistic accounts.",
            methodName: "POST",
            iconName: "DatabaseArrowUp",
            isHighlighted: true,
            confirmationMessage: "Replace all customers with the 24 sample accounts?",
            resultMode: HttpCommandResultMode.Json,
        })
        .withHttpCommand("/demo/simulate", "Simulate traffic", {
            commandName: "simulate-traffic",
            description: "POST /demo/simulate: searches, new leads, conversions, upsells and churn, plus one duplicate sign-up (409) and one stale lookup (404).",
            methodName: "POST",
            iconName: "ArrowSync",
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        });
}

await builder.build().run();
