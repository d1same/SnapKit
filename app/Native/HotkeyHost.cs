using System.Windows.Interop;

namespace SnapKit;

internal sealed class HotkeyHost : IDisposable
{
    private readonly AppState _state;
    private readonly Dictionary<int, CaptureAction> _registered = new();
    private HwndSource? _source;

    public HotkeyHost(AppState state) => _state = state;

    public IReadOnlyList<string> RegisterAll()
    {
        EnsureWindow();
        Unregister();
        var hwnd = _source!.Handle;
        var failed = new List<string>();
        var seen = new List<HotkeyGesture>();
        foreach (var (action, gesture) in _state.Settings.Gestures())
        {
            if (gesture.IsEmpty)
                continue;
            if (seen.Any(existing => existing.EqualsGesture(gesture)))
            {
                failed.Add($"{Label(action)} uses the same keys as another action");
                continue;
            }
            int id = (int)action + 1;
            uint modifiers = gesture.NativeModifiers;
            bool registered = NativeMethods.RegisterHotKey(hwnd, id, modifiers, (uint)gesture.VirtualKey);
            if (!registered && (modifiers & 0x4000) != 0)
                registered = NativeMethods.RegisterHotKey(hwnd, id, modifiers & ~0x4000u, (uint)gesture.VirtualKey);
            if (!registered)
            {
                failed.Add($"{Label(action)} ({gesture.Display})");
                continue;
            }
            _registered[id] = action;
            seen.Add(gesture);
        }

        return failed;
    }

    public void Unregister()
    {
        if (_source == null)
        {
            _registered.Clear();
            return;
        }
        foreach (var id in _registered.Keys.ToList())
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }

    private void EnsureWindow()
    {
        if (_source != null)
            return;
        var parameters = new HwndSourceParameters(Brand.Name + " hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WsExToolWindow
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            _state.Coordinator.Start(action);
        }
        return IntPtr.Zero;
    }

    private static string Label(CaptureAction action) => action switch
    {
        CaptureAction.Area => "Area",
        CaptureAction.Window => "Window",
        CaptureAction.FullScreen => "Full screen",
        CaptureAction.Scrolling => "Scrolling capture",
        CaptureAction.Ocr => "OCR",
        CaptureAction.RepeatLastArea => "Repeat last area",
        _ => action.ToString()
    };
}
