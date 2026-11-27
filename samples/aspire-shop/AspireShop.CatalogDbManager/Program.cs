using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using AspireShop.CatalogDb;
using AspireShop.CatalogDbManager;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb", null,
    optionsBuilder => optionsBuilder.UseNpgsql(npgsqlBuilder =>
        npgsqlBuilder.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));

builder.Services.AddSingleton<CatalogDbInitializer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CatalogDbInitializer>());
builder.Services.AddHealthChecks()
    .AddCheck<CatalogDbInitializerHealthCheck>("DbInitializer", null);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Backs the AppHost's "Reset database" command: revert every migration (dropping the catalog tables and HiLo
    // sequences), then the command restarts this service, which re-applies the migrations and re-seeds the catalog.
    app.MapPost("/reset-db", async (CatalogDbContext dbContext, ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => dbContext.Database.MigrateAsync(Migration.InitialDatabase, cancellationToken));
        logger.MigrationsReverted();
    });
}

app.MapDefaultEndpoints();

await app.RunAsync();
