var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

// Redis holds the Orleans cluster membership table and each event's persisted seat inventory
var orleans = builder.AddOrleans("ticketing-cluster")
    .WithClustering(redis)
    .WithGrainStorage("inventory", redis);

builder.AddDotnetProject("boxoffice", "../OrleansVoting.Service/OrleansVoting.Service.csproj")
    .WithReference(orleans)
    .WaitFor(redis)
    .WithReplicas(3)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        // Lead with the box office UI and the API reference (mapped in Development only); keep the raw endpoints,
        // including the Orleans silo and gateway ports, on the details page
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/", DisplayText = "Box office", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    })
    .WithHttpCommand("/dev/simulate-rush", "Simulate on-sale rush", commandName: "simulate-rush", commandOptions: new()
    {
        Description = "Sends 400 simulated fans at The Midnight Arcade's on-sale within 12 seconds. They contend for the event's inventory grain, sell out sections, and fall back to others, so traces, metrics, and structured logs populate.",
        IconName = "PeopleQueue",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    })
    .WithHttpCommand("/dev/reset", "Reset events", commandName: "reset-events", commandOptions: new()
    {
        Description = "Cancels every reservation and returns all events to full availability.",
        ConfirmationMessage = "Cancel every reservation and put all seats back on sale?",
        IconName = "ArrowReset",
        ResultMode = HttpCommandResultMode.Json
    });

builder.Build().Run();
