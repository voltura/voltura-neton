using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Voltura.NetOn;

internal static class StatusIcons
{
    public static Icon Create(bool? enabled, AppSettings settings, int size = 0)
    {
        int[] sizes = size > 0 ? [size] : [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = sizes.Select(frameSize =>
        {
            using var bitmap = Draw(enabled, settings, frameSize);
            using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png); return png.ToArray();
        }).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        stream.Position = 0;
        return new Icon(stream, size > 0 ? size : 64, size > 0 ? size : 64);
    }

    internal static Bitmap Draw(bool? enabled, AppSettings settings, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = enabled switch { true => Color.FromArgb(settings.OnBackground), false => Color.FromArgb(settings.OffBackground), null => Color.DimGray };
        Color foreground = Color.FromArgb(enabled == false ? settings.OffForeground : settings.OnForeground);
        graphics.Clear(Color.Transparent);
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var body = new RectangleF(size * .025f, size * .235f, size * .95f, size * .53f);
        using var shape = AppTheme.Rounded(body, body.Height / 2);
        using var fill = new LinearGradientBrush(body, ControlPaint.Light(background, .10f), ControlPaint.Dark(background, .14f), LinearGradientMode.Vertical);
        graphics.FillPath(fill, shape);
        using var rim = new Pen(Color.FromArgb(180, foreground), Math.Max(.8f, size * .018f));
        graphics.DrawPath(rim, shape);
        float diameter = size * .44f;
        float x = enabled switch { true => size * .50f, false => size * .06f, null => size * .28f };
        var thumb = new RectangleF(x, (size - diameter) / 2, diameter, diameter);
        Color thumbLight = Color.FromArgb(foreground.R, foreground.G, (int)(foreground.B * .95));
        Color thumbShade = Color.FromArgb((int)(foreground.R * .87), (int)(foreground.G * .87), (int)(foreground.B * .79));
        using var thumbFill = new LinearGradientBrush(thumb, thumbLight, thumbShade, LinearGradientMode.ForwardDiagonal);
        graphics.FillEllipse(thumbFill, thumb);
        if (size >= 32)
        {
            using var power = new Pen(Color.FromArgb(142, 136, 108), Math.Max(1, size * .023f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var ring = new RectangleF(thumb.X + diameter * .29f, thumb.Y + diameter * .29f, diameter * .42f, diameter * .42f);
            graphics.DrawArc(power, ring, -45, 270);
            graphics.DrawLine(power, thumb.X + diameter / 2, thumb.Y + diameter * .23f, thumb.X + diameter / 2, thumb.Y + diameter * .49f);
        }
        return bitmap;
    }
}
