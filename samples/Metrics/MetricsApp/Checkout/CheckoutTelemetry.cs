using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MetricsApp.Checkout;

/// <summary>
/// Domain traces and metrics for carts and checkout. ServiceDefaults registers the "MetricsApp.*" meters and activity
/// sources with OpenTelemetry, which exports them to the Aspire dashboard and, through the collector, to Prometheus.
/// Tags are limited to the outcome, payment and shipping method, and product category so metric cardinality stays
/// bounded. Cart, order, and customer IDs only appear on spans and logs.
/// </summary>
public sealed class CheckoutTelemetry
{
    public const string Name = "MetricsApp.Checkout";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _orders;
    private readonly Histogram<double> _orderValue;
    private readonly Histogram<double> _duration;
    private readonly UpDownCounter<long> _activeCarts;
    private readonly Counter<long> _itemsAdded;

    public CheckoutTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _orders = meter.CreateCounter<long>(
            "checkout.orders",
            unit: "{order}",
            description: "Checkout attempts, by outcome and payment method. The completed outcome counts placed orders.");

        _orderValue = meter.CreateHistogram(
            "checkout.order.value",
            unit: "USD",
            description: "Value of placed orders, including shipping and tax.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [50, 100, 150, 200, 300, 400, 500, 750, 1000, 1500] });

        _duration = meter.CreateHistogram(
            "checkout.duration",
            unit: "s",
            description: "Server-side checkout time, from validating the cart to confirming the order.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.1, 0.2, 0.3, 0.5, 0.75, 1, 1.5, 2, 3, 4, 6] });

        _activeCarts = meter.CreateUpDownCounter<long>(
            "checkout.carts.active",
            unit: "{cart}",
            description: "Carts that haven't been checked out or abandoned yet.");

        _itemsAdded = meter.CreateCounter<long>(
            "checkout.cart.items.added",
            unit: "{item}",
            description: "Units added to carts, by product category.");
    }

    public void CartOpened() => _activeCarts.Add(1);

    public void CartClosed() => _activeCarts.Add(-1);

    public void ItemsAdded(string category, int quantity) =>
        _itemsAdded.Add(quantity, new KeyValuePair<string, object?>("checkout.product.category", category));

    public void OrderPlaced(PaymentMethod paymentMethod, ShippingMethod shippingMethod, decimal total, TimeSpan duration)
    {
        _orders.Add(1, Outcome("completed"), Payment(paymentMethod));
        _orderValue.Record((double)total, Payment(paymentMethod), new("checkout.shipping.method", shippingMethod.ToTagValue()));
        _duration.Record(duration.TotalSeconds, Outcome("completed"), Payment(paymentMethod));
    }

    public void PaymentDeclined(PaymentMethod paymentMethod, TimeSpan duration)
    {
        _orders.Add(1, Outcome("payment_declined"), Payment(paymentMethod));
        _duration.Record(duration.TotalSeconds, Outcome("payment_declined"), Payment(paymentMethod));
    }

    public void CheckoutRejected(string outcome, PaymentMethod paymentMethod) =>
        _orders.Add(1, Outcome(outcome), Payment(paymentMethod));

    private static KeyValuePair<string, object?> Outcome(string outcome) => new("checkout.outcome", outcome);

    private static KeyValuePair<string, object?> Payment(PaymentMethod method) => new("checkout.payment.method", method.ToTagValue());
}
