namespace Worker;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Thumbnail worker started in {Mode} mode")]
    public static partial void WorkerStarted(this ILogger logger, string mode);

    [LoggerMessage(2, LogLevel.Information, "Thumbnail worker stopped after processing {Count} messages in {Elapsed}")]
    public static partial void WorkerStopped(this ILogger logger, int count, TimeSpan elapsed);

    [LoggerMessage(3, LogLevel.Information, "Received {Count} thumbnail requests ({QueueDepth} in queue)")]
    public static partial void BatchReceived(this ILogger logger, int count, int queueDepth);

    [LoggerMessage(4, LogLevel.Information, "Queue empty, waiting {WaitSeconds}s before retry ({PollCount}/{MaxPolls})")]
    public static partial void QueueEmpty(this ILogger logger, int waitSeconds, int pollCount, int maxPolls);

    [LoggerMessage(5, LogLevel.Information, "Generated {ThumbnailWidth}x{ThumbnailHeight} thumbnail for photo {ImageId} ({SourceWidth}x{SourceHeight}) in {ElapsedMs:F1} ms")]
    public static partial void ThumbnailGenerated(this ILogger logger, int imageId, int thumbnailWidth, int thumbnailHeight, int sourceWidth, int sourceHeight, double elapsedMs);

    [LoggerMessage(6, LogLevel.Error, "Thumbnail generation failed for queue message {MessageId} (attempt {DequeueCount})")]
    public static partial void ThumbnailFailed(this ILogger logger, Exception exception, string messageId, long dequeueCount);

    [LoggerMessage(7, LogLevel.Warning, "Queue message {MessageId} exceeded {MaxRetryCount} attempts and was discarded")]
    public static partial void MessageDiscarded(this ILogger logger, string messageId, int maxRetryCount);

    [LoggerMessage(8, LogLevel.Warning, "Photo {ImageId} no longer exists; skipping thumbnail")]
    public static partial void PhotoMissing(this ILogger logger, int imageId);
}
