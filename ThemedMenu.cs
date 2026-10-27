using System.Drawing.Drawing2D;

namespace Voltura.NetOn;

// Match Worlds' flat surfaces, comfortable rows, muted separators, and clear selected items.
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var fill = new SolidBrush(AppTheme.Surface); e.Graphics.FillRectangle(fill, e.AffectedBounds);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = AppTheme.Rounded(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 5 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f);
        using var fill = new SolidBrush(AppTheme.Hover); e.Graphics.FillPath(fill, shape);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? AppTheme.Ink : AppTheme.Muted;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Border);
        e.Graphics.DrawLine(pen, 13, e.Item.Height / 2, e.Item.Width - 13, e.Item.Height / 2);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = AppTheme.Muted; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using var pen = new Pen(AppTheme.Positive, Math.Max(1.5f, r.Height / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLines(pen, [new PointF(r.Left + r.Width * .18f, r.Top + r.Height * .5f), new PointF(r.Left + r.Width * .42f, r.Top + r.Height * .73f), new PointF(r.Left + r.Width * .83f, r.Top + r.Height * .25f)]);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Border); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color CheckBackground => AppTheme.Hover;
        public override Color CheckSelectedBackground => AppTheme.Hover;
        public override Color ImageMarginGradientBegin => AppTheme.Surface;
        public override Color ImageMarginGradientMiddle => AppTheme.Surface;
        public override Color ImageMarginGradientEnd => AppTheme.Surface;
    }
}
