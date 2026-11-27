#:package Aspire.Hosting.AppHost@17.0.0
#:package Aspire.Hosting.Redis@17.0.0
#:package Aspire.Hosting.Dotnet@17.0.0
#:property PublishAot=false

var builder = DistributedApplication.CreateBuilder(args);

// The optional HTTP profile in apphost.run.json keeps dashboard authentication but not transport encryption.
// Keep its dashboard, telemetry, and resource-service loopback endpoints private; do not forward them.

#pragma warning disable ASPIRETERMINAL001
// No persistent volume: use disposable data. The authenticated Redis REPL is for trusted dashboard users only.
var redis = builder.AddRedis("redis").WithRepl();

// Local demo: anyone who can reach this unauthenticated HTTP API can read, replace, or delete demo notes.
var api = builder.AddDotnetProject("api", "./api/Notes.Api.csproj")
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(redis)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Notes API")
    .WithUrls(context =>
    {
        // Scalar, the OpenAPI document and the seed endpoint are mapped in Development (run mode) only.
        var http = context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = http });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = http });
    })
    .WithHttpCommand("/dev/notes/seed", "Seed sample notes", commandName: "seed-notes", commandOptions: new()
    {
        Description = "POST /dev/notes/seed: writes five team notes for Slumber's List notes request. The welcome note is left alone.",
        IconName = "NoteAdd",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });

builder.AddDockerfile("slumber", "./slumber")
    .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api)
    .WithTerminal();
#pragma warning restore ASPIRETERMINAL001

builder.Build().Run();
