using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SnapKit;

/// <summary>
/// Style 3 — Ultra-Minimal Rail UI:
/// Top floating pill: slim single-row tool rail (icons + subtle shortcut letters, Undo/Redo/Clear,
/// compact OCR/Pin/Save, optional live zoom controls, crisp white Copy CTA, and Close).
/// Bottom detached floating pill: contextual options for the active tool/selection
/// (color swatches + #HEX, stroke dots OR text size stepper OR shape fill toggle, Frame toggle,
/// and live W×H pixel dimensions + green "Copied!" feedback).
/// </summary>
internal sealed class ToolbarView : StackPanel
{
    private readonly Dictionary<EditorTool, Border> _tools = new();
    private readonly Dictionary<string, Border> _swatches = new();
    private readonly Dictionary<double, Border> _sizes = new();
    private readonly Dictionary<ShapeFillMode, Border> _fills = new();
    private readonly Dictionary<BackdropPreset, Border> _backdrops = new();

    private readonly StackPanel _colorGroup;
    private readonly FrameworkElement _colorSep;
    private readonly StackPanel _strokeGroup;
    private readonly FrameworkElement _strokeSep;
    private readonly StackPanel _fillGroup;
    private readonly FrameworkElement _fillSep;
    private readonly StackPanel _fontGroup;
    private readonly FrameworkElement _fontSep;
    private readonly TextBlock _fontValue;
    private readonly Border _resetStep;
    private readonly TextBlock _resetStepLabel;
    private readonly TextBox _hex;
    private readonly Border _hexSwatch;
    private readonly Border _undo;
    private readonly Border _redo;
    private readonly Border _clear;
    private readonly Border _copyButton;
    private readonly TextBlock _copyLabel;
    private readonly UIElement _copyIcon;
    private readonly TextBlock? _zoomText;
    private readonly TextBlock _dims;
    private readonly Border _copiedPill;
    private readonly TextBlock _copiedText;
    private readonly TextBlock _statusText;
    private bool _sync;

