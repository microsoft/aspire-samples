namespace AspireJavaScript.MinimalApi.Shipments;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Shipment board queried with filter {StatusFilter}: {ShipmentCount} shipments returned")]
    public static partial void BoardQueried(this ILogger logger, string statusFilter, int shipmentCount);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Tracked shipment {ShipmentId}: {ShipmentStatus} with {Carrier}, last scanned at {ScanLocation}")]
    public static partial void ShipmentTracked(this ILogger logger, string shipmentId, string shipmentStatus, string carrier, string scanLocation);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Shipment {ShipmentId} with {Carrier} has an exception at {ScanLocation}: {ScanDescription}")]
    public static partial void ShipmentExceptionObserved(this ILogger logger, string shipmentId, string carrier, string scanLocation, string scanDescription);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Shipment {ShipmentId} was not found")]
    public static partial void ShipmentNotFound(this ILogger logger, string shipmentId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning,
        Message = "Rejected shipment request: {Parameter} '{Value}' is not valid")]
    public static partial void RequestRejected(this ILogger logger, string parameter, string? value);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information,
        Message = "Simulated {BoardQueries} board queries and {Lookups} tracking lookups ({NotFound} not found, {ExceptionsObserved} exceptions observed)")]
    public static partial void LookupsSimulated(this ILogger logger, int boardQueries, int lookups, int notFound, int exceptionsObserved);
}
