using System.Text.Json.Serialization;

namespace ImageGallery.Shared;

public enum PhotoQuality
{
    // Sharp enough for an adjuster to assess the damage
    Ok,
    // Below the minimum resolution; the policyholder is asked to retake the photo
    LowResolution,
    // The file couldn't be decoded as an image
    Unreadable
}

// The function's result for one claim photo, sent to the front end through the results queue.
// TraceParent/TraceState carry the function's span so the front end's consumer span joins the upload's trace.
public sealed record PhotoProcessed(
    string ClaimNumber,
    string FileName,
    PhotoQuality Quality,
    int? Width,
    int? Height,
    int? ThumbnailWidth,
    int? ThumbnailHeight,
    DateTimeOffset ProcessedAt,
    [property: JsonPropertyName("traceparent")] string? TraceParent,
    [property: JsonPropertyName("tracestate")] string? TraceState);
