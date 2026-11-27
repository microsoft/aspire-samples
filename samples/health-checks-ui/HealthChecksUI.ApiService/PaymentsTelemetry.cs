using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthChecksUI.ApiService;

/// <summary>
/// Domain traces and metrics for the payments gateway. ServiceDefaults registers the "Contoso.*" meters and activity
/// sources with OpenTelemetry. Payment and merchant IDs only appear on spans and logs, never as metric tags.
/// </summary>
public sealed class PaymentsTelemetry
{
    public const string Name = "Contoso.Payments";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _authorizations;
    private readonly Histogram<double> _authorizationDuration;
    private readonly Histogram<double> _settledAmount;
    private readonly UpDownCounter<long> _inFlight;
    private readonly Histogram<double> _dependencyDuration;
    private readonly ConcurrentDictionary<string, HealthStatus> _dependencyHealth = new();

    public PaymentsTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _authorizations = meter.CreateCounter<long>(
            "contoso.payments.authorizations",
            unit: "{payment}",
            description: "Payment authorization attempts, by outcome, card brand, and currency.");

        _authorizationDuration = meter.CreateHistogram(
            "contoso.payments.authorization.duration",
            unit: "s",
            description: "Time the gateway took to authorize a payment, by outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.15, 0.2, 0.3, 0.5, 0.75, 1, 1.5, 2.5] });

        _settledAmount = meter.CreateHistogram(
            "contoso.payments.settled.amount",
            unit: "USD",
            description: "Value of approved payments, converted to USD.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000] });

        _inFlight = meter.CreateUpDownCounter<long>(
            "contoso.payments.in_flight",
            unit: "{payment}",
            description: "Payments currently being authorized.");

        _dependencyDuration = meter.CreateHistogram(
            "contoso.payments.dependency.duration",
            unit: "s",
            description: "Duration of calls the gateway makes to its dependencies while authorizing payments, by dependency and outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.2, 0.4, 0.8, 1.5] });

        meter.CreateObservableGauge(
            "contoso.payments.dependency.health",
            ObserveDependencyHealth,
            unit: "{status}",
            description: "Latest health check result per dependency: 2 = healthy, 1 = degraded, 0 = unhealthy.");
    }

    public void AuthorizationStarted() => _inFlight.Add(1);

    public void AuthorizationFinished(string outcome, CardBrand cardBrand, Currency currency, TimeSpan elapsed, decimal settledAmountUsd)
    {
        _inFlight.Add(-1);

        var tags = new TagList
        {
            { "contoso.payment.outcome", outcome },
            { "contoso.payment.card_brand", cardBrand.ToString() },
            { "contoso.payment.currency", currency.ToString() }
        };
        _authorizations.Add(1, tags);
        _authorizationDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("contoso.payment.outcome", outcome));

        if (outcome == "approved")
        {
            _settledAmount.Record((double)settledAmountUsd);
        }
    }

    public void DependencyCalled(string dependency, bool succeeded, TimeSpan elapsed) =>
        _dependencyDuration.Record(elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("contoso.dependency.name", dependency),
            new KeyValuePair<string, object?>("contoso.dependency.outcome", succeeded ? "success" : "failure"));

    public void DependencyChecked(string dependency, HealthStatus status) => _dependencyHealth[dependency] = status;

    private IEnumerable<Measurement<int>> ObserveDependencyHealth() =>
        _dependencyHealth.Select(d => new Measurement<int>((int)d.Value, new KeyValuePair<string, object?>("contoso.dependency.name", d.Key)));
}
