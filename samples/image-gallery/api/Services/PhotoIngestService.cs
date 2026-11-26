using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Data;
using Api.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http.HttpResults;
using SkiaSharp;

namespace Api.Services;

// The upload pipeline shared by the upload endpoint and the "Upload sample photos" command:
// validate -> store the original in Blob Storage -> record it in SQL -> publish a thumbnail request.
public sealed class PhotoIngestService(
    ImageDbContext db,
    BlobContainerClient containerClient,
    QueueServiceClient queueService,
    GalleryTelemetry telemetry,
    ILogger<PhotoIngestService> logger)
{
    public const string QueueName = "thumbnails";
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    // Cap decoded pixel count to guard against decompression bombs:
    // a heavily compressed image well under MaxFileSizeBytes can declare
    // dimensions that decode to multi-gigabyte SKBitmap allocations.
    // 100 MP allows ~10000x10000 images while bounding peak memory at ~400 MB (RGBA).
    private const long MaxPixelCount = 100_000_000;

    public static readonly IReadOnlyDictionary<string, SKEncodedImageFormat> AllowedFormatsByExtension =
        new Dictionary<string, SKEncodedImageFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = SKEncodedImageFormat.Jpeg,
            [".jpeg"] = SKEncodedImageFormat.Jpeg,
            [".png"] = SKEncodedImageFormat.Png,
            [".gif"] = SKEncodedImageFormat.Gif,
            [".webp"] = SKEncodedImageFormat.Webp
        };

    private static readonly IReadOnlyDictionary<SKEncodedImageFormat, string> ContentTypesByFormat =
        new Dictionary<SKEncodedImageFormat, string>
        {
            [SKEncodedImageFormat.Jpeg] = "image/jpeg",
            [SKEncodedImageFormat.Png] = "image/png",
            [SKEncodedImageFormat.Gif] = "image/gif",
            [SKEncodedImageFormat.Webp] = "image/webp"
        };

    public readonly record struct ValidatedPhoto(string ContentType, string Format);

    public ProblemHttpResult? Validate(string fileName, string? declaredContentType, long length, Func<Stream> openRead, out ValidatedPhoto photo)
    {
        photo = default;

        if (length == 0)
        {
            return Reject(fileName, "empty", StatusCodes.Status400BadRequest, "No file uploaded", "The request didn't include a photo, or the file was empty.");
        }

        if (length > MaxFileSizeBytes)
        {
            return Reject(fileName, "too_large", StatusCodes.Status413PayloadTooLarge, "Photo too large",
                $"Product photos can be at most {MaxFileSizeBytes / (1024 * 1024)} MB; this file is {length / (1024.0 * 1024):F1} MB.");
        }

        if (declaredContentType is not null && !declaredContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Reject(fileName, "unsupported_type", StatusCodes.Status400BadRequest, "Unsupported file type", "The file must be an image.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedFormatsByExtension.TryGetValue(extension, out var expectedFormat))
        {
            return Reject(fileName, "unsupported_type", StatusCodes.Status400BadRequest, "Unsupported file type",
                $"Allowed file types: {string.Join(", ", AllowedFormatsByExtension.Keys)}.");
        }

        using (var detectionStream = openRead())
        using (var codec = SKCodec.Create(detectionStream))
        {
            if (codec is null ||
                codec.EncodedFormat != expectedFormat ||
                codec.Info.Width <= 0 ||
                codec.Info.Height <= 0 ||
                (long)codec.Info.Width * codec.Info.Height > MaxPixelCount ||
                !ContentTypesByFormat.TryGetValue(codec.EncodedFormat, out var contentType))
            {
                return Reject(fileName, "invalid_image", StatusCodes.Status400BadRequest, "Unreadable image",
                    "The file contents must be a valid JPEG, PNG, GIF or WebP image that matches its extension.");
            }

            using var decodeStream = openRead();
            using var bitmap = SKBitmap.Decode(decodeStream);
            if (bitmap is null || bitmap.IsNull || !bitmap.ReadyToDraw)
            {
                return Reject(fileName, "invalid_image", StatusCodes.Status400BadRequest, "Unreadable image",
                    "The file contents must be a valid JPEG, PNG, GIF or WebP image that matches its extension.");
            }

            photo = new ValidatedPhoto(contentType, codec.EncodedFormat.ToString().ToLowerInvariant());
            Activity.Current?.SetTag("lumina.photo.width", codec.Info.Width);
            Activity.Current?.SetTag("lumina.photo.height", codec.Info.Height);
            return null;
        }
    }

    public async Task<Image> StoreAsync(string fileName, long size, ValidatedPhoto photo, Func<Stream> openRead, string source, CancellationToken cancellationToken)
    {
        var sanitizedFileName = SanitizeFileName(fileName);
        var blobName = $"{Guid.NewGuid()}{Path.GetExtension(sanitizedFileName).ToLowerInvariant()}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await using (var stream = openRead())
        {
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = photo.ContentType }
            }, cancellationToken);
        }

        var image = new Image
        {
            FileName = sanitizedFileName,
            ContentType = photo.ContentType,
            Size = size,
            BlobUrl = blobClient.Uri.ToString(),
            ThumbnailProcessed = false
        };

        db.Images.Add(image);
        await db.SaveChangesAsync(cancellationToken);

        var activity = Activity.Current;
        activity?.SetTag("lumina.image.id", image.Id);
        activity?.SetTag("lumina.photo.format", photo.Format);
        activity?.SetTag("lumina.photo.size", size);
        activity?.AddEvent(new ActivityEvent("photo.stored", tags: new ActivityTagsCollection { ["lumina.blob.name"] = blobName }));

        telemetry.PhotoUploaded(photo.Format, source, size);
        logger.PhotoUploaded(image.Id, sanitizedFileName, size, photo.Format);

        await RequestThumbnailAsync(image, "upload", cancellationToken);
        return image;
    }

    // Publishes a thumbnail request and carries the W3C trace context in the message body,
    // so the worker's processing span joins the upload's trace.
    public async Task RequestThumbnailAsync(Image image, string trigger, CancellationToken cancellationToken)
    {
        using var activity = GalleryTelemetry.ActivitySource.StartActivity($"send {QueueName}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "azure_storage_queues");
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.destination.name", QueueName);
        activity?.SetTag("lumina.image.id", image.Id);
        activity?.SetTag("lumina.thumbnail.trigger", trigger);

        var carrier = new Dictionary<string, string>();
        DistributedContextPropagator.Current.Inject(activity ?? Activity.Current, carrier,
            static (c, key, value) => ((Dictionary<string, string>)c!)[key] = value);

        var request = new ThumbnailRequest(
            image.Id,
            image.BlobUrl.Split('/').Last(),
            carrier.GetValueOrDefault("traceparent"),
            carrier.GetValueOrDefault("tracestate"));

        var queueClient = queueService.GetQueueClient(QueueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var receipt = await queueClient.SendMessageAsync(JsonSerializer.Serialize(request, JsonSerializerOptions.Web), cancellationToken);

        activity?.SetTag("messaging.message.id", receipt.Value.MessageId);
        telemetry.ThumbnailRequested(trigger);
        logger.ThumbnailRequested(image.Id, receipt.Value.MessageId, trigger);
    }

    private ProblemHttpResult Reject(string? fileName, string reason, int statusCode, string title, string detail)
    {
        telemetry.UploadRejected(reason);
        logger.UploadRejected(fileName, reason);
        Activity.Current?.SetTag("lumina.rejection.reason", reason);
        return TypedResults.Problem(title: title, detail: detail, statusCode: statusCode);
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove path separators and invalid characters
        var sanitized = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));

        if (sanitized.Length > 255)
        {
            var extension = Path.GetExtension(sanitized);
            sanitized = Path.GetFileNameWithoutExtension(sanitized)[..(255 - extension.Length)] + extension;
        }

        return sanitized;
    }
}

public record ThumbnailRequest(
    int ImageId,
    string BlobName,
    [property: JsonPropertyName("traceparent")] string? TraceParent,
    [property: JsonPropertyName("tracestate")] string? TraceState);
