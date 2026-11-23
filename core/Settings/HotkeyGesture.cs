using System.Text.Json.Serialization;

namespace SnapKit;

public enum CaptureAction
{
    Area,
    Window,
    FullScreen,
    Scrolling,
    Ocr,
    RepeatLastArea,
    RecordVideo,
    RecordGif
}

public sealed class HotkeyGesture
{
    public int VirtualKey { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0;

    [JsonIgnore]
    public uint NativeModifiers
    {
        get
        {
            uint mods = 0x4000; // MOD_NOREPEAT
            if (Alt) mods |= 0x0001;
            if (Ctrl) mods |= 0x0002;
            if (Shift) mods |= 0x0004;
            if (Win) mods |= 0x0008;
            return mods;
        }
    }

    [JsonIgnore]
    public string Display => HotkeyFormatter.Format(this);

    public HotkeyGesture Clone() => new()
    {
        VirtualKey = VirtualKey,
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Win = Win
    };

    public bool EqualsGesture(HotkeyGesture? other) =>
        other != null
        && VirtualKey == other.VirtualKey
        && Ctrl == other.Ctrl
        && Alt == other.Alt
        && Shift == other.Shift
        && Win == other.Win;

    public bool MatchesKey(int virtualKey, bool ctrl, bool alt, bool shift, bool win) =>
        !IsEmpty
        && VirtualKey == virtualKey
        && Ctrl == ctrl
        && Alt == alt
        && Shift == shift
        && Win == win;
}

public static class HotkeyFormatter
{
    public static string Format(HotkeyGesture gesture)
    {
        if (gesture.VirtualKey == 0)
            return "None";
        var parts = new List<string>(5);
        if (gesture.Ctrl) parts.Add("Ctrl");
        if (gesture.Shift) parts.Add("Shift");
        if (gesture.Alt) parts.Add("Alt");
        if (gesture.Win) parts.Add("Win");
        parts.Add(KeyName(gesture.VirtualKey));
        return string.Join("+", parts);
    }

    public static string KeyName(int virtualKey)
    {
        if (virtualKey >= 0x70 && virtualKey <= 0x87)
            return "F" + (virtualKey - 0x70 + 1);
        if (virtualKey is >= 0x30 and <= 0x39)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x60 and <= 0x69)
            return "Num" + (virtualKey - 0x60);
        return virtualKey switch
        {
            0x2C => "PrtSc",
            0x1B => "Esc",
            0x0D => "Enter",
            0x20 => "Space",
            0x09 => "Tab",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2E => "Delete",
            0x08 => "Backspace",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x2D => "Insert",
            0x14 => "CapsLock",
            0x91 => "ScrollLock",
            0x13 => "Pause",
            0x90 => "NumLock",
            _ => "VK" + virtualKey.ToString("X")
        };
    }
}
