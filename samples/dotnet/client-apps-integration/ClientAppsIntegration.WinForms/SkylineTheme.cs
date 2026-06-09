using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClientAppsIntegration.WinForms;

/// <summary>
/// Central palette, typography and GDI+ drawing helpers for the "Skyline Dawn" theme.
/// All text colours are chosen to clear WCAG AAA (>=7:1) on the opaque white card surface.
/// </summary>
internal static class SkylineTheme
{
    // Window gradient (pale dawn sky -> warm horizon peach).
    public static readonly Color SkyTop = Hex("EAF2FF");
    public static readonly Color SkyBottom = Hex("FFE9D2");

    // Surfaces.
    public static readonly Color Surface = Hex("FFFFFF");
    public static readonly Color Border = Hex("E2E8F5");
    public static readonly Color ShadowColor = Hex("1D2433");

    // Text (AAA on white: primary 15.5:1, secondary 7.37:1).
    public static readonly Color TextPrimary = Hex("1D2433");
    public static readonly Color TextSecondary = Hex("4C566B");
    public static readonly Color TextOnAccent = Hex("FFFFFF");

    // Primary action (deep warm coral; white label = 7.31:1 AAA).
    // Hover/down go darker so the white label stays AAA in every state.
    public static readonly Color ButtonFill = Hex("9A3412");
    public static readonly Color ButtonHover = Hex("8A2F10");
    public static readonly Color ButtonDown = Hex("6B250E");
    public static readonly Color ButtonDisabled = Hex("B6A79E");
    public static readonly Color FocusRing = Hex("1D2433");

    // Decorative accents (never carry text).
    public static readonly Color AccentCoral = Hex("E5533D");
    public static readonly Color AccentAmber = Hex("F59E0B");

    // Weather glyph colours.
    public static readonly Color SunColor = Hex("F59E0B");
    public static readonly Color HotSunColor = Hex("E5533D");
    public static readonly Color CloudColor = Hex("9AA7BD");
    public static readonly Color SnowColor = Hex("38BDF8");

    // Error state.
    public static readonly Color ErrorAccent = Hex("DC2626");
    public static readonly Color ErrorHeading = Hex("991B1B");
    public static readonly Color ErrorTint = Hex("FEF2F2");

    private const string FamilyName = "Segoe UI";

