using System.ComponentModel;

namespace ImageGallery.FrontEnd.Claims;

[Description("An open claim that accepts photos.")]
public sealed record ClaimSummary(
    [property: Description("Claim number.")] string ClaimNumber,
    [property: Description("Line of business.")] ClaimLine Line,
    [property: Description("What happened.")] string LossType,
    [property: Description("The insured vehicle or property.")] string Subject,
    [property: Description("Policyholder name.")] string Policyholder,
    [property: Description("Where the loss happened.")] string Location,
    [property: Description("Date of loss.")] DateOnly LossDate,
    [property: Description("Photos attached to the claim.")] int PhotoCount,
    [property: Description("Photos that passed the function's checks and are ready for an adjuster.")] int ReadyCount)
{
    public static ClaimSummary From(Claim claim, IReadOnlyList<ClaimPhoto> photos) =>
        new(claim.Number, claim.Line, claim.LossType, claim.Subject, claim.Policyholder, claim.Location, claim.LossDate,
            photos.Count, photos.Count(p => p.Status is PhotoStatus.Ready));
}

[Description("A damage photo attached to a claim, with the result of the Azure Function's checks.")]
public sealed record ClaimPhotoDto(
    [property: Description("Claim number the photo is attached to.")] string ClaimNumber,
    [property: Description("Blob-safe file name, unique within the claim.")] string FileName,
    [property: Description("Readable name derived from the uploaded file name.")] string DisplayName,
    [property: Description("Review status: `processing` until the function's result arrives, then `ready`, `retake_requested` (below the 720p minimum) or `unreadable`.")] PhotoStatus Status,
    [property: Description("Size of the uploaded file, in bytes.")] long SizeBytes,
    [property: Description("Width in pixels, once processed.")] int? Width,
    [property: Description("Height in pixels, once processed.")] int? Height,
    [property: Description("When the photo was uploaded.")] DateTimeOffset UploadedAt,
    [property: Description("When the function finished processing the photo.")] DateTimeOffset? ProcessedAt,
    [property: Description("URL of the original photo.")] string PhotoUrl,
    [property: Description("URL of the 480 px thumbnail, once the function has rendered it.")] string? ThumbnailUrl)
{
    public static ClaimPhotoDto From(ClaimPhoto photo) =>
        new(photo.ClaimNumber, photo.FileName, photo.DisplayName, photo.Status, photo.SizeBytes, photo.Width, photo.Height,
            photo.UploadedAt, photo.ProcessedAt, ClaimPhotoApi.PhotoUrl(photo), ClaimPhotoApi.ThumbnailUrl(photo));
}

[Description("Result of the Upload sample photos command.")]
public sealed record SamplePhotosResult(
    [property: Description("Number of photos uploaded.")] int Uploaded,
    [property: Description("The uploaded photos, all `processing` until the function's results arrive.")] IReadOnlyList<ClaimPhotoDto> Photos);

[Description("Result of the Delete all photos command.")]
public sealed record ClearPhotosResult(
    [property: Description("Number of photos deleted, with their thumbnails.")] int Deleted);
