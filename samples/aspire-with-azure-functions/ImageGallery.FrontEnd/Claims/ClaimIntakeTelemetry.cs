using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ImageGallery.FrontEnd.Claims;

// Domain telemetry for claim photo intake. The "Contoso.*" wildcard in ServiceDefaults exports it.
public sealed class ClaimIntakeTelemetry
{
    public const string Name = "Contoso.ClaimIntake";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _uploaded;
    private readonly Histogram<long> _uploadSize;
    private readonly Counter<long> _rejected;
    private readonly UpDownCounter<long> _backlog;
    private readonly Histogram<double> _turnaround;
    private readonly Counter<long> _retriggered;

    public ClaimIntakeTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _uploaded = meter.CreateCounter<long>(
            "claims.photos.uploaded", unit: "{photo}",
            description: "Claim photos stored in the claim-photos blob container.");

        _uploadSize = meter.CreateHistogram<long>(
            "claims.photo.upload.size", unit: "By",
            description: "Size of accepted claim photo uploads.",
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = [65_536, 131_072, 262_144, 524_288, 1_048_576, 2_097_152, 4_194_304, 10_485_760] });

        _rejected = meter.CreateCounter<long>(
            "claims.photos.rejected", unit: "{photo}",
            description: "Uploads rejected before reaching Blob Storage.");

        _backlog = meter.CreateUpDownCounter<long>(
            "claims.photos.backlog", unit: "{photo}",
            description: "Uploaded claim photos still waiting for the Azure Function's result.");

        _turnaround = meter.CreateHistogram<double>(
            "claims.photo.turnaround", unit: "s",
            description: "Time from upload until the function's result reached the front end, including the blob trigger's pickup delay.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 3, 5, 8, 13, 21, 34, 55] });

        _retriggered = meter.CreateCounter<long>(
            "claims.photos.retriggered", unit: "{photo}",
            description: "Photos the blob trigger hadn't picked up after 30 seconds, touched so the next container scan finds them.");
    }

    // Tags are bounded: source is browser/api/sample, format is jpeg/png/webp, reason and quality are fixed sets.
    public void PhotoUploaded(string source, string format, long sizeBytes)
    {
        var tags = new TagList { { "claims.photo.source", source }, { "claims.photo.format", format } };
        _uploaded.Add(1, tags);
        _uploadSize.Record(sizeBytes, tags);
    }

    public void UploadRejected(string reason) =>
        _rejected.Add(1, new KeyValuePair<string, object?>("claims.rejection.reason", reason));

    public void BacklogChanged(long delta)
    {
        if (delta != 0)
        {
            _backlog.Add(delta);
        }
    }

    public void ResultReceived(string quality, TimeSpan turnaround) =>
        _turnaround.Record(turnaround.TotalSeconds, new KeyValuePair<string, object?>("claims.photo.quality", quality));

    public void PhotoRetriggered() => _retriggered.Add(1);
}
