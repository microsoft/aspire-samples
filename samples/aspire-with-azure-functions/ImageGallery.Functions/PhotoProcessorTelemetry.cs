using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ImageGallery.Functions;

// Domain telemetry for the claim photo function. The "Contoso.*" wildcard in ServiceDefaults exports it.
public sealed class PhotoProcessorTelemetry
{
    public const string Name = "Contoso.PhotoProcessor";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _processed;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _failed;
    private readonly UpDownCounter<long> _active;

    public PhotoProcessorTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _processed = meter.CreateCounter<long>(
            "claims.photos.processed", unit: "{photo}",
            description: "Claim photos checked and thumbnailed by the blob-triggered function.");

        _duration = meter.CreateHistogram<double>(
            "claims.photo.processing.duration", unit: "s",
            description: "Time to download, check, resize and save a claim photo's thumbnail and queue the result.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });

        _failed = meter.CreateCounter<long>(
            "claims.photos.failed", unit: "{photo}",
            description: "Claim photos the function couldn't process.");

        _active = meter.CreateUpDownCounter<long>(
            "claims.photos.processing.active", unit: "{photo}",
            description: "Claim photos the function is processing right now.");
    }

    public void Started() => _active.Add(1);

    public void Stopped() => _active.Add(-1);

    // Tags are bounded: quality is ok/low_resolution, format is jpeg/png/webp/gif.
    public void Processed(string quality, string format, TimeSpan elapsed)
    {
        var tags = new TagList { { "claims.photo.quality", quality }, { "claims.photo.format", format } };
        _processed.Add(1, tags);
        _duration.Record(elapsed.TotalSeconds, tags);
    }

    public void Failed(string errorType) => _failed.Add(1, new KeyValuePair<string, object?>("error.type", errorType));
}
