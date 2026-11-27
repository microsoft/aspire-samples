using System.Diagnostics;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ImageGallery.Shared;

namespace ImageGallery.FrontEnd.Claims;

public sealed record IntakeProblem(int StatusCode, string Reason, string Title, string Detail);

public sealed record IntakeResult(ClaimPhoto? Photo, IntakeProblem? Problem);

// The upload pipeline shared by the intake page, the upload API and the "Upload sample photos" command:
// validate -> store the photo in the claim-photos container (which fires the function's blob trigger) -> track it.
public sealed class ClaimPhotoIntake(
    BlobServiceClient blobServiceClient,
    ClaimPhotoCatalog catalog,
    ClaimIntakeTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<ClaimPhotoIntake> logger)
{
    public const long MaxPhotoBytes = 10 * 1024 * 1024;
    public const string AcceptedFileTypes = ".jpg,.jpeg,.png,.webp";

    private static readonly Dictionary<string, (string ContentType, string Format)> s_allowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ("image/jpeg", "jpeg"),
        [".jpeg"] = ("image/jpeg", "jpeg"),
        [".png"] = ("image/png", "png"),
        [".webp"] = ("image/webp", "webp")
    };

    private readonly BlobContainerClient _photos = blobServiceClient.GetBlobContainerClient(ClaimPhotoStorage.PhotosContainer);

    public async Task<IntakeResult> UploadAsync(string claimNumber, string fileName, long length, Func<Stream> openRead, string source, CancellationToken cancellationToken)
    {
        using var activity = ClaimIntakeTelemetry.ActivitySource.StartActivity("upload claim photo", ActivityKind.Producer);
        activity?.SetTag("claims.claim.number", claimNumber);
        activity?.SetTag("claims.photo.source", source);
        activity?.SetTag("claims.photo.size", length);

        if (ClaimDirectory.Find(claimNumber) is not { } claim)
        {
            return Reject(claimNumber, fileName, new(StatusCodes.Status404NotFound, "unknown_claim", "Claim not found", $"There's no open claim '{claimNumber}'."));
        }

        if (length == 0)
        {
            return Reject(claimNumber, fileName, new(StatusCodes.Status400BadRequest, "empty", "No photo uploaded", "The request didn't include a photo, or the file was empty."));
        }

        if (length > MaxPhotoBytes)
        {
            return Reject(claimNumber, fileName, new(StatusCodes.Status413PayloadTooLarge, "too_large", "Photo too large",
                $"Claim photos can be at most {MaxPhotoBytes / (1024 * 1024)} MB; this file is {length / (1024.0 * 1024):F1} MB."));
        }

        if (!s_allowedTypes.TryGetValue(Path.GetExtension(fileName), out var type))
        {
            return Reject(claimNumber, fileName, new(StatusCodes.Status400BadRequest, "unsupported_type", "Unsupported file type", "Upload a JPEG, PNG or WebP photo."));
        }

        var photo = new ClaimPhoto(claim.Number, ClaimPhotoFileNames.Create(fileName), Path.GetFileName(fileName), length, timeProvider.GetUtcNow());
        activity?.SetTag("claims.photo.name", photo.FileName);
        activity?.SetTag("claims.photo.format", type.Format);

        var metadata = new Dictionary<string, string>
        {
            [ClaimPhotoStorage.Metadata.ClaimNumber] = claim.Number,
            [ClaimPhotoStorage.Metadata.OriginalFileName] = Uri.EscapeDataString(photo.OriginalFileName),
            [ClaimPhotoStorage.Metadata.Source] = source
        };

        // Blob triggers have no message headers, so the W3C trace context rides along in the blob's metadata.
        // The function parents its span on it, which keeps the whole upload in one trace.
        if ((activity ?? Activity.Current) is { IdFormat: ActivityIdFormat.W3C, Id: { } traceParent } current)
        {
            metadata[ClaimPhotoStorage.Metadata.TraceParent] = traceParent;
            if (current.TraceStateString is { } traceState)
            {
                metadata[ClaimPhotoStorage.Metadata.TraceState] = traceState;
            }
        }

        // Track the photo before the upload so a fast result can't arrive for a photo the catalog doesn't know yet.
        catalog.Upsert(photo);

        try
        {
            await using var content = openRead();
            await _photos.GetBlobClient(photo.BlobName).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = type.ContentType },
                Metadata = metadata
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            catalog.Remove(photo);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.UploadFailed(ex, claim.Number, photo.FileName);
            throw;
        }

        telemetry.PhotoUploaded(source, type.Format, length);
        activity?.AddEvent(new ActivityEvent("photo.stored", tags: new ActivityTagsCollection
        {
            ["claims.blob.container"] = ClaimPhotoStorage.PhotosContainer,
            ["claims.blob.name"] = photo.BlobName
        }));
        logger.PhotoUploaded(claim.Number, photo.FileName, length, source);

        return new(photo, null);
    }

    private IntakeResult Reject(string claimNumber, string fileName, IntakeProblem problem)
    {
        telemetry.UploadRejected(problem.Reason);
        logger.UploadRejected(claimNumber, fileName, problem.Reason);
        Activity.Current?.SetTag("claims.rejection.reason", problem.Reason);
        return new(null, problem);
    }
}
