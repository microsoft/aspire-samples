using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthChecksUI.ApiService;

[JsonConverter(typeof(JsonStringEnumConverter<ComponentStatus>))]
[Description("A status page state: `Operational` (healthy), `Degraded`, or `Outage` (unhealthy).")]
public enum ComponentStatus
{
    Operational,
    Degraded,
    Outage
}

[Description("The current status of the payments gateway and its dependencies.")]
public sealed record GatewayStatus(
    [property: Description("The overall status: the worst status of any dependency.")] ComponentStatus Status,
    [property: Description("A one-line summary of the overall status.")] string Summary,
    [property: Description("When the dependencies were last checked.")] DateTimeOffset? CheckedAt,
    [property: Description("How often the gateway checks its dependencies, in seconds.")] int CheckIntervalSeconds,
    [property: Description("Each dependency's status and recent check history.")] IReadOnlyList<ComponentHealth> Components,
    [property: Description("The most recent status changes, newest first.")] IReadOnlyList<StatusChange> RecentChanges);

[Description("The health of one dependency of the payments gateway.")]
public sealed record ComponentHealth(
    [property: Description("The health check name.")] string Name,
    [property: Description("The dependency's display name.")] string DisplayName,
    [property: Description("Who provides the dependency.")] string Provider,
    [property: Description("Whether payments fail when this dependency is down. Non-critical dependencies have fallbacks.")] bool IsCritical,
    [property: Description("The latest status.")] ComponentStatus Status,
    [property: Description("The latest health check description.")] string? Description,
    [property: Description("How long the latest health check took, in milliseconds.")] int? LatencyMs,
    [property: Description("The percentage of recent checks that weren't an outage.")] double AvailabilityPercent,
    [property: Description("The recent check results, oldest first.")] IReadOnlyList<ComponentStatus> History);

[Description("A change in a dependency's status.")]
public sealed record StatusChange(
    [property: Description("The health check name.")] string Component,
    [property: Description("The dependency's display name.")] string DisplayName,
    [property: Description("The status before the change.")] ComponentStatus From,
    [property: Description("The status after the change.")] ComponentStatus To,
    [property: Description("When the change was detected.")] DateTimeOffset ChangedAt,
    [property: Description("The health check description at the time of the change.")] string? Description);

/// <summary>
/// An <see cref="IHealthCheckPublisher"/> that the health check service calls every few seconds with a fresh report. It keeps
/// a short history per dependency for the status page, logs status changes, and feeds the dependency health gauge.
/// </summary>
public sealed class GatewayStatusMonitor(
    PaymentDependencies dependencies,
    PaymentsTelemetry telemetry,
    ILogger<GatewayStatusMonitor> logger) : IHealthCheckPublisher
{
    public const int HistoryLength = 30;
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private const int MaxRecentChanges = 8;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Queue<ComponentStatus>> _history = [];
    private readonly Dictionary<string, HealthReportEntry> _latest = [];
    private readonly LinkedList<StatusChange> _changes = new();
    private DateTimeOffset? _checkedAt;

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            _checkedAt = now;

            foreach (var dependency in dependencies.All)
            {
                if (!report.Entries.TryGetValue(dependency.Name, out var entry))
                {
                    continue;
                }

                telemetry.DependencyChecked(dependency.Name, entry.Status);

                if (!_history.TryGetValue(dependency.Name, out var history))
                {
                    _history[dependency.Name] = history = new Queue<ComponentStatus>(HistoryLength);
                }

                history.Enqueue(ToComponentStatus(entry.Status));
                while (history.Count > HistoryLength)
                {
                    history.Dequeue();
                }

                if (_latest.TryGetValue(dependency.Name, out var previous) && previous.Status != entry.Status)
                {
                    _changes.AddFirst(new StatusChange(dependency.Name, dependency.DisplayName,
                        ToComponentStatus(previous.Status), ToComponentStatus(entry.Status), now, entry.Description));
                    if (_changes.Count > MaxRecentChanges)
                    {
                        _changes.RemoveLast();
                    }

                    var level = entry.Status switch
                    {
                        HealthStatus.Unhealthy => LogLevel.Error,
                        HealthStatus.Degraded => LogLevel.Warning,
                        _ => LogLevel.Information
                    };
                    logger.DependencyHealthChanged(level, dependency.Name, previous.Status, entry.Status, entry.Description);
                }

                _latest[dependency.Name] = entry;
            }
        }

        return Task.CompletedTask;
    }

    public GatewayStatus GetStatus()
    {
        lock (_lock)
        {
            var components = dependencies.All.Select(dependency =>
            {
                var history = _history.TryGetValue(dependency.Name, out var h) ? h.ToArray() : [];
                var hasLatest = _latest.TryGetValue(dependency.Name, out var latest);

                return new ComponentHealth(
                    dependency.Name,
                    dependency.DisplayName,
                    dependency.Provider,
                    dependency.IsCritical,
                    hasLatest ? ToComponentStatus(latest.Status) : ComponentStatus.Operational,
                    hasLatest ? latest.Description : "Waiting for the first health check.",
                    hasLatest && latest.Status != HealthStatus.Unhealthy ? (int)Math.Ceiling(latest.Duration.TotalMilliseconds) : null,
                    history.Length == 0 ? 100 : Math.Round(100.0 * history.Count(s => s != ComponentStatus.Outage) / history.Length, 1),
                    history);
            }).ToList();

            var overall = components.Count == 0 ? ComponentStatus.Operational : components.Max(c => c.Status);
            var summary = overall switch
            {
                ComponentStatus.Outage => "Payments are disrupted",
                ComponentStatus.Degraded => "Degraded performance",
                _ => "All systems operational"
            };

            return new GatewayStatus(overall, summary, _checkedAt, (int)CheckInterval.TotalSeconds, components, [.. _changes]);
        }
    }

    private static ComponentStatus ToComponentStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => ComponentStatus.Operational,
        HealthStatus.Degraded => ComponentStatus.Degraded,
        _ => ComponentStatus.Outage
    };
}
