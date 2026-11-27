using MetricsApp.Checkout;

namespace MetricsApp;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Created cart {CartId}")]
    public static partial void CartCreated(this ILogger logger, Guid cartId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Set {Sku} to {Quantity} in cart {CartId}, which now holds {ItemCount} items")]
    public static partial void CartItemChanged(this ILogger logger, Guid cartId, string sku, int quantity, int itemCount);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Abandoned cart {CartId} with {ItemCount} items")]
    public static partial void CartAbandoned(this ILogger logger, Guid cartId, int itemCount);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Rejected checkout for cart {CartId} because the cart is empty")]
    public static partial void EmptyCartRejected(this ILogger logger, Guid cartId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "Payment declined for cart {CartId}: {DeclineReason} ({PaymentMethod}, {OrderTotal} USD)")]
    public static partial void PaymentDeclined(this ILogger logger, Guid cartId, string declineReason, PaymentMethod paymentMethod, decimal orderTotal);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Placed order {OrderId} from cart {CartId}: {ItemCount} items totaling {OrderTotal} USD, paid by {PaymentMethod} with {ShippingMethod} shipping")]
    public static partial void OrderPlaced(this ILogger logger, string orderId, Guid cartId, int itemCount, decimal orderTotal, PaymentMethod paymentMethod, ShippingMethod shippingMethod);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Simulating a card issuer outage until {OutageEndsAt:u}: card payments are slow and often declined")]
    public static partial void CardIssuerOutageStarted(this ILogger logger, DateTimeOffset outageEndsAt);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "Simulated {Shoppers} shoppers: {OrdersPlaced} orders placed ({Revenue} USD), {PaymentsDeclined} payments declined, {CartsAbandoned} carts abandoned, {CartsLeftOpen} left open, {Failed} failed")]
    public static partial void CheckoutTrafficSimulated(this ILogger logger, int shoppers, int ordersPlaced, decimal revenue, int paymentsDeclined, int cartsAbandoned, int cartsLeftOpen, int failed);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Simulated shopper {Shopper} failed")]
    public static partial void SimulatedShopperFailed(this ILogger logger, Exception exception, string shopper);
}
