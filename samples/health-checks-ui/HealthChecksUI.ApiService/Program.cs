using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HealthChecksUI.ApiService;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// The payment store's health check is registered below with the gateway's other dependencies
builder.AddRedisClient("cache", settings => settings.DisableHealthChecks = true);

var dependencies = new PaymentDependencies();
builder.Services.AddSingleton(dependencies);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentsTelemetry>();
builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddSingleton<PaymentGateway>();
builder.Services.AddTransient<PaymentTrafficSimulator>();

builder.Services.AddHealthChecks()
    .AddPaymentDependencyChecks(dependencies);

// Check the dependencies in the background and publish each report to the status monitor (and the status page)
builder.Services.AddSingleton<GatewayStatusMonitor>();
builder.Services.AddSingleton<IHealthCheckPublisher>(sp => sp.GetRequiredService<GatewayStatusMonitor>());
builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Delay = TimeSpan.FromSeconds(1);
    options.Period = GatewayStatusMonitor.CheckInterval;
    options.Predicate = check => check.Tags.Contains(PaymentHealthCheckExtensions.DependencyTag);
});

builder.Services.AddProblemDetails();

// The web defaults accept numbers as strings, which documents every number as "number | string" in OpenAPI
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Pay Gateway API",
            Version = "v1",
            Description = """
                Authorize card payments and check the health of the payments gateway. The gateway depends on a card processor,
                a fraud scoring service, an FX rates feed, and a Redis payment store, each monitored by an ASP.NET Core health check.
                Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Payments", Description = "Authorize card payments and look up processed payments." },
            new() { Name = "Status", Description = "The live status of the gateway's dependencies, for status pages." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(PaymentRequest))
        {
            schema.Examples = [new JsonObject { ["merchantId"] = "alpine-ski-house", ["amount"] = 184.20m, ["currency"] = "EUR", ["cardBrand"] = "Visa" }];
        }
        else if (type == typeof(Payment))
        {
            schema.Examples = [ExamplePayment()];
        }
        else if (type == typeof(GatewayStatus))
        {
            schema.Examples = [ExampleStatus()];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseStatusCodePages();

app.UseRequestTimeouts();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Contoso Pay Gateway API"));

    // Back the AppHost's "Simulate payments traffic", "Simulate dependency incident", and "Restore dependencies" commands
    app.MapDevEndpoints();
}
else
{
    app.UseExceptionHandler();
}

app.MapPaymentsApi();

app.MapDefaultEndpoints();

app.Run();

static JsonObject ExamplePayment() => new()
{
    ["id"] = "pay_3f9a1c7e5b2d4f60",
    ["merchantId"] = "alpine-ski-house",
    ["merchantName"] = "Alpine Ski House",
    ["amount"] = 184.20m,
    ["currency"] = "EUR",
    ["cardBrand"] = "Visa",
    ["status"] = "Approved",
    ["authorizationCode"] = "8C41F2",
    ["declineReason"] = null,
    ["settlementAmountUsd"] = 199.71m,
    ["fxRate"] = 1.084213m,
    ["riskScore"] = 18,
    ["fallbacks"] = new JsonArray(),
    ["createdAt"] = "2026-11-26T14:02:31.412Z",
    ["processingMs"] = 213.4
};

static JsonObject ExampleStatus() => new()
{
    ["status"] = "Degraded",
    ["summary"] = "Degraded performance",
    ["checkedAt"] = "2026-11-26T14:02:35.008Z",
    ["checkIntervalSeconds"] = 5,
    ["components"] = new JsonArray(
        new JsonObject
        {
            ["name"] = "card-processor",
            ["displayName"] = "Card processor",
            ["provider"] = "Northwind Card Network",
            ["isCritical"] = true,
            ["status"] = "Operational",
            ["description"] = "Northwind Card Network responded in 112 ms.",
            ["latencyMs"] = 113,
            ["availabilityPercent"] = 100.0,
            ["history"] = new JsonArray("Operational", "Operational", "Operational")
        },
        new JsonObject
        {
            ["name"] = "fx-rates",
            ["displayName"] = "FX rates",
            ["provider"] = "Fabrikam FX",
            ["isCritical"] = false,
            ["status"] = "Degraded",
            ["description"] = "Fabrikam FX returned 503 Service Unavailable. Non-USD payments settle at the last cached rates.",
            ["latencyMs"] = 41,
            ["availabilityPercent"] = 100.0,
            ["history"] = new JsonArray("Operational", "Degraded", "Degraded")
        }),
    ["recentChanges"] = new JsonArray(new JsonObject
    {
        ["component"] = "fx-rates",
        ["displayName"] = "FX rates",
        ["from"] = "Operational",
        ["to"] = "Degraded",
        ["changedAt"] = "2026-11-26T14:02:25.004Z",
        ["description"] = "Fabrikam FX returned 503 Service Unavailable. Non-USD payments settle at the last cached rates."
    })
};
