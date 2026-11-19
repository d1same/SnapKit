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
    private readonly MonitorInfo _monitor;
    private readonly int _originX;
    private readonly int _originY;
    private bool _discard;
    private int _placedW = -1;
    private int _placedH = -1;

    private InPlaceEditorWindow(EditorSession session, MonitorInfo monitor, int originX, int originY)
    {
        _session = session;
        _monitor = monitor;
        _originX = originX;
        _originY = originY;
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
        _frameInk = Dash(Brushes.Black, 2.75);
        _frame = Dash(Brushes.White, 0);
        Wire();
        var canvas = new Canvas { Background = Brushes.Transparent };
        canvas.Children.Add(_surface);
        canvas.Children.Add(_frameInk);
        canvas.Children.Add(_frame);
        canvas.Children.Add(_toolbar);
        Content = canvas;
        session.Document.Changed += () => Dispatcher.BeginInvoke(PlaceIfViewChanged);
        SourceInitialized += (_, _) =>
        {
            Place();
            Opacity = 1;
        };
        PreviewKeyDown += (_, e) => EditorKeys.Handle(e, _session, _surface, Discard, null);
        ClickAway.Attach(this, Discard, () => _session.State.Settings.CloseOnClickAway);
        Closed += (_, _) =>
        {
            if (!_discard)
            {
                try { _session.Remember(); } catch { /* render can fail on a huge image */ }
            }
        };
    }

    public static InPlaceEditorWindow Show(PixelBuffer image, int screenX, int screenY, MonitorInfo monitor, AppState state)
    {
        var recent = state.Recent.Add(image.Clone());
        var session = new EditorSession(new ShotDocument(image), recent, state);
        var window = new InPlaceEditorWindow(session, monitor, screenX, screenY);
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
        _surface.ColorPicked += hex =>
        {
            _session.SetColor(hex);
            _surface.ColorHex = _session.ColorHex;
        };
        _toolbar.UndoClicked += () => _session.Document.Undo();
        _toolbar.RedoClicked += () => _session.Document.Redo();
        ShotMenu.Attach(_surface, _surface, _session, this);
        _toolbar.CopyClicked += () =>
        {
            _surface.CommitEditing();
            _session.Copy();
        };
        _toolbar.SaveClicked += () => _session.Save(this);
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
        _toolbar.Sync(_session);
    }

    private void PlaceIfViewChanged()
    {
        if (_session.Document.View.Width != _placedW || _session.Document.View.Height != _placedH)
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

        double maxDip = Math.Max(imgW / scale, 280);
        _toolbar.MaxWidth = maxDip;
        _toolbar.Measure(new Size(maxDip, double.PositiveInfinity));
        int toolW = Math.Max(1, (int)Math.Ceiling(_toolbar.DesiredSize.Width * scale));
        int toolH = Math.Max(40, (int)Math.Ceiling(_toolbar.DesiredSize.Height * scale));

        int screenX = _originX + _session.Document.View.X;
        int screenY = _originY + _session.Document.View.Y;
        bool roomBelow = _monitor.Bottom - (screenY + imgH) >= toolH;
        bool roomAbove = screenY - _monitor.Y >= toolH;
        bool below = roomBelow || !roomAbove;

        int contentW = Math.Max(imgW, toolW);
        int contentH = imgH + toolH;
        int winW = Math.Min(contentW, _monitor.Width);
        int winH = Math.Min(contentH, _monitor.Height);
        int winX = screenX;
        int winY = below ? screenY : screenY - toolH;
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

        double toolX = imageX / scale;
        double toolY = below
            ? (imageY + imgH) / scale
            : imageY / scale - _toolbar.DesiredSize.Height;
        if (toolY < 0)
            toolY = Math.Max(0, winH / scale - _toolbar.DesiredSize.Height);
        if (toolY + _toolbar.DesiredSize.Height > winH / scale)
            toolY = Math.Max(0, winH / scale - _toolbar.DesiredSize.Height);
        Canvas.SetLeft(_toolbar, Math.Max(0, toolX));
        Canvas.SetTop(_toolbar, toolY);
        Panel.SetZIndex(_toolbar, 8);
        Topmost = true;
        WindowChrome.Place(this, winX, winY, winW, winH, scale, topmost: true, activate: true, inTaskbar: true);
    }

    private static Rectangle Dash(Brush stroke, double offset) => new()
    {
        Stroke = stroke,
        StrokeThickness = 2.5,
        StrokeDashArray = new DoubleCollection { 3, 2.4 },
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
            var bitmap = _session.Render();
            _session.Remember();
            new PinnedWindow(bitmap).Show();
            _discard = true;
            Close();
        }
        catch (Exception ex)
        {
            _session.State.Tray?.Balloon(Brand.Name, ex.Message);
        }
    }

}
