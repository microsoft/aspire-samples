using Microsoft.AspNetCore.Components;

namespace OrleansVoting;

/// <summary>
/// Hand-authored inline SVG glyphs for the Grain Tickets UI. Bespoke set (not an
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

    /// <summary>A notched admission ticket — the Grain Tickets mark.</summary>
    public static MarkupString Logo { get; } = Fill(
        "<path d='M4.5 5h15A2.5 2.5 0 0122 7.5V10a2 2 0 000 4v2.5a2.5 2.5 0 01-2.5 2.5h-15A2.5 2.5 0 012 16.5V14a2 2 0 000-4V7.5A2.5 2.5 0 014.5 5z' opacity='.35'/>" +
        "<path d='M15 5h1.6v2.2H15zM15 9h1.6v2.2H15zM15 12.8h1.6V15H15zM15 16.8h1.6V19H15z'/>" +
        "<path d='M8.6 8.3l.9 1.9 2.1.3-1.5 1.5.4 2.1-1.9-1-1.9 1 .4-2.1-1.5-1.5 2.1-.3z'/>");

    public static MarkupString Ticket { get; } = Stroke(
        "<path d='M4.5 5.5h15a2 2 0 012 2V10a2 2 0 000 4v2.5a2 2 0 01-2 2h-15a2 2 0 01-2-2V14a2 2 0 000-4V7.5a2 2 0 012-2z'/>" +
        "<path d='M15 6v2M15 11v2M15 16v2'/>");

    public static MarkupString Check { get; } = Stroke("<path d='M5 12.6l4.3 4.4L19 6.9'/>", 2.4);

    public static MarkupString Cross { get; } = Stroke("<path d='M6.5 6.5l11 11M17.5 6.5l-11 11'/>", 2.4);

    public static MarkupString Undo { get; } = Stroke("<path d='M9 14L4 9l5-5'/><path d='M4 9h10.5a5.5 5.5 0 010 11H11'/>");

    public static MarkupString Plus { get; } = Stroke("<path d='M12 5v14M5 12h14'/>", 2.2);

    public static MarkupString Minus { get; } = Stroke("<path d='M5 12h14'/>", 2.2);

    public static MarkupString Calendar { get; } = Stroke(
        "<rect x='3.5' y='5' width='17' height='15.5' rx='3'/><path d='M3.5 10h17M8 3v4M16 3v4'/>");

    public static MarkupString MapPin { get; } = Stroke(
        "<path d='M12 21s-6.5-5.6-6.5-11a6.5 6.5 0 0113 0c0 5.4-6.5 11-6.5 11z'/><circle cx='12' cy='10' r='2.4'/>");

    public static MarkupString Users { get; } = Stroke(
        "<circle cx='9' cy='8' r='3.2'/><path d='M3.6 19.4a5.4 5.4 0 0110.8 0'/>" +
        "<path d='M16.2 5.3a3.2 3.2 0 010 5.7'/><path d='M17.8 14.3a5.4 5.4 0 013 5.1'/>");

    public static MarkupString Server { get; } = Stroke(
        "<rect x='3.5' y='4' width='17' height='7' rx='2.2'/><rect x='3.5' y='13' width='17' height='7' rx='2.2'/>" +
        "<path d='M7.5 7.5h.01M7.5 16.5h.01M11 7.5h5.5M11 16.5h5.5'/>", 2.2);

    public static MarkupString Flame { get; } = Fill(
        "<path d='M12.6 2.5c.4 3-1.3 4.7-2.8 6.3C8.3 10.4 7 11.9 7 14.6A5 5 0 0012 19.8a5 5 0 005-5.2c0-1.7-.6-3.1-1.5-4.2-.2 1.3-.9 2.2-1.9 2.6.6-3.6-.3-7.7-1-10.5z'/>");

    public static MarkupString Bolt { get; } = Fill("<path d='M13 2L4 13.6h5.4L8.4 22 20 9.4h-5.4z'/>");

    public static MarkupString Sparkles { get; } = Fill(
        "<path d='M11.5 3l1.6 4.3L17.4 9l-4.3 1.7L11.5 15l-1.6-4.3L5.6 9l4.3-1.7z'/>" +
        "<path d='M18.4 13.6l.7 2 2 .7-2 .7-.7 2-.7-2-2-.7 2-.7z'/>");

    public static MarkupString Refresh { get; } = Stroke(
        "<path d='M20 11a8 8 0 10-1.8 6'/><path d='M20 4v6h-6'/>");

    public static MarkupString ArrowRight { get; } = Stroke("<path d='M5 12h14M13 6l6 6-6 6'/>");

    public static MarkupString ArrowLeft { get; } = Stroke("<path d='M19 12H5M11 6l-6 6 6 6'/>");

    public static MarkupString Github { get; } = Fill(
        "<path d='M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 " +
        "0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 " +
        "1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 " +
        "0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 " +
        "2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 " +
        "3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0016 8c0-4.42-3.58-8-8-8z'/>",
        "0 0 16 16");
}
