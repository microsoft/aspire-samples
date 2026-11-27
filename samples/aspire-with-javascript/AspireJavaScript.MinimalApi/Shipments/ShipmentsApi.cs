using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AspireJavaScript.MinimalApi.Shipments;

public static partial class ShipmentsApi
{
    public static RouteGroupBuilder MapShipmentsApi(this IEndpointRouteBuilder routes)
    {
        var logger = routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ShipmentsApi).FullName!);

        var group = routes.MapGroup("/api/shipments")
            .WithTags("Shipments");

        group.MapGet("/", Results<Ok<ShipmentBoard>, ValidationProblem> (
                [Description("Only return shipments with this status. Omit it to return the whole board.")] string? status,
                ShipmentTracker tracker) =>
            {
                ShipmentStatus? filter = null;
                if (!string.IsNullOrEmpty(status))
                {
                    if (!ShipmentStatusNames.TryParse(status, out var parsed))
                    {
                        logger.RequestRejected(nameof(status), status);
                        return TypedResults.ValidationProblem(
                            new Dictionary<string, string[]> { [nameof(status)] = [$"'{status}' isn't a shipment status. Use one of: {string.Join(", ", ShipmentStatusNames.All)}."] },
                            title: "Invalid status filter");
                    }

                    filter = parsed;
                }

                return TypedResults.Ok(tracker.GetBoard(filter));
            })
            .WithName("ListShipments")
            .WithSummary("List shipments on the tracking board")
            .WithDescription("""
                Returns the shipments on the Contoso tracking board, with exceptions first and then by estimated delivery.
                `statusCounts` always covers the whole board so filter chips can show their counts while a filter is applied.
                """);

        group.MapGet("/{id}", async Task<Results<Ok<ShipmentDetail>, ProblemHttpResult, ValidationProblem>> (
                [Description("Tracking number in the form `CTS-########`.")] string id,
                ShipmentTracker tracker,
                CancellationToken cancellationToken) =>
            {
                if (!TrackingNumber().IsMatch(id))
                {
                    logger.RequestRejected(nameof(id), id);
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { [nameof(id)] = ["Tracking numbers look like CTS-48214101."] },
                        title: "Invalid tracking number");
                }

                if (await tracker.TrackAsync(id.ToUpperInvariant(), cancellationToken) is not { } detail)
                {
                    return TypedResults.Problem(
                        title: "Shipment not found",
                        detail: $"No shipment with tracking number {id.ToUpperInvariant()} is on the board.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                return TypedResults.Ok(detail);
            })
            .WithName("TrackShipment")
            .WithSummary("Track a shipment")
            .WithDescription("""
                Asks the carrier for the latest scan and returns the shipment with its full scan history, newest first.
                Returns a `404` problem details response for an unknown tracking number and a `400` for a malformed one.
                """)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    [GeneratedRegex("^CTS-[0-9]{8}$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingNumber();
}
