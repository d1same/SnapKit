using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal sealed class InPlaceEditorWindow : Window
{
    private readonly EditorSession _session;
    private readonly ShotSurface _surface;
    private readonly ToolbarView _toolbar;
    private readonly Rectangle _frameInk;
    private readonly Rectangle _frame;
    private readonly Border[] _handles;
    private readonly Canvas _canvas;
    private readonly MonitorInfo _monitor;
    private readonly int _originX;
    private readonly int _originY;
    private readonly Action<IntRect>? _reframe;
    private bool _discard;
    private int _placedW = -1;
    private int _placedH = -1;
    private double _placedToolH = -1;
    private SelectionEdge? _adjust;
    private IntRect _adjustStart;
    private int _adjustScreenX;
    private int _adjustScreenY;
    private bool _adjusting;
    private bool _recapture;

    private InPlaceEditorWindow(EditorSession session, MonitorInfo monitor, int originX, int originY, Action<IntRect>? reframe)
    {
        _session = session;
        _monitor = monitor;
        _originX = originX;
        _originY = originY;
        _reframe = reframe;
        Title = Brand.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;

        _surface = new ShotSurface(session.Document);
        _toolbar = new ToolbarView(showFit: false);
        _frameInk = Dash(Brushes.Black, 4);
        _frame = Dash(Brushes.White, 0);
        _handles = new[] { Handle(), Handle(), Handle(), Handle() };
        Wire();
        _canvas = new Canvas { Background = Brushes.Transparent };
        _canvas.Children.Add(_surface);
        _canvas.Children.Add(_frameInk);
        _canvas.Children.Add(_frame);
        foreach (var handle in _handles)
            _canvas.Children.Add(handle);
        _canvas.Children.Add(_toolbar);
        Content = _canvas;
        session.Document.Changed += () => Dispatcher.BeginInvoke(PlaceIfNeeded);
        SourceInitialized += (_, _) =>
        {
            Place();
            Opacity = 1;
        };
        PreviewKeyDown += (_, e) => EditorKeys.Handle(e, _session, _surface, Discard);
        ClickAway.Attach(this, Discard, () => _session.State.Settings.CloseOnClickAway);
        _session.Delivered += () => Dispatcher.BeginInvoke(Discard);
        PreviewMouseLeftButtonDown += OnAdjustDown;
        PreviewMouseMove += OnAdjustMove;
        PreviewMouseLeftButtonUp += (_, e) => FinishAdjust(e);
        LostMouseCapture += (_, _) =>
        {
            if (_recapture)
                return;
            if (_adjust != null && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                _recapture = true;
                CaptureMouse();
                _recapture = false;
                return;
            }
            FinishAdjust(null);
        };
        _session.Flash("Drag a corner to resize · Drag edge to move · Copy copies & closes");
        Closed += (_, _) =>
        {
            if (!_discard)
            {
                try { _session.Remember(); } catch { /* render can fail on a huge image */ }
            }
        };
    }

    public static InPlaceEditorWindow Show(PixelBuffer image, int screenX, int screenY, MonitorInfo monitor, AppState state) =>
        Show(image, new IntRect(0, 0, image.Width, image.Height), screenX, screenY, monitor, state, null);

    public static InPlaceEditorWindow Show(PixelBuffer source, IntRect view, int sourceScreenX, int sourceScreenY, MonitorInfo monitor, AppState state, Action<IntRect>? reframe)
    {
        var document = new ShotDocument(source, view, state.Settings.LastBackdrop);
        var recent = state.Recent.Add(document.ComposeUnderlay());
        var session = new EditorSession(document, recent, state);
        var window = new InPlaceEditorWindow(session, monitor, sourceScreenX, sourceScreenY, reframe);
        window.Show();
        return window;
    }

    public void Discard()
    {
        _discard = true;
        Close();
    }

    private void Wire()
    {
        ApplySession();
        _session.Changed += ApplySession;
        _toolbar.ToolPicked += tool =>
        {
            _session.SelectTool(tool);
            _surface.Tool = tool;
        };
        _toolbar.ColorPicked += hex =>
        {
            _session.SetColor(hex);
            _surface.ColorHex = _session.ColorHex;
            _surface.RestyleSelection(_session.ColorHex, null, null);
        };
        _toolbar.StrokePicked += size =>
        {
            _session.SetStroke(size);
            _surface.StrokeWidth = _session.Stroke;
            _surface.RestyleSelection(null, _session.Stroke, null);
        };
        _toolbar.FontPicked += size =>
        {
            _session.SetFontSize(size);
            _surface.FontSize = size;
            _surface.RestyleSelection(null, null, size);
        };
        _toolbar.FillModePicked += mode =>
        {
            _session.SetFillMode(mode);
            _surface.FillMode = mode;
            _surface.RestyleSelection(null, null, null, mode);
        };
        _toolbar.BackdropPicked += preset => _session.SetBackdrop(preset);
        _surface.ColorPicked += hex =>
        {
            _session.SetColor(hex);
            _surface.ColorHex = _session.ColorHex;
        };
        _surface.SelectionChanged += mark => _session.SetSelectedKind(mark?.Kind);
        _toolbar.UndoClicked += () => _session.Document.Undo();
        _toolbar.RedoClicked += () => _session.Document.Redo();
        _toolbar.ClearAllClicked += () =>
        {
            _surface.CommitEditing();
            _session.ClearAll();
        };
        _toolbar.ResetStepClicked += () => _session.ResetStep();
        ShotMenu.Attach(_surface, _surface, _session, this);
        _toolbar.CopyClicked += () =>
        {
            _surface.CommitEditing();
            _session.Copy();
        };
        _toolbar.SaveClicked += () =>
        {
            _surface.CommitEditing();
            _session.Save(this);
        };
        _toolbar.QuickSaveClicked += () =>
        {
            _surface.CommitEditing();
            _session.QuickSave();
        };
        _toolbar.OcrClicked += () =>
        {
            _surface.CommitEditing();
            OcrFlow.Start(_session.Document.ComposeUnderlay(), _session.Flash, this, () => _session.State.Settings.CloseOnClickAway);
        };
        _toolbar.PinClicked += Pin;
        _toolbar.CloseClicked += Discard;
    }

    private void ApplySession()
    {
        _surface.Tool = _session.Tool;
        _surface.ColorHex = _session.ColorHex;
        _surface.StrokeWidth = _session.Stroke;
        _surface.FontSize = _session.FontSize;
        _surface.FillMode = _session.FillMode;
        _toolbar.Sync(_session);
        if (_placedW > 0)
            Dispatcher.BeginInvoke(PlaceIfNeeded);
    }

    private void PlaceIfNeeded()
    {
        if (!IsVisible && _placedW < 0)
            return;
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        double maxDip = Math.Max(_session.Document.View.Width / scale, 640);
        maxDip = Math.Min(maxDip, Math.Max(320, (_monitor.Width - 24) / scale));
        _toolbar.MaxWidth = maxDip;
        _toolbar.Measure(new Size(maxDip, double.PositiveInfinity));
        bool sizeChanged = _session.Document.View.Width != _placedW
            || _session.Document.View.Height != _placedH
            || Math.Abs(_toolbar.DesiredSize.Height - _placedToolH) > 2;
        if (sizeChanged)
            Place();
    }

    private void Place()
    {
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        int imgW = Math.Max(1, _session.Document.View.Width);
        int imgH = Math.Max(1, _session.Document.View.Height);
        _placedW = imgW;
        _placedH = imgH;

        _surface.SetPixelScale(scale);

        double maxDip = Math.Max(imgW / scale, 640);
        maxDip = Math.Min(maxDip, Math.Max(320, (_monitor.Width - 24) / scale));
        _toolbar.MaxWidth = maxDip;
        _toolbar.Measure(new Size(maxDip, double.PositiveInfinity));
        _placedToolH = _toolbar.DesiredSize.Height;
        int gapPx = (int)Math.Ceiling(8 * scale);
        int toolW = Math.Max(1, (int)Math.Ceiling(_toolbar.DesiredSize.Width * scale));
        int toolH = Math.Max(40, (int)Math.Ceiling(_toolbar.DesiredSize.Height * scale));

        int screenX = _originX + _session.Document.View.X;
        int screenY = _originY + _session.Document.View.Y;
        bool roomBelow = _monitor.Bottom - (screenY + imgH) >= toolH + gapPx;
        bool roomAbove = screenY - _monitor.Y >= toolH + gapPx;
        bool below = roomBelow || !roomAbove;

        int contentW = Math.Max(imgW, toolW);
        int contentH = imgH + toolH + gapPx;
        int winW = Math.Min(contentW, _monitor.Width);
        int winH = Math.Min(contentH, _monitor.Height);
        int winX = screenX;
        int winY = below ? screenY : screenY - toolH - gapPx;
        if (winX + winW > _monitor.Right)
            winX = _monitor.Right - winW;
        if (winY + winH > _monitor.Bottom)
            winY = _monitor.Bottom - winH;
        if (winX < _monitor.X)
            winX = _monitor.X;
        if (winY < _monitor.Y)
            winY = _monitor.Y;

        int imageX = Math.Clamp(screenX - winX, 0, Math.Max(0, winW - 1));
        int imageY = Math.Clamp(screenY - winY, 0, Math.Max(0, winH - 1));
        double imageLeft = imageX / scale;
        double imageTop = imageY / scale;
        double imageWidth = imgW / scale;
        double imageHeight = imgH / scale;
        Canvas.SetLeft(_surface, imageLeft);
        Canvas.SetTop(_surface, imageTop);
        PlaceFrame(_frameInk, imageLeft, imageTop, imageWidth, imageHeight);
        PlaceFrame(_frame, imageLeft, imageTop, imageWidth, imageHeight);
        PlaceHandles(imageLeft, imageTop, imageWidth, imageHeight);

        double toolX = imageX / scale;
        if (toolX + _toolbar.DesiredSize.Width > winW / scale)
            toolX = Math.Max(0, winW / scale - _toolbar.DesiredSize.Width);
        double toolY = below
            ? (imageY + imgH + gapPx) / scale
            : imageY / scale - _toolbar.DesiredSize.Height - gapPx / scale;
        if (toolY < 0)
            toolY = Math.Max(0, winH / scale - _toolbar.DesiredSize.Height);
        if (toolY + _toolbar.DesiredSize.Height > winH / scale)
            toolY = Math.Max(0, winH / scale - _toolbar.DesiredSize.Height);
        Canvas.SetLeft(_toolbar, Math.Max(0, toolX));
        Canvas.SetTop(_toolbar, toolY);
        Panel.SetZIndex(_toolbar, 8);
        Topmost = true;
        WindowChrome.Place(this, winX, winY, winW, winH, scale, topmost: true, activate: !_adjusting, inTaskbar: true);
    }

    private void PlaceHandles(double left, double top, double width, double height)
    {
        const double size = 12;
        var spots = new[]
        {
            (left + 3, top + 3),
            (left + width - size - 3, top + 3),
            (left + width - size - 3, top + height - size - 3),
            (left + 3, top + height - size - 3)
        };
        for (int i = 0; i < _handles.Length; i++)
        {
            Canvas.SetLeft(_handles[i], spots[i].Item1);
            Canvas.SetTop(_handles[i], spots[i].Item2);
            Panel.SetZIndex(_handles[i], 6);
        }
    }

    private static Border Handle() => new()
    {
        Width = 12,
        Height = 12,
        CornerRadius = new CornerRadius(3),
        Background = Brushes.White,
        BorderBrush = Palette.Accent,
        BorderThickness = new Thickness(1.5),
        IsHitTestVisible = false,
        SnapsToDevicePixels = true
    };

    private void OnAdjustDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _adjust != null)
            return;
        var edge = HitAdjust(e.GetPosition(_canvas));
        if (edge == null)
            return;
        e.Handled = true;
        _adjust = edge;
        _adjustStart = _session.Document.View;
        NativeMethods.GetCursorPos(out var point);
        _adjustScreenX = point.X;
        _adjustScreenY = point.Y;
        _adjusting = true;
        _session.Document.BeginEdit();
        Mouse.OverrideCursor = CursorFor(edge);
        CaptureMouse();
    }

    private void OnAdjustMove(object sender, MouseEventArgs e)
    {
        if (_adjust == null)
        {
            Cursor = CursorFor(HitAdjust(e.GetPosition(_canvas)));
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        NativeMethods.GetCursorPos(out var point);
        var next = SelectionAdjust.Apply(
            _adjustStart,
            _adjust.Value,
            point.X - _adjustScreenX,
            point.Y - _adjustScreenY,
            _session.Document.Source.Width,
            _session.Document.Source.Height);
        _session.Document.PreviewView(next);
        _toolbar.Sync(_session);
        _reframe?.Invoke(_session.Document.View);
    }

    private void FinishAdjust(MouseButtonEventArgs? e)
    {
        if (_adjust == null)
            return;
        if (e != null)
            e.Handled = true;
        bool changed = !_session.Document.View.Equals(_adjustStart);
        _adjust = null;
        _adjusting = false;
        Mouse.OverrideCursor = null;
        Cursor = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _session.Document.EndEdit(changed);
        if (changed)
            _session.State.RememberLastArea(_originX + _session.Document.View.X, _originY + _session.Document.View.Y, _session.Document.View.Width, _session.Document.View.Height);
        _reframe?.Invoke(_session.Document.View);
    }

    private SelectionEdge? HitAdjust(Point dip)
    {
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        double left = Canvas.GetLeft(_surface);
        double top = Canvas.GetTop(_surface);
        if (double.IsNaN(left) || double.IsNaN(top))
            return null;
        double width = _session.Document.View.Width / scale;
        double height = _session.Document.View.Height / scale;
        double x = dip.X - left;
        double y = dip.Y - top;
        if (x < -8 || y < -8 || x > width + 8 || y > height + 8)
            return null;
        const double corner = 18;
        const double edge = 12;
        bool nearLeft = x <= corner;
        bool nearRight = x >= width - corner;
        bool nearTop = y <= corner;
        bool nearBottom = y >= height - corner;
        if (nearLeft && nearTop)
            return SelectionEdge.NorthWest;
        if (nearRight && nearTop)
            return SelectionEdge.NorthEast;
        if (nearRight && nearBottom)
            return SelectionEdge.SouthEast;
        if (nearLeft && nearBottom)
            return SelectionEdge.SouthWest;
        if (x <= edge || y <= edge || x >= width - edge || y >= height - edge)
            return SelectionEdge.Move;
        return null;
    }

    private static Cursor? CursorFor(SelectionEdge? edge) => edge switch
    {
        SelectionEdge.Move => Cursors.SizeAll,
        SelectionEdge.NorthWest or SelectionEdge.SouthEast => Cursors.SizeNWSE,
        SelectionEdge.NorthEast or SelectionEdge.SouthWest => Cursors.SizeNESW,
        _ => null
    };

    private static Rectangle Dash(Brush stroke, double offset) => new()
    {
        Stroke = stroke,
        StrokeThickness = 1.5,
        StrokeDashArray = new DoubleCollection { 4, 4 },
        StrokeDashOffset = offset,
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
        SnapsToDevicePixels = true
    };

    private static void PlaceFrame(Rectangle frame, double left, double top, double width, double height)
    {
        const double inset = 2;
        Canvas.SetLeft(frame, left + inset);
        Canvas.SetTop(frame, top + inset);
        frame.Width = Math.Max(1, width - inset * 2);
        frame.Height = Math.Max(1, height - inset * 2);
        Panel.SetZIndex(frame, 4);
    }

    private void Pin()
    {
        try
        {
            _surface.CommitEditing();
            var bitmap = _session.Render();
            _session.Remember();
            new PinnedWindow(bitmap, _session.State).Show();
            _discard = true;
            Close();
        }
        catch (Exception ex)
        {
            _session.State.Tray?.Balloon(Brand.Name, ex.Message);
        }
    }
}
