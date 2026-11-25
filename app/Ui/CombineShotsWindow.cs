using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

/// <summary>
/// Combines 2–6 recent screenshots into a single Before/After or multi-step image,
/// ready to copy directly into a ticket reply or open in SnapKit's editor for arrows/callouts.
/// </summary>
internal sealed class CombineShotsWindow : Window
{
    private enum LabelMode { None, BeforeAfter, Steps }

    private readonly AppState _state;
    private readonly List<RecentCapture> _available;
    private readonly List<RecentCapture> _selected = new();
    private readonly StackPanel _listPanel;
    private readonly Image _preview;
    private readonly TextBlock _emptyHint;
    private readonly TextBlock _status;
    private readonly Border _sideChip;
    private readonly Border _vertChip;
    private readonly Border _lblNoneChip;
    private readonly Border _lblBeforeAfterChip;
    private readonly Border _lblStepsChip;
    private readonly Border _darkChip;
    private readonly Border _lightChip;

    private MultiShotLayout _layout = MultiShotLayout.SideBySide;
    private LabelMode _labelMode = LabelMode.BeforeAfter;
    private bool _darkTheme = true;

    public CombineShotsWindow(AppState state)
    {
        _state = state;
        _available = state.Recent.Items.ToList();

        // Pre-select the 2 most recent shots in chronological order (older = Before, newer = After)
        if (_available.Count >= 2)
        {
            _selected.Add(_available[1]);
            _selected.Add(_available[0]);
        }

        Title = Brand.Name + " — Combine Recent Shots";
        Width = 940;
        Height = 640;
        MinWidth = 680;
        MinHeight = 460;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        // ── Left: Recent Captures Picker ───────────────────────────────────────
        var leftHeader = new TextBlock
        {
            Text = "RECENT SHOTS (PICK 2–6)",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 8)
        };
        _listPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 12) };
        var leftScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _listPanel
        };
        var leftDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(leftHeader, Dock.Top);
        leftDock.Children.Add(leftHeader);
        leftDock.Children.Add(leftScroll);

        var sidebar = new Border
        {
            Width = 230,
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftDock
        };

        // ── Right: Live Combined Preview ───────────────────────────────────────
        _preview = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(18)
        };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);

        _emptyHint = new TextBlock
        {
            Text = _available.Count < 2
                ? "Take at least 2 screenshots first.\nYour recent captures will appear here automatically for Before/After or Step 1–4 stitching."
                : "Select at least 2 screenshots on the left to combine them.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32)
        };

        var previewHost = new Grid { Background = Palette.Bg, Children = { _preview, _emptyHint } };

        // ── Bottom Controls Bar ────────────────────────────────────────────────
        _sideChip = SegChip("Side by Side", () => { _layout = MultiShotLayout.SideBySide; RefreshAll(); });
        _vertChip = SegChip("Vertical", () => { _layout = MultiShotLayout.Vertical; RefreshAll(); });
        var layoutGroup = SegGroup(_sideChip, _vertChip);

        _lblNoneChip = SegChip("No Labels", () => { _labelMode = LabelMode.None; RefreshAll(); });
        _lblBeforeAfterChip = SegChip("Before / After", () => { _labelMode = LabelMode.BeforeAfter; RefreshAll(); });
        _lblStepsChip = SegChip("Step 1, 2…", () => { _labelMode = LabelMode.Steps; RefreshAll(); });
        var labelGroup = SegGroup(_lblNoneChip, _lblBeforeAfterChip, _lblStepsChip);
        labelGroup.Margin = new Thickness(8, 0, 0, 0);

        _darkChip = SegChip("Dark", () => { _darkTheme = true; RefreshAll(); });
        _lightChip = SegChip("Light", () => { _darkTheme = false; RefreshAll(); });
        var themeGroup = SegGroup(_darkChip, _lightChip);
        themeGroup.Margin = new Thickness(8, 0, 0, 0);

        var optionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { layoutGroup, labelGroup, themeGroup }
        };

        var copyBtn = UiChrome.ActionButton("Copy combined", CopyCombined, ButtonTone.Primary, minWidth: 122);
        copyBtn.Margin = new Thickness(0, 0, 6, 0);
        var editBtn = UiChrome.ActionButton("Open in Editor", OpenInEditor, ButtonTone.Secondary, minWidth: 112);
        editBtn.Margin = new Thickness(0, 0, 6, 0);
        var saveBtn = UiChrome.ActionButton("Save PNG…", SavePng, ButtonTone.Secondary, minWidth: 86);
        saveBtn.Margin = new Thickness(0, 0, 6, 0);
        var printBtn = UiChrome.ActionButton("Print…", PrintCombined, ButtonTone.Secondary, minWidth: 72);
        printBtn.ToolTip = "Print combined image  (Ctrl+P)";

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { copyBtn, editBtn, saveBtn, printBtn }
        };

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(optionsRow, 0);
        Grid.SetColumn(actionRow, 1);
        barGrid.Children.Add(optionsRow);
        barGrid.Children.Add(actionRow);

        _status = new TextBlock
        {
            Text = "",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var bottomBar = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new StackPanel { Orientation = Orientation.Vertical, Children = { barGrid, _status } }
        };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(sidebar, Dock.Left);
        body.Children.Add(sidebar);
        body.Children.Add(previewHost);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bottomBar, Dock.Bottom);
        root.Children.Add(bottomBar);
        root.Children.Add(body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopyCombined();
                e.Handled = true;
            }
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PrintCombined();
                e.Handled = true;
            }
        };

        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        RefreshAll();
    }

    public static void Launch(AppState state)
    {
        foreach (Window win in Application.Current.Windows)
        {
            if (win is CombineShotsWindow existing)
            {
                existing.Close();
                break;
            }
        }
        var winNew = new CombineShotsWindow(state);
        winNew.Show();
        winNew.Activate();
    }

    private static Border SegGroup(params Border[] chips)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in chips)
            row.Children.Add(c);
        return new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row
        };
    }

    private static Border SegChip(string label, Action click)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            }
        };
        b.MouseLeftButtonDown += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    private static void PaintSeg(Border chip, bool active)
    {
        chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
        if (chip.Child is TextBlock tb)
            tb.Foreground = active ? Palette.Ink : Palette.Muted;
    }

    private void RefreshAll()
    {
        PaintSeg(_sideChip, _layout == MultiShotLayout.SideBySide);
        PaintSeg(_vertChip, _layout == MultiShotLayout.Vertical);
        PaintSeg(_lblNoneChip, _labelMode == LabelMode.None);
        PaintSeg(_lblBeforeAfterChip, _labelMode == LabelMode.BeforeAfter);
        PaintSeg(_lblStepsChip, _labelMode == LabelMode.Steps);
        PaintSeg(_darkChip, _darkTheme);
        PaintSeg(_lightChip, !_darkTheme);

        RebuildSidebar();
        RefreshPreview();
    }

    private void RebuildSidebar()
    {
        _listPanel.Children.Clear();
        if (_available.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text = "No recent shots yet.",
                Foreground = Palette.Subtle,
                FontFamily = Palette.Font,
                FontSize = 12,
                Margin = new Thickness(4, 8, 4, 8)
            });
            return;
        }

        foreach (var cap in _available)
        {
            var item = cap;
            int order = _selected.IndexOf(item);
            bool picked = order >= 0;

            var thumb = new Image
            {
                Source = BitmapConvert.ToBitmapSource(item.Image),
                Height = 68,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);

            var badge = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = picked ? Palette.ActiveSurface : Palette.Card,
                BorderBrush = picked ? Palette.ActiveSurface : Palette.LineStrong,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4),
                Child = new TextBlock
                {
                    Text = picked ? (order + 1).ToString() : "",
                    Foreground = Palette.Ink,
                    FontFamily = Palette.Font,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var imgHost = new Grid
            {
                Background = Palette.Bg,
                Children = { thumb, badge }
            };

            var caption = new TextBlock
            {
                Text = item.Label,
                Foreground = picked ? Palette.Text : Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 5, 2, 0)
            };

            var card = new Border
            {
                Background = picked ? Palette.Elevated : Palette.Card,
                BorderBrush = picked ? Palette.ActiveSurface : Palette.Line,
                BorderThickness = new Thickness(picked ? 1.5 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = new StackPanel { Children = { imgHost, caption } }
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (picked)
                    _selected.Remove(item);
                else if (_selected.Count < 6)
                    _selected.Add(item);
                RefreshAll();
            };
            _listPanel.Children.Add(card);
        }
    }

    private void RefreshPreview()
    {
        if (_selected.Count < 2)
        {
            _preview.Source = null;
            _emptyHint.Visibility = Visibility.Visible;
            _status.Foreground = Palette.Muted;
            _status.Text = _available.Count < 2
                ? "Capture 2 or more screenshots to combine them."
                : "Select at least 2 screenshots to preview.";
            return;
        }

        _emptyHint.Visibility = Visibility.Collapsed;
        var combined = RenderCombined();
        _preview.Source = combined;
        _status.Foreground = Palette.Muted;
        _status.Text = $"Combined {_selected.Count} shots  ·  {combined.PixelWidth} × {combined.PixelHeight} px  ·  Ctrl+C copies";
    }

    private string LabelFor(int index, int total) => _labelMode switch
    {
        LabelMode.BeforeAfter => total == 2
            ? (index == 0 ? "Before" : "After")
            : index == 0 ? "Before" : (index == total - 1 ? "After" : $"Step {index + 1}"),
        LabelMode.Steps => $"Step {index + 1}",
        _ => ""
    };

    private BitmapSource RenderCombined()
    {
        int labelH = _labelMode == LabelMode.None ? 0 : 28;
        var sizes = _selected.Select(s => (s.Image.Width, s.Image.Height)).ToList();
        var plan = MultiShotComposer.Measure(sizes, _layout, padding: 20, gap: 16, labelHeight: labelH);

        var bg = _darkTheme ? Palette.Bg : Palette.Freeze(0xF4, 0xF6, 0xF9);
        var border = _darkTheme ? Palette.LineStrong : Palette.Freeze(0xCB, 0xD5, 0xE1);
        var ink = _darkTheme ? Palette.Text : Palette.Freeze(0x0F, 0x17, 0x2A);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(bg, null, new Rect(0, 0, plan.TotalWidth, plan.TotalHeight));
            var pen = new Pen(border, 1);
            pen.Freeze();

            foreach (var slot in plan.Slots)
            {
                var r = new Rect(slot.ImageRect.X, slot.ImageRect.Y, slot.ImageRect.Width, slot.ImageRect.Height);

                if (labelH > 0)
                {
                    string lbl = LabelFor(slot.Index, plan.Slots.Count);
                    var ft = new FormattedText(
                        lbl,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(Palette.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        13,
                        ink,
                        1.0);
                    dc.DrawText(ft, new Point(r.X, slot.LabelY + 4));
                }

                var bmp = BitmapConvert.ToBitmapSource(_selected[slot.Index].Image);
                var clip = new RectangleGeometry(r, 6, 6);
                clip.Freeze();
                dc.PushClip(clip);
                dc.DrawImage(bmp, r);
                dc.Pop();
                dc.DrawRoundedRectangle(null, pen, r, 6, 6);
            }
        }

        var target = new RenderTargetBitmap(plan.TotalWidth, plan.TotalHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private void CopyCombined()
    {
        if (_selected.Count < 2)
            return;
        var img = RenderCombined();
        if (ImageFile.Copy(img))
        {
            _state.Recent.Add(BitmapConvert.ToPixelBuffer(img));
            _status.Foreground = Palette.Copied;
            _status.Text = "Copied combined image! Paste it straight into your ticket or email.";
        }
        else
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Clipboard busy — try again.";
        }
    }

    private void OpenInEditor()
    {
        if (_selected.Count < 2)
            return;
        var buf = BitmapConvert.ToPixelBuffer(RenderCombined());
        var recent = _state.Recent.Add(buf);
        EditorWindow.Open(buf, recent, _state);
        Close();
    }

    private void SavePng()
    {
        if (_selected.Count < 2)
            return;
        if (ImageFile.Save(RenderCombined(), this))
        {
            _status.Foreground = Palette.Copied;
            _status.Text = "Saved combined PNG!";
        }
    }

    private void PrintCombined()
    {
        if (_selected.Count < 2)
            return;
        try
        {
            if (ImageFile.Print(RenderCombined(), this, $"{Brand.Name} Combined Shots"))
            {
                _status.Foreground = Palette.Copied;
                _status.Text = "Sent combined shots to printer!";
            }
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Print failed: " + ex.Message;
        }
    }
}
