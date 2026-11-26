using VolumeMount.Data;

namespace VolumeMount.BlazorWeb;

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Inspection {InspectionReference} saved for {AssetTag} at {Site}: {Outcome} with {AttachmentCount} attachments")]
    public static partial void InspectionCreated(this ILogger logger, string inspectionReference, string assetTag, string site, InspectionOutcome outcome, int attachmentCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Uploaded {FileName} to blob {BlobName} ({SizeBytes} bytes)")]
    public static partial void AttachmentUploaded(this ILogger logger, string fileName, string blobName, long sizeBytes);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Failed to upload {FileName} to blob {BlobName}")]
    public static partial void AttachmentUploadFailed(this ILogger logger, Exception exception, string fileName, string blobName);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Couldn't delete orphaned blob {BlobName}")]
    public static partial void AttachmentCleanupFailed(this ILogger logger, Exception exception, string blobName);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Attachment blob {BlobName} is missing from Blob Storage")]
    public static partial void AttachmentBlobMissing(this ILogger logger, string blobName);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Seeded {InspectionCount} sample inspections with {AttachmentCount} attachments")]
    public static partial void SampleDataSeeded(this ILogger logger, int inspectionCount, int attachmentCount);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Storage status: {InspectionCount} inspections in SQL Server (started {SqlServerStartedAt:u}), {BlobCount} blobs in {BlobContainer}")]
    public static partial void StorageStatusChecked(this ILogger logger, int inspectionCount, DateTimeOffset sqlServerStartedAt, int blobCount, string blobContainer);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Rejected inspection for {AssetTag}: {Reason}")]
    public static partial void InspectionRejected(this ILogger logger, string assetTag, string reason);
}
