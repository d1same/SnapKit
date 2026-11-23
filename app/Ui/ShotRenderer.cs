using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal static class ShotRenderer
{
    public static BitmapSource Render(ShotDocument document)
    {
        var underlay = document.ComposeUnderlay();
        int width = Math.Max(1, underlay.Width);
        int height = Math.Max(1, underlay.Height);
        var pixels = new byte[width * height * 4];
        int count = Math.Min(pixels.Length, underlay.Pixels.Length);
        Buffer.BlockCopy(underlay.Pixels, 0, pixels, 0, count);

        // Marks are painted on their own layer and blended on top. Drawing the photo
        // through a render target shifts its colors.
        if (HasMarks(document))
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-document.View.X, -document.View.Y));
                foreach (var annotation in document.Annotations)
                {
                    if (annotation.Kind is AnnotationKind.Blur or AnnotationKind.Spotlight)
                        continue;
                    AnnotationPainter.Draw(dc, annotation);
                }
                AnnotationPainter.DrawSpotlight(dc, document, null);
                dc.Pop();
            }

            var layer = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            layer.Render(visual);
            var marks = new byte[width * height * 4];
            layer.CopyPixels(marks, width * 4, 0);
            BlendOver(pixels, marks);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();

        return document.Backdrop == BackdropPreset.None
            ? bitmap
            : ApplyBackdrop(bitmap, document.Backdrop);
    }

    public static (Brush Background, Brush Border) PreviewBrushes(BackdropPreset preset) => preset switch
    {
        BackdropPreset.Clean => (
            Gradient(Color.FromRgb(0xF8, 0xFA, 0xFC), Color.FromRgb(0xE8, 0xEE, 0xF5)),
            Palette.Freeze(0xCB, 0xD5, 0xE1)),
        BackdropPreset.Dark => (
            Gradient(Color.FromRgb(0x1E, 0x20, 0x28), Color.FromRgb(0x0E, 0x10, 0x16)),
            Palette.Freeze(0xFF, 0xFF, 0xFF, 0x22)),
        BackdropPreset.Slate => (
            Gradient(Color.FromRgb(0x3B, 0x4C, 0x63), Color.FromRgb(0x1E, 0x29, 0x3B)),
            Palette.Freeze(0xFF, 0xFF, 0xFF, 0x2A)),
        _ => (Brushes.Transparent, Brushes.Transparent)
    };

    private static BitmapSource ApplyBackdrop(BitmapSource shot, BackdropPreset preset)
    {
        var metrics = BackdropLayout.Measure(preset, shot.PixelWidth, shot.PixelHeight);
        var (bg, borderBrush) = PreviewBrushes(preset);
        var shotRect = new Rect(metrics.PadX, metrics.PadTop, shot.PixelWidth, shot.PixelHeight);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Outer documentation canvas background
            dc.DrawRectangle(bg, null, new Rect(0, 0, metrics.TotalWidth, metrics.TotalHeight));

            // Soft layered drop shadow behind the screenshot card
            var shadowBase = preset == BackdropPreset.Clean
                ? Color.FromRgb(0x0F, 0x17, 0x2A)
                : Color.FromRgb(0x00, 0x00, 0x00);
            double shadowStrength = preset == BackdropPreset.Clean ? 0.16 : 0.42;
            DrawSoftShadow(dc, shotRect, metrics.CornerRadius, shadowBase, shadowStrength);

            // Clipped rounded screenshot
            var clip = new RectangleGeometry(shotRect, metrics.CornerRadius, metrics.CornerRadius);
            clip.Freeze();
            dc.PushClip(clip);
            dc.DrawImage(shot, shotRect);
            dc.Pop();

            // Crisp 1px framing stroke around screenshot edge
            var borderPen = new Pen(borderBrush, 1);
            borderPen.Freeze();
            dc.DrawRoundedRectangle(null, borderPen, new Rect(shotRect.X + 0.5, shotRect.Y + 0.5, Math.Max(1, shotRect.Width - 1), Math.Max(1, shotRect.Height - 1)), metrics.CornerRadius, metrics.CornerRadius);
        }

        var target = new RenderTargetBitmap(metrics.TotalWidth, metrics.TotalHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static void DrawSoftShadow(DrawingContext dc, Rect rect, double radius, Color baseColor, double maxOpacity)
    {
        const int layers = 6;
        for (int i = layers; i >= 1; i--)
        {
            double t = i / (double)layers;
            double spread = i * 2.4;
            double offsetY = i * 1.9;
            byte alpha = (byte)Math.Clamp(Math.Round(255 * maxOpacity * (1.0 - t * 0.72) / 3.2), 2, 160);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            var shadowRect = new Rect(
                rect.X - spread * 0.65,
                rect.Y + offsetY - spread * 0.25,
                rect.Width + spread * 1.3,
                rect.Height + spread * 1.0);
            dc.DrawRoundedRectangle(brush, null, shadowRect, radius + spread * 0.4, radius + spread * 0.4);
        }
    }

    private static LinearGradientBrush Gradient(Color topLeft, Color bottomRight)
    {
        var brush = new LinearGradientBrush(topLeft, bottomRight, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private static bool HasMarks(ShotDocument document)
    {
        foreach (var annotation in document.Annotations)
        {
            if (annotation.Kind != AnnotationKind.Blur)
                return true;
        }
        return false;
    }

    private static void BlendOver(byte[] dest, byte[] src)
    {
        int length = Math.Min(dest.Length, src.Length);
        for (int i = 0; i < length; i += 4)
        {
            int alpha = src[i + 3];
            if (alpha == 0)
                continue;
            if (alpha == 255)
            {
                dest[i] = src[i];
                dest[i + 1] = src[i + 1];
                dest[i + 2] = src[i + 2];
                dest[i + 3] = 255;
                continue;
            }
            int keep = 255 - alpha;
            dest[i] = (byte)(src[i] + (dest[i] * keep + 127) / 255);
            dest[i + 1] = (byte)(src[i + 1] + (dest[i + 1] * keep + 127) / 255);
            dest[i + 2] = (byte)(src[i + 2] + (dest[i + 2] * keep + 127) / 255);
            dest[i + 3] = 255;
        }
    }
}
