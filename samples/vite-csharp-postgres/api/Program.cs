using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Api.Data;
using Api.Extensions;
using Api.Models;
using Api.Services;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.SetSampler(new SkipHealthProbeQueriesSampler()));

// Aspire PostgreSQL + EF Core: connection string injection, health check, retries, and Npgsql traces and metrics
builder.AddNpgsqlDbContext<ServiceDeskDbContext>("servicedesk");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServiceDeskTelemetry>();
builder.Services.AddScoped<RequestQueue>();
builder.Services.AddScoped<QueueSimulator>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums travel as names ("Critical", "InProgress"), and integers aren't documented as "integer | string"
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Service Desk API",
            Version = "v1",
            Description = """
                The IT service desk request queue. Employees raise requests for hardware, software, access and network
                problems; agents pick them up, resolve them, and are measured against a priority-based SLA.
                Requests are stored in PostgreSQL with EF Core. Errors are returned as
                [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Requests", Description = "Raise, assign, resolve and reopen service requests." },
            new() { Name = "Queue", Description = "Headline numbers for the whole queue." },
            new() { Name = "Demo", Description = "Development-only operations behind the Aspire dashboard commands." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(ServiceRequestDto))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 1002,
                    ["title"] = "Replace failing SSD in build agent BLD-07",
                    ["category"] = "Hardware",
                    ["priority"] = "Critical",
                    ["status"] = "InProgress",
                    ["requestedBy"] = "Raj Mehta",
                    ["assignedTo"] = "Kenji Watanabe",
                    ["createdAt"] = "2026-11-26T08:15:00Z",
                    ["dueAt"] = "2026-11-26T12:15:00Z",
                    ["resolvedAt"] = null,
                    ["isOverdue"] = false
                }
            ];
        }
        else if (type == typeof(CreateServiceRequest))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["title"] = "Replace failing SSD in build agent BLD-07",
                    ["requestedBy"] = "Raj Mehta",
                    ["category"] = "Hardware",
                    ["priority"] = "Critical"
                }
            ];
        }
        else if (type == typeof(UpdateServiceRequest))
        {
            schema.Examples = [new JsonObject { ["status"] = "InProgress", ["assignedTo"] = "Kenji Watanabe" }];
        }
        else if (type == typeof(QueueSummary))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["open"] = 13,
                    ["unassigned"] = 8,
                    ["inProgress"] = 5,
                    ["critical"] = 2,
                    ["overdue"] = 3,
                    ["resolvedLast24Hours"] = 4
                }
            ];
        }

        return Task.CompletedTask;
    });
});

// Creates the database schema and primes the open-requests metric before the API takes traffic
builder.Services.AddHostedService<DatabaseInitializer>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

// Serves the Vite build output that publish mode copies into wwwroot
app.UseFileServer();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Contoso Service Desk API")
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));
    app.MapDemoEndpoints();
}

app.MapDefaultEndpoints();

app.MapServiceRequests();

app.Run();