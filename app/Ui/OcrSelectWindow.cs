using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

/// <summary>
/// Snipping Tool style: recognized words are highlighted on the capture.
/// Drag across the ones you want and that text is copied.
/// </summary>
internal sealed class OcrSelectWindow : Window
{
    private static readonly SolidColorBrush Idle = Palette.Freeze(10, 132, 255, 0x38);
    private static readonly SolidColorBrush Hot = Palette.Freeze(10, 132, 255, 0x66);
    private static readonly SolidColorBrush On = Palette.Freeze(10, 132, 255, 0xB0);

    private readonly PixelBuffer _image;
    private readonly OcrPage _page;
    private readonly Image _picture;
    private readonly Canvas _marks;
    private readonly TextBlock _status;
    private readonly List<Border> _boxes = new();
    private readonly HashSet<int> _selected = new();
    private Rectangle? _band;
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
        _marks = new Canvas
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true
        };
        var stage = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _picture, _marks }
        };
        stage.MouseLeftButtonDown += OnDown;
        stage.MouseMove += OnMove;
        stage.MouseLeftButtonUp += OnUp;
        _marks.SizeChanged += (_, _) => BuildMarks();
        _marks.LostMouseCapture += (_, _) => EndDrag();

        _status = new TextBlock
        {
            Text = page.Words.Count == 0
                ? "No text found in that area."
                : "Drag across the highlighted words. Esc closes.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        var copyAll = Button("Copy all", CopyAll);
        var close = Button("Close", Close);
        close.Background = Palette.Elevated;
        close.Foreground = Palette.Text;
        close.BorderBrush = Palette.Line;
        close.BorderThickness = new Thickness(1);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        if (!string.IsNullOrWhiteSpace(page.Text) || page.Words.Count > 0)
            buttons.Children.Add(copyAll);
        buttons.Children.Add(close);
        var bar = new Grid
        {
            Height = 52,
            Background = Palette.Card,
            Children = { _status, buttons }
        };

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { At(bar, Dock.Bottom), stage }
        };

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => PlaceOnMonitor();
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
        _marks.Width = dipW;
        _marks.Height = dipH;
        int x = monitor.X + Math.Max(0, (monitor.Width - viewW) / 2);
        int y = monitor.Y + Math.Max(0, (monitor.Height - viewH - bar) / 2);
        WindowChrome.Place(this, x, y, viewW, viewH + bar, scale, topmost: true, activate: true, inTaskbar: true);
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
        Paint();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            _selected.Clear();
            for (int i = 0; i < _page.Words.Count; i++)
                _selected.Add(i);
            Paint();
            CopySelection(_page.Text);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            if (_selected.Count == 0)
                CopyAll();
            else
                CopySelection(_page.TextFor(_selected));
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
        Paint();
        if (_selected.Count > 0)
            CopySelection(_page.TextFor(_selected));
        else
            _status.Text = "Nothing under that drag. Try the highlighted words.";
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;
        _dragging = false;
        HideBand();
        Paint();
        if (_selected.Count > 0)
            CopySelection(_page.TextFor(_selected));
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
            _boxes[i].Background = _selected.Contains(i)
                ? On
                : i == _hover ? Hot : Idle;
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

    private void CopySelection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _status.Text = "No text in that selection.";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _status.Text = "Copied. Paste it anywhere.";
        }
        catch
        {
            _status.Text = "The clipboard is busy. Try Copy all again.";
        }
    }

    private void CopyAll()
    {
        string text = string.IsNullOrWhiteSpace(_page.Text) ? _page.TextFor(Enumerable.Range(0, _page.Words.Count)) : _page.Text;
        CopySelection(text);
    }

    private static Button Button(string label, Action click)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Palette.Text,
            Foreground = Palette.Ink,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Font,
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static UIElement At(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }
}
