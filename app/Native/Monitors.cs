using System.Runtime.InteropServices;

namespace SnapKit;

internal sealed class MonitorInfo
{
    public IntPtr Handle { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public uint DpiX { get; init; } = 96;
    public bool Primary { get; init; }
    public double Scale => (DpiX == 0 ? 96u : DpiX) / 96.0;
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

internal sealed class MonitorShot
{
    public MonitorShot(MonitorInfo monitor, PixelBuffer pixels)
    {
        Monitor = monitor;
        Pixels = pixels;
    }

    public MonitorInfo Monitor { get; }
    public PixelBuffer Pixels { get; }
}

internal static class MonitorQuery
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.MonitorEnumProc callback = (IntPtr monitor, IntPtr _, IntPtr rectPtr, IntPtr _) =>
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
            };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return true;

            uint dpiX = 96, dpiY = 96;
            try
            {
                NativeMethods.GetDpiForMonitor(monitor, 0, out dpiX, out dpiY);
            }
            catch
            {
                dpiX = 96;
            }

            var bounds = info.Monitor;
            if (rectPtr != IntPtr.Zero && bounds.Width <= 0)
                bounds = Marshal.PtrToStructure<NativeMethods.Rect>(rectPtr);

            list.Add(new MonitorInfo
            {
                Handle = monitor,
                X = bounds.Left,
                Y = bounds.Top,
                Width = Math.Max(1, bounds.Width),
                Height = Math.Max(1, bounds.Height),
                DpiX = dpiX == 0 ? 96 : dpiX,
                Primary = (info.Flags & 1) != 0
            });
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        if (list.Count == 0)
        {
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
            list.Add(new MonitorInfo
            {
                X = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen),
                Y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen),
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                DpiX = 96,
                Primary = true
            });
        }
        return list;
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        var all = All();
        var handle = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        return all.FirstOrDefault(m => m.Handle == handle)
            ?? all.FirstOrDefault(m => x >= m.X && x < m.Right && y >= m.Y && y < m.Bottom)
            ?? all[0];
    }

    public static MonitorInfo UnderCursor()
    {
        NativeMethods.GetCursorPos(out var point);
        return FromPoint(point.X, point.Y);
    }
}
