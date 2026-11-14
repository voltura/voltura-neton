using System.Drawing.Drawing2D;

namespace Voltura.NetOn;

internal sealed class ThemedCheckBox : CheckBox
{
    private const TextFormatFlags LabelFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    private bool hovered;

    public ThemedCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UseVisualStyleBackColor = false;
        AutoSize = true;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, LabelFlags);
        return new Size(Scale(26) + text.Width + Padding.Horizontal + Scale(2),
            Math.Max(Scale(18), text.Height) + Padding.Vertical + Scale(2));
    }

    private int Scale(int logicalPixels) => (int)Math.Ceiling(logicalPixels * DeviceDpi / 96f);

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        // ButtonBase is opaque, so WinForms skips OnPaintBackground on screen.
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);

        float scale = DeviceDpi / 96f;
        float size = 16 * scale;
        int contentHeight = Height - Padding.Vertical;
        var box = new RectangleF(Padding.Left + scale, Padding.Top + (contentHeight - size) / 2, size, size);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = AppTheme.Rounded(box, 3 * scale);
        using var fill = new SolidBrush(!Enabled ? AppTheme.Disabled : Checked ? AppTheme.Green : AppTheme.Surface);
        e.Graphics.FillPath(fill, shape);
        using var border = new Pen(hovered && Enabled ? AppTheme.Positive : Checked ? AppTheme.Green : AppTheme.Border, Math.Max(1, scale));
        e.Graphics.DrawPath(border, shape);
        if (Checked)
        {
            using var tick = new Pen(Enabled ? Color.White : AppTheme.Muted, 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.DrawLines(tick, [new PointF(box.Left + box.Width * .22f, box.Top + box.Height * .51f), new PointF(box.Left + box.Width * .43f, box.Top + box.Height * .71f), new PointF(box.Left + box.Width * .79f, box.Top + box.Height * .29f)]);
        }
        int textLeft = Padding.Left + Scale(26);
        var textBounds = new Rectangle(textLeft, Padding.Top, Math.Max(0, Width - textLeft - Padding.Right), contentHeight);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : AppTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | LabelFlags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(textBounds, -1, -1), ForeColor, BackColor);
    }
}
