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
    }

    public ShotDocument Document { get; }
    public RecentCapture Recent { get; }
    public AppState State { get; }
    public EditorTool Tool { get; private set; } = EditorTool.Arrow;
    public string ColorHex { get; private set; } = "#FF3B30";
    public double Stroke { get; private set; } = 3;
    public double FontSize { get; private set; } = 18;
    public string Status { get; private set; } = "Ctrl+C copy    ·    Ctrl+S save    ·    Esc discards";
    public event Action? Changed;

    public void SelectTool(EditorTool tool)
    {
        Tool = tool;
        Status = tool switch
        {
            EditorTool.Text => "Click to place text. Esc leaves the text box.",
            EditorTool.Callout => "Drag from the point to the note.",
            EditorTool.Number => "Click to drop the next step number.",
            EditorTool.Crop => "Drag the area you want to keep.",
            EditorTool.Picker => "Click a pixel. Its hex is the new color.",
            EditorTool.Blur => "Drag a box to pixelate it.",
            EditorTool.Spotlight => "Drag a box. Everything outside it dims.",
            _ => "Ctrl+C copy    ·    Ctrl+S save    ·    Esc discards"
        };
        Changed?.Invoke();
    }

    public void SetColor(string hex)
    {
        ColorHex = HexColor.Normalize(hex, ColorHex);
        Changed?.Invoke();
    }

    public void SetStroke(double stroke)
    {
        Stroke = Math.Clamp(stroke, 1, 16);
        Changed?.Invoke();
    }

    public void SetFontSize(double size)
    {
        FontSize = size;
        Changed?.Invoke();
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
        ImageFile.Copy(bitmap);
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Copied");
    }

    public bool Save(Window owner)
    {
        var bitmap = Render();
        if (!ImageFile.Save(bitmap, owner))
            return false;
        State.Recent.Update(Recent, BitmapConvert.ToPixelBuffer(bitmap));
        Flash("Saved");
        return true;
    }
}
