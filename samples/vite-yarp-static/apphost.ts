import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();
const executionContext = await builder.executionContext();

await builder.addDockerComposeEnvironment("dc");

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
    .publishWithStaticFiles(frontend);

await builder.build().run();
