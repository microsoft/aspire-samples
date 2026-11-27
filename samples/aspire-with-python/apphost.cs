#:package Aspire.Hosting.AppHost@17.0.0
#:package Aspire.Hosting.JavaScript@17.0.0
#:package Aspire.Hosting.Python@17.0.0
#:package Aspire.Hosting.Redis@17.0.0
#:property PublishAot=false

var builder = DistributedApplication.CreateBuilder(args);

// Redis sits in front of the (simulated) warehouse system: availability reads are cache-aside.
var cache = builder.AddRedis("cache");

// The FastAPI inventory API exports traces, metrics and logs to the dashboard (see app/telemetry.py).
var app = builder.AddUvicornApp("app", "./app", "main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        var endpoint = context.GetEndpoint("http");
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    });

if (builder.ExecutionContext.IsRunMode)
{
    // Dev-only cache administration and demo endpoints, plus dashboard buttons that drive them,
    // so cache hits, misses and their telemetry can be produced without touching the UI.
    app.WithEnvironment("ENABLE_DEMO_ENDPOINTS", "true")
        .WithHttpCommand("/api/cache/warm", "Warm cache", commandName: "warm-cache", commandOptions: new()
        {
            Description = "POST /api/cache/warm: load every SKU from the warehouse system in one query and cache it.",
            IconName = "Flash",
            IsHighlighted = true,
            ResultMode = HttpCommandResultMode.Json
        })
        .WithHttpCommand("/api/cache", "Flush cache", commandName: "flush-cache", commandOptions: new()
        {
            Description = "DELETE /api/cache: remove every cached availability entry so the next lookups miss.",
            Method = HttpMethod.Delete,
            ConfirmationMessage = "Remove every cached availability entry from Redis?",
            IconName = "DeleteDismiss",
            IsHighlighted = true,
            ResultMode = HttpCommandResultMode.Json
        })
        .WithHttpCommand("/api/demo/simulate", "Simulate shoppers", commandName: "simulate-shoppers", commandOptions: new()
        {
            Description = "POST /api/demo/simulate: 40 shoppers check availability (cache hits and misses), some reserve units, and one tries to reserve a sold-out tent (409).",
            IconName = "ShoppingBag",
            ResultMode = HttpCommandResultMode.Json
        });
}

// Browser telemetry: the Vite dev server gets the dashboard's OTLP/HTTP endpoint and API key,
// and proxies same-origin `/otlp/*` requests from the browser to it (see frontend/vite.config.ts).
var frontend = builder.AddViteApp("frontend", "./frontend")
    .WithReference(app)
    .WaitFor(app)
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Availability board");

app.PublishWithContainerFiles(frontend, "./static");

builder.Build().Run();
