using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapKit;

internal sealed class EditorWindow : Window
{
    private readonly EditorSession _session;
    private readonly ShotSurface _surface;
    private readonly ToolbarView _toolbar;
    private readonly ScrollViewer _scroller;
    private double _dipPerPixel = 1;
    private bool _discard;

    private EditorWindow(EditorSession session)
    {
        _session = session;
        Title = Brand.Name;
        Width = 1100;
        Height = 760;
        MinWidth = 720;
        MinHeight = 480;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _surface = new ShotSurface(session.Document);
        _toolbar = new ToolbarView(showFit: true);
        _scroller = new ScrollViewer
        {
            Background = Palette.Bg,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = _surface,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                At(_toolbar, Dock.Top),
                _scroller
            }
        };

        Wire();
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        Loaded += (_, _) => Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.ContextIdle);
        PreviewKeyDown += (_, e) => EditorKeys.Handle(e, _session, _surface, Discard, Fit);
        ClickAway.Attach(this, Discard, () => _session.State.Settings.CloseOnClickAway);
        PreviewMouseWheel += OnWheel;
        Closed += (_, _) =>
        {
            if (!_discard)
            {
                try { _session.Remember(); } catch { /* keep the earlier copy */ }
            }
        };
    }

    public static EditorWindow OpenNew(PixelBuffer image, AppState state)
    {
        var recent = state.Recent.Add(image.Clone());
        return Open(image, recent, state);
    }

    public static EditorWindow Open(PixelBuffer image, RecentCapture recent, AppState state)
    {
        var window = new EditorWindow(new EditorSession(new ShotDocument(image.Clone()), recent, state));
        window.Show();
        return window;
    }

    private void Wire()
    {
        Apply();
        _session.Changed += Apply;
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
        _toolbar.FitClicked += Fit;
        _session.Document.Changed += () => Dispatcher.BeginInvoke(Apply);
    }

    private void Apply()
    {
        _surface.Tool = _session.Tool;
        _surface.ColorHex = _session.ColorHex;
        _surface.StrokeWidth = _session.Stroke;
        _surface.FontSize = _session.FontSize;
        _toolbar.Sync(_session);
    }

    private void Fit()
    {
        double dpi = 1;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            dpi = source.CompositionTarget.TransformToDevice.M11;
        if (dpi <= 0)
            dpi = 1;
        double availW = Math.Max(120, _scroller.ViewportWidth - 32);
        double availH = Math.Max(120, _scroller.ViewportHeight - 32);
        if (availW < 140 || availH < 140)
        {
            availW = Math.Max(availW, ActualWidth - 48);
            availH = Math.Max(availH, ActualHeight - 180);
        }
        double fit = Math.Min(availW / _session.Document.View.Width, availH / _session.Document.View.Height);
        _dipPerPixel = Math.Min(1.0 / dpi, fit);
        if (double.IsNaN(_dipPerPixel) || _dipPerPixel <= 0)
            _dipPerPixel = 1;
        _surface.SetPixelScale(1 / _dipPerPixel);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        _dipPerPixel = Math.Clamp(_dipPerPixel * (e.Delta > 0 ? 1.1 : 0.9), 0.04, 6);
        _surface.SetPixelScale(1 / _dipPerPixel);
        e.Handled = true;
    }

    private void Discard()
    {
        _discard = true;
        Close();
    }

    private void Pin()
    {
        try
        {
            var bitmap = _session.Render();
            _session.Remember();
            new PinnedWindow(bitmap).Show();
        }
        catch (Exception ex)
        {
            _session.State.Tray?.Balloon(Brand.Name, ex.Message);
        }
    }

    private static UIElement At(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        if (element is FrameworkElement framework)
            framework.Margin = new Thickness(12, 12, 12, 0);
        return element;
    }
}
