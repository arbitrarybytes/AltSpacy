using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace AltSpacy;

/// <summary>
/// The AltSpacy logo: a search bar (pill) with an arrow taking off from it, i.e. "type, then go",
/// on a coral-to-magenta tile. Drawn as vectors so the same source produces the window icon,
/// tray icon and the exe .ico.
/// </summary>
public static class Logo
{
    public static readonly int[] IcoSizes = [16, 20, 24, 32, 48, 64, 256];

    public static DrawingImage Image { get; } = Create();

    private static DrawingImage Create()
    {
        var tile = new LinearGradientBrush(Color.FromRgb(0xFF, 0x6A, 0x3D), Color.FromRgb(0xC0, 0x26, 0xD3), new Point(0, 0), new Point(1, 1));
        var arrow = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xE7, 0xA0)), 26)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(new Rect(0, 0, 256, 256), 60, 60)));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(44, 160, 120, 40), 20, 20)));
        group.Children.Add(new GeometryDrawing(null, arrow, Geometry.Parse("M 118,136 L 196,58 M 144,58 L 196,58 L 196,110")));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public static BitmapSource Render(int size) => Render(Image, size);

    public static BitmapSource Render(ImageSource source, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(size)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Writes a multi-resolution .ico with PNG-compressed entries.</summary>
    public static void WriteIco(Stream output, IReadOnlyList<int> sizes)
    {
        var images = sizes.Select(s => (Size: s, Data: Png(s))).ToList();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Count);

        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            var dimension = (byte)(size >= 256 ? 0 : size);
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) writer.Write(data);
    }

    public static Drawing.Icon CreateTrayIcon()
    {
        var size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = new Drawing.Bitmap(new MemoryStream(Png(size)));
        var handle = bitmap.GetHicon();
        try
        {
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
