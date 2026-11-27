using Microsoft.Extensions.Diagnostics.HealthChecks;
using AspireShop.Frontend.Components;
using AspireShop.Frontend.Services;
using AspireShop.GrpcBasket;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpForwarderWithServiceDiscovery();

builder.Services.AddSingleton<FrontendTelemetry>();
builder.Services.AddTransient<ShopperTrafficSimulator>();

builder.Services.AddHttpServiceReference<CatalogServiceClient>("https+http://catalogservice", healthRelativePath: "health");

var isHttps = builder.Configuration["DOTNET_LAUNCH_PROFILE"] == "https";

builder.Services.AddSingleton<BasketServiceClient>()
    .AddGrpcServiceReference<Basket.BasketClient>($"{(isHttps ? "https" : "http")}://basketservice", failureStatus: HealthStatus.Degraded);

builder.Services.AddRazorComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

// Prices are in USD, so format them consistently regardless of the host's culture
app.UseRequestLocalization("en-US");

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseStaticFiles();

app.MapRazorComponents<App>();

app.MapForwarder("/catalog/images/{id}", "https+http://catalogservice", "/api/v1/catalog/items/{id}/image");

if (app.Environment.IsDevelopment())
{
    // Backs the AppHost's "Simulate shopper traffic" command
    app.MapPost("/dev/simulate-traffic", async (ShopperTrafficSimulator simulator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await simulator.RunAsync(shoppers: 20, cancellationToken)));
}

app.MapDefaultEndpoints();

app.Run();
