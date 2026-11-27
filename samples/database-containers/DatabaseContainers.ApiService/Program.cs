using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DatabaseContainers.ApiService;
using Microsoft.Data.SqlClient;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Each client integration registers a connection to its database, plus health checks, traces, and metrics.
builder.AddNpgsqlDataSource("catalog");
builder.AddMySqlDataSource("customers");
builder.AddSqlServerClient("orders");

// Add services to the container.
builder.Services.AddSingleton<OrderTelemetry>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderSimulator>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // The web defaults accept numbers as strings, which documents every integer as "integer | string" in OpenAPI
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Generate an OpenAPI document that powers the Scalar API reference.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Contoso Outfitters API",
            Version = "v1",
            Description = """
                The store API for Contoso Outfitters, an outdoor gear retailer. Each part of the business lives in its
                own database **container** orchestrated by Aspire:

                | Group | Database | Container image |
                | --- | --- | --- |
                | **Products** | PostgreSQL `catalog` | `postgres` |
                | **Customers** | MySQL `customers` | `mysql` |
                | **Orders** | SQL Server `orders` | `mssql/server` |

                Each database is created and seeded by an init script when its container first starts, so the API
                serves real data as soon as the app is running. Placing an order uses all three databases. Errors are
                returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Products", Description = "The product catalog, stored in PostgreSQL." },
            new() { Name = "Customers", Description = "Customer accounts and loyalty tiers, stored in MySQL." },
            new() { Name = "Orders", Description = "Orders and their lines, stored in SQL Server. Placing an order also reads MySQL and PostgreSQL." }
        };
        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        var customer = new JsonObject
        {
            ["id"] = 3,
            ["firstName"] = "Priya",
            ["lastName"] = "Raman",
            ["email"] = "priya.raman@example.com",
            ["city"] = "Toronto",
            ["country"] = "CA",
            ["loyaltyTier"] = "Gold"
        };

        var type = context.JsonTypeInfo.Type;
        if (type == typeof(Product))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 1,
                    ["sku"] = "PK-TRL-45",
                    ["name"] = "Trailblazer 45L Backpack",
                    ["category"] = "Packs",
                    ["description"] = "A ventilated 45-liter trekking pack with an adjustable harness and a stowable rain cover.",
                    ["price"] = 189.00m,
                    ["unitsInStock"] = 42
                }
            ];
        }
        else if (type == typeof(Customer))
        {
            schema.Examples = [customer];
        }
        else if (type == typeof(PlaceOrderRequest))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["customerId"] = 3,
                    ["lines"] = new JsonArray(
                        new JsonObject { ["productId"] = 1, ["quantity"] = 1 },
                        new JsonObject { ["productId"] = 13, ["quantity"] = 2 })
                }
            ];
        }
        else if (type == typeof(OrderDetails))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 13,
                    ["placedAt"] = "2026-11-26T18:42:10.5120000+00:00",
                    ["status"] = "Placed",
                    ["customerId"] = 3,
                    ["customer"] = customer,
                    ["lines"] = new JsonArray(
                        new JsonObject { ["productId"] = 1, ["productName"] = "Trailblazer 45L Backpack", ["quantity"] = 1, ["unitPrice"] = 189.00m, ["lineTotal"] = 189.00m },
                        new JsonObject { ["productId"] = 13, ["productName"] = "Lumen 400 Headlamp", ["quantity"] = 2, ["unitPrice"] = 49.00m, ["lineTotal"] = 98.00m }),
                    ["itemCount"] = 3,
                    ["total"] = 287.00m
                }
            ];
        }
        else if (type == typeof(OrderSummary))
        {
            schema.Examples =
            [
                new JsonObject
                {
                    ["id"] = 13,
                    ["customerId"] = 3,
                    ["placedAt"] = "2026-11-26T18:42:10.5120000+00:00",
                    ["status"] = "Placed",
                    ["itemCount"] = 3,
                    ["total"] = 287.00m
                }
            ];
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Serve the OpenAPI document and a themed Scalar API reference.
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Contoso Outfitters API")
        .ExpandAllTags()
        .WithCustomCss(ApiReferenceTheme.Css));

    // Land visitors on the API reference from the app root.
    app.MapGet("/", () => Results.Redirect("/scalar/"))
        .ExcludeFromDescription();

    // Back the AppHost's "Simulate orders" dashboard command.
    app.MapPost("/dev/simulate-orders", async (OrderSimulator simulator, CancellationToken cancellationToken) =>
            TypedResults.Ok(await simulator.RunAsync(orders: 24, cancellationToken)))
        .ExcludeFromDescription();

    // Back the AppHost's "Reset orders" dashboard command by re-running the script the AppHost passes to WithCreationScript.
    app.MapPost("/dev/reset-orders", async (SqlConnection ordersDb, IHostEnvironment environment, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            var initScript = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "data", "sqlserver", "init.sql"), cancellationToken);
            var (batches, orders) = await ordersDb.ResetAsync(initScript, cancellationToken);
            logger.OrdersReset(batches, orders);

            return TypedResults.Ok(new { database = "orders", seededOrders = orders });
        })
        .ExcludeFromDescription();
}

app.MapProductsApi();
app.MapCustomersApi();
app.MapOrdersApi();

app.MapDefaultEndpoints();

app.Run();
