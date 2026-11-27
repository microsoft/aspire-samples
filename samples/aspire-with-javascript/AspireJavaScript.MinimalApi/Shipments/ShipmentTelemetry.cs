using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireJavaScript.MinimalApi.Shipments;

/// <summary>
/// Domain traces and metrics for shipment tracking. ServiceDefaults registers the "Contoso.*" meters and activity
/// sources with OpenTelemetry. Metric tags are limited to the status, operation, and carrier so cardinality stays
/// bounded; tracking numbers only appear on spans and logs.
/// </summary>
public sealed class ShipmentTelemetry
{
    public const string Name = "Contoso.Shipments";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _shipmentsQueried;
    private readonly Counter<long> _statusLookups;
    private readonly Counter<long> _exceptionsObserved;
    private readonly Histogram<double> _carrierLookupDuration;

    public ShipmentTelemetry(IMeterFactory meterFactory, ShipmentStore store)
    {
        var meter = meterFactory.Create(Name);

        _shipmentsQueried = meter.CreateCounter<long>(
            "contoso.shipments.queried",
            unit: "{shipment}",
            description: "Shipments returned to callers, by operation (board or lookup).");

        _statusLookups = meter.CreateCounter<long>(
            "contoso.shipments.status_lookups",
            unit: "{query}",
            description: "Board queries filtered by a status, by status.");

        _exceptionsObserved = meter.CreateCounter<long>(
            "contoso.shipments.exceptions_observed",
            unit: "{shipment}",
            description: "Tracking lookups that found the shipment in the Exception status, by carrier.");

        _carrierLookupDuration = meter.CreateHistogram(
            "contoso.shipments.carrier_lookup.duration",
            unit: "s",
            description: "Time spent fetching the latest scan from the carrier's tracking API, by carrier.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.04, 0.06, 0.08, 0.1, 0.12, 0.14, 0.16, 0.18, 0.2, 0.22, 0.25, 0.3] });

        meter.CreateObservableGauge(
            "contoso.shipments.active",
            () => Enum.GetValues<ShipmentStatus>()
                .Where(status => status != ShipmentStatus.Delivered)
                .Select(status => new Measurement<int>(store.CountByStatus(status), new KeyValuePair<string, object?>(Tags.Status, status.ToKebabCase()))),
            unit: "{shipment}",
            description: "Shipments on the board that haven't been delivered yet, by status.");
    }

    public void BoardQueried(ShipmentStatus? filter, int returned)
    {
        _shipmentsQueried.Add(returned, new KeyValuePair<string, object?>(Tags.Operation, "board"));

        if (filter is { } status)
        {
            _statusLookups.Add(1, new KeyValuePair<string, object?>(Tags.Status, status.ToKebabCase()));
        }
    }

    public void ShipmentTracked() =>
        _shipmentsQueried.Add(1, new KeyValuePair<string, object?>(Tags.Operation, "lookup"));

    public void ExceptionObserved(string carrier) =>
        _exceptionsObserved.Add(1, new KeyValuePair<string, object?>(Tags.Carrier, carrier));

    public void CarrierLookupCompleted(string carrier, TimeSpan elapsed) =>
        _carrierLookupDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>(Tags.Carrier, carrier));

    /// <summary>Attribute names shared by spans and metrics.</summary>
    public static class Tags
    {
        public const string ShipmentId = "contoso.shipment.id";
        public const string Status = "contoso.shipment.status";
        public const string Carrier = "contoso.shipment.carrier";
        public const string Operation = "contoso.shipment.operation";
        public const string StatusFilter = "contoso.shipment.status_filter";
        public const string ResultCount = "contoso.shipment.result_count";
        public const string ScanAt = "contoso.scan.at";
        public const string ScanLocation = "contoso.scan.location";
        public const string ScanDescription = "contoso.scan.description";
    }
}
