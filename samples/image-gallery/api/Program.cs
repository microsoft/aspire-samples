using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Api.Data;
using Api.Extensions;
using Api.Models;
using Api.Services;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add Azure SQL with EF Core
builder.AddSqlServerDbContext<ImageDbContext>("imagedb");

// Add Azure Storage services
builder.AddAzureBlobContainerClient("images");
builder.AddAzureQueueServiceClient("queues");

builder.Services.AddSingleton<GalleryTelemetry>();
builder.Services.AddScoped<PhotoIngestService>();
builder.Services.AddProblemDetails();

// The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Lumina Catalog Photos API",
            Version = "v1",
            Description = """
                Upload product photos for an e-commerce catalog. Originals are stored in Azure Blob Storage, metadata in Azure SQL,
                and a queue-triggered worker generates 300×300 catalog thumbnails. Uploads are limited to 10 MB (JPEG, PNG, GIF or WebP).
                Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Photos", Description = "Upload, list, download and delete catalog product photos." },
            new() { Name = "Security", Description = "Antiforgery tokens required by state-changing requests." },
            new() { Name = "Operations", Description = "Development-only operations behind the Aspire dashboard commands." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(ImageDto))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 42,
                    ["fileName"] = "field-chronograph-watch.jpg",
                    ["contentType"] = "image/jpeg",
                    ["size"] = 42783,
                    ["thumbnailProcessed"] = true,
                    ["uploadedAt"] = "2026-11-23T09:41:00Z"
                }
            ];
        }
        return Task.CompletedTask;
    });
});

// Add antiforgery services for XSRF protection
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Register database initializer as hosted service
builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

// Add antiforgery middleware
app.UseAntiforgery();

app.UseFileServer();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Lumina Catalog Photos API")
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));
    app.MapOperations();
}

app.MapDefaultEndpoints();

app.MapImages();

app.Run();
