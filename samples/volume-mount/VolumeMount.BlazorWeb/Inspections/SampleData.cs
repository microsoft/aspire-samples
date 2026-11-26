using System.Globalization;
using System.Security;
using System.Text;
using VolumeMount.Data;

namespace VolumeMount.BlazorWeb.Inspections;

/// <summary>Generates realistic facility-maintenance inspections with nameplate captures and reading logs.</summary>
public sealed class SampleData(InspectionService inspections, TimeProvider timeProvider, ILogger<SampleData> logger)
{
    private sealed record Asset(string Tag, string Name, string Manufacturer, string Model, string Rating, string Reading, string Unit, double Nominal, string Site);

    private static readonly Asset[] s_assets =
    [
        new("AHU-201", "Rooftop air handling unit", "Trane", "Performance CSAA", "12,000 CFM · 460 V 3Ø", "Supply air temp", "°F", 55, "Seattle Data Center 1"),
        new("CH-02", "Centrifugal chiller", "Carrier", "AquaEdge 19DV", "500 tons · R-1233zd(E)", "Leaving chilled water", "°F", 44, "Seattle Data Center 1"),
        new("GEN-01", "Standby diesel generator", "Caterpillar", "C15 ACERT", "500 kW · 480 V", "Coolant temp", "°F", 180, "Seattle Data Center 1"),
        new("FP-114", "Electric fire pump", "Aurora", "Model 481", "1,500 GPM @ 125 psi", "Discharge pressure", "psi", 125, "Portland Assembly Plant"),
        new("BLR-03", "Condensing boiler", "Lochinvar", "Crest FBN2001", "2,000 MBH", "Supply water temp", "°F", 160, "Portland Assembly Plant"),
        new("CMP-07", "Rotary screw air compressor", "Atlas Copco", "GA 37 VSD+", "50 hp · 125 psig", "Line pressure", "psi", 118, "Portland Assembly Plant"),
        new("XFMR-2A", "Pad-mount transformer", "Eaton", "Cooper 3-phase", "1,500 kVA · 13.2 kV/480 V", "Top oil temp", "°C", 62, "Spokane Distribution Center"),
        new("UPS-B", "Data room UPS", "Schneider Electric", "Galaxy VS", "100 kVA · N+1", "Battery runtime", "min", 18, "Seattle Data Center 1"),
        new("ELV-2", "Freight elevator", "Otis", "Gen2 Freight", "4,500 lb capacity", "Door cycle time", "s", 4.2, "Spokane Distribution Center"),
        new("FLT-12", "Electric forklift", "Toyota", "8FBCU25", "5,000 lb · 48 V", "Battery charge", "%", 86, "Boise Cold Storage")
    ];

    private static readonly string[] s_inspectors = ["Maya Chen", "Luis Ortega", "Priya Nair", "Jordan Blake", "Sam Okafor"];

    private static readonly Dictionary<InspectionOutcome, string[]> s_notes = new()
    {
        [InspectionOutcome.Pass] =
        [
            "All readings within spec. Belts, filters, and guards in good condition. Next service due in 90 days.",
            "Visual and operational checks complete. No leaks, abnormal noise, or vibration. Logged readings attached.",
            "Monthly checklist complete. Labels legible, access clear, and lockout points verified."
        ],
        [InspectionOutcome.Attention] =
        [
            "Minor oil seep at the access panel gasket. Replacement scheduled within 30 days; monitor weekly.",
            "Vibration slightly above baseline on the drive end bearing. Lubricated and added to next PM for trending.",
            "Filter differential pressure approaching limit. Spare filters ordered; swap at next visit."
        ],
        [InspectionOutcome.Fail] =
        [
            "Reading out of tolerance under load. Tagged out of service and work order raised with the OEM.",
            "Corrosion on the main terminal lugs with signs of heating. Isolated pending electrician repair."
        ]
    };

    public async Task<SeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var random = Random.Shared;
        var now = timeProvider.GetUtcNow();
        var created = 0;
        var uploaded = 0;

        // Oldest first so the newest inspections get the highest references
        var visits = s_assets
            .Select(asset => (Asset: asset, At: now.AddMinutes(-random.Next(20, 7 * 24 * 60))))
            .OrderBy(v => v.At);

        foreach (var (asset, at) in visits)
        {
            var outcome = random.Next(100) switch { < 62 => InspectionOutcome.Pass, < 88 => InspectionOutcome.Attention, _ => InspectionOutcome.Fail };
            var reading = Math.Round(asset.Nominal * (outcome == InspectionOutcome.Fail ? 0.78 : 1 + (random.NextDouble() - 0.5) * 0.08), 1);
            var form = new InspectionForm
            {
                AssetTag = asset.Tag,
                AssetName = asset.Name,
                Site = asset.Site,
                Inspector = s_inspectors[random.Next(s_inspectors.Length)],
                Outcome = outcome,
                Notes = s_notes[outcome][random.Next(s_notes[outcome].Length)]
            };

            List<AttachmentUpload> files =
            [
                Text($"{asset.Tag.ToLowerInvariant()}-nameplate.svg", "image/svg+xml", NameplateSvg(asset, reading, outcome, at)),
                Text($"{asset.Tag.ToLowerInvariant()}-readings.csv", "text/csv", ReadingsCsv(asset, reading, at, random))
            ];

            var detail = await inspections.CreateAsync(form, files, source: "seed", inspectedAt: at, cancellationToken: cancellationToken);
            created++;
            uploaded += detail.Attachments.Count;
        }

