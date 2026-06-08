using System.Globalization;
using System.Net;

namespace AspireShop.Frontend.Services;

public class CatalogServiceClient(HttpClient client)
{
    public Task<CatalogItemsPage?> GetItemsAsync(int? before = null, int? after = null)
    {
        // Make the query string with encoded parameters
        var query = (before, after) switch
        {
            (null, null) => default,
            (int b, null) => QueryString.Create("before", b.ToString(CultureInfo.InvariantCulture)),
            (null, int a) => QueryString.Create("after", a.ToString(CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(),
        };

        return client.GetFromJsonAsync<CatalogItemsPage>($"api/v1/catalog/items/type/all{query}");
    }

    public async Task<CatalogItem?> GetItemAsync(int catalogItemId)
    {
        // The item may have been removed from the catalog while it lingers in a basket,
        // so treat 404 as "unavailable" and surface any other failure to the caller.
        using var response = await client.GetAsync($"api/v1/catalog/items/{catalogItemId.ToString(CultureInfo.InvariantCulture)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<CatalogItem>();
    }
}

public record CatalogItemsPage(int FirstId, int NextId, bool IsLastPage, IEnumerable<CatalogItem> Data);

public record CatalogItem
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int AvailableStock { get; init; }
    public CatalogBrandRef? CatalogBrand { get; init; }
    public CatalogTypeRef? CatalogType { get; init; }
}

public record CatalogBrandRef(int Id, string Brand);

public record CatalogTypeRef(int Id, string Type);
