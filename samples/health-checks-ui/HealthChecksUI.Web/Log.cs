namespace HealthChecksUI.Web;

internal static partial class Log
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Couldn't load the gateway status from the payments API")]
    public static partial void StatusUnavailable(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "Couldn't load recent payments from the payments API")]
    public static partial void PaymentsUnavailable(this ILogger logger, Exception exception);
}
