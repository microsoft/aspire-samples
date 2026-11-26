using System.Diagnostics;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using VolumeMount.Data;

namespace VolumeMount.BlazorWeb.Inspections;

/// <summary>Raised when an attachment can't be written to Blob Storage. Nothing is saved to SQL Server in that case.</summary>
public sealed class AttachmentStorageException(string fileName, Exception inner)
    : Exception($"Couldn't store '{fileName}' in Blob Storage.", inner);

/// <summary>
/// Writes inspection records to SQL Server and their attachments to Azure Blob Storage, as one traced operation.
/// </summary>
public sealed class InspectionService(
    InspectionDbContext db,
    BlobContainerClient attachments,
    InspectionTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<InspectionService> logger)
{
    public async Task<InspectionDetail> CreateAsync(InspectionForm form, IReadOnlyList<AttachmentUpload> files, string source, DateTimeOffset? inspectedAt = null, CancellationToken cancellationToken = default)
    {
        using var activity = InspectionTelemetry.ActivitySource.StartActivity("inspection.create");
        activity?.SetTag("inspection.asset_tag", form.AssetTag);
        activity?.SetTag("inspection.site", form.Site);
        activity?.SetTag("inspection.outcome", form.Outcome.ToString());
        activity?.SetTag("inspection.source", source);
        activity?.SetTag("inspection.attachment.count", files.Count);

        var now = timeProvider.GetUtcNow();
        var inspection = new Inspection
        {
            AssetTag = form.AssetTag.Trim().ToUpperInvariant(),
            AssetName = form.AssetName.Trim(),
            Site = form.Site.Trim(),
            Inspector = form.Inspector.Trim(),
            Outcome = form.Outcome,
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            InspectedAt = inspectedAt ?? now,
            CreatedAt = now
        };

        // Upload blobs first so a SQL row never points at a missing file; undo the uploads if the insert fails
        try
        {
            foreach (var file in files)
            {
                inspection.Attachments.Add(await UploadAsync(inspection.AssetTag, file, cancellationToken));
            }

            db.Inspections.Add(inspection);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await DeleteBlobsAsync(inspection.Attachments);
            throw;
        }

        activity?.SetTag("inspection.id", inspection.Id);
        activity?.AddEvent(new ActivityEvent("inspection.saved", tags: new ActivityTagsCollection
        {
            ["inspection.id"] = inspection.Id,
            ["db.system.name"] = "microsoft.sql_server"
        }));

        telemetry.RecordCreated(inspection.Outcome.ToString().ToLowerInvariant(), source);
        logger.InspectionCreated(InspectionReference.Format(inspection.Id), inspection.AssetTag, inspection.Site, inspection.Outcome, inspection.Attachments.Count);

        return ToDetail(inspection);
    }

    public async Task<IReadOnlyList<InspectionSummary>> ListAsync(InspectionOutcome? outcome, string? site, int take, CancellationToken cancellationToken = default)
    {
        var query = db.Inspections.AsNoTracking();
        if (outcome is { } o)
        {
            query = query.Where(i => i.Outcome == o);
        }
        if (!string.IsNullOrWhiteSpace(site))
        {
            query = query.Where(i => i.Site == site);
        }

        var rows = await query
            .OrderByDescending(i => i.InspectedAt)
            .Take(take)
            .Select(i => new
            {
                i.Id, i.AssetTag, i.AssetName, i.Site, i.Inspector, i.Outcome, i.InspectedAt,
                AttachmentCount = i.Attachments.Count,
                AttachmentBytes = i.Attachments.Sum(a => (long?)a.SizeBytes) ?? 0,
                Cover = i.Attachments.Where(a => a.ContentType.StartsWith("image/")).OrderBy(a => a.Id).Select(a => (int?)a.Id).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new InspectionSummary(r.Id, InspectionReference.Format(r.Id), r.AssetTag, r.AssetName, r.Site, r.Inspector, r.Outcome, r.InspectedAt, r.AttachmentCount, r.AttachmentBytes, r.Cover))];
    }

    public async Task<Dictionary<InspectionOutcome, int>> CountByOutcomeAsync(CancellationToken cancellationToken = default) =>
        await db.Inspections
            .GroupBy(i => i.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Outcome, g => g.Count, cancellationToken);

    public async Task<InspectionDetail?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var inspection = await db.Inspections.AsNoTracking()
            .Include(i => i.Attachments.OrderBy(a => a.Id))
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        return inspection is null ? null : ToDetail(inspection);
    }

    public async Task<(InspectionAttachment Attachment, Stream Content)?> OpenAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        try
        {
            var download = await attachments.GetBlobClient(attachment.BlobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return (attachment, download.Value.Content);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            logger.AttachmentBlobMissing(attachment.BlobName);
            return null;
        }
    }

    public async Task<StorageStatus> GetStorageStatusAsync(CancellationToken cancellationToken = default)
    {
        var inspectionCount = await db.Inspections.CountAsync(cancellationToken);
        var attachmentCount = await db.Attachments.CountAsync(cancellationToken);
        var startedAt = await db.Database
            .SqlQuery<DateTime>($"SELECT sqlserver_start_time AS [Value] FROM sys.dm_os_sys_info")
            .SingleAsync(cancellationToken);
        var dataFile = await db.Database
            .SqlQuery<string>($"SELECT physical_name AS [Value] FROM sys.database_files WHERE type = 0")
            .FirstAsync(cancellationToken);

        int blobCount = 0;
        long blobBytes = 0;
        await foreach (var blob in attachments.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            blobCount++;
            blobBytes += blob.Properties.ContentLength ?? 0;
        }

        // SQL Server in the container runs on UTC
        return new StorageStatus(inspectionCount, attachmentCount, blobCount, blobBytes, attachments.Name,
            new DateTimeOffset(DateTime.SpecifyKind(startedAt, DateTimeKind.Utc)), dataFile);
    }

    private async Task<InspectionAttachment> UploadAsync(string assetTag, AttachmentUpload file, CancellationToken cancellationToken)
    {
        var kind = AttachmentRules.KindOf(file.ContentType);
        var blobName = $"{assetTag}/{timeProvider.GetUtcNow():yyyy/MM/dd}/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";

        telemetry.UploadStarted();
        try
        {
            await using var content = file.OpenReadStream();
            await attachments.GetBlobClient(blobName).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                Metadata = new Dictionary<string, string> { ["assetTag"] = assetTag }
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            telemetry.UploadFailed(kind, ex is RequestFailedException rfe ? rfe.ErrorCode ?? rfe.Status.ToString() : ex.GetType().Name);
            logger.AttachmentUploadFailed(ex, file.FileName, blobName);
            throw new AttachmentStorageException(file.FileName, ex);
        }
        finally
        {
            telemetry.UploadFinished();
        }

        telemetry.AttachmentStored(kind, file.Length);
        Activity.Current?.AddEvent(new ActivityEvent("attachment.uploaded", tags: new ActivityTagsCollection
        {
            ["attachment.blob_name"] = blobName,
            ["attachment.kind"] = kind,
            ["attachment.size"] = file.Length
        }));
        logger.AttachmentUploaded(file.FileName, blobName, file.Length);

        return new InspectionAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            BlobName = blobName,
            UploadedAt = timeProvider.GetUtcNow()
        };
    }

    private async Task DeleteBlobsAsync(IEnumerable<InspectionAttachment> uploaded)
    {
        foreach (var attachment in uploaded)
        {
            try
            {
                await attachments.DeleteBlobIfExistsAsync(attachment.BlobName);
            }
            catch (RequestFailedException ex)
            {
                logger.AttachmentCleanupFailed(ex, attachment.BlobName);
            }
        }
    }

    private static InspectionDetail ToDetail(Inspection i) => new(
        i.Id, InspectionReference.Format(i.Id), i.AssetTag, i.AssetName, i.Site, i.Inspector, i.Outcome, i.Notes, i.InspectedAt, i.CreatedAt,
        [.. i.Attachments.Select(a => new AttachmentInfo(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.BlobName, a.UploadedAt, $"/api/attachments/{a.Id}/content"))]);
}
