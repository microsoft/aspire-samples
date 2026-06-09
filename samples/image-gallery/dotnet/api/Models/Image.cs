namespace Api.Models;

public class Image
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required string BlobUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool ThumbnailProcessed { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
