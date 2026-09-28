#:package Aspire.Hosting.Redis@14.0.0-preview.1.26475.14
#:sdk Aspire.AppHost.Sdk@14.0.0-preview.1.26475.14
#:property AspireUseCliBundle=true

var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRETERMINAL001
var redis = builder.AddRedis("redis").WithRepl();

var api = builder.AddProject("api", "./api/Notes.Api.csproj")
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis);

builder.AddDockerfile("slumber", "./slumber")
    .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api)
    .WithTerminal();
#pragma warning restore ASPIRETERMINAL001

builder.Build().Run();
