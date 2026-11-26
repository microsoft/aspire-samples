namespace Api;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Photo {ImageId} '{FileName}' uploaded ({SizeBytes} bytes, {Format}) and queued for thumbnail generation")]
    public static partial void PhotoUploaded(this ILogger logger, int imageId, string fileName, long sizeBytes, string format);

    [LoggerMessage(2, LogLevel.Warning, "Upload of '{FileName}' rejected: {Reason}")]
    public static partial void UploadRejected(this ILogger logger, string? fileName, string reason);

    [LoggerMessage(3, LogLevel.Error, "Failed to store photo '{FileName}'")]
    public static partial void UploadFailed(this ILogger logger, Exception exception, string fileName);

    [LoggerMessage(4, LogLevel.Information, "Photo {ImageId} '{FileName}' deleted")]
    public static partial void PhotoDeleted(this ILogger logger, int imageId, string fileName);

    [LoggerMessage(5, LogLevel.Error, "Failed to delete photo {ImageId}")]
    public static partial void DeleteFailed(this ILogger logger, Exception exception, int imageId);

    [LoggerMessage(6, LogLevel.Information, "Thumbnail request for photo {ImageId} sent as queue message {MessageId} ({Trigger})")]
    public static partial void ThumbnailRequested(this ILogger logger, int imageId, string messageId, string trigger);

    [LoggerMessage(7, LogLevel.Information, "Uploaded {Count} sample product photos")]
    public static partial void SamplePhotosUploaded(this ILogger logger, int count);

    [LoggerMessage(8, LogLevel.Information, "Re-queued {Count} photos for thumbnail generation")]
    public static partial void ThumbnailsReprocessed(this ILogger logger, int count);

    [LoggerMessage(9, LogLevel.Information, "Catalog database is ready")]
    public static partial void DatabaseReady(this ILogger logger);

    [LoggerMessage(10, LogLevel.Error, "Catalog database initialization failed")]
    public static partial void DatabaseInitializationFailed(this ILogger logger, Exception exception);
}
