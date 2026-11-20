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
    private readonly TextBox _hex;
    private readonly TextBlock _font;
    private readonly TextBlock _hint;
    private readonly Border _undo;
    private readonly Border _redo;
    private bool _sync;

    public ToolbarView(bool showFit)
    {
        Background = Palette.Card;
        BorderBrush = Palette.Line;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(10, 8, 10, 8);
        SnapsToDevicePixels = true;
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 1,
            Opacity = 0.45,
            Color = Colors.Black
        };

        var tools = new WrapPanel();
        AddTool(tools, EditorTool.Select, "Select", "Select or move a mark  (V)", "M7,4 L7,20 L11,16 L14.5,22 L17.5,20.5 L14,14.5 L20,14.5 Z", filled: true);
        AddTool(tools, EditorTool.Pen, "Pen", "Draw freehand  (P)", "M4,20 L8.5,18.5 L19,8 L16,5 L5.5,15.5 Z M15.2,6.2 L17.8,8.8");
        AddTool(tools, EditorTool.Arrow, "Arrow", "Arrow  (A)", "M5,19 L19,5 M19,5 L12,6.2 M19,5 L17.8,12");
        AddTool(tools, EditorTool.Rectangle, "Box", "Rectangle  (R)", "M5,5 H19 V19 H5 Z");
        AddTool(tools, EditorTool.Ellipse, "Circle", "Ellipse  (E)", "M12,5.5 A6.5,6.5 0 1 0 12.1,5.5");
        AddTool(tools, EditorTool.Highlighter, "Mark", "Highlighter  (H)", "M4,15 L14,5 L19,10 L9,20 Z M4,15 L7.5,18.5");
        AddTool(tools, EditorTool.Text, "Text", "Type text  (T)", "M5,5 H19 M12,5 V19 M8,19 H16");
        AddTool(tools, EditorTool.Callout, "Note", "Callout  (C)", "M4,4 H20 V14 H12 L8,19 V14 H4 Z");
        AddTool(tools, EditorTool.Number, "Step", "Numbered step  (N)", "M12,4.5 A7.5,7.5 0 1 0 12.1,4.5 M12,8 V16 M9.5,10.2 L12,8");
        AddTool(tools, EditorTool.Blur, "Blur", "Pixelate a box  (B)", "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z");
        AddTool(tools, EditorTool.Spotlight, "Spot", "Dim everything outside a box  (S)", "M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M6,6 L8,8 M16,16 L18,18 M18,6 L16,8 M8,16 L6,18 M12,8.5 A3.5,3.5 0 1 0 12.1,8.5");
        AddTool(tools, EditorTool.Crop, "Crop", "Keep only the dragged area  (X)", "M8,4 V8 H4 M16,4 V8 H20 M20,16 H16 V20 M4,16 H8 V20");
        AddTool(tools, EditorTool.Picker, "Color", "Pick a color from the picture  (I)", "M14,4 L20,10 L10,20 L6,21 L7,17 Z M8,15 L13,10");

        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var swatch in Palette.Swatches)
        {
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Palette.Brush(swatch),
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(3, 4, 3, 4),
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

        _hex = new TextBox
        {
            Text = "#FF3B30",
            Width = 78,
            Height = 26,
            Margin = new Thickness(6, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Mono,
            FontSize = 12,
            CaretBrush = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 7,
            ToolTip = "Type a hex color. It recolors the selected mark and the next one."
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            CommitHex();
            Keyboard.ClearFocus();
            e.Handled = true;
        };
        _hex.LostKeyboardFocus += (_, _) => CommitHex();
        options.Children.Add(_hex);

        foreach (var size in new[] { 2d, 3d, 5d, 8d, 12d })
        {
            var dot = new Ellipse
            {
                Width = Math.Clamp(size + 2, 5, 16),
                Height = Math.Clamp(size + 2, 5, 16),
                Fill = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = Chip(dot, $"{size:0} px stroke", () => StrokePicked?.Invoke(size));
            chip.Width = 28;
            chip.Height = 28;
            _sizes[size] = chip;
            options.Children.Add(chip);
        }

        _font = new TextBlock
        {
            Text = "18",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        options.Children.Add(NamedChip(_font, "Size", "Text size. Click to cycle 14, 18, 24, 32.", CycleFont, 44, 36));

        _undo = NamedChip(Icon("M8,7 A6,6 0 1 0 14,12 M8,7 V11 M8,7 H12"), "Undo", "Undo  (Ctrl+Z)", () => UndoClicked?.Invoke(), 44, 36);
        _redo = NamedChip(Icon("M16,7 A6,6 0 1 1 10,12 M16,7 V11 M16,7 H12"), "Redo", "Redo  (Ctrl+Y)", () => RedoClicked?.Invoke(), 44, 36);
        options.Children.Add(_undo);
        options.Children.Add(_redo);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(CopyButton());
        actions.Children.Add(NamedChip(Icon("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M8,12 H16 M8,16 H14"), "Read", "Read the text in this capture", () => OcrClicked?.Invoke(), 52, 40));
        actions.Children.Add(NamedChip(Icon("M9,3 H15 L17,8 H19 L12,15 L5,8 H7 Z M12,15 V21"), "Pin", "Keep this picture on top of other windows", () => PinClicked?.Invoke(), 48, 40));
        actions.Children.Add(NamedChip(Icon("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M8,13 H16 V21 H8 Z"), "Save", "Save as PNG or JPG  (Ctrl+S)", () => SaveClicked?.Invoke(), 52, 40));
        if (showFit)
            actions.Children.Add(NamedChip(Label("Fit"), "Fit", "Fit the image in the window", () => FitClicked?.Invoke(), 48, 40));
        actions.Children.Add(NamedChip(Icon("M7,7 L17,17 M17,7 L7,17"), "Close", "Discard and close  (Esc)", () => CloseClicked?.Invoke(), 52, 40));

        _hint = new TextBlock
        {
            Text = "Copy copies the shot and closes it. Esc closes it too.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 2, 0)
        };

        Child = new StackPanel
        {
            Children = { tools, options, actions, _hint }
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
        if (!_hex.IsKeyboardFocusWithin)
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

    private void CommitHex()
    {
        if (_sync)
            return;
        ColorPicked?.Invoke(_hex.Text);
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

    private void AddTool(Panel panel, EditorTool tool, string name, string tip, string geometry, bool filled = false)
    {
        var chip = NamedChip(Icon(geometry, filled), name, tip, () =>
        {
            if (_sync)
                return;
            ToolPicked?.Invoke(tool);
        }, 54, 48);
        _tools[tool] = chip;
        panel.Children.Add(chip);
    }

    private Border CopyButton()
    {
        var label = new TextBlock
        {
            Text = "Copy",
            Foreground = Palette.Ink,
            FontFamily = Palette.Font,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var icon = Icon("M8,8 H18 V20 H8 Z M6,6 H16 V8", filled: false);
        icon.Stroke = Palette.Ink;
        icon.Width = 18;
        icon.Height = 18;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(icon);
        row.Children.Add(label);
        var button = new Border
        {
            MinWidth = 148,
            Height = 44,
            Margin = new Thickness(2, 2, 8, 2),
            Padding = new Thickness(16, 0, 18, 0),
            CornerRadius = new CornerRadius(12),
            Background = Palette.Text,
            Child = row,
            ToolTip = "Copy the picture and close the shot  (Ctrl+C)",
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.86;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) =>
        {
            CopyClicked?.Invoke();
            e.Handled = true;
        };
        return button;
    }

    private void HighlightTool(EditorTool tool)
    {
        foreach (var (key, chip) in _tools)
            PaintChip(chip, key == tool);
    }

    private static void PaintChip(Border chip, bool on)
    {
        chip.Background = on ? Palette.Text : Brushes.Transparent;
        PaintInk(chip, on ? Palette.Ink : Palette.Text);
    }

    private static void PaintInk(DependencyObject node, Brush ink)
    {
        if (node is Shape shape)
        {
            shape.Stroke = ink;
            if (shape.Fill != null && shape.Fill != Brushes.Transparent)
                shape.Fill = ink;
        }
        else if (node is TextBlock block)
        {
            block.Foreground = ink;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                PaintInk(child, ink);
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

    private Border NamedChip(UIElement icon, string name, string tip, Action click, double width, double height)
    {
        var caption = new TextBlock
        {
            Text = name,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(icon);
        stack.Children.Add(caption);
        var chip = Chip(stack, tip, click);
        chip.Width = width;
        chip.Height = height;
        return chip;
    }

    private static Border Chip(UIElement content, string tip, Action click)
    {
        var chip = new Border
        {
            Margin = new Thickness(2),
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
            StrokeThickness = 1.7,
            Fill = filled ? Palette.Text : Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Palette.Text,
        FontFamily = Palette.Font,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
