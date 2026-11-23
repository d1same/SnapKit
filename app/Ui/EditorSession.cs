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
    public double Stroke { get; private set; } = 3;
    public double FontSize { get; private set; } = 18;
    public ShapeFillMode FillMode { get; private set; } = ShapeFillMode.Outline;
    public AnnotationKind? SelectedKind { get; private set; }
    public double? ZoomPercent { get; private set; }
    public string Status { get; private set; } = "Copy copies the shot and closes it. Esc closes it too.";
    public event Action? Changed;
    public event Action? Delivered;

    public void SelectTool(EditorTool tool)
    {
        Tool = tool;
        State.Settings.LastTool = tool.ToString();
        PersistStyle();
        Status = HintFor(tool);
        Changed?.Invoke();
    }

    public void SetColor(string hex)
    {
        ColorHex = HexColor.Normalize(hex, ColorHex);
        State.Settings.LastColorHex = ColorHex;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetStroke(double stroke)
    {
        Stroke = Math.Clamp(stroke, 1, 16);
        State.Settings.LastStroke = Stroke;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetFontSize(double size)
    {
        FontSize = Math.Clamp(size, 8, 72);
        State.Settings.LastFontSize = FontSize;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetFillMode(ShapeFillMode mode)
    {
        FillMode = mode;
        State.Settings.LastFillMode = mode;
        PersistStyle();
        Changed?.Invoke();
    }

    public void SetBackdrop(BackdropPreset preset)
    {
        Document.SetBackdrop(preset);
        State.Settings.LastBackdrop = preset;
        PersistStyle();
        Status = preset == BackdropPreset.None
            ? "Frame removed."
            : $"{preset} docs frame applied.";
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
        if (!Document.ClearAnnotations())
        {
            Flash("Nothing to clear.");
            return;
        }
        SelectedKind = null;
        Flash("Cleared all marks. Ctrl+Z restores them.");
    }

    public void ResetStep()
    {
        if (!Document.ResetNumbering())
        {
            Flash("Step numbering is already at #1.");
            return;
        }
        Flash("Next step reset to #1.");
    }

    public void Flash(string status)
    {
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
            Flash("Clipboard is busy. Click Copy again.");
            return;
        }
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Copied. Paste it anywhere.");
        Delivered?.Invoke();
    }

    public bool Save(Window owner)
    {
        var bitmap = Render();
        if (!ImageFile.Save(bitmap, owner))
            return false;
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Saved.");
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
        EditorTool.Select => "Drag to move · Drag handles to resize · Arrows nudge · Ctrl+D duplicates",
        EditorTool.Text => "Click to type text · Ctrl+Enter finishes · Esc cancels",
        EditorTool.Callout => "Drag from the pointer tip to the note bubble",
        EditorTool.Number => "Click to drop the next step number",
        EditorTool.Crop => "Drag the area you want to keep",
        EditorTool.Picker => "Click any pixel to pick its hex color",
        EditorTool.Blur => "Drag a box to pixelate sensitive details",
        EditorTool.Redact => "Drag a box to permanently block out sensitive text",
        EditorTool.Spotlight => "Drag a box · Everything outside it dims",
        EditorTool.Rectangle or EditorTool.Ellipse => "Drag to draw · Hold Shift for a square/circle",
        EditorTool.Arrow => "Drag to point · Hold Shift for 45° angles",
        _ => "Copy copies and closes · Ctrl+Shift+S quick-saves · Esc closes"
    };
}
