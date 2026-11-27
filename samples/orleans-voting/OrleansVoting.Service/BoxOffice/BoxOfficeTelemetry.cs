using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OrleansVoting.Grains;

namespace OrleansVoting.BoxOffice;

/// <summary>
/// Domain traces and metrics for the box office. ServiceDefaults registers the "OrleansTicketing.*" meters and
/// activity sources with OpenTelemetry. Metric tags are limited to the reservation outcome and seat tier; event,
/// fan, and reservation IDs only appear on spans and logs.
/// </summary>
public sealed class BoxOfficeTelemetry
{
    public const string Name = "OrleansTicketing.BoxOffice";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _reservations;
    private readonly Counter<long> _seatsReserved;
    private readonly Histogram<double> _reservationDuration;
    private readonly UpDownCounter<long> _reservationsInFlight;

    // Seats still available per tier for each event whose inventory grain is active in this silo
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<SeatTier, int>> _availableByEvent = new();

    public BoxOfficeTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _reservations = meter.CreateCounter<long>(
            "orleansticketing.boxoffice.reservations",
            unit: "{reservation}",
            description: "Reservation attempts, by outcome and seat tier.");

        _seatsReserved = meter.CreateCounter<long>(
            "orleansticketing.boxoffice.seats.reserved",
            unit: "{seat}",
            description: "Seats reserved, by seat tier.");

        _reservationDuration = meter.CreateHistogram(
            "orleansticketing.boxoffice.reservation.duration",
            unit: "s",
            description: "Time for a reservation to get an answer from the event's inventory grain, including time queued behind other fans.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5] });

        _reservationsInFlight = meter.CreateUpDownCounter<long>(
            "orleansticketing.boxoffice.reservations.in_flight",
            unit: "{reservation}",
            description: "Reservations waiting for an answer from an inventory grain. Spikes when fans contend for the same event.");

        meter.CreateObservableGauge(
            "orleansticketing.boxoffice.seats.available",
            ObserveSeatsAvailable,
            unit: "{seat}",
            description: "Seats still available, by seat tier, across the events whose inventory grains are active in this silo.");
    }

    public void ReservationStarted() => _reservationsInFlight.Add(1);

    public void ReservationFinished(ReservationOutcome outcome, SeatTier tier, int seatsReserved, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "orleansticketing.reservation.outcome", OutcomeName(outcome) },
            { "orleansticketing.seat.tier", TierName(tier) }
        };

        _reservationsInFlight.Add(-1);
        _reservations.Add(1, tags);
        _reservationDuration.Record(elapsed.TotalSeconds, tags);

        if (seatsReserved > 0)
        {
            _seatsReserved.Add(seatsReserved, new KeyValuePair<string, object?>("orleansticketing.seat.tier", TierName(tier)));
        }
    }

    public void ReservationFailed() => _reservationsInFlight.Add(-1);

    public void ReportInventory(string eventId, IReadOnlyDictionary<SeatTier, int> availableByTier) =>
        _availableByEvent[eventId] = availableByTier;

    public void RemoveInventory(string eventId) => _availableByEvent.TryRemove(eventId, out _);

    private IEnumerable<Measurement<int>> ObserveSeatsAvailable() =>
        _availableByEvent.Values
            .SelectMany(e => e)
            .GroupBy(e => e.Key, e => e.Value)
            .Select(g => new Measurement<int>(g.Sum(), new KeyValuePair<string, object?>("orleansticketing.seat.tier", TierName(g.Key))));

    public static string OutcomeName(ReservationOutcome outcome) => outcome switch
    {
        ReservationOutcome.Confirmed => "confirmed",
        ReservationOutcome.SoldOut => "sold_out",
        ReservationOutcome.InsufficientSeats => "insufficient_seats",
        ReservationOutcome.FanLimitReached => "fan_limit_reached",
        ReservationOutcome.Throttled => "throttled",
        _ => "unknown"
    };

    public static string TierName(SeatTier tier) => tier switch
    {
        SeatTier.Vip => "vip",
        SeatTier.Floor => "floor",
        SeatTier.Lower => "lower",
        SeatTier.Upper => "upper",
        _ => "unknown"
    };
}
