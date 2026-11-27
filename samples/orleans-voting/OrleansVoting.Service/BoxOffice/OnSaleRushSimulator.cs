using System.Diagnostics;
using OrleansVoting.Grains;

namespace OrleansVoting.BoxOffice;

/// <summary>
/// Simulates the moment tickets go on sale: hundreds of fans arrive within seconds and all contend for the same
/// event's inventory grain. Most want the best seats, some settle for another section when theirs sells out, and a
/// few scalpers come back for more than the per-fan limit allows. Backs the AppHost's "Simulate on-sale rush" command
/// and the box office page's demo button.
/// </summary>
public sealed class OnSaleRushSimulator(BoxOfficeService boxOffice, ILogger<OnSaleRushSimulator> logger)
{
    public const int DefaultFans = 400;
    public const int MaxFans = 2000;

    // Arrivals are front-loaded across this window, like a queue opening at the on-sale time
    private static readonly TimeSpan ArrivalWindow = TimeSpan.FromSeconds(12);

    private static readonly Dictionary<SeatTier, double> TierDemand = new()
    {
        [SeatTier.Vip] = 0.14,
        [SeatTier.Floor] = 0.40,
        [SeatTier.Lower] = 0.28,
        [SeatTier.Upper] = 0.18
    };

    public async Task<OnSaleRushSummary> RunAsync(EventDefinition @event, int fans, CancellationToken cancellationToken)
    {
        using var activity = BoxOfficeTelemetry.ActivitySource.StartActivity("on-sale rush");
        activity?.SetTag("orleansticketing.event.id", @event.Id);
        activity?.SetTag("orleansticketing.rush.fans", fans);

        var trigger = Activity.Current?.Context;
        var start = Stopwatch.GetTimestamp();
        logger.OnSaleRushStarted(fans, @event.Name);

        var results = await Task.WhenAll(Enumerable.Range(0, fans).Select(i => RunFanAsync(@event, i, fans, trigger, cancellationToken)));
        var inventory = await boxOffice.GetInventoryAsync(@event.Id);
        var elapsed = Stopwatch.GetElapsedTime(start);

        var summary = new OnSaleRushSummary(
            @event.Id,
            fans,
            Reservations: results.Sum(r => r.Reservations),
            SeatsReserved: results.Sum(r => r.Seats),
            Revenue: results.Sum(r => r.Spent),
            FansTurnedAway: results.Count(r => r.Reservations == 0 && !r.Failed),
            SoldOutRejections: results.Sum(r => r.SoldOut),
            InsufficientSeatRejections: results.Sum(r => r.InsufficientSeats),
            FanLimitRejections: results.Sum(r => r.FanLimitReached),
            Failed: results.Count(r => r.Failed),
            SeatsRemaining: inventory.Available,
            ElapsedSeconds: Math.Round(elapsed.TotalSeconds, 1));

        activity?.SetTag("orleansticketing.rush.reservations", summary.Reservations);
        activity?.SetTag("orleansticketing.rush.fans_turned_away", summary.FansTurnedAway);
        logger.OnSaleRushFinished(@event.Name, fans, summary.Reservations, summary.SeatsReserved, summary.FansTurnedAway, summary.SeatsRemaining, elapsed.TotalSeconds);

        return summary;
    }

    private async Task<FanResult> RunFanAsync(EventDefinition @event, int index, int fans, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        await Task.Delay(ArrivalWindow * Math.Pow((double)index / fans, 2), cancellationToken);

        // Give each fan its own trace, linked back to the request that started the rush
        Activity.Current = null;
        using var activity = BoxOfficeTelemetry.ActivitySource.StartActivity(
            "simulated fan", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var fanId = $"fan-{Guid.NewGuid().ToString("N")[..6]}";
        activity?.SetTag("orleansticketing.fan.id", fanId);

        var result = new FanResult();

        try
        {
            var isScalper = Random.Shared.NextDouble() < 0.03;
            var section = PickSection(@event);
            var quantity = isScalper ? 6 : PickQuantity();

            await ThinkAsync(cancellationToken);
            var attempt = result.Track(await boxOffice.ReserveAsync(@event, section, fanId, quantity));

            if (isScalper && attempt.Outcome == ReservationOutcome.Confirmed)
            {
                // Scalpers come straight back for more and run into the per-fan limit
                activity?.AddEvent(new("fan.returned"));
                result.Track(await boxOffice.ReserveAsync(@event, section, fanId, quantity));
            }
            else if (attempt.Outcome is ReservationOutcome.SoldOut or ReservationOutcome.InsufficientSeats && Random.Shared.NextDouble() < 0.7)
            {
                // Most fans who miss out check what's left and settle for another section
                var inventory = await boxOffice.GetInventoryAsync(@event.Id);
                var fallback = @event.Sections
                    .Where(s => s.Id != section.Id && inventory.FindSection(s.Id)?.Available >= quantity)
                    .OrderBy(_ => Random.Shared.Next())
                    .FirstOrDefault();

                if (fallback is not null)
                {
                    activity?.AddEvent(new("fan.retried", tags: new() { ["orleansticketing.section.id"] = fallback.Id }));
                    await ThinkAsync(cancellationToken);
                    result.Track(await boxOffice.ReserveAsync(@event, fallback, fanId, quantity));
                }
            }

            activity?.SetTag("orleansticketing.fan.seats_reserved", result.Seats);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.SimulatedFanFailed(ex, fanId);
            result.Failed = true;
            return result;
        }
    }

    private static SectionDefinition PickSection(EventDefinition @event)
    {
        var weights = @event.Sections.Select(s => TierDemand[s.Tier]).ToArray();
        var pick = Random.Shared.NextDouble() * weights.Sum();

        for (var i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];
            if (pick <= 0)
            {
                return @event.Sections[i];
            }
        }

        return @event.Sections[^1];
    }

    private static int PickQuantity() => Random.Shared.NextDouble() switch
    {
        < 0.20 => 1,
        < 0.65 => 2,
        < 0.75 => 3,
        < 0.95 => 4,
        _ => 6
    };

    private static Task ThinkAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 600)), cancellationToken);

    private sealed class FanResult
    {
        public int Reservations { get; private set; }
        public int Seats { get; private set; }
        public decimal Spent { get; private set; }
        public int SoldOut { get; private set; }
        public int InsufficientSeats { get; private set; }
        public int FanLimitReached { get; private set; }
        public bool Failed { get; set; }

        public ReservationResult Track(ReservationResult result)
        {
            switch (result.Outcome)
            {
                case ReservationOutcome.Confirmed:
                    Reservations++;
                    Seats += result.Reservation!.Quantity;
                    Spent += result.Reservation.Total;
                    break;
                case ReservationOutcome.SoldOut:
                    SoldOut++;
                    break;
                case ReservationOutcome.InsufficientSeats:
                    InsufficientSeats++;
                    break;
                case ReservationOutcome.FanLimitReached:
                    FanLimitReached++;
                    break;
            }

            return result;
        }
    }
}

/// <summary>The result of a simulated on-sale rush.</summary>
public sealed record OnSaleRushSummary(
    string EventId,
    int Fans,
    int Reservations,
    int SeatsReserved,
    decimal Revenue,
    int FansTurnedAway,
    int SoldOutRejections,
    int InsufficientSeatRejections,
    int FanLimitRejections,
    int Failed,
    int SeatsRemaining,
    double ElapsedSeconds);
