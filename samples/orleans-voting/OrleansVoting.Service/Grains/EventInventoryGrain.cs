using System.Diagnostics;
using Orleans.Utilities;
using OrleansVoting.BoxOffice;

namespace OrleansVoting.Grains;

public sealed class EventInventoryGrain(
    [PersistentState("inventory", storageName: "inventory")] IPersistentState<EventInventoryState> state,
    ILocalSiloDetails siloDetails,
    BoxOfficeTelemetry telemetry,
    ILogger<EventInventoryGrain> logger) : Grain, IEventInventoryGrain
{
    public const int MaxSeatsPerFan = 8;
    private const int RecentActivityLimit = 12;

    private readonly ObserverManager<IEventWatcher> _watchers = new(TimeSpan.FromMinutes(1), logger);
    private readonly LinkedList<SaleActivity> _recentActivity = new();
    private EventDefinition _event = null!;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var eventId = this.GetPrimaryKeyString();
        _event = EventCatalog.Find(eventId) ?? throw new KeyNotFoundException($"Event '{eventId}' isn't in the catalog.");
        ReportInventory();
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        telemetry.RemoveInventory(_event.Id);
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task<EventInventorySnapshot> GetSnapshot() => Task.FromResult(CreateSnapshot());

    public async Task<ReservationResult> Reserve(string fanId, string sectionId, int quantity)
    {
        var section = _event.FindSection(sectionId) ?? throw new ArgumentException($"Section '{sectionId}' doesn't exist.", nameof(sectionId));
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        using var activity = BoxOfficeTelemetry.ActivitySource.StartActivity("allocate seats");
        activity?.SetTag("orleansticketing.event.id", _event.Id);
        activity?.SetTag("orleansticketing.section.id", section.Id);
        activity?.SetTag("orleansticketing.seat.tier", BoxOfficeTelemetry.TierName(section.Tier));
        activity?.SetTag("orleansticketing.fan.id", fanId);
        activity?.SetTag("orleansticketing.reservation.quantity", quantity);

        var sold = state.State.SoldBySection.GetValueOrDefault(section.Id);
        var available = section.Capacity - sold;
        var heldByFan = state.State.SeatsByFan.GetValueOrDefault(fanId);

        activity?.AddEvent(new("inventory.checked", tags: new()
        {
            ["orleansticketing.section.seats_available"] = available,
            ["orleansticketing.fan.seats_held"] = heldByFan
        }));

        var outcome = heldByFan + quantity > MaxSeatsPerFan ? ReservationOutcome.FanLimitReached
            : available == 0 ? ReservationOutcome.SoldOut
            : available < quantity ? ReservationOutcome.InsufficientSeats
            : ReservationOutcome.Confirmed;

        activity?.SetTag("orleansticketing.reservation.outcome", BoxOfficeTelemetry.OutcomeName(outcome));

        if (outcome != ReservationOutcome.Confirmed)
        {
            // Rejections only change the turned-away count, which is saved with the next sale
            state.State.TurnedAway++;
            Publish(new(DateTimeOffset.UtcNow, fanId, section.Id, quantity, ToActivityKind(outcome)));
            return new(outcome, null, available, heldByFan);
        }

        var reservation = new Reservation(NewReservationId(), _event.Id, section.Id, fanId, quantity, section.Price * quantity, DateTimeOffset.UtcNow);
        state.State.SoldBySection[section.Id] = sold + quantity;
        state.State.SeatsByFan[fanId] = heldByFan + quantity;
        state.State.Reservations[reservation.Id] = reservation;
        state.State.Revenue += reservation.Total;
        await WriteStateAsync();

        var remaining = available - quantity;
        activity?.SetTag("orleansticketing.reservation.id", reservation.Id);
        activity?.SetTag("orleansticketing.section.seats_remaining", remaining);
        activity?.AddEvent(new("seats.allocated", tags: new()
        {
            ["orleansticketing.reservation.id"] = reservation.Id,
            ["orleansticketing.section.seats_remaining"] = remaining
        }));

        if (remaining == 0)
        {
            activity?.AddEvent(new("section.sold_out"));
            logger.SectionSoldOut(section.Name, _event.Name, state.State.Reservations.Count);

            if (_event.Sections.All(s => state.State.SoldBySection.GetValueOrDefault(s.Id) >= s.Capacity))
            {
                activity?.AddEvent(new("event.sold_out"));
                logger.EventSoldOut(_event.Name, _event.Capacity, state.State.Revenue);
            }
        }

        Publish(new(reservation.ReservedAt, fanId, section.Id, quantity, SaleActivityKind.Reserved));
        return new(outcome, reservation, remaining, heldByFan + quantity);
    }

    public Task<Reservation?> GetReservation(string reservationId) =>
        Task.FromResult(state.State.Reservations.GetValueOrDefault(reservationId));

    public async Task<Reservation?> CancelReservation(string reservationId)
    {
        if (!state.State.Reservations.Remove(reservationId, out var reservation))
        {
            return null;
        }

        using var activity = BoxOfficeTelemetry.ActivitySource.StartActivity("release seats");
        activity?.SetTag("orleansticketing.event.id", _event.Id);
        activity?.SetTag("orleansticketing.reservation.id", reservation.Id);
        activity?.SetTag("orleansticketing.section.id", reservation.SectionId);
        activity?.SetTag("orleansticketing.reservation.quantity", reservation.Quantity);

        state.State.SoldBySection[reservation.SectionId] -= reservation.Quantity;
        state.State.SeatsByFan[reservation.FanId] -= reservation.Quantity;
        state.State.Revenue -= reservation.Total;
        await WriteStateAsync();

        var section = _event.FindSection(reservation.SectionId)!;
        logger.ReservationCancelled(reservation.Id, reservation.Quantity, section.Name, _event.Name);
        Publish(new(DateTimeOffset.UtcNow, reservation.FanId, reservation.SectionId, reservation.Quantity, SaleActivityKind.Cancelled));
        return reservation;
    }

    public async Task Reset()
    {
        var seatsReleased = state.State.SoldBySection.Values.Sum();
        var reservationCount = state.State.Reservations.Count;

        await state.ClearStateAsync();
        state.State = new();
        _recentActivity.Clear();

        logger.InventoryReset(_event.Name, seatsReleased, reservationCount);
        Publish(change: null);
    }

    public Task<EventInventorySnapshot> StartWatching(IEventWatcher watcher)
    {
        _watchers.Subscribe(watcher, watcher);
        return Task.FromResult(CreateSnapshot());
    }

    public Task StopWatching(IEventWatcher watcher)
    {
        _watchers.Unsubscribe(watcher);
        return Task.CompletedTask;
    }

    private async Task WriteStateAsync()
    {
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            // Never keep a change that wasn't persisted: the next call reloads the inventory from storage
            DeactivateOnIdle();
            throw;
        }
    }

    private void Publish(SaleActivity? change)
    {
        if (change is not null)
        {
            _recentActivity.AddFirst(change);
            if (_recentActivity.Count > RecentActivityLimit)
            {
                _recentActivity.RemoveLast();
            }
        }

        _updatedAt = DateTimeOffset.UtcNow;
        ReportInventory();

        var snapshot = CreateSnapshot();
        _watchers.Notify(watcher => watcher.OnInventoryChanged(snapshot));
    }

    private void ReportInventory() => telemetry.ReportInventory(_event.Id, _event.Sections
        .GroupBy(s => s.Tier)
        .ToDictionary(g => g.Key, g => g.Sum(s => s.Capacity - state.State.SoldBySection.GetValueOrDefault(s.Id))));

    private EventInventorySnapshot CreateSnapshot() => new(
        _event.Id,
        [.. _event.Sections.Select(s => new SectionInventory(s.Id, s.Capacity, state.State.SoldBySection.GetValueOrDefault(s.Id)))],
        state.State.Reservations.Count,
        state.State.TurnedAway,
        state.State.Revenue,
        [.. _recentActivity],
        siloDetails.SiloAddress.ToString(),
        _updatedAt);

    private static SaleActivityKind ToActivityKind(ReservationOutcome outcome) => outcome switch
    {
        ReservationOutcome.SoldOut => SaleActivityKind.SoldOut,
        ReservationOutcome.InsufficientSeats => SaleActivityKind.InsufficientSeats,
        ReservationOutcome.FanLimitReached => SaleActivityKind.FanLimitReached,
        _ => SaleActivityKind.Reserved
    };

    private string NewReservationId()
    {
        string id;
        do
        {
            id = $"GT-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }
        while (state.State.Reservations.ContainsKey(id));

        return id;
    }
}
