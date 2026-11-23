using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Transparent overlay aligned exactly to the screen recording region for live tutorial drawing.
/// Intentionally NOT excluded from capture so drawn arrows, boxes, pen strokes, highlighter,
/// and step badges are baked straight into the recorded MP4/GIF in real time.
/// Flips seamlessly between interactive Draw mode and WS_EX_TRANSPARENT click-through mode.
/// </summary>
internal sealed class LiveDrawOverlayWindow : Window
{
    private const double HoldSeconds = 2.4;
    private const double FadeOutSeconds = 0.65;

    private readonly MonitorInfo _monitor;
    private readonly int _screenX;
    private readonly int _screenY;
    private readonly int _width;
    private readonly int _height;
    private readonly LiveCanvasElement _canvas;
    private readonly DispatcherTimer _fadeTimer;
    private readonly List<LiveMark> _marks = new();

    private Annotation? _draft;
    private PointD _start;
    private bool _drawing;
    private bool _drawEnabled;
    private bool _closed;
    private int _nextStep = 1;

    public LiveDrawOverlayWindow(MonitorInfo monitor, int screenX, int screenY, int width, int height)
    {
        _monitor = monitor;
        _screenX = screenX;
        _screenY = screenY;
        _width = Math.Max(16, width);
        _height = Math.Max(16, height);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _canvas = new LiveCanvasElement(this);
        Content = _canvas;

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _fadeTimer.Tick += OnFadeTick;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _drawEnabled)
            {
                ExitDrawRequested?.Invoke();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z && _drawEnabled)
            {
                Undo();
                e.Handled = true;
            }
        };

        SourceInitialized += (_, _) =>
        {
            double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
            WindowChrome.Place(this, _screenX, _screenY, _width, _height, scale, topmost: true, activate: false);
            ApplyClickThrough(clickThrough: true);
        };
    }

    public AnnotationKind ActiveTool { get; set; } = AnnotationKind.Arrow;
    public string ColorHex { get; set; } = "#FF3B30";
    public double StrokeWidth { get; set; } = 4;
    public bool AutoFade { get; set; } = true;
    public bool IsDrawMode => _drawEnabled;
    public int MarkCount => _marks.Count;

    public event Action? ExitDrawRequested;
    public event Action? MarksChanged;

    public void SetDrawMode(bool enabled)
    {
        if (_closed)
            return;
        _drawEnabled = enabled;
        if (!enabled)
        {
            CancelDraft();
            IsHitTestVisible = false;
            Background = Brushes.Transparent;
            Cursor = Cursors.Arrow;
            ApplyClickThrough(clickThrough: true);
        }
        else
        {
            ApplyClickThrough(clickThrough: false);
            // 1/255 invisible alpha lets WPF hit-test clicks inside the recording region without darkening the video.
            Background = Palette.Freeze(0, 0, 0, 1);
            IsHitTestVisible = true;
            Cursor = Cursors.Cross;
        }
    }

    public void Undo()
    {
        if (_marks.Count == 0)
            return;
        _marks.RemoveAt(_marks.Count - 1);
        _canvas.InvalidateVisual();
        MarksChanged?.Invoke();
    }

    public void Clear()
    {
        CancelDraft();
        _marks.Clear();
        _nextStep = 1;
        _fadeTimer.Stop();
        _canvas.InvalidateVisual();
        MarksChanged?.Invoke();
    }

    public void SafeClose()
    {
        if (_closed)
            return;
        _closed = true;
        _fadeTimer.Stop();
        try { Close(); } catch { /* already closed */ }
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        ex |= NativeMethods.WsExToolWindow;
        if (clickThrough)
            ex |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        else
            ex &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(ex));
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!_drawEnabled)
            return;
        var pos = e.GetPosition(_canvas);
        _start = new PointD(pos.X, pos.Y);

        if (ActiveTool == AnnotationKind.Number)
        {
            var badge = new Annotation
            {
                Kind = AnnotationKind.Number,
                ColorHex = ColorHex,
                FontSize = 18,
                Number = _nextStep++,
                Points = new List<PointD> { _start }
            };
            CommitMark(badge);
            e.Handled = true;
            return;
        }

        _drawing = true;
        _draft = new Annotation
        {
            Kind = ActiveTool,
            ColorHex = ColorHex,
            Stroke = StrokeWidth,
            FillMode = ShapeFillMode.Outline,
            Points = ActiveTool is AnnotationKind.Pen or AnnotationKind.Highlighter
                ? new List<PointD> { _start }
                : new List<PointD> { _start, _start }
        };
        CaptureMouse();
        _canvas.InvalidateVisual();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _draft == null)
            return;
        var pos = e.GetPosition(_canvas);
        var pt = new PointD(pos.X, pos.Y);

        if (_draft.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
        {
            var last = _draft.Points[^1];
            double dx = pt.X - last.X;
            double dy = pt.Y - last.Y;
            if (dx * dx + dy * dy >= 4)
                _draft.Points.Add(pt);
        }
        else
        {
            _draft.Points[1] = pt;
        }

        _canvas.InvalidateVisual();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _draft == null)
            return;
        _drawing = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        var mark = _draft;
        _draft = null;

        bool valid = mark.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter
            ? mark.Points.Count >= 1
            : mark.Points.Count >= 2 && (Math.Abs(mark.Points[1].X - mark.Points[0].X) >= 3 || Math.Abs(mark.Points[1].Y - mark.Points[0].Y) >= 3);

        if (valid)
            CommitMark(mark);
        else
            _canvas.InvalidateVisual();
        e.Handled = true;
    }

    private void CommitMark(Annotation annotation)
    {
        _marks.Add(new LiveMark(annotation, AutoFade ? Environment.TickCount64 : null));
        if (AutoFade && !_fadeTimer.IsEnabled)
            _fadeTimer.Start();
        _canvas.InvalidateVisual();
        MarksChanged?.Invoke();
    }

    private void CancelDraft()
    {
        _drawing = false;
        _draft = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _canvas.InvalidateVisual();
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        bool anyFading = false;
        bool removed = false;

        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            var m = _marks[i];
            if (!m.CreatedTicks.HasValue)
                continue;
            double age = (now - m.CreatedTicks.Value) / 1000.0;
            if (age >= HoldSeconds + FadeOutSeconds)
            {
                _marks.RemoveAt(i);
                removed = true;
            }
            else
            {
                anyFading = true;
            }
        }

        if (!anyFading)
            _fadeTimer.Stop();
        _canvas.InvalidateVisual();
        if (removed)
            MarksChanged?.Invoke();
    }

    private void RenderMarks(DrawingContext dc)
    {
        long now = Environment.TickCount64;
        foreach (var mark in _marks)
        {
            double opacity = 1.0;
            if (mark.CreatedTicks.HasValue)
            {
                double age = (now - mark.CreatedTicks.Value) / 1000.0;
                if (age >= HoldSeconds)
                {
                    double t = Math.Clamp((age - HoldSeconds) / FadeOutSeconds, 0, 1);
                    opacity = 1.0 - t * t;
                }
            }
            if (opacity <= 0.01)
                continue;
            dc.PushOpacity(opacity);
            AnnotationPainter.Draw(dc, mark.Annotation);
            dc.Pop();
        }

        if (_draft != null)
            AnnotationPainter.Draw(dc, _draft);
    }

    private readonly record struct LiveMark(Annotation Annotation, long? CreatedTicks);

    private sealed class LiveCanvasElement : FrameworkElement
    {
        private readonly LiveDrawOverlayWindow _owner;
        public LiveCanvasElement(LiveDrawOverlayWindow owner) => _owner = owner;
        protected override void OnRender(DrawingContext dc) => _owner.RenderMarks(dc);
    }
}
