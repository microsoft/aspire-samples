using Scalar.AspNetCore;
using Api.Data;
using Api.Extensions;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add Azure SQL with EF Core
builder.AddSqlServerDbContext<ImageDbContext>("imagedb");

// Add Azure Storage services
builder.AddAzureBlobContainerClient("images");
builder.AddAzureQueueServiceClient("queues");

// Add OpenAPI support
builder.Services.AddOpenApi();

// Add antiforgery services for XSRF protection
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Register database initializer as hosted service
builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

// Add antiforgery middleware
app.UseAntiforgery();

app.UseFileServer();

if (app.Environment.IsDevelopment())
{
    // Map OpenAPI and Scalar
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

// Map Image endpoints
app.MapImages();

app.Run();