    public ToolbarView(bool showFit)
    {
        Orientation = Orientation.Vertical;
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Left;
        SnapsToDevicePixels = true;

        // ── Top Pill: Slim Single-Row Floating Tool Rail ────────────────────────────
        var railRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        AddTool(railRow, EditorTool.Select, "V", "Select, move, or drag handles  (V)", LucideIcon.Select);
        railRow.Children.Add(Divider());

        AddTool(railRow, EditorTool.Arrow, "A", "Arrow  (A) · Hold Shift for 45°", LucideIcon.Arrow);
        AddTool(railRow, EditorTool.Pen, "P", "Freehand pen  (P)", LucideIcon.Pen);
        AddTool(railRow, EditorTool.Rectangle, "R", "Rectangle  (R) · Shift = square", LucideIcon.Rectangle);
        AddTool(railRow, EditorTool.Ellipse, "E", "Ellipse  (E) · Shift = circle", LucideIcon.Ellipse);
        AddTool(railRow, EditorTool.Highlighter, "H", "Highlighter  (H)", LucideIcon.Highlighter);
        railRow.Children.Add(Divider());

        AddTool(railRow, EditorTool.Text, "T", "Text  (T)", LucideIcon.Text);
        AddTool(railRow, EditorTool.Callout, "C", "Callout note  (C)", LucideIcon.Callout);
        AddTool(railRow, EditorTool.Number, "N", "Step number  (N)", LucideIcon.Number);
        railRow.Children.Add(Divider());

        AddTool(railRow, EditorTool.Blur, "B", "Pixelate blur  (B)", LucideIcon.Blur);
        AddTool(railRow, EditorTool.Redact, "D", "Redact blackout box  (D)", LucideIcon.Redact);
        AddTool(railRow, EditorTool.Spotlight, "S", "Spotlight focus  (S)", LucideIcon.Spotlight);
        railRow.Children.Add(Divider());

        AddTool(railRow, EditorTool.Crop, "X", "Crop area  (X)", LucideIcon.Crop);
        AddTool(railRow, EditorTool.Picker, "I", "Eyedropper — pick & copy #HEX  (I)", LucideIcon.Eyedropper);
        railRow.Children.Add(Divider());

        _undo = RailIconChip(LucideIcon.Undo, "Undo  (Ctrl+Z)", () => UndoClicked?.Invoke());
        _redo = RailIconChip(LucideIcon.Redo, "Redo  (Ctrl+Y)", () => RedoClicked?.Invoke());
        _clear = RailIconChip(LucideIcon.Trash, "Clear all marks", () => ClearAllClicked?.Invoke());
        railRow.Children.Add(_undo);
        railRow.Children.Add(_redo);
        railRow.Children.Add(_clear);
        railRow.Children.Add(Divider());

        railRow.Children.Add(RailCompactAction(LucideIcon.Ocr, "OCR", "Copy text from capture (OCR)", () => OcrClicked?.Invoke()));
        railRow.Children.Add(RailCompactAction(LucideIcon.Pin, "Pin", "Pin floating on top", () => PinClicked?.Invoke()));
        railRow.Children.Add(RailCompactAction(LucideIcon.Save, "Save", "Save as… (Ctrl+S) · Quick Save (Ctrl+Shift+S)", () => SaveClicked?.Invoke()));

        if (showFit)
        {
            railRow.Children.Add(Divider());
            railRow.Children.Add(RailIconChip(LucideIcon.ZoomOut, "Zoom out  (Ctrl+-)", () => ZoomOutClicked?.Invoke()));
            _zoomText = new TextBlock
            {
                Text = "100%",
                Foreground = Palette.Text,
                FontFamily = Palette.Mono,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            var zoomReadout = PillChip(_zoomText, "Fit to window  (Ctrl+0) · Ctrl+1 for 100%", () => FitClicked?.Invoke());
            zoomReadout.MinWidth = 44;
            railRow.Children.Add(zoomReadout);
            railRow.Children.Add(RailIconChip(LucideIcon.ZoomIn, "Zoom in  (Ctrl++)", () => ZoomInClicked?.Invoke()));
        }

        railRow.Children.Add(Divider());

        // Crisp white Copy CTA on the right
        _copyIcon = LucideIcons.Create(LucideIcon.Copy, Palette.Ink, size: 14, strokeWidth: 2.0);
        _copyLabel = new TextBlock
        {
            Text = "Copy",
            Foreground = Palette.Ink,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        _copyButton = new Border
        {
            Height = 28,
            Padding = new Thickness(11, 0, 12, 0),
            Margin = new Thickness(2, 1, 3, 1),
            CornerRadius = new CornerRadius(7),
            Background = Palette.ActiveSurface,
            Cursor = Cursors.Hand,
            ToolTip = "Copy to clipboard and close  (Ctrl+C)",
            SnapsToDevicePixels = true,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _copyIcon, _copyLabel }
            }
        };
        _copyButton.MouseEnter += (_, _) => _copyButton.Background = Palette.ActiveHover;
        _copyButton.MouseLeave += (_, _) => _copyButton.Background = Palette.ActiveSurface;
        _copyButton.MouseLeftButtonUp += (_, e) =>
        {
            CopyClicked?.Invoke();
            e.Handled = true;
        };
        railRow.Children.Add(_copyButton);

        var closeBtn = RailIconChip(LucideIcon.Close, "Discard & close  (Esc)", () => CloseClicked?.Invoke());
        railRow.Children.Add(closeBtn);

        var topRail = FloatingPill(railRow, cornerRadius: 11, padding: new Thickness(5, 4, 5, 4));
        Children.Add(topRail);

        // ── Bottom Detached Pill: Contextual Options + Frame + Live W×H ────────────
        var optRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        // Color swatches + #HEX
        _colorGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var swatch in Palette.Swatches)
        {
            var dot = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = Palette.Brush(swatch),
                BorderBrush = Palette.LineStrong,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = swatch
            };
            dot.MouseLeftButtonUp += (_, e) =>
            {
                ColorPicked?.Invoke(swatch);
                e.Handled = true;
            };
            _swatches[swatch] = dot;
            _colorGroup.Children.Add(dot);
        }

        _hexSwatch = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = Palette.Brush("#FF3B30"),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _hex = new TextBox
        {
            Text = "#FF3B30",
            Width = 58,
            Height = 20,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Mono,
            FontSize = 11,
            CaretBrush = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 7,
            ToolTip = "Hex color — press Enter to apply"
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
        _colorGroup.Children.Add(new Border
        {
            Background = Palette.Field,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5, 0, 4, 0),
            Margin = new Thickness(5, 0, 1, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _hexSwatch, _hex } }
        });
        _colorSep = SmallDivider();
        optRow.Children.Add(_colorGroup);
        optRow.Children.Add(_colorSep);

        // Stroke dots
        _strokeGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var size in new[] { 2d, 3d, 5d, 8d, 12d })
        {
            var dot = new Ellipse
            {
                Width = Math.Clamp(size + 1.5, 4, 12),
                Height = Math.Clamp(size + 1.5, 4, 12),
                Fill = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = FixedChip(dot, $"{size:0} px  ([ / ])", () => StrokePicked?.Invoke(size), 22, 22);
            _sizes[size] = chip;
            _strokeGroup.Children.Add(chip);
        }
        _strokeSep = SmallDivider();
        optRow.Children.Add(_strokeGroup);
        optRow.Children.Add(_strokeSep);

        // Shape fill toggle (Outline / Tint / Solid)
        _fillGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        AddFillChip(_fillGroup, ShapeFillMode.Outline, "Outline", "Stroke outline only");
        AddFillChip(_fillGroup, ShapeFillMode.Tint, "Tint", "Outline with subtle translucent fill");
        AddFillChip(_fillGroup, ShapeFillMode.Solid, "Solid", "Solid filled shape");
        _fillSep = SmallDivider();
        optRow.Children.Add(_fillGroup);
        optRow.Children.Add(_fillSep);

        // Text size stepper (− 18 +)
        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _fontValue = new TextBlock
        {
            Text = "18",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 22,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _fontGroup.Children.Add(FixedChip(MiniLabel("−"), "Smaller text", () => FontStepped?.Invoke(-2), 20, 22));
        _fontGroup.Children.Add(_fontValue);
        _fontGroup.Children.Add(FixedChip(MiniLabel("+"), "Larger text", () => FontStepped?.Invoke(2), 20, 22));

        _resetStepLabel = new TextBlock
        {
            Text = "Reset #1",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _resetStep = PillChip(_resetStepLabel, "Reset next step counter back to #1", () => ResetStepClicked?.Invoke());
        _resetStep.Margin = new Thickness(4, 0, 0, 0);
        _fontGroup.Children.Add(_resetStep);

        _fontSep = SmallDivider();
        optRow.Children.Add(_fontGroup);
        optRow.Children.Add(_fontSep);

        // Docs Backdrop Frame toggle
        var frameGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        frameGroup.Children.Add(new TextBlock
        {
            Text = "Frame",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 4, 0)
        });
        AddBackdropChip(frameGroup, BackdropPreset.None, "Off", "No backdrop frame");
        AddBackdropChip(frameGroup, BackdropPreset.Clean, "Clean", "Clean light docs frame with soft shadow");
        AddBackdropChip(frameGroup, BackdropPreset.Dark, "Dark", "Dark graphite docs frame");
        AddBackdropChip(frameGroup, BackdropPreset.Slate, "Slate", "Slate docs frame");
        optRow.Children.Add(frameGroup);
        optRow.Children.Add(SmallDivider());

        // Live W×H dimensions + optional status / green "Copied!" badge
        _dims = new TextBlock
        {
            Text = "0 × 0",
            Foreground = Palette.Muted,
            FontFamily = Palette.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = "Live capture dimensions (pixels)"
        };
        optRow.Children.Add(_dims);

        _statusText = new TextBlock
        {
            Text = "",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            Visibility = Visibility.Collapsed
        };
        optRow.Children.Add(_statusText);

        _copiedText = new TextBlock
        {
            Text = "Copied!",
            Foreground = Palette.Copied,
            FontFamily = Palette.Font,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _copiedPill = new Border
        {
            Background = Palette.CopiedSoft,
            BorderBrush = Palette.Copied,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(6, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = _copiedText
        };
        optRow.Children.Add(_copiedPill);

        var optionsPill = FloatingPill(optRow, cornerRadius: 9, padding: new Thickness(8, 4, 8, 4));
        optionsPill.Margin = new Thickness(0, 6, 0, 0);
        optionsPill.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(optionsPill);

        HighlightTool(EditorTool.Arrow);
        HighlightSwatch("#FF3B30");
        HighlightSize(3);
        HighlightFill(ShapeFillMode.Outline);
        HighlightBackdrop(BackdropPreset.None);
    }

    public event Action<EditorTool>? ToolPicked;
    public event Action<string>? ColorPicked;
    public event Action<double>? StrokePicked;
    public event Action<double>? FontStepped;
    public event Action<ShapeFillMode>? FillModePicked;
    public event Action<BackdropPreset>? BackdropPicked;
    public event Action? UndoClicked;
    public event Action? RedoClicked;
    public event Action? ClearAllClicked;
    public event Action? ResetStepClicked;
    public event Action? CopyClicked;
    public event Action? SaveClicked;
    public event Action? PinClicked;
    public event Action? OcrClicked;
    public event Action? CloseClicked;
    public event Action? FitClicked;
    public event Action? ZoomInClicked;
    public event Action? ZoomOutClicked;

    public void Sync(EditorSession session)
    {
        _sync = true;
        string activeColor = session.ActiveColorHex;
        if (!_hex.IsKeyboardFocusWithin)
            _hex.Text = activeColor;
        _hexSwatch.Background = Palette.Brush(activeColor);
        _fontValue.Text = session.FontSize.ToString("0");

        HighlightTool(session.Tool);
        HighlightSwatch(activeColor);
        HighlightSize(session.Stroke);
        HighlightFill(session.FillMode);
        HighlightBackdrop(session.Document.Backdrop);
        UpdateContextualControls(session);
        UpdateDimensionsAndFeedback(session);

        if (_zoomText != null && session.ZoomPercent is { } pct)
            _zoomText.Text = $"{Math.Round(pct)}%";

        _undo.Opacity = session.Document.CanUndo ? 1 : 0.32;
        _redo.Opacity = session.Document.CanRedo ? 1 : 0.32;
        _clear.Opacity = session.Document.Annotations.Count > 0 ? 1 : 0.32;
        _resetStepLabel.Text = session.Document.NextNumber > 1 ? $"Reset #1 (#{session.Document.NextNumber})" : "Step #1";
        _resetStep.Opacity = session.Document.NextNumber > 1 ? 1 : 0.5;
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
        _colorSep.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;

        _strokeGroup.Visibility = showStroke ? Visibility.Visible : Visibility.Collapsed;
        _strokeSep.Visibility = showStroke ? Visibility.Visible : Visibility.Collapsed;

        _fillGroup.Visibility = showFill ? Visibility.Visible : Visibility.Collapsed;
        _fillSep.Visibility = showFill ? Visibility.Visible : Visibility.Collapsed;

        bool showFontArea = showFont || showResetStep;
        _fontGroup.Visibility = showFontArea ? Visibility.Visible : Visibility.Collapsed;
        _fontSep.Visibility = showFontArea ? Visibility.Visible : Visibility.Collapsed;
        // Hide stepper controls inside _fontGroup if only Reset #1 is visible
        for (int i = 0; i < 3 && i < _fontGroup.Children.Count; i++)
            _fontGroup.Children[i].Visibility = showFont ? Visibility.Visible : Visibility.Collapsed;
        _resetStep.Visibility = showResetStep ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDimensionsAndFeedback(EditorSession session)
    {
        int w = session.Document.View.Width;
        int h = session.Document.View.Height;
        if (session.Document.Backdrop == BackdropPreset.None)
        {
            _dims.Text = $"{w}×{h}";
        }
        else
        {
            var m = BackdropLayout.Measure(session.Document.Backdrop, w, h);
            _dims.Text = $"{w}×{h} → {m.TotalWidth}×{m.TotalHeight}";
        }

        if (session.IsCopiedFeedback && !string.IsNullOrWhiteSpace(session.Status))
        {
            _copiedText.Text = session.Status;
            _copiedPill.Visibility = Visibility.Visible;
            _statusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _copiedPill.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrWhiteSpace(session.Status) && session.Status.Length <= 42)
            {
                _statusText.Text = "· " + session.Status;
                _statusText.Visibility = Visibility.Visible;
            }
            else
            {
                _statusText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void CommitHex()
    {
        if (_sync)
            return;
        ColorPicked?.Invoke(_hex.Text);
    }

    private static Border FloatingPill(UIElement child, double cornerRadius, Thickness padding) => new()
    {
        Background = Palette.Toolbar,
        BorderBrush = Palette.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(cornerRadius),
        Padding = padding,
        SnapsToDevicePixels = true,
        Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.48,
            Color = Colors.Black
        },
        Child = child
    };

    private static Border Divider() => new()
    {
        Width = 1,
        Height = 18,
        Background = Palette.Divider,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border SmallDivider() => new()
    {
        Width = 1,
        Height = 14,
        Background = Palette.Divider,
        Margin = new Thickness(6, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AddTool(Panel panel, EditorTool tool, string key, string tip, LucideIcon glyph)
    {
        var icon = LucideIcons.Create(glyph, Palette.Text, size: 15, strokeWidth: 1.9);
        icon.Margin = new Thickness(0, 0, 2, 2);
        var badge = new TextBlock
        {
            Text = key,
            Foreground = Palette.Subtle,
            FontFamily = Palette.Font,
            FontSize = 7.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2.5, 1),
            IsHitTestVisible = false
        };
        var host = new Grid { Children = { icon, badge } };
        var chip = FixedChip(host, tip, () =>
        {
            if (!_sync)
                ToolPicked?.Invoke(tool);
        }, 30, 28);
        chip.CornerRadius = new CornerRadius(7);
        _tools[tool] = chip;
        panel.Children.Add(chip);
    }

    private Border RailIconChip(LucideIcon glyph, string tip, Action click)
    {
        var chip = FixedChip(LucideIcons.Create(glyph, Palette.Text, size: 14.5, strokeWidth: 1.9), tip, click, 28, 28);
        chip.CornerRadius = new CornerRadius(7);
        return chip;
    }

    private Border RailCompactAction(LucideIcon glyph, string label, string tip, Action click)
    {
        var icon = LucideIcons.Create(glyph, Palette.Text, size: 13.5, strokeWidth: 1.9);
        var text = new TextBlock
        {
            Text = label,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        var chip = PillChip(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, text }
        }, tip, click);
        chip.Height = 28;
        chip.CornerRadius = new CornerRadius(7);
        chip.Padding = new Thickness(7, 0, 8, 0);
        return chip;
    }

    private void AddFillChip(Panel panel, ShapeFillMode mode, string label, string tip)
    {
        var chip = PillChip(MiniLabel(label, 10.5), tip, () =>
        {
            if (!_sync)
                FillModePicked?.Invoke(mode);
        });
        _fills[mode] = chip;
        panel.Children.Add(chip);
    }

    private void AddBackdropChip(Panel panel, BackdropPreset preset, string label, string tip)
    {
        var chip = PillChip(MiniLabel(label, 10.5), tip, () =>
        {
            if (!_sync)
                BackdropPicked?.Invoke(preset);
        });
        _backdrops[preset] = chip;
        panel.Children.Add(chip);
    }

    private static Border FixedChip(UIElement content, string tip, Action click, double width, double height)
    {
        var chip = new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Child = content,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        AttachChipBehavior(chip, click);
        return chip;
    }

    private static Border PillChip(UIElement content, string tip, Action click)
    {
        var chip = new Border
        {
            Height = 22,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(5.5),
            Background = Brushes.Transparent,
            Child = content,
            ToolTip = tip,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true
        };
        AttachChipBehavior(chip, click);
        return chip;
    }

    private static void AttachChipBehavior(Border chip, Action click)
    {
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
    }

    private void HighlightTool(EditorTool tool)
    {
        foreach (var (key, chip) in _tools)
        {
            bool active = key == tool;
            chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
            PaintInk(chip, active ? Palette.Ink : Palette.Text, active ? Palette.Ink : Palette.Subtle);
        }
    }

    private void HighlightSwatch(string hex)
    {
        foreach (var (key, chip) in _swatches)
        {
            bool active = key.Equals(hex, StringComparison.OrdinalIgnoreCase);
            chip.BorderBrush = active ? Palette.Text : Palette.LineStrong;
            chip.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    private void HighlightSize(double stroke)
    {
        double nearest = _sizes.Keys.OrderBy(size => Math.Abs(size - stroke)).First();
        foreach (var (size, chip) in _sizes)
        {
            bool active = Math.Abs(size - nearest) < 0.1;
            chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
            if (chip.Child is Ellipse dot)
                dot.Fill = active ? Palette.Ink : Palette.Text;
        }
    }

    private void HighlightFill(ShapeFillMode mode)
    {
        foreach (var (key, chip) in _fills)
        {
            bool active = key == mode;
            chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
            PaintTextOnly(chip, active ? Palette.Ink : Palette.Muted);
        }
    }

    private void HighlightBackdrop(BackdropPreset preset)
    {
        foreach (var (key, chip) in _backdrops)
        {
            bool active = key == preset;
            chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
            PaintTextOnly(chip, active ? Palette.Ink : Palette.Muted);
        }
    }

    private static void PaintInk(DependencyObject node, Brush shapeInk, Brush labelInk)
    {
        if (node is Viewbox vb)
        {
            LucideIcons.Recolor(vb, shapeInk);
            return;
        }
        if (node is Shape shape)
        {
            shape.Stroke = shapeInk;
            if (shape.Fill != null && shape.Fill != Brushes.Transparent)
                shape.Fill = shapeInk;
            return;
        }
        if (node is TextBlock block)
        {
            block.Foreground = labelInk;
            return;
        }
        if (node is Decorator dec && dec.Child is DependencyObject decChild)
        {
            PaintInk(decChild, shapeInk, labelInk);
            return;
        }
        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is DependencyObject dep)
                    PaintInk(dep, shapeInk, labelInk);
            }
            return;
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
        if (node is Decorator dec && dec.Child is DependencyObject decChild)
        {
            PaintTextOnly(decChild, textInk);
            return;
        }
        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is DependencyObject dep)
                    PaintTextOnly(dep, textInk);
            }
            return;
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                PaintTextOnly(child, textInk);
        }
    }

    private static TextBlock MiniLabel(string text, double size = 12) => new()
    {
        Text = text,
        Foreground = Palette.Text,
        FontFamily = Palette.Font,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
