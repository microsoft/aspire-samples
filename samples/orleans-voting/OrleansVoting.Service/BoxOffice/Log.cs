namespace OrleansVoting.BoxOffice;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Reserved {Quantity} × {SectionName} for {EventName} as {ReservationId} (fan {FanId}, {OrderTotal} USD, {SeatsRemaining} left in section, {ElapsedMilliseconds:F1} ms)")]
    public static partial void ReservationConfirmed(this ILogger logger, string reservationId, string fanId, int quantity, string sectionName, string eventName, decimal orderTotal, int seatsRemaining, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Turned away fan {FanId} asking for {Quantity} × {SectionName} for {EventName}: {ReservationOutcome} ({SeatsRemaining} left in section)")]
    public static partial void ReservationRejected(this ILogger logger, string fanId, int quantity, string sectionName, string eventName, string reservationOutcome, int seatsRemaining);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Throttled fan {FanId}: too many requests, retry after {RetryAfterSeconds} s")]
    public static partial void FanThrottled(this ILogger logger, string fanId, int retryAfterSeconds);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Cancelled reservation {ReservationId}: released {Quantity} × {SectionName} for {EventName}")]
    public static partial void ReservationCancelled(this ILogger logger, string reservationId, int quantity, string sectionName, string eventName);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "{SectionName} sold out for {EventName} after {ReservationCount} reservations")]
    public static partial void SectionSoldOut(this ILogger logger, string sectionName, string eventName, int reservationCount);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information,
        Message = "{EventName} is sold out: {Capacity} seats, {Revenue} USD")]
    public static partial void EventSoldOut(this ILogger logger, string eventName, int capacity, decimal revenue);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "Reset inventory for {EventName}: released {SeatsReleased} seats from {ReservationCount} reservations")]
    public static partial void InventoryReset(this ILogger logger, string eventName, int seatsReleased, int reservationCount);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "On-sale rush started: {FanCount} fans for {EventName}")]
    public static partial void OnSaleRushStarted(this ILogger logger, int fanCount, string eventName);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information,
        Message = "On-sale rush finished for {EventName}: {FanCount} fans, {Reservations} reservations, {SeatsReserved} seats, {TurnedAway} turned away, {SeatsRemaining} seats left in {ElapsedSeconds:F1} s")]
    public static partial void OnSaleRushFinished(this ILogger logger, string eventName, int fanCount, int reservations, int seatsReserved, int turnedAway, int seatsRemaining, double elapsedSeconds);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error,
        Message = "Simulated fan {FanId} failed")]
    public static partial void SimulatedFanFailed(this ILogger logger, Exception exception, string fanId);
}
