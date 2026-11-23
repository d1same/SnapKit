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

    public static void SetTopmost(Window window, bool topmost)
    {
        try
        {
            window.Topmost = topmost;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var after = topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost;
            NativeMethods.SetWindowPos(hwnd, after, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
        catch
        {
            // Window may have closed.
        }
    }

    public static IDisposable SuspendTopmost(Window? owner = null)
    {
        var saved = new List<Window>();
        var app = Application.Current;
        if (app != null)
        {
            foreach (Window w in app.Windows.OfType<Window>().ToList())
            {
                if (w.IsVisible && w.Topmost)
                {
                    saved.Add(w);
                    SetTopmost(w, false);
                }
            }
        }
        return new TopmostScope(saved, owner);
    }

    private sealed class TopmostScope : IDisposable
    {
        private readonly List<Window> _windows;
        private readonly Window? _owner;
        private bool _disposed;

        public TopmostScope(List<Window> windows, Window? owner)
        {
            _windows = windows;
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            // Restore OverlayWindows first so the editor/owner stays above them.
            foreach (var w in _windows.Where(w => w is OverlayWindow && w != _owner))
            {
                if (w.IsVisible)
                    SetTopmost(w, true);
            }
            foreach (var w in _windows.Where(w => w is not OverlayWindow && w != _owner))
            {
                if (w.IsVisible)
                    SetTopmost(w, true);
            }
            if (_owner != null && _windows.Contains(_owner) && _owner.IsVisible)
                SetTopmost(_owner, true);
        }
    }
}
