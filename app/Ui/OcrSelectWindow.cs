using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

/// <summary>
/// Snipping Tool style: recognized words are highlighted on the capture.
/// Drag across the ones you want and that text is copied — either as plain text
/// or as a tab-separated table ready to paste directly into Excel / Google Sheets.
/// </summary>
internal sealed class OcrSelectWindow : Window
{
    private static readonly SolidColorBrush Idle = Palette.Freeze(242, 242, 242, 0x26);
    private static readonly SolidColorBrush Hot = Palette.Freeze(242, 242, 242, 0x48);
    private static readonly SolidColorBrush On = Palette.Freeze(242, 242, 242, 0x72);
    private static readonly SolidColorBrush BoxEdge = Palette.Freeze(21, 21, 21, 0x88);
    private static readonly SolidColorBrush BoxEdgeOn = Palette.Freeze(242, 242, 242, 0xE6);
    private static readonly SolidColorBrush GuideLight = Palette.Freeze(242, 242, 242, 0x8C);
    private static readonly SolidColorBrush GuideDark = Palette.Freeze(21, 21, 21, 0x99);

    private readonly PixelBuffer _image;
    private readonly OcrPage _page;
    private readonly OcrTableResult _fullTable;
    private readonly Image _picture;
    private readonly Canvas _marks;
    private readonly Canvas _guides;
    private readonly TextBlock _status;
    private readonly Border _textModeBtn;
    private readonly Border _tableModeBtn;
    private readonly TextBlock _textModeLabel;
    private readonly TextBlock _tableModeLabel;
    private readonly UIElement _textModeIcon;
    private readonly UIElement _tableModeIcon;
    private readonly Button? _copyAllBtn;
    private readonly List<Border> _boxes = new();
    private readonly HashSet<int> _selected = new();
    private Rectangle? _band;
    private bool _tableMode;
    private bool _dragging;
    private bool _moved;
    private Point _start;
    private int _hover = -1;
    private double _builtWidth;
    private double _builtHeight;

