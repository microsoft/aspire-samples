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
                new() { Type = "Jacket" },
                new() { Type = "Skateboard" },
                new() { Type = "Pin Badge" },
                new() { Type = "Sticker" }
            ];
        }

        static List<CatalogItem> GetPreconfiguredItems(DbSet<CatalogBrand> catalogBrands, DbSet<CatalogType> catalogTypes)
        {
            var aspire = catalogBrands.First(b => b.Brand == "Aspire");

            var mug = catalogTypes.First(c => c.Type == "Mug");
            var tshirt = catalogTypes.First(c => c.Type == "T-Shirt");
            var hoodie = catalogTypes.First(c => c.Type == "Hoodie");
            var jacket = catalogTypes.First(c => c.Type == "Jacket");
            var skateboard = catalogTypes.First(c => c.Type == "Skateboard");
            var pinBadge = catalogTypes.First(c => c.Type == "Pin Badge");
            var sticker = catalogTypes.First(c => c.Type == "Sticker");

            return [
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Charcoal Hoodie", Description = "A cozy charcoal pullover hoodie with the layered Aspire logo centered on the chest.", Price = 44.00M, PictureFileName = "1.png" },
                new() { CatalogType = mug, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo Mug", Description = "A glossy white ceramic mug featuring the layered Aspire logo.", Price = 12.00M, PictureFileName = "2.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire.dev Logo T-Shirt", Description = "A white cotton tee with a left-chest Aspire logo and the aspire.dev wordmark.", Price = 24.00M, PictureFileName = "3.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire OG T-Shirt", Description = "A lavender tee with an \"OG\" graphic and a mosaic band of Aspire logo tiles.", Price = 26.00M, PictureFileName = "4.png" },
                new() { CatalogType = sticker, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo Sticker", Description = "A glossy die-cut vinyl sticker of the layered Aspire logo.", Price = 3.50M, PictureFileName = "5.png" },
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire All-Over Print Hoodie", Description = "A sky-blue pullover hoodie with an all-over print of repeating Aspire logos.", Price = 52.00M, PictureFileName = "6.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Red Logo T-Shirt", Description = "A bold red cotton tee with a large, centered Aspire logo.", Price = 22.00M, PictureFileName = "7.png" },
                new() { CatalogType = hoodie, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire.dev Zip Hoodie", Description = "A periwinkle full-zip hoodie with a chest Aspire logo and aspire.dev down the sleeve.", Price = 49.50M, PictureFileName = "8.png" },
                new() { CatalogType = mug, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire \"Say Bye to YAML\" Mug", Description = "A white ceramic mug with the Aspire logo and a playful \"Say bye to YAML\" message.", Price = 14.00M, PictureFileName = "9.png" },
                new() { CatalogType = sticker, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Logo Sticker Pack", Description = "A pack of glossy die-cut stickers featuring the layered Aspire logo.", Price = 9.00M, PictureFileName = "10.png" },
                new() { CatalogType = pinBadge, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire \"No YAML\" Pin Badge", Description = "A round pin-back button badge with the Aspire logo and a \"No YAML\" callout.", Price = 5.00M, PictureFileName = "11.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire \"Goodbye YAML\" T-Shirt", Description = "A white tee with the Aspire logo and a \"Goodbye, YAML \u2014 Hello, Aspire\" slogan.", Price = 24.00M, PictureFileName = "12.png" },
                new() { CatalogType = tshirt, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire.dev Ringer T-Shirt", Description = "A white ringer tee with lavender trim, the Aspire logo, and a \"Goodbye, YAML \u2014 Hello, aspire.dev\" slogan.", Price = 26.00M, PictureFileName = "13.png" },
                new() { CatalogType = jacket, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire.dev Softshell Jacket", Description = "A tan softshell jacket with a chest Aspire logo and aspire.dev down the sleeve.", Price = 79.00M, PictureFileName = "14.png" },
                new() { CatalogType = skateboard, CatalogBrand = aspire, AvailableStock = 100, Name = "Aspire Skateboard Deck", Description = "A white maple skateboard deck with the Aspire logo, wordmark, and aspire.dev branding.", Price = 65.00M, PictureFileName = "15.png" }
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
