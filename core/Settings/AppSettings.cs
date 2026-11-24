namespace SnapKit;

public sealed class SavedRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsValid => Width >= SelectionAdjust.MinSize && Height >= SelectionAdjust.MinSize;
}

public sealed class AppSettings
{
    public HotkeyGesture Area { get; set; } = new() { VirtualKey = 0x2C };
    public HotkeyGesture Window { get; set; } = new() { VirtualKey = 0x2C, Shift = true };
    public HotkeyGesture FullScreen { get; set; } = new() { VirtualKey = 0x2C, Alt = true };
    public HotkeyGesture Scrolling { get; set; } = new() { VirtualKey = 0x2C, Ctrl = true, Shift = true };
    public HotkeyGesture Ocr { get; set; } = new() { VirtualKey = 0x4F, Ctrl = true, Shift = true };
    public HotkeyGesture RepeatLastArea { get; set; } = new() { VirtualKey = 0x2C, Ctrl = true, Alt = true };
    public HotkeyGesture RecordVideo { get; set; } = new() { VirtualKey = 0x56, Ctrl = true, Alt = true };
    public HotkeyGesture RecordGif { get; set; } = new() { VirtualKey = 0x47, Ctrl = true, Alt = true };
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// 2 is the first revision that turns startup on for existing installs.
    /// A later save keeps whatever the user chose.
    /// </summary>
    public int Revision { get; set; }
    public bool CaptureFlash { get; set; } = true;
    public bool ShutterSound { get; set; }
    public bool CloseOnClickAway { get; set; } = true;
    public string QuickSaveFolder { get; set; } = "";
    public SavedRegion? LastArea { get; set; }

    // Persisted editor style defaults
    public string LastTool { get; set; } = "Arrow";
    public string LastColorHex { get; set; } = "#FF3B30";
    public double LastStroke { get; set; } = 3;
    public double LastFontSize { get; set; } = 18;
    public ShapeFillMode LastFillMode { get; set; } = ShapeFillMode.Outline;
    public BackdropPreset LastBackdrop { get; set; } = BackdropPreset.None;
    public bool ShowTicketStamp { get; set; }

    // Persisted recording defaults
    public bool RecordShowCursor { get; set; } = true;
    public bool RecordMicAudio { get; set; }
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordCountdown { get; set; } = true;
    public int RecordVideoFps { get; set; } = 30;
    public int RecordGifFps { get; set; } = 12;

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        ApplyStartupDefault(settings);
        return settings;
    }

    public static bool ApplyStartupDefault(AppSettings settings)
    {
        if (settings.Revision >= 2)
            return false;
        settings.StartWithWindows = true;
        settings.Revision = 2;
        return true;
    }

    public HotkeyGesture GestureFor(CaptureAction action) => action switch
    {
        CaptureAction.Area => Area,
        CaptureAction.Window => Window,
        CaptureAction.FullScreen => FullScreen,
        CaptureAction.Scrolling => Scrolling,
        CaptureAction.Ocr => Ocr,
        CaptureAction.RepeatLastArea => RepeatLastArea,
        CaptureAction.RecordVideo => RecordVideo,
        CaptureAction.RecordGif => RecordGif,
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
            case CaptureAction.RepeatLastArea: RepeatLastArea = gesture; break;
            case CaptureAction.RecordVideo: RecordVideo = gesture; break;
            case CaptureAction.RecordGif: RecordGif = gesture; break;
        }
    }

    public IEnumerable<(CaptureAction Action, HotkeyGesture Gesture)> Gestures()
    {
        yield return (CaptureAction.Area, Area);
        yield return (CaptureAction.Window, Window);
        yield return (CaptureAction.FullScreen, FullScreen);
        yield return (CaptureAction.Scrolling, Scrolling);
        yield return (CaptureAction.Ocr, Ocr);
        yield return (CaptureAction.RepeatLastArea, RepeatLastArea);
        yield return (CaptureAction.RecordVideo, RecordVideo);
        yield return (CaptureAction.RecordGif, RecordGif);
    }
}
