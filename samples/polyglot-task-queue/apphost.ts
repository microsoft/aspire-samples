import { ContainerLifetime, UrlDisplayLocation, createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();

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

const api = await builder.addNodeApp("api", "./api", "index.js")
    .withHttpEndpoint({ env: "PORT" })
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withUrl("", { displayText: "Task Queue UI" });

await builder.addPythonApp("worker-python", "./worker-python", "main.py")
    .withUv()
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

await builder.addCSharpApp("worker-csharp", "./worker-csharp")
    .waitFor(rabbitmq)
    .withReference(rabbitmq);

await api.publishWithContainerFiles(frontend, "public");

await builder.build().run();
