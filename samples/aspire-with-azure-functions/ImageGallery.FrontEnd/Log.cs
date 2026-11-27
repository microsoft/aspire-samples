namespace ImageGallery.FrontEnd;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Uploaded claim {ClaimNumber} photo {FileName} ({SizeBytes} bytes, source {Source}); waiting for the blob trigger")]
    public static partial void PhotoUploaded(this ILogger logger, string claimNumber, string fileName, long sizeBytes, string source);

    [LoggerMessage(2, LogLevel.Warning, "Rejected photo '{FileName}' for claim {ClaimNumber}: {Reason}")]
    public static partial void UploadRejected(this ILogger logger, string claimNumber, string fileName, string reason);

    [LoggerMessage(3, LogLevel.Error, "Failed to store claim {ClaimNumber} photo {FileName}")]
    public static partial void UploadFailed(this ILogger logger, Exception exception, string claimNumber, string fileName);

    [LoggerMessage(4, LogLevel.Information, "Claim {ClaimNumber} photo {FileName} is {Status} after {TurnaroundMs:F0} ms")]
    public static partial void PhotoReviewed(this ILogger logger, string claimNumber, string fileName, string status, double turnaroundMs);

    [LoggerMessage(5, LogLevel.Warning, "Discarded unreadable result message {MessageId}")]
    public static partial void InvalidResultMessage(this ILogger logger, Exception? exception, string messageId);

    [LoggerMessage(6, LogLevel.Information, "Loaded {PhotoCount} claim photos ({ProcessingCount} still processing) from Blob Storage")]
    public static partial void CatalogLoaded(this ILogger logger, int photoCount, int processingCount);

    [LoggerMessage(7, LogLevel.Information, "Uploaded {Count} sample claim photos")]
    public static partial void SamplePhotosUploaded(this ILogger logger, int count);

    [LoggerMessage(8, LogLevel.Information, "Deleted {Count} claim photos and their thumbnails")]
    public static partial void PhotosCleared(this ILogger logger, int count);

    [LoggerMessage(9, LogLevel.Error, "An Azure Storage request from the results worker failed; retrying in 5 seconds")]
    public static partial void StorageRequestFailed(this ILogger logger, Exception exception);

    [LoggerMessage(10, LogLevel.Warning, "Claim {ClaimNumber} photo {FileName} is still waiting for the blob trigger after {WaitSeconds:F0} s; touched its metadata so the next container scan picks it up")]
    public static partial void PhotoRetriggered(this ILogger logger, string claimNumber, string fileName, double waitSeconds);
}
