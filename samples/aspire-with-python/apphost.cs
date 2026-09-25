#:sdk Aspire.AppHost.Sdk@13.6.0-preview.1.26474.10
#:package Aspire.Hosting.JavaScript@13.6.0-preview.1.26474.10
#:package Aspire.Hosting.Python@13.6.0-preview.1.26474.10
#:package Aspire.Hosting.Redis@13.6.0-preview.1.26474.10

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var app = builder.AddUvicornApp("app", "./app", "main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health");

var frontend = builder.AddViteApp("frontend", "./frontend")
    .WithReference(app)
    .WaitFor(app);

app.PublishWithContainerFiles(frontend, "./static");

builder.Build().Run();
