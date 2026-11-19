namespace SnapKit;

/// <summary>A rectangle in one coordinate space. X and Y are the top-left.</summary>
public readonly record struct OcrBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Maps an OCR word from the bitmap the engine saw onto the picture the user is looking at.
/// Windows OCR reports pixels of that bitmap, from its top-left. The capture, a DPI-scaled
/// decode, and the on-screen view are three different sizes, so the conversion lives here.
/// </summary>
public static class OcrMap
{
    public static OcrBox ToImagePixels(OcrBox engine, int bitmapWidth, int bitmapHeight, int imageWidth, int imageHeight)
    {
        double sx = imageWidth / (double)Math.Max(1, bitmapWidth);
        double sy = imageHeight / (double)Math.Max(1, bitmapHeight);
        return new OcrBox(engine.X * sx, engine.Y * sy, Math.Max(0, engine.Width * sx), Math.Max(0, engine.Height * sy));
    }

    /// <summary>
    /// Place an image-pixel box onto the surface that shows the capture.
    /// <paramref name="viewWidth"/> and <paramref name="viewHeight"/> are that surface
    /// in the same units as <paramref name="originX"/> and <paramref name="originY"/>
    /// (DIPs for a WPF canvas, physical pixels for the desktop). The origin is the
    /// image's top-left in that space: zero when the canvas is the image, or the
    /// virtual-screen position when the image sits on a monitor.
    /// </summary>
    public static OcrBox ToView(OcrBox imagePixels, int imageWidth, int imageHeight, double viewWidth, double viewHeight, double originX, double originY)
    {
        double sx = viewWidth / Math.Max(1, imageWidth);
        double sy = viewHeight / Math.Max(1, imageHeight);
        return new OcrBox(
            originX + imagePixels.X * sx,
            originY + imagePixels.Y * sy,
            Math.Max(0, imagePixels.Width * sx),
            Math.Max(0, imagePixels.Height * sy));
    }

    public static OcrBox FromView(OcrBox view, int imageWidth, int imageHeight, double viewWidth, double viewHeight, double originX, double originY)
    {
        double sx = Math.Max(1, imageWidth) / Math.Max(1e-6, viewWidth);
        double sy = Math.Max(1, imageHeight) / Math.Max(1e-6, viewHeight);
        return new OcrBox((view.X - originX) * sx, (view.Y - originY) * sy, Math.Max(0, view.Width * sx), Math.Max(0, view.Height * sy));
    }
}
