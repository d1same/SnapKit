using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal static class ShotRenderer
{
    public static BitmapSource Render(ShotDocument document)
    {
        var underlay = document.ComposeUnderlay();
        var image = BitmapConvert.ToBitmapSource(underlay);
        int width = Math.Max(1, underlay.Width);
        int height = Math.Max(1, underlay.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
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

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