        var total = (await inspections.GetStorageStatusAsync(cancellationToken)).InspectionCount;
        logger.SampleDataSeeded(created, uploaded);
        return new SeedResult(created, uploaded, total);
    }

    private static AttachmentUpload Text(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new AttachmentUpload(fileName, contentType, bytes.Length, () => new MemoryStream(bytes, writable: false));
    }

    private static string ReadingsCsv(Asset asset, double reading, DateTimeOffset at, Random random)
    {
        var csv = new StringBuilder($"timestamp_utc,asset_tag,measurement,value,unit\n");
        for (var i = 11; i >= 0; i--)
        {
            var value = i == 0 ? reading : Math.Round(asset.Nominal * (1 + (random.NextDouble() - 0.5) * 0.06), 1);
            csv.Append(CultureInfo.InvariantCulture, $"{at.AddMinutes(-5 * i):yyyy-MM-ddTHH:mm:ssZ},{asset.Tag},{asset.Reading},{value},{asset.Unit}\n");
        }
        return csv.ToString();
    }

    /// <summary>A stylized capture of the equipment nameplate with the recorded reading.</summary>
    private static string NameplateSvg(Asset asset, double reading, InspectionOutcome outcome, DateTimeOffset at)
    {
        static string E(string s) => SecurityElement.Escape(s);
        var (badge, badgeText) = outcome switch
        {
            InspectionOutcome.Pass => ("#1f9d63", "PASS"),
            InspectionOutcome.Attention => ("#d98a04", "ATTENTION"),
            _ => ("#d13c3c", "FAIL")
        };
        var hue = asset.Tag.Aggregate(0, (h, c) => (h * 31 + c) % 360);

        return string.Create(CultureInfo.InvariantCulture, $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 400" width="640" height="400">
              <defs>
                <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0" stop-color="hsl({hue},28%,30%)"/><stop offset="1" stop-color="hsl({(hue + 40) % 360},32%,14%)"/>
                </linearGradient>
                <linearGradient id="plate" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0" stop-color="#eef1f4"/><stop offset="0.5" stop-color="#c9cfd6"/><stop offset="1" stop-color="#e3e7eb"/>
                </linearGradient>
              </defs>
              <rect width="640" height="400" fill="url(#bg)"/>
              <g opacity="0.18" stroke="#fff">{string.Concat(Enumerable.Range(0, 9).Select(i => $"<line x1=\"{i * 80}\" y1=\"0\" x2=\"{i * 80 - 160}\" y2=\"400\"/>"))}</g>
              <g transform="rotate(-2 320 200)">
                <rect x="70" y="58" width="500" height="284" rx="14" fill="url(#plate)" stroke="#8b949e" stroke-width="3"/>
                <circle cx="92" cy="80" r="7" fill="#9aa3ad"/><circle cx="548" cy="80" r="7" fill="#9aa3ad"/>
                <circle cx="92" cy="320" r="7" fill="#9aa3ad"/><circle cx="548" cy="320" r="7" fill="#9aa3ad"/>
                <text x="110" y="112" font-family="Arial, sans-serif" font-size="30" font-weight="700" fill="#1f2933">{E(asset.Manufacturer.ToUpperInvariant())}</text>
                <text x="110" y="142" font-family="Arial, sans-serif" font-size="18" fill="#3e4c59">{E(asset.Name)} · {E(asset.Model)}</text>
                <line x1="110" y1="158" x2="530" y2="158" stroke="#7b8794" stroke-width="2"/>
                <text x="110" y="192" font-family="Courier New, monospace" font-size="20" fill="#1f2933">ASSET  {E(asset.Tag)}</text>
                <text x="110" y="222" font-family="Courier New, monospace" font-size="20" fill="#1f2933">RATING {E(asset.Rating)}</text>
                <text x="110" y="252" font-family="Courier New, monospace" font-size="20" fill="#1f2933">S/N    {asset.Tag.Aggregate(7, (h, c) => (h * 131 + c) % 900000) + 100000:D6}-{at:yy}</text>
                <rect x="110" y="272" width="300" height="44" rx="8" fill="#1f2933"/>
                <text x="124" y="301" font-family="Arial, sans-serif" font-size="18" fill="#f5f7fa">{E(asset.Reading)}: {reading} {E(asset.Unit)}</text>
                <rect x="424" y="272" width="106" height="44" rx="8" fill="{badge}"/>
                <text x="477" y="301" text-anchor="middle" font-family="Arial, sans-serif" font-size="16" font-weight="700" fill="#fff">{badgeText}</text>
              </g>
              <text x="20" y="384" font-family="Arial, sans-serif" font-size="14" fill="#ffffffb3">{E(asset.Site)} · {at:yyyy-MM-dd HH:mm} UTC</text>
            </svg>
            """);
    }
}
