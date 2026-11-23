using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Transparent overlay aligned exactly to the screen recording region for live tutorial drawing.
/// Intentionally NOT excluded from capture so drawn arrows, boxes, pen strokes, highlighter,
/// redact boxes, and step badges are baked straight into the recorded MP4/GIF in real time.
/// Live Blur regions are reported to RegionRecorder in physical pixels so real pixels are
/// pixelated directly into the encoded video (while their visual guide sits on RecordFrameWindow).
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
    public event Action<IReadOnlyList<IntRect>, IReadOnlyList<Rect>>? BlurRegionsChanged;

    public IReadOnlyList<IntRect> GetBlurPixelRects()
    {
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        var list = new List<IntRect>();
        foreach (var mark in _marks)
        {
            if (mark.Annotation.Kind != AnnotationKind.Blur || mark.Annotation.Points.Count < 2)
                continue;
            var (x, y, w, h) = ShapeMath.Bounds(mark.Annotation.Points);
            int px = Math.Clamp((int)Math.Round(x * scale), 0, _width);
            int py = Math.Clamp((int)Math.Round(y * scale), 0, _height);
            int pw = Math.Clamp((int)Math.Round(w * scale), 1, _width - px);
            int ph = Math.Clamp((int)Math.Round(h * scale), 1, _height - py);
            if (pw >= 4 && ph >= 4)
                list.Add(new IntRect(px, py, pw, ph));
        }
        return list;
    }

    public IReadOnlyList<Rect> GetBlurDipRects()
    {
        var list = new List<Rect>();
        foreach (var mark in _marks)
        {
            if (mark.Annotation.Kind != AnnotationKind.Blur || mark.Annotation.Points.Count < 2)
                continue;
            list.Add(AnnotationPainter.Box(mark.Annotation));
        }
        if (_draft is { Kind: AnnotationKind.Blur, Points.Count: >= 2 })
            list.Add(AnnotationPainter.Box(_draft));
        return list;
    }

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
            Background = Palette.Freeze(0, 0, 0, 1);
            IsHitTestVisible = true;
            Cursor = Cursors.Cross;
        }
    }

    public void Undo()
    {
        if (_marks.Count == 0)
            return;
        bool wasBlur = _marks[^1].Annotation.Kind == AnnotationKind.Blur;
        _marks.RemoveAt(_marks.Count - 1);
        _canvas.InvalidateVisual();
        if (wasBlur)
            NotifyBlursChanged();
        MarksChanged?.Invoke();
    }

    public void Clear()
    {
        CancelDraft();
        _marks.Clear();
        _nextStep = 1;
        _fadeTimer.Stop();
        _canvas.InvalidateVisual();
        NotifyBlursChanged();
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
            ColorHex = ActiveTool == AnnotationKind.Redact ? "#111111" : ColorHex,
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

        if (_draft.Kind == AnnotationKind.Blur)
            NotifyBlursChanged();
        else
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
            : mark.Points.Count >= 2 && (Math.Abs(mark.Points[1].X - mark.Points[0].X) >= 4 || Math.Abs(mark.Points[1].Y - mark.Points[0].Y) >= 4);

        if (valid)
            CommitMark(mark);
        else
        {
            if (mark.Kind == AnnotationKind.Blur)
                NotifyBlursChanged();
            _canvas.InvalidateVisual();
        }
        e.Handled = true;
    }

    private void CommitMark(Annotation annotation)
    {
        // Privacy tools (Blur & Redact) never auto-fade so sensitive data stays hidden until cleared.
        bool shouldFade = AutoFade && annotation.Kind is not (AnnotationKind.Blur or AnnotationKind.Redact);
        _marks.Add(new LiveMark(annotation, shouldFade ? Environment.TickCount64 : null));
        if (shouldFade && !_fadeTimer.IsEnabled)
            _fadeTimer.Start();
        if (annotation.Kind == AnnotationKind.Blur)
            NotifyBlursChanged();
        _canvas.InvalidateVisual();
        MarksChanged?.Invoke();
    }

    private void CancelDraft()
    {
        bool wasBlur = _draft?.Kind == AnnotationKind.Blur;
        _drawing = false;
        _draft = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (wasBlur)
            NotifyBlursChanged();
        _canvas.InvalidateVisual();
    }

    private void NotifyBlursChanged() =>
        BlurRegionsChanged?.Invoke(GetBlurPixelRects(), GetBlurDipRects());

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
            // Blur is pixelated directly in RegionRecorder; its visual guide renders on WDA-excluded RecordFrameWindow.
            if (mark.Annotation.Kind == AnnotationKind.Blur)
                continue;
            double opacity = 1.0;
            if (mark.CreatedTicks.HasValue)
            {
                double age = (now - mAge(mark)) / 1000.0;
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

        if (_draft != null && _draft.Kind != AnnotationKind.Blur)
            AnnotationPainter.Draw(dc, _draft);
    }

    private static long mAge(LiveMark mark) => mark.CreatedTicks ?? Environment.TickCount64;

    private readonly record struct LiveMark(Annotation Annotation, long? CreatedTicks);

    private sealed class LiveCanvasElement : FrameworkElement
    {
        private readonly LiveDrawOverlayWindow _owner;
        public LiveCanvasElement(LiveDrawOverlayWindow owner) => _owner = owner;
        protected override void OnRender(DrawingContext dc) => _owner.RenderMarks(dc);
    }
}
