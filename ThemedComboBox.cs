using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Voltura.NetOn;

// Keep the native dropdown, keyboard input and accessibility; paint only its closed face.
internal sealed class ThemedComboBox : ComboBox
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintData
    {
        public IntPtr Hdc;
        public int Erase, Left, Top, Right, Bottom, Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintData data);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintData data);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x14) { m.Result = (IntPtr)1; return; }
        if (m.Msg is 0x0f or 0x317 or 0x318)
        {
            bool painting = m.Msg == 0x0f;
            PaintData data = default;
            IntPtr dc = painting ? BeginPaint(Handle, out data) : m.WParam;
            try { using var graphics = Graphics.FromHdc(dc); PaintFace(graphics); }
            finally { if (painting) EndPaint(Handle, ref data); }
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
        if (m.Msg is 0x7 or 0x8 or 0xA or 0x14e or 0x14f) Invalidate();
    }

    private void PaintFace(Graphics graphics)
    {
        graphics.Clear(AppTheme.Surface);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var border = new Pen(Focused ? AppTheme.Positive : AppTheme.Border, Math.Max(1, scale));
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        Color ink = Enabled ? AppTheme.Ink : AppTheme.Muted;
        int right = Width - (int)(28 * scale);
        string text = SelectedItem?.ToString() ?? "Choose a connection";
        TextRenderer.DrawText(graphics, text, Font, new Rectangle((int)(10 * scale), 0, Math.Max(1, right - (int)(14 * scale)), Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        float x = Width - 14 * scale, y = Height / 2f, span = 4 * scale;
        using var chevron = new Pen(ink, Math.Max(1, scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(chevron, [new PointF(x - span, y - span / 2), new PointF(x, y + span / 2), new PointF(x + span, y - span / 2)]);
    }
}
