using System.Runtime.InteropServices;

namespace SnapKit;

internal static class ScreenGrabber
{
    public static PixelBuffer CaptureRect(int x, int y, int width, int height)
    {
        if (width < 1 || height < 1)
            return new PixelBuffer(1, 1);

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memory = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memory = NativeMethods.CreateCompatibleDC(screen);
            bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
            previous = NativeMethods.SelectObject(memory, bitmap);
            NativeMethods.BitBlt(memory, 0, 0, width, height, screen, x, y, NativeMethods.SrcCopy);
            // GetDIBits fails when the bitmap is still selected into a DC.
            NativeMethods.SelectObject(memory, previous);
            previous = IntPtr.Zero;

            var info = new NativeMethods.BitmapInfo
            {
                bmiHeader = new NativeMethods.BitmapInfoHeader
                {
                    Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    SizeImage = width * height * 4
                }
            };
            var pixels = new byte[width * height * 4];
            NativeMethods.GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0);
            var buffer = new PixelBuffer(width, height, pixels);
            buffer.ForceOpaque();
            return buffer;
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero)
                NativeMethods.SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(bitmap);
            if (memory != IntPtr.Zero)
                NativeMethods.DeleteDC(memory);
            if (screen != IntPtr.Zero)
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static IReadOnlyList<MonitorShot> CaptureAll()
    {
        var monitors = MonitorQuery.All();
        if (monitors.Count == 0)
            return Array.Empty<MonitorShot>();

        int left = monitors.Min(m => m.X);
        int top = monitors.Min(m => m.Y);
        int right = monitors.Max(m => m.Right);
        int bottom = monitors.Max(m => m.Bottom);
        var desktop = CaptureRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        var shots = new List<MonitorShot>(monitors.Count);
        foreach (var monitor in monitors)
        {
            var crop = desktop.Crop(monitor.X - left, monitor.Y - top, monitor.Width, monitor.Height);
            shots.Add(new MonitorShot(monitor, crop));
        }
        return shots;
    }
}
