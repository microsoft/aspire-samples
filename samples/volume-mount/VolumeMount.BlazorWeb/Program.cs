using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using VolumeMount.BlazorWeb.Components;
using VolumeMount.BlazorWeb.Inspections;
using VolumeMount.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<InspectionDbContext>("sqldb");
builder.AddAzureBlobContainerClient("attachments");

builder.Services.AddSingleton<InspectionTelemetry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InspectionService>();
builder.Services.AddScoped<SampleData>();

builder.Services.AddRazorComponents();

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
            Title = "Equipment Inspections API",
            Version = "v1",
            Description = """
                Record equipment inspections and their photos and documents. Inspection records are stored in SQL Server and
                attachments in Azure Blob Storage (Azurite locally); both run on Docker volumes, so data survives restarts.
                Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Inspections", Description = "Inspection records in SQL Server." },
            new() { Name = "Attachments", Description = "Photos and documents in Azure Blob Storage." },
            new() { Name = "Storage", Description = "What's persisted on the data volumes." },
            new() { Name = "Development", Description = "Helpers that are only mapped in the Development environment." }
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Equipment Inspections API")
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.MapInspectionsApi(app.Environment);
app.MapDefaultEndpoints();

app.Run();
