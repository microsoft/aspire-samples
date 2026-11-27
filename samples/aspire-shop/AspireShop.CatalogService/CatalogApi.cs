using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using AspireShop.CatalogDb;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AspireShop.CatalogService;

public static class CatalogApi
{
    public const int DefaultPageSize = 8;
    public const int MaxPageSize = 50;

    public static RouteGroupBuilder MapCatalogApi(this IEndpointRouteBuilder routes)
    {
        var logger = routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CatalogApi).FullName!);

        var group = routes.MapGroup("/api/v1/catalog")
            .WithTags("Catalog");

        group.MapGet("items/type/all", (
                [Description("Return the page of items that ends at this item ID (page backward). Use the `firstId` of the current page.")] int? before,
                [Description("Return the page of items that starts at this item ID (page forward). Use the `nextId` of the current page.")] int? after,
                CatalogDbContext catalogContext,
                CatalogTelemetry telemetry,
                [Description("Number of items per page (1-50).")][Range(1, MaxPageSize)] int pageSize = DefaultPageSize)
            => GetCatalogItemsAsync(null, before, after, pageSize, catalogContext, telemetry, logger))
            .WithName("GetCatalogItems")
            .WithSummary("List catalog items")
            .WithDescription("""
                Returns a page of catalog items ordered by ID using keyset pagination.
                Omit `before` and `after` for the first page, pass `after={nextId}` for the next page,
                or `before={firstId}` for the previous page. `before` and `after` are mutually exclusive.
                """);

        group.MapGet("items/type/all/brand/{catalogBrandId:int}", (
                [Description("The ID of the brand to filter by.")] int catalogBrandId,
                [Description("Return the page of items that ends at this item ID (page backward).")] int? before,
                [Description("Return the page of items that starts at this item ID (page forward).")] int? after,
                CatalogDbContext catalogContext,
                CatalogTelemetry telemetry,
                [Description("Number of items per page (1-50).")][Range(1, MaxPageSize)] int pageSize = DefaultPageSize)
            => GetCatalogItemsAsync(catalogBrandId, before, after, pageSize, catalogContext, telemetry, logger))
            .WithName("GetCatalogItemsByBrand")
            .WithSummary("List catalog items for a brand")
            .WithDescription("Returns a page of catalog items for a single brand. Paging works the same way as `GET /api/v1/catalog/items/type/all`.");

        group.MapGet("items/{catalogItemId:int}", async Task<Results<Ok<CatalogItemDto>, ProblemHttpResult>> (
                [Description("The ID of the catalog item.")] int catalogItemId,
                CatalogDbContext catalogContext,
                CatalogTelemetry telemetry) =>
            {
                Activity.Current?.SetTag("aspireshop.catalog.item.id", catalogItemId);

                var item = await catalogContext.CatalogItems
                    .AsNoTracking()
                    .Include(ci => ci.CatalogBrand)
                    .Include(ci => ci.CatalogType)
                    .FirstOrDefaultAsync(ci => ci.Id == catalogItemId);

                telemetry.ItemLookedUp(found: item is not null);

                if (item is null)
                {
                    logger.CatalogItemNotFound(catalogItemId);
                    return ItemNotFound(catalogItemId);
                }

                return TypedResults.Ok(ToDto(item));
            })
            .WithName("GetCatalogItem")
            .WithSummary("Get a catalog item")
            .WithDescription("Returns a single catalog item, including its brand and type. Returns a problem details response when the item doesn't exist.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("items/{catalogItemId:int}/image", async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> (
                [Description("The ID of the catalog item.")] int catalogItemId,
                CatalogDbContext catalogDbContext,
                IHostEnvironment environment) =>
            {
                var item = await catalogDbContext.CatalogItems.FindAsync(catalogItemId);

                if (item is null)
                {
                    return ItemNotFound(catalogItemId);
                }

                var path = Path.Combine(environment.ContentRootPath, "Images", item.PictureFileName);

                if (!File.Exists(path))
                {
                    logger.CatalogImageMissing(catalogItemId, item.PictureFileName);
                    return TypedResults.Problem(
                        title: "Catalog item image not found",
                        detail: $"Catalog item {catalogItemId} has no image.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                return TypedResults.PhysicalFile(path, "image/png");
            })
            .WithName("GetCatalogItemImage")
            .WithSummary("Get a catalog item's image")
            .WithDescription("Returns the product image for a catalog item as a PNG.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Results<Ok<CatalogItemsPage>, ValidationProblem>> GetCatalogItemsAsync(
        int? catalogBrandId, int? before, int? after, int pageSize,
        CatalogDbContext catalogContext, CatalogTelemetry telemetry, ILogger logger)
    {
        var filter = catalogBrandId is null ? "all" : "brand";
        var direction = (before, after) switch
        {
            (> 0, _) => "backward",
            (_, > 0) => "forward",
            _ => "first"
        };

        var activity = Activity.Current;
        activity?.SetTag("aspireshop.catalog.filter", filter);
        activity?.SetTag("aspireshop.catalog.brand.id", catalogBrandId);
        activity?.SetTag("aspireshop.catalog.page.direction", direction);
        activity?.SetTag("aspireshop.catalog.page.size", pageSize);

        if (ValidatePaging(before, after, pageSize) is { } errors)
        {
            logger.InvalidPageRequest(errors.First().Value[0], before, after, pageSize);
            return TypedResults.ValidationProblem(errors, title: "Invalid paging parameters");
        }

        var itemsOnPage = await catalogContext.GetCatalogItemsCompiledAsync(catalogBrandId, before, after, pageSize);

        var (firstId, nextId) = itemsOnPage switch
        {
            [] => (0, 0),
            [var only] => (only.Id, only.Id),
            [var first, .., var last] => (first.Id, last.Id)
        };

        var page = new CatalogItemsPage(
            firstId,
            nextId,
            itemsOnPage.Count < pageSize,
            itemsOnPage.Take(pageSize).Select(ToDto).ToList());

        telemetry.PageServed(filter, direction, page.Data.Count);
        activity?.SetTag("aspireshop.catalog.page.item_count", page.Data.Count);
        logger.CatalogPageServed(page.Data.Count, filter, direction, page.IsLastPage);

        return TypedResults.Ok(page);
    }

    private static Dictionary<string, string[]>? ValidatePaging(int? before, int? after, int pageSize)
    {
        if (before is > 0 && after is > 0)
        {
            return new() { [nameof(before)] = [$"Only one of '{nameof(before)}' or '{nameof(after)}' can be specified, not both."] };
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return new() { [nameof(pageSize)] = [$"'{nameof(pageSize)}' must be between 1 and {MaxPageSize}."] };
        }

        return null;
    }

    private static ProblemHttpResult ItemNotFound(int catalogItemId) => TypedResults.Problem(
        title: "Catalog item not found",
        detail: $"No catalog item exists with ID {catalogItemId}.",
        statusCode: StatusCodes.Status404NotFound);

    private static CatalogItemDto ToDto(CatalogItem item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.Price,
        item.AvailableStock,
        item.Badge,
        item.CatalogBrand is { } brand ? new CatalogBrandDto(brand.Id, brand.Brand) : null,
        item.CatalogType is { } type ? new CatalogTypeDto(type.Id, type.Type) : null);
}

[Description("A page of catalog items returned by keyset pagination.")]
public record CatalogItemsPage(
    [property: Description("ID of the first item on the page. Pass it as `before` to fetch the previous page.")] int FirstId,
    [property: Description("ID to pass as `after` to fetch the next page.")] int NextId,
    [property: Description("Whether this is the last page of results.")] bool IsLastPage,
    [property: Description("The catalog items on this page.")] IReadOnlyList<CatalogItemDto> Data);

[Description("A product in the catalog.")]
public record CatalogItemDto(
    [property: Description("The catalog item ID.")] int Id,
    [property: Description("The product name.")] string Name,
    [property: Description("A short marketing description of the product.")] string? Description,
    [property: Description("The unit price in USD.")] decimal Price,
    [property: Description("Units currently in stock.")] int AvailableStock,
    [property: Description("An optional merchandising badge, such as \"New arrival\" or \"Special\".")] string? Badge,
    [property: Description("The product's brand.")] CatalogBrandDto? CatalogBrand,
    [property: Description("The product's type.")] CatalogTypeDto? CatalogType);

[Description("A catalog brand.")]
public record CatalogBrandDto(int Id, string Brand);

[Description("A catalog product type.")]
public record CatalogTypeDto(int Id, string Type);
