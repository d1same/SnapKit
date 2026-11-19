namespace SnapKit;

public sealed class AppSettings
{
    public HotkeyGesture Area { get; set; } = new() { VirtualKey = 0x2C };
    public HotkeyGesture Window { get; set; } = new() { VirtualKey = 0x2C, Shift = true };
    public HotkeyGesture FullScreen { get; set; } = new() { VirtualKey = 0x2C, Alt = true };
    public HotkeyGesture Scrolling { get; set; } = new() { VirtualKey = 0x2C, Ctrl = true, Shift = true };
    public HotkeyGesture Ocr { get; set; } = new() { VirtualKey = 0x4F, Ctrl = true, Shift = true };
    public bool StartWithWindows { get; set; }
    public bool CaptureFlash { get; set; } = true;
    public bool ShutterSound { get; set; }

    // v2 can add a Record gesture next to these without changing the file shape much.

    public static AppSettings CreateDefault() => new();

    public HotkeyGesture GestureFor(CaptureAction action) => action switch
    {
        CaptureAction.Area => Area,
        CaptureAction.Window => Window,
        CaptureAction.FullScreen => FullScreen,
        CaptureAction.Scrolling => Scrolling,
        CaptureAction.Ocr => Ocr,
        _ => Area
    };

    public void SetGesture(CaptureAction action, HotkeyGesture gesture)
    {
        switch (action)
        {
            case CaptureAction.Area: Area = gesture; break;
            case CaptureAction.Window: Window = gesture; break;
            case CaptureAction.FullScreen: FullScreen = gesture; break;
            case CaptureAction.Scrolling: Scrolling = gesture; break;
            case CaptureAction.Ocr: Ocr = gesture; break;
        }
    }

    public IEnumerable<(CaptureAction Action, HotkeyGesture Gesture)> Gestures()
    {
        yield return (CaptureAction.Area, Area);
        yield return (CaptureAction.Window, Window);
        yield return (CaptureAction.FullScreen, FullScreen);
        yield return (CaptureAction.Scrolling, Scrolling);
        yield return (CaptureAction.Ocr, Ocr);
    }
}
