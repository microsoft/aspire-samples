using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShop.CatalogService;

/// <summary>
/// Domain metrics for the catalog. ServiceDefaults registers the "AspireShop.*" meters with OpenTelemetry.
/// Tags are limited to the filter, page direction, and lookup outcome so metric cardinality stays bounded.
/// </summary>
public sealed class CatalogTelemetry
{
    public const string Name = "AspireShop.Catalog";

    private readonly Counter<long> _pagesServed;
    private readonly Histogram<int> _pageItems;
    private readonly Counter<long> _itemLookups;

    public CatalogTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _pagesServed = meter.CreateCounter<long>(
            "aspireshop.catalog.pages.served",
            unit: "{page}",
            description: "Catalog pages served, by filter and page direction.");

        _pageItems = meter.CreateHistogram(
            "aspireshop.catalog.page.items",
            unit: "{item}",
            description: "Number of items returned per catalog page.",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [0, 1, 2, 4, 8, 16, 32, 50] });

        _itemLookups = meter.CreateCounter<long>(
            "aspireshop.catalog.item.lookups",
            unit: "{lookup}",
            description: "Single catalog item lookups, by whether the item was found.");
    }

    public void PageServed(string filter, string direction, int itemCount)
    {
        var tags = new TagList
        {
            { "aspireshop.catalog.filter", filter },
            { "aspireshop.catalog.page.direction", direction }
        };

        _pagesServed.Add(1, tags);
        _pageItems.Record(itemCount, tags);
    }

    public void ItemLookedUp(bool found) =>
        _itemLookups.Add(1, new KeyValuePair<string, object?>("aspireshop.catalog.item.found", found));
}
