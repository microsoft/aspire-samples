using System.Globalization;

namespace ClientAppsIntegration.WinForms;

/// <summary>
/// A single day's forecast rendered as a real child control (so UI Automation / screen readers
/// expose each card individually). Text lives in <see cref="Label"/> children on the opaque white
/// surface; the weather glyph and temperature accent bar are GDI+ vector drawn (no icon fonts/emoji).
/// </summary>
internal sealed class ForecastCard : CardPanel
{
    private readonly Label _weekday = MakeLabel(SkylineTheme.WeekdayFont, SkylineTheme.TextPrimary);
    private readonly Label _date = MakeLabel(SkylineTheme.DateFont, SkylineTheme.TextSecondary);
    private readonly Label _temp = MakeLabel(SkylineTheme.TempFont, SkylineTheme.TextPrimary);
    private readonly Label _tempF = MakeLabel(SkylineTheme.TempFFont, SkylineTheme.TextSecondary);
    private readonly Label _summary = MakeLabel(SkylineTheme.SummaryFont, SkylineTheme.TextPrimary);

    private SkylineTheme.WeatherGlyph _glyph = SkylineTheme.WeatherGlyph.Sun;
    private Color _accent = SkylineTheme.AccentAmber;
    private RectangleF _glyphBounds;
    private RectangleF _accentBounds;

    public ForecastCard()
    {
        ContentPadding = 16;
        Margin = new Padding(8);
        AccessibleRole = AccessibleRole.Grouping;
        Controls.AddRange(new Control[] { _weekday, _date, _temp, _tempF, _summary });
    }

    public void SetForecast(WeatherForecast forecast)
    {
        var date = forecast.Date;
        var summary = string.IsNullOrWhiteSpace(forecast.Summary) ? "Unknown" : forecast.Summary!;

        _weekday.Text = date.ToString("ddd", CultureInfo.InvariantCulture);
        _date.Text = date.ToString("MMM d", CultureInfo.InvariantCulture);
        _temp.Text = $"{forecast.TemperatureC}\u00B0";
        _tempF.Text = $"{forecast.TemperatureF}\u00B0F";
        _summary.Text = summary;

        _glyph = SkylineTheme.GlyphForSummary(summary, forecast.TemperatureC);
        _accent = SkylineTheme.AccentForTemp(forecast.TemperatureC);

        AccessibleName =
            $"{date.ToString("dddd MMMM d", CultureInfo.InvariantCulture)}, {summary}, " +
            $"{DescribeTemp(forecast.TemperatureC)} Celsius, {DescribeTemp(forecast.TemperatureF)} Fahrenheit";
        AccessibleDescription = $"{summary}. {forecast.TemperatureC} degrees Celsius.";

        LayoutContent();
        Invalidate();
    }

    private static string DescribeTemp(int value) =>
        value < 0
            ? $"minus {Math.Abs(value)} degrees"
            : $"{value} degrees";

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutContent();
    }

    private void LayoutContent()
    {
        var c = ContentRectangle;
        if (c.Width <= 0 || c.Height <= 0)
        {
            return;
        }

        var x = c.Left;
        var w = c.Width;
        var y = c.Top;

        _weekday.SetBounds(x, y, w, 26);
        y += 26;
        _date.SetBounds(x, y, w, 18);
        y += 18;

        const int summaryH = 24;
        const int tempFH = 18;
        const int tempH = 46;
        var summaryY = c.Bottom - summaryH;
        var tempFY = summaryY - tempFH;
        var tempY = tempFY - tempH;

        _summary.SetBounds(x, summaryY, w, summaryH);
        _tempF.SetBounds(x, tempFY, w, tempFH);
        _temp.SetBounds(x, tempY, w, tempH);

        var glyphTop = y + 6;
        var glyphBottom = tempY - 6;
        var glyphMax = Math.Max(28, glyphBottom - glyphTop);
        var glyphSize = Math.Min(Math.Min(w, glyphMax), 78);
        var gx = x + (w - glyphSize) / 2f;
        var gy = glyphTop + (glyphBottom - glyphTop - glyphSize) / 2f;
        _glyphBounds = new RectangleF(gx, gy, glyphSize, glyphSize);

        var body = BodyBounds;
        const float barH = 5f;
        _accentBounds = new RectangleF(body.X + 16, body.Bottom - 15, body.Width - 32, barH);
    }

    protected override void OnPaintContent(Graphics g)
    {
        base.OnPaintContent(g);

        if (_glyphBounds.Width > 0)
        {
            SkylineTheme.DrawWeatherGlyph(g, _glyphBounds, _glyph);
        }

        if (_accentBounds.Width > 0)
        {
            using var path = SkylineTheme.RoundedRect(_accentBounds, _accentBounds.Height / 2f);
            using var brush = new SolidBrush(_accent);
            g.FillPath(brush, path);
        }
    }

    private static Label MakeLabel(Font font, Color color) => new()
    {
        AutoSize = false,
        BackColor = SkylineTheme.Surface,
        Font = font,
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleCenter,
        UseMnemonic = false,
    };
}
