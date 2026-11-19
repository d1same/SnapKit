using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal enum OverlayMode
{
    Area,
    Window,
    FullScreen,
    ScrollPick
}

internal enum OverlayPurpose
{
    Edit,
    Ocr,
    Scroll
}

internal sealed class CapturePick
{
    public PixelBuffer? Image { get; init; }
    public int ScreenX { get; init; }
    public int ScreenY { get; init; }
    public MonitorInfo? Monitor { get; init; }
    public IntPtr WindowHandle { get; init; }
    public string WindowTitle { get; init; } = "";
}

internal sealed class OverlaySession
{
    private readonly List<OverlayWindow> _windows = new();
    private bool _done;

    public OverlaySession(IReadOnlyList<MonitorShot> shots, OverlayMode mode, OverlayPurpose purpose)
    {
        Shots = shots;
        Mode = mode;
        Purpose = purpose;
    }

    public IReadOnlyList<MonitorShot> Shots { get; }
    public OverlayMode Mode { get; private set; }
    public OverlayPurpose Purpose { get; }
    public bool Frozen { get; private set; }
    public event Action<CapturePick?>? Finished;

    public void Add(OverlayWindow window) => _windows.Add(window);

    public void SetMode(OverlayMode mode)
    {
        if (Frozen || Purpose != OverlayPurpose.Edit)
            return;
        Mode = mode;
        foreach (var window in _windows)
            window.RefreshChrome();
    }

    public void Complete(CapturePick? pick)
    {
        if (_done)
            return;
        _done = true;
        foreach (var window in _windows.ToList())
        {
            window.Topmost = false;
            window.ForceClose();
        }
        Finished?.Invoke(pick);
    }

    public void CloseAll()
    {
        _done = true;
        foreach (var window in _windows.ToList())
            window.ForceClose();
    }

    public void FocusUnderCursor()
    {
        NativeMethods.GetCursorPos(out var point);
        var window = _windows.FirstOrDefault(item =>
            point.X >= item.Shot.Monitor.X && point.X < item.Shot.Monitor.Right &&
            point.Y >= item.Shot.Monitor.Y && point.Y < item.Shot.Monitor.Bottom);
        window?.Activate();
    }
}

internal sealed class OverlayWindow : Window
{
    private readonly OverlaySession _session;
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Path _dim = new() { Fill = Palette.Freeze(0, 0, 0, 0x88), IsHitTestVisible = false };
    private readonly Rectangle _selection = new()
    {
        Stroke = Brushes.White,
        StrokeThickness = 1.5,
        Fill = Palette.Freeze(255, 255, 255, 0x18),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly Border _sizePill;
    private readonly TextBlock _sizeText;
    private readonly Grid _loupe;
    private readonly ImageBrush _loupeBrush = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _loupeHex;
    private readonly TextBlock _hintText;
    private readonly Border _hint;
    private bool _dragging;
    private int _startX;
    private int _startY;
    private int _selX;
    private int _selY;
    private int _selW;
    private int _selH;
    private bool _frozen;
    private bool _closing;

    public OverlayWindow(OverlaySession session, MonitorShot shot)
    {
        _session = session;
        Shot = shot;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Opacity = 0;
        ShowActivated = false;

        _sizeText = new TextBlock { Foreground = Brushes.White, FontFamily = Palette.Font, FontSize = 12 };
        _sizePill = new Border
        {
            Background = Palette.Freeze(16, 16, 16, 0xE6),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _sizeText
        };
        _loupeHex = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = Palette.Mono,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var loupeFace = new Rectangle { Width = 140, Height = 140 };
        loupeFace.Fill = _loupeBrush;
        var cross = new Canvas { Width = 140, Height = 140, IsHitTestVisible = false };
        cross.Children.Add(loupeFace);
        cross.Children.Add(new Line { X1 = 70, X2 = 70, Y1 = 0, Y2 = 140, Stroke = Palette.Freeze(255, 59, 48, 0xCC), StrokeThickness = 1 });
        cross.Children.Add(new Line { X1 = 0, X2 = 140, Y1 = 70, Y2 = 70, Stroke = Palette.Freeze(255, 59, 48, 0xCC), StrokeThickness = 1 });
        _loupe = new Grid
        {
            Width = 148,
            Height = 172,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        var frame = new Border
        {
            Width = 148,
            Height = 148,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Black,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = cross
        };
        _loupe.Children.Add(frame);
        _loupe.Children.Add(new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Palette.Freeze(16, 16, 16, 0xE6),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = _loupeHex
        });

        _hintText = new TextBlock { Foreground = Palette.Text, FontFamily = Palette.Font, FontSize = 13 };
        _hint = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
            Background = Palette.Freeze(16, 16, 16, 0xCC),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            IsHitTestVisible = false,
            Child = _hintText
        };

        Content = new Grid
        {
            Children = { _image, _dim, _selection, _sizePill, _loupe, _hint }
        };

        var bitmap = BitmapConvert.ToBitmapSource(shot.Pixels);
        _image.Source = bitmap;
        _loupeBrush.ImageSource = bitmap;
        _loupeBrush.ViewboxUnits = BrushMappingMode.Absolute;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => _session.Complete(null);
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => Redraw();
        SourceInitialized += (_, _) =>
        {
            WindowChrome.Place(this, shot.Monitor.X, shot.Monitor.Y, shot.Monitor.Width, shot.Monitor.Height, shot.Monitor.Scale, topmost: true, activate: false);
            Opacity = 1;
            RefreshChrome();
        };
        Closing += (_, _) =>
        {
            if (_closing)
                return;
            _closing = true;
            _session.Complete(null);
        };
    }

