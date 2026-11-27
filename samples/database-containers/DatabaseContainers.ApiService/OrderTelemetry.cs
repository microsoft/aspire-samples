using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DatabaseContainers.ApiService;

/// <summary>
/// Domain traces and metrics for placing orders. ServiceDefaults registers the "ContosoOutfitters.*" meters and
/// activity sources with OpenTelemetry. Customer and order IDs only appear on spans and logs, never as metric tags.
/// </summary>
public sealed class OrderTelemetry
{
    public const string Name = "ContosoOutfitters.Orders";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _placed;
    private readonly Counter<long> _rejected;
    private readonly Histogram<double> _orderValue;
    private readonly UpDownCounter<long> _inProgress;

    public OrderTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _placed = meter.CreateCounter<long>(
            "outfitters.orders.placed",
            unit: "{order}",
            description: "Orders placed, by the customer's loyalty tier.");

        _rejected = meter.CreateCounter<long>(
            "outfitters.orders.rejected",
            unit: "{order}",
            description: "Orders rejected, by reason.");

        _orderValue = meter.CreateHistogram(
            "outfitters.order.value",
            unit: "USD",
            description: "Total value of placed orders, by the customer's loyalty tier.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [50, 100, 150, 200, 300, 400, 500, 750, 1000, 1500, 2000] });

        _inProgress = meter.CreateUpDownCounter<long>(
            "outfitters.orders.in_progress",
            unit: "{order}",
            description: "Orders being placed right now.");
    }

    public void PlacementStarted() => _inProgress.Add(1);

    public void PlacementFinished() => _inProgress.Add(-1);

    public void OrderPlaced(LoyaltyTier tier, decimal total)
    {
        var tag = new KeyValuePair<string, object?>("outfitters.customer.tier", tier.ToString());
        _placed.Add(1, tag);
        _orderValue.Record((double)total, tag);
    }

    public void OrderRejected(string reason) =>
        _rejected.Add(1, new KeyValuePair<string, object?>("outfitters.order.rejection_reason", reason));
}
