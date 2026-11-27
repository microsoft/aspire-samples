namespace AspireShop.BasketService;

internal static partial class Log
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Updated basket for buyer {BuyerId}: {ItemCount} items ({ItemsAdded} added, {ItemsRemoved} removed)")]
    public static partial void BasketUpdated(this ILogger logger, string buyerId, int itemCount, int itemsAdded, int itemsRemoved);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "Created order {OrderId} for buyer {BuyerId}: {ItemCount} items totaling {OrderTotal} USD")]
    public static partial void OrderCreated(this ILogger logger, string orderId, string buyerId, int itemCount, decimal orderTotal);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Rejected checkout for buyer {BuyerId}: {CheckoutOutcome}")]
    public static partial void CheckoutRejected(this ILogger logger, string buyerId, string checkoutOutcome);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Deleted basket for buyer {BuyerId}")]
    public static partial void BasketDeleted(this ILogger logger, string buyerId);
}
