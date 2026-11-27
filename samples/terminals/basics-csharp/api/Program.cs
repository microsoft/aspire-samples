using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Notes.Api;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.SetSampler(new SkipHealthCheckPingSampler()));
builder.AddRedisClient("redis");
builder.Services.AddSingleton<INotesStore, RedisNotesStore>();
builder.Services.AddSingleton<NotesTelemetry>();
builder.Services.AddProblemDetails();
// The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddNotesOpenApi();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024);

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed or oversized request bodies are client errors, so don't log them as unhandled exceptions.
    SuppressDiagnosticsCallback = context => context.Exception is BadHttpRequestException,
    ExceptionHandler = async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()!.Error;
        var (status, title, detail) = error switch
        {
            RedisException => (503, "Redis is unavailable", "The notes store couldn't be reached. Check the redis resource in the Aspire dashboard."),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Invalid request", badRequest.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => "Send at most 10 KiB of JSON.",
                StatusCodes.Status415UnsupportedMediaType => "Send the note as application/json.",
                _ => """Send a JSON object with a string "text" property, such as {"text": "Hello from Slumber!"}."""
            }),
            _ => (500, "An unexpected error occurred", null)
        };
        await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
    }
});
// Body-less error responses, such as 415 for a non-JSON request body, also become problem details.
app.UseStatusCodePages();
app.MapDefaultEndpoints();

// The API reference and the endpoint behind the "Seed sample notes" dashboard command are local-development features.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Terminal Notes API")
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));
    app.MapDemoEndpoints();
}

app.MapNotesApi();

app.Run();

public partial class Program;
