using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DatabaseMigrations.ApiModel;
using DatabaseMigrations.ApiService;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<ClinicDbContext>("clinicdb");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppointmentTelemetry>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Clinic Appointments API",
            Version = "v1",
            Description = """
                Book and manage clinic appointments. The SQL Server schema is owned by EF Core migrations that the
                `migration` worker applies before this API starts. Errors are returned as
                [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Appointments", Description = "Book, look up, and cancel patient visits." },
            new() { Name = "Clinicians", Description = "Clinicians and their upcoming workload." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(BookAppointmentRequest))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["clinicianId"] = 2,
                    ["patientId"] = 5,
                    ["startsAt"] = "2026-12-01T14:30:00+00:00",
                    ["durationMinutes"] = 30,
                    ["reason"] = "Chest pain follow-up"
                }
            ];
        }
        else if (context.JsonTypeInfo.Type == typeof(AppointmentDto))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 31,
                    ["clinicianId"] = 2,
                    ["clinicianName"] = "Dr. Daniel Whitaker",
                    ["specialty"] = "Cardiology",
                    ["patientId"] = 5,
                    ["patientName"] = "Isabella Rossi",
                    ["startsAt"] = "2026-12-01T14:30:00+00:00",
                    ["durationMinutes"] = 30,
                    ["reason"] = "Chest pain follow-up",
                    ["status"] = "Scheduled",
                    ["cancelledAt"] = null
                }
            ];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Contoso Clinic Appointments API"));

    // Backs the AppHost's "Reset database" command: drop the database so the migration worker rebuilds it from scratch
    app.MapPost("/dev/reset-database", async (ClinicDbContext db, ILogger<Program> logger) =>
        {
            var databaseName = db.Database.GetDbConnection().Database;
            await db.Database.EnsureDeletedAsync();
            SqlConnection.ClearAllPools();
            logger.DatabaseDropped(databaseName);

            return TypedResults.Ok(new { database = databaseName, status = "dropped" });
        })
        .ExcludeFromDescription();
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

app.MapClinicApi();
app.MapDefaultEndpoints();

app.Run();
