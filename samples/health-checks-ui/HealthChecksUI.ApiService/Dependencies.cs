using System.Text.Json.Serialization;

namespace HealthChecksUI.ApiService;

/// <summary>A downstream service the payments gateway depends on, as shown on the status page.</summary>
public class DependencyInfo(string name, string displayName, string provider, bool isCritical, string impactWhenDown)
{
    /// <summary>The health check name, e.g. <c>card-processor</c>.</summary>
    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public string Provider { get; } = provider;

    /// <summary>Critical dependencies report Unhealthy when they fail; the gateway degrades gracefully without the others.</summary>
    public bool IsCritical { get; } = isCritical;

    public string ImpactWhenDown { get; } = impactWhenDown;
}

[JsonConverter(typeof(JsonStringEnumConverter<DependencyMode>))]
public enum DependencyMode
{
    Operational,
    Slow,
    Outage
}

/// <summary>
/// An in-process stand-in for a third-party API (card network, risk engine, FX feed). Its mode is switched by the
/// development-only fault endpoints that back the AppHost's incident commands.
/// </summary>
public sealed class SimulatedDependency(
    string name,
    string displayName,
    string provider,
    bool isCritical,
    string impactWhenDown,
    (int Min, int Max) latencyMs,
    (int Min, int Max) slowLatencyMs,
    TimeSpan degradedThreshold)
    : DependencyInfo(name, displayName, provider, isCritical, impactWhenDown)
{
    private volatile DependencyMode _mode = DependencyMode.Operational;

    public DependencyMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>Responses slower than this make the dependency's health check report Degraded.</summary>
    public TimeSpan DegradedThreshold { get; } = degradedThreshold;

    /// <summary>Simulates a round trip to the provider and returns how long it took.</summary>
    public async Task<TimeSpan> CallAsync(CancellationToken cancellationToken)
    {
        var mode = Mode;
        var (min, max) = mode switch
        {
            DependencyMode.Slow => slowLatencyMs,
            DependencyMode.Outage => (20, 60),
            _ => latencyMs
        };

        var latency = TimeSpan.FromMilliseconds(Random.Shared.Next(min, max));
        await Task.Delay(latency, cancellationToken);

        if (mode == DependencyMode.Outage)
        {
            throw new DependencyUnavailableException(this, $"{Provider} returned 503 Service Unavailable.");
        }

        return latency;
    }
}

public sealed class DependencyUnavailableException(DependencyInfo dependency, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public DependencyInfo Dependency { get; } = dependency;
}

/// <summary>The gateway's dependencies, in the order the status page lists them.</summary>
public sealed class PaymentDependencies
{
    public SimulatedDependency CardProcessor { get; } = new(
        "card-processor", "Card processor", "Northwind Card Network", isCritical: true,
        "Card payments can't be authorized.",
        latencyMs: (70, 160), slowLatencyMs: (650, 1300), degradedThreshold: TimeSpan.FromMilliseconds(500));

    public SimulatedDependency FraudScoring { get; } = new(
        "fraud-scoring", "Fraud scoring", "Woodgrove Risk Engine", isCritical: false,
        "Payments are screened with fallback rules.",
        latencyMs: (20, 60), slowLatencyMs: (400, 800), degradedThreshold: TimeSpan.FromMilliseconds(250));

    public SimulatedDependency FxRates { get; } = new(
        "fx-rates", "FX rates", "Fabrikam FX", isCritical: false,
        "Non-USD payments settle at the last cached rates.",
        latencyMs: (10, 35), slowLatencyMs: (350, 700), degradedThreshold: TimeSpan.FromMilliseconds(200));

    public DependencyInfo PaymentStore { get; } = new(
        "payment-store", "Payment store", "Redis", isCritical: true,
        "Payments can't be recorded.");

    public IReadOnlyList<SimulatedDependency> Simulated => [CardProcessor, FraudScoring, FxRates];

    public IReadOnlyList<DependencyInfo> All => [CardProcessor, FraudScoring, FxRates, PaymentStore];

    public SimulatedDependency? FindSimulated(string name) =>
        Simulated.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}
