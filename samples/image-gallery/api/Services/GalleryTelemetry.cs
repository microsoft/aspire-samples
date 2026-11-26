using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Services;

// Domain telemetry for the catalog photo API. The "Lumina.*" wildcard in shared/Extensions.cs exports it.
public sealed class GalleryTelemetry
{
    public const string Name = "Lumina.Api";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _photosUploaded;
    private readonly Histogram<long> _uploadSize;
    private readonly Counter<long> _uploadsRejected;
    private readonly Counter<long> _thumbnailsRequested;

    public GalleryTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _photosUploaded = meter.CreateCounter<long>(
            "lumina.photos.uploaded", unit: "{photo}",
            description: "Product photos stored in Blob Storage and recorded in the catalog database.");

        _uploadSize = meter.CreateHistogram<long>(
            "lumina.photo.upload.size", unit: "By",
            description: "Size of accepted product photo uploads.",
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = [16_384, 32_768, 65_536, 131_072, 262_144, 524_288, 1_048_576, 4_194_304, 10_485_760] });

        _uploadsRejected = meter.CreateCounter<long>(
            "lumina.photos.rejected", unit: "{photo}",
            description: "Uploads rejected by validation (size, type, or unreadable image).");

        _thumbnailsRequested = meter.CreateCounter<long>(
            "lumina.thumbnails.requested", unit: "{message}",
            description: "Thumbnail requests published to the 'thumbnails' queue.");
    }

    // Tags are bounded: format is one of jpeg/png/gif/webp, source is "upload" or "sample".
    public void PhotoUploaded(string format, string source, long sizeBytes)
    {
        var tags = new TagList { { "lumina.photo.format", format }, { "lumina.upload.source", source } };
        _photosUploaded.Add(1, tags);
        _uploadSize.Record(sizeBytes, tags);
    }

    public void UploadRejected(string reason) =>
        _uploadsRejected.Add(1, new KeyValuePair<string, object?>("lumina.rejection.reason", reason));

    public void ThumbnailRequested(string trigger) =>
        _thumbnailsRequested.Add(1, new KeyValuePair<string, object?>("lumina.thumbnail.trigger", trigger));
}
