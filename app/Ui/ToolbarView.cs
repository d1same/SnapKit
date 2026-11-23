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
    private readonly Dictionary<ShapeFillMode, Border> _fills = new();
    private readonly Dictionary<double, Border> _fonts = new();
    private readonly Dictionary<BackdropPreset, Border> _backdrops = new();

    private readonly Border _colorGroup;
    private readonly Border _strokeGroup;
    private readonly Border _fillGroup;
    private readonly Border _fontGroup;
    private readonly Border _resetStep;
    private readonly TextBlock _resetStepLabel;
    private readonly TextBox _hex;
    private readonly Border _hexSwatch;
    private readonly Border _undo;
    private readonly Border _redo;
    private readonly Border _clear;
    private readonly TextBlock _hint;
    private readonly TextBlock _dims;
    private bool _sync;

    public ToolbarView(bool showFit)
    {
        Background = Palette.Toolbar;
        BorderBrush = Palette.Line;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(10, 9, 10, 8);
        SnapsToDevicePixels = true;
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 3,
            Opacity = 0.5,
            Color = Colors.Black
        };

        // Row 1: Grouped tools
        var toolsRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        var selectGroup = ToolGroup();
        AddTool(selectGroup, EditorTool.Select, "Select", "Select, move, or resize marks  (V)", "M7,4 L7,20 L11,16 L14.5,22 L17.5,20.5 L14,14.5 L20,14.5 Z", filled: true);
        toolsRow.Children.Add(selectGroup);

        var drawGroup = ToolGroup();
        AddTool(drawGroup, EditorTool.Arrow, "Arrow", "Arrow  (A) · Hold Shift for 45°", "M5,19 L19,5 M19,5 L12,6.2 M19,5 L17.8,12");
        AddTool(drawGroup, EditorTool.Rectangle, "Box", "Rectangle  (R) · Hold Shift for square", "M5,5 H19 V19 H5 Z");
        AddTool(drawGroup, EditorTool.Ellipse, "Circle", "Ellipse  (E) · Hold Shift for circle", "M12,5.5 A6.5,6.5 0 1 0 12.1,5.5");
        AddTool(drawGroup, EditorTool.Pen, "Pen", "Freehand pen  (P)", "M4,20 L8.5,18.5 L19,8 L16,5 L5.5,15.5 Z M15.2,6.2 L17.8,8.8");
        AddTool(drawGroup, EditorTool.Highlighter, "Mark", "Highlighter  (H)", "M4,15 L14,5 L19,10 L9,20 Z M4,15 L7.5,18.5");
        toolsRow.Children.Add(drawGroup);

        var annotateGroup = ToolGroup();
        AddTool(annotateGroup, EditorTool.Text, "Text", "Text label  (T)", "M5,5 H19 M12,5 V19 M8,19 H16");
        AddTool(annotateGroup, EditorTool.Callout, "Note", "Callout bubble  (C)", "M4,4 H20 V14 H12 L8,19 V14 H4 Z");
        AddTool(annotateGroup, EditorTool.Number, "Step", "Numbered step badge  (N)", "M12,4.5 A7.5,7.5 0 1 0 12.1,4.5 M12,8 V16 M9.5,10.2 L12,8");
        toolsRow.Children.Add(annotateGroup);

        var privacyGroup = ToolGroup();
        AddTool(privacyGroup, EditorTool.Blur, "Blur", "Pixelate box  (B)", "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z");
        AddTool(privacyGroup, EditorTool.Redact, "Redact", "Solid opaque redaction block  (D)", "M4,6 H20 V18 H4 Z", filled: true);
        AddTool(privacyGroup, EditorTool.Spotlight, "Spot", "Dim everything outside box  (S)", "M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M6,6 L8,8 M16,16 L18,18 M18,6 L16,8 M8,16 L6,18 M12,8.5 A3.5,3.5 0 1 0 12.1,8.5");
        toolsRow.Children.Add(privacyGroup);

        var utilityGroup = ToolGroup();
        AddTool(utilityGroup, EditorTool.Crop, "Crop", "Crop capture area  (X)", "M8,4 V8 H4 M16,4 V8 H20 M20,16 H16 V20 M4,16 H8 V20");
        AddTool(utilityGroup, EditorTool.Picker, "Color", "Pick pixel hex color  (I)", "M14,4 L20,10 L10,20 L6,21 L7,17 Z M8,15 L13,10");
        toolsRow.Children.Add(utilityGroup);

        // Row 2: Contextual style controls + docs backdrop + history
        var styleRow = new WrapPanel { Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        // Color swatches + hex input
        var colorStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var swatch in Palette.Swatches)
        {
            var dot = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                Background = Palette.Brush(swatch),
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(2.5, 0, 2.5, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"Color {swatch}"
            };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                ColorPicked?.Invoke(swatch);
                e.Handled = true;
            };
            _swatches[swatch] = dot;
            colorStack.Children.Add(dot);
        }

        _hexSwatch = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Palette.Brush("#FF3B30"),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _hex = new TextBox
        {
            Text = "#FF3B30",
            Width = 64,
            Height = 22,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Mono,
            FontSize = 11.5,
            CaretBrush = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 7,
            ToolTip = "Hex color — press Enter to apply to the selected mark and next marks"
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
        var hexPill = new Border
        {
            Background = Palette.Field,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 1, 5, 1),
            Margin = new Thickness(6, 0, 2, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _hexSwatch, _hex }
            }
        };
        colorStack.Children.Add(hexPill);
        _colorGroup = StyleGroup(colorStack);
        styleRow.Children.Add(_colorGroup);

        // Stroke size pills (contextual)
        var strokeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var size in new[] { 2d, 3d, 5d, 8d, 12d })
        {
            var dot = new Ellipse
            {
                Width = Math.Clamp(size + 2, 4.5, 14),
                Height = Math.Clamp(size + 2, 4.5, 14),
                Fill = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = CompactChip(dot, $"{size:0} px width  ([ / ])", () => StrokePicked?.Invoke(size), 24, 24);
            _sizes[size] = chip;
            strokeStack.Children.Add(chip);
        }
        _strokeGroup = StyleGroup(strokeStack);
        styleRow.Children.Add(_strokeGroup);

        // Fill mode selector (contextual for Box / Circle)
        var fillStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AddFillChip(fillStack, ShapeFillMode.Outline, "Outline", "Stroke outline only");
        AddFillChip(fillStack, ShapeFillMode.Tint, "Tint", "Outline with translucent tint fill");
        AddFillChip(fillStack, ShapeFillMode.Solid, "Solid", "Solid filled shape");
        _fillGroup = StyleGroup(fillStack);
        styleRow.Children.Add(_fillGroup);

        // Font size selector (contextual for Text / Callout / Step)
        var fontStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var pt in new[] { 14d, 18d, 24d, 32d })
        {
            var text = new TextBlock
            {
                Text = pt.ToString("0"),
                Foreground = Palette.Text,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = CompactChip(text, $"Text size {pt:0} pt", () => FontPicked?.Invoke(pt), 28, 24);
            _fonts[pt] = chip;
            fontStack.Children.Add(chip);
        }
        _fontGroup = StyleGroup(fontStack);
        styleRow.Children.Add(_fontGroup);

        // Reset step #1 pill (contextual for Step tool or when NextNumber > 1)
        _resetStepLabel = new TextBlock
        {
            Text = "Reset #1",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _resetStep = StyleChip(_resetStepLabel, "Reset next step counter back to #1", () => ResetStepClicked?.Invoke());
        _resetStep.Margin = new Thickness(0, 2, 6, 2);
        styleRow.Children.Add(_resetStep);

        // Docs Backdrop Frame selector
        var backdropStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        backdropStack.Children.Add(new TextBlock
        {
            Text = "Frame",
            Foreground = Palette.Subtle,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        });
        AddBackdropChip(backdropStack, BackdropPreset.None, "Off", null, "No backdrop frame");
        AddBackdropChip(backdropStack, BackdropPreset.Clean, "Clean", "#EDF2F7", "Clean light docs frame with soft shadow");
        AddBackdropChip(backdropStack, BackdropPreset.Dark, "Dark", "#1C1E26", "Dark docs frame with soft shadow");
        AddBackdropChip(backdropStack, BackdropPreset.Slate, "Slate", "#3B4C63", "Slate blue docs frame with soft shadow");
        styleRow.Children.Add(StyleGroup(backdropStack));

        // History & Clear group
        var historyStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _undo = CompactIconChip("M8,7 A6,6 0 1 0 14,12 M8,7 V11 M8,7 H12", "Undo", "Undo  (Ctrl+Z)", () => UndoClicked?.Invoke());
        _redo = CompactIconChip("M16,7 A6,6 0 1 1 10,12 M16,7 V11 M16,7 H12", "Redo", "Redo  (Ctrl+Y)", () => RedoClicked?.Invoke());
        _clear = CompactIconChip("M6,7 H18 M9,7 V5 H15 V7 M8,7 L9,19 H15 L16,7", "Clear", "Clear all marks (Ctrl+Z restores them)", () => ClearAllClicked?.Invoke());
        historyStack.Children.Add(_undo);
        historyStack.Children.Add(_redo);
        historyStack.Children.Add(_clear);
        styleRow.Children.Add(StyleGroup(historyStack));

        // Row 3: Primary actions (Electric-Blue Copy + Save / Quick / OCR / Pin / Fit / Close)
        var actionsRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        actionsRow.Children.Add(CopyButton());
        actionsRow.Children.Add(ActionChip("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M8,13 H16 V21 H8 Z", "Save", "Save as PNG or JPG  (Ctrl+S)", () => SaveClicked?.Invoke()));
        actionsRow.Children.Add(ActionChip("M13,3 L5,13 H12 L11,21 L19,11 H12 Z", "Quick", "Quick save PNG to SnapKit folder  (Ctrl+Shift+S)", () => QuickSaveClicked?.Invoke()));
        actionsRow.Children.Add(ActionChip("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M8,12 H16 M8,16 H14", "OCR", "Read & copy text from this capture", () => OcrClicked?.Invoke()));
        actionsRow.Children.Add(ActionChip("M9,3 H15 L17,8 H19 L12,15 L5,8 H7 Z M12,15 V21", "Pin", "Pin capture floating always-on-top", () => PinClicked?.Invoke()));
        if (showFit)
            actionsRow.Children.Add(ActionChip("M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15", "Fit", "Fit to window  (Ctrl+0) · Zoom Ctrl++ / Ctrl+-", () => FitClicked?.Invoke()));
        actionsRow.Children.Add(ActionChip("M7,7 L17,17 M17,7 L7,17", "Close", "Discard edits and close  (Esc)", () => CloseClicked?.Invoke()));

        // Row 4: Live dimensions footer
        _hint = new TextBlock
        {
            Text = "Copy copies and closes · Esc closes",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _dims = new TextBlock
        {
            Text = "0 × 0 px",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dimsPill = new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _dims
        };

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_hint, 0);
        Grid.SetColumn(dimsPill, 1);
        footerGrid.Children.Add(_hint);
        footerGrid.Children.Add(dimsPill);

        var footer = new Border
        {
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(2, 6, 0, 0),
            Child = footerGrid
        };

        Child = new StackPanel
        {
            Children = { toolsRow, styleRow, actionsRow, footer }
        };

        HighlightTool(EditorTool.Arrow);
        HighlightSwatch("#FF3B30");
        HighlightSize(3);
        HighlightFill(ShapeFillMode.Outline);
        HighlightFont(18);
        HighlightBackdrop(BackdropPreset.None);
    }

    public event Action<EditorTool>? ToolPicked;
    public event Action<string>? ColorPicked;
    public event Action<double>? StrokePicked;
    public event Action<double>? FontPicked;
    public event Action<ShapeFillMode>? FillModePicked;
    public event Action<BackdropPreset>? BackdropPicked;
    public event Action? UndoClicked;
    public event Action? RedoClicked;
    public event Action? ClearAllClicked;
    public event Action? ResetStepClicked;
    public event Action? CopyClicked;
    public event Action? SaveClicked;
    public event Action? QuickSaveClicked;
    public event Action? PinClicked;
    public event Action? OcrClicked;
    public event Action? CloseClicked;
    public event Action? FitClicked;

    public void Sync(EditorSession session)
    {
        _sync = true;
        if (!_hex.IsKeyboardFocusWithin)
            _hex.Text = session.ColorHex;
        _hexSwatch.Background = Palette.Brush(session.ColorHex);
        _hint.Text = session.Status;

        HighlightTool(session.Tool);
        HighlightSwatch(session.ColorHex);
        HighlightSize(session.Stroke);
        HighlightFill(session.FillMode);
        HighlightFont(session.FontSize);
        HighlightBackdrop(session.Document.Backdrop);
        UpdateContextualControls(session);
        UpdateDimensions(session);

        _undo.Opacity = session.Document.CanUndo ? 1 : 0.35;
        _redo.Opacity = session.Document.CanRedo ? 1 : 0.35;
        _clear.Opacity = session.Document.Annotations.Count > 0 ? 1 : 0.35;
        _resetStepLabel.Text = session.Document.NextNumber > 1
            ? $"Reset #1 (#{session.Document.NextNumber})"
            : "Step #1";
        _resetStep.Opacity = session.Document.NextNumber > 1 ? 1 : 0.55;
        _sync = false;
    }

    private void UpdateContextualControls(EditorSession session)
    {
        var tool = session.Tool;
        var sel = tool == EditorTool.Select ? session.SelectedKind : null;

        bool showColor = tool is not (EditorTool.Blur or EditorTool.Crop)
            && !(tool == EditorTool.Select && sel == AnnotationKind.Blur);
        bool showStroke = tool is EditorTool.Pen or EditorTool.Line or EditorTool.Arrow or EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Highlighter or EditorTool.Blur
            || (tool == EditorTool.Select && sel is AnnotationKind.Pen or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Highlighter or AnnotationKind.Blur);
        bool showFill = tool is EditorTool.Rectangle or EditorTool.Ellipse
            || (tool == EditorTool.Select && sel is AnnotationKind.Rectangle or AnnotationKind.Ellipse);
        bool showFont = tool is EditorTool.Text or EditorTool.Callout or EditorTool.Number
            || (tool == EditorTool.Select && sel is AnnotationKind.Text or AnnotationKind.Callout or AnnotationKind.Number);
        bool showResetStep = tool == EditorTool.Number || session.Document.NextNumber > 1;

        _colorGroup.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        _strokeGroup.Visibility = showStroke ? Visibility.Visible : Visibility.Collapsed;
        _fillGroup.Visibility = showFill ? Visibility.Visible : Visibility.Collapsed;
        _fontGroup.Visibility = showFont ? Visibility.Visible : Visibility.Collapsed;
        _resetStep.Visibility = showResetStep ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDimensions(EditorSession session)
    {
        int w = session.Document.View.Width;
        int h = session.Document.View.Height;
        var backdrop = session.Document.Backdrop;
        string dimText;
        if (backdrop == BackdropPreset.None)
        {
            dimText = $"{w} × {h} px";
        }
        else
        {
            var metrics = BackdropLayout.Measure(backdrop, w, h);
            dimText = $"{w}×{h} → {metrics.TotalWidth}×{metrics.TotalHeight} · {backdrop}";
        }

        if (session.ZoomPercent is { } zoom)
            dimText += $"  ·  {Math.Round(zoom)}%";

        int marks = session.Document.Annotations.Count;
        if (marks > 0)
            dimText += marks == 1 ? "  ·  1 mark" : $"  ·  {marks} marks";

        _dims.Text = dimText;
    }

    private void CommitHex()
    {
        if (_sync)
            return;
        ColorPicked?.Invoke(_hex.Text);
    }

    private static Border ToolGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        return new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 2, 6, 2),
            SnapsToDevicePixels = true,
            Child = panel
        };
    }

    private static Border StyleGroup(UIElement content) => new()
    {
        Background = Palette.Group,
        BorderBrush = Palette.Divider,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(4, 2, 4, 2),
        Margin = new Thickness(0, 2, 6, 2),
        MinHeight = 30,
        SnapsToDevicePixels = true,
        Child = content
    };

    private void AddTool(Border group, EditorTool tool, string name, string tip, string geometry, bool filled = false)
    {
        var chip = ToolChip(Icon(geometry, filled, 16), name, tip, () =>
        {
            if (_sync)
                return;
            ToolPicked?.Invoke(tool);
        });
        _tools[tool] = chip;
        ((StackPanel)group.Child).Children.Add(chip);
    }

    private void AddFillChip(Panel panel, ShapeFillMode mode, string label, string tip)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var chip = StyleChip(text, tip, () =>
        {
            if (!_sync)
                FillModePicked?.Invoke(mode);
        });
        _fills[mode] = chip;
        panel.Children.Add(chip);
    }

    private void AddBackdropChip(Panel panel, BackdropPreset preset, string label, string? swatchHex, string tip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (swatchHex != null)
        {
            content.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2.5),
                Background = Palette.Brush(swatchHex),
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var chip = StyleChip(content, tip, () =>
        {
            if (!_sync)
                BackdropPicked?.Invoke(preset);
        });
        _backdrops[preset] = chip;
        panel.Children.Add(chip);
    }

    private Border CopyButton()
    {
        var icon = Icon("M8,8 H18 V20 H8 Z M6,6 H16 V8", filled: false, size: 17);
        icon.Stroke = Brushes.White;
        var label = new TextBlock
        {
            Text = "Copy",
            Foreground = Brushes.White,
            FontFamily = Palette.Font,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 8, 0)
        };
        var kbd = new Border
        {
            Background = Palette.Freeze(255, 255, 255, 0x26),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Ctrl+C",
                Foreground = Palette.Freeze(255, 255, 255, 0xE8),
                FontFamily = Palette.Mono,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, label, kbd }
        };
        var button = new Border
        {
            MinWidth = 144,
            Height = 38,
            Margin = new Thickness(0, 2, 8, 2),
            Padding = new Thickness(14, 0, 12, 0),
            CornerRadius = new CornerRadius(10),
            Background = Palette.Accent,
            Child = row,
            ToolTip = "Copy capture to clipboard and close  (Ctrl+C)",
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        button.MouseEnter += (_, _) => button.Background = Palette.AccentHover;
        button.MouseLeave += (_, _) => button.Background = Palette.Accent;
        button.MouseLeftButtonUp += (_, e) =>
        {
            CopyClicked?.Invoke();
            e.Handled = true;
        };
        return button;
    }

    private Border ActionChip(string geometry, string name, string tip, Action click)
    {
        var icon = Icon(geometry, filled: false, size: 15);
        var caption = new TextBlock
        {
            Text = name,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, caption }
        };
        var chip = new Border
        {
            Height = 38,
            Margin = new Thickness(0, 2, 5, 2),
            Padding = new Thickness(10, 0, 11, 0),
            CornerRadius = new CornerRadius(9),
            Background = Palette.Elevated,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Child = row,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        chip.MouseEnter += (_, _) => chip.Background = Palette.Hover;
        chip.MouseLeave += (_, _) => chip.Background = Palette.Elevated;
        chip.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return chip;
    }

    private Border ToolChip(UIElement icon, string name, string tip, Action click)
    {
        var caption = new TextBlock
        {
            Text = name,
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, caption }
        };
        var chip = new Border
        {
            Width = 46,
            Height = 40,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = stack,
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

    private Border CompactIconChip(string geometry, string label, string tip, Action click)
    {
        var icon = Icon(geometry, filled: false, size: 13);
        var text = new TextBlock
        {
            Text = label,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, text }
        };
        return StyleChip(row, tip, click);
    }

    private static Border CompactChip(UIElement content, string tip, Action click, double width, double height)
    {
        var chip = new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(6),
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

    private static Border StyleChip(UIElement content, string tip, Action click)
    {
        var chip = new Border
        {
            Height = 24,
            Margin = new Thickness(1),
            Padding = new Thickness(7, 0, 7, 0),
            CornerRadius = new CornerRadius(6),
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

    private void HighlightTool(EditorTool tool)
    {
        foreach (var (key, chip) in _tools)
        {
            bool active = key == tool;
            chip.Background = active ? Palette.Accent : Brushes.Transparent;
            PaintInk(chip, active ? Brushes.White : Palette.Text, active ? Brushes.White : Palette.Muted);
        }
    }

    private void HighlightSwatch(string hex)
    {
        foreach (var (key, chip) in _swatches)
        {
            bool active = key.Equals(hex, StringComparison.OrdinalIgnoreCase);
            chip.BorderBrush = active ? Brushes.White : Palette.Line;
            chip.BorderThickness = new Thickness(active ? 2.2 : 1.2);
        }
    }

    private void HighlightSize(double stroke)
    {
        double nearest = _sizes.Keys.OrderBy(size => Math.Abs(size - stroke)).First();
        foreach (var (size, chip) in _sizes)
            chip.Background = Math.Abs(size - nearest) < 0.1 ? Palette.Accent : Brushes.Transparent;
    }

    private void HighlightFill(ShapeFillMode mode)
    {
        foreach (var (key, chip) in _fills)
        {
            bool active = key == mode;
            chip.Background = active ? Palette.Accent : Brushes.Transparent;
            PaintInk(chip, active ? Brushes.White : Palette.Text, active ? Brushes.White : Palette.Text);
        }
    }

    private void HighlightFont(double fontSize)
    {
        double nearest = _fonts.Keys.OrderBy(pt => Math.Abs(pt - fontSize)).First();
        foreach (var (pt, chip) in _fonts)
        {
            bool active = Math.Abs(pt - nearest) < 0.1;
            chip.Background = active ? Palette.Accent : Brushes.Transparent;
            PaintInk(chip, active ? Brushes.White : Palette.Text, active ? Brushes.White : Palette.Text);
        }
    }

    private void HighlightBackdrop(BackdropPreset preset)
    {
        foreach (var (key, chip) in _backdrops)
        {
            bool active = key == preset;
            chip.Background = active ? Palette.Accent : Brushes.Transparent;
            PaintTextOnly(chip, active ? Brushes.White : Palette.Text);
        }
    }

    private static void PaintInk(DependencyObject node, Brush shapeInk, Brush labelInk)
    {
        if (node is Shape shape)
        {
            shape.Stroke = shapeInk;
            if (shape.Fill != null && shape.Fill != Brushes.Transparent)
                shape.Fill = shapeInk;
        }
        else if (node is TextBlock block)
        {
            block.Foreground = labelInk;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                PaintInk(child, shapeInk, labelInk);
        }
    }

    private static void PaintTextOnly(DependencyObject node, Brush textInk)
    {
        if (node is TextBlock block)
            block.Foreground = textInk;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                PaintTextOnly(child, textInk);
        }
    }

    private static Shape Icon(string geometry, bool filled = false, double size = 16) => new Path
    {
        Data = Geometry.Parse(geometry),
        Stroke = Palette.Text,
        StrokeThickness = 1.65,
        Fill = filled ? Palette.Text : Brushes.Transparent,
        Stretch = Stretch.Uniform,
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round
    };
}
