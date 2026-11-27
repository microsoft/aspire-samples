import {
  EndpointProperty,
  HttpCommandResultMode,
  OtlpProtocol,
  createBuilder,
  refExpr,
} from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const isRunMode = await (await builder.executionContext()).isRunMode();
const mavenWrapper = process.platform === "win32" ? "tools/run-mvnw.cmd" : "./mvnw";

// Disposable demo database: no persistent volume; the API lets Hibernate update its schema.
const postgres = await builder.addPostgres("postgres");
const database = await postgres.addDatabase("petclinic");

// Local demo: the HTTP API has no authentication or transport encryption. Use synthetic owner data only.
// The OpenTelemetry Java agent traces Spring MVC, JDBC, and Hibernate, exports JVM and HTTP metrics, and bridges
// Logback to OTLP logs. The API adds its own spans and the Petclinic.Api meter through the OpenTelemetry API.
const api = await builder.addJavaApp("api", "./api")
  .withWrapperPath(mavenWrapper)
  .withMavenGoal("spring-boot:run", [])
  .withOtelAgentDefaultPath()
  .withJvmArgs(["-Xms128m", "-Xmx512m"])
  .withHttpEndpoint({ targetPort: 8080, env: "SERVER_PORT" })
  .withEnvironment("SPRING_DATASOURCE_URL", await database.jdbcConnectionString())
  .withEnvironment("SPRING_DATASOURCE_USERNAME", await postgres.userNameReference())
  .withEnvironment("SPRING_DATASOURCE_PASSWORD", postgres.passwordParameter.get())
  // Key-value pairs on SLF4J log events (for example petclinic.visit.id) become structured log attributes.
  .withEnvironment("OTEL_INSTRUMENTATION_LOGBACK_APPENDER_EXPERIMENTAL_CAPTURE_KEY_VALUE_PAIR_ATTRIBUTES", "true")
  .withReference(database)
  .waitFor(database)
  .withHttpHealthCheck({ path: "/actuator/health" })
  .withExternalHttpEndpoints()
  .withUrlForEndpoint("http", async (url) => {
    url.displayText = "API";
  });

// Deep links to the generated API docs, shown next to the API's endpoint in the dashboard.
const apiEndpoint = await api.getEndpoint("http");
await api
  .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: "API reference (Scalar)" })
  .withUrl(refExpr`${apiEndpoint}/v3/api-docs`, { displayText: "OpenAPI JSON" });

if (isRunMode) {
  // Dev-only demo endpoints behind dashboard buttons, so the app, traces, metrics, and logs fill up instantly.
  await api
    .withEnvironment("PETCLINIC_DEMO_ENABLED", "true")
    .withHttpCommand("/demo/seed", "Seed sample visits", {
      commandName: "seed-sample-visits",
      description: "POST /demo/seed: replace the upcoming schedule with five days of realistic visits.",
      methodName: "POST",
      iconName: "CalendarAdd",
      isHighlighted: true,
      resultMode: HttpCommandResultMode.Json,
    })
    .withHttpCommand("/demo/simulate", "Simulate clinic traffic", {
      commandName: "simulate-clinic-traffic",
      description:
        "POST /demo/simulate: a walk-in registers, visits are booked (one double-booked dentist, one out-of-hours request), past-due visits are checked out, and one visit is cancelled.",
      methodName: "POST",
      iconName: "PeopleQueue",
      isHighlighted: true,
      resultMode: HttpCommandResultMode.Json,
    });
}

// Angular listens on all interfaces; keep both HTTP endpoints private and do not publicly forward them.
// Browser telemetry: the Angular dev server gets the dashboard's OTLP/HTTP endpoint and API key, and proxies
// same-origin /otlp requests from the browser to it (see frontend/proxy.conf.cjs). /api is proxied same-origin too,
// so the browser's traceparent header reaches Spring and one click is one trace.
await builder.addJavaScriptApp("frontend", "./frontend", { runScriptName: "start" })
  .withHttpEndpoint({ targetPort: 4200 })
  .withEnvironment("PETCLINIC_API_URL", api.getEndpoint("http").property(EndpointProperty.Url))
  .withReference(api)
  .waitFor(api)
  .withHttpHealthCheck({ path: "/" })
  .withExternalHttpEndpoints()
  .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
  .withUrlForEndpoint("http", async (url) => {
    url.displayText = "Front desk";
  });

await builder.build().run();
