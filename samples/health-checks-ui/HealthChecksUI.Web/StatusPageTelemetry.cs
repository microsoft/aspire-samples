using System.Diagnostics.Metrics;

namespace HealthChecksUI.Web;

/// <summary>
/// Domain metrics for the status page. ServiceDefaults registers the "Contoso.*" meters with OpenTelemetry.
/// Status page traffic typically spikes during incidents, so views are tagged with the status the page showed.
/// </summary>
public sealed class StatusPageTelemetry
{
    public const string Name = "Contoso.StatusPage";

    private readonly Counter<long> _views;
    private readonly Counter<long> _statusUnavailable;

    public StatusPageTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _views = meter.CreateCounter<long>(
            "contoso.statuspage.views",
            unit: "{view}",
            description: "Status page renders, by the overall gateway status shown.");

        _statusUnavailable = meter.CreateCounter<long>(
            "contoso.statuspage.status_unavailable",
            unit: "{request}",
            description: "Times the status page couldn't load the gateway status.");
    }

    public void Viewed(ComponentStatus status) =>
        _views.Add(1, new KeyValuePair<string, object?>("contoso.status.overall", status.ToString()));

    public void StatusUnavailable() => _statusUnavailable.Add(1);
}
