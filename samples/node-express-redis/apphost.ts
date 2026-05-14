import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

await builder.addDockerComposeEnvironment("dc");

const redis = await builder.addRedis("redis")
    .withRedisInsight();

const api = await builder.addNodeApp("api", "./api", "index.js")
    .withHttpEndpoint({ env: "PORT" })
    .withHttpHealthCheck({ path: "/health" })
    .waitFor(redis)
    .withReference(redis);

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api);

await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        const apiCluster = await yarp.addClusterFromResource(api);
        await (await yarp.addRoute("api/{**catch-all}", apiCluster))
            .withTransformPathRemovePrefix("/api");

        if (await executionContext.isRunMode())
        {
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withExternalHttpEndpoints()
    .publishWithStaticFiles(frontend)
    .withExplicitStart();

await builder.build().run();
