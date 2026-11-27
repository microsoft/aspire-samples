using System.ComponentModel;

namespace AspireJavaScript.MinimalApi.Shipments;

/// <summary>Where a shipment is in its journey. Serialized in kebab case, for example <c>out-for-delivery</c>.</summary>
public enum ShipmentStatus
{
    LabelCreated,
    InTransit,
    OutForDelivery,
    Delivered,
    Exception
}

[Description("A scan recorded by the carrier as the parcel moves through its network.")]
public record ScanEvent(
    [property: Description("When the parcel was scanned (UTC).")] DateTimeOffset At,
    [property: Description("Where the parcel was scanned.")] string Location,
    [property: Description("What happened at the scan.")] string Description);

[Description("A shipment on the Contoso tracking board.")]
public record Shipment(
    [property: Description("Contoso tracking number in the form `CTS-########`.")] string Id,
    [property: Description("Where the parcel was picked up.")] string Origin,
    [property: Description("Where the parcel is being delivered.")] string Destination,
    [property: Description("The carrier moving the parcel.")] string Carrier,
    [property: Description("Where the shipment is in its journey.")] ShipmentStatus Status,
    [property: Description("Estimated delivery time (UTC), or the delivery time once delivered.")] DateTimeOffset Eta,
    [property: Description("Parcel weight in kilograms.")] decimal WeightKg,
    [property: Description("How much of the route is complete, from 0 (label created) to 100 (delivered).")] int Progress,
    [property: Description("The carrier's most recent scan.")] ScanEvent LastScan);

[Description("A shipment and its full scan history.")]
public record ShipmentDetail(
    [property: Description("The shipment.")] Shipment Shipment,
    [property: Description("Every scan for the shipment, newest first.")] IReadOnlyList<ScanEvent> History);

[Description("The number of shipments on the board with a status.")]
public record StatusCount(
    [property: Description("The status.")] ShipmentStatus Status,
    [property: Description("Shipments with this status.")] int Count);

[Description("The shipment tracking board, optionally filtered by status.")]
public record ShipmentBoard(
    [property: Description("The status filter that was applied, or null for every shipment.")] ShipmentStatus? Status,
    [property: Description("Total shipments on the board, regardless of the filter.")] int Total,
    [property: Description("Shipments per status across the whole board, for filter chips.")] IReadOnlyList<StatusCount> StatusCounts,
    [property: Description("The matching shipments, exceptions first, then by estimated delivery.")] IReadOnlyList<Shipment> Shipments);

/// <summary>The result of the "Simulate tracking lookups" dashboard command.</summary>
public record SimulationSummary(int BoardQueries, int Lookups, int NotFound, int ExceptionsObserved, double DurationMs);

public static class ShipmentStatusNames
{
    public static readonly IReadOnlyList<string> All = Enum.GetValues<ShipmentStatus>().Select(ToKebabCase).ToArray();

    public static string ToKebabCase(this ShipmentStatus status) => status switch
    {
        ShipmentStatus.LabelCreated => "label-created",
        ShipmentStatus.InTransit => "in-transit",
        ShipmentStatus.OutForDelivery => "out-for-delivery",
        ShipmentStatus.Delivered => "delivered",
        _ => "exception"
    };

    public static bool TryParse(string? value, out ShipmentStatus status)
    {
        foreach (var candidate in Enum.GetValues<ShipmentStatus>())
        {
            if (string.Equals(candidate.ToKebabCase(), value, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }
}
