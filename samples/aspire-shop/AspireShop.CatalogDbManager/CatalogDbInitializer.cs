using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using AspireShop.CatalogDb;

namespace AspireShop.CatalogDbManager;

internal class CatalogDbInitializer(IServiceProvider serviceProvider, ILogger<CatalogDbInitializer> logger)
    : BackgroundService
{
    public const string ActivitySourceName = "Migrations";

    private readonly ActivitySource _activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        using var activity = _activitySource.StartActivity("Initializing catalog database", ActivityKind.Client);
        await InitializeDatabaseAsync(dbContext, cancellationToken);
    }

    public async Task InitializeDatabaseAsync(CatalogDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(dbContext.Database.MigrateAsync, cancellationToken);

        await SeedAsync(dbContext, cancellationToken);

        logger.LogInformation("Database initialization completed after {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }

    private async Task SeedAsync(CatalogDbContext dbContext, CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding database");

        static List<CatalogBrand> GetPreconfiguredCatalogBrands()
        {
            return [
                new() { Brand = "Aspire" }
            ];
        }

        static List<CatalogType> GetPreconfiguredCatalogTypes()
        {
            return [
                new() { Type = "Mug" },
                new() { Type = "T-Shirt" },
                new() { Type = "Hoodie" },
                new() { Type = "Pin Badge" },
                new() { Type = "Sticker Pack" }
            ];
        }

        static List<CatalogItem> GetPreconfiguredItems(DbSet<CatalogBrand> catalogBrands, DbSet<CatalogType> catalogTypes)
        {
            var aspire = catalogBrands.First(b => b.Brand == "Aspire");

            var mug = catalogTypes.First(c => c.Type == "Mug");
            var tshirt = catalogTypes.First(c => c.Type == "T-Shirt");
            var hoodie = catalogTypes.First(c => c.Type == "Hoodie");
            var pinBadge = catalogTypes.First(c => c.Type == "Pin Badge");
            var stickerPack = catalogTypes.First(c => c.Type == "Sticker Pack");

            return [
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Bot Black Hoodie", Description = "A cozy black pullover hoodie featuring the friendly purple Aspire bot mascot.", Price = 19.5M, PictureFileName = "1.png" },
                new() { CatalogType = mug, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Black & White Mug", Description = "A classic enamel mug stamped with the Aspire wordmark in black and white.", Price = 8.50M, PictureFileName = "2.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Dashboard T-Shirt", Description = "A soft cotton tee celebrating the Aspire developer dashboard.", Price = 12, PictureFileName = "3.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo T-Shirt", Description = "A crisp white tee showcasing the signature Aspire logo.", Price = 12, PictureFileName = "4.png" },
                new() { CatalogType = pinBadge, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo Pin Badge", Description = "A pin-back button badge sporting the Aspire mark.", Price = 4.5M, PictureFileName = "5.png" },
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Blue Hoodie", Description = "A blue pullover hoodie finished with the Aspire logo.", Price = 12, PictureFileName = "6.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Telemetry T-Shirt", Description = "A tee inspired by Aspire's built-in OpenTelemetry tracing.", Price = 12, PictureFileName = "7.png" },
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Purple Hoodie", Description = "A pullover hoodie in Aspire's signature violet.", Price = 8.5M, PictureFileName = "8.png" },
                new() { CatalogType = mug, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Resource Graph Mug", Description = "A ceramic mug featuring the Aspire resource graph.", Price = 12, PictureFileName = "9.png" },
                new() { CatalogType = pinBadge, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Orchestration Pin Badge", Description = "A pin badge celebrating Aspire app orchestration.", Price = 4.5M, PictureFileName = "10.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Service Discovery T-Shirt", Description = "A tee highlighting Aspire's service discovery.", Price = 12, PictureFileName = "11.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire AppHost T-Shirt", Description = "A tee dedicated to the Aspire AppHost.", Price = 12, PictureFileName = "12.png" },
                new() { CatalogType = stickerPack, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo Sticker Pack", Description = "An assorted sticker pack to cover your laptop in Aspire logos.", Price = 5, PictureFileName = "13.png" },
                new() { CatalogType = stickerPack, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Integrations Sticker Pack", Description = "A sticker pack featuring popular Aspire integrations.", Price = 6, PictureFileName = "14.png" },
                new() { CatalogType = stickerPack, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Dashboard Sticker Pack", Description = "A sticker pack inspired by the Aspire dashboard.", Price = 5.5M, PictureFileName = "15.png" }
            ];
        }

        if (!dbContext.CatalogBrands.Any())
        {
            var brands = GetPreconfiguredCatalogBrands();
            await dbContext.CatalogBrands.AddRangeAsync(brands, cancellationToken);

            logger.LogInformation("Seeding {CatalogBrandCount} catalog brands", brands.Count);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!dbContext.CatalogTypes.Any())
        {
            var types = GetPreconfiguredCatalogTypes();
            await dbContext.CatalogTypes.AddRangeAsync(types, cancellationToken);

            logger.LogInformation("Seeding {CatalogTypeCount} catalog item types", types.Count);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!dbContext.CatalogItems.Any())
        {
            var items = GetPreconfiguredItems(dbContext.CatalogBrands, dbContext.CatalogTypes);
            await dbContext.CatalogItems.AddRangeAsync(items, cancellationToken);

            logger.LogInformation("Seeding {CatalogItemCount} catalog items", items.Count);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
