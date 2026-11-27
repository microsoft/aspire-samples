namespace AspireShop.Frontend;

internal static partial class Log
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Placed order {OrderId} for buyer {BuyerId}: {ItemCount} items totaling {OrderTotal} USD in {ElapsedMilliseconds:F1} ms")]
    public static partial void OrderPlaced(this ILogger logger, string orderId, string buyerId, int itemCount, decimal orderTotal, double elapsedMilliseconds);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Checkout for buyer {BuyerId} was rejected: {Reason}")]
    public static partial void CheckoutRejected(this ILogger logger, string buyerId, string reason);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Simulated shopper {BuyerId} finished with outcome {ShopperOutcome} after adding {ItemsAdded} items")]
    public static partial void ShopperSessionFinished(this ILogger logger, string buyerId, string shopperOutcome, int itemsAdded);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error,
        Message = "Simulated shopper {BuyerId} failed")]
    public static partial void ShopperSessionFailed(this ILogger logger, Exception exception, string buyerId);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Information,
        Message = "Simulated {ShopperCount} shoppers: {OrdersPlaced} orders placed ({Revenue} USD), {BasketsAbandoned} baskets abandoned, {BasketsLeftOpen} left open, {Failed} failed")]
    public static partial void ShopperTrafficSimulated(this ILogger logger, int shopperCount, int ordersPlaced, decimal revenue, int basketsAbandoned, int basketsLeftOpen, int failed);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Information,
        Message = "Captured catalog prices for {ItemCount} unpriced items in the basket for buyer {BuyerId}")]
    public static partial void UnitPricesBackfilled(this ILogger logger, int itemCount, string buyerId);
}
