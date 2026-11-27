using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShop.Frontend.Services;

/// <summary>
/// Domain traces and metrics for the storefront. ServiceDefaults registers the "AspireShop.*" meters and
/// activity sources with OpenTelemetry. Metric tags are limited to page direction and checkout outcome.
/// </summary>
public sealed class FrontendTelemetry
{
    public const string Name = "AspireShop.Frontend";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _catalogPageViews;
    private readonly Histogram<double> _checkoutDuration;
    private readonly UpDownCounter<long> _activeShopperSessions;

    public FrontendTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _catalogPageViews = meter.CreateCounter<long>(
            "aspireshop.frontend.catalog.page_views",
            unit: "{view}",
            description: "Catalog pages requested by the storefront, by page direction.");

        _checkoutDuration = meter.CreateHistogram(
            "aspireshop.frontend.checkout.duration",
            unit: "s",
            description: "End-to-end checkout time as seen by the storefront, by outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5] });

        _activeShopperSessions = meter.CreateUpDownCounter<long>(
            "aspireshop.frontend.shopper_sessions.active",
            unit: "{session}",
            description: "Simulated shopper sessions in progress.");
    }

    public void CatalogPageViewed(string direction) =>
        _catalogPageViews.Add(1, new KeyValuePair<string, object?>("aspireshop.catalog.page.direction", direction));

    public void CheckoutFinished(string outcome, TimeSpan elapsed) =>
        _checkoutDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("aspireshop.checkout.outcome", outcome));

    public void ShopperSessionStarted() => _activeShopperSessions.Add(1);

    public void ShopperSessionEnded() => _activeShopperSessions.Add(-1);
}
