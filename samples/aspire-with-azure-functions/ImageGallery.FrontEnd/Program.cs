using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImageGallery.FrontEnd.Claims;
using ImageGallery.FrontEnd.Components;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.SetSampler(new SkipIdlePollingSampler()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.AddAzureBlobServiceClient("blobs");
builder.AddAzureQueueServiceClient("queues");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ClaimIntakeTelemetry>();
builder.Services.AddSingleton<ClaimPhotoCatalog>();
builder.Services.AddSingleton<ClaimPhotoIntake>();
builder.Services.AddHostedService<PhotoResultsWorker>();

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    // The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddOpenApi(options =>
{
    // The Blazor pages are HTML, not part of the API contract
    options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Claim Photo Intake API",
            Version = "v1",
            Description = """
                Attach damage photos to open insurance claims. Photos are stored in Azure Blob Storage, where a blob-triggered
                Azure Function checks each photo's resolution, renders a thumbnail and queues the result back to the intake app.
                Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Claims", Description = "Open claims that accept photos." },
            new() { Name = "Photos", Description = "Upload claim photos and download originals and thumbnails." },
            new() { Name = "Operations", Description = "Development-only operations behind the Aspire dashboard commands." }
        };
        return Task.CompletedTask;
    });
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(ClaimPhotoDto))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["claimNumber"] = "CLM-26-10482",
                    ["fileName"] = "rear-bumper-damage.jpg",
                    ["displayName"] = "Rear bumper damage",
                    ["status"] = "ready",
                    ["sizeBytes"] = 182_406,
                    ["width"] = 1600,
                    ["height"] = 1200,
                    ["uploadedAt"] = "2026-11-24T16:05:12Z",
                    ["processedAt"] = "2026-11-24T16:05:15Z",
                    ["photoUrl"] = "/api/claims/CLM-26-10482/photos/rear-bumper-damage.jpg",
                    ["thumbnailUrl"] = "/api/claims/CLM-26-10482/photos/rear-bumper-damage.jpg/thumbnail?v=1795968315000"
                }
            ];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Contoso Claim Photo Intake API")
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
        .HideDeveloperTools()
        .DisableAgent());
    app.MapIntakeOperations();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapClaimPhotoApi();

app.Run();
