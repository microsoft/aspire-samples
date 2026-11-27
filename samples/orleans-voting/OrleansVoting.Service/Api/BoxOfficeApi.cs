using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using OrleansVoting.BoxOffice;
using OrleansVoting.Grains;

namespace OrleansVoting.Api;

public static class BoxOfficeApi
{
    public static RouteGroupBuilder MapBoxOfficeApi(this IEndpointRouteBuilder routes)
    {
        var events = routes.MapGroup("/api/events");

        events.MapGet("", async (BoxOfficeService boxOffice) =>
            {
                var inventories = await Task.WhenAll(EventCatalog.Events.Select(e => boxOffice.GetInventoryAsync(e.Id)));
                return TypedResults.Ok(EventCatalog.Events.Zip(inventories, ToSummary).ToArray());
            })
            .WithName("ListEvents")
            .WithTags("Events")
            .WithSummary("List events on sale")
            .WithDescription("Returns every event on sale with its live seat availability. Each event's availability comes from its own inventory grain.");

        events.MapGet("{eventId}", async Task<Results<Ok<EventDetails>, ProblemHttpResult>> (
                [Description("The event ID, for example `midnight-arcade-seattle`.")] string eventId,
                BoxOfficeService boxOffice) =>
            {
                if (EventCatalog.Find(eventId) is not { } @event)
                {
                    return EventNotFound(eventId);
                }

                return TypedResults.Ok(ToDetails(@event, await boxOffice.GetInventoryAsync(@event.Id)));
            })
            .WithName("GetEvent")
            .WithTags("Events")
            .WithSummary("Get an event's live availability")
            .WithDescription("Returns an event with live availability for each section, plus the number of reservations held, the attempts turned away, and the Orleans silo that hosts the event's inventory grain.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapPost("{eventId}/reservations", async Task<Results<Created<ReservationResponse>, ValidationProblem, ProblemHttpResult>> (
                [Description("The event ID, for example `midnight-arcade-seattle`.")] string eventId,
                ReservationRequest request,
                BoxOfficeService boxOffice,
                HttpContext httpContext) =>
            {
                if (EventCatalog.Find(eventId) is not { } @event)
                {
                    return EventNotFound(eventId);
                }

                if (Validate(@event, request) is { } errors)
                {
                    return TypedResults.ValidationProblem(errors, title: "Invalid reservation request");
                }

                var section = @event.FindSection(request.SectionId)!;
                var result = await boxOffice.ReserveAsync(@event, section, request.FanId, request.Quantity);

                if (result.Reservation is { } reservation)
                {
                    return TypedResults.Created($"/api/events/{@event.Id}/reservations/{reservation.Id}", ToResponse(@event, reservation));
                }

                if (result.RetryAfter is not null)
                {
                    httpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                }

                return ReservationRejected(@event, section, request, result);
            })
            .WithName("ReserveSeats")
            .WithTags("Reservations")
            .WithSummary("Reserve seats")
            .WithDescription("""
                Reserves seats in one section of an event. The request goes through the fan's grain, which throttles bursts,
                to the event's inventory grain, which handles one reservation at a time so seats are never double-sold.

                Returns `201 Created` with the reservation, or a problem details response: `404` for an unknown event,
                `409` when the section is sold out, has too few seats left, or the fan would exceed 8 seats for the event,
                and `429` (with `Retry-After`) when the fan sends requests too quickly.
                """)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        events.MapGet("{eventId}/reservations/{reservationId}", async Task<Results<Ok<ReservationResponse>, ProblemHttpResult>> (
                [Description("The event ID, for example `midnight-arcade-seattle`.")] string eventId,
                [Description("The reservation ID, for example `GT-7F3A91`.")] string reservationId,
                BoxOfficeService boxOffice) =>
            {
                if (EventCatalog.Find(eventId) is not { } @event)
                {
                    return EventNotFound(eventId);
                }

                return await boxOffice.GetReservationAsync(@event.Id, reservationId) is { } reservation
                    ? TypedResults.Ok(ToResponse(@event, reservation))
                    : ReservationNotFound(@event, reservationId);
            })
            .WithName("GetReservation")
            .WithTags("Reservations")
            .WithSummary("Get a reservation")
            .WithDescription("Returns a reservation held for an event.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapDelete("{eventId}/reservations/{reservationId}", async Task<Results<NoContent, ProblemHttpResult>> (
                [Description("The event ID, for example `midnight-arcade-seattle`.")] string eventId,
                [Description("The reservation ID, for example `GT-7F3A91`.")] string reservationId,
                BoxOfficeService boxOffice) =>
            {
                if (EventCatalog.Find(eventId) is not { } @event)
                {
                    return EventNotFound(eventId);
                }

                return await boxOffice.CancelReservationAsync(@event.Id, reservationId) is not null
                    ? TypedResults.NoContent()
                    : ReservationNotFound(@event, reservationId);
            })
            .WithName("CancelReservation")
            .WithTags("Reservations")
            .WithSummary("Cancel a reservation")
            .WithDescription("Cancels a reservation and releases its seats back to the section, so other fans can reserve them.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return events;
    }

    /// <summary>Development-only endpoints that back the AppHost's dashboard commands.</summary>
    public static RouteGroupBuilder MapBoxOfficeDevEndpoints(this IEndpointRouteBuilder routes)
    {
        var dev = routes.MapGroup("/dev").ExcludeFromDescription();

        dev.MapPost("simulate-rush", async Task<Results<Ok<OnSaleRushSummary>, ProblemHttpResult>> (
                OnSaleRushSimulator simulator,
                CancellationToken cancellationToken,
                string eventId = EventCatalog.HeadlineEventId,
                int fans = OnSaleRushSimulator.DefaultFans) =>
            {
                if (EventCatalog.Find(eventId) is not { } @event)
                {
                    return EventNotFound(eventId);
                }

                return TypedResults.Ok(await simulator.RunAsync(@event, Math.Clamp(fans, 1, OnSaleRushSimulator.MaxFans), cancellationToken));
            });

        dev.MapPost("reset", async (BoxOfficeService boxOffice) =>
            {
                await Task.WhenAll(EventCatalog.Events.Select(e => boxOffice.ResetAsync(e.Id)));
                return TypedResults.Ok(new { EventsReset = EventCatalog.Events.Count });
            });

        return dev;
    }

    private static Dictionary<string, string[]>? Validate(EventDefinition @event, ReservationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FanId) || request.FanId.Length is < 3 or > 64)
        {
            errors[nameof(request.FanId)] = ["'fanId' must be between 3 and 64 characters."];
        }

        if (@event.FindSection(request.SectionId ?? "") is null)
        {
            errors[nameof(request.SectionId)] = [$"'{request.SectionId}' isn't a section of this event. Choose one of: {string.Join(", ", @event.Sections.Select(s => s.Id))}."];
        }

        if (request.Quantity is < 1 or > EventInventoryGrain.MaxSeatsPerFan)
        {
            errors[nameof(request.Quantity)] = [$"'quantity' must be between 1 and {EventInventoryGrain.MaxSeatsPerFan}."];
        }

        return errors.Count > 0 ? errors.ToDictionary(e => JsonName(e.Key), e => e.Value) : null;

        static string JsonName(string name) => char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static ProblemHttpResult ReservationRejected(EventDefinition @event, SectionDefinition section, ReservationRequest request, ReservationResult result)
    {
        var (status, title, detail) = result.Outcome switch
        {
            ReservationOutcome.SoldOut => (StatusCodes.Status409Conflict, "Section sold out",
                $"{section.Name} is sold out for {@event.Name}."),
            ReservationOutcome.InsufficientSeats => (StatusCodes.Status409Conflict, "Not enough seats left",
                $"Only {result.SeatsRemaining} {section.Name} seats are left for {@event.Name}, and {request.Quantity} were requested."),
            ReservationOutcome.FanLimitReached => (StatusCodes.Status409Conflict, "Ticket limit reached",
                $"Fans can hold at most {EventInventoryGrain.MaxSeatsPerFan} seats for {@event.Name}. Fan {request.FanId} already holds {result.SeatsHeldByFan}."),
            _ => (StatusCodes.Status429TooManyRequests, "Too many requests",
                $"Fan {request.FanId} is sending requests too quickly. Retry after {result.RetryAfterSeconds} s.")
        };

        return TypedResults.Problem(detail, statusCode: status, title: title,
            type: status == StatusCodes.Status429TooManyRequests ? "https://www.rfc-editor.org/rfc/rfc6585#section-4" : null,
            extensions: result.Outcome == ReservationOutcome.Throttled
                ? new Dictionary<string, object?> { ["outcome"] = BoxOfficeTelemetry.OutcomeName(result.Outcome) }
                : new Dictionary<string, object?>
                {
                    ["outcome"] = BoxOfficeTelemetry.OutcomeName(result.Outcome),
                    ["seatsRemaining"] = result.SeatsRemaining
                });
    }

    private static ProblemHttpResult EventNotFound(string eventId) => TypedResults.Problem(
        title: "Event not found",
        detail: $"No event exists with ID '{eventId}'.",
        statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult ReservationNotFound(EventDefinition @event, string reservationId) => TypedResults.Problem(
        title: "Reservation not found",
        detail: $"No reservation exists with ID '{reservationId}' for {@event.Name}.",
        statusCode: StatusCodes.Status404NotFound);

    private static AvailabilityStatus Status(int available, int capacity) =>
        available == 0 ? AvailabilityStatus.SoldOut
        : available < capacity * 0.2 ? AvailabilityStatus.SellingFast
        : AvailabilityStatus.OnSale;

    private static EventSummary ToSummary(EventDefinition @event, EventInventorySnapshot inventory) => new(
        @event.Id, @event.Performer, @event.Title, @event.Category, @event.Venue, @event.City, @event.StartsAt,
        @event.PriceFrom, inventory.Capacity, inventory.Available, Status(inventory.Available, inventory.Capacity));

    private static EventDetails ToDetails(EventDefinition @event, EventInventorySnapshot inventory) => new(
        @event.Id, @event.Performer, @event.Title, @event.Category, @event.Venue, @event.City, @event.StartsAt,
        inventory.Capacity, inventory.Available, Status(inventory.Available, inventory.Capacity),
        inventory.ReservationCount, inventory.TurnedAway, inventory.Revenue, inventory.SiloAddress,
        [.. @event.Sections.Select(s => inventory.FindSection(s.Id) is { } i
            ? new SectionAvailability(s.Id, s.Name, s.Tier, s.Price, i.Capacity, i.Available, Status(i.Available, i.Capacity))
            : new SectionAvailability(s.Id, s.Name, s.Tier, s.Price, s.Capacity, s.Capacity, AvailabilityStatus.OnSale))]);

    private static ReservationResponse ToResponse(EventDefinition @event, Reservation reservation)
    {
        var section = @event.FindSection(reservation.SectionId)!;
        return new(reservation.Id, @event.Id, @event.Name, section.Id, section.Name, reservation.FanId,
            reservation.Quantity, section.Price, reservation.Total, reservation.ReservedAt);
    }
}
