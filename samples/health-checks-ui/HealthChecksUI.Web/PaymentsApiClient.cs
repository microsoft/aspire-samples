using System.Text.Json.Serialization;

namespace HealthChecksUI.Web;

/// <summary>Reads the payments gateway's public status and recent payments.</summary>
public sealed class PaymentsApiClient(HttpClient httpClient)
{
    public Task<GatewayStatus?> GetStatusAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<GatewayStatus>("/api/status", cancellationToken);

    public async Task<Payment[]> GetRecentPaymentsAsync(int limit, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<Payment[]>($"/api/payments?limit={limit}", cancellationToken) ?? [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ComponentStatus>))]
public enum ComponentStatus
{
    Operational,
    Degraded,
    Outage
}

public sealed record GatewayStatus(
    ComponentStatus Status,
    string Summary,
    DateTimeOffset? CheckedAt,
    int CheckIntervalSeconds,
    IReadOnlyList<ComponentHealth> Components,
    IReadOnlyList<StatusChange> RecentChanges);

public sealed record ComponentHealth(
    string Name,
    string DisplayName,
    string Provider,
    bool IsCritical,
    ComponentStatus Status,
    string? Description,
    int? LatencyMs,
    double AvailabilityPercent,
    IReadOnlyList<ComponentStatus> History);

public sealed record StatusChange(
    string Component,
    string DisplayName,
    ComponentStatus From,
    ComponentStatus To,
    DateTimeOffset ChangedAt,
    string? Description);

public sealed record Payment(
    string Id,
    string MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string CardBrand,
    string Status,
    string? AuthorizationCode,
    string? DeclineReason,
    decimal SettlementAmountUsd,
    decimal FxRate,
    int RiskScore,
    IReadOnlyList<string> Fallbacks,
    DateTimeOffset CreatedAt,
    double ProcessingMs)
{
    public bool IsApproved => Status == "Approved";
}
