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
        ShowInTaskbar = false;
        Topmost = true;
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
        _toolbar.CopyClicked += () => _session.Copy();
        _toolbar.SaveClicked += () => _session.Save(this);
        _toolbar.OcrClicked += () => _ = ReadTextAsync();
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
        Canvas.SetLeft(_surface, imageX / scale);
        Canvas.SetTop(_surface, imageY / scale);

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
        WindowChrome.Place(this, winX, winY, winW, winH, scale, topmost: true, activate: true);
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

    private async Task ReadTextAsync()
    {
        try
        {
            var text = await OcrClient.RecognizeAsync(_session.Document.ComposeUnderlay());
            if (!string.IsNullOrWhiteSpace(text))
                Clipboard.SetText(text);
            _session.Flash(string.IsNullOrWhiteSpace(text) ? "No text found" : "Text copied");
            new OcrWindow(text).Show();
        }
        catch (Exception ex)
        {
            new OcrWindow("", ex.Message).Show();
        }
    }
}
