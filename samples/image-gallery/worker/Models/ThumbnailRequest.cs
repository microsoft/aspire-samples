using System.Text.Json.Serialization;

namespace Worker.Models;

// Matches the message the API publishes; traceparent/tracestate carry the uploader's W3C trace context.
public record ThumbnailRequest(
    int ImageId,
    string BlobName,
    [property: JsonPropertyName("traceparent")] string? TraceParent,
    [property: JsonPropertyName("tracestate")] string? TraceState);
