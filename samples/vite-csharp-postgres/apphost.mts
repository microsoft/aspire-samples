import { ContainerLifetime, UrlDisplayLocation, createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

const dc = await builder.addDockerComposeEnvironment("dc");

const postgres = await builder.addPostgres("postgres")
    .withComputeEnvironment(dc)
    .withDataVolume()
    .withLifetime(ContainerLifetime.Persistent)
    .withPgAdmin({
        configureContainer: async (pgAdmin) =>
        {
            await pgAdmin.withLifetime(ContainerLifetime.Persistent);
        }
    });

const db = await postgres.addDatabase("db");

const api = await builder.addCSharpApp("api", "./api")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .withComputeEnvironment(dc)
    .waitFor(db)
    .withReference(db)
    .withUrlForEndpoint("http", async (url) =>
    {
        url.displayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .withUrlForEndpoint("https", async (url) =>
    {
        url.displayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .withUrls(async (ctx) =>
    {
        const endpoint = ctx.getEndpoint("https");
        const urls = await ctx.urls();
        await urls.addForEndpoint(endpoint, `${await endpoint.url()}/scalar`, { displayText: "API Reference" });
    })
    .publishAsDockerComposeService(async (_, service) =>
    {
        await service.restart.set("always");
    });

const frontend = await builder.addViteApp("frontend", "./frontend")
    .withReference(api)
    .withUrl("", { displayText: "Todo UI" })
    .withBrowserLogs();

await api.publishWithContainerFiles(frontend, "wwwroot");

await builder.build().run();
