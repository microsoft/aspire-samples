using DatabaseContainers.ApiService;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("Todos");
builder.AddMySqlDataSource("Catalog");
builder.AddSqlServerClient("AddressBook");

// Add services to the container.
builder.Services.AddProblemDetails();

// Generate an OpenAPI document that powers the Scalar API reference.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Database Containers API";
        document.Info.Version = "v1";
        document.Info.Description =
            """
            A minimal API backed by three database **containers** orchestrated by Aspire.

            | Group | Database | Container image |
            | --- | --- | --- |
            | **Todos** | PostgreSQL | `postgres` |
            | **Catalog** | MySQL | `mysql` |
            | **AddressBook** | SQL Server | `mssql/server` |

            Each database is created and seeded from the container image's init scripts during startup,
            so the API serves real data the moment the app is running.
            """;
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // Serve the OpenAPI document and a themed Scalar API reference.
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Database Containers API · Aspire")
        .ForceDarkMode()
        .HideDarkModeToggle()
        .ExpandAllTags()
        .WithCustomCss(ApiReferenceTheme.Css));

    // Land visitors on the API reference from the app root.
    app.MapGet("/", () => Results.Redirect("/scalar/"))
        .ExcludeFromDescription();
}

app.MapTodosApi();
app.MapCatalogApi();
app.MapAddressBookApi();

app.MapDefaultEndpoints();

app.Run();
