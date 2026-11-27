var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

// Redis holds the gateway's recent payments and the status page's output cache
var cache = builder.AddRedis("cache");

var apiService = builder.AddDotnetProject("apiservice", "../HealthChecksUI.ApiService/HealthChecksUI.ApiService.csproj")
    .WithReference(cache)
    .WaitFor(cache)
    // Unlike WithHttpHealthCheck, this also reports Degraded, so the dashboard shows when the gateway is running on fallbacks
    .WithHealthReportCheck("/health")
    .WithHttpProbe(ProbeType.Liveness, "/alive")
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
    })
    .WithHttpCommand("/dev/simulate-traffic", "Simulate payments traffic", commandName: "simulate-traffic", commandOptions: new()
    {
        Description = "Sends 40 card payments from six merchants through the gateway over about six seconds, so traces, metrics, and structured logs populate.",
        IconName = "Payment",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    })
    .WithHttpCommand("/dev/dependencies", "Simulate dependency incident", commandName: "simulate-incident", commandOptions: new()
    {
        Description = "Makes one of the gateway's third-party dependencies fail or slow down. Watch its health check, the dashboard, the status page, and HealthChecksUI react.",
        IconName = "PlugDisconnected",
        Arguments =
        [
            new()
            {
                Name = "dependency",
                Label = "Dependency",
                InputType = InputType.Choice,
                Required = true,
                Value = "card-processor",
                Options =
                [
                    new("card-processor", "Card processor (critical)"),
                    new("fraud-scoring", "Fraud scoring (has a fallback)"),
                    new("fx-rates", "FX rates (has a fallback)")
                ]
            },
            new()
            {
                Name = "issue",
                Label = "Issue",
                InputType = InputType.Choice,
                Required = true,
                Value = "outage",
                Options =
                [
                    new("outage", "Outage: every call fails"),
                    new("slow", "Slow responses: calls take up to a second")
                ]
            }
        ],
        PrepareRequest = context =>
        {
            var dependency = context.Arguments.GetString("dependency");
            var issue = context.Arguments.GetString("issue");
            context.Request.RequestUri = new Uri(context.Request.RequestUri!, $"/dev/dependencies/{dependency}/{issue}");
            return Task.CompletedTask;
        },
        ResultMode = HttpCommandResultMode.Json
    })
    .WithHttpCommand("/dev/dependencies/restore", "Restore dependencies", commandName: "restore-dependencies", commandOptions: new()
    {
        Description = "Ends every simulated incident, so the dependencies respond normally again.",
        ConfirmationMessage = "Restore every dependency to normal operation?",
        IconName = "PlugConnected",
        ResultMode = HttpCommandResultMode.Json
    });

var webFrontend = builder.AddDotnetProject("webfrontend", "../HealthChecksUI.Web/HealthChecksUI.Web.csproj")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    // The status page reports incidents, so it waits for the gateway to start, not for it to be healthy
    .WaitForStart(apiService)
    .WithHttpProbe(ProbeType.Liveness, "/alive")
    .WithHttpHealthCheck("/health")
    .WithFriendlyUrls("Status page")
    .WithExternalHttpEndpoints();

var healthChecksUI = builder.AddHealthChecksUI("healthchecksui")
    .WithReference(apiService, displayName: "Payments gateway (apiservice)")
    .WithReference(webFrontend, displayName: "Status page (webfrontend)")
    .WithFriendlyUrls("HealthChecksUI Dashboard", "http")
    .WithHttpProbe(ProbeType.Liveness, "/")
    // This will make the HealthChecksUI dashboard available from external networks when deployed.
    // In a production environment, you should consider adding authentication to the ingress layer
    // to restrict access to the dashboard.
    .WithExternalHttpEndpoints();

if (builder.ExecutionContext.IsRunMode)
{
    healthChecksUI.WithHostPort(7230);
}

builder.Build().Run();


static class UrlHelpers
{
    extension<T>(IResourceBuilder<T> builder) where T : IResource
    {
        public IResourceBuilder<T> WithFriendlyUrls(string? displayText = null, string? endpointName = null, string? path = null)
        {
            return builder.WithUrls(c =>
            {
                List<string?> endpointNames = [endpointName, "https", "http"];
                var endpoint = endpointNames
                    .Where(n => n is not null)
                    .Select(n => c.GetEndpoint(n!))
                    .FirstOrDefault(e => e?.Exists ?? false);

                if (endpoint is null) return;

                displayText ??= builder.Resource.Name;
                foreach (var url in c.Urls)
                {
                    url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                }

                c.Urls.Add(new()
                {
                    Endpoint = endpoint,
                    DisplayText = displayText,
                    DisplayLocation = UrlDisplayLocation.SummaryAndDetails,
                    Url = path ?? "/"
                });
                
            });
        }
    }
}
