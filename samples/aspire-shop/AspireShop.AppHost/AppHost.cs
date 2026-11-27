using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

if (builder.ExecutionContext.IsRunMode)
{
    // Data volumes don't work on ACA for Postgres so only add when running
    postgres.WithDataVolume();
}

var catalogDb = postgres.AddDatabase("catalogdb");

var basketCache = builder.AddRedis("basketcache")
    .WithDataVolume()
    .WithRedisCommander();

var catalogDbManager = builder.AddDotnetProject("catalogdbmanager", "../AspireShop.CatalogDbManager/AspireShop.CatalogDbManager.csproj")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithHttpHealthCheck("/health");

// The manager reverts every migration (dev-only endpoint), then the command restarts it to re-apply them and re-seed
catalogDbManager.WithHttpCommand("/reset-db", "Reset database", commandName: "reset-database", commandOptions: new()
{
    Description = "Reverts the catalog's EF Core migrations, then restarts the manager to re-apply them and re-seed the products.",
    ConfirmationMessage = "Reset the catalog database to its seeded state?",
    IconName = "DatabaseLightning",
    GetCommandResult = async context =>
    {
        if (!context.Response.IsSuccessStatusCode)
        {
            return CommandResults.Failure($"The catalog manager couldn't revert the migrations ({(int)context.Response.StatusCode} {context.Response.ReasonPhrase}).");
        }

        var commands = context.Services.GetRequiredService<ResourceCommandService>();
        return await commands.ExecuteCommandAsync(catalogDbManager.Resource, KnownResourceCommands.RestartCommand, context.CancellationToken);
    }
});

var catalogService = builder.AddDotnetProject("catalogservice", "../AspireShop.CatalogService/AspireShop.CatalogService.csproj")
    .WithReference(catalogDb)
    .WaitFor(catalogDbManager)
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        // Surface the API reference (mapped in Development only) and keep the raw endpoint URLs on the details page
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    });

var basketService = builder.AddDotnetProject("basketservice", "../AspireShop.BasketService/AspireShop.BasketService.csproj")
    .WithReference(basketCache)
    .WaitFor(basketCache);

builder.AddDotnetProject("frontend", "../AspireShop.Frontend/AspireShop.Frontend.csproj")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("https", url => url.DisplayText = "Online Store (HTTPS)")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Online Store (HTTP)")
    .WithHttpHealthCheck("/health")
    .WithReference(basketService)
    .WithReference(catalogService)
    .WaitFor(catalogService)
    .WithHttpCommand("/dev/simulate-traffic", "Simulate shopper traffic", commandName: "simulate-traffic", commandOptions: new()
    {
        Description = "Runs 20 simulated shoppers that browse the catalog, fill baskets, and check out, so traces, metrics, and structured logs populate.",
        IconName = "PeopleTeam",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });

builder.Build().Run();
