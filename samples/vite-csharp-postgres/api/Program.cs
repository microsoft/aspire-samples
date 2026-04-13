using Scalar.AspNetCore;
using Api.Data;
using Api.Extensions;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add Aspire PostgreSQL with EF Core
builder.AddNpgsqlDbContext<TodoDbContext>("db");

// Add OpenAPI support
builder.Services.AddOpenApi();

// Register database initializer as hosted service
builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

app.UseFileServer();

if (app.Environment.IsDevelopment())
{
    // Map OpenAPI and Scalar
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();

// Map Todo endpoints
app.MapTodos();

app.Run();
