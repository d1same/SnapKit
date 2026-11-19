using System.Runtime.InteropServices;

namespace SnapKit;

internal sealed class KeyChordListener : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hook;

    public event Action<HotkeyGesture>? Captured;

    public KeyChordListener() => _proc = Hook;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _proc,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == (IntPtr)NativeMethods.WmKeyDown || wParam == (IntPtr)NativeMethods.WmSysKeyDown))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (IsModifier(vk))
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

            var gesture = vk == NativeMethods.VkEscape
                ? new HotkeyGesture()
                : new HotkeyGesture
                {
                    VirtualKey = vk,
                    Ctrl = Down(NativeMethods.VkControl),
                    Shift = Down(NativeMethods.VkShift),
                    Alt = Down(NativeMethods.VkMenu),
                    Win = Down(NativeMethods.VkLWin) || Down(NativeMethods.VkRWin)
                };
            Captured?.Invoke(gesture);
            return (IntPtr)1;
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
}
