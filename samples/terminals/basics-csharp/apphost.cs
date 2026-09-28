#:package Aspire.Hosting.Redis@13.6.0
#:sdk Aspire.AppHost.Sdk@13.6.0
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
