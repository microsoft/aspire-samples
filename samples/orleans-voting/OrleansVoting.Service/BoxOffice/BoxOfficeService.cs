using System.Diagnostics;
using OrleansVoting.Grains;

namespace OrleansVoting.BoxOffice;

/// <summary>
/// The front door to the box office for the HTTP API, the Blazor UI, and the on-sale rush simulator.
/// It routes each reservation through the fan's grain to the event's inventory grain and records the outcome.
/// </summary>
public sealed partial class BoxOfficeService(IGrainFactory grains, BoxOfficeTelemetry telemetry, ILogger<BoxOfficeService> logger)
{
    public Task<EventInventorySnapshot> GetInventoryAsync(string eventId) =>
        grains.GetGrain<IEventInventoryGrain>(eventId).GetSnapshot();

    public Task<Reservation?> GetReservationAsync(string eventId, string reservationId) =>
        grains.GetGrain<IEventInventoryGrain>(eventId).GetReservation(reservationId);

    public Task<Reservation?> CancelReservationAsync(string eventId, string reservationId) =>
        grains.GetGrain<IEventInventoryGrain>(eventId).CancelReservation(reservationId);

    public Task ResetAsync(string eventId) =>
        grains.GetGrain<IEventInventoryGrain>(eventId).Reset();

    public async Task<ReservationResult> ReserveAsync(EventDefinition @event, SectionDefinition section, string fanId, int quantity)
    {
        using var activity = BoxOfficeTelemetry.ActivitySource.StartActivity("reserve seats");
        activity?.SetTag("orleansticketing.event.id", @event.Id);
        activity?.SetTag("orleansticketing.section.id", section.Id);
        activity?.SetTag("orleansticketing.seat.tier", BoxOfficeTelemetry.TierName(section.Tier));
        activity?.SetTag("orleansticketing.fan.id", fanId);
        activity?.SetTag("orleansticketing.reservation.quantity", quantity);

        var start = Stopwatch.GetTimestamp();
        telemetry.ReservationStarted();

        ReservationResult result;
        try
        {
            result = await grains.GetGrain<IFanGrain>(fanId).Reserve(@event.Id, section.Id, quantity);
        }
        catch (Exception ex)
        {
            telemetry.ReservationFailed();
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        var outcome = BoxOfficeTelemetry.OutcomeName(result.Outcome);
        telemetry.ReservationFinished(result.Outcome, section.Tier, result.Reservation?.Quantity ?? 0, elapsed);

        activity?.SetTag("orleansticketing.reservation.outcome", outcome);

        if (result.Outcome == ReservationOutcome.Throttled)
        {
            // Throttled requests never reach the inventory grain, so there's no seat count to report
            logger.FanThrottled(fanId, result.RetryAfterSeconds);
            return result;
        }

        activity?.SetTag("orleansticketing.section.seats_remaining", result.SeatsRemaining);

        if (result.Reservation is { } reservation)
        {
            activity?.SetTag("orleansticketing.reservation.id", reservation.Id);
            activity?.SetTag("orleansticketing.reservation.total", (double)reservation.Total);
            logger.ReservationConfirmed(reservation.Id, fanId, quantity, section.Name, @event.Name, reservation.Total, result.SeatsRemaining, elapsed.TotalMilliseconds);
        }
        else
        {
            logger.ReservationRejected(fanId, quantity, section.Name, @event.Name, outcome, result.SeatsRemaining);
        }

        return result;
    }

    /// <summary>
    /// Subscribes <paramref name="watcher"/> to an event's inventory changes until the returned subscription is disposed.
    /// Orleans only holds a weak reference to the watcher, so the caller must keep it alive while subscribed.
    /// </summary>
    public IAsyncDisposable WatchEvent(string eventId, IEventWatcher watcher)
    {
        var inventory = grains.GetGrain<IEventInventoryGrain>(eventId);
        var reference = grains.CreateObjectReference<IEventWatcher>(watcher);
        return new EventWatcherSubscription(grains, watcher, inventory, reference);
    }
}
