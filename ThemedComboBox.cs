using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Voltura.NetOn;

// Keep the native dropdown, keyboard input and accessibility; paint only its closed face.
internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FormattingEnabled = true;
        AutoSize = true;
        IntegralHeight = false;
        MaxDropDownItems = 8;
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
    protected override void OnDropDown(EventArgs e) { UpdateMetrics(); base.OnDropDown(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size preferred = base.GetPreferredSize(proposedSize);
        // TableLayoutPanel must reserve the real owner-drawn field height, not just the font height.
        return new Size(preferred.Width, Math.Max(preferred.Height, Height));
    }

    private void UpdateMetrics()
    {
        if (!IsHandleCreated) return;
        // ItemHeight is in physical pixels; the font is already scaled by WinForms.
        int rowHeight = Math.Max((int)Math.Ceiling(32 * DeviceDpi / 96f), Font.Height + (int)Math.Ceiling(12 * DeviceDpi / 96f));
        ItemHeight = rowHeight; // Updates both the native selection field and list rows.
        Height = PreferredHeight;
        DropDownHeight = rowHeight * MaxDropDownItems + 2;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? AppTheme.Green : AppTheme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        string text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) ?? "" : Text;
        var bounds = Rectangle.Inflate(e.Bounds, -(int)Math.Ceiling(10 * DeviceDpi / 96f), 0);
        TextRenderer.DrawText(e.Graphics, text, Font, bounds, selected ? Color.White : AppTheme.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        base.OnDrawItem(e);
    }

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

    internal void PaintFace(Graphics graphics)
    {
        using var background = new SolidBrush(AppTheme.Surface);
        graphics.FillRectangle(background, ClientRectangle);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var border = new Pen(Focused ? AppTheme.Positive : AppTheme.Border, Math.Max(1, scale));
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        Color ink = Enabled ? AppTheme.Ink : AppTheme.Muted;
        int right = Width - (int)(28 * scale);
        string text = SelectedItem is null ? "Choose a connection" : GetItemText(SelectedItem) ?? "";
        TextRenderer.DrawText(graphics, text, Font, new Rectangle((int)(10 * scale), 0, Math.Max(1, right - (int)(14 * scale)), Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        float x = Width - 14 * scale, y = Height / 2f, span = 4 * scale;
        using var chevron = new Pen(ink, Math.Max(1, scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(chevron, [new PointF(x - span, y - span / 2), new PointF(x, y + span / 2), new PointF(x + span, y - span / 2)]);
    }
}
