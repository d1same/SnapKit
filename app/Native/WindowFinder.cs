using System.Runtime.InteropServices;
using System.Text;

namespace SnapKit;

internal sealed class TopWindow
{
    public IntPtr Handle { get; init; }
    public NativeMethods.Rect Bounds { get; init; }
    public string Title { get; init; } = "";
}

internal static class WindowFinder
{
    public static TopWindow? At(int x, int y)
    {
        TopWindow? found = null;
        uint self = (uint)Environment.ProcessId;
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (found != null)
                return false;
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self)
                return true;

            int cloaked = 0;
            NativeMethods.DwmGetWindowAttributeInt(hwnd, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int));
            if (cloaked != 0)
                return true;

            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            if ((style & NativeMethods.WsExToolWindow) != 0)
                return true;

            var bounds = GetBounds(hwnd);
            if (bounds.Width < 16 || bounds.Height < 16)
                return true;
            if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
                return true;

            found = new TopWindow
            {
                Handle = hwnd,
                Bounds = bounds,
                Title = ReadTitle(hwnd)
            };
            return false;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return found;
    }

    public static NativeMethods.Rect GetBounds(IntPtr hwnd)
    {
        if (NativeMethods.DwmGetWindowAttributeRect(hwnd, NativeMethods.DwmwaExtendedFrameBounds, out var extended, Marshal.SizeOf<NativeMethods.Rect>()) == 0
            && extended.Width > 0 && extended.Height > 0)
            return extended;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    public static IntPtr RootFromPoint(int x, int y)
    {
        var hwnd = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = x, Y = y });
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
