using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Worker.Telemetry;

// Domain telemetry for the thumbnail worker. The "Lumina.*" wildcard in shared/Extensions.cs exports it.
public sealed class ThumbnailTelemetry
{
    public const string Name = "Lumina.ThumbnailWorker";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _generated;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _failed;
    private readonly UpDownCounter<long> _backlog;
    private long _lastObservedDepth;

    public ThumbnailTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _generated = meter.CreateCounter<long>(
            "lumina.thumbnails.generated", unit: "{thumbnail}",
            description: "Catalog thumbnails generated and saved to Blob Storage.");

        _duration = meter.CreateHistogram<double>(
            "lumina.thumbnail.duration", unit: "s",
            description: "Time to download, resize, encode and save a thumbnail and update the catalog record.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });

        _failed = meter.CreateCounter<long>(
            "lumina.thumbnails.failed", unit: "{thumbnail}",
            description: "Thumbnail attempts that failed (the message is retried up to 3 times).");

        _backlog = meter.CreateUpDownCounter<long>(
            "lumina.thumbnails.backlog", unit: "{message}",
            description: "Thumbnail requests waiting in the 'thumbnails' queue, as last observed by the worker.");
    }

    public void Generated(string format, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("lumina.photo.format", format);
        _generated.Add(1, tag);
        _duration.Record(elapsed.TotalSeconds, tag);
    }

    public void Failed(string errorType) => _failed.Add(1, new KeyValuePair<string, object?>("error.type", errorType));

    // Track queue depth as deltas so the up-down counter's running sum equals the latest observation.
    public void QueueDepthObserved(long depth)
    {
        var delta = depth - Interlocked.Exchange(ref _lastObservedDepth, depth);
        if (delta != 0)
        {
            _backlog.Add(delta);
        }
    }
}
