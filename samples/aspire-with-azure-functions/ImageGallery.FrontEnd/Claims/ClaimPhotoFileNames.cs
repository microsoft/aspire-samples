using System.Text;
using System.Text.RegularExpressions;

namespace ImageGallery.FrontEnd.Claims;

internal static partial class ClaimPhotoFileNames
{
    private const int MaxStemLength = 80;

    // "IMG_2041 (1).JPG" -> "img-2041-1.jpg": lowercase ASCII letters, digits and single hyphens, so blob names and URLs stay simple.
    public static string Create(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var stem = new StringBuilder();

        foreach (var c in Path.GetFileNameWithoutExtension(name).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                stem.Append(c);
            }
            else if (stem.Length > 0 && stem[^1] != '-')
            {
                stem.Append('-');
            }
        }

        var result = stem.ToString().Trim('-');
        if (result.Length > MaxStemLength)
        {
            result = result[..MaxStemLength].TrimEnd('-');
        }

        return (result.Length == 0 ? "photo" : result) + extension;
    }

    public static bool IsValid(string fileName) => ValidFileName().IsMatch(fileName);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*\\.(jpg|jpeg|png|webp)$")]
    private static partial Regex ValidFileName();
}
