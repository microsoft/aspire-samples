using System.ComponentModel;

namespace Api.Models;

[Description("A product photo in the catalog, with the status of its generated thumbnail.")]
public record ImageDto(
    [property: Description("The photo ID.")] int Id,
    [property: Description("The sanitized file name supplied at upload, e.g. `field-chronograph-watch.jpg`.")] string FileName,
    [property: Description("The detected image content type (`image/jpeg`, `image/png`, `image/gif` or `image/webp`).")] string ContentType,
    [property: Description("Size of the original upload in bytes.")] long Size,
    [property: Description("Whether the thumbnail worker has generated the 300×300 catalog thumbnail.")] bool ThumbnailProcessed,
    [property: Description("When the photo was uploaded (UTC).")] DateTime UploadedAt)
{
    public static ImageDto FromImage(Image image) =>
        new(image.Id, image.FileName, image.ContentType, image.Size, image.ThumbnailProcessed, image.UploadedAt);
}

[Description("An antiforgery request token. Send it in the `RequestVerificationToken` header on uploads and deletes.")]
public record AntiforgeryTokenResponse([property: Description("The request token.")] string? Token);

[Description("Result of uploading the bundled sample product photos.")]
public record SamplePhotosResult(
    [property: Description("Number of photos uploaded.")] int Uploaded,
    [property: Description("The new catalog photos, each queued for thumbnail generation.")] IReadOnlyList<ImageDto> Photos);

[Description("Result of re-queuing thumbnail generation.")]
public record ReprocessResult([property: Description("Number of photos queued for a new thumbnail.")] int Queued);
