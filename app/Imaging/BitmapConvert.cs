using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal static class BitmapConvert
{
    public static BitmapSource ToBitmapSource(PixelBuffer buffer)
    {
        var copy = new byte[buffer.Width * buffer.Height * 4];
        Buffer.BlockCopy(buffer.Pixels, 0, copy, 0, copy.Length);
        var source = BitmapSource.Create(
            buffer.Width,
            buffer.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            copy,
            buffer.Stride);
        source.Freeze();
        return source;
    }

    public static PixelBuffer ToPixelBuffer(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        var buffer = new PixelBuffer(width, height, pixels);
        buffer.ForceOpaque();
        return buffer;
    }
}
