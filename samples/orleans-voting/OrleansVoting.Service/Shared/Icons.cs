using Microsoft.AspNetCore.Components;

namespace OrleansVoting;

/// <summary>
/// Hand-authored inline SVG glyphs for the Grain Poll UI. Bespoke set (not an
/// icon font/pack). Each glyph is decorative; callers provide accessible text.
/// </summary>
public static class Icons
{
    private static MarkupString Stroke(string body, double width = 2) => new(
        $"<svg class=\"icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
        $"stroke-width=\"{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" " +
        $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>");

    private static MarkupString Fill(string body, string viewBox = "0 0 24 24") => new(
        $"<svg class=\"icon\" viewBox=\"{viewBox}\" fill=\"currentColor\" aria-hidden=\"true\">{body}</svg>");

    /// <summary>Ascending result bars — the Grain Poll mark.</summary>
    public static MarkupString Logo { get; } = Fill(
        "<rect x='3.4' y='12' width='4.2' height='8.6' rx='1.6'/>" +
        "<rect x='9.9' y='7.6' width='4.2' height='13' rx='1.6'/>" +
        "<rect x='16.4' y='4' width='4.2' height='16.6' rx='1.6'/>");

    public static MarkupString Ballot { get; } = Stroke(
        "<rect x='3.5' y='3.5' width='17' height='17' rx='4.5'/><path d='M12 8.4v7.2M8.4 12h7.2'/>");

    public static MarkupString Check { get; } = Stroke("<path d='M5 12.6l4.3 4.4L19 6.9'/>", 2.4);

    public static MarkupString Plus { get; } = Stroke("<path d='M12 5v14M5 12h14'/>", 2.2);

    public static MarkupString Trash { get; } = Stroke(
        "<path d='M4 7h16'/><path d='M9.5 7V5.6A1.6 1.6 0 0111 4h2a1.6 1.6 0 011.5 1.6V7'/>" +
        "<path d='M6.4 7l1 12.6A1.6 1.6 0 009 21h6a1.6 1.6 0 001.6-1.4L17.6 7'/>");

    public static MarkupString Crown { get; } = Fill(
        "<path d='M2.7 8.2c.6-.5 1.5-.4 2 .2l2.5 3 3-4.6c.5-.7 1.6-.7 2 0l3 4.6 2.5-3c.5-.6 1.4-.7 2-.2.5.4.7 1 .5 1.6l-2 8.2a1.2 1.2 0 01-1.2.9H5.4a1.2 1.2 0 01-1.2-.9l-2-8.2c-.2-.6 0-1.2.5-1.6z'/>");

    public static MarkupString Users { get; } = Stroke(
        "<circle cx='9' cy='8' r='3.2'/><path d='M3.6 19.4a5.4 5.4 0 0110.8 0'/>" +
        "<path d='M16.2 5.3a3.2 3.2 0 010 5.7'/><path d='M17.8 14.3a5.4 5.4 0 013 5.1'/>");

    public static MarkupString Bolt { get; } = Fill("<path d='M13 2L4 13.6h5.4L8.4 22 20 9.4h-5.4z'/>");

    public static MarkupString Share { get; } = Stroke(
        "<circle cx='6' cy='12' r='2.6'/><circle cx='18' cy='5.6' r='2.6'/><circle cx='18' cy='18.4' r='2.6'/>" +
        "<path d='M8.3 10.8l7.4-3.7M8.3 13.2l7.4 3.7'/>");

    public static MarkupString Sparkles { get; } = Fill(
        "<path d='M11.5 3l1.6 4.3L17.4 9l-4.3 1.7L11.5 15l-1.6-4.3L5.6 9l4.3-1.7z'/>" +
        "<path d='M18.4 13.6l.7 2 2 .7-2 .7-.7 2-.7-2-2-.7 2-.7z'/>");

    public static MarkupString Refresh { get; } = Stroke(
        "<path d='M20 11a8 8 0 10-1.8 6'/><path d='M20 4v6h-6'/>");

    public static MarkupString ArrowUpRight { get; } = Stroke("<path d='M7 17L17 7M8.5 7H17v8.5'/>");

    public static MarkupString Github { get; } = Fill(
        "<path d='M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 " +
        "0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 " +
        "1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 " +
        "0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 " +
        "2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 " +
        "3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0016 8c0-4.42-3.58-8-8-8z'/>",
        "0 0 16 16");
}
