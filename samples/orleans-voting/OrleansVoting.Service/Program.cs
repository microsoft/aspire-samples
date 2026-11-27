using System.Text.Json;
using System.Text.Json.Serialization;
using OrleansVoting.Api;
using OrleansVoting.BoxOffice;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddKeyedRedisClient("redis");

// Every box office replica is also an Orleans silo. Orleans traces each grain call, so a reservation's trace
// follows it from silo to silo.
builder.UseOrleans();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddSingleton<BoxOfficeTelemetry>();
builder.Services.AddSingleton<BoxOfficeService>();
builder.Services.AddSingleton<OnSaleRushSimulator>();
builder.Services.AddScoped<FanSession>();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The web defaults accept numbers as strings, which documents every number as "number | string" in OpenAPI
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddBoxOfficeOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle(BoxOfficeOpenApi.Title));

    // Backs the AppHost's "Simulate on-sale rush" and "Reset events" commands
    app.MapBoxOfficeDevEndpoints();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBoxOfficeApi();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapDefaultEndpoints();

app.Run();
