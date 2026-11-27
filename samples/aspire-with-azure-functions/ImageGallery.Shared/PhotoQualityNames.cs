namespace ImageGallery.Shared;

public static class PhotoQualityNames
{
    // Bounded values used for metric tags, span attributes and blob metadata.
    public static string ToTagValue(this PhotoQuality quality) => quality switch
    {
        PhotoQuality.Ok => "ok",
        PhotoQuality.LowResolution => "low_resolution",
        _ => "unreadable"
    };

    public static PhotoQuality? FromTagValue(string? value) => value switch
    {
        "ok" => PhotoQuality.Ok,
        "low_resolution" => PhotoQuality.LowResolution,
        "unreadable" => PhotoQuality.Unreadable,
        _ => null
    };
}
