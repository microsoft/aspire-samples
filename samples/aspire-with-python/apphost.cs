#pragma warning disable ASPIREBROWSERLOGS001
#pragma warning disable ASPIREJAVASCRIPT001

#:sdk Aspire.AppHost.Sdk@13.4.0
#:package Aspire.Hosting.JavaScript@13.4.0
#:package Aspire.Hosting.Browsers@13.4.0-preview.1.26281.18
#:package Aspire.Hosting.Python@13.4.0
#:package Aspire.Hosting.Redis@13.4.0

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var app = builder.AddUvicornApp("app", "./app", "main:app")
    .WithUv()
    .WithExternalHttpEndpoints()
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health");

builder.AddViteApp("frontend", "./frontend")
    .WithReference(app)
    .WaitFor(app)
    .WithBrowserLogs()
    .PublishAsStaticWebsite("/api", app);

builder.Build().Run();
