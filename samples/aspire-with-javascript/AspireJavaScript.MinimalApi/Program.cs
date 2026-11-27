using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AspireJavaScript.MinimalApi.Shipments;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddCors();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
    // The web defaults accept numbers as strings, which documents every number as "number | string" in OpenAPI
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShipmentStore>();
builder.Services.AddSingleton<ShipmentTelemetry>();
builder.Services.AddSingleton<ShipmentTracker>();
builder.Services.AddTransient<TrackingLookupSimulator>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Shipment Tracking API",
            Version = "v1",
            Description = """
                Track Contoso shipments across carriers. The tracking board lists every shipment with its route, carrier,
                status, ETA, and last scan; a lookup asks the carrier for the latest scan and returns the full scan history.
                Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Shipments", Description = "The shipment board, status filters, and per-shipment scan history." }
        };
        return Task.CompletedTask;
    });

    // The status filter is bound as a string (so bad values get a friendly 400), so document its allowed values here
    options.AddOperationTransformer((operation, _, _) =>
    {
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.Name == "status" && parameter.Schema is OpenApiSchema status)
            {
                status.Enum = [.. ShipmentStatusNames.All.Select(name => JsonValue.Create(name)!)];
                parameter.Example = "exception";
            }
            else if (parameter.Name == "id" && parameter.Schema is OpenApiSchema id)
            {
                id.Pattern = "^CTS-[0-9]{8}$";
                parameter.Example = "CTS-48214101";
            }
        }
        return Task.CompletedTask;
    });

    // Show a realistic shipment in every response schema
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(ShipmentStatus) || context.JsonTypeInfo.Type == typeof(ShipmentStatus?))
        {
            // The board's nullable status filter otherwise adds null to every use of the shared enum schema
            schema.Type = JsonSchemaType.String;
            schema.Enum = [.. ShipmentStatusNames.All.Select(name => JsonValue.Create(name)!)];
            return Task.CompletedTask;
        }

        object? example = context.JsonTypeInfo.Type switch
        {
            var t when t == typeof(ScanEvent) => OpenApiExamples.LatestScan,
            var t when t == typeof(Shipment) => OpenApiExamples.Shipment,
            var t when t == typeof(ShipmentDetail) => OpenApiExamples.Detail,
            var t when t == typeof(ShipmentBoard) => OpenApiExamples.Board,
            _ => null
        };
        if (example is not null)
        {
            var json = context.ApplicationServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            schema.Examples = [JsonSerializer.SerializeToNode(example, example.GetType(), json)!];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Contoso Shipment Tracking API")
        .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch));
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();
app.UseHttpsRedirection();

// SAMPLE ONLY: the wide-open CORS policy is gated on Development so the pattern
// does not follow the sample into deployments that ship cookies or state-changing
// endpoints. Real apps should configure an explicit origin allow-list via
// WithOrigins(...) and only the headers/methods they actually need.
if (app.Environment.IsDevelopment())
{
    app.UseCors(static builder =>
        builder.AllowAnyMethod()
            .AllowAnyHeader()
            .AllowAnyOrigin());
}

app.MapShipmentsApi();

if (app.Environment.IsDevelopment())
{
    // Backs the AppHost's "Simulate tracking lookups" command
    app.MapPost("/dev/simulate-lookups", async (TrackingLookupSimulator simulator, CancellationToken cancellationToken) =>
        TypedResults.Ok(await simulator.RunAsync(visitors: 10, cancellationToken)))
        .ExcludeFromDescription();
}

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();

/// <summary>Example payloads for the API reference.</summary>
internal static class OpenApiExamples
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 14, 30, 0, TimeSpan.Zero);

    public static readonly ScanEvent LatestScan = new(Now.AddHours(-6), "Buffalo, NY", "Held by customs, commercial invoice required");

    public static readonly Shipment Shipment = new(
        "CTS-48214101", "Newark, NJ", "Toronto, ON", "Tailspin Air Cargo", ShipmentStatus.Exception,
        Now.AddHours(40), 22.0m, 43, LatestScan);

    public static readonly ShipmentDetail Detail = new(Shipment,
    [
        LatestScan,
        new(Now.AddHours(-7.5), "Buffalo, NY", "Arrived at sort facility"),
        new(Now.AddHours(-14.4), "Newark, NJ", "Departed origin facility"),
        new(Now.AddHours(-16.3), "Newark, NJ", "Picked up by Tailspin Air Cargo"),
        new(Now.AddHours(-21.3), "Newark, NJ", "Shipping label created")
    ]);

    public static readonly ShipmentBoard Board = new(ShipmentStatus.Exception, 20,
        [
            new(ShipmentStatus.LabelCreated, 3),
            new(ShipmentStatus.InTransit, 6),
            new(ShipmentStatus.OutForDelivery, 4),
            new(ShipmentStatus.Delivered, 4),
            new(ShipmentStatus.Exception, 3)
        ],
        [Shipment]);
}
