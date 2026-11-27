using Microsoft.Extensions.Logging;

namespace ImageGallery.Functions;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Processed claim {ClaimNumber} photo {FileName}: {Width}x{Height} ({Quality}), {ThumbnailWidth}x{ThumbnailHeight} thumbnail in {ElapsedMs:F1} ms")]
    public static partial void PhotoProcessed(this ILogger logger, string claimNumber, string fileName, int width, int height, string quality, int thumbnailWidth, int thumbnailHeight, double elapsedMs);

    [LoggerMessage(2, LogLevel.Warning, "Claim {ClaimNumber} photo {FileName} is only {Width}x{Height}; adjusters need a short edge of at least {MinimumShortEdge} px, so a retake was requested")]
    public static partial void RetakeRequested(this ILogger logger, string claimNumber, string fileName, int width, int height, int minimumShortEdge);

    [LoggerMessage(3, LogLevel.Error, "Claim {ClaimNumber} photo {FileName} couldn't be decoded as an image")]
    public static partial void PhotoUnreadable(this ILogger logger, Exception exception, string claimNumber, string fileName);

    [LoggerMessage(4, LogLevel.Error, "Processing claim {ClaimNumber} photo {FileName} failed; the Functions host will retry the blob trigger")]
    public static partial void ProcessingFailed(this ILogger logger, Exception exception, string claimNumber, string fileName);

    [LoggerMessage(5, LogLevel.Information, "Queued {Quality} result for claim {ClaimNumber} photo {FileName} as message {MessageId}")]
    public static partial void ResultQueued(this ILogger logger, string quality, string claimNumber, string fileName, string messageId);

    [LoggerMessage(6, LogLevel.Debug, "Claim {ClaimNumber} photo {FileName} has no trace context in its metadata; starting a new trace")]
    public static partial void TraceContextMissing(this ILogger logger, string claimNumber, string fileName);
}
