using MetricsApp.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

// Prometheus receives metrics over OTLP from the collector and stores histogram exemplars so Grafana can link points to traces
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.2.1")
    .WithBindMount("../prometheus", "/etc/prometheus", isReadOnly: true)
    .WithArgs("--web.enable-otlp-receiver", "--enable-feature=exemplar-storage", "--config.file=/etc/prometheus/prometheus.yml")
    .WithHttpEndpoint(targetPort: 9090)
    .WithHttpHealthCheck("/-/ready")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Prometheus");

var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithBindMount("../grafana/config", "/etc/grafana", isReadOnly: true)
    .WithBindMount("../grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithEnvironment("PROMETHEUS_ENDPOINT", prometheus.GetEndpoint("http"))
    // Lets Grafana link exemplars on the checkout dashboard to the trace in the Aspire dashboard
    .WithEnvironment("ASPIRE_DASHBOARD_URL", builder.Configuration["ASPNETCORE_URLS"]?.Split(';')[0] ?? "")
    .WithHttpEndpoint(targetPort: 3000)
    .WithHttpHealthCheck("/api/health")
    .WaitFor(prometheus)
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayText = "Grafana";
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        context.Urls.Add(new() { Url = "/d/checkout/checkout", DisplayText = "Checkout dashboard", Endpoint = context.GetEndpoint("http") });
    });

var collector = builder.AddOpenTelemetryCollector("otelcollector", "../otelcollector/config.yaml")
    .WithEnvironment("PROMETHEUS_ENDPOINT", $"{prometheus.GetEndpoint("http")}/api/v1/otlp")
    .WaitFor(prometheus);

builder.AddDotnetProject("app", "../MetricsApp/MetricsApp.csproj")
    .WithEnvironment("GRAFANA_URL", grafana.GetEndpoint("http"))
    .WithHttpHealthCheck("/health")
    .WaitFor(collector)
    .WithUrls(context =>
    {
        // Surface the console and the API reference (mapped in Development only); keep the raw endpoint URLs on the details page
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/", DisplayText = "Checkout console", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    })
    .WithHttpCommand("/dev/simulate-checkouts", "Simulate checkout traffic", commandName: "simulate-checkouts", commandOptions: new()
    {
        Description = "Runs 24 simulated shoppers who browse the catalog, fill carts, and check out through the API, so the dashboard, Prometheus, and Grafana populate.",
        IconName = "Cart",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    })
    .WithHttpCommand("/dev/card-issuer-outage", "Simulate card issuer outage", commandName: "card-issuer-outage", commandOptions: new()
    {
        Description = "For the next two minutes, card payments take 1-3 seconds and about a third of them are declined. Watch the checkout latency and decline panels react.",
        ConfirmationMessage = "Slow down and decline card payments for the next two minutes?",
        IconName = "PlugDisconnected",
        ResultMode = HttpCommandResultMode.Json
    });

builder.Build().Run();
