using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SnapKit;

internal sealed class HotkeyHost : IDisposable
{
    private readonly AppState _state;
    private readonly Dictionary<int, CaptureAction> _registered = new();
    private readonly List<(CaptureAction Action, HotkeyGesture Gesture)> _hookBindings = new();
    private readonly HashSet<int> _swallowedVks = new();
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private HwndSource? _source;
    private IntPtr _keyboardHook;
    private long _lastTriggerTicks;
    private CaptureAction _lastTriggerAction;

    public HotkeyHost(AppState state)
    {
        _state = state;
        _hookProc = KeyboardHookProc;
    }

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

            if (registered)
                _registered[id] = action;

            // On Windows 11, Snipping Tool claims Print Screen (VK_SNAPSHOT) at the shell level
            // so RegisterHotKey fails or Snipping Tool still intercepts it. A WH_KEYBOARD_LL hook
            // runs before the shell hotkey handler and cleanly intercepts + swallows Print Screen
            // (and acts as a fallback for any other hotkey RegisterHotKey couldn't claim).
            bool needsHook = gesture.VirtualKey == NativeMethods.VkSnapshot || !registered;
            if (needsHook)
                _hookBindings.Add((action, gesture.Clone()));

            seen.Add(gesture);
        }

        if (_hookBindings.Count > 0)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WhKeyboardLl,
                _hookProc,
                NativeMethods.GetModuleHandle(null),
                0);

            if (_keyboardHook == IntPtr.Zero)
            {
                foreach (var (action, gesture) in _hookBindings)
                {
                    if (!_registered.ContainsValue(action))
                        failed.Add($"{Label(action)} ({gesture.Display})");
                }
                _hookBindings.Clear();
            }
        }

        return failed;
    }

    public void Unregister()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _hookBindings.Clear();
        _swallowedVks.Clear();

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

    public static bool IsWindowsSnippingPrintScreenEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: false);
            var raw = key?.GetValue("PrintScreenKeyForSnippingEnabled");
            return raw is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    public static bool DisableWindowsSnippingPrintScreen()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard");
            key.SetValue("PrintScreenKeyForSnippingEnabled", 0, RegistryValueKind.DWord);
            NativeMethods.SendMessageTimeout(
                new IntPtr(0xFFFF), // HWND_BROADCAST
                NativeMethods.WmSettingChange,
                UIntPtr.Zero,
                "Keyboard",
                0x0002, // SMTO_ABORTIFHUNG
                1000,
                out _);
            return true;
        }
        catch
        {
            return false;
        }
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
            Trigger(action);
        }
        return IntPtr.Zero;
    }

    private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _hookBindings.Count > 0)
        {
            int msg = wParam.ToInt32();
            bool isDown = msg is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            bool isUp = msg is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

            if (isDown || isUp)
            {
                var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                if ((info.Flags & NativeMethods.LlkhfInjected) == 0)
                {
                    int vk = (int)info.VkCode;

                    if (isUp)
                    {
                        if (_swallowedVks.Remove(vk))
                            return (IntPtr)1;
                    }
                    else
                    {
                        if (_swallowedVks.Contains(vk))
                            return (IntPtr)1;

                        bool ctrl = IsKeyDown(NativeMethods.VkControl);
                        bool shift = IsKeyDown(NativeMethods.VkShift);
                        bool alt = IsKeyDown(NativeMethods.VkMenu) || (info.Flags & NativeMethods.LlkhfAltDown) != 0;
                        bool win = IsKeyDown(NativeMethods.VkLWin) || IsKeyDown(NativeMethods.VkRWin);

                        foreach (var (action, gesture) in _hookBindings)
                        {
                            if (gesture.MatchesKey(vk, ctrl, alt, shift, win))
                            {
                                _swallowedVks.Add(vk);
                                DispatchTrigger(action);
                                // Return non-zero immediately so Windows Snipping Tool never sees this key.
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void DispatchTrigger(CaptureAction action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.BeginInvoke(() => Trigger(action));
        else
            Trigger(action);
    }

    private void Trigger(CaptureAction action)
    {
        long now = Environment.TickCount64;
        if (action == _lastTriggerAction && now - _lastTriggerTicks < 250)
            return;
        _lastTriggerAction = action;
        _lastTriggerTicks = now;
        _state.Coordinator.Start(action);
    }

    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static string Label(CaptureAction action) => action switch
    {
        CaptureAction.Area => "Area",
        CaptureAction.Window => "Window",
        CaptureAction.FullScreen => "Full screen",
        CaptureAction.Scrolling => "Scrolling capture",
        CaptureAction.Ocr => "OCR",
        CaptureAction.RepeatLastArea => "Repeat last area",
        CaptureAction.RecordVideo => "Record video (MP4)",
        CaptureAction.RecordGif => "Record GIF",
        _ => action.ToString()
    };
}
