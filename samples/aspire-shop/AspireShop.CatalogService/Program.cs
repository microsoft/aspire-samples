using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AspireShop.CatalogDb;
using AspireShop.CatalogService;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

builder.Services.AddSingleton<CatalogTelemetry>();
builder.Services.AddProblemDetails();

// The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Aspire Shop Catalog API",
            Version = "v1",
            Description = """
                Browse the Aspire Shop product catalog. Items are stored in PostgreSQL and paged with
                keyset pagination. Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Catalog", Description = "Products, brands, and product images." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        var item = new JsonObject
        {
            ["id"] = 6,
            ["name"] = "Aspire All-Over Print Hoodie",
            ["description"] = "A sky-blue pullover hoodie with an all-over print of repeating Aspire logos.",
            ["price"] = 52.00m,
            ["availableStock"] = 100,
            ["badge"] = "New arrival",
            ["catalogBrand"] = new JsonObject { ["id"] = 1, ["brand"] = "Aspire" },
            ["catalogType"] = new JsonObject { ["id"] = 3, ["type"] = "Hoodie" }
        };

        if (context.JsonTypeInfo.Type == typeof(CatalogItemDto))
        {
            schema.Examples = [item];
        }
        else if (context.JsonTypeInfo.Type == typeof(CatalogItemsPage))
        {
            schema.Examples = [new JsonObject { ["firstId"] = 6, ["nextId"] = 7, ["isLastPage"] = false, ["data"] = new JsonArray(item) }];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Aspire Shop Catalog API"));
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

app.MapCatalogApi();
app.MapDefaultEndpoints();

app.Run();
