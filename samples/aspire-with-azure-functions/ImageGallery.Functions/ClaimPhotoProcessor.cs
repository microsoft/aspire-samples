using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using ImageGallery.Shared;
using ImageGallery.Shared.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ImageGallery.Functions;

// Runs for every new or updated blob in the claim-photos container: checks that the photo is sharp enough for an
// adjuster to assess the damage, saves a thumbnail, and queues the result for the front end.
public sealed class ClaimPhotoProcessor(
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    PhotoProcessorTelemetry telemetry,
    ILogger<ClaimPhotoProcessor> logger)
{
    private const int ThumbnailMaxEdge = 480;
    private const int MinimumShortEdge = 720;
    // Guards against decompression bombs: small files can declare huge dimensions.
    private const long MaxPixelCount = 50_000_000;

    private readonly BlobContainerClient _thumbnails = blobServiceClient.GetBlobContainerClient(ClaimPhotoStorage.ThumbnailsContainer);
    private readonly QueueClient _results = queueServiceClient.GetQueueClient(ClaimPhotoStorage.ResultsQueue);

    [Function(nameof(ProcessClaimPhoto))]
    public async Task ProcessClaimPhoto(
        [BlobTrigger(ClaimPhotoStorage.PhotosContainer + "/{claim}/{name}", Connection = "blobs")] BlobClient photo,
        string claim,
        string name,
        FunctionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var metadata = ReadBlobMetadata(context);

        // Blob triggers have no message headers, so the front end wrote its W3C trace context into the blob's
        // metadata. Parenting on it makes this invocation part of the upload's trace; the link keeps the
        // Functions host's own invocation context.
        var continuesUpload = ActivityContext.TryParse(
            metadata.GetValueOrDefault(ClaimPhotoStorage.Metadata.TraceParent),
            metadata.GetValueOrDefault(ClaimPhotoStorage.Metadata.TraceState),
            isRemote: true,
            out var uploadContext);

        if (!continuesUpload)
        {
            logger.TraceContextMissing(claim, name);
        }

        using var activity = PhotoProcessorTelemetry.ActivitySource.StartActivity(
            nameof(ProcessClaimPhoto),
            ActivityKind.Consumer,
            uploadContext,
            links: continuesUpload && Activity.Current is { } invocation ? [new ActivityLink(invocation.Context)] : null);

        activity?.SetTag("faas.trigger", "datasource");
        activity?.SetTag("faas.invocation_id", context.InvocationId);
        activity?.SetTag("faas.document.collection", ClaimPhotoStorage.PhotosContainer);
        activity?.SetTag("faas.document.operation", "insert");
        activity?.SetTag("faas.document.name", $"{claim}/{name}");
        activity?.SetTag("claims.claim.number", claim);
        activity?.SetTag("claims.photo.name", name);

        telemetry.Started();
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            var download = (await photo.DownloadContentAsync(cancellationToken)).Value;
            activity?.SetTag("claims.photo.size", download.Details.ContentLength);

            RenderedThumbnail rendered;
            try
            {
                rendered = RenderThumbnail(download.Content, cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                // Retrying won't fix a corrupt or unsupported file, so report it instead of failing the invocation.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                telemetry.Failed("unreadable_image");
                logger.PhotoUnreadable(ex, claim, name);

                await SendResultAsync(new PhotoProcessed(claim, name, PhotoQuality.Unreadable, null, null, null, null, DateTimeOffset.UtcNow, null, null), cancellationToken);
                return;
            }

            var quality = Math.Min(rendered.Width, rendered.Height) < MinimumShortEdge ? PhotoQuality.LowResolution : PhotoQuality.Ok;
            activity?.SetTag("claims.photo.quality", quality.ToTagValue());

            if (quality is PhotoQuality.LowResolution)
            {
                activity?.AddEvent(new ActivityEvent("photo.retake_requested", tags: new ActivityTagsCollection { ["claims.photo.minimum_short_edge"] = MinimumShortEdge }));
                logger.RetakeRequested(claim, name, rendered.Width, rendered.Height, MinimumShortEdge);
            }

            await _thumbnails.GetBlobClient($"{claim}/{name}").UploadAsync(rendered.Thumbnail, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "image/jpeg" },
                Metadata = new Dictionary<string, string>
                {
                    [ClaimPhotoStorage.Metadata.Width] = rendered.Width.ToString(),
                    [ClaimPhotoStorage.Metadata.Height] = rendered.Height.ToString(),
                    [ClaimPhotoStorage.Metadata.Quality] = quality.ToTagValue()
                }
            }, cancellationToken);
            activity?.AddEvent(new ActivityEvent("thumbnail.saved"));

            await SendResultAsync(new PhotoProcessed(
                claim, name, quality, rendered.Width, rendered.Height, rendered.ThumbnailWidth, rendered.ThumbnailHeight, DateTimeOffset.UtcNow, null, null), cancellationToken);

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            telemetry.Processed(quality.ToTagValue(), rendered.Format, elapsed);
            logger.PhotoProcessed(claim, name, rendered.Width, rendered.Height, quality.ToTagValue(), rendered.ThumbnailWidth, rendered.ThumbnailHeight, elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage errors are usually transient: rethrow so the Functions host retries the blob trigger.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            telemetry.Failed(ex is RequestFailedException ? "storage_error" : ex.GetType().Name);
            logger.ProcessingFailed(ex, claim, name);
            throw;
        }
        finally
        {
            telemetry.Stopped();
        }
    }

    // Puts the current span's context in the message body so the front end's consumer span joins this trace.
    private async Task SendResultAsync(PhotoProcessed result, CancellationToken cancellationToken)
    {
        var current = Activity.Current;
        var message = result with { TraceParent = current?.Id, TraceState = current?.TraceStateString };

        var receipt = await _results.SendMessageAsync(JsonSerializer.Serialize(message, SerializationContext.Default.PhotoProcessed), cancellationToken);

        current?.SetTag("messaging.message.id", receipt.Value.MessageId);
        logger.ResultQueued(result.Quality.ToTagValue(), result.ClaimNumber, result.FileName, receipt.Value.MessageId);
    }

    private static RenderedThumbnail RenderThumbnail(BinaryData content, CancellationToken cancellationToken)
    {
        using var activity = PhotoProcessorTelemetry.ActivitySource.StartActivity("render thumbnail");
        cancellationToken.ThrowIfCancellationRequested();

        using var codec = SKCodec.Create(content.ToStream())
            ?? throw new InvalidDataException("The file isn't a supported image format.");

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixelCount)
        {
            throw new InvalidDataException($"Unsupported image dimensions {width}x{height}.");
        }

        var format = codec.EncodedFormat.ToString().ToLowerInvariant();
        activity?.SetTag("claims.photo.format", format);
        activity?.SetTag("claims.photo.width", width);
        activity?.SetTag("claims.photo.height", height);

        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("The image data couldn't be decoded.");
        activity?.AddEvent(new ActivityEvent("image.decoded"));

        var scale = Math.Min(1d, (double)ThumbnailMaxEdge / Math.Max(width, height));
        var target = new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));

        using var resized = bitmap.Resize(target, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidDataException("The image couldn't be resized.");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 82)
            ?? throw new InvalidDataException("The thumbnail couldn't be encoded.");

        var thumbnail = BinaryData.FromBytes(encoded.ToArray());
        activity?.SetTag("claims.thumbnail.width", target.Width);
        activity?.SetTag("claims.thumbnail.height", target.Height);
        activity?.SetTag("claims.thumbnail.size", thumbnail.ToMemory().Length);
        activity?.AddEvent(new ActivityEvent("thumbnail.encoded"));

        return new RenderedThumbnail(thumbnail, format, width, height, target.Width, target.Height);
    }

    // The trigger's binding data carries the blob's metadata as JSON, so reading it costs no extra storage call.
    private static Dictionary<string, string> ReadBlobMetadata(FunctionContext context)
    {
        if (context.BindingContext.BindingData.TryGetValue("Metadata", out var value) && value?.ToString() is { Length: > 0 } json)
        {
            try
            {
                if (JsonSerializer.Deserialize(json, SerializationContext.Default.DictionaryStringString) is { } metadata)
                {
                    return new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RenderedThumbnail(BinaryData Thumbnail, string Format, int Width, int Height, int ThumbnailWidth, int ThumbnailHeight);
}
