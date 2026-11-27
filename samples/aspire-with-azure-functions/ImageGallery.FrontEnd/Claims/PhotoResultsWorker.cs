using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using ImageGallery.Shared;
using ImageGallery.Shared.Serialization;

namespace ImageGallery.FrontEnd.Claims;

// Creates the storage containers and queue, loads the catalog, then reads the function's results from the queue
// and applies them to the catalog so open intake pages update live.
public sealed class PhotoResultsWorker(
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    ClaimPhotoCatalog catalog,
    ClaimIntakeTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<PhotoResultsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_stalledCheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_stalledAfter = TimeSpan.FromSeconds(30);

    private readonly BlobContainerClient _photos = blobServiceClient.GetBlobContainerClient(ClaimPhotoStorage.PhotosContainer);
    private readonly BlobContainerClient _thumbnails = blobServiceClient.GetBlobContainerClient(ClaimPhotoStorage.ThumbnailsContainer);
    private readonly QueueClient _results = queueServiceClient.GetQueueClient(ClaimPhotoStorage.ResultsQueue);
    private readonly Dictionary<string, DateTimeOffset> _retriggeredAt = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _results.CreateIfNotExistsAsync(cancellationToken: cancellationToken),
            _photos.CreateIfNotExistsAsync(cancellationToken: cancellationToken),
            _thumbnails.CreateIfNotExistsAsync(cancellationToken: cancellationToken));

        await LoadCatalogAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextStalledCheck = timeProvider.GetUtcNow() + s_stalledCheckInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (timeProvider.GetUtcNow() >= nextStalledCheck)
                {
                    nextStalledCheck = timeProvider.GetUtcNow() + s_stalledCheckInterval;
                    await RetriggerStalledPhotosAsync(stoppingToken);
                }

                var messages = (await _results.ReceiveMessagesAsync(maxMessages: 16, visibilityTimeout: TimeSpan.FromSeconds(30), stoppingToken)).Value;
                if (messages.Length == 0)
                {
                    await Task.Delay(s_pollInterval, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    await HandleResultAsync(message, stoppingToken);
                }
            }
            catch (RequestFailedException ex)
            {
                logger.StorageRequestFailed(ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleResultAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        PhotoProcessed? result = null;
        JsonException? error = null;
        try
        {
            result = JsonSerializer.Deserialize(message.Body.ToString(), SerializationContext.Default.PhotoProcessed);
        }
        catch (JsonException ex)
        {
            error = ex;
        }

        if (result is null)
        {
            logger.InvalidResultMessage(error, message.MessageId);
            await _results.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        // Continue the upload's trace: the function wrote its span's W3C context into the message body.
        ActivityContext.TryParse(result.TraceParent, result.TraceState, isRemote: true, out var parentContext);

        using var activity = ClaimIntakeTelemetry.ActivitySource.StartActivity($"process {ClaimPhotoStorage.ResultsQueue}", ActivityKind.Consumer, parentContext);
        activity?.SetTag("messaging.system", "azure_storage_queues");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", ClaimPhotoStorage.ResultsQueue);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("claims.claim.number", result.ClaimNumber);
        activity?.SetTag("claims.photo.name", result.FileName);
        activity?.SetTag("claims.photo.quality", result.Quality.ToTagValue());

        if (catalog.ApplyResult(result) is { } photo)
        {
            var turnaround = timeProvider.GetUtcNow() - photo.UploadedAt;
            telemetry.ResultReceived(result.Quality.ToTagValue(), turnaround);
            activity?.SetTag("claims.photo.turnaround_ms", Math.Round(turnaround.TotalMilliseconds));
            activity?.AddEvent(new ActivityEvent("catalog.updated", tags: new ActivityTagsCollection { ["claims.photo.status"] = photo.Status.ToString() }));
            logger.PhotoReviewed(result.ClaimNumber, result.FileName, photo.Status.ToString(), turnaround.TotalMilliseconds);
        }

        await _results.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
    }

    // The blob trigger's container scan can miss a blob that's uploaded while a scan is running
    // (Azure/azure-sdk-for-net#62763). Rewriting the photo's metadata gives it a new ETag and Last-Modified time,
    // so the next scan picks it up; the metadata still carries the upload's trace context.
    private async Task RetriggerStalledPhotosAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var processing = catalog.GetPhotos().Where(p => p.Status is PhotoStatus.Processing).ToList();

        foreach (var key in _retriggeredAt.Keys.Except(processing.Select(p => p.BlobName)).ToList())
        {
            _retriggeredAt.Remove(key);
        }

        var stalled = processing
            .Where(p => now - (_retriggeredAt.TryGetValue(p.BlobName, out var at) ? at : p.UploadedAt) > s_stalledAfter)
            .ToList();

        if (stalled.Count == 0)
        {
            return;
        }

        using var activity = ClaimIntakeTelemetry.ActivitySource.StartActivity("retrigger stalled photos");
        activity?.SetTag("claims.photos.count", stalled.Count);

        foreach (var photo in stalled)
        {
            _retriggeredAt[photo.BlobName] = now;
            var blob = _photos.GetBlobClient(photo.BlobName);
            try
            {
                var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
                await blob.SetMetadataAsync(properties.Metadata, cancellationToken: cancellationToken);

                telemetry.PhotoRetriggered();
                logger.PhotoRetriggered(photo.ClaimNumber, photo.FileName, (now - photo.UploadedAt).TotalSeconds);
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                catalog.Remove(photo);
            }
        }
    }

    // Rebuilds the catalog from storage: a photo is processed when its thumbnail is at least as new as the photo.
    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        using var activity = ClaimIntakeTelemetry.ActivitySource.StartActivity("load photo catalog");

        var thumbnails = new Dictionary<string, BlobItem>(StringComparer.OrdinalIgnoreCase);
        await foreach (var thumbnail in _thumbnails.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }, cancellationToken))
        {
            thumbnails[thumbnail.Name] = thumbnail;
        }

        var photos = new List<ClaimPhoto>();
        await foreach (var blob in _photos.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }, cancellationToken))
        {
            if (blob.Name.Split('/') is not [var claimNumber, var fileName])
            {
                continue;
            }

            var uploadedAt = blob.Properties.LastModified ?? timeProvider.GetUtcNow();
            var originalFileName = blob.Metadata.TryGetValue(ClaimPhotoStorage.Metadata.OriginalFileName, out var original)
                ? Uri.UnescapeDataString(original)
                : fileName;
            var photo = new ClaimPhoto(claimNumber, fileName, originalFileName, blob.Properties.ContentLength ?? 0, uploadedAt);

            if (thumbnails.TryGetValue(blob.Name, out var thumbnail) &&
                thumbnail.Properties.LastModified >= uploadedAt &&
                PhotoQualityNames.FromTagValue(Read(thumbnail.Metadata, ClaimPhotoStorage.Metadata.Quality)) is { } quality)
            {
                photo = photo with
                {
                    Status = ClaimPhoto.StatusFor(quality),
                    Width = int.TryParse(Read(thumbnail.Metadata, ClaimPhotoStorage.Metadata.Width), out var width) ? width : null,
                    Height = int.TryParse(Read(thumbnail.Metadata, ClaimPhotoStorage.Metadata.Height), out var height) ? height : null,
                    ProcessedAt = thumbnail.Properties.LastModified
                };
            }

            photos.Add(photo);
        }

        catalog.Load(photos);

        var processing = photos.Count(p => p.Status is PhotoStatus.Processing);
        activity?.SetTag("claims.photos.count", photos.Count);
        logger.CatalogLoaded(photos.Count, processing);
    }

    private static string? Read(IDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) ? value : null;
}
