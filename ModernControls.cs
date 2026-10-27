using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Voltura.NetOn;

internal static class AppTheme
{
    public static readonly Color Paper = Color.FromArgb(247, 248, 242);
    public static readonly Color Ink = Color.FromArgb(23, 44, 34);
    public static readonly Color Muted = Color.FromArgb(99, 115, 102);
    public static readonly Color Green = Color.FromArgb(29, 101, 68);
    public static readonly Color Red = Color.FromArgb(166, 61, 55);
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color Accent { get; set; } = AppTheme.Green;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Quiet { get; set; }
    private bool hovered;
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        Padding = new Padding(17, 12, 17, 12);
        AutoSize = false; Size = new Size(160, 46); MinimumSize = new Size(110, 40);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        using var shape = AppTheme.Rounded(rect, 9 * DeviceDpi / 96f);
        Color fill = !Enabled ? Color.FromArgb(234, 237, 229) : Quiet ? (hovered ? Color.FromArgb(228, 235, 220) : AppTheme.Paper) : hovered ? ControlPaint.Dark(Accent, .12f) : Accent;
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, shape);
        if (Quiet) { using var border = new Pen(Color.FromArgb(206, 216, 199)); e.Graphics.DrawPath(border, shape); }
        var textColor = !Enabled ? Color.FromArgb(137, 147, 133) : Quiet ? AppTheme.Ink : Color.White;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(rect), textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), textColor, fill);
    }
}

internal sealed class SwitchGlyph : Control
{
    private bool? state;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool? State { get => state; set { state = value; Invalidate(); } }
    public SwitchGlyph() { DoubleBuffered = true; Size = new Size(130, 80); AccessibleName = "Internet access state"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var bitmap = StatusIcons.Draw(State, new AppSettings(), Math.Max(Width, Height));
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(bitmap, new Rectangle((Width - Height) / 2, 0, Height, Height));
    }
}

internal sealed class ArtworkPanel : Control
{
    private readonly Image artwork;
    public ArtworkPanel()
    {
        DoubleBuffered = true;
        using var stream = typeof(ArtworkPanel).Assembly.GetManifestResourceStream("Voltura.NetOn.Artwork")!;
        using var image = Image.FromStream(stream);
        artwork = new Bitmap(image);
        Dock = DockStyle.Fill; Margin = new Padding(0, 0, 30, 0);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var clip = AppTheme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 18 * DeviceDpi / 96f);
        var old = e.Graphics.Save(); e.Graphics.SetClip(clip);
        float scale = Math.Max(Width / (float)artwork.Width, Height / (float)artwork.Height);
        float w = artwork.Width * scale, h = artwork.Height * scale;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(artwork, (Width - w) / 2, (Height - h) / 2, w, h);
        float unit = DeviceDpi / 96f;
        using var font = new Font("Segoe UI", 17, FontStyle.Regular);
        using var brush = new SolidBrush(Color.FromArgb(245, 249, 230));
        e.Graphics.DrawString("Your connection.\nYour choice.", font, brush, 18 * unit, Height - 94 * unit);
        using var small = new Font("Segoe UI", 8, FontStyle.Bold);
        e.Graphics.DrawString("A LITTLE MORE CONTROL", small, brush, 18 * unit, 22 * unit);
        e.Graphics.Restore(old);
    }
    protected override void Dispose(bool disposing) { if (disposing) artwork.Dispose(); base.Dispose(disposing); }
}
