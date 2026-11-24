using System.Runtime.InteropServices;
using System.Text;

namespace SnapKit;

internal static class NativeMethods
{
    public const int SrcCopy = 0x00CC0020;
    public const int IcmOff = 1;
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const int WsCaption = 0x00C00000;
    public const int WsThickFrame = 0x00040000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTransparent = 0x00000020;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExNoActivate = 0x08000000;
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;
    public const int MonitorDefaultToNearest = 2;
    public const int GaRoot = 2;
    public const int SwRestore = 9;
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmLButtonDown = 0x0201;
    public const int WmHotkey = 0x0312;
    public const int WmSettingChange = 0x001A;
    public const uint LlkhfInjected = 0x10;
    public const uint LlkhfAltDown = 0x20;
    public const int WmMouseWheel = 0x020A;
    public const int VkLButton = 0x01;
    public const int VkRButton = 0x02;
    public const int VkEscape = 0x1B;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkSnapshot = 0x2C;
    public const uint MouseEventWheel = 0x0800;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;
    public const int DwmwaExtendedFrameBounds = 9;
    public const int DwmwaCloaked = 14;
    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwaUseImmersiveDarkModeOld = 19;
    public const int DwmwaWindowCornerPreference = 33;
    public const uint SndMemory = 0x0004;
    public const uint SndNoDefault = 0x0002;
    public const uint KeyEventKeyUp = 0x0002;

    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNotTopmost = new(-2);

    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BitmapInfo bmi, uint usage);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern int SetICMMode(IntPtr hdc, int mode);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref Point pt);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point pt);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMax);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint source, uint target, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool MoveFileEx(string existing, string? replacement, int flags);
    public const int MoveFileDelayUntilReboot = 0x4;
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out Rect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("winmm.dll", SetLastError = true)] public static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int X;
        public int Y;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    // win-x64 INPUT: the type dword is followed by 4 bytes of padding, then the union.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
