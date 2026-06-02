import { createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

const dc = await builder.addDockerComposeEnvironment("dc");

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withHttpHealthCheck({ path: "/health" })
    .withComputeEnvironment(dc);

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api);

await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await (await yarp.addClusterWithDestination("api", "https://api"))
            .withHttpClientConfig({ dangerousAcceptAnyServerCertificate: true });
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (await executionContext.isRunMode())
        {
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withReference(api)
    .withExternalHttpEndpoints()
    .withBrowserLogs()
    .publishWithStaticFiles(frontend)
    .withComputeEnvironment(dc)
    .withExplicitStart();

await builder.build().run();
