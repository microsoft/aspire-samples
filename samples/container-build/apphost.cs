#:package Aspire.Hosting.AppHost@17.0.0
#:property PublishAot=false

var builder = DistributedApplication.CreateBuilder(args);

var goVersion = builder.AddParameter("goversion", "1.27.4", publishValueAsDefault: true);

IResourceBuilder<ContainerResource> ginapp;

if (builder.ExecutionContext.IsPublishMode)
{
    // Production build: multi-stage Dockerfile for optimized image
    ginapp = builder.AddDockerfile("ginapp", "./ginapp")
        .WithBuildArg("GO_VERSION", goVersion);
}
else
{
    // Development build: use Air for hot reload with bind mount
    ginapp = builder.AddDockerfile("ginapp", "./ginapp", "Dockerfile.dev")
        .WithBuildArg("GO_VERSION", goVersion)
        .WithBindMount("./ginapp", "/app");
}

ginapp
    .WithHttpEndpoint(targetPort: 5555, env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithOtlpExporter()
    .WithDeveloperCertificateTrust(trust: true)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Feature flags API")
    .WithUrls(context =>
    {
        var endpoint = context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    });

if (builder.ExecutionContext.IsPublishMode)
{
    ginapp
        .WithEnvironment("GIN_MODE", "release")
        // Trust all proxies when running behind a reverse proxy. If deploying to an environment
        // without a reverse proxy that ensures X-Forwarded-* headers are not forwarded from clients,
        // this should be removed.
        .WithEnvironment("TRUSTED_PROXIES", "all");
}
else
{
    // Development-only admin endpoints back these dashboard commands so the telemetry pages fill up quickly.
    ginapp
        .WithEnvironment("FLAGS_ADMIN_ENABLED", "true")
        .WithHttpCommand("/admin/simulate", "Simulate traffic", commandName: "simulate-traffic", commandOptions: new()
        {
            Description = "POST /admin/simulate: 40 storefront shoppers evaluate their page's flags through the API.",
            IconName = "Play",
            IsHighlighted = true,
            ResultMode = HttpCommandResultMode.Json
        })
        .WithHttpCommand("/flags/checkout.express-pay/rollout/advance", "Advance express-pay rollout", commandName: "advance-rollout", commandOptions: new()
        {
            Description = "POST /flags/checkout.express-pay/rollout/advance: move the express checkout rollout to its next stage (5, 10, 25, 50, 100%).",
            ConfirmationMessage = "Roll Apple Pay and Google Pay express checkout out to more shoppers?",
            IconName = "ArrowTrending",
            ResultMode = HttpCommandResultMode.Json
        })
        .WithHttpCommand("/admin/reset", "Reset flags", commandName: "reset-flags", commandOptions: new()
        {
            Description = "POST /admin/reset: restore the seeded storefront flag catalog.",
            ConfirmationMessage = "Restore the seeded flag catalog? Flag changes made since startup will be lost.",
            IconName = "ArrowReset",
            ResultMode = HttpCommandResultMode.Json
        });
}

builder.Build().Run();
