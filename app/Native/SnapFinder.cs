using System.Text;

namespace SnapKit;

internal sealed class SnapTarget
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Kind { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public string Line => string.IsNullOrWhiteSpace(Detail) || string.Equals(Detail, Kind, StringComparison.Ordinal)
        ? Kind
        : Kind + "   ·   " + Detail;
}

/// <summary>
/// Targets under the cursor, from the tightest pane up to the whole monitor.
/// Win32 windows only: a browser content window is "this tab", the frame is the browser,
/// an Explorer view is the folder, and the monitor is always the last choice.
/// </summary>
internal static class SnapFinder
{
    public static IReadOnlyList<SnapTarget> At(int x, int y, MonitorInfo monitor)
    {
        var found = new List<SnapTarget>();
        var window = TopLevelAt(x, y);
        if (window != null)
        {
            string cls = ClassName(window.Handle);
            string title = Short(window.Title);
            foreach (var child in ChildrenAt(window, x, y))
            {
                var local = Clip(child.Bounds, monitor);
                if (local.Width < 80 || local.Height < 60)
                    continue;
                AddDistinct(found, local, LabelChild(child.ClassName, cls), title);
            }

            var frame = Clip(window.Bounds, monitor);
            if (frame.Width >= 16 && frame.Height >= 16)
                AddDistinct(found, frame, LabelWindow(cls, window.Title), title);
        }

        AddDistinct(found, new NativeMethods.Rect
        {
            Left = monitor.X,
            Top = monitor.Y,
            Right = monitor.Right,
            Bottom = monitor.Bottom
        }, "Entire screen", "");

        return found;
    }

    private static void AddDistinct(List<SnapTarget> list, NativeMethods.Rect rect, string kind, string detail)
    {
        foreach (var existing in list)
        {
            if (Math.Abs(existing.X - rect.Left) <= 12 && Math.Abs(existing.Y - rect.Top) <= 12
                && Math.Abs(existing.Right - rect.Right) <= 12 && Math.Abs(existing.Bottom - rect.Bottom) <= 12)
                return;
        }

        if (list.Count >= 6)
            return;
        list.Add(new SnapTarget
        {
            X = rect.Left,
            Y = rect.Top,
            Width = rect.Width,
            Height = rect.Height,
            Kind = kind,
            Detail = detail
        });
    }

    private static TopWindow? TopLevelAt(int x, int y)
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
            string cls = ClassName(hwnd);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768" or "#32769" or "#32774")
                return true;
            var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            if ((style & NativeMethods.WsExToolWindow) != 0)
                return true;
            var bounds = WindowFinder.GetBounds(hwnd);
            if (bounds.Width < 16 || bounds.Height < 16)
                return true;
            if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
                return true;
            found = new TopWindow { Handle = hwnd, Bounds = bounds, Title = Title(hwnd) };
            return false;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return found;
    }

    private sealed class ChildHit
    {
        public NativeMethods.Rect Bounds { get; init; }
        public string ClassName { get; init; } = "";
    }

    private static List<ChildHit> ChildrenAt(TopWindow window, int x, int y)
    {
        var hits = new List<ChildHit>();
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;
            var bounds = WindowFinder.GetBounds(hwnd);
            if (bounds.Width < 80 || bounds.Height < 60)
                return true;
            if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
                return true;
            long area = (long)bounds.Width * bounds.Height;
            long parent = (long)window.Bounds.Width * window.Bounds.Height;
            if (parent > 0 && area > parent * 98 / 100)
                return true;
            hits.Add(new ChildHit { Bounds = bounds, ClassName = ClassName(hwnd) });
            return true;
        };
        NativeMethods.EnumChildWindows(window.Handle, callback, IntPtr.Zero);
        hits.Sort((a, b) => ((long)a.Bounds.Width * a.Bounds.Height).CompareTo((long)b.Bounds.Width * b.Bounds.Height));

        var kept = new List<ChildHit>();
        foreach (var hit in hits)
        {
            if (kept.Count == 0)
            {
                kept.Add(hit);
                continue;
            }
            var previous = kept[^1].Bounds;
            bool grew = hit.Bounds.Width >= previous.Width + 24 || hit.Bounds.Height >= previous.Height + 24;
            if (grew)
                kept.Add(hit);
            if (kept.Count == 4)
                break;
        }
        return kept;
    }

    private static NativeMethods.Rect Clip(NativeMethods.Rect rect, MonitorInfo monitor)
    {
        int left = Math.Max(rect.Left, monitor.X);
        int top = Math.Max(rect.Top, monitor.Y);
        int right = Math.Min(rect.Right, monitor.Right);
        int bottom = Math.Min(rect.Bottom, monitor.Bottom);
        return new NativeMethods.Rect { Left = left, Top = top, Right = Math.Max(left, right), Bottom = Math.Max(top, bottom) };
    }

    private static string LabelChild(string childClass, string windowClass)
    {
        if (childClass is "Chrome_RenderWidgetHostHWND" or "MozillaCompositorWindowClass" or "Intermediate D3D Window")
            return "This tab";
        if (IsExplorer(windowClass) && childClass is "SHELLDLL_DefView" or "DirectUIHWND" or "SysListView32")
            return "This folder";
        if (childClass == "SysTreeView32")
            return "This pane";
        return "This pane";
    }

    private static string LabelWindow(string cls, string title)
    {
        if (cls is "Chrome_WidgetWin_1" or "Chrome_WidgetWin_0" or "MozillaWindowClass")
            return "Browser window";
        if (IsExplorer(cls))
            return "File Explorer";
        if (cls == "ApplicationFrameWindow" && LooksLikeBrowser(title))
            return "Browser window";
        if (!string.IsNullOrWhiteSpace(title))
            return "This window";
        return "This window";
    }

    private static bool IsExplorer(string cls) => cls is "CabinetWClass" or "ExploreWClass";

    private static bool LooksLikeBrowser(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;
        return title.Contains("Edge", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Firefox", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Brave", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string Title(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string Short(string value)
    {
        value = value.Trim();
        return value.Length <= 48 ? value : value[..45] + "…";
    }
}
