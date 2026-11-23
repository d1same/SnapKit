using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapKit;

internal sealed class EditorWindow : Window
{
    private readonly EditorSession _session;
    private readonly ShotSurface _surface;
    private readonly Border _backdropFrame;
    private readonly Border _shotCard;
    private readonly ToolbarView _toolbar;
    private readonly ScrollViewer _scroller;
    private double _dipPerPixel = 1;
    private bool _discard;

    private EditorWindow(EditorSession session)
    {
        _session = session;
        Title = Brand.Name;
        Width = 1140;
        Height = 780;
        MinWidth = 760;
        MinHeight = 500;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _surface = new ShotSurface(session.Document);
        _shotCard = new Border
        {
            Child = _surface,
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        _backdropFrame = new Border
        {
            Padding = new Thickness(16),
            Child = _shotCard,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        _toolbar = new ToolbarView(showFit: true)
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _scroller = new ScrollViewer
        {
            Background = Palette.Bg,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = _backdropFrame,
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
        PreviewKeyDown += (_, e) => EditorKeys.Handle(
            e,
            _session,
            _surface,
            Discard,
            zoomIn: () => StepZoom(1.15),
            zoomOut: () => StepZoom(1.0 / 1.15),
            zoomReset: Fit,
            zoomActual: ZoomActual);
        ClickAway.Attach(this, Discard, () => _session.State.Settings.CloseOnClickAway);
        _session.Delivered += () => Dispatcher.BeginInvoke(Discard);
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
        var doc = new ShotDocument(image.Clone(), backdrop: state.Settings.LastBackdrop);
        var window = new EditorWindow(new EditorSession(doc, recent, state));
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
            _surface.RedactColorHex = _session.RedactColorHex;
            _surface.RestyleSelection(_session.ActiveColorHex, null, null);
        };
        _toolbar.StrokePicked += size =>
        {
            _session.SetStroke(size);
            _surface.StrokeWidth = _session.Stroke;
            _surface.RestyleSelection(null, _session.Stroke, null);
        };
        _toolbar.FontStepped += delta =>
        {
            _session.StepFontSize(delta);
            _surface.FontSize = _session.FontSize;
            _surface.RestyleSelection(null, null, _session.FontSize);
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
            _session.PickColorFromPixel(hex);
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
        ShotMenu.Attach(_surface, _surface, _session, this, RunOcr, Pin, Discard);
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
        _toolbar.OcrClicked += RunOcr;
        _toolbar.PinClicked += Pin;
        _toolbar.CloseClicked += Discard;
        _toolbar.FitClicked += Fit;
        _toolbar.ZoomInClicked += () => StepZoom(1.15);
        _toolbar.ZoomOutClicked += () => StepZoom(1.0 / 1.15);
        _session.Document.Changed += () => Dispatcher.BeginInvoke(Apply);
    }

    private void RunOcr()
    {
        _surface.CommitEditing();
        OcrFlow.Start(_session.Document.ComposeUnderlay(), message => _session.Flash(message), this, () => _session.State.Settings.CloseOnClickAway);
    }

    private void Apply()
    {
        _surface.Tool = _session.Tool;
        _surface.ColorHex = _session.ColorHex;
        _surface.RedactColorHex = _session.RedactColorHex;
        _surface.StrokeWidth = _session.Stroke;
        _surface.FontSize = _session.FontSize;
        _surface.FillMode = _session.FillMode;
        UpdateBackdropPreview();
        _toolbar.Sync(_session);
    }

    private void UpdateBackdropPreview()
    {
        var preset = _session.Document.Backdrop;
        if (preset == BackdropPreset.None)
        {
            _backdropFrame.Background = Brushes.Transparent;
            _backdropFrame.Padding = new Thickness(0);
            _backdropFrame.CornerRadius = new CornerRadius(0);
            _shotCard.CornerRadius = new CornerRadius(0);
            _shotCard.BorderThickness = new Thickness(0);
            _shotCard.Effect = null;
            return;
        }

        var metrics = BackdropLayout.Measure(preset, _session.Document.View.Width, _session.Document.View.Height);
        double scale = Math.Max(0.1, _surface.PixelScale);
        var (bg, border) = ShotRenderer.PreviewBrushes(preset);
        _backdropFrame.Background = bg;
        _backdropFrame.Padding = new Thickness(
            metrics.PadX / scale,
            metrics.PadTop / scale,
            metrics.PadX / scale,
            metrics.PadBottom / scale);
        _backdropFrame.CornerRadius = new CornerRadius(6);
        _shotCard.CornerRadius = new CornerRadius(metrics.CornerRadius / scale);
        _shotCard.BorderBrush = border;
        _shotCard.BorderThickness = new Thickness(1);
        _shotCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 6,
            Opacity = preset == BackdropPreset.Clean ? 0.18 : 0.45,
            Color = Colors.Black
        };
    }

    private double CurrentDpi()
    {
        var source = PresentationSource.FromVisual(this);
        double dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        return dpi <= 0 ? 1 : dpi;
    }

    private void Fit()
    {
        double dpi = CurrentDpi();
        double availW = Math.Max(120, _scroller.ViewportWidth - 48);
        double availH = Math.Max(120, _scroller.ViewportHeight - 48);
        if (availW < 140 || availH < 140)
        {
            availW = Math.Max(availW, ActualWidth - 64);
            availH = Math.Max(availH, ActualHeight - 160);
        }
        var metrics = BackdropLayout.Measure(_session.Document.Backdrop, _session.Document.View.Width, _session.Document.View.Height);
        double fit = Math.Min(availW / metrics.TotalWidth, availH / metrics.TotalHeight);
        SetZoom(Math.Min(1.0 / dpi, fit));
    }

    private void ZoomActual()
    {
        SetZoom(1.0 / CurrentDpi());
    }

    private void StepZoom(double factor)
    {
        SetZoom(_dipPerPixel * factor);
    }

    private void SetZoom(double nextDipPerPixel)
    {
        if (double.IsNaN(nextDipPerPixel) || nextDipPerPixel <= 0)
            nextDipPerPixel = 1;
        _dipPerPixel = Math.Clamp(nextDipPerPixel, 0.04, 6);
        _surface.SetPixelScale(1 / _dipPerPixel);
        UpdateBackdropPreview();
        _session.SetZoomPercent(_dipPerPixel * CurrentDpi() * 100.0);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        StepZoom(e.Delta > 0 ? 1.12 : 1.0 / 1.12);
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
            _surface.CommitEditing();
            var bitmap = _session.Render();
            _session.Remember();
            new PinnedWindow(bitmap, _session.State).Show();
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
            framework.Margin = new Thickness(12, 10, 12, 0);
        return element;
    }
}
