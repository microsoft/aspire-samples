import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();
const executionContext = await builder.executionContext.get();

await builder.addDockerComposeEnvironment("dc");

const api = await builder.addUvicornApp("api", "./api", "main:app")
    .withHttpHealthCheck({ path: "/health" });

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api);

await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await yarp.addClusterFromResource(api);
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (await executionContext.isRunMode.get())
        {
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withExternalHttpEndpoints()
    .publishWithStaticFiles(frontend)
    .withExplicitStart();

await builder.build().run();
