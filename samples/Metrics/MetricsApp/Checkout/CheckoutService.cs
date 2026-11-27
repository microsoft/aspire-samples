using System.Diagnostics;

namespace MetricsApp.Checkout;

/// <summary>Turns a cart into an order: prices it, authorizes the payment, and records the checkout's telemetry.</summary>
public sealed class CheckoutService(
    CartStore carts,
    PaymentGateway payments,
    OrderFeed feed,
    CheckoutTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<CheckoutService> logger)
{
    public const decimal FreeShippingThreshold = 100m;
    public const decimal StandardShipping = 7.95m;
    public const decimal ExpressShipping = 19.95m;
    public const decimal SalesTaxRate = 0.0825m;

    public async Task<CheckoutResult> PlaceOrderAsync(Cart cart, CheckoutRequest request, string? customerId, CancellationToken cancellationToken)
    {
        if (!cart.TryLock())
        {
            return CheckoutResult.InProgress;
        }

        var placed = false;
        try
        {
            var result = await PlaceLockedOrderAsync(cart, request, customerId, cancellationToken);
            placed = result.Status == CheckoutStatus.Placed;
            return result;
        }
        finally
        {
            // A placed order removes the cart; otherwise it stays open for another attempt
            if (!placed)
            {
                cart.Unlock();
            }
        }
    }

    private async Task<CheckoutResult> PlaceLockedOrderAsync(Cart cart, CheckoutRequest request, string? customerId, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();

        using var activity = CheckoutTelemetry.ActivitySource.StartActivity("place order");
        activity?.SetTag("checkout.cart.id", cart.Id.ToString());
        activity?.SetTag("checkout.customer.id", customerId);
        activity?.SetTag("checkout.payment.method", request.PaymentMethod.ToTagValue());
        activity?.SetTag("checkout.shipping.method", request.ShippingMethod.ToTagValue());

        var lines = cart.GetLines();
        if (lines.Count == 0)
        {
            activity?.SetTag("checkout.outcome", "empty_cart");
            telemetry.CheckoutRejected("empty_cart", request.PaymentMethod);
            logger.EmptyCartRejected(cart.Id);
            return CheckoutResult.EmptyCart;
        }

        var itemCount = lines.Sum(l => l.Quantity);
        var subtotal = lines.Sum(l => l.LineTotal);
        var shipping = request.ShippingMethod switch
        {
            ShippingMethod.Express => ExpressShipping,
            _ => subtotal >= FreeShippingThreshold ? 0m : StandardShipping
        };
        var tax = Math.Round(subtotal * SalesTaxRate, 2, MidpointRounding.AwayFromZero);
        var total = subtotal + shipping + tax;

        activity?.SetTag("checkout.order.item_count", itemCount);
        activity?.SetTag("checkout.order.total", (double)total);
        activity?.AddEvent(new ActivityEvent("cart.priced", tags: new()
        {
            ["checkout.order.subtotal"] = (double)subtotal,
            ["checkout.order.shipping"] = (double)shipping,
            ["checkout.order.tax"] = (double)tax
        }));

        var payment = await payments.AuthorizeAsync(request.PaymentMethod, total, cancellationToken);
        var placedAt = timeProvider.GetUtcNow();

        if (!payment.Approved)
        {
            var declinedAfter = timeProvider.GetElapsedTime(startedAt);
            activity?.SetTag("checkout.outcome", "payment_declined");
            activity?.AddEvent(new ActivityEvent("payment.declined", tags: new() { ["checkout.payment.decline_reason"] = payment.DeclineReason }));

            telemetry.PaymentDeclined(request.PaymentMethod, declinedAfter);
            feed.Add(new RecentCheckout(null, placedAt, itemCount, total, request.PaymentMethod, request.ShippingMethod, "payment_declined", payment.DeclineReason, declinedAfter.TotalMilliseconds));
            logger.PaymentDeclined(cart.Id, payment.DeclineReason!, request.PaymentMethod, total);
            return CheckoutResult.Declined(payment.DeclineReason!);
        }

        var orderId = $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        activity?.SetTag("checkout.order.id", orderId);
        activity?.AddEvent(new ActivityEvent("payment.authorized", tags: new() { ["checkout.payment.authorization_code"] = payment.AuthorizationCode }));

        // Inventory reservation and fulfillment would be implemented here
        carts.Remove(cart.Id);

        var duration = timeProvider.GetElapsedTime(startedAt);
        activity?.SetTag("checkout.outcome", "completed");
        activity?.AddEvent(new ActivityEvent("order.confirmed"));

        telemetry.OrderPlaced(request.PaymentMethod, request.ShippingMethod, total, duration);
        feed.Add(new RecentCheckout(orderId, placedAt, itemCount, total, request.PaymentMethod, request.ShippingMethod, "completed", null, duration.TotalMilliseconds));
        logger.OrderPlaced(orderId, cart.Id, itemCount, total, request.PaymentMethod, request.ShippingMethod);

        return CheckoutResult.Placed(new OrderConfirmation(
            orderId, lines, itemCount, subtotal, shipping, tax, total, request.PaymentMethod, request.ShippingMethod, placedAt));
    }
}

public enum CheckoutStatus
{
    Placed,
    PaymentDeclined,
    EmptyCart,
    InProgress
}

public sealed record CheckoutResult(CheckoutStatus Status, OrderConfirmation? Order = null, string? DeclineReason = null)
{
    public static CheckoutResult EmptyCart { get; } = new(CheckoutStatus.EmptyCart);

    public static CheckoutResult InProgress { get; } = new(CheckoutStatus.InProgress);

    public static CheckoutResult Declined(string reason) => new(CheckoutStatus.PaymentDeclined, DeclineReason: reason);

    public static CheckoutResult Placed(OrderConfirmation order) => new(CheckoutStatus.Placed, order);
}
