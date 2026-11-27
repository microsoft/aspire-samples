using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireShop.BasketService;

/// <summary>
/// Domain traces and metrics for baskets and checkout. ServiceDefaults registers the "AspireShop.*" meters and
/// activity sources with OpenTelemetry. Buyer and order IDs only appear on spans, never as metric tags.
/// </summary>
public sealed class BasketTelemetry
{
    public const string Name = "AspireShop.Basket";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _itemsAdded;
    private readonly Counter<long> _itemsRemoved;
    private readonly UpDownCounter<long> _activeBaskets;
    private readonly Counter<long> _checkouts;
    private readonly Histogram<double> _orderValue;

    public BasketTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _itemsAdded = meter.CreateCounter<long>(
            "aspireshop.basket.items.added",
            unit: "{item}",
            description: "Units added to baskets.");

        _itemsRemoved = meter.CreateCounter<long>(
            "aspireshop.basket.items.removed",
            unit: "{item}",
            description: "Units removed from baskets.");

        _activeBaskets = meter.CreateUpDownCounter<long>(
            "aspireshop.basket.active",
            unit: "{basket}",
            description: "Baskets created by this process that haven't been checked out or deleted yet.");

        _checkouts = meter.CreateCounter<long>(
            "aspireshop.basket.checkouts",
            unit: "{checkout}",
            description: "Checkout attempts, by outcome.");

        _orderValue = meter.CreateHistogram(
            "aspireshop.basket.order.value",
            unit: "USD",
            description: "Total value of checked-out orders.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [10, 20, 30, 40, 50, 60, 80, 100, 125, 150, 200, 250, 300, 400, 500] });
    }

    public void BasketChanged(bool created, int itemsAdded, int itemsRemoved)
    {
        if (created)
        {
            _activeBaskets.Add(1);
        }

        if (itemsAdded > 0)
        {
            _itemsAdded.Add(itemsAdded);
        }

        if (itemsRemoved > 0)
        {
            _itemsRemoved.Add(itemsRemoved);
        }
    }

    public void BasketDeleted() => _activeBaskets.Add(-1);

    public void CheckoutCompleted(decimal orderTotal)
    {
        _activeBaskets.Add(-1);
        _checkouts.Add(1, Outcome("completed"));
        _orderValue.Record((double)orderTotal);
    }

    public void CheckoutRejected(string outcome) => _checkouts.Add(1, Outcome(outcome));

    private static KeyValuePair<string, object?> Outcome(string outcome) => new("aspireshop.checkout.outcome", outcome);
}
