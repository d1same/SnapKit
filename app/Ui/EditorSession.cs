using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal enum EditorTool
{
    Select,
    Pen,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Highlighter,
    Text,
    Callout,
    Number,
    Blur,
    Redact,
    Spotlight,
    Crop,
    Picker
}

internal sealed class EditorSession
{
    public EditorSession(ShotDocument document, RecentCapture recent, AppState state)
    {
        Document = document;
        Recent = recent;
        State = state;
        ColorHex = HexColor.Normalize(state.Settings.LastColorHex, "#FF3B30");
        Stroke = Math.Clamp(state.Settings.LastStroke <= 0 ? 3 : state.Settings.LastStroke, 1, 16);
        FontSize = Math.Clamp(state.Settings.LastFontSize <= 0 ? 18 : state.Settings.LastFontSize, 8, 72);
        FillMode = state.Settings.LastFillMode;
        Tool = ParseTool(state.Settings.LastTool);
        Status = HintFor(Tool);
    }

    public ShotDocument Document { get; }
    public RecentCapture Recent { get; }
    public AppState State { get; }
    public EditorTool Tool { get; private set; } = EditorTool.Arrow;
    public string ColorHex { get; private set; } = "#FF3B30";
    public string RedactColorHex { get; private set; } = "#151515";
    public double Stroke { get; private set; } = 3;
    public double FontSize { get; private set; } = 18;
    public ShapeFillMode FillMode { get; private set; } = ShapeFillMode.Outline;
    public AnnotationKind? SelectedKind { get; private set; }
    public double? ZoomPercent { get; private set; }
    public bool IsCopiedFeedback { get; private set; }
    public string Status { get; private set; } = "";
    public event Action? Changed;
    public event Action? Delivered;

    public string ActiveColorHex => Tool == EditorTool.Redact ? RedactColorHex : ColorHex;

    public void SelectTool(EditorTool tool)
    {
        Tool = tool;
        IsCopiedFeedback = false;
        State.Settings.LastTool = tool.ToString();
        PersistStyle();
        Status = HintFor(tool);
        Changed?.Invoke();
    }

    public void SetColor(string hex)
    {
        IsCopiedFeedback = false;
        if (Tool == EditorTool.Redact)
        {
            RedactColorHex = HexColor.Normalize(hex, RedactColorHex);
            Changed?.Invoke();
            return;
        }
        ColorHex = HexColor.Normalize(hex, ColorHex);
        State.Settings.LastColorHex = ColorHex;
        PersistStyle();
        Changed?.Invoke();
    }

    public void PickColorFromPixel(string hex)
    {
        ColorHex = HexColor.Normalize(hex, ColorHex);
        State.Settings.LastColorHex = ColorHex;
        PersistStyle();
        try
        {
            Clipboard.SetText(ColorHex);
            IsCopiedFeedback = true;
            Status = $"Copied {ColorHex}!";
        }
        catch
        {
            IsCopiedFeedback = false;
            Status = $"Picked {ColorHex}";
        }
        Changed?.Invoke();
    }

    public void SetStroke(double stroke)
    {
        IsCopiedFeedback = false;
        Stroke = Math.Clamp(stroke, 1, 16);
        State.Settings.LastStroke = Stroke;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetFontSize(double size)
    {
        IsCopiedFeedback = false;
        FontSize = Math.Clamp(size, 10, 64);
        State.Settings.LastFontSize = FontSize;
        PersistStyle();
        Changed?.Invoke();
    }

    public void StepFontSize(double delta) => SetFontSize(FontSize + delta);

    public void SetFillMode(ShapeFillMode mode)
    {
        IsCopiedFeedback = false;
        FillMode = mode;
        State.Settings.LastFillMode = mode;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetBackdrop(BackdropPreset preset)
    {
        IsCopiedFeedback = false;
        Document.SetBackdrop(preset);
        State.Settings.LastBackdrop = preset;
        PersistStyle();
        Status = preset == BackdropPreset.None ? "Frame off" : $"{preset} frame";
        Changed?.Invoke();
    }

    public void SetSelectedKind(AnnotationKind? kind)
    {
        if (SelectedKind == kind)
            return;
        SelectedKind = kind;
        Changed?.Invoke();
    }

    public void SetZoomPercent(double? percent)
    {
        ZoomPercent = percent;
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        IsCopiedFeedback = false;
        if (!Document.ClearAnnotations())
        {
            Flash("No marks to clear.");
            return;
        }
        SelectedKind = null;
        Flash("Cleared marks (Ctrl+Z undoes)");
    }

    public void ResetStep()
    {
        IsCopiedFeedback = false;
        if (!Document.ResetNumbering())
        {
            Flash("Step is already #1");
            return;
        }
        Flash("Step reset to #1");
    }

    public void Flash(string status, bool copied = false)
    {
        IsCopiedFeedback = copied;
        Status = status;
        Changed?.Invoke();
    }

    public BitmapSource Render() => ShotRenderer.Render(Document);

    public void Remember()
    {
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(Render()));
    }

    public void Copy()
    {
        var bitmap = Render();
        if (!ImageFile.Copy(bitmap))
        {
            Flash("Clipboard busy — try Copy again.");
            return;
        }
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Copied!", copied: true);
        Delivered?.Invoke();
    }

    public bool Save(Window owner)
    {
        var bitmap = Render();
        if (!ImageFile.Save(bitmap, owner))
            return false;
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Saved");
        Delivered?.Invoke();
        return true;
    }

    public bool QuickSave()
    {
        try
        {
            var bitmap = Render();
            string path = ImageFile.QuickSave(bitmap, State.Settings.QuickSaveFolder);
            State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
            Flash("Saved " + Path.GetFileName(path));
            State.Tray?.Balloon(Brand.Name, "Saved to " + path);
            Delivered?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Flash("Quick save failed: " + ex.Message);
            return false;
        }
    }

    private void PersistStyle()
    {
        try
        {
            SettingsStore.Save(State.Settings);
        }
        catch
        {
            // Keep editing smoothly even if settings.json is momentarily locked.
        }
    }

    private static EditorTool ParseTool(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<EditorTool>(value, ignoreCase: true, out var parsed)
            && parsed != EditorTool.Line)
            return parsed;
        return EditorTool.Arrow;
    }

    private static string HintFor(EditorTool tool) => tool switch
    {
        EditorTool.Select => "Drag handles to resize · Arrows nudge · Ctrl+D duplicates",
        EditorTool.Text => "Click to type · Ctrl+Enter commits",
        EditorTool.Callout => "Drag tip to note",
        EditorTool.Number => "Click to place step",
        EditorTool.Crop => "Drag area to keep",
        EditorTool.Picker => "Click pixel to pick & copy hex",
        EditorTool.Blur => "Drag box to pixelate",
        EditorTool.Redact => "Drag box to blackout",
        EditorTool.Spotlight => "Drag box to spotlight",
        _ => ""
    };
}
