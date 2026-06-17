namespace Api.Models;

public class ImageDto
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public bool ThumbnailProcessed { get; set; }
    public DateTime UploadedAt { get; set; }

    public static ImageDto FromImage(Image image) => new()
    {
        Id = image.Id,
        FileName = image.FileName,
        ContentType = image.ContentType,
        Size = image.Size,
        ThumbnailProcessed = image.ThumbnailProcessed,
        UploadedAt = image.UploadedAt
    };
}
