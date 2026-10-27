using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Voltura.NetOn;

internal static class AppTheme
{
    public static bool Dark { get; private set; }
    public static Color Paper => Dark ? Color.FromArgb(19, 27, 24) : Color.FromArgb(247, 248, 242);
    public static Color Surface => Dark ? Color.FromArgb(29, 39, 33) : Color.FromArgb(255, 255, 251);
    public static Color Ink => Dark ? Color.FromArgb(237, 243, 232) : Color.FromArgb(23, 44, 34);
    public static Color Muted => Dark ? Color.FromArgb(166, 183, 169) : Color.FromArgb(99, 115, 102);
    public static Color Border => Dark ? Color.FromArgb(63, 79, 66) : Color.FromArgb(206, 216, 199);
    public static Color Hover => Dark ? Color.FromArgb(44, 65, 50) : Color.FromArgb(228, 235, 220);
    public static Color Disabled => Dark ? Color.FromArgb(36, 45, 38) : Color.FromArgb(234, 237, 229);
    public static Color Positive => Dark ? Color.FromArgb(123, 211, 155) : Green;
    public static Color Negative => Dark ? Color.FromArgb(243, 143, 132) : Red;
    public static readonly Color Green = Color.FromArgb(29, 101, 68);
    public static readonly Color Red = Color.FromArgb(166, 61, 55);
    public static bool ResolveDark(string choice, Func<object?>? readPreference = null)
    {
        if (choice == "Dark") return true;
        if (choice == "Light") return false;
        try { return (readPreference ?? (() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)))() is int value && value == 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    public static void Set(string choice) => Dark = ResolveDark(choice);
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

internal class ModernButton : Button
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
    protected override void OnPaintBackground(PaintEventArgs e) { using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle); }
    protected override void OnPaint(PaintEventArgs e)
    {
        // ButtonBase's native background can leave black pixels outside our rounded face.
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(1, 1, Width - 3, Height - 3);
        using var shape = AppTheme.Rounded(rect, 9 * DeviceDpi / 96f);
        Color fill = !Enabled ? AppTheme.Disabled : Quiet ? (hovered ? AppTheme.Hover : AppTheme.Paper) : hovered ? ControlPaint.Dark(Accent, .12f) : Accent;
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, shape);
        if (Quiet) { using var border = new Pen(AppTheme.Border); e.Graphics.DrawPath(border, shape); }
        var textColor = !Enabled ? AppTheme.Muted : Quiet ? AppTheme.Ink : Color.White;
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(rect), textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), textColor, fill);
    }
}

internal sealed class CopyButton : ModernButton
{
    public CopyButton()
    {
        Quiet = true; MinimumSize = Size.Empty; Size = new Size(34, 30);
        AccessibleName = "Copy external IP address";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = Math.Min(Width / 34f, Height / 30f);
        float left = (Width - 16 * scale) / 2, top = (Height - 18 * scale) / 2;
        using var pen = new Pen(Enabled ? AppTheme.Ink : AppTheme.Muted, Math.Max(1, 1.4f * scale));
        e.Graphics.DrawRectangle(pen, left + 4 * scale, top, 12 * scale, 14 * scale);
        e.Graphics.DrawLines(pen, [new PointF(left + 2 * scale, top + 4 * scale), new PointF(left, top + 4 * scale), new PointF(left, top + 18 * scale), new PointF(left + 12 * scale, top + 18 * scale), new PointF(left + 12 * scale, top + 16 * scale)]);
    }
}

internal sealed class SwitchGlyph : Button
{
    private bool? state;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool? State { get => state; set { state = value; AccessibleName = value == true ? "Turn internet off" : "Turn internet on"; AccessibleDescription = value is null ? "Reading network status" : $"Internet is {(value.Value ? "on" : "off")}"; Invalidate(); } }
    public SwitchGlyph()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(130, 80); Cursor = Cursors.Hand;
        AccessibleName = "Toggle internet access";
    }
    protected override void OnPaintBackground(PaintEventArgs e) { using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle); }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        using var bitmap = StatusIcons.Draw(State, new AppSettings(), Math.Max(Width, Height));
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(bitmap, new Rectangle((Width - Height) / 2, 0, Height, Height));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), AppTheme.Ink, BackColor);
    }
}

internal sealed class ArtworkPanel : Control
{
    private readonly Image artwork;
    public ArtworkPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
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
        e.Graphics.DrawString("Internet on.\nInternet off.", font, brush, 18 * unit, Height - 94 * unit);
        using var small = new Font("Segoe UI", 8, FontStyle.Bold);
        e.Graphics.DrawString("NETWORK ACCESS", small, brush, 18 * unit, 22 * unit);
        e.Graphics.Restore(old);
    }
    protected override void Dispose(bool disposing) { if (disposing) artwork.Dispose(); base.Dispose(disposing); }
}
