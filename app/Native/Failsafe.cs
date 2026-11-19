using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SnapKit;

/// <summary>
/// Ctrl+Alt+Shift+Esc is registered on its own thread. If the UI thread is stuck
/// under a topmost window, this hotkey still ends the process and Windows drops the windows.
/// </summary>
internal static class Failsafe
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr HwndMessage = new(-3);
    private static WndProc? _proc;

    public static void Start()
    {
        var thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = Brand.Name + " failsafe"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void Kill()
    {
        try
        {
            Process.GetCurrentProcess().Kill(entireProcessTree: true);
        }
        catch
        {
            Environment.FailFast(Brand.Name + " failsafe");
        }
    }

    public static void ReleaseCovers()
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return;

        void Work()
        {
            foreach (System.Windows.Window window in app.Windows.OfType<System.Windows.Window>().ToList())
            {
                try
                {
                    window.Topmost = false;
                    if (window is OverlayWindow overlay)
                        overlay.ForceClose();
                }
                catch
                {
                    // Keep going until every cover is down.
                }
            }
        }

        if (app.Dispatcher.CheckAccess())
            Work();
        else
            app.Dispatcher.BeginInvoke(Work);
    }

    private static void Loop()
    {
        _proc = WindowProc;
        var klass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            lpszClassName = Brand.Name + "Failsafe"
        };
        RegisterClass(ref klass);
        var hwnd = CreateWindowEx(0, klass.lpszClassName, klass.lpszClassName, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd != IntPtr.Zero)
            RegisterHotKey(hwnd, 1, ModControl | ModAlt | ModShift | ModNoRepeat, NativeMethods.VkEscape);

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WmHotkey)
            Kill();
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public IntPtr lpfnWndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? Menu;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WndClass klass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string klass, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
