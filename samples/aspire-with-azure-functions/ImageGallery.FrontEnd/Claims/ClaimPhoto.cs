using System.Text;
using ImageGallery.Shared;

namespace ImageGallery.FrontEnd.Claims;

public enum PhotoStatus
{
    Processing,
    Ready,
    RetakeRequested,
    Unreadable
}

// A claim photo as the intake page sees it. FileName is the blob-safe name under "{ClaimNumber}/".
public sealed record ClaimPhoto(
    string ClaimNumber,
    string FileName,
    string OriginalFileName,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    PhotoStatus Status = PhotoStatus.Processing,
    int? Width = null,
    int? Height = null,
    DateTimeOffset? ProcessedAt = null)
{
    public string BlobName => $"{ClaimNumber}/{FileName}";

    public string DisplayName { get; } = Humanize(Path.GetFileNameWithoutExtension(OriginalFileName));

    public TimeSpan? Turnaround => ProcessedAt - UploadedAt;

    public static PhotoStatus StatusFor(PhotoQuality quality) => quality switch
    {
        PhotoQuality.Ok => PhotoStatus.Ready,
        PhotoQuality.LowResolution => PhotoStatus.RetakeRequested,
        _ => PhotoStatus.Unreadable
    };

    // "rear-bumper-damage" -> "Rear bumper damage"
    private static string Humanize(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            text.Append(c is '-' or '_' ? ' ' : c);
        }

        var result = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return result.Length == 0 ? "Claim photo" : char.ToUpperInvariant(result[0]) + result[1..];
    }
}
