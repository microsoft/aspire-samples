using Microsoft.AspNetCore.Components;

namespace HealthChecksUI.Web.Components;

/// <summary>
/// Inline Lucide icons (rendered as SVG) so the status console has no external
/// icon-font dependency and renders deterministically for screenshots/tests.
/// </summary>
public static class Icons
{
    private static MarkupString Lucide(string inner) => new(
        $"<svg class=\"icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
        $"stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{inner}</svg>");

    public static MarkupString Activity => Lucide(
        "<path d=\"M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2\"/>");

    public static MarkupString Server => Lucide(
        "<rect width=\"20\" height=\"8\" x=\"2\" y=\"2\" rx=\"2\" ry=\"2\"/>" +
        "<rect width=\"20\" height=\"8\" x=\"2\" y=\"14\" rx=\"2\" ry=\"2\"/>" +
        "<line x1=\"6\" x2=\"6.01\" y1=\"6\" y2=\"6\"/>" +
        "<line x1=\"6\" x2=\"6.01\" y1=\"18\" y2=\"18\"/>");

    public static MarkupString Database => Lucide(
        "<ellipse cx=\"12\" cy=\"5\" rx=\"9\" ry=\"3\"/>" +
        "<path d=\"M3 5V19A9 3 0 0 0 21 19V5\"/>" +
        "<path d=\"M3 12A9 3 0 0 0 21 12\"/>");

    public static MarkupString Globe => Lucide(
        "<circle cx=\"12\" cy=\"12\" r=\"10\"/>" +
        "<path d=\"M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20\"/>" +
        "<path d=\"M2 12h20\"/>");

    public static MarkupString Gauge => Lucide(
        "<path d=\"m12 14 4-4\"/>" +
        "<path d=\"M3.34 19a10 10 0 1 1 17.32 0\"/>");

    public static MarkupString CircleCheck => Lucide(
        "<circle cx=\"12\" cy=\"12\" r=\"10\"/>" +
        "<path d=\"m9 12 2 2 4-4\"/>");

    public static MarkupString TriangleAlert => Lucide(
        "<path d=\"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3\"/>" +
        "<path d=\"M12 9v4\"/><path d=\"M12 17h.01\"/>");

    public static MarkupString Zap => Lucide(
        "<path d=\"M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z\"/>");

    public static MarkupString RefreshCw => Lucide(
        "<path d=\"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8\"/>" +
        "<path d=\"M21 3v5h-5\"/>" +
        "<path d=\"M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16\"/>" +
        "<path d=\"M8 16H3v5\"/>");

    public static MarkupString ArrowUpRight => Lucide(
        "<path d=\"M7 7h10v10\"/><path d=\"M7 17 17 7\"/>");

    public static MarkupString ShieldCheck => Lucide(
        "<path d=\"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z\"/>" +
        "<path d=\"m9 12 2 2 4-4\"/>");

    public static MarkupString Signal => Lucide(
        "<path d=\"M2 20h.01\"/><path d=\"M7 20v-4\"/><path d=\"M12 20v-8\"/>" +
        "<path d=\"M17 20V8\"/><path d=\"M22 4v16\"/>");

    public static MarkupString GitHub => new(
        "<svg class=\"icon\" viewBox=\"0 0 16 16\" fill=\"currentColor\" aria-hidden=\"true\">" +
        "<path d=\"M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.6 7.6 0 0 1 2-.27c.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8z\"/></svg>");
}
