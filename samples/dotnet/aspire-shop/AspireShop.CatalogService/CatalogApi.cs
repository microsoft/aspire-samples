using AspireShop.CatalogDb;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AspireShop.CatalogService;

public static class CatalogApi
{
    public static RouteGroupBuilder MapCatalogApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/catalog");

        group.WithTags("Catalog");

        group.MapGet("items/type/all", (CatalogDbContext catalogContext, int? before, int? after, int pageSize = 8)
            => GetCatalogItems(null, catalogContext, before, after, pageSize))
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<CatalogItemsPage>();

        group.MapGet("items/type/all/brand/{catalogBrandId:int}", (int catalogBrandId, CatalogDbContext catalogContext, int? before, int? after, int pageSize = 8)
            => GetCatalogItems(catalogBrandId, catalogContext, before, after, pageSize))
            .Produces(StatusCodes.Status400BadRequest)
            .Produces<CatalogItemsPage>();

        static async Task<IResult> GetCatalogItems(int? catalogBrandId, CatalogDbContext catalogContext, int? before, int? after, int pageSize)
        {
            if (before is > 0 && after is > 0)
            {
                return TypedResults.BadRequest($"Invalid paging parameters. Only one of {nameof(before)} or {nameof(after)} can be specified, not both.");
            }

            var itemsOnPage = await catalogContext.GetCatalogItemsCompiledAsync(catalogBrandId, before, after, pageSize);

            var (firstId, nextId) = itemsOnPage switch
            {
            [] => (0, 0),
            [var only] => (only.Id, only.Id),
            [var first, .., var last] => (first.Id, last.Id)
            };

            return Results.Ok(new CatalogItemsPage(
                firstId,
                nextId,
                itemsOnPage.Count < pageSize,
                itemsOnPage.Take(pageSize).Select(ToDto)));
        }

        group.MapGet("items/{catalogItemId:int}", async Task<Results<Ok<CatalogItemDto>, NotFound>> (int catalogItemId, CatalogDbContext catalogContext) =>
        {
            var item = await catalogContext.CatalogItems
                .AsNoTracking()
                .Include(ci => ci.CatalogBrand)
                .Include(ci => ci.CatalogType)
                .FirstOrDefaultAsync(ci => ci.Id == catalogItemId);

            return item is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(item));
        })
        .Produces<CatalogItemDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("items/{catalogItemId:int}/image", async (int catalogItemId, CatalogDbContext catalogDbContext, IHostEnvironment environment) =>
        {
            var item = await catalogDbContext.CatalogItems.FindAsync(catalogItemId);

            if (item is null)
            {
                return Results.NotFound();
            }

            var path = Path.Combine(environment.ContentRootPath, "Images", item.PictureFileName);

            if (!File.Exists(path))
            {
                return Results.NotFound();
            }

            return Results.File(path, "image/jpeg");
        })
        .Produces(404)
        .Produces(200, contentType: "image/jpeg");

        return group;
    }

    private static CatalogItemDto ToDto(CatalogItem item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.Price,
        item.AvailableStock,
        item.CatalogBrand is { } brand ? new CatalogBrandDto(brand.Id, brand.Brand) : null,
        item.CatalogType is { } type ? new CatalogTypeDto(type.Id, type.Type) : null);
}

public record CatalogItemsPage(int FirstId, int NextId, bool IsLastPage, IEnumerable<CatalogItemDto> Data);

public record CatalogItemDto(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    int AvailableStock,
    CatalogBrandDto? CatalogBrand,
    CatalogTypeDto? CatalogType);

public record CatalogBrandDto(int Id, string Brand);

public record CatalogTypeDto(int Id, string Type);