    public OcrSelectWindow(PixelBuffer image, OcrPage page)
    {
        _image = image;
        _page = page;
        _fullTable = page.TableAll();
        _tableMode = _fullTable.IsLikelyTable;

        Title = Brand.Name + " OCR";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Palette.Bg;
        ShowInTaskbar = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = true;

        _picture = new Image
        {
            Source = BitmapConvert.ToBitmapSource(image),
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.NearestNeighbor);
        _guides = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true
        };
        _marks = new Canvas
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true
        };
        var stage = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _picture, _guides, _marks }
        };
        stage.MouseLeftButtonDown += OnDown;
        stage.MouseMove += OnMove;
        stage.MouseLeftButtonUp += OnUp;
        _marks.SizeChanged += (_, _) => BuildMarks();
        _marks.LostMouseCapture += (_, _) => EndDrag();

        // Style 3 segmented mode toggle: Text | Table (Excel)
        (_textModeBtn, _textModeIcon, _textModeLabel) = ModeChip(
            LucideIcon.AlignLeft,
            "Text",
            "Copy as plain reading-order text  (Tab to toggle)",
            () => SetTableMode(false, recopyIfSelected: true));
        (_tableModeBtn, _tableModeIcon, _tableModeLabel) = ModeChip(
            LucideIcon.Table,
            "Table",
            "Copy as Excel / Google Sheets table (TSV)  (Tab to toggle)",
            () => SetTableMode(true, recopyIfSelected: true));
        _textModeBtn.Margin = new Thickness(0, 0, 2, 0);

        var modeSwitch = new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _textModeBtn, _tableModeBtn }
            }
        };

        _status = new TextBlock
        {
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        if (!string.IsNullOrWhiteSpace(page.Text) || page.Words.Count > 0)
        {
            _copyAllBtn = UiChrome.ActionButton("Copy all", CopyAll, ButtonTone.Primary, minWidth: 102);
            _copyAllBtn.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(_copyAllBtn);
        }
        var close = UiChrome.ActionButton("Close", Close, ButtonTone.Secondary, minWidth: 74);
        buttons.Children.Add(close);

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (page.Words.Count > 0)
        {
            Grid.SetColumn(modeSwitch, 0);
            barGrid.Children.Add(modeSwitch);
        }
        Grid.SetColumn(_status, 1);
        Grid.SetColumn(buttons, 2);
        barGrid.Children.Add(_status);
        barGrid.Children.Add(buttons);

        var bar = new Border
        {
            Height = 52,
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = barGrid
        };

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { At(bar, Dock.Bottom), stage }
        };

        UpdateModeChrome();
        ResetStatusHint();

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => PlaceOnMonitor();
    }

    private static (Border chip, UIElement icon, TextBlock label) ModeChip(LucideIcon kind, string text, string tip, Action onClick)
    {
        var icon = LucideIcons.Create(kind, Palette.Muted, size: 13.5, strokeWidth: 1.9);
        var label = new TextBlock
        {
            Text = text,
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, label }
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 10, 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = row
        };
        chip.MouseLeftButtonDown += (_, e) =>
        {
            onClick();
            e.Handled = true;
        };
        return (chip, icon, label);
    }

    private void SetTableMode(bool enabled, bool recopyIfSelected)
    {
        if (_tableMode == enabled)
            return;
        _tableMode = enabled;
        UpdateModeChrome();
        BuildGuides();

        if (recopyIfSelected && _selected.Count > 0)
            CopyCurrentSelection();
        else
            ResetStatusHint();
    }

    private void UpdateModeChrome()
    {
        ApplyModeChip(_textModeBtn, _textModeIcon, _textModeLabel, active: !_tableMode);
        ApplyModeChip(_tableModeBtn, _tableModeIcon, _tableModeLabel, active: _tableMode);
        if (_copyAllBtn != null)
            _copyAllBtn.Content = _tableMode ? "Copy table" : "Copy all";
    }

    private static void ApplyModeChip(Border chip, UIElement icon, TextBlock label, bool active)
    {
        chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
        var ink = active ? Palette.Ink : Palette.Muted;
        label.Foreground = ink;
        LucideIcons.Recolor(icon, ink);
    }

    private void ResetStatusHint()
    {
        _status.Foreground = Palette.Muted;
        if (_page.Words.Count == 0)
        {
            _status.Text = "No text found in that area.";
            return;
        }

        if (_tableMode)
        {
            string dims = _fullTable.ColumnCount >= 2
                ? $"Table ({_fullTable.ColumnCount} cols × {_fullTable.RowCount} rows)"
                : "Table mode";
            _status.Text = $"{dims}  ·  Drag to copy for Excel  ·  Ctrl+A copies all  ·  Tab switches mode";
        }
        else
        {
            _status.Text = $"{_page.Words.Count} {(_page.Words.Count == 1 ? "word" : "words")} found  ·  Drag to copy  ·  Tab for Excel table  ·  Esc closes";
        }
    }

    private void PlaceOnMonitor()
    {
        var monitor = MonitorQuery.UnderCursor();
        double scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
        int bar = (int)Math.Ceiling(52 * scale);
        int maxW = Math.Max(240, monitor.Width - 80);
        int maxH = Math.Max(160, monitor.Height - bar - 80);
        double fit = Math.Min(1, Math.Min(maxW / (double)Math.Max(1, _image.Width), maxH / (double)Math.Max(1, _image.Height)));
        if (double.IsNaN(fit) || fit <= 0)
            fit = 1;
        int viewW = Math.Max(1, (int)Math.Round(_image.Width * fit));
        int viewH = Math.Max(1, (int)Math.Round(_image.Height * fit));
        double dipW = viewW / scale;
        double dipH = viewH / scale;
        _picture.Width = dipW;
        _picture.Height = dipH;
        _guides.Width = dipW;
        _guides.Height = dipH;
        _marks.Width = dipW;
        _marks.Height = dipH;

        int minBarW = Math.Min(maxW, (int)Math.Ceiling(560 * scale));
        int winW = Math.Max(viewW, minBarW);
        int x = monitor.X + Math.Max(0, (monitor.Width - winW) / 2);
        int y = monitor.Y + Math.Max(0, (monitor.Height - viewH - bar) / 2);
        WindowChrome.Place(this, x, y, winW, viewH + bar, scale, topmost: true, activate: true, inTaskbar: true);
        BuildMarks();
    }

    private void BuildMarks()
    {
        if (double.IsNaN(_marks.Width) || double.IsNaN(_marks.Height) || _marks.Width < 1 || _marks.Height < 1 || _image.Width < 1)
            return;
        if (_boxes.Count == _page.Words.Count && Math.Abs(_builtWidth - _marks.Width) < 0.5 && Math.Abs(_builtHeight - _marks.Height) < 0.5)
            return;
        _builtWidth = _marks.Width;
        _builtHeight = _marks.Height;
        _marks.Children.Clear();
        _boxes.Clear();
        for (int i = 0; i < _page.Words.Count; i++)
        {
            var word = _page.Words[i];
            var placed = OcrMap.ToView(new OcrBox(word.X, word.Y, word.Width, word.Height), _image.Width, _image.Height, _marks.Width, _marks.Height, 0, 0);
            var box = new Border
            {
                Width = Math.Max(2, placed.Width),
                Height = Math.Max(2, placed.Height),
                Background = Idle,
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(box, placed.X);
            Canvas.SetTop(box, placed.Y);
            _marks.Children.Add(box);
            _boxes.Add(box);
        }
        BuildGuides();
        Paint();
    }

    private void BuildGuides()
    {
        _guides.Children.Clear();
        if (!_tableMode || double.IsNaN(_guides.Width) || _guides.Width < 1 || _guides.Height < 1 || _image.Width < 1)
            return;

        var table = _selected.Count >= 2 ? _page.TableFor(_selected) : _fullTable;
        if (table.ColumnSplits.Count == 0)
            return;

        double y0 = 0;
        double y1 = _guides.Height;
        if (_selected.Count >= 2)
        {
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            foreach (int i in _selected)
            {
                if (i < 0 || i >= _page.Words.Count) continue;
                var w = _page.Words[i];
                if (w.Y < minY) minY = w.Y;
                if (w.Bottom > maxY) maxY = w.Bottom;
            }
            if (minY < maxY)
            {
                var span = OcrMap.ToView(new OcrBox(0, minY, 1, maxY - minY), _image.Width, _image.Height, _guides.Width, _guides.Height, 0, 0);
                y0 = Math.Max(0, span.Y - 4);
                y1 = Math.Min(_guides.Height, span.Y + span.Height + 4);
            }
        }

        foreach (double splitX in table.ColumnSplits)
        {
            var mapped = OcrMap.ToView(new OcrBox(splitX, 0, 1, 1), _image.Width, _image.Height, _guides.Width, _guides.Height, 0, 0);
            double x = Math.Round(mapped.X);
            // High-contrast paired hairline (dark shadow + dashed light) stays legible on both light & dark tables.
            _guides.Children.Add(new Line
            {
                X1 = x + 1,
                Y1 = y0,
                X2 = x + 1,
                Y2 = y1,
                Stroke = GuideDark,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            });
            _guides.Children.Add(new Line
            {
                X1 = x,
                Y1 = y0,
                X2 = x,
                Y2 = y1,
                Stroke = GuideLight,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            });
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetTableMode(!_tableMode, recopyIfSelected: true);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            _selected.Clear();
            for (int i = 0; i < _page.Words.Count; i++)
                _selected.Add(i);
            BuildGuides();
            Paint();
            CopyAll();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C)
        {
            if (!_tableMode)
                SetTableMode(true, recopyIfSelected: false);
            if (_selected.Count == 0)
                CopyAll();
            else
                CopyCurrentSelection();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            if (_selected.Count == 0)
                CopyAll();
            else
                CopyCurrentSelection();
            e.Handled = true;
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _start = e.GetPosition(_marks);
        _marks.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_marks);
        if (_dragging)
        {
            if (Math.Abs(point.X - _start.X) > 3 || Math.Abs(point.Y - _start.Y) > 3)
                _moved = true;
            if (_moved)
                SelectRect(_start, point);
            return;
        }
        int hit = WordAt(point);
        if (hit == _hover)
            return;
        _hover = hit;
        Paint();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_marks);
        if (!_dragging)
            return;
        _dragging = false;
        if (_marks.IsMouseCaptured)
            _marks.ReleaseMouseCapture();
        HideBand();
        if (!_moved)
        {
            _selected.Clear();
            int hit = WordAt(point);
            if (hit >= 0)
                _selected.Add(hit);
        }
        BuildGuides();
        Paint();
        if (_selected.Count > 0)
            CopyCurrentSelection();
        else
        {
            _status.Foreground = Palette.Muted;
            _status.Text = "Nothing under that drag. Try the highlighted words.";
        }
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;
        _dragging = false;
        HideBand();
        BuildGuides();
        Paint();
        if (_selected.Count > 0)
            CopyCurrentSelection();
    }

    private void SelectRect(Point a, Point b)
    {
        double x1 = Math.Min(a.X, b.X);
        double y1 = Math.Min(a.Y, b.Y);
        double x2 = Math.Max(a.X, b.X);
        double y2 = Math.Max(a.Y, b.Y);
        ShowBand(x1, y1, x2 - x1, y2 - y1);
        var hit = OcrMap.FromView(new OcrBox(x1, y1, x2 - x1, y2 - y1), _image.Width, _image.Height, Math.Max(1, _marks.Width), Math.Max(1, _marks.Height), 0, 0);
        double ix = hit.X, iy = hit.Y, iw = hit.Width, ih = hit.Height;
        _selected.Clear();
        for (int i = 0; i < _page.Words.Count; i++)
        {
            if (_page.Words[i].Intersects(ix, iy, iw, ih))
                _selected.Add(i);
        }
        Paint();
    }

    private int WordAt(Point point)
    {
        var mapped = OcrMap.FromView(new OcrBox(point.X, point.Y, 0, 0), _image.Width, _image.Height, Math.Max(1, _marks.Width), Math.Max(1, _marks.Height), 0, 0);
        double x = mapped.X, y = mapped.Y;
        int best = -1;
        double bestArea = double.MaxValue;
        for (int i = 0; i < _page.Words.Count; i++)
        {
            var word = _page.Words[i];
            if (!word.Hits(x, y, 3))
                continue;
            double area = Math.Max(1, word.Width * word.Height);
            if (area < bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        return best;
    }

    private void Paint()
    {
        for (int i = 0; i < _boxes.Count; i++)
        {
            bool selected = _selected.Contains(i);
            _boxes[i].Background = selected
                ? On
                : i == _hover ? Hot : Idle;
            _boxes[i].BorderBrush = selected ? BoxEdgeOn : BoxEdge;
            _boxes[i].BorderThickness = new Thickness(1);
        }
    }

    private void ShowBand(double x, double y, double w, double h)
    {
        _band ??= new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = Palette.Freeze(255, 255, 255, 0x18),
            IsHitTestVisible = false
        };
        if (!_marks.Children.Contains(_band))
            _marks.Children.Add(_band);
        Canvas.SetLeft(_band, x);
        Canvas.SetTop(_band, y);
        _band.Width = Math.Max(1, w);
        _band.Height = Math.Max(1, h);
    }

    private void HideBand()
    {
        if (_band != null)
            _marks.Children.Remove(_band);
    }

    private void CopyCurrentSelection()
    {
        if (_tableMode)
        {
            var table = _page.TableFor(_selected);
            CopySelection(
                table.ToTsv(),
                table.ColumnCount > 1 || table.RowCount > 1
                    ? $"Copied table ({table.ColumnCount} cols × {table.RowCount} rows) — paste into Excel!"
                    : "Copied! Paste it anywhere.");
        }
        else
        {
            CopySelection(_page.TextFor(_selected), "Copied! Paste it anywhere.");
        }
    }

    private void CopySelection(string text, string successMessage = "Copied! Paste it anywhere.")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _status.Foreground = Palette.Muted;
            _status.Text = "No text in that selection.";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _status.Foreground = Palette.Copied;
            _status.Text = successMessage;
        }
        catch
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "The clipboard is busy. Try Copy again.";
        }
    }

    private void CopyAll()
    {
        if (_tableMode)
        {
            CopySelection(
                _fullTable.ToTsv(),
                _fullTable.ColumnCount > 1 || _fullTable.RowCount > 1
                    ? $"Copied table ({_fullTable.ColumnCount} cols × {_fullTable.RowCount} rows) — paste into Excel!"
                    : "Copied! Paste it anywhere.");
            return;
        }

        string text = string.IsNullOrWhiteSpace(_page.Text) ? _page.TextFor(Enumerable.Range(0, _page.Words.Count)) : _page.Text;
        CopySelection(text, "Copied! Paste it anywhere.");
    }

    private static UIElement At(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }
}
