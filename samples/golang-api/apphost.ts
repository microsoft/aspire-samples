import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();
const executionContext = await builder.executionContext.get();

await builder.addDockerComposeEnvironment("env")
    .configureDashboard(async (dashboard) =>
    {
        await dashboard.withHostPort({ port: 9003 });
    });

if (await executionContext.isPublishMode.get())
{
    await builder.addDockerfile("api", "./api")
        .withHttpEndpoint({ env: "PORT" })
        .withHttpHealthCheck({ path: "/health" })
        .withExternalHttpEndpoints();
}
else
{
    const api = await builder.addExecutable("api", "go", "./api", ["run", "main.go"])
        .withHttpEndpoint({ env: "PORT" })
        .withHttpHealthCheck({ path: "/health" })
        .withExternalHttpEndpoints();

    const goModInstaller = await builder.addExecutable("api-go-mod-installer", "go", "./api", ["mod", "tidy"])
        .withParentRelationship(api);

    await api.waitForCompletion(goModInstaller);
}

await builder.build().run();
