using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapKit;

internal sealed class InPlaceEditorWindow : Window
{
    private readonly EditorSession _session;
    private readonly ShotSurface _surface;
    private readonly ToolbarView _toolbar;
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
        Topmost = false;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;

        _surface = new ShotSurface(session.Document);
        _toolbar = new ToolbarView(showFit: false);
        Wire();
        var canvas = new Canvas { Background = Brushes.Transparent };
        canvas.Children.Add(_surface);
        canvas.Children.Add(_toolbar);
        Content = canvas;
        session.Document.Changed += () => Dispatcher.BeginInvoke(PlaceIfViewChanged);
        SourceInitialized += (_, _) =>
        {
            Place();
            Opacity = 1;
        };
        PreviewKeyDown += (_, e) => EditorKeys.Handle(e, _session, _surface, Discard, null);
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
        };
        _toolbar.StrokePicked += size =>
        {
            _session.SetStroke(size);
            _surface.StrokeWidth = _session.Stroke;
        };
        _toolbar.FontPicked += size =>
        {
            _session.SetFontSize(size);
            _surface.FontSize = size;
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
            OcrFlow.Start(_session.Document.ComposeUnderlay(), _session.Flash, this);
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

        int limitW = Math.Max(280, _monitor.Width - 96);
        int limitH = Math.Max(200, _monitor.Height - 96);
        _toolbar.MaxWidth = Math.Max(280, limitW / scale);
        _toolbar.Measure(new Size(_toolbar.MaxWidth, double.PositiveInfinity));
        int toolW = Math.Max(1, (int)Math.Ceiling(_toolbar.DesiredSize.Width * scale));
        int toolH = Math.Max(40, (int)Math.Ceiling(_toolbar.DesiredSize.Height * scale));
        int availH = Math.Max(80, limitH - toolH);
        double fit = Math.Min(1, Math.Min(limitW / (double)Math.Max(imgW, toolW), availH / (double)imgH));
        if (double.IsNaN(fit) || fit <= 0)
            fit = 1;
        _surface.SetPixelScale(scale / fit);

        int showW = Math.Max(1, (int)Math.Round(Math.Max(imgW, toolW) * fit));
        int showH = Math.Max(1, (int)Math.Round(imgH * fit));
        int winW = Math.Min(limitW, Math.Max(showW, toolW));
        int winH = Math.Min(limitH, showH + toolH);

        int screenX = _originX + _session.Document.View.X;
        int screenY = _originY + _session.Document.View.Y;
        bool below = _monitor.Bottom - (screenY + showH) >= toolH || screenY - _monitor.Y < toolH;
        int winX = Math.Clamp(screenX, _monitor.X, Math.Max(_monitor.X, _monitor.Right - winW));
        int winY = below ? screenY : screenY - toolH;
        winY = Math.Clamp(winY, _monitor.Y, Math.Max(_monitor.Y, _monitor.Bottom - winH));

        Canvas.SetLeft(_surface, 0);
        Canvas.SetTop(_surface, below ? 0 : toolH / scale);
        Canvas.SetLeft(_toolbar, 0);
        Canvas.SetTop(_toolbar, below ? showH / scale : 0);
        Topmost = false;
        WindowChrome.Place(this, winX, winY, winW, winH, scale, topmost: false, activate: true, inTaskbar: true);
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
