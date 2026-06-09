namespace ClientAppsIntegration.WinForms;

/// <summary>
/// A real <see cref="Button"/> (so keyboard focus, mnemonics and UI Automation keep working)
/// painted as a rounded warm-coral pill with a visible keyboard focus ring.
/// </summary>
internal sealed class PrimaryButton : Button
{
    private bool _hover;
    private bool _down;

    public PrimaryButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        BackColor = SkylineTheme.Surface;
        ForeColor = SkylineTheme.TextOnAccent;
        Font = SkylineTheme.ButtonFont;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        SkylineTheme.EnableQuality(g);
        using (var bg = new SolidBrush(BackColor))
        {
            g.FillRectangle(bg, ClientRectangle);
        }

        var rect = new RectangleF(1, 1, Width - 2, Height - 2);
        var radius = Math.Min(14f, rect.Height / 2f);

        var fill =
            !Enabled ? SkylineTheme.ButtonDisabled :
            _down ? SkylineTheme.ButtonDown :
            _hover ? SkylineTheme.ButtonHover :
            SkylineTheme.ButtonFill;

        using (var path = SkylineTheme.RoundedRect(rect, radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            SkylineTheme.TextOnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (Focused && Enabled)
        {
            var ring = RectangleF.Inflate(rect, -2.5f, -2.5f);
            using var pen = new Pen(Color.FromArgb(210, SkylineTheme.TextOnAccent), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            using var path = SkylineTheme.RoundedRect(ring, radius - 2.5f);
            g.DrawPath(pen, path);
        }
    }
}
