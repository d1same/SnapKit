using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Click-through recording outline frame + Style 3 floating Start/Pause/Stop HUD.
/// Both windows are excluded from screen capture via WDA_EXCLUDEFROMCAPTURE.
/// </summary>
internal sealed class RecordHudWindow : Window
{
    private static readonly SolidColorBrush RecRed = Palette.Freeze(0xEF, 0x44, 0x44);

    private readonly AppState _state;
    private readonly MonitorInfo _monitor;
    private readonly int _screenX;
    private readonly int _screenY;
    private readonly int _rawW;
    private readonly int _rawH;
    private readonly Action _onClosedAll;
    private readonly RecordFrameWindow _frameWindow;
    private readonly LiveDrawOverlayWindow _drawOverlay;
    private readonly DispatcherTimer _timer;

    private readonly StackPanel _readyRow;
    private readonly StackPanel _liveContainer;
    private readonly StackPanel _liveRow;
    private readonly Border _drawBar;
    private readonly Border _mp4Chip;
    private readonly Border _gifChip;
    private readonly Border _cursorChip;
    private readonly Border _micChip;
    private readonly Border _sysChip;
    private readonly TextBlock _dimsText;
    private readonly Ellipse _liveDot;
    private readonly TextBlock _liveFormat;
    private readonly TextBlock _liveTime;
    private readonly Border _drawToggleChip;
    private readonly Border _fadeChip;
    private readonly Dictionary<AnnotationKind, Border> _drawToolChips = new();
    private readonly List<(string Hex, Border Swatch)> _drawSwatches = new();
    private readonly TextBlock _pauseLabel;
    private readonly Border _pauseChip;
    private readonly Border _stopChip;
    private readonly TextBlock _stopLabel;

    private RecordingFormat _format;
    private bool _showCursor;
    private bool _recordMic;
    private bool _recordSystem;
    private RegionRecorder? _recorder;
    private bool _stopping;
    private bool _pulseOn;

