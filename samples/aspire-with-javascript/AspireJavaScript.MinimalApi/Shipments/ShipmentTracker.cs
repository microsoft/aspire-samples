using System.Diagnostics;

namespace AspireJavaScript.MinimalApi.Shipments;

/// <summary>
/// Serves the tracking board and tracks single shipments. Each lookup asks the (simulated) carrier tracking API for
/// the latest scan, so a trace shows the request, the <c>shipments.track</c> domain span, and the <c>carrier.track</c> call.
/// </summary>
public sealed class ShipmentTracker(ShipmentStore store, ShipmentTelemetry telemetry, ILogger<ShipmentTracker> logger)
{
    // Typical response times of each carrier's tracking API, so the latency histogram has a realistic spread
    private static readonly Dictionary<string, int> CarrierBaseLatencyMs = new()
    {
        ["Northwind Express"] = 45,
        ["Tailspin Air Cargo"] = 70,
        ["Fabrikam Freight"] = 95,
        ["Wingtip Ground"] = 120
    };

    public ShipmentBoard GetBoard(ShipmentStatus? filter)
    {
        using var activity = ShipmentTelemetry.ActivitySource.StartActivity("shipments.board");
        var filterName = filter?.ToKebabCase() ?? "all";
        activity?.SetTag(ShipmentTelemetry.Tags.StatusFilter, filterName);

        var shipments = store.Query(filter);

        activity?.SetTag(ShipmentTelemetry.Tags.ResultCount, shipments.Count);
        telemetry.BoardQueried(filter, shipments.Count);
        logger.BoardQueried(filterName, shipments.Count);

        return new ShipmentBoard(filter, store.Count, store.StatusCounts(), shipments);
    }

    public async Task<ShipmentDetail?> TrackAsync(string id, CancellationToken cancellationToken)
    {
        using var activity = ShipmentTelemetry.ActivitySource.StartActivity("shipments.track");
        activity?.SetTag(ShipmentTelemetry.Tags.ShipmentId, id);

        if (store.Find(id) is not { } detail)
        {
            activity?.AddEvent(new ActivityEvent("shipment.not_found"));
            logger.ShipmentNotFound(id);
            return null;
        }

        var shipment = detail.Shipment;
        activity?.SetTag(ShipmentTelemetry.Tags.Carrier, shipment.Carrier);
        activity?.SetTag(ShipmentTelemetry.Tags.Status, shipment.Status.ToKebabCase());

        var latest = await QueryCarrierAsync(shipment, cancellationToken);
        telemetry.ShipmentTracked();

        if (shipment.Status == ShipmentStatus.Exception)
        {
            activity?.AddEvent(new ActivityEvent("shipment.exception_observed", tags: new ActivityTagsCollection
            {
                [ShipmentTelemetry.Tags.ScanLocation] = latest.Location,
                [ShipmentTelemetry.Tags.ScanDescription] = latest.Description
            }));
            telemetry.ExceptionObserved(shipment.Carrier);
            logger.ShipmentExceptionObserved(shipment.Id, shipment.Carrier, latest.Location, latest.Description);
        }
        else
        {
            logger.ShipmentTracked(shipment.Id, shipment.Status.ToKebabCase(), shipment.Carrier, latest.Location);
        }

        return detail;
    }

    private async Task<ScanEvent> QueryCarrierAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        using var activity = ShipmentTelemetry.ActivitySource.StartActivity("carrier.track");
        activity?.SetTag(ShipmentTelemetry.Tags.Carrier, shipment.Carrier);
        activity?.SetTag(ShipmentTelemetry.Tags.ShipmentId, shipment.Id);

        var started = Stopwatch.GetTimestamp();

        // Stand-in for the carrier's tracking API; shipments with an exception take longer to resolve
        var latency = CarrierBaseLatencyMs.GetValueOrDefault(shipment.Carrier, 80)
            + Random.Shared.Next(0, 70)
            + (shipment.Status == ShipmentStatus.Exception ? 40 : 0);
        await Task.Delay(latency, cancellationToken);

        var scan = shipment.LastScan;
        activity?.AddEvent(new ActivityEvent("carrier.scan_received", tags: new ActivityTagsCollection
        {
            [ShipmentTelemetry.Tags.ScanAt] = scan.At.ToString("O"),
            [ShipmentTelemetry.Tags.ScanLocation] = scan.Location,
            [ShipmentTelemetry.Tags.ScanDescription] = scan.Description
        }));

        // Recorded inside the span so the histogram's exemplars link back to this trace
        telemetry.CarrierLookupCompleted(shipment.Carrier, Stopwatch.GetElapsedTime(started));
        return scan;
    }
}
