using System.ComponentModel;

namespace ClientAppsIntegration.WinForms;

/// <summary>
/// An opaque white rounded "surface" card with a painted soft shadow and hairline border.
/// Text controls placed inside <see cref="ContentRectangle"/> sit on solid white (AAA contrast)
/// while the surrounding gradient remains visible in the shadow margin.
/// </summary>
internal class CardPanel : GradientPanel
{
    private int _cornerRadius = 16;
    private int _shadowSize = 14;
    private int _contentPadding = 18;

    /// <summary>Raised after the card chrome is painted so callers can draw bespoke content (e.g. a glyph).</summary>
    public event Action<Graphics>? ContentPaint;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set { _cornerRadius = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ShadowSize
    {
        get => _shadowSize;
        set { _shadowSize = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentPadding
    {
        get => _contentPadding;
        set { _contentPadding = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Tinted { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TintColor { get; set; } = SkylineTheme.ErrorTint;

    /// <summary>The opaque white card bounds (inside the shadow margin).</summary>
    public RectangleF BodyBounds
    {
        get
        {
            var inset = _shadowSize;
            var w = Math.Max(0, Width - inset * 2);
            var h = Math.Max(0, Height - inset * 2);
            return new RectangleF(inset, inset, w, h);
        }
    }

    /// <summary>The usable content area inside the card, padded away from the rounded edges.</summary>
    public Rectangle ContentRectangle
    {
        get
        {
            var b = BodyBounds;
            var p = _contentPadding;
            return Rectangle.Round(new RectangleF(b.X + p, b.Y + p, Math.Max(0, b.Width - p * 2), Math.Max(0, b.Height - p * 2)));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintBackdrop(e.Graphics);
        SkylineTheme.DrawCard(e.Graphics, BodyBounds, _cornerRadius, Tinted, TintColor);
        OnPaintContent(e.Graphics);
    }

    protected virtual void OnPaintContent(Graphics g) => ContentPaint?.Invoke(g);
}
