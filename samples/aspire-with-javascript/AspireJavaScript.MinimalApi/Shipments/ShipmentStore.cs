namespace AspireJavaScript.MinimalApi.Shipments;

/// <summary>
/// An in-memory tracking board of realistic shipments. Scan times are generated relative to when the API starts,
/// so ETAs and "last scanned" times always look current.
/// </summary>
public sealed class ShipmentStore
{
    private static readonly Seed[] Seeds =
    [
        new("CTS-48213904", "Wingtip Ground", ShipmentStatus.Delivered, 3.4m, ["Seattle, WA", "Tacoma, WA", "Portland, OR"], HoursSinceScan: 19, Note: "left at front porch"),
        new("CTS-48213922", "Northwind Express", ShipmentStatus.InTransit, 8.6m, ["Chicago, IL", "Toledo, OH", "Buffalo, NY", "Albany, NY", "Boston, MA"], Scans: 7, HoursSinceScan: 2, EtaHours: 26),
        new("CTS-48213957", "Fabrikam Freight", ShipmentStatus.OutForDelivery, 12.1m, ["Austin, TX", "Dallas, TX", "Amarillo, TX", "Denver, CO"], HoursSinceScan: 1.5, EtaHours: 4),
        new("CTS-48213988", "Tailspin Air Cargo", ShipmentStatus.Delivered, 1.2m, ["Miami, FL", "Atlanta, GA"], HoursSinceScan: 30, Note: "signed by J. Okafor"),
        new("CTS-48214016", "Wingtip Ground", ShipmentStatus.InTransit, 5.8m, ["San Francisco, CA", "Fresno, CA", "Los Angeles, CA", "Phoenix, AZ"], Scans: 5, HoursSinceScan: 3, EtaHours: 18),
        new("CTS-48214043", "Northwind Express", ShipmentStatus.LabelCreated, 2.3m, ["Minneapolis, MN", "Des Moines, IA", "Kansas City, MO"], HoursSinceScan: 4, EtaHours: 52),
        new("CTS-48214077", "Fabrikam Freight", ShipmentStatus.InTransit, 18.4m, ["Vancouver, BC", "Kamloops, BC", "Calgary, AB"], Scans: 4, HoursSinceScan: 5, EtaHours: 20),
        new("CTS-48214101", "Tailspin Air Cargo", ShipmentStatus.Exception, 22.0m, ["Newark, NJ", "Buffalo, NY", "Toronto, ON"], Scans: 4, HoursSinceScan: 6, EtaHours: 40, Note: "Held by customs, commercial invoice required"),
        new("CTS-48214128", "Wingtip Ground", ShipmentStatus.OutForDelivery, 4.5m, ["Nashville, TN", "Knoxville, TN", "Charlotte, NC"], HoursSinceScan: 2, EtaHours: 3),
        new("CTS-48214156", "Northwind Express", ShipmentStatus.Delivered, 0.8m, ["Los Angeles, CA", "Barstow, CA", "Las Vegas, NV"], HoursSinceScan: 9, Note: "left with front desk"),
        new("CTS-48214183", "Fabrikam Freight", ShipmentStatus.InTransit, 9.9m, ["Portland, ME", "Boston, MA", "Hartford, CT", "Philadelphia, PA"], Scans: 5, HoursSinceScan: 1, EtaHours: 22),
        new("CTS-48214210", "Wingtip Ground", ShipmentStatus.LabelCreated, 6.7m, ["Salt Lake City, UT", "Twin Falls, ID", "Boise, ID"], HoursSinceScan: 7, EtaHours: 48),
        new("CTS-48214239", "Tailspin Air Cargo", ShipmentStatus.OutForDelivery, 3.1m, ["Houston, TX", "New Orleans, LA"], HoursSinceScan: 1, EtaHours: 5),
        new("CTS-48214265", "Northwind Express", ShipmentStatus.Exception, 7.3m, ["Detroit, MI", "Toledo, OH", "Columbus, OH"], Scans: 7, HoursSinceScan: 2, EtaHours: 20, Note: "Delivery attempted, business closed; will retry next business day"),
        new("CTS-48214292", "Fabrikam Freight", ShipmentStatus.Delivered, 14.6m, ["Raleigh, NC", "Richmond, VA", "Washington, DC"], HoursSinceScan: 26, Note: "signed by M. Chen"),
        new("CTS-48214318", "Wingtip Ground", ShipmentStatus.InTransit, 2.9m, ["San Diego, CA", "Los Angeles, CA", "Fresno, CA", "Sacramento, CA"], Scans: 6, HoursSinceScan: 4, EtaHours: 15),
        new("CTS-48214346", "Northwind Express", ShipmentStatus.OutForDelivery, 5.2m, ["Montréal, QC", "Ottawa, ON"], HoursSinceScan: 0.5, EtaHours: 6),
        new("CTS-48214387", "Wingtip Ground", ShipmentStatus.Exception, 15.2m, ["Denver, CO", "Cheyenne, WY", "Rock Springs, WY", "Salt Lake City, UT"], Scans: 4, HoursSinceScan: 8, EtaHours: 34, Note: "Weather delay, I-80 closed at the Wyoming border"),
        new("CTS-48214412", "Fabrikam Freight", ShipmentStatus.LabelCreated, 11.0m, ["St. Louis, MO", "Indianapolis, IN"], HoursSinceScan: 2, EtaHours: 30),
        new("CTS-48214450", "Tailspin Air Cargo", ShipmentStatus.InTransit, 4.2m, ["Atlanta, GA", "Jacksonville, FL", "Orlando, FL"], Scans: 4, HoursSinceScan: 3, EtaHours: 9)
    ];

