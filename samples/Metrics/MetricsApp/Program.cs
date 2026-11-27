using System.Text.Json.Serialization;
using MetricsApp;
using MetricsApp.Checkout;
using MetricsApp.Simulation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme);
builder.Services.AddAuthorizationBuilder();

builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("AppDb"));

builder.Services.AddIdentityCore<MyUser>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddApiEndpoints();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CheckoutTelemetry>();
builder.Services.AddSingleton<CartStore>();
builder.Services.AddSingleton<PaymentGateway>();
builder.Services.AddSingleton<OrderFeed>();
builder.Services.AddSingleton<CheckoutService>();

builder.Services.AddProblemDetails();

// The web defaults accept numbers as strings, which documents every number as "number | string" in OpenAPI
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddCheckoutOpenApi();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCheckoutSimulation();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle(OpenApiConfiguration.Title));
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapDefaultEndpoints();

var api = app.MapGroup("/api");
api.MapIdentityApi<MyUser>().WithTags("Account");
api.MapCheckoutApi();
api.MapGet("/startup", (IConfiguration configuration) => new { GrafanaUrl = configuration["GRAFANA_URL"] })
    .ExcludeFromDescription();

app.MapFallbackToFile("index.html");

// SAMPLE ONLY -- DO NOT KEEP IN PRODUCTION.
// Seeds a known test user (test@contoso.com / Password1!) so the demo is usable
// without a sign-up flow, and maps the endpoints behind the AppHost's simulation
// commands. Gating on Development ensures neither follows the sample into other
// environments if the in-memory DB is swapped for a persistent one.
if (app.Environment.IsDevelopment())
{
    app.MapCheckoutSimulation();
    await Auth.InitializeTestUserAsync(app.Services);
}

app.Run();
