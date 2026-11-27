using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OrleansVoting.Api;

public static class BoxOfficeOpenApi
{
    public const string Title = "Grain Tickets Box Office API";

    public static IServiceCollection AddBoxOfficeOpenApi(this IServiceCollection services) => services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = Title,
                Version = "v1",
                Description = """
                    Browse events on sale and reserve seats. Each event's seat inventory lives in its own
                    [Orleans](https://learn.microsoft.com/dotnet/orleans) grain, which handles one reservation at a time,
                    so seats are never double-sold even when thousands of fans rush the same event.
                    Errors are returned as [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details.
                    """
            };
            document.Tags = new HashSet<OpenApiTag>
            {
                new() { Name = "Events", Description = "Events on sale, with live availability for every section." },
                new() { Name = "Reservations", Description = "Reserve, look up, and cancel seats. A fan can hold at most 8 seats per event." }
            };
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;

            if (type == typeof(ReservationRequest))
            {
                schema.Examples = [new JsonObject { ["fanId"] = "fan-7f3a91", ["sectionId"] = "floor", ["quantity"] = 2 }];
            }
            else if (type == typeof(ReservationResponse))
            {
                schema.Examples = [new JsonObject
                {
                    ["id"] = "GT-4C1E8B",
                    ["eventId"] = "midnight-arcade-seattle",
                    ["eventName"] = "The Midnight Arcade: Neon Skyline Tour",
                    ["sectionId"] = "floor",
                    ["sectionName"] = "Floor GA",
                    ["fanId"] = "fan-7f3a91",
                    ["quantity"] = 2,
                    ["unitPrice"] = 139.00m,
                    ["total"] = 278.00m,
                    ["reservedAt"] = "2026-11-27T17:00:03.412Z"
                }];
            }
            else if (type == typeof(EventDetails))
            {
                schema.Examples = [new JsonObject
                {
                    ["id"] = "midnight-arcade-seattle",
                    ["performer"] = "The Midnight Arcade",
                    ["title"] = "Neon Skyline Tour",
                    ["category"] = "Concert",
                    ["venue"] = "Harborview Arena",
                    ["city"] = "Seattle, WA",
                    ["startsAt"] = "2027-03-12T20:00:00-08:00",
                    ["capacity"] = 1140,
                    ["seatsAvailable"] = 212,
                    ["status"] = "selling_fast",
                    ["reservations"] = 371,
                    ["turnedAway"] = 64,
                    ["revenue"] = 106432.00m,
                    ["inventorySilo"] = "S127.0.0.1:51438:122220384",
                    ["sections"] = new JsonArray(
                        Section("vip-pit", "VIP Pit", "vip", 249.00m, 60, 0, "sold_out"),
                        Section("floor", "Floor GA", "floor", 139.00m, 240, 0, "sold_out"),
                        Section("lower-bowl", "Lower Bowl", "lower", 99.00m, 360, 18, "selling_fast"),
                        Section("upper-bowl", "Upper Bowl", "upper", 59.00m, 480, 194, "on_sale"))
                }];
            }

            return Task.CompletedTask;
        });
    });

    private static JsonObject Section(string id, string name, string tier, decimal price, int capacity, int available, string status) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["tier"] = tier,
        ["price"] = price,
        ["capacity"] = capacity,
        ["seatsAvailable"] = available,
        ["status"] = status
    };
}
