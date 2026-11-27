using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using OrleansVoting.BoxOffice;
using OrleansVoting.Grains;

namespace OrleansVoting.Api;

[Description("Seat availability: `on_sale`, `selling_fast` when fewer than 20% of the seats are left, or `sold_out`.")]
public enum AvailabilityStatus
{
    OnSale,
    SellingFast,
    SoldOut
}

[Description("An event on sale, with live availability.")]
public sealed record EventSummary(
    [property: Description("The event ID, used in every event route.")] string Id,
    [property: Description("The headliner.")] string Performer,
    [property: Description("The tour or show name.")] string Title,
    [property: Description("The kind of event, such as `Concert`, `Classical`, `Esports`, or `Comedy`.")] string Category,
    [property: Description("The venue name.")] string Venue,
    [property: Description("The venue's city and state.")] string City,
    [property: Description("When the show starts, with the venue's UTC offset.")] DateTimeOffset StartsAt,
    [property: Description("The lowest seat price, in USD.")] decimal PriceFrom,
    [property: Description("Total seats across every section.")] int Capacity,
    [property: Description("Seats that haven't been reserved yet.")] int SeatsAvailable,
    [property: Description("`selling_fast` when fewer than 20% of the seats are left.")] AvailabilityStatus Status);

[Description("Live availability for one section of an event.")]
public sealed record SectionAvailability(
    [property: Description("The section ID, used when reserving seats.")] string Id,
    [property: Description("The section name shown to fans.")] string Name,
    [property: Description("The seat tier.")] SeatTier Tier,
    [property: Description("The price per seat, in USD.")] decimal Price,
    [property: Description("Seats in the section.")] int Capacity,
    [property: Description("Seats that haven't been reserved yet.")] int SeatsAvailable,
    [property: Description("`selling_fast` when fewer than 20% of the seats are left.")] AvailabilityStatus Status);

[Description("An event with live availability for each section, read from the event's inventory grain.")]
public sealed record EventDetails(
    [property: Description("The event ID.")] string Id,
    [property: Description("The headliner.")] string Performer,
    [property: Description("The tour or show name.")] string Title,
    [property: Description("The kind of event.")] string Category,
    [property: Description("The venue name.")] string Venue,
    [property: Description("The venue's city and state.")] string City,
    [property: Description("When the show starts, with the venue's UTC offset.")] DateTimeOffset StartsAt,
    [property: Description("Total seats across every section.")] int Capacity,
    [property: Description("Seats that haven't been reserved yet.")] int SeatsAvailable,
    [property: Description("Overall availability.")] AvailabilityStatus Status,
    [property: Description("Reservations held for the event.")] int Reservations,
    [property: Description("Reservation attempts that were turned away because a section was sold out, too few seats were left, or the fan hit the ticket limit.")] int TurnedAway,
    [property: Description("Ticket revenue from the reservations held, in USD.")] decimal Revenue,
    [property: Description("The Orleans silo that hosts the event's inventory grain.")] string InventorySilo,
    [property: Description("Availability for each section.")] IReadOnlyList<SectionAvailability> Sections);

[Description("A request to reserve seats in one section of an event.")]
public sealed record ReservationRequest(
    [property: Description("The fan making the reservation. A fan can hold at most 8 seats per event.")]
    [property: Required, StringLength(64, MinimumLength = 3)] string FanId,
    [property: Description("The section to reserve seats in, such as `floor`. See `GET /api/events/{eventId}` for the sections.")]
    [property: Required] string SectionId,
    [property: Description("The number of seats to reserve (1-8).")]
    [property: Range(1, EventInventoryGrain.MaxSeatsPerFan)] int Quantity);

[Description("A seat reservation.")]
public sealed record ReservationResponse(
    [property: Description("The reservation ID.")] string Id,
    [property: Description("The event ID.")] string EventId,
    [property: Description("The event name.")] string EventName,
    [property: Description("The section ID.")] string SectionId,
    [property: Description("The section name.")] string SectionName,
    [property: Description("The fan who holds the reservation.")] string FanId,
    [property: Description("The number of seats reserved.")] int Quantity,
    [property: Description("The price per seat, in USD.")] decimal UnitPrice,
    [property: Description("The total price, in USD.")] decimal Total,
    [property: Description("When the seats were reserved (UTC).")] DateTimeOffset ReservedAt);