    private readonly Dictionary<string, ShipmentDetail> _shipments;
    private readonly IReadOnlyList<Shipment> _board;

    public ShipmentStore(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        _shipments = Seeds.Select(seed => Build(seed, now)).ToDictionary(d => d.Shipment.Id, StringComparer.OrdinalIgnoreCase);

        // Shipments that need attention first, then by when they're due (most recently delivered first)
        _board = _shipments.Values
            .Select(d => d.Shipment)
            .OrderBy(s => s.Status switch
            {
                ShipmentStatus.Exception => 0,
                ShipmentStatus.OutForDelivery => 1,
                ShipmentStatus.InTransit => 2,
                ShipmentStatus.LabelCreated => 3,
                _ => 4
            })
            .ThenBy(s => s.Status == ShipmentStatus.Delivered ? -s.Eta.UtcTicks : s.Eta.UtcTicks)
            .ToArray();
    }

    public int Count => _board.Count;

    public IReadOnlyList<string> TrackingNumbers => _board.Select(s => s.Id).ToArray();

    public IReadOnlyList<Shipment> Query(ShipmentStatus? status) =>
        status is { } filter ? _board.Where(s => s.Status == filter).ToArray() : _board;

    public IReadOnlyList<StatusCount> StatusCounts() =>
        Enum.GetValues<ShipmentStatus>().Select(status => new StatusCount(status, CountByStatus(status))).ToArray();

    public int CountByStatus(ShipmentStatus status) => _board.Count(s => s.Status == status);

    public ShipmentDetail? Find(string id) => _shipments.GetValueOrDefault(id);

    private static ShipmentDetail Build(Seed seed, DateTimeOffset now)
    {
        string origin = seed.Route[0], destination = seed.Route[^1];

        // The full journey the carrier will scan: label, pickup, origin, every hub, delivery station, out for delivery, delivered
        List<(string Location, string Description, double HoursBefore)> journey =
        [
            (origin, "Shipping label created", 0),
            (origin, $"Picked up by {seed.Carrier}", 5),
            (origin, "Departed origin facility", 1.5)
        ];
        foreach (var hub in seed.Route[1..^1])
        {
            journey.Add((hub, "Arrived at sort facility", 6.5));
            journey.Add((hub, "Departed sort facility", 2));
        }
        journey.Add((destination, "Arrived at local delivery station", 5));
        journey.Add((destination, "Out for delivery", 2.5));
        journey.Add((destination, $"Delivered, {seed.Note}", 3));

        var completed = seed.Status switch
        {
            ShipmentStatus.LabelCreated => 1,
            ShipmentStatus.OutForDelivery => journey.Count - 1,
            ShipmentStatus.Delivered => journey.Count,
            _ => seed.Scans
        };

        var steps = journey.Take(completed).ToList();
        if (seed.Status == ShipmentStatus.Exception)
        {
            steps.Add((steps[^1].Location, seed.Note!, 1.5));
        }

        // Walk back from the most recent scan so every scan time is relative to now
        var history = new ScanEvent[steps.Count];
        var at = now - Hours(seed.HoursSinceScan);
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            history[steps.Count - 1 - i] = new ScanEvent(at, steps[i].Location, steps[i].Description);
            at -= Hours(steps[i].HoursBefore + (i % 3) * 0.4);
        }

        var progress = seed.Status switch
        {
            ShipmentStatus.LabelCreated => 4,
            ShipmentStatus.Delivered => 100,
            _ => (int)Math.Round(100.0 * (completed - 1) / (journey.Count - 1))
        };

        var lastScan = history[0];
        var shipment = new Shipment(
            seed.Id, origin, destination, seed.Carrier, seed.Status,
            seed.Status == ShipmentStatus.Delivered ? lastScan.At : now + Hours(seed.EtaHours),
            seed.WeightKg, progress, lastScan);

        return new ShipmentDetail(shipment, history);
    }

    private static TimeSpan Hours(double hours) => TimeSpan.FromMinutes(Math.Round(hours * 60));

    private sealed record Seed(
        string Id, string Carrier, ShipmentStatus Status, decimal WeightKg, string[] Route,
        int Scans = 0, double HoursSinceScan = 0, double EtaHours = 0, string? Note = null);
}
