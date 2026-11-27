using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthChecksUI.ApiService;

internal static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Approved payment {PaymentId} for {MerchantId}: {Amount} {Currency} on {CardBrand} ({SettlementAmountUsd} USD) in {ElapsedMilliseconds:F0} ms")]
    public static partial void PaymentApproved(this ILogger logger, string paymentId, string merchantId, decimal amount, Currency currency, CardBrand cardBrand, decimal settlementAmountUsd, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Declined payment {PaymentId} for {MerchantId}: {DeclineReason} (risk score {RiskScore})")]
    public static partial void PaymentDeclined(this ILogger logger, string paymentId, string merchantId, string declineReason, int riskScore);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "Payment {PaymentId} for {MerchantId} failed because {Dependency} is unavailable")]
    public static partial void PaymentFailed(this ILogger logger, Exception exception, string paymentId, string merchantId, string dependency);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Payment {PaymentId} used fallback {Fallback} because {Dependency} is unavailable")]
    public static partial void FallbackUsed(this ILogger logger, string paymentId, string fallback, string dependency);

    [LoggerMessage(EventId = 2001,
        Message = "Dependency {Dependency} changed from {PreviousStatus} to {HealthStatus}: {HealthDescription}")]
    public static partial void DependencyHealthChanged(this ILogger logger, LogLevel level, string dependency, HealthStatus previousStatus, HealthStatus healthStatus, string? healthDescription);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Simulated incident: {Dependency} switched to {DependencyMode}")]
    public static partial void IncidentSimulated(this ILogger logger, string dependency, DependencyMode dependencyMode);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information,
        Message = "Restored {DependencyCount} dependencies to normal operation")]
    public static partial void DependenciesRestored(this ILogger logger, int dependencyCount);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Simulated {PaymentCount} payments: {Approved} approved, {Declined} declined, {Failed} failed, {SettledUsd} USD settled")]
    public static partial void TrafficSimulated(this ILogger logger, int paymentCount, int approved, int declined, int failed, decimal settledUsd);
}
