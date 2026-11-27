using System.ComponentModel;

namespace OrleansVoting.BoxOffice;

/// <summary>
/// A seat tier. The tier is the only seat attribute used as a metric tag, which keeps metric cardinality bounded.
/// </summary>
[Description("The seat tier: `vip`, `floor`, `lower` level, or `upper` level.")]
public enum SeatTier
{
    Vip,
    Floor,
    Lower,
    Upper
}

public sealed record SectionDefinition(string Id, string Name, SeatTier Tier, decimal Price, int Capacity);

public sealed record EventDefinition(
    string Id,
    string Performer,
    string Title,
    string Category,
    string Venue,
    string City,
    DateTimeOffset StartsAt,
    IReadOnlyList<SectionDefinition> Sections)
{
    public string Name => $"{Performer}: {Title}";

    public int Capacity => Sections.Sum(s => s.Capacity);

    public decimal PriceFrom => Sections.Min(s => s.Price);

    public SectionDefinition? FindSection(string sectionId) =>
        Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The events on sale. A real box office would load these from a database; the sample keeps them in code
/// so the inventory grains only have to persist what they sell.
/// </summary>
public static class EventCatalog
{
    /// <summary>The headline event that the on-sale rush targets by default.</summary>
    public const string HeadlineEventId = "midnight-arcade-seattle";

    public static IReadOnlyList<EventDefinition> Events { get; } =
    [
        new("midnight-arcade-seattle", "The Midnight Arcade", "Neon Skyline Tour", "Concert",
            "Harborview Arena", "Seattle, WA", new(2027, 3, 12, 20, 0, 0, TimeSpan.FromHours(-8)),
            [
                new("vip-pit", "VIP Pit", SeatTier.Vip, 249m, 60),
                new("floor", "Floor GA", SeatTier.Floor, 139m, 240),
                new("lower-bowl", "Lower Bowl", SeatTier.Lower, 99m, 360),
                new("upper-bowl", "Upper Bowl", SeatTier.Upper, 59m, 480)
            ]),
        new("aurora-strings-chicago", "Aurora Strings Orchestra", "Symphony of Lights", "Classical",
            "Lakeshore Hall", "Chicago, IL", new(2027, 4, 3, 19, 30, 0, TimeSpan.FromHours(-5)),
            [
                new("box-seats", "Box Seats", SeatTier.Vip, 189m, 40),
                new("orchestra", "Orchestra", SeatTier.Floor, 119m, 300),
                new("mezzanine", "Mezzanine", SeatTier.Lower, 79m, 260),
                new("balcony", "Balcony", SeatTier.Upper, 45m, 320)
            ]),
        new("grid-legends-austin", "Grid Legends", "Championship Finals", "Esports",
            "Riverside Dome", "Austin, TX", new(2027, 5, 22, 18, 0, 0, TimeSpan.FromHours(-5)),
            [
                new("courtside", "Courtside", SeatTier.Vip, 299m, 48),
                new("floor", "Floor", SeatTier.Floor, 149m, 200),
                new("lower-level", "Lower Level", SeatTier.Lower, 89m, 420),
                new("upper-level", "Upper Level", SeatTier.Upper, 49m, 600)
            ]),
        new("priya-raman-brooklyn", "Priya Raman", "Late Night Laughs", "Comedy",
            "The Velvet Room", "Brooklyn, NY", new(2027, 2, 19, 21, 0, 0, TimeSpan.FromHours(-5)),
            [
                new("front-tables", "Front Tables", SeatTier.Vip, 95m, 24),
                new("main-room", "Main Room", SeatTier.Floor, 55m, 140),
                new("balcony", "Balcony", SeatTier.Upper, 35m, 60)
            ])
    ];

    public static EventDefinition? Find(string eventId) =>
        Events.FirstOrDefault(e => string.Equals(e.Id, eventId, StringComparison.OrdinalIgnoreCase));
}
