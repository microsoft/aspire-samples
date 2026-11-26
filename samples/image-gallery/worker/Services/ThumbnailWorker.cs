using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using SkiaSharp;
using Worker.Data;
using Worker.Models;
using Worker.Telemetry;

namespace Worker.Services;

public class ThumbnailWorker(
    IServiceProvider serviceProvider,
    BlobContainerClient containerClient,
    QueueServiceClient queueService,
    ThumbnailTelemetry telemetry,
    IHostApplicationLifetime hostApplicationLifetime,
    IConfiguration configuration,
    ILogger<ThumbnailWorker> logger) : BackgroundService
{
    private const string QueueName = "thumbnails";
    private const string ThumbnailContentType = "image/jpeg";
    private const int ThumbnailWidth = 300;
    private const int ThumbnailHeight = 300;
    private const long MaxImageSizeBytes = 20 * 1024 * 1024; // 20 MB - slightly larger than upload limit
    // Cap decoded pixel count to guard against decompression bombs that
    // pass the byte-size check but declare extreme dimensions.
    // 100 MP allows ~10000x10000 images while bounding peak SKBitmap memory.
    private const long MaxPixelCount = 100_000_000;
    private const int MaxRetryCount = 3;
    private const int MaxEmptyPolls = 2;        // Poll up to 2 times (event-triggered)
    private const int EmptyPollWaitSeconds = 5; // Wait 5 seconds between polls (total: ~5 seconds)

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.GetValue<bool>("WORKER_RUN_CONTINUOUSLY"))
        {
            // Local dev: poll every 5 seconds, run forever
            await RunAsync(continuous: true, stoppingToken);
        }
        else
        {
            try
            {
                // Event-triggered mode: process messages until the queue is empty, then shut down
                await RunAsync(continuous: false, stoppingToken);
            }
            finally
            {
                // New job instances start automatically when the queue has messages
                hostApplicationLifetime.StopApplication();
            }
        }
    }

    private async Task RunAsync(bool continuous, CancellationToken stoppingToken)
    {
        logger.WorkerStarted(continuous ? "continuous (local dev)" : "event-triggered");

        var queueClient = queueService.GetQueueClient(QueueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        var processedCount = 0;
        var emptyPollCount = 0;
        var startTime = Stopwatch.GetTimestamp();

        while (!stoppingToken.IsCancellationRequested)
        {
            var queueDepth = (await queueClient.GetPropertiesAsync(stoppingToken)).Value.ApproximateMessagesCount;
            telemetry.QueueDepthObserved(queueDepth);

            var messages = (await queueClient.ReceiveMessagesAsync(
                maxMessages: 10,
                visibilityTimeout: TimeSpan.FromMinutes(5),
                cancellationToken: stoppingToken)).Value;

            if (messages.Length == 0)
            {
                if (!continuous && ++emptyPollCount >= MaxEmptyPolls)
                {
                    break;
                }

                if (!continuous)
                {
                    logger.QueueEmpty(EmptyPollWaitSeconds, emptyPollCount, MaxEmptyPolls);
                }

                await Task.Delay(TimeSpan.FromSeconds(EmptyPollWaitSeconds), stoppingToken);
                continue;
            }

            emptyPollCount = 0;
            logger.BatchReceived(messages.Length, queueDepth);

            foreach (var message in messages)
            {
                await ProcessMessageWithRetryAsync(message, queueClient, stoppingToken);
                processedCount++;
            }
        }

        logger.WorkerStopped(processedCount, Stopwatch.GetElapsedTime(startTime));
    }

    private async Task ProcessMessageWithRetryAsync(QueueMessage message, QueueClient queueClient, CancellationToken cancellationToken)
    {
        ThumbnailRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<ThumbnailRequest>(message.MessageText, JsonSerializerOptions.Web)
                ?? throw new JsonException("Empty thumbnail request");
        }
        catch (JsonException ex)
        {
            telemetry.Failed("invalid_message");
            logger.ThumbnailFailed(ex, message.MessageId, message.DequeueCount);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        // Continue the uploader's trace: the API wrote its W3C trace context into the message.
        ActivityContext.TryParse(request.TraceParent, request.TraceState, isRemote: true, out var parentContext);

        using var activity = ThumbnailTelemetry.ActivitySource.StartActivity($"process {QueueName}", ActivityKind.Consumer, parentContext);
        activity?.SetTag("messaging.system", "azure_storage_queues");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", QueueName);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("lumina.message.dequeue_count", message.DequeueCount);
        activity?.SetTag("lumina.image.id", request.ImageId);

        try
        {
            await ProcessMessageAsync(request, message, queueClient, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            telemetry.Failed(ex switch
            {
                RequestFailedException => "storage_error",
                InvalidOperationException => "invalid_image",
                _ => ex.GetType().Name
            });
            logger.ThumbnailFailed(ex, message.MessageId, message.DequeueCount);

            if (message.DequeueCount >= MaxRetryCount)
            {
                logger.MessageDiscarded(message.MessageId, MaxRetryCount);
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            }
        }
    }

    private async Task ProcessMessageAsync(ThumbnailRequest request, QueueMessage message, QueueClient queueClient, CancellationToken cancellationToken)
    {
        var startTime = Stopwatch.GetTimestamp();
        var imageId = request.ImageId;
        var sourceBlobClient = containerClient.GetBlobClient(request.BlobName);

        // Check blob size before downloading
        Response<BlobProperties> properties;
        try
        {
            properties = await sourceBlobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            // The photo was deleted after its thumbnail was requested; retrying won't help.
            Activity.Current?.AddEvent(new ActivityEvent("photo.missing"));
            logger.PhotoMissing(imageId);
            await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (properties.Value.ContentLength > MaxImageSizeBytes)
        {
            throw new InvalidOperationException($"Image size {properties.Value.ContentLength} exceeds maximum allowed {MaxImageSizeBytes}");
        }

        using var originalStream = new MemoryStream();
        await sourceBlobClient.DownloadToAsync(originalStream, cancellationToken);
        originalStream.Position = 0;

        var (thumbnail, sourceSize, thumbnailSize) = RenderThumbnail(imageId, originalStream, cancellationToken);
        using (thumbnail)
        {
            var thumbnailBlobClient = containerClient.GetBlobClient($"thumb-{request.BlobName}");
            await thumbnailBlobClient.UploadAsync(thumbnail, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = ThumbnailContentType }
            }, cancellationToken);

            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImageDbContext>();
            var imageRecord = await db.Images.FindAsync([imageId], cancellationToken);

            if (imageRecord is null)
            {
                logger.PhotoMissing(imageId);
            }
            else
            {
                imageRecord.ThumbnailUrl = thumbnailBlobClient.Uri.ToString();
                imageRecord.ThumbnailProcessed = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        var format = Path.GetExtension(request.BlobName).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "jpeg",
            var ext => ext
        };
        telemetry.Generated(format, elapsed);
        Activity.Current?.AddEvent(new ActivityEvent("thumbnail.saved"));
        logger.ThumbnailGenerated(imageId, thumbnailSize.Width, thumbnailSize.Height, sourceSize.Width, sourceSize.Height, elapsed.TotalMilliseconds);
    }

    private static (MemoryStream Thumbnail, SKSizeI Source, SKSizeI Target) RenderThumbnail(int imageId, Stream original, CancellationToken cancellationToken)
    {
        using var activity = ThumbnailTelemetry.ActivitySource.StartActivity("render thumbnail");
        cancellationToken.ThrowIfCancellationRequested();

        using var codec = SKCodec.Create(original)
            ?? throw new InvalidOperationException($"Unable to decode image {imageId}");

        if (codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            throw new InvalidOperationException($"Image {imageId} has invalid dimensions");
        }

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixelCount)
        {
            throw new InvalidOperationException(
                $"Image {imageId} pixel count {(long)codec.Info.Width * codec.Info.Height} exceeds maximum allowed {MaxPixelCount}");
        }

        activity?.SetTag("lumina.photo.width", codec.Info.Width);
        activity?.SetTag("lumina.photo.height", codec.Info.Height);
        activity?.SetTag("lumina.photo.format", codec.EncodedFormat.ToString().ToLowerInvariant());

        using var image = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException($"Unable to decode image {imageId}");
        activity?.AddEvent(new ActivityEvent("image.decoded"));

        var scale = Math.Min((double)ThumbnailWidth / image.Width, (double)ThumbnailHeight / image.Height);
        var targetSize = new SKImageInfo(
            Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale)));

        using var resizedImage = image.Resize(targetSize, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException($"Unable to resize image {imageId}");

        using var encoded = resizedImage.Encode(SKEncodedImageFormat.Jpeg, quality: 85)
            ?? throw new InvalidOperationException($"Unable to encode thumbnail for image {imageId}");

        var thumbnail = new MemoryStream();
        encoded.SaveTo(thumbnail);
        thumbnail.Position = 0;

        activity?.SetTag("lumina.thumbnail.width", targetSize.Width);
        activity?.SetTag("lumina.thumbnail.height", targetSize.Height);
        activity?.SetTag("lumina.thumbnail.size", thumbnail.Length);
        activity?.AddEvent(new ActivityEvent("thumbnail.encoded"));

        return (thumbnail, new SKSizeI(codec.Info.Width, codec.Info.Height), new SKSizeI(targetSize.Width, targetSize.Height));
    }
}
