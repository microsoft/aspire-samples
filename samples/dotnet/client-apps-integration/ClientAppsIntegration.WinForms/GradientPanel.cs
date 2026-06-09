namespace ClientAppsIntegration.WinForms;

/// <summary>
/// A double-buffered panel that paints a slice of the form-wide vertical dawn gradient,
/// keeping the background continuous across nested containers without relying on
/// (DrawToBitmap-unfriendly) transparent backgrounds.
/// </summary>
internal class GradientPanel : Panel
{
    public GradientPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Background is painted in OnPaint so the gradient is captured by DrawToBitmap.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintBackdrop(e.Graphics);
        base.OnPaint(e);
    }

    protected void PaintBackdrop(Graphics g) => SkylineTheme.PaintBackdrop(this, g);
}

/// <summary>
/// A <see cref="TableLayoutPanel"/> that paints the same continuous gradient backdrop, so the
/// gaps between forecast cards show the dawn gradient (rather than default control grey) and are
/// reliably captured by DrawToBitmap.
/// </summary>
internal sealed class GradientTableLayoutPanel : TableLayoutPanel
{
    public GradientTableLayoutPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Painted in OnPaint instead, for DrawToBitmap fidelity.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        SkylineTheme.PaintBackdrop(this, e.Graphics);
        base.OnPaint(e);
    }
}
