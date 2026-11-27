namespace DatabaseContainers.ApiService;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Placed order {OrderId} for {LoyaltyTier} customer {CustomerId}: {ItemCount} item(s), {OrderTotal} USD")]
    public static partial void OrderPlaced(this ILogger logger, int orderId, LoyaltyTier loyaltyTier, int customerId, int itemCount, decimal orderTotal);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Rejected order for customer {CustomerId}: {RejectionReason} ({RejectionDetail})")]
    public static partial void OrderRejected(this ILogger logger, int customerId, string rejectionReason, string rejectionDetail);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "{EntityType} {EntityId} was not found in the {DatabaseName} database")]
    public static partial void EntityNotFound(this ILogger logger, string entityType, int entityId, string databaseName);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Simulated {OrderCount} orders: {PlacedCount} placed, {RejectedCount} rejected, {Revenue} USD revenue")]
    public static partial void OrdersSimulated(this ILogger logger, int orderCount, int placedCount, int rejectedCount, decimal revenue);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "Reset the orders database by re-running its init script ({BatchCount} batches); {OrderCount} seeded orders remain")]
    public static partial void OrdersReset(this ILogger logger, int batchCount, int orderCount);
}
