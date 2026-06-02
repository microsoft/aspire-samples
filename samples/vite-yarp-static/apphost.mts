import { createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

const dc = await builder.addDockerComposeEnvironment("dc");

const frontend = await builder.addViteApp("frontend", "./frontend");

await builder.addYarp("app")
    .withConfiguration(async (yarp) =>
    {
        if (await executionContext.isRunMode())
        {
            const frontendCluster = await yarp.addClusterFromResource(frontend);
            await yarp.addRoute("{**catch-all}", frontendCluster);
        }
    })
    .withExternalHttpEndpoints()
    .withBrowserLogs()
    .publishWithStaticFiles(frontend)
    .withComputeEnvironment(dc);

await builder.build().run();
