import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

await builder.addDockerComposeEnvironment("dc");

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withHttpHealthCheck({ path: "/health" });

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
    .publishWithStaticFiles(frontend)
    .withExplicitStart();

await builder.build().run();
