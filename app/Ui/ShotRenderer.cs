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
        return bitmap;
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
