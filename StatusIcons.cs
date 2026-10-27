using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Voltura.NetOn;

internal static class StatusIcons
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    public static Icon Create(bool? enabled, AppSettings settings, int size = 64)
    {
        using var bitmap = Draw(enabled, settings, size);
        IntPtr handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    internal static Bitmap Draw(bool? enabled, AppSettings settings, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = enabled switch { true => Color.FromArgb(settings.OnBackground), false => Color.FromArgb(settings.OffBackground), null => Color.DimGray };
        Color foreground = Color.FromArgb(enabled == false ? settings.OffForeground : settings.OnForeground);
        graphics.Clear(background);
        using var pen = new Pen(foreground, Math.Max(1, size / 32f));
        graphics.DrawRectangle(pen, 1, 1, size - 3, size - 3);
        using var font = new Font(FontFamily.GenericMonospace, size * (enabled == false ? .40f : .54f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(foreground);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(enabled switch { true => "ON", false => "OFF", null => "?" }, font, brush, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }
}