    public MonitorShot Shot { get; }

    public void Freeze()
    {
        _frozen = true;
        _dragging = false;
        Cursor = Cursors.Arrow;
        _loupe.Visibility = Visibility.Collapsed;
        _hint.Visibility = Visibility.Collapsed;
    }

    public void ForceClose()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    public void RefreshChrome()
    {
        _hintText.Text = _session.Mode switch
        {
            OverlayMode.Window => "Window    ·    Click the highlighted window    ·    Space changes mode    ·    Esc cancels    ·    Ctrl+Alt+Shift+Esc quits",
            OverlayMode.FullScreen => "Full screen    ·    Click to capture this monitor    ·    Space changes mode    ·    Esc cancels    ·    Ctrl+Alt+Shift+Esc quits",
            OverlayMode.ScrollPick => "Click the window to scroll-capture    ·    Esc cancels    ·    Ctrl+Alt+Shift+Esc quits",
            _ => "Area    ·    Drag to select    ·    Space changes mode    ·    Esc cancels    ·    Ctrl+Alt+Shift+Esc quits"
        };
        Cursor = _session.Mode == OverlayMode.Area ? Cursors.Cross : Cursors.Hand;
        if (_session.Mode == OverlayMode.FullScreen)
            ShowRect(0, 0, Shot.Pixels.Width, Shot.Pixels.Height, "This monitor");
        else if (!_dragging)
        {
            _selection.Visibility = Visibility.Collapsed;
            _sizePill.Visibility = Visibility.Collapsed;
            _loupe.Visibility = Visibility.Collapsed;
            RedrawHole(0, 0, 0, 0);
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_frozen)
            return;
        if (e.Key == Key.Escape)
        {
            _session.Complete(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && _session.Purpose == OverlayPurpose.Edit)
        {
            var next = _session.Mode switch
            {
                OverlayMode.Area => OverlayMode.Window,
                OverlayMode.Window => OverlayMode.FullScreen,
                _ => OverlayMode.Area
            };
            _session.SetMode(next);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _session.Mode == OverlayMode.FullScreen)
        {
            FinishFull();
            e.Handled = true;
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_frozen)
            return;
        var (x, y) = ToPixel(e.GetPosition(this));
        if (_session.Mode == OverlayMode.Area)
        {
            _dragging = true;
            _startX = x;
            _startY = y;
            CaptureMouse();
            _hint.Visibility = Visibility.Collapsed;
            return;
        }
        if (_session.Mode is OverlayMode.Window or OverlayMode.ScrollPick)
        {
            NativeMethods.GetCursorPos(out var cursor);
            var window = WindowFinder.At(cursor.X, cursor.Y);
            if (window == null)
                return;
            if (_session.Mode == OverlayMode.ScrollPick)
            {
                _session.Complete(new CapturePick { WindowHandle = window.Handle, WindowTitle = window.Title });
                return;
            }
            FinishWindow(window);
            return;
        }
        FinishFull();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_frozen)
            return;
        var (x, y) = ToPixel(e.GetPosition(this));
        if (_session.Mode == OverlayMode.Area && _dragging)
        {
            ShowRect(Math.Min(_startX, x), Math.Min(_startY, y), Math.Abs(x - _startX), Math.Abs(y - _startY), null);
            UpdateLoupe(x, y);
            return;
        }
        if (_session.Mode is OverlayMode.Window or OverlayMode.ScrollPick)
        {
            NativeMethods.GetCursorPos(out var cursor);
            var window = WindowFinder.At(cursor.X, cursor.Y);
            if (window == null)
            {
                _selection.Visibility = Visibility.Collapsed;
                _sizePill.Visibility = Visibility.Collapsed;
                RedrawHole(0, 0, 0, 0);
                return;
            }
            var local = Intersect(window.Bounds);
            string label = string.IsNullOrWhiteSpace(window.Title) ? "Window" : window.Title;
            ShowRect(local.X, local.Y, local.Width, local.Height, label);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _loupe.Visibility = Visibility.Collapsed;
        if (_selW < 4 || _selH < 4)
        {
            RefreshChrome();
            return;
        }
        var crop = Shot.Pixels.Crop(_selX, _selY, _selW, _selH);
        _session.Complete(new CapturePick
        {
            Image = crop,
            ScreenX = Shot.Monitor.X + _selX,
            ScreenY = Shot.Monitor.Y + _selY,
            Monitor = Shot.Monitor
        });
    }

    private void FinishWindow(TopWindow window)
    {
        var local = Intersect(window.Bounds);
        if (local.Width < 4 || local.Height < 4)
            return;
        _session.Complete(new CapturePick
        {
            Image = Shot.Pixels.Crop(local.X, local.Y, local.Width, local.Height),
            ScreenX = Shot.Monitor.X + local.X,
            ScreenY = Shot.Monitor.Y + local.Y,
            Monitor = Shot.Monitor,
            WindowHandle = window.Handle,
            WindowTitle = window.Title
        });
    }

    private void FinishFull()
    {
        _session.Complete(new CapturePick
        {
            Image = Shot.Pixels.Clone(),
            ScreenX = Shot.Monitor.X,
            ScreenY = Shot.Monitor.Y,
            Monitor = Shot.Monitor
        });
    }

    private (int X, int Y, int Width, int Height) Intersect(NativeMethods.Rect bounds)
    {
        int x = Math.Max(0, bounds.Left - Shot.Monitor.X);
        int y = Math.Max(0, bounds.Top - Shot.Monitor.Y);
        int right = Math.Min(Shot.Monitor.Width, bounds.Right - Shot.Monitor.X);
        int bottom = Math.Min(Shot.Monitor.Height, bounds.Bottom - Shot.Monitor.Y);
        return (x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private void ShowRect(int x, int y, int w, int h, string? label)
    {
        _selX = x;
        _selY = y;
        _selW = w;
        _selH = h;
        if (ActualWidth < 1 || Shot.Pixels.Width < 1)
            return;
        double sx = ActualWidth / Shot.Pixels.Width;
        double sy = ActualHeight / Shot.Pixels.Height;
        _selection.Visibility = w > 1 && h > 1 ? Visibility.Visible : Visibility.Collapsed;
        _selection.Margin = new Thickness(x * sx, y * sy, 0, 0);
        _selection.Width = Math.Max(0, w * sx);
        _selection.Height = Math.Max(0, h * sy);
        _sizeText.Text = label == null ? $"{w} × {h}" : $"{Trim(label)}    ·    {w} × {h}";
        _sizePill.Visibility = Visibility.Visible;
        double pillX = x * sx + w * sx + 8;
        double pillY = y * sy + h * sy + 8;
        if (pillX > ActualWidth - 140)
            pillX = Math.Max(8, x * sx);
        if (pillY > ActualHeight - 36)
            pillY = Math.Max(8, y * sy - 32);
        _sizePill.Margin = new Thickness(pillX, pillY, 0, 0);
        RedrawHole(x * sx, y * sy, w * sx, h * sy);
    }

    private void UpdateLoupe(int x, int y)
    {
        const double zoom = 4;
        const double size = 140;
        double view = size / zoom;
        _loupeBrush.Viewbox = new Rect(x - view / 2, y - view / 2, view, view);
        _loupeHex.Text = Shot.Pixels.HexAt(x, y);
        _loupe.Visibility = Visibility.Visible;
        double sx = ActualWidth / Shot.Pixels.Width;
        double sy = ActualHeight / Shot.Pixels.Height;
        double left = x * sx + 24;
        double top = y * sy + 24;
        if (left + _loupe.Width > ActualWidth)
            left = x * sx - _loupe.Width - 16;
        if (top + _loupe.Height > ActualHeight)
            top = y * sy - _loupe.Height - 16;
        _loupe.Margin = new Thickness(Math.Max(4, left), Math.Max(4, top), 0, 0);
    }

    private void Redraw()
    {
        if (_selW > 1)
            ShowRect(_selX, _selY, _selW, _selH, null);
        else
            RedrawHole(0, 0, 0, 0);
    }

    private void RedrawHole(double x, double y, double w, double h)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
        if (w < 2 || h < 2)
        {
            _dim.Data = full;
            return;
        }
        var hole = new RectangleGeometry(new Rect(x, y, w, h));
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
    }

    private (int X, int Y) ToPixel(Point dip)
    {
        if (ActualWidth < 1 || ActualHeight < 1)
            return (0, 0);
        int x = (int)Math.Round(dip.X / ActualWidth * Shot.Pixels.Width);
        int y = (int)Math.Round(dip.Y / ActualHeight * Shot.Pixels.Height);
        // Width/Height (not Width-1) so a drag to the corner includes the last pixel.
        return (Math.Clamp(x, 0, Shot.Pixels.Width), Math.Clamp(y, 0, Shot.Pixels.Height));
    }

    private static string Trim(string value) => value.Length <= 42 ? value : value[..39] + "…";
}
