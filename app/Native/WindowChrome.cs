using System.Windows;
using System.Windows.Interop;

namespace SnapKit;

internal static class WindowChrome
{
    public static void Place(Window window, int x, int y, int width, int height, double scale, bool topmost, bool activate, bool inTaskbar = false)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        scale = scale <= 0 ? 1 : scale;
        window.Width = width / scale;
        window.Height = height / scale;
        window.Topmost = topmost;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        StripFrame(hwnd, inTaskbar);
        uint flags = NativeMethods.SwpFrameChanged | NativeMethods.SwpShowWindow;
        if (!activate)
            flags |= NativeMethods.SwpNoActivate;
        var after = topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost;
        NativeMethods.SetWindowPos(hwnd, after, x, y, width, height, flags);
    }

    public static void UseDarkTitle(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        UseDarkPopup(hwnd);
    }

    public static void UseDarkPopup(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            int enabled = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkModeOld, ref enabled, sizeof(int));
            int rounded = 2; // DWMWCP_ROUND
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref rounded, sizeof(int));
        }
        catch
        {
            // Older Windows builds may ignore DWM corner preference.
        }
    }

    public static void StripFrame(IntPtr hwnd, bool inTaskbar = false)
    {
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~NativeMethods.WsCaption;
        style &= ~NativeMethods.WsThickFrame;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(style));
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        if (inTaskbar)
        {
            ex &= ~NativeMethods.WsExToolWindow;
            ex |= NativeMethods.WsExAppWindow;
        }
        else
        {
            ex |= NativeMethods.WsExToolWindow;
            ex &= ~NativeMethods.WsExAppWindow;
        }
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(ex));
    }
}