    public static readonly Font WordmarkFont = new(FamilyName, 21f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SubtitleFont = new(FamilyName, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font ButtonFont = new(FamilyName, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font ToggleFont = new(FamilyName, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font StatusFont = new(FamilyName, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font FooterFont = new(FamilyName, 9f, FontStyle.Regular, GraphicsUnit.Point);

    public static readonly Font WeekdayFont = new(FamilyName, 13f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font DateFont = new(FamilyName, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font TempFont = new(FamilyName, 30f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font TempFFont = new(FamilyName, 10f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font SummaryFont = new(FamilyName, 11.5f, FontStyle.Bold, GraphicsUnit.Point);

    public static readonly Font StateTitleFont = new(FamilyName, 15f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font StateBodyFont = new(FamilyName, 10.5f, FontStyle.Regular, GraphicsUnit.Point);

    public enum WeatherGlyph
    {
        Snow,
        CloudSun,
        Sun,
        HotSun,
    }

    public static Color Hex(string rgb) =>
        Color.FromArgb(
            Convert.ToInt32(rgb.Substring(0, 2), 16),
            Convert.ToInt32(rgb.Substring(2, 2), 16),
            Convert.ToInt32(rgb.Substring(4, 2), 16));

    public static void EnableQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void PaintVerticalGradient(Graphics g, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        using var brush = new LinearGradientBrush(area, SkyTop, SkyBottom, LinearGradientMode.Vertical);
        g.FillRectangle(brush, area);
    }

    /// <summary>
    /// Paints the slice of the form-wide vertical gradient that sits beneath <paramref name="c"/>,
    /// so nested containers keep one continuous backdrop (and DrawToBitmap captures it).
    /// </summary>
    public static void PaintBackdrop(Control c, Graphics g)
    {
        var form = c.FindForm();
        var totalHeight = form?.ClientSize.Height ?? c.Height;
        var offsetY = 0;

        if (form is not null && c.IsHandleCreated && form.IsHandleCreated)
        {
            offsetY = c.PointToScreen(Point.Empty).Y - form.PointToScreen(Point.Empty).Y;
        }

        var area = new Rectangle(0, -offsetY, c.Width, Math.Max(totalHeight, c.Height + offsetY));
        PaintVerticalGradient(g, area);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Paints a soft drop shadow + white rounded card + hairline border inside <paramref name="body"/>.</summary>
    public static void DrawCard(Graphics g, RectangleF body, float radius, bool tinted = false, Color? tint = null)
    {
        EnableQuality(g);

        // Layered, painted shadow (captured by DrawToBitmap, unlike a DWM shadow).
        for (var i = 7; i >= 1; i--)
        {
            var alpha = 5 + (7 - i) * 2;
            var shadowRect = RectangleF.Inflate(body, i, i);
            shadowRect.Offset(0, i * 0.55f);
            using var sp = RoundedRect(shadowRect, radius + i);
            using var sb = new SolidBrush(Color.FromArgb(alpha, ShadowColor));
            g.FillPath(sb, sp);
        }

        using var path = RoundedRect(body, radius);
        using var fill = new SolidBrush(tinted ? (tint ?? ErrorTint) : Surface);
        g.FillPath(fill, path);
        using var pen = new Pen(Border, 1f);
        g.DrawPath(pen, path);
    }

    public static Color AccentForTemp(int celsius) => celsius switch
    {
        < 0 => Hex("38BDF8"),
        < 10 => Hex("14B8A6"),
        < 20 => Hex("22C55E"),
        < 30 => Hex("F59E0B"),
        _ => Hex("E5533D"),
    };

    public static WeatherGlyph GlyphForSummary(string? summary, int celsius)
    {
        switch ((summary ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "freezing":
            case "bracing":
            case "chilly":
                return WeatherGlyph.Snow;
            case "cool":
            case "mild":
                return WeatherGlyph.CloudSun;
            case "warm":
            case "balmy":
                return WeatherGlyph.Sun;
            case "hot":
            case "sweltering":
            case "scorching":
                return WeatherGlyph.HotSun;
        }

        // Fallback by temperature when the summary is unknown.
        return celsius switch
        {
            < 5 => WeatherGlyph.Snow,
            < 18 => WeatherGlyph.CloudSun,
            < 30 => WeatherGlyph.Sun,
            _ => WeatherGlyph.HotSun,
        };
    }

    public static void DrawWeatherGlyph(Graphics g, RectangleF r, WeatherGlyph kind)
    {
        EnableQuality(g);
        switch (kind)
        {
            case WeatherGlyph.Sun:
                DrawSun(g, r, SunColor, bold: false);
                break;
            case WeatherGlyph.HotSun:
                DrawSun(g, r, HotSunColor, bold: true);
                break;
            case WeatherGlyph.CloudSun:
                DrawCloudSun(g, r);
                break;
            case WeatherGlyph.Snow:
                DrawSnow(g, r);
                break;
        }
    }

    private static void DrawSun(Graphics g, RectangleF r, Color color, bool bold)
    {
        var cx = r.X + r.Width / 2f;
        var cy = r.Y + r.Height / 2f;
        var radius = Math.Min(r.Width, r.Height) * (bold ? 0.25f : 0.22f);
        var rayInner = radius * 1.22f;
        var rayOuter = rayInner + radius * (bold ? 0.78f : 0.62f);
        var rays = bold ? 12 : 8;
        var thickness = Math.Max(2.4f, radius * (bold ? 0.30f : 0.24f));

        using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < rays; i++)
        {
            var a = (float)(i * 2 * Math.PI / rays);
            var dx = (float)Math.Cos(a);
            var dy = (float)Math.Sin(a);
            g.DrawLine(pen, cx + dx * rayInner, cy + dy * rayInner, cx + dx * rayOuter, cy + dy * rayOuter);
        }

        using var core = new SolidBrush(color);
        g.FillEllipse(core, cx - radius, cy - radius, radius * 2f, radius * 2f);

        if (bold)
        {
            using var halo = new Pen(Color.FromArgb(70, color), Math.Max(1.5f, radius * 0.16f));
            var hr = radius * 1.05f;
            g.DrawEllipse(halo, cx - hr, cy - hr, hr * 2f, hr * 2f);
        }
    }

    private static void DrawCloudSun(Graphics g, RectangleF r)
    {
        // Sun peeks from the upper-right, cloud sits in front lower-left.
        var sunRect = new RectangleF(r.X + r.Width * 0.40f, r.Y + r.Height * 0.04f, r.Width * 0.56f, r.Height * 0.56f);
        DrawSun(g, sunRect, SunColor, bold: false);

        var cloudRect = new RectangleF(r.X + r.Width * 0.04f, r.Y + r.Height * 0.34f, r.Width * 0.84f, r.Height * 0.56f);
        DrawCloud(g, cloudRect, CloudColor);
    }

    private static void DrawSnow(Graphics g, RectangleF r)
    {
        var cloudRect = new RectangleF(r.X + r.Width * 0.06f, r.Y + r.Height * 0.06f, r.Width * 0.88f, r.Height * 0.56f);
        DrawCloud(g, cloudRect, CloudColor);

        var flakeY = r.Y + r.Height * 0.78f;
        var flakeR = Math.Min(r.Width, r.Height) * 0.085f;
        var xs = new[] { r.X + r.Width * 0.30f, r.X + r.Width * 0.52f, r.X + r.Width * 0.74f };
        var ys = new[] { flakeY, flakeY + flakeR * 0.9f, flakeY };
        using var pen = new Pen(SnowColor, Math.Max(1.6f, flakeR * 0.42f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (var i = 0; i < xs.Length; i++)
        {
            DrawFlake(g, pen, xs[i], ys[i], flakeR);
        }
    }

    private static void DrawFlake(Graphics g, Pen pen, float cx, float cy, float radius)
    {
        for (var i = 0; i < 3; i++)
        {
            var a = (float)(i * Math.PI / 3);
            var dx = (float)Math.Cos(a) * radius;
            var dy = (float)Math.Sin(a) * radius;
            g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
        }
    }

    private static void DrawCloud(Graphics g, RectangleF r, Color color)
    {
        using var path = new GraphicsPath();
        var h = r.Height;
        var w = r.Width;

        var left = new RectangleF(r.X, r.Y + h * 0.40f, h * 0.66f, h * 0.66f);
        var mid = new RectangleF(r.X + w * 0.24f, r.Y + h * 0.06f, h * 0.80f, h * 0.80f);
        var right = new RectangleF(r.Right - h * 0.74f, r.Y + h * 0.34f, h * 0.72f, h * 0.72f);
        var baseRect = new RectangleF(r.X + w * 0.06f, r.Y + h * 0.52f, w * 0.88f, h * 0.46f);

        path.AddEllipse(left);
        path.AddEllipse(mid);
        path.AddEllipse(right);
        path.AddPath(RoundedRect(baseRect, h * 0.22f), false);

        using var fill = new SolidBrush(color);
        g.FillPath(fill, path);
    }

    /// <summary>Draws the Skyline mark: a rising sun over a horizon line.</summary>
    public static void DrawSkylineMark(Graphics g, RectangleF r, Color sun, Color horizon)
    {
        EnableQuality(g);
        var cx = r.X + r.Width / 2f;
        var horizonY = r.Y + r.Height * 0.70f;
        var radius = r.Height * 0.30f;

        using var rayPen = new Pen(sun, Math.Max(2f, r.Height * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float[] angles = { 200, 235, 270, 305, 340 };
        foreach (var deg in angles)
        {
            var a = (float)(deg * Math.PI / 180);
            var dx = (float)Math.Cos(a);
            var dy = (float)Math.Sin(a);
            g.DrawLine(rayPen, cx + dx * radius * 1.35f, horizonY + dy * radius * 1.35f, cx + dx * radius * 1.95f, horizonY + dy * radius * 1.95f);
        }

        var clip = g.Clip;
        g.SetClip(new RectangleF(r.X, r.Y, r.Width, horizonY - r.Y));
        using (var sunBrush = new SolidBrush(sun))
        {
            g.FillEllipse(sunBrush, cx - radius, horizonY - radius, radius * 2f, radius * 2f);
        }

        g.Clip = clip;

        using var horizonPen = new Pen(horizon, Math.Max(2f, r.Height * 0.055f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(horizonPen, r.X + r.Width * 0.06f, horizonY, r.Right - r.Width * 0.06f, horizonY);
    }

    /// <summary>Draws a rounded warning triangle with an exclamation mark.</summary>
    public static void DrawWarningGlyph(Graphics g, RectangleF r, Color fill, Color mark)
    {
        EnableQuality(g);
        var w = r.Width;
        var h = r.Height;
        var apex = new PointF(r.X + w / 2f, r.Y + h * 0.05f);
        var bl = new PointF(r.X + w * 0.05f, r.Y + h * 0.95f);
        var br = new PointF(r.X + w * 0.95f, r.Y + h * 0.95f);

        using (var path = new GraphicsPath())
        {
            path.AddPolygon(new[] { apex, br, bl });
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        using var markBrush = new SolidBrush(mark);
        var barW = Math.Max(3f, w * 0.075f);
        var barTop = r.Y + h * 0.36f;
        var barBot = r.Y + h * 0.66f;
        using (var bar = RoundedRect(new RectangleF(r.X + w / 2f - barW / 2f, barTop, barW, barBot - barTop), barW / 2f))
        {
            g.FillPath(markBrush, bar);
        }

        var dotR = barW * 0.62f;
        g.FillEllipse(markBrush, r.X + w / 2f - dotR, r.Y + h * 0.74f, dotR * 2f, dotR * 2f);
    }
}
