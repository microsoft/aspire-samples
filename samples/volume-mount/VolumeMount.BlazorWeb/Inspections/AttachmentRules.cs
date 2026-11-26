namespace VolumeMount.BlazorWeb.Inspections;

public static class AttachmentRules
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxFiles = 6;

    private static readonly Dictionary<string, string> s_allowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "image",
        ["image/png"] = "image",
        ["image/webp"] = "image",
        ["image/svg+xml"] = "image",
        ["application/pdf"] = "document",
        ["text/plain"] = "document",
        ["text/csv"] = "data"
    };

    public static string Accept => string.Join(',', s_allowedTypes.Keys);

    /// <summary>A bounded category used as a metric tag instead of the raw content type.</summary>
    public static string KindOf(string contentType) => s_allowedTypes.GetValueOrDefault(contentType, "other");

    public static bool IsImage(string contentType) => KindOf(contentType) == "image";

    public static string? Validate(IReadOnlyCollection<AttachmentUpload> files)
    {
        if (files.Count > MaxFiles)
        {
            return $"Attach at most {MaxFiles} files per inspection.";
        }

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                return $"'{file.FileName}' is empty.";
            }
            if (file.Length > MaxFileBytes)
            {
                return $"'{file.FileName}' is larger than {MaxFileBytes / (1024 * 1024)} MB.";
            }
            if (!s_allowedTypes.ContainsKey(file.ContentType))
            {
                return $"'{file.FileName}' has an unsupported type ({file.ContentType}). Upload JPEG, PNG, WebP, SVG, PDF, TXT, or CSV files.";
            }
        }

        return null;
    }

    public static IReadOnlyList<AttachmentUpload> FromFormFiles(IEnumerable<IFormFile> files) =>
        [.. files.Where(f => !string.IsNullOrEmpty(f.FileName))
                 .Select(f => new AttachmentUpload(Path.GetFileName(f.FileName), f.ContentType, f.Length, f.OpenReadStream))];
}
