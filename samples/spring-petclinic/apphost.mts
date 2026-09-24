import { EndpointProperty, createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const mavenWrapper = process.platform === "win32" ? "tools/run-mvnw.cmd" : "./mvnw";

const postgres = await builder.addPostgres("postgres");
const database = await postgres.addDatabase("petclinic");

const api = await builder.addJavaApp("api", "./api")
  .withWrapperPath(mavenWrapper)
  .withMavenGoal("spring-boot:run", [])
  .withJvmArgs(["-Xms128m", "-Xmx512m"])
  .withHttpEndpoint({ targetPort: 8080, env: "SERVER_PORT" })
  .withEnvironment("SPRING_DATASOURCE_URL", await database.jdbcConnectionString())
  .withEnvironment("SPRING_DATASOURCE_USERNAME", await postgres.userNameReference())
  .withEnvironment("SPRING_DATASOURCE_PASSWORD", postgres.passwordParameter.get())
  .withReference(database)
  .waitFor(database)
  .withHttpHealthCheck({ path: "/actuator/health" })
  .withExternalHttpEndpoints();

await builder.addJavaScriptApp("frontend", "./frontend", { runScriptName: "start" })
  .withHttpEndpoint({ targetPort: 4200 })
  .withEnvironment("PETCLINIC_API_URL", api.getEndpoint("http").property(EndpointProperty.Url))
  .withReference(api)
  .waitFor(api)
  .withHttpHealthCheck({ path: "/" })
  .withExternalHttpEndpoints();

await builder.build().run();
