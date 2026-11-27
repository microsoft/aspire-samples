# Aspire Orleans sample app

![Grain Tickets box office in the middle of an on-sale rush: VIP Pit and Floor GA are stamped sold out, the other sections are draining, the live sales feed lists reservations and turned-away fans, and a 2 × Lower Bowl reservation is confirmed (light theme)](./images/orleans-voting-app-light.png#gh-light-mode-only)
![Grain Tickets box office in the middle of an on-sale rush: VIP Pit and Floor GA are stamped sold out, the other sections are draining, the live sales feed lists reservations and turned-away fans, and a 2 × Lower Bowl reservation is confirmed (dark theme)](./images/orleans-voting-app-dark.png#gh-dark-mode-only)

This is a .NET app that shows how to use [Orleans](https://learn.microsoft.com/dotnet/orleans) with Aspire orchestration.

**Grain Tickets** is an event box office. Each event's seat inventory lives in its own Orleans grain, which handles one reservation at a time, so no seat is ever sold twice, even when hundreds of fans rush the same event at once across a three-silo cluster. Seat availability, the live sales feed, and the number of fans turned away update in real time in every open browser. The UI supports both light and dark themes.

The sample also shows what Aspire gives you while you build: the box office API is documented with OpenAPI and Scalar and linked from the dashboard, the app emits domain traces, metrics, and structured logs that follow each reservation from silo to silo, and dashboard commands simulate an on-sale rush or reset the events, so the telemetry pages fill up in seconds.

> The sample started out as a voting app, and its folder and project names still say *voting* so that existing links keep working.

## Demonstrates

- How to use Aspire to work with Orleans: a three-silo Orleans cluster that uses Redis for clustering and grain storage, hosted by three replicas of an ASP.NET Core app
- A grain as the consistency boundary for a contended resource (an event's seats), grain-to-grain calls across silos, and grain observers that push live updates to a Blazor UI
- Distributed tracing of Orleans grain calls and storage, plus domain spans, metrics, and `[LoggerMessage]` structured logs
- An OpenAPI document with a Scalar API reference, linked from the dashboard
- Custom dashboard commands with `WithHttpCommand`

## Sample prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- This sample is written in C# and targets .NET 10. It requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

## Running the sample

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `OrleansVoting.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `OrleansVoting.slnx` and launch/debug the `OrleansVoting.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `OrleansVoting.AppHost` directory.

1. On the **Resources** page, click the **Box office** link of any `boxoffice` replica.
2. In the box office:
    1. Pick an event. Each card shows the event's live availability, read from its inventory grain.
    2. Select a section, choose the number of seats, and click **Reserve**. A fan can hold at most 8 seats per event.
    3. Click **Simulate on-sale rush** to send 400 simulated fans at the event and watch sections sell out in real time. Open the same event in a second browser window to see both windows update together.
    4. Click **Reset event** to cancel every reservation and put the seats back on sale.

![Grain Tickets events page listing four events, each with a live availability bar: The Midnight Arcade selling fast, Priya Raman sold out, and the other two partly sold (light theme)](./images/orleans-voting-events-light.png#gh-light-mode-only)
![Grain Tickets events page listing four events, each with a live availability bar: The Midnight Arcade selling fast, Priya Raman sold out, and the other two partly sold (dark theme)](./images/orleans-voting-events-dark.png#gh-dark-mode-only)

For more information about using Orleans, see the [Orleans documentation](https://learn.microsoft.com/dotnet/orleans).

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with Redis and three running boxoffice replicas, and the actions menu of a replica open on the Simulate on-sale rush command and its description (light theme)](./images/orleans-voting-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with Redis and three running boxoffice replicas, and the actions menu of a replica open on the Simulate on-sale rush command and its description (dark theme)](./images/orleans-voting-dashboard-resources-dark.png#gh-dark-mode-only)

- **boxoffice** runs as three replicas. Each replica is an ASP.NET Core app (Blazor UI and HTTP API) and an Orleans silo, so the three form one cluster. **redis** holds the cluster membership table and each event's persisted seat inventory.
- Each replica links to the **Box office** UI, the **API reference (Scalar)**, and the raw **OpenAPI JSON**. The raw endpoint URLs, including the Orleans silo and gateway ports, are listed on the resource details page.
- Run **Simulate on-sale rush** on any replica. It sends 400 simulated fans at The Midnight Arcade's on-sale within 12 seconds, and the command result is a JSON summary such as `{"eventId":"midnight-arcade-seattle","fans":400,"reservations":366,"seatsReserved":955,"revenue":100625,"fansTurnedAway":34,"soldOutRejections":99,"insufficientSeatRejections":2,"fanLimitRejections":8,"failed":0,"seatsRemaining":185,"elapsedSeconds":12.6}`. The endpoint behind it, `POST /dev/simulate-rush`, is only mapped in development and also accepts `eventId` and `fans` query parameters.
- Run **Reset events** to cancel every reservation and put all seats back on sale. It asks for confirmation first and calls the development-only `POST /dev/reset` endpoint.

### API reference

![Scalar API reference for the Grain Tickets Box Office API showing the Reserve seats operation with its request body schema and limits, the 201, 400, 404, 409 and 429 responses, and an example reservation (light theme)](./images/orleans-voting-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Grain Tickets Box Office API showing the Reserve seats operation with its request body schema and limits, the 201, 400, 404, 409 and 429 responses, and an example reservation (dark theme)](./images/orleans-voting-scalar-dark.png#gh-dark-mode-only)

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/events` | Events on sale, with live availability |
| `GET` | `/api/events/{eventId}` | An event with live availability for each section, plus its reservations, turned-away attempts, revenue, and the silo that hosts its inventory grain |
| `POST` | `/api/events/{eventId}/reservations` | Reserve seats in one section (`fanId`, `sectionId`, and `quantity` from 1 to 8) |
| `GET` | `/api/events/{eventId}/reservations/{reservationId}` | A reservation |
| `DELETE` | `/api/events/{eventId}/reservations/{reservationId}` | Cancel a reservation and release its seats |

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details: `400` for an invalid request, `404` for an unknown event or reservation, `409` when a section is sold out, has too few seats left, or the fan would hold more than 8 seats for the event (the `outcome` and `seatsRemaining` extensions say which), and `429` with a `Retry-After` header when one fan sends requests too quickly. `OrleansVoting.Service/OrleansVoting.Service.http` has a request for each case.

### Traces

![Aspire dashboard trace of a simulated fan whose first choice sold out: the retry crosses from one boxoffice replica to the replica hosting the event's inventory grain, waits behind other fans, and persists the sale to Redis, with the allocate seats span selected to show its attributes and events (light theme)](./images/orleans-voting-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of a simulated fan whose first choice sold out: the retry crosses from one boxoffice replica to the replica hosting the event's inventory grain, waits behind other fans, and persists the sale to Redis, with the allocate seats span selected to show its attributes and events (dark theme)](./images/orleans-voting-trace-dark.png#gh-dark-mode-only)

- **simulated fan**: each simulated fan gets its own trace, linked to the request that started the rush. Orleans traces every grain call, so the trace follows the reservation from the replica that received it, through the fan's grain (`IFanGrain/Reserve`, which throttles bursts), to the event's grain (`IEventInventoryGrain/Reserve`) on whichever silo hosts it. A fan whose section sold out reads the event's availability (`IEventInventoryGrain/GetSnapshot`) and tries another section.
- **Contention**: during a rush, compare the client and server `IEventInventoryGrain/Reserve` spans. The gap between them is the time the call waited behind other fans, because the grain handles one reservation at a time.
- **allocate seats**: the inventory grain's span carries `orleansticketing.event.id`, `orleansticketing.section.id`, `orleansticketing.seat.tier`, `orleansticketing.fan.id`, `orleansticketing.reservation.quantity`, `orleansticketing.reservation.outcome`, and, when it succeeds, `orleansticketing.reservation.id`. It records `inventory.checked` (seats available and seats the fan already holds), `seats.allocated`, and `section.sold_out` events, and its children are the Orleans `write storage` span and the Redis `EVAL` that persists the sale.
- **Live updates**: `IEventWatcher/OnInventoryChanged` spans show the grain pushing the new availability to every open box office page, on any silo.
- **reserve seats**: the span around each reservation in the app, with the outcome, the seats remaining, and the total.

### Metrics

![Aspire dashboard metrics page charting orleansticketing.boxoffice.reservation.duration P50, P90 and P99 with exemplars, spiking during each simulated on-sale rush (light theme)](./images/orleans-voting-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting orleansticketing.boxoffice.reservation.duration P50, P90 and P99 with exemplars, spiking during each simulated on-sale rush (dark theme)](./images/orleans-voting-metrics-dark.png#gh-dark-mode-only)

The `OrleansTicketing.BoxOffice` meter has these instruments:

| Instrument | Description |
| --- | --- |
| `orleansticketing.boxoffice.reservations` | Reservation attempts, by outcome (`confirmed`, `sold_out`, `insufficient_seats`, `fan_limit_reached`, `throttled`) and seat tier |
| `orleansticketing.boxoffice.seats.reserved` | Seats reserved, by seat tier |
| `orleansticketing.boxoffice.reservation.duration` | Histogram of the time to get an answer from the event's inventory grain, in seconds, including time queued behind other fans |
| `orleansticketing.boxoffice.reservations.in_flight` | Up-down counter of reservations waiting for an inventory grain, which spikes when fans contend for the same event |
| `orleansticketing.boxoffice.seats.available` | Observable gauge of the seats still available, by seat tier, across the events whose inventory grains are active in the silo |

Metric tags are limited to the outcome and the seat tier. Event, fan, and reservation IDs only appear on spans and logs. The duration histogram records inside the `reserve seats` span, so its exemplars link to the trace that produced each point. The `Microsoft.Orleans` meter adds the cluster's own metrics, such as grain activations and messaging.

### Structured logs

![Aspire dashboard structured logs filtered to confirmed reservations, with a ReservationConfirmed entry open to show its ReservationId, FanId, SectionName, EventName, OrderTotal and SeatsRemaining properties and its trace link (light theme)](./images/orleans-voting-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs filtered to confirmed reservations, with a ReservationConfirmed entry open to show its ReservationId, FanId, SectionName, EventName, OrderTotal and SeatsRemaining properties and its trace link (dark theme)](./images/orleans-voting-logs-dark.png#gh-dark-mode-only)

The box office logs its key events with source-generated `[LoggerMessage]` methods (`OrleansVoting.Service/BoxOffice/Log.cs`): confirmed, rejected, and cancelled reservations, sections and events that sell out, inventory resets, and on-sale rush summaries. Values such as `ReservationId`, `FanId`, `SectionName`, `EventName`, `ReservationOutcome`, `SeatsRemaining`, and `OrderTotal` arrive as properties you can filter on, and each entry links to the trace that produced it.
