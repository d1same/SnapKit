using System.Globalization;
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
            BitmapSource? underlayBitmap = null;
            if (document.Annotations.Any(a => a.Kind == AnnotationKind.Magnifier))
            {
                underlayBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                underlayBitmap.Freeze();
            }

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-document.View.X, -document.View.Y));
                foreach (var annotation in document.Annotations)
                {
                    if (annotation.Kind is AnnotationKind.Blur or AnnotationKind.Spotlight)
                        continue;
                    AnnotationPainter.Draw(dc, annotation, hideText: false, underlay: underlayBitmap, view: document.View);
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

        string? stamp = document.ShowStamp
            ? TicketStamp.Format(DateTime.Now, document.StampNote, Environment.MachineName)
            : null;

        if (document.Backdrop != BackdropPreset.None)
            return ApplyBackdrop(bitmap, document.Backdrop, stamp);

        return stamp != null
            ? ApplyStampFooter(bitmap, stamp)
            : bitmap;
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

    private static BitmapSource ApplyBackdrop(BitmapSource shot, BackdropPreset preset, string? stamp)
    {
        var metrics = BackdropLayout.Measure(preset, shot.PixelWidth, shot.PixelHeight);
        int padBottom = stamp != null ? Math.Max(metrics.PadBottom, 34) : metrics.PadBottom;
        int totalH = shot.PixelHeight + metrics.PadTop + padBottom;
        var (bg, borderBrush) = PreviewBrushes(preset);
        var shotRect = new Rect(metrics.PadX, metrics.PadTop, shot.PixelWidth, shot.PixelHeight);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Outer documentation canvas background
            dc.DrawRectangle(bg, null, new Rect(0, 0, metrics.TotalWidth, totalH));

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

            // Subtle proof timestamp in bottom margin of the frame
            if (!string.IsNullOrWhiteSpace(stamp))
            {
                var ink = preset == BackdropPreset.Clean
                    ? Palette.Freeze(0x47, 0x55, 0x69)
                    : Palette.Freeze(0xD4, 0xD4, 0xD8, 0xCC);
                var ft = new FormattedText(
                    stamp,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(Palette.Mono, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
                    11,
                    ink,
                    1.0);
                double tx = Math.Max(metrics.PadX, shotRect.Right - ft.Width);
                double ty = shotRect.Bottom + Math.Max(6, (padBottom - ft.Height) * 0.5);
                dc.DrawText(ft, new Point(tx, ty));
            }
        }

        var target = new RenderTargetBitmap(metrics.TotalWidth, totalH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource ApplyStampFooter(BitmapSource shot, string stamp)
    {
        const int footerH = 28;
        int w = shot.PixelWidth;
        int totalH = shot.PixelHeight + footerH;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(shot, new Rect(0, 0, w, shot.PixelHeight));
            dc.DrawRectangle(Palette.Group, null, new Rect(0, shot.PixelHeight, w, footerH));
            var linePen = new Pen(Palette.Line, 1);
            linePen.Freeze();
            dc.DrawLine(linePen, new Point(0, shot.PixelHeight + 0.5), new Point(w, shot.PixelHeight + 0.5));

            var ft = new FormattedText(
                stamp,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(Palette.Mono, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
                11,
                Palette.Muted,
                1.0);
            double tx = Math.Max(10, w - ft.Width - 10);
            double ty = shot.PixelHeight + Math.Max(4, (footerH - ft.Height) * 0.5);
            dc.DrawText(ft, new Point(tx, ty));
        }

        var target = new RenderTargetBitmap(w, totalH, 96, 96, PixelFormats.Pbgra32);
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
