using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    public event Action? Dismissed;
    public event Action? FocusEditor;

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
        if (pick?.Image == null || Purpose != OverlayPurpose.Edit)
        {
            foreach (var window in _windows.ToList())
            {
                window.Topmost = false;
                window.ForceClose();
            }
        }
        else
        {
            Frozen = true;
            foreach (var window in _windows)
                window.Freeze(pick);
        }
        Finished?.Invoke(pick);
    }

    public void Dismiss()
    {
        if (!_done)
        {
            Complete(null);
            return;
        }
        Dismissed?.Invoke();
    }

    public void RaiseFocusEditor() => FocusEditor?.Invoke();

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
    private readonly Path _dim = new() { Fill = Palette.Freeze(0, 0, 0, 0x40), IsHitTestVisible = false };
    private readonly Rectangle _selectionInk = new()
    {
        Stroke = Brushes.Black,
        StrokeThickness = 1.25,
        StrokeDashArray = new DoubleCollection { 5, 4 },
        StrokeDashOffset = 5,
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        SnapsToDevicePixels = true
    };
    private readonly Rectangle _selection = new()
    {
        Stroke = Brushes.White,
        StrokeThickness = 1.25,
        StrokeDashArray = new DoubleCollection { 5, 4 },
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        SnapsToDevicePixels = true
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
    private bool _dragMoved;
    private int _dragX;
    private int _dragY;
    private int _dragW;
    private int _dragH;
    private readonly Border _choiceHost;
    private readonly StackPanel _choiceRows = new();
    private List<SnapTarget> _snaps = new();
    private int _snapIndex;
    private int _preferFromEnd = int.MaxValue;
    private readonly List<Border> _choiceChips = new();

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

        _choiceRows.Margin = new Thickness(8, 6, 8, 8);
        _choiceHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Click a target    ·    scroll changes it    ·    drag draws",
                        Foreground = Palette.Muted,
                        FontFamily = Palette.Font,
                        FontSize = 11,
                        Margin = new Thickness(12, 8, 12, 2)
                    },
                    _choiceRows
                }
            }
        };

        Content = new Grid
        {
            Children = { _image, _dim, _selectionInk, _selection, _sizePill, _loupe, _hint, _choiceHost }
        };

        var bitmap = BitmapConvert.ToBitmapSource(shot.Pixels);
        _image.Source = bitmap;
        _loupeBrush.ImageSource = bitmap;
        _loupeBrush.ViewboxUnits = BrushMappingMode.Absolute;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        PreviewMouseWheel += OnWheel;
        MouseRightButtonDown += (_, _) => _session.Dismiss();
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => Redraw();
        SourceInitialized += (_, _) =>
        {
            WindowChrome.Place(this, shot.Monitor.X, shot.Monitor.Y, shot.Monitor.Width, shot.Monitor.Height, shot.Monitor.Scale, topmost: true, activate: false);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(HitTestHook);
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

    public void Freeze(CapturePick? pick)
    {
        _frozen = true;
        _dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        _loupe.Visibility = Visibility.Collapsed;
        _hint.Visibility = Visibility.Collapsed;
        _choiceHost.Visibility = Visibility.Collapsed;
        long ex = NativeMethods.GetWindowLongPtr(new WindowInteropHelper(this).Handle, NativeMethods.GwlExStyle).ToInt64();
        ex |= NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(new WindowInteropHelper(this).Handle, NativeMethods.GwlExStyle, new IntPtr(ex));
        if (pick?.Image != null)
        {
            int x = pick.ScreenX - Shot.Monitor.X;
            int y = pick.ScreenY - Shot.Monitor.Y;
            if (x < Shot.Pixels.Width && y < Shot.Pixels.Height && x + pick.Image.Width > 0 && y + pick.Image.Height > 0)
            {
                ShowRect(Math.Max(0, x), Math.Max(0, y), pick.Image.Width, pick.Image.Height, null);
                HideSelection();
                _sizePill.Visibility = Visibility.Collapsed;
                return;
            }
        }
        RedrawHole(0, 0, 0, 0);
    }

    private IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_frozen && msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            _session.RaiseFocusEditor();
            return new IntPtr(NativeMethods.MaNoActivate);
        }
        return IntPtr.Zero;
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
            _ => "Drag a box to capture just that    ·    click the dashed target    ·    scroll changes it    ·    Esc cancels"
        };
        Cursor = _session.Mode == OverlayMode.Area ? Cursors.Cross : Cursors.Hand;
        if (_session.Mode != OverlayMode.Area)
            _choiceHost.Visibility = Visibility.Collapsed;
        if (_session.Mode == OverlayMode.FullScreen)
            ShowRect(0, 0, Shot.Pixels.Width, Shot.Pixels.Height, "This monitor");
        else if (!_dragging)
        {
            HideSelection();
            _sizePill.Visibility = Visibility.Collapsed;
            _loupe.Visibility = Visibility.Collapsed;
            RedrawHole(0, 0, 0, 0);
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _session.Dismiss();
            e.Handled = true;
            return;
        }
        if (_frozen)
            return;
        if (e.Key == Key.Space && _session.Purpose == OverlayPurpose.Edit)
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
        else if (e.Key == Key.Enter && _session.Mode == OverlayMode.Area && _snaps.Count > 0)
        {
            CaptureSnap(_snaps[_snapIndex]);
            e.Handled = true;
        }
        else if (_session.Mode == OverlayMode.Area && e.Key is Key.Up or Key.Down)
        {
            CycleSnap(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_frozen || _dragging || _session.Mode != OverlayMode.Area)
            return;
        e.Handled = true;
        if (_snaps.Count == 0)
        {
            var (x, y) = CursorLocal();
            OfferSnaps(x, y);
        }
        CycleSnap(e.Delta > 0 ? 1 : -1);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_frozen)
            return;
        if (_session.Mode == OverlayMode.Area)
        {
            var (x, y) = CursorLocal();
            _dragging = true;
            _dragMoved = false;
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
        if (_session.Mode == OverlayMode.Area && _dragging)
        {
            var (x, y) = CursorLocal();
            int w = Math.Abs(x - _startX);
            int h = Math.Abs(y - _startY);
            if (w > 10 || h > 10)
                _dragMoved = true;
            if (_dragMoved)
            {
                _choiceHost.Visibility = Visibility.Collapsed;
                _dragX = Math.Min(_startX, x);
                _dragY = Math.Min(_startY, y);
                _dragW = Math.Max(1, w);
                _dragH = Math.Max(1, h);
                ShowRect(_dragX, _dragY, _dragW, _dragH, null);
                UpdateLoupe(x, y);
            }
            return;
        }
        if (_session.Mode == OverlayMode.Area)
        {
            var (x, y) = CursorLocal();
            OfferSnaps(x, y);
            return;
        }
        if (_session.Mode is OverlayMode.Window or OverlayMode.ScrollPick)
        {
            NativeMethods.GetCursorPos(out var cursor);
            var window = WindowFinder.At(cursor.X, cursor.Y);
            if (window == null)
            {
                HideSelection();
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
        if (_dragMoved && _dragW >= 4 && _dragH >= 4)
        {
            var crop = Shot.Pixels.Crop(_dragX, _dragY, _dragW, _dragH);
            _session.Complete(new CapturePick
            {
                Image = crop,
                ScreenX = Shot.Monitor.X + _dragX,
                ScreenY = Shot.Monitor.Y + _dragY,
                Monitor = Shot.Monitor
            });
            return;
        }
        if (_snaps.Count == 0)
        {
            var (x, y) = CursorLocal();
            OfferSnaps(x, y);
        }
        if (_snapIndex >= 0 && _snapIndex < _snaps.Count)
        {
            CaptureSnap(_snaps[_snapIndex]);
            return;
        }
        RefreshChrome();
    }

    private (int X, int Y) CursorLocal()
    {
        NativeMethods.GetCursorPos(out var point);
        int maxX = Math.Max(0, Shot.Pixels.Width - 1);
        int maxY = Math.Max(0, Shot.Pixels.Height - 1);
        return (
            Math.Clamp(point.X - Shot.Monitor.X, 0, maxX),
            Math.Clamp(point.Y - Shot.Monitor.Y, 0, maxY));
    }

    private bool HeldByCurrentTarget(int localX, int localY)
    {
        if (_snapIndex < 0 || _snapIndex >= _snaps.Count)
            return false;
        var current = _snaps[_snapIndex];
        if (current.Kind == "Entire screen")
            return false;
        int x = Shot.Monitor.X + localX;
        int y = Shot.Monitor.Y + localY;
        return x >= current.X && x < current.Right && y >= current.Y && y < current.Bottom;
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

    private void OfferSnaps(int pixelX, int pixelY)
    {
        int sx = Shot.Monitor.X + Math.Clamp(pixelX, 0, Math.Max(0, Shot.Pixels.Width - 1));
        int sy = Shot.Monitor.Y + Math.Clamp(pixelY, 0, Math.Max(0, Shot.Pixels.Height - 1));
        var found = SnapFinder.At(sx, sy, Shot.Monitor).ToList();
        if (found.Count == 0)
            return;
        if (HeldByCurrentTarget(sx, sy))
            return;
        bool same = found.Count == _snaps.Count;
        if (same)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].X != _snaps[i].X || found[i].Y != _snaps[i].Y || found[i].Width != _snaps[i].Width || found[i].Height != _snaps[i].Height || found[i].Kind != _snaps[i].Kind)
                {
                    same = false;
                    break;
                }
            }
        }
        _snaps = found;
        _snapIndex = _preferFromEnd == int.MaxValue
            ? 0
            : Math.Clamp(found.Count - 1 - _preferFromEnd, 0, found.Count - 1);
        if (!same)
            RebuildChoices();
        else
            MarkChips();
        HighlightSnap();
    }

    private void CycleSnap(int delta)
    {
        if (_snaps.Count == 0)
            return;
        _snapIndex = Math.Clamp(_snapIndex + delta, 0, _snaps.Count - 1);
        _preferFromEnd = _snaps.Count - 1 - _snapIndex;
        MarkChips();
        HighlightSnap();
    }

    private void RebuildChoices()
    {
        _choiceRows.Children.Clear();
        _choiceChips.Clear();
        for (int i = 0; i < _snaps.Count; i++)
        {
            int index = i;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(6, 2, 6, 2),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = _snaps[i].Line,
                    Foreground = Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 13
                }
            };
            chip.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _dragging = false;
                if (IsMouseCaptured)
                    ReleaseMouseCapture();
                if (index < _snaps.Count)
                    CaptureSnap(_snaps[index]);
            };
            _choiceChips.Add(chip);
            _choiceRows.Children.Add(chip);
        }
        MarkChips();
        _choiceHost.Visibility = Visibility.Visible;
    }

    private void MarkChips()
    {
        for (int i = 0; i < _choiceChips.Count; i++)
        {
            bool on = i == _snapIndex;
            _choiceChips[i].Background = on ? Palette.Elevated : Brushes.Transparent;
            _choiceChips[i].BorderBrush = on ? Brushes.White : Brushes.Transparent;
            _choiceChips[i].BorderThickness = new Thickness(1);
        }
    }

    private void HighlightSnap()
    {
        if (_snapIndex < 0 || _snapIndex >= _snaps.Count || ActualWidth < 1)
            return;
        var target = _snaps[_snapIndex];
        var local = Intersect(new NativeMethods.Rect
        {
            Left = target.X,
            Top = target.Y,
            Right = target.Right,
            Bottom = target.Bottom
        });
        ShowRect(local.X, local.Y, local.Width, local.Height, target.Kind);
        _sizePill.Visibility = Visibility.Collapsed;
        _loupe.Visibility = Visibility.Collapsed;
        PlaceChoices();
    }

    private void PlaceChoices()
    {
        _choiceHost.Measure(new Size(320, 640));
        double hostW = Math.Max(160, _choiceHost.DesiredSize.Width);
        double hostH = Math.Max(36, _choiceHost.DesiredSize.Height);
        double sx = ActualWidth / Math.Max(1, Shot.Pixels.Width);
        double sy = ActualHeight / Math.Max(1, Shot.Pixels.Height);
        double left = (_selX + _selW) * sx + 12;
        double top = _selY * sy;
        if (left + hostW > ActualWidth - 8)
            left = _selX * sx - hostW - 12;
        if (left < 8)
            left = 8;
        if (top + hostH > ActualHeight - 8)
            top = Math.Max(8, ActualHeight - hostH - 8);
        _choiceHost.Margin = new Thickness(left, Math.Max(8, top), 0, 0);
    }

    private void CaptureSnap(SnapTarget target)
    {
        var local = Intersect(new NativeMethods.Rect
        {
            Left = target.X,
            Top = target.Y,
            Right = target.Right,
            Bottom = target.Bottom
        });
        if (local.Width < 4 || local.Height < 4)
            return;
        _session.Complete(new CapturePick
        {
            Image = Shot.Pixels.Crop(local.X, local.Y, local.Width, local.Height),
            ScreenX = Shot.Monitor.X + local.X,
            ScreenY = Shot.Monitor.Y + local.Y,
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

    private void HideSelection()
    {
        _selection.Visibility = Visibility.Collapsed;
        _selectionInk.Visibility = Visibility.Collapsed;
    }

    private void PlaceSelection(double x, double y, double w, double h, bool visible)
    {
        if (!visible)
        {
            HideSelection();
            return;
        }
        var margin = new Thickness(x, y, 0, 0);
        double width = Math.Max(0, w);
        double height = Math.Max(0, h);
        _selectionInk.Visibility = Visibility.Visible;
        _selectionInk.Margin = margin;
        _selectionInk.Width = width;
        _selectionInk.Height = height;
        _selection.Visibility = Visibility.Visible;
        _selection.Margin = margin;
        _selection.Width = width;
        _selection.Height = height;
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
        PlaceSelection(x * sx, y * sy, w * sx, h * sy, w > 1 && h > 1);
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
