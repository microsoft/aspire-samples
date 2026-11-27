using HealthChecksUI.Web;
using HealthChecksUI.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiServiceUri = new Uri("https+http://apiservice");
builder.Services.AddHttpClient<PaymentsApiClient>(client => client.BaseAddress = apiServiceUri);
builder.Services.AddSingleton<StatusPageTelemetry>();

// The status page must stay up during a payments incident, so its check of the gateway only calls the gateway's
// liveness endpoint, not its health endpoint
builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri(apiServiceUri, "/alive"), name: "payments-api");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();

app.UseAntiforgery();

app.UseRequestTimeouts();
app.UseOutputCache();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