    public RecordHudWindow(
        AppState state,
        MonitorInfo monitor,
        int screenX,
        int screenY,
        int width,
        int height,
        RecordingFormat initialFormat,
        Action onClosedAll)
    {
        _state = state;
        _monitor = monitor;
        _screenX = screenX;
        _screenY = screenY;
        _rawW = Math.Max(16, width);
        _rawH = Math.Max(16, height);
        _format = initialFormat;
        _showCursor = state.Settings.RecordShowCursor;
        _recordMic = state.Settings.RecordMicAudio;
        _recordSystem = state.Settings.RecordSystemAudio;
        _onClosedAll = onClosedAll;

        Title = Brand.Name + " Recorder";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _frameWindow = new RecordFrameWindow(monitor, screenX, screenY, _rawW, _rawH);
        _drawOverlay = new LiveDrawOverlayWindow(monitor, screenX, screenY, _rawW, _rawH)
        {
            ColorHex = HexColor.Normalize(state.Settings.LastColorHex, "#FF3B30"),
            StrokeWidth = Math.Clamp(state.Settings.LastStroke, 2, 10),
            AutoFade = true
        };
        _drawOverlay.ExitDrawRequested += () => SetLiveDraw(false);
        _drawOverlay.BlurRegionsChanged += (pixelRects, dipRects) =>
        {
            _recorder?.SetLiveBlurRegions(pixelRects);
            _frameWindow.SetBlurGuides(dipRects);
        };

        // ── Ready row (before recording starts) ────────────────────────────────
        _readyRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _mp4Chip = ToggleChip("MP4", "Record H.264 MP4 video", () => SetFormat(RecordingFormat.Mp4));
        _gifChip = ToggleChip("GIF", "Record animated GIF", () => SetFormat(RecordingFormat.Gif));
        _cursorChip = ToggleChip("Cursor", "Include pointer cursor & click highlight rings", () =>
        {
            _showCursor = !_showCursor;
            _state.Settings.RecordShowCursor = _showCursor;
            SavePrefs();
            RefreshReadyState();
        });
        _sysChip = ToggleChip("Audio", "Capture system audio (WASAPI loopback)", () =>
        {
            _recordSystem = !_recordSystem;
            _state.Settings.RecordSystemAudio = _recordSystem;
            SavePrefs();
            RefreshReadyState();
        });
        _micChip = ToggleChip("Mic", "Capture default microphone", () =>
        {
            _recordMic = !_recordMic;
            _state.Settings.RecordMicAudio = _recordMic;
            SavePrefs();
            RefreshReadyState();
        });

        _dimsText = new TextBlock
        {
            Foreground = Palette.Muted,
            FontFamily = Palette.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };

        var recDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = RecRed,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var recBtn = new Border
        {
            Height = 28,
            Padding = new Thickness(11, 0, 12, 0),
            Margin = new Thickness(4, 0, 3, 0),
            CornerRadius = new CornerRadius(7),
            Background = Palette.ActiveSurface,
            Cursor = Cursors.Hand,
            ToolTip = "Start recording  (Enter)",
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    recDot,
                    new TextBlock
                    {
                        Text = "Record",
                        Foreground = Palette.Ink,
                        FontFamily = Palette.Font,
                        FontSize = 12.5,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        recBtn.MouseEnter += (_, _) => recBtn.Background = Palette.ActiveHover;
        recBtn.MouseLeave += (_, _) => recBtn.Background = Palette.ActiveSurface;
        recBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
        recBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; StartRecording(); };

        var cancelBtn = IconButton(LucideIcon.Close, "Cancel  (Esc)", CancelAndClose);

        _readyRow.Children.Add(_mp4Chip);
        _readyRow.Children.Add(_gifChip);
        _readyRow.Children.Add(Hairline());
        _readyRow.Children.Add(_cursorChip);
        _readyRow.Children.Add(_sysChip);
        _readyRow.Children.Add(_micChip);
        _readyRow.Children.Add(Hairline());
        _readyRow.Children.Add(_dimsText);
        _readyRow.Children.Add(recBtn);
        _readyRow.Children.Add(cancelBtn);

        // ── Live row (while recording) ─────────────────────────────────────────
        _liveRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _liveDot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = RecRed,
            Margin = new Thickness(4, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _liveFormat = new TextBlock
        {
            Text = "MP4",
            Foreground = Palette.Muted,
            FontFamily = Palette.Mono,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _liveTime = new TextBlock
        {
            Text = "00:00",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 44,
            VerticalAlignment = VerticalAlignment.Center
        };

        _drawToggleChip = ToggleIconTextChip(LucideIcon.Pen, "Draw", "Toggle live tutorial drawing over the recording area", () => SetLiveDraw(!_drawOverlay.IsDrawMode));
        _drawToggleChip.Height = 28;
        _drawToggleChip.Padding = new Thickness(9, 0, 9, 0);
        _drawToggleChip.Margin = new Thickness(2, 0, 3, 0);
        _drawToggleChip.BorderBrush = Palette.Line;
        _drawToggleChip.BorderThickness = new Thickness(1);

        _pauseLabel = new TextBlock
        {
            Text = "Pause",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _pauseChip = ActionPill(_pauseLabel, "Pause or resume recording", TogglePause, primary: false);

        _stopLabel = new TextBlock
        {
            Text = "Stop",
            Foreground = Palette.Ink,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        _stopChip = ActionPill(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { LucideIcons.Create(LucideIcon.Stop, Palette.Ink, size: 11.5, strokeWidth: 2.0, filled: true), _stopLabel }
            },
            "Stop recording and finish",
            () => _ = StopAndPresentAsync(),
            primary: true);

        _liveRow.Children.Add(_liveDot);
        _liveRow.Children.Add(_liveFormat);
        _liveRow.Children.Add(_liveTime);
        _liveRow.Children.Add(Hairline());
        _liveRow.Children.Add(_drawToggleChip);
        _liveRow.Children.Add(_pauseChip);
        _liveRow.Children.Add(_stopChip);

        // Live draw tool strip (shown beneath live row when Draw mode is active)
        var drawRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var liveTools = new (AnnotationKind Kind, LucideIcon Icon, string Tip)[]
        {
            (AnnotationKind.Arrow, LucideIcon.Arrow, "Arrow"),
            (AnnotationKind.Pen, LucideIcon.Pen, "Pen"),
            (AnnotationKind.Rectangle, LucideIcon.Rectangle, "Box"),
            (AnnotationKind.Ellipse, LucideIcon.Ellipse, "Circle"),
            (AnnotationKind.Highlighter, LucideIcon.Highlighter, "Highlighter"),
            (AnnotationKind.Number, LucideIcon.Number, "Step #"),
            (AnnotationKind.Blur, LucideIcon.Blur, "Live Blur (pixelates pixels directly into the video; never auto-fades)"),
            (AnnotationKind.Redact, LucideIcon.Redact, "Solid Blackout Redact (never auto-fades)")
        };
        foreach (var (kind, icon, tip) in liveTools)
        {
            var chip = ToggleIconChip(icon, tip, () => SelectLiveTool(kind));
            _drawToolChips[kind] = chip;
            drawRow.Children.Add(chip);
        }
        drawRow.Children.Add(Hairline());

        foreach (var hex in new[] { "#FF3B30", "#F59E0B", "#22C55E", "#0A84FF", "#F2F2F2" })
        {
            string captured = hex;
            var swatch = new Border
            {
                Width = 15,
                Height = 15,
                Margin = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(8),
                Background = Palette.Brush(captured),
                BorderBrush = Palette.LineStrong,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = captured
            };
            swatch.MouseLeftButtonDown += (_, e) => e.Handled = true;
            swatch.MouseLeftButtonUp += (_, e) => { e.Handled = true; SelectLiveColor(captured); };
            _drawSwatches.Add((captured, swatch));
            drawRow.Children.Add(swatch);
        }
        drawRow.Children.Add(Hairline());

        _fadeChip = ToggleChip("Fade", "Auto-fade marks after 2.4s (turn off to keep marks on screen)", () =>
        {
            _drawOverlay.AutoFade = !_drawOverlay.AutoFade;
            RefreshLiveDrawUi();
        });
        drawRow.Children.Add(_fadeChip);

        drawRow.Children.Add(IconButton(LucideIcon.Undo, "Undo last mark", () => _drawOverlay.Undo()));
        drawRow.Children.Add(IconButton(LucideIcon.Trash, "Clear all marks on screen", () => _drawOverlay.Clear()));

        _drawBar = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(4, 5, 4, 2),
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = drawRow
        };

        _liveContainer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed,
            Children = { _liveRow, _drawBar }
        };

        var pill = new Border
        {
            Margin = new Thickness(10),
            Background = Palette.Toolbar,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 5, 8, 5),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.5,
                Color = Colors.Black
            },
            Child = new Grid { Children = { _readyRow, _liveContainer } }
        };
        Content = pill;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled)
                return;
            try { DragMove(); } catch { /* mouse released */ }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_recorder == null)
            {
                if (e.Key == Key.Escape)
                {
                    CancelAndClose();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    StartRecording();
                    e.Handled = true;
                }
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;

        SourceInitialized += (_, _) =>
        {
            ExcludeFromCapture(this);
            PositionHud();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _drawOverlay.SafeClose();
            _frameWindow.SafeClose();
            if (_recorder is { IsRecording: true })
            {
                try { _recorder.Dispose(); } catch { /* ignore */ }
            }
            _onClosedAll();
        };

        RefreshReadyState();
        RefreshLiveDrawUi();
    }

    public void ShowWithFrame()
    {
        _drawOverlay.Show();
        _frameWindow.Show();
        Show();
        Activate();
    }

    public void StopFromHotkey()
    {
        if (_recorder != null && !_stopping)
            _ = StopAndPresentAsync();
        else if (_recorder == null)
            CancelAndClose();
    }

    private void SetLiveDraw(bool enabled)
    {
        if (_recorder == null || _stopping)
            return;
        _drawOverlay.SetDrawMode(enabled);
        _drawBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshLiveDrawUi();
        // Keep HUD above the live draw canvas so Draw/Pause/Stop remain clickable at all times.
        WindowChrome.SetTopmost(this, true);
    }

    private void SelectLiveTool(AnnotationKind kind)
    {
        _drawOverlay.ActiveTool = kind;
        RefreshLiveDrawUi();
    }

    private void SelectLiveColor(string hex)
    {
        _drawOverlay.ColorHex = hex;
        RefreshLiveDrawUi();
    }

    private void RefreshLiveDrawUi()
    {
        PaintToggle(_drawToggleChip, _drawOverlay.IsDrawMode);
        PaintToggle(_fadeChip, _drawOverlay.AutoFade);
        foreach (var (kind, chip) in _drawToolChips)
            PaintToggle(chip, _drawOverlay.ActiveTool == kind);
        foreach (var (hex, swatch) in _drawSwatches)
        {
            bool active = string.Equals(hex, _drawOverlay.ColorHex, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = active ? Palette.TextBright : Palette.LineStrong;
            swatch.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    private void SetFormat(RecordingFormat format)
    {
        _format = format;
        RefreshReadyState();
    }

    private void RefreshReadyState()
    {
        PaintToggle(_mp4Chip, _format == RecordingFormat.Mp4);
        PaintToggle(_gifChip, _format == RecordingFormat.Gif);
        PaintToggle(_cursorChip, _showCursor);
        bool isMp4 = _format == RecordingFormat.Mp4;
        _sysChip.Visibility = isMp4 ? Visibility.Visible : Visibility.Collapsed;
        _micChip.Visibility = isMp4 ? Visibility.Visible : Visibility.Collapsed;
        PaintToggle(_sysChip, _recordSystem);
        PaintToggle(_micChip, _recordMic);

        int w = isMp4 ? Math.Max(16, _rawW & ~1) : _rawW;
        int h = isMp4 ? Math.Max(16, _rawH & ~1) : _rawH;
        _dimsText.Text = $"{w}×{h}";
    }

    private void StartRecording()
    {
        if (_recorder != null)
            return;

        try
        {
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SnapKit-recordings");
            Directory.CreateDirectory(tempDir);
            string ext = _format == RecordingFormat.Gif ? ".gif" : ".mp4";
            string outPath = System.IO.Path.Combine(tempDir, $"{Brand.Name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

            int fps = _format == RecordingFormat.Gif
                ? Math.Clamp(_state.Settings.RecordGifFps, 5, 25)
                : Math.Clamp(_state.Settings.RecordVideoFps, 15, 60);

            var options = new RecordingOptions(
                _screenX,
                _screenY,
                _rawW,
                _rawH,
                _monitor.Scale <= 0 ? 1 : _monitor.Scale,
                _format,
                fps,
                _showCursor,
                _format == RecordingFormat.Mp4 && _recordMic,
                _format == RecordingFormat.Mp4 && _recordSystem);

            _recorder = new RegionRecorder();
            _recorder.SetLiveBlurRegions(_drawOverlay.GetBlurPixelRects());
            _recorder.Start(options, outPath);

            _readyRow.Visibility = Visibility.Collapsed;
            _liveContainer.Visibility = Visibility.Visible;
            _liveFormat.Text = _format == RecordingFormat.Gif ? "GIF" : "MP4";
            _liveTime.Text = "00:00";
            _frameWindow.SetRecordingState(recording: true, paused: false);
            _timer.Start();
        }
        catch (Exception ex)
        {
            _recorder?.Dispose();
            _recorder = null;
            _state.Tray?.Balloon(Brand.Name, "Could not start recording: " + ex.Message);
        }
    }

    private void TogglePause()
    {
        if (_recorder == null || _stopping)
            return;
        bool next = !_recorder.IsPaused;
        _recorder.SetPaused(next);
        _pauseLabel.Text = next ? "Resume" : "Pause";
        _liveDot.Fill = next ? Palette.Muted : RecRed;
        _frameWindow.SetRecordingState(recording: true, paused: next);
    }

    private async Task StopAndPresentAsync()
    {
        if (_recorder == null || _stopping)
            return;
        _stopping = true;
        _timer.Stop();
        _drawOverlay.SetDrawMode(false);
        _pauseChip.IsEnabled = false;
        _stopChip.IsEnabled = false;
        _stopLabel.Text = "Finishing…";

        try
        {
            var result = await _recorder.StopAsync();
            _recorder = null;
            _drawOverlay.SafeClose();
            _frameWindow.SafeClose();
            Close();

            var done = new RecordingDoneWindow(result, _state);
            done.Show();
            done.Activate();
        }
        catch (Exception ex)
        {
            _recorder = null;
            _drawOverlay.SafeClose();
            _frameWindow.SafeClose();
            Close();
            _state.Tray?.Balloon(Brand.Name, "Recording failed: " + ex.Message);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_recorder == null)
            return;
        var elapsed = _recorder.Elapsed;
        _liveTime.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        if (!_recorder.IsPaused)
        {
            _pulseOn = !_pulseOn;
            _liveDot.Opacity = _pulseOn ? 1.0 : 0.35;
        }
        else
        {
            _liveDot.Opacity = 0.6;
        }
    }

    private void CancelAndClose()
    {
        _frameWindow.SafeClose();
        Close();
    }

    private void PositionHud()
    {
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        UpdateLayout();
        double hudW = ActualWidth > 0 ? ActualWidth : 440;
        double hudH = ActualHeight > 0 ? ActualHeight : 60;

        double targetLeft = (_screenX + _rawW / 2.0) / scale - hudW / 2.0;
        double belowTop = (_screenY + _rawH) / scale + 6;
        double aboveTop = _screenY / scale - hudH - 6;

        double minLeft = _monitor.X / scale;
        double maxLeft = _monitor.Right / scale - hudW;
        double minTop = _monitor.Y / scale;
        double maxTop = _monitor.Bottom / scale - hudH;

        Left = Math.Clamp(targetLeft, minLeft, Math.Max(minLeft, maxLeft));
        if (belowTop <= maxTop)
            Top = belowTop;
        else if (aboveTop >= minTop)
            Top = aboveTop;
        else
            Top = Math.Max(minTop, maxTop);
    }

    private void SavePrefs()
    {
        try { SettingsStore.Save(_state.Settings); } catch { /* ignore */ }
    }

    private static void ExcludeFromCapture(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
    }

    private static Border Hairline() => new()
    {
        Width = 1,
        Height = 16,
        Background = Palette.Divider,
        Margin = new Thickness(6, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border ToggleChip(string label, string tip, Action click)
    {
        var chip = new Border
        {
            Height = 24,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            SnapsToDevicePixels = true,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static Border ToggleIconChip(LucideIcon icon, string tip, Action click)
    {
        var chip = new Border
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            SnapsToDevicePixels = true,
            Child = LucideIcons.Create(icon, Palette.Muted, size: 14, strokeWidth: 1.9)
        };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static Border ToggleIconTextChip(LucideIcon icon, string label, string tip, Action click)
    {
        var chip = new Border
        {
            Height = 26,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(8, 0, 9, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            SnapsToDevicePixels = true,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    LucideIcons.Create(icon, Palette.Muted, size: 13.5, strokeWidth: 1.9),
                    new TextBlock
                    {
                        Text = label,
                        Foreground = Palette.Muted,
                        FontFamily = Palette.Font,
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    }
                }
            }
        };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static void PaintToggle(Border chip, bool on)
    {
        chip.Background = on ? Palette.ActiveSurface : Brushes.Transparent;
        var ink = on ? Palette.Ink : Palette.Muted;
        if (chip.Child is TextBlock tb)
            tb.Foreground = ink;
        else if (chip.Child is Viewbox vb)
            LucideIcons.Recolor(vb, ink);
        else if (chip.Child is StackPanel sp)
        {
            foreach (var child in sp.Children)
            {
                if (child is TextBlock t) t.Foreground = ink;
                else if (child is Viewbox v) LucideIcons.Recolor(v, ink);
            }
        }
    }

    private static Border ActionPill(UIElement content, string tip, Action click, bool primary)
    {
        var idle = primary ? Palette.ActiveSurface : Palette.Elevated;
        var hover = primary ? Palette.ActiveHover : Palette.Hover;
        var chip = new Border
        {
            Height = 28,
            Margin = new Thickness(3, 0, 2, 0),
            Padding = new Thickness(11, 0, 11, 0),
            CornerRadius = new CornerRadius(7),
            Background = idle,
            BorderBrush = primary ? Palette.ActiveSurface : Palette.Line,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content,
            SnapsToDevicePixels = true
        };
        chip.MouseEnter += (_, _) => { if (chip.IsEnabled) chip.Background = hover; };
        chip.MouseLeave += (_, _) => chip.Background = idle;
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (chip.IsEnabled) click(); };
        return chip;
    }

    private static Border IconButton(LucideIcon glyph, string tip, Action click)
    {
        var chip = new Border
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = LucideIcons.Create(glyph, Palette.Muted, size: 14, strokeWidth: 1.9)
        };
        chip.MouseEnter += (_, _) => chip.Background = Palette.Hover;
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private sealed class RecordFrameWindow : Window
    {
        private const int FramePad = 3;
        private readonly Rectangle _dash;
        private readonly Canvas _blurGuides;
        private readonly MonitorInfo _monitor;
        private readonly int _x;
        private readonly int _y;
        private readonly int _w;
        private readonly int _h;
        private bool _closed;

        public RecordFrameWindow(MonitorInfo monitor, int screenX, int screenY, int width, int height)
        {
            _monitor = monitor;
            _x = screenX;
            _y = screenY;
            _w = width;
            _h = height;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            _dash = new Rectangle
            {
                Stroke = Palette.Text,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 4 },
                Fill = Brushes.Transparent,
                SnapsToDevicePixels = true
            };
            _blurGuides = new Canvas { IsHitTestVisible = false };
            Content = new Grid
            {
                Children =
                {
                    _blurGuides,
                    new Rectangle
                    {
                        Stroke = Palette.Freeze(0, 0, 0, 0xCC),
                        StrokeThickness = 3,
                        Fill = Brushes.Transparent
                    },
                    _dash
                }
            };

            SourceInitialized += (_, _) =>
            {
                double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
                WindowChrome.Place(this, _x - FramePad, _y - FramePad, _w + FramePad * 2, _h + FramePad * 2, scale, topmost: true, activate: false);
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
                    ex |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
                    NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(ex));
                    NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
                }
            };
        }

        public void SetBlurGuides(IReadOnlyList<Rect> dipRects)
        {
            _blurGuides.Children.Clear();
            double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
            double offset = FramePad / scale;
            foreach (var r in dipRects)
            {
                var box = new Border
                {
                    Width = Math.Max(4, r.Width),
                    Height = Math.Max(4, r.Height),
                    Background = Palette.Freeze(0x15, 0x15, 0x15, 0x48),
                    BorderBrush = Palette.Text,
                    BorderThickness = new Thickness(1.25),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = "BLUR",
                        Foreground = Palette.Text,
                        FontFamily = Palette.Mono,
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(4, 2, 0, 0)
                    }
                };
                Canvas.SetLeft(box, r.X + offset);
                Canvas.SetTop(box, r.Y + offset);
                _blurGuides.Children.Add(box);
            }
        }

        public void SetRecordingState(bool recording, bool paused)
        {
            _dash.Stroke = !recording
                ? Palette.Text
                : paused ? Palette.Muted : RecRed;
        }

        public void SafeClose()
        {
            if (_closed)
                return;
            _closed = true;
            try { Close(); } catch { /* already closed */ }
        }
    }
}
