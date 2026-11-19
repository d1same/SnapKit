using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal sealed class ToolbarView : Border
{
    private readonly Dictionary<EditorTool, Border> _tools = new();
    private readonly Dictionary<string, Border> _swatches = new();
    private readonly Dictionary<double, Border> _sizes = new();
    private readonly TextBlock _hex;
    private readonly TextBlock _font;
    private readonly TextBlock _hint;
    private readonly Border _undo;
    private readonly Border _redo;
    private EditorTool _active = EditorTool.Arrow;
    private bool _sync;

    public ToolbarView(bool showFit)
    {
        Background = Palette.Card;
        BorderBrush = Palette.Line;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(8, 7, 8, 7);
        SnapsToDevicePixels = true;
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 1,
            Opacity = 0.45,
            Color = Colors.Black
        };

        var tools = new WrapPanel();
        AddTool(tools, EditorTool.Select, "Select  (V)", "M3,2 L3,14 L6.2,10.2 L8.4,14.2 L10.2,13.2 L8,9.2 L12,9 Z", filled: true);
        AddTool(tools, EditorTool.Pen, "Pen  (P)", "M12.5,1.5 L14.5,3.5 L5,13 L2,14 L3,11 Z");
        AddTool(tools, EditorTool.Line, "Line  (L)", "M3,13 L13,3");
        AddTool(tools, EditorTool.Arrow, "Arrow  (A)", "M2,12 L13,3 M13,3 L8,3.2 M13,3 L12.6,8");
        AddTool(tools, EditorTool.Rectangle, "Rectangle  (R)", "M3,3 H13 V13 H3 Z");
        AddTool(tools, EditorTool.Ellipse, "Ellipse  (E)", "M8,3.2 A4.8,4.8 0 1 0 8.1,3.2");
        AddTool(tools, EditorTool.Highlighter, "Highlighter  (H)", "M3,11 L10,4 L13,7 L6,14 Z");
        AddTool(tools, EditorTool.Text, "Text  (T)", "M3,3.5 H13 M8,3.5 V13 M5.2,13 H10.8");
        AddTool(tools, EditorTool.Callout, "Callout  (C)", "M2.5,3 H12.5 V9.2 H7.2 L4.2,12.2 V9.2 H2.5 Z");
        AddTool(tools, EditorTool.Number, "Step number  (N)", "M8,2.2 A5.8,5.8 0 1 0 8.1,2.2");
        AddTool(tools, EditorTool.Blur, "Blur  (B)", "M3,3 H6.2 V6.2 H3 Z M9.8,3 H13 V6.2 H9.8 Z M3,9.8 H6.2 V13 H3 Z M9.8,9.8 H13 V13 H9.8 Z");
        AddTool(tools, EditorTool.Spotlight, "Spotlight  (S)", "M2.5,2.5 H13.5 V13.5 H2.5 Z M6,6 H10 V10 H6 Z");
        AddTool(tools, EditorTool.Crop, "Crop  (X)", "M3,6 V3 H6 M10,3 H13 V6 M13,10 V13 H10 M6,13 H3 V10");
        AddTool(tools, EditorTool.Picker, "Pick color  (I)", "M10.8,2.2 L13.2,4.6 L6.2,11.6 L3.2,12.8 L4.4,9.8 Z");

        var options = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        foreach (var swatch in Palette.Swatches)
        {
            var dot = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = Palette.Brush(swatch),
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = swatch
            };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                ColorPicked?.Invoke(swatch);
                e.Handled = true;
            };
            _swatches[swatch] = dot;
            options.Children.Add(dot);
        }

        _hex = new TextBlock
        {
            Text = "#FF3B30",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0)
        };
        options.Children.Add(_hex);

        foreach (var size in new[] { 2d, 3d, 5d, 8d, 12d })
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = Math.Clamp(size + 2, 4, 14),
                Height = Math.Clamp(size + 2, 4, 14),
                Fill = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = Chip(dot, $"{size:0} px stroke", () => StrokePicked?.Invoke(size));
            chip.Width = 22;
            _sizes[size] = chip;
            options.Children.Add(chip);
        }

        _font = new TextBlock
        {
            Text = "18",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        options.Children.Add(Chip(_font, "Text size", CycleFont));

        _undo = Chip(Icon("M8,4.5 A4.2,4.2 0 1 0 12.2,8 M8,4.5 V7.4 M8,4.5 H5.2"), "Undo  (Ctrl+Z)", () => UndoClicked?.Invoke());
        _redo = Chip(Icon("M8,4.5 A4.2,4.2 0 1 1 3.8,8 M8,4.5 V7.4 M8,4.5 H10.8"), "Redo  (Ctrl+Y)", () => RedoClicked?.Invoke());
        options.Children.Add(_undo);
        options.Children.Add(_redo);
        options.Children.Add(Chip(Label("OCR"), "Read text in this capture", () => OcrClicked?.Invoke()));
        options.Children.Add(Chip(Icon("M8,2.2 V9.5 M5.2,4.2 H10.8 M6,12.5 H10"), "Pin on top", () => PinClicked?.Invoke()));
        options.Children.Add(Chip(Icon("M5,5 H11 V8 H13 L8,13 L3,8 H5 Z"), "Copy the marked-up picture  (right-click or Ctrl+C)", () => CopyClicked?.Invoke()));
        options.Children.Add(Chip(Icon("M4,3 H10 L13,6 V13 H4 Z M10,3 V6 H13"), "Save  (Ctrl+S)", () => SaveClicked?.Invoke()));
        if (showFit)
            options.Children.Add(Chip(Label("Fit"), "Fit the image in the window", () => FitClicked?.Invoke()));
        options.Children.Add(Chip(Label("×"), "Discard  (Esc)", () => CloseClicked?.Invoke()));

        _hint = new TextBlock
        {
            Text = "Right-click copies the picture. Ctrl+S saves. Esc discards.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11,
            Margin = new Thickness(4, 6, 4, 0)
        };

        Child = new StackPanel
        {
            Children = { tools, options, _hint }
        };
        HighlightTool(EditorTool.Arrow);
        HighlightSize(3);
        HighlightSwatch("#FF3B30");
    }

    public event Action<EditorTool>? ToolPicked;
    public event Action<string>? ColorPicked;
    public event Action<double>? StrokePicked;
    public event Action<double>? FontPicked;
    public event Action? UndoClicked;
    public event Action? RedoClicked;
    public event Action? CopyClicked;
    public event Action? SaveClicked;
    public event Action? PinClicked;
    public event Action? OcrClicked;
    public event Action? CloseClicked;
    public event Action? FitClicked;

    public void Sync(EditorSession session)
    {
        _sync = true;
        _hex.Text = session.ColorHex;
        _font.Text = session.FontSize.ToString("0");
        _hint.Text = session.Status;
        HighlightTool(session.Tool);
        HighlightSwatch(session.ColorHex);
        HighlightSize(session.Stroke);
        _undo.Opacity = session.Document.CanUndo ? 1 : 0.35;
        _redo.Opacity = session.Document.CanRedo ? 1 : 0.35;
        _sync = false;
    }

    private void CycleFont()
    {
        double next = _font.Text switch
        {
            "14" => 18,
            "18" => 24,
            "24" => 32,
            _ => 14
        };
        FontPicked?.Invoke(next);
    }

    private void AddTool(Panel panel, EditorTool tool, string tip, string geometry, bool filled = false)
    {
        var icon = Icon(geometry, filled);
        var chip = Chip(icon, tip, () =>
        {
            if (_sync) return;
            ToolPicked?.Invoke(tool);
        });
        _tools[tool] = chip;
        panel.Children.Add(chip);
    }

    private void HighlightTool(EditorTool tool)
    {
        _active = tool;
        foreach (var (key, chip) in _tools)
        {
            bool on = key == tool;
            chip.Background = on ? Palette.Text : Brushes.Transparent;
            if (chip.Child is Shape shape)
            {
                var ink = on ? Palette.Ink : Palette.Text;
                shape.Stroke = ink;
                if (shape.Fill != null && shape.Fill != Brushes.Transparent)
                    shape.Fill = ink;
            }
            else if (chip.Child is TextBlock block)
            {
                block.Foreground = on ? Palette.Ink : Palette.Text;
            }
        }
    }

    private void HighlightSwatch(string hex)
    {
        foreach (var (key, chip) in _swatches)
            chip.BorderBrush = key.Equals(hex, StringComparison.OrdinalIgnoreCase) ? Palette.Text : Palette.Line;
    }

    private void HighlightSize(double stroke)
    {
        double nearest = _sizes.Keys.OrderBy(size => Math.Abs(size - stroke)).First();
        foreach (var (size, chip) in _sizes)
            chip.Background = Math.Abs(size - nearest) < 0.1 ? Palette.Elevated : Brushes.Transparent;
    }

    private static Border Chip(UIElement content, string tip, Action click)
    {
        var chip = new Border
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = content,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        chip.MouseEnter += (_, _) =>
        {
            if (chip.Background == Brushes.Transparent)
                chip.Background = Palette.Hover;
        };
        chip.MouseLeave += (_, _) =>
        {
            if (chip.Background == Palette.Hover)
                chip.Background = Brushes.Transparent;
        };
        chip.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return chip;
    }

    private static Shape Icon(string geometry, bool filled = false)
    {
        return new Path
        {
            Data = Geometry.Parse(geometry),
            Stroke = Palette.Text,
            StrokeThickness = 1.4,
            Fill = filled ? Palette.Text : Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(8)
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Palette.Text,
        FontFamily = Palette.Font,
        FontSize = text == "×" ? 16 : 11,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
