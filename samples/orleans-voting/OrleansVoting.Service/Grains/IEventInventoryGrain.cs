namespace OrleansVoting.Grains;

/// <summary>
/// Owns the seat inventory for one event (the grain key is the event ID). Orleans keeps a single activation
/// per event across the cluster and runs its calls one at a time, so seats can never be double-sold, and fans
/// who rush the same event queue up behind each other on this grain.
/// </summary>
public interface IEventInventoryGrain : IGrainWithStringKey
{
    Task<EventInventorySnapshot> GetSnapshot();
    Task<ReservationResult> Reserve(string fanId, string sectionId, int quantity);
    Task<Reservation?> GetReservation(string reservationId);
    Task<Reservation?> CancelReservation(string reservationId);
    Task Reset();
    /// <summary>Subscribes (or renews) a watcher and returns the current snapshot, so the watcher starts from up-to-date state.</summary>
    Task<EventInventorySnapshot> StartWatching(IEventWatcher watcher);
    Task StopWatching(IEventWatcher watcher);
}

/// <summary>Receives a fresh snapshot whenever an event's inventory changes.</summary>
public interface IEventWatcher : IGrainObserver
{
    void OnInventoryChanged(EventInventorySnapshot snapshot);
}

public enum ReservationOutcome
{
    Confirmed,
    SoldOut,
    InsufficientSeats,
    FanLimitReached,
    Throttled
}

public enum SaleActivityKind
{
    Reserved,
    SoldOut,
    InsufficientSeats,
    FanLimitReached,
    Cancelled
}

[GenerateSerializer, Immutable, Alias("OrleansTicketing.Reservation")]
public sealed record Reservation(
    string Id, string EventId, string SectionId, string FanId, int Quantity, decimal Total, DateTimeOffset ReservedAt);

[GenerateSerializer, Immutable, Alias("OrleansTicketing.ReservationResult")]
public sealed record ReservationResult(
    ReservationOutcome Outcome, Reservation? Reservation, int SeatsRemaining, int SeatsHeldByFan, TimeSpan? RetryAfter = null)
{
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter?.TotalSeconds ?? 1));
}

[GenerateSerializer, Immutable, Alias("OrleansTicketing.SectionInventory")]
public sealed record SectionInventory(string SectionId, int Capacity, int Sold)
{
    public int Available => Capacity - Sold;
}

[GenerateSerializer, Immutable, Alias("OrleansTicketing.SaleActivity")]
public sealed record SaleActivity(DateTimeOffset At, string FanId, string SectionId, int Quantity, SaleActivityKind Kind);

[GenerateSerializer, Immutable, Alias("OrleansTicketing.EventInventorySnapshot")]
public sealed record EventInventorySnapshot(
    string EventId,
    SectionInventory[] Sections,
    int ReservationCount,
    int TurnedAway,
    decimal Revenue,
    SaleActivity[] RecentActivity,
    string SiloAddress,
    DateTimeOffset UpdatedAt)
{
    public int Capacity => Sections.Sum(s => s.Capacity);

    public int Sold => Sections.Sum(s => s.Sold);

    public int Available => Capacity - Sold;

    public SectionInventory? FindSection(string sectionId) =>
        Sections.FirstOrDefault(s => string.Equals(s.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The persisted part of an event's inventory. The event and its sections come from the catalog.</summary>
[GenerateSerializer, Alias("OrleansTicketing.EventInventoryState")]
public sealed class EventInventoryState
{
    [Id(0)] public Dictionary<string, int> SoldBySection { get; set; } = [];
    [Id(1)] public Dictionary<string, Reservation> Reservations { get; set; } = [];
    [Id(2)] public Dictionary<string, int> SeatsByFan { get; set; } = [];
    [Id(3)] public int TurnedAway { get; set; }
    [Id(4)] public decimal Revenue { get; set; }
}
