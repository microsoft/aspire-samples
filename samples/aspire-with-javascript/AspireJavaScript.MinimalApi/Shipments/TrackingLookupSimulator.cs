using System.Diagnostics;

namespace AspireJavaScript.MinimalApi.Shipments;

/// <summary>
/// Simulates customers who open the tracking board and look up a few shipments, including mistyped tracking numbers.
/// Backs the AppHost's "Simulate tracking lookups" command so traces, metrics, and logs populate without using a UI.
/// </summary>
public sealed class TrackingLookupSimulator(ShipmentTracker tracker, ShipmentStore store, ILogger<TrackingLookupSimulator> logger)
{
    private const int LookupsPerVisitor = 3;

    public async Task<SimulationSummary> RunAsync(int visitors, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var trigger = Activity.Current?.Context;

        var results = await Task.WhenAll(Enumerable.Range(0, visitors).Select(i => RunVisitorAsync(i, trigger, cancellationToken)));

        var summary = new SimulationSummary(
            BoardQueries: visitors,
            Lookups: visitors * LookupsPerVisitor,
            NotFound: results.Sum(r => r.NotFound),
            ExceptionsObserved: results.Sum(r => r.Exceptions),
            DurationMs: Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));

        logger.LookupsSimulated(summary.BoardQueries, summary.Lookups, summary.NotFound, summary.ExceptionsObserved);
        return summary;
    }

    private async Task<(int NotFound, int Exceptions)> RunVisitorAsync(int index, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        // Stagger visitors so the dashboard shows a stream of lookups rather than a single burst
        await Task.Delay(TimeSpan.FromMilliseconds(index * 250), cancellationToken);

        // Give each visitor its own trace, linked back to the request that started the simulation
        Activity.Current = null;
        using var activity = ShipmentTelemetry.ActivitySource.StartActivity(
            "simulated tracking session", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var statuses = Enum.GetValues<ShipmentStatus>();
        ShipmentStatus? filter = index % 2 == 0 ? null : statuses[index / 2 % statuses.Length];
        var board = tracker.GetBoard(filter);

        var trackingNumbers = store.TrackingNumbers;
        int notFound = 0, exceptions = 0;

        for (var i = 0; i < LookupsPerVisitor; i++)
        {
            await Task.Delay(Random.Shared.Next(40, 160), cancellationToken);

            // Most customers pick from the board; roughly one lookup in ten is a mistyped tracking number
            var id = (index * LookupsPerVisitor + i) % 10 == 7
                ? $"CTS-{Random.Shared.Next(48_220_000, 48_299_999)}"
                : i == 0 && board.Shipments.Count > 0
                    ? board.Shipments[Random.Shared.Next(board.Shipments.Count)].Id
                    : trackingNumbers[Random.Shared.Next(trackingNumbers.Count)];

            var detail = await tracker.TrackAsync(id, cancellationToken);
            if (detail is null)
            {
                notFound++;
            }
            else if (detail.Shipment.Status == ShipmentStatus.Exception)
            {
                exceptions++;
            }
        }

        activity?.SetTag("contoso.simulation.lookups", LookupsPerVisitor);
        activity?.SetTag("contoso.simulation.not_found", notFound);
        return (notFound, exceptions);
    }
}
