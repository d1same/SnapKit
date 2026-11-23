using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Post-recording Style 3 tutorial video editor for MP4 & GIF recordings.
/// Supports scrubbing, playback, trim start/end handles, and timed vector annotations
/// (Arrows, Pen, Box, Circle, Highlighter, Text, Callout, Step #, Blur, Redact, Spotlight)
/// with smooth fade-in/out and one-click export to Clipboard, Quick Save, or Save as….
/// </summary>
internal sealed class VideoEditorWindow : Window
{
    private enum ToolMode
    {
        Select,
        Arrow,
        Pen,
        Rectangle,
        Ellipse,
        Highlighter,
        Text,
        Callout,
        Number,
        Blur,
        Redact,
        Spotlight
    }

    private readonly RecordingResult _source;
    private readonly AppState _state;
    private readonly VideoAnnotationTrack _track;
    private readonly DispatcherTimer _playbackTimer;
    private readonly List<(PixelBuffer Frame, int DelayMs, BitmapSource Source)> _gifFrames = new();
    private readonly List<double> _gifFrameStarts = new();

    private readonly MediaElement? _media;
    private readonly Image? _gifImage;
    private readonly VideoOverlayCanvas _overlay;
    private readonly Canvas _inlineHost;

    private readonly Dictionary<ToolMode, Border> _toolButtons = new();
    private readonly List<(string Hex, Border Swatch)> _swatches = new();
    private readonly List<(double Size, Border Dot)> _strokeDots = new();
    private readonly List<(double Strength, Border Chip)> _blurChips = new();
    private readonly List<(ShapeFillMode Mode, Border Chip)> _fillChips = new();
    private readonly StackPanel _strokeGroup;
    private readonly StackPanel _blurGroup;
    private readonly StackPanel _fontGroup;
    private readonly StackPanel _fillGroup;
    private readonly StackPanel _timingGroup;
    private readonly TextBlock _fontSizeLabel;
    private readonly TextBlock _timingLabel;
    private readonly TextBlock _playLabel;
    private readonly ContentControl _playIconHost;
    private readonly TextBlock _timeLabel;
    private readonly TextBlock _statusText;
    private readonly Border _copyBtn;
    private readonly TextBlock _copyBtnLabel;
    private readonly Border _quickSaveBtn;
    private readonly Border _saveAsBtn;
    private readonly TimelineStrip _timeline;

    private ToolMode _tool = ToolMode.Arrow;
    private string _colorHex;
    private double _stroke;
    private double _fontSize;
    private ShapeFillMode _fillMode;
    private Guid? _selectedId;
    private double _currentTime;
    private bool _playing;
    private bool _exporting;
    private bool _mediaLoaded;
    private DateTime _gifPlayAnchorUtc;
    private double _gifPlayAnchorSec;
    private RecordingResult? _cachedExport;
    private int _cachedExportVersion = -1;
    private int _trackVersion;
    private CancellationTokenSource? _exportCts;
    private TextBox? _inlineEditor;
    private Guid? _editingTextId;

    // Canvas drag state
    private Annotation? _draft;
    private bool _draggingNew;
    private bool _movingSelected;
    private bool _resizingSelected;
    private AnnotationHandleKind _resizeHandle;
    private PointD _resizeAnchor;
    private Annotation? _resizeSnapshot;
    private PointD _dragStart;
    private PointD _lastDrag;
    private double _moveTotalDx;
    private double _moveTotalDy;

    public VideoEditorWindow(RecordingResult source, AppState state)
    {
        _source = source;
        _state = state;
        _colorHex = HexColor.Normalize(state.Settings.LastColorHex, "#FF3B30");
        _stroke = Math.Clamp(state.Settings.LastStroke, 2, 12);
        _fontSize = Math.Clamp(state.Settings.LastFontSize, 12, 48);
        _fillMode = state.Settings.LastFillMode;

        double durationSec = Math.Max(0.4, source.Duration.TotalSeconds);
        if (source.Format == RecordingFormat.Gif)
        {
            LoadGifFrames(source.FilePath);
            if (_gifFrameStarts.Count > 0)
                durationSec = Math.Max(0.4, _gifFrameStarts[^1]);
        }

        _track = new VideoAnnotationTrack(durationSec);
        _track.Changed += OnTrackChanged;

        string kind = source.Format == RecordingFormat.Gif ? "GIF" : "MP4";
        Title = $"{Brand.Name} — Annotate {kind}";
        Width = 1160;
        Height = 790;
        MinWidth = 860;
        MinHeight = 580;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ── Top Tool Rail ───────────────────────────────────────────────────────
        var railLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toolDefs = new (ToolMode Mode, LucideIcon Icon, string KeyHint, string Tip)[]
        {
            (ToolMode.Select, LucideIcon.Select, "V", "Select / Move / Resize  (V)"),
            (ToolMode.Arrow, LucideIcon.Arrow, "A", "Arrow  (A)"),
            (ToolMode.Pen, LucideIcon.Pen, "P", "Freehand Pen  (P)"),
            (ToolMode.Rectangle, LucideIcon.Rectangle, "R", "Box  (R)"),
            (ToolMode.Ellipse, LucideIcon.Ellipse, "E", "Circle  (E)"),
            (ToolMode.Highlighter, LucideIcon.Highlighter, "H", "Highlighter  (H)"),
            (ToolMode.Text, LucideIcon.Text, "T", "Text label  (T)"),
            (ToolMode.Callout, LucideIcon.Callout, "C", "Speech Callout  (C)"),
            (ToolMode.Number, LucideIcon.Number, "N", "Step number  (N)"),
            (ToolMode.Blur, LucideIcon.Blur, "B", "Pixelate / Blur sensitive info  (B)"),
            (ToolMode.Redact, LucideIcon.Redact, "D", "Solid blackout redact  (D)"),
            (ToolMode.Spotlight, LucideIcon.Spotlight, "S", "Spotlight highlight  (S)")
        };
        foreach (var (mode, icon, hint, tip) in toolDefs)
        {
            var btn = ToolChip(icon, hint, tip, () => SelectTool(mode));
            _toolButtons[mode] = btn;
            railLeft.Children.Add(btn);
        }

        railLeft.Children.Add(RailDivider());
        railLeft.Children.Add(SmallIconChip(LucideIcon.Undo, "Undo  (Ctrl+Z)", () => { CommitInlineText(); _track.Undo(); }));
        railLeft.Children.Add(SmallIconChip(LucideIcon.Redo, "Redo  (Ctrl+Y)", () => { CommitInlineText(); _track.Redo(); }));
        railLeft.Children.Add(SmallIconChip(LucideIcon.Trash, "Clear all annotations", () => { CommitInlineText(); _selectedId = null; _track.Clear(); }));

        var railRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _quickSaveBtn = ActionPill(LucideIcon.FolderDown, "Quick Save", "Export & save to Quick Save folder  (Ctrl+Shift+S)", () => _ = ExportAndQuickSaveAsync(), primary: false);
        _saveAsBtn = ActionPill(LucideIcon.Save, "Save as…", "Export & choose location  (Ctrl+S)", () => _ = ExportAndSaveAsAsync(), primary: false);
        _copyBtnLabel = new TextBlock
        {
            Text = "Copy file",
            Foreground = Palette.Ink,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0)
        };
        _copyBtn = ActionPillWithContent(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { LucideIcons.Create(LucideIcon.Copy, Palette.Ink, size: 13.5, strokeWidth: 2.0), _copyBtnLabel }
            },
            "Export & copy file to clipboard  (Ctrl+C)",
            () => _ = ExportAndCopyAsync(),
            primary: true);
        railRight.Children.Add(_quickSaveBtn);
        railRight.Children.Add(_saveAsBtn);
        railRight.Children.Add(_copyBtn);

        var railGrid = new Grid();
        railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        railGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(railLeft, 0);
        Grid.SetColumn(railRight, 1);
        railGrid.Children.Add(railLeft);
        railGrid.Children.Add(railRight);

        var railBar = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(12, 10, 12, 0),
            Child = railGrid
        };

        // ── Contextual Options + Timing Pill ────────────────────────────────────
        var optRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var hex in Palette.Swatches)
        {
            string captured = hex;
            var sw = new Border
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
            sw.MouseLeftButtonDown += (_, e) => e.Handled = true;
            sw.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetColor(captured); };
            _swatches.Add((captured, sw));
            optRow.Children.Add(sw);
        }

        _strokeGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _strokeGroup.Children.Add(RailDivider());
        foreach (var size in new[] { 2.0, 4.0, 7.0 })
        {
            double s = size;
            var dot = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = $"Stroke {s:0}px",
                Child = new Ellipse
                {
                    Width = s + 3,
                    Height = s + 3,
                    Fill = Palette.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            dot.MouseLeftButtonDown += (_, e) => e.Handled = true;
            dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetStroke(s); };
            _strokeDots.Add((s, dot));
            _strokeGroup.Children.Add(dot);
        }
        optRow.Children.Add(_strokeGroup);

        _blurGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _blurGroup.Children.Add(RailDivider());
        foreach (var (strength, label) in new[] { (3.0, "Soft"), (5.0, "Med"), (9.0, "Strong") })
        {
            double val = strength;
            var chip = SmallChip(label, $"Blur strength: {label}", () => SetStroke(val));
            _blurChips.Add((val, chip));
            _blurGroup.Children.Add(chip);
        }
        optRow.Children.Add(_blurGroup);

        _fontGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _fontGroup.Children.Add(RailDivider());
        _fontGroup.Children.Add(SmallChip("−", "Smaller text", () => SetFontSize(_fontSize - 2)));
        _fontSizeLabel = new TextBlock
        {
            Text = $"{_fontSize:0}px",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0)
        };
        _fontGroup.Children.Add(_fontSizeLabel);
        _fontGroup.Children.Add(SmallChip("+", "Larger text", () => SetFontSize(_fontSize + 2)));
        optRow.Children.Add(_fontGroup);

        _fillGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _fillGroup.Children.Add(RailDivider());
        foreach (var (mode, label) in new[] { (ShapeFillMode.Outline, "Outline"), (ShapeFillMode.Tint, "Tint"), (ShapeFillMode.Solid, "Solid") })
        {
            var m = mode;
            var chip = SmallChip(label, $"Shape fill: {label}", () => SetFillMode(m));
            _fillChips.Add((m, chip));
            _fillGroup.Children.Add(chip);
        }
        optRow.Children.Add(_fillGroup);

        _timingGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _timingGroup.Children.Add(RailDivider());
        _timingLabel = new TextBlock
        {
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0)
        };
        _timingGroup.Children.Add(_timingLabel);
        _timingGroup.Children.Add(SmallChip("−0.5s", "Shorten annotation by 0.5s", () => NudgeSelectedDuration(-0.5)));
        _timingGroup.Children.Add(SmallChip("+0.5s", "Extend annotation by 0.5s", () => NudgeSelectedDuration(0.5)));
        _timingGroup.Children.Add(SmallChip("Full clip", "Show annotation for the full video", PinSelectedFullClip));
        _timingGroup.Children.Add(SmallChip("Delete", "Delete selected annotation  (Del)", DeleteSelected));
        optRow.Children.Add(_timingGroup);

        var optPill = new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = optRow
        };

        // ── Center Video Stage ──────────────────────────────────────────────────
        var stageGrid = new Grid
        {
            Width = Math.Max(16, source.Width),
            Height = Math.Max(16, source.Height),
            Background = Brushes.Black,
            ClipToBounds = true
        };

        if (source.Format == RecordingFormat.Mp4)
        {
            _media = new MediaElement
            {
                Source = new Uri(source.FilePath),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                ScrubbingEnabled = true,
                Stretch = Stretch.Fill
            };
            _media.MediaOpened += (_, _) =>
            {
                _mediaLoaded = true;
                _media.Pause();
                _media.Position = TimeSpan.FromSeconds(_currentTime);
            };
            _media.MediaEnded += (_, _) =>
            {
                SetPlaying(false);
                SeekTo(_track.TrimStartSeconds);
            };
            stageGrid.Children.Add(_media);
        }
        else
        {
            _gifImage = new Image
            {
                Stretch = Stretch.Fill,
                Source = _gifFrames.Count > 0 ? _gifFrames[0].Source : null
            };
            stageGrid.Children.Add(_gifImage);
        }

        _overlay = new VideoOverlayCanvas(this)
        {
            Width = Math.Max(16, source.Width),
            Height = Math.Max(16, source.Height)
        };
        _overlay.MouseLeftButtonDown += OnCanvasDown;
        _overlay.MouseMove += OnCanvasMove;
        _overlay.MouseLeftButtonUp += OnCanvasUp;
        stageGrid.Children.Add(_overlay);

        _inlineHost = new Canvas
        {
            Width = Math.Max(16, source.Width),
            Height = Math.Max(16, source.Height)
        };
        stageGrid.Children.Add(_inlineHost);

        var stageFrame = new Border
        {
            Margin = new Thickness(14, 2, 14, 6),
            Background = Palette.Field,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = stageGrid
            }
        };

        // ── Bottom Timeline Panel ───────────────────────────────────────────────
        _playIconHost = new ContentControl
        {
            Content = LucideIcons.Create(LucideIcon.Play, Palette.Text, size: 12.5, strokeWidth: 2.0, filled: true),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            IsHitTestVisible = false
        };
        _playLabel = new TextBlock
        {
            Text = "Play",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var playBtn = ActionPillWithContent(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _playIconHost, _playLabel }
            },
            "Play / Pause  (Space)",
            () => SetPlaying(!_playing),
            primary: false);
        var backBtn = SmallChip("−1s", "Step back 1 second", () => SeekTo(_currentTime - 1.0));
        var fwdBtn = SmallChip("+1s", "Step forward 1 second", () => SeekTo(_currentTime + 1.0));

        _timeLabel = new TextBlock
        {
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };

        _statusText = new TextBlock
        {
            Text = "Draw directly on the video at any timestamp — drag timeline bars to adjust when each mark appears.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var transportLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { playBtn, backBtn, fwdBtn, _timeLabel }
        };
        var transportRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        transportRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transportRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(transportLeft, 0);
        Grid.SetColumn(_statusText, 1);
        transportRow.Children.Add(transportLeft);
        transportRow.Children.Add(_statusText);

        _timeline = new TimelineStrip(_track);
        _timeline.SeekRequested += sec =>
        {
            if (_playing)
                SetPlaying(false);
            SeekTo(sec);
        };
        _timeline.SelectionRequested += id =>
        {
            CommitInlineText();
            _selectedId = id;
            if (id.HasValue && _track.Find(id.Value) is { } item)
            {
                if (_currentTime < item.StartSeconds || _currentTime > item.EndSeconds)
                    SeekTo(item.StartSeconds + Math.Min(0.25, item.DurationSeconds * 0.2));
            }
            RefreshUi();
        };

        var timelinePanel = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(12, 0, 12, 12),
            Child = new StackPanel
            {
                Children = { transportRow, _timeline }
            }
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(railBar, 0);
        Grid.SetRow(optPill, 1);
        Grid.SetRow(stageFrame, 2);
        Grid.SetRow(timelinePanel, 3);
        root.Children.Add(railBar);
        root.Children.Add(optPill);
        root.Children.Add(stageFrame);
        root.Children.Add(timelinePanel);
        Content = root;

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playbackTimer.Tick += OnPlaybackTick;

        PreviewKeyDown += OnKeyDown;
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        Loaded += (_, _) =>
        {
            if (_media != null)
            {
                _media.Play();
                _media.Pause();
                _media.Position = TimeSpan.Zero;
            }
            RefreshUi();
        };
        Closed += (_, _) =>
        {
            _playbackTimer.Stop();
            _exportCts?.Cancel();
            try
            {
                _media?.Stop();
                _media?.Close();
            }
            catch { /* ignore */ }
        };

        RefreshUi();
    }

    private void OnTrackChanged()
    {
        _trackVersion++;
        if (_selectedId.HasValue && _track.Find(_selectedId.Value) == null)
            _selectedId = null;
        RefreshUi();
    }

    private void SelectTool(ToolMode mode)
    {
        CommitInlineText();
        _tool = mode;
        _overlay.Cursor = mode == ToolMode.Select ? Cursors.Arrow : Cursors.Cross;
        RefreshUi();
    }

    private void SetColor(string hex)
    {
        _colorHex = HexColor.Normalize(hex, "#FF3B30");
        _state.Settings.LastColorHex = _colorHex;
        if (_selectedId.HasValue)
            _track.Restyle(_selectedId.Value, colorHex: _colorHex);
        RefreshUi();
    }

    private void SetStroke(double stroke)
    {
        _stroke = Math.Clamp(stroke, 1, 16);
        _state.Settings.LastStroke = _stroke;
        if (_selectedId.HasValue)
            _track.Restyle(_selectedId.Value, stroke: _stroke);
        RefreshUi();
    }

    private void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, 10, 64);
        _state.Settings.LastFontSize = _fontSize;
        if (_selectedId.HasValue)
            _track.Restyle(_selectedId.Value, fontSize: _fontSize);
        RefreshUi();
    }

    private void SetFillMode(ShapeFillMode mode)
    {
        _fillMode = mode;
        _state.Settings.LastFillMode = mode;
        if (_selectedId.HasValue)
            _track.Restyle(_selectedId.Value, fillMode: mode);
        RefreshUi();
    }

    private void NudgeSelectedDuration(double deltaSec)
    {
        if (!_selectedId.HasValue || _track.Find(_selectedId.Value) is not { } item)
            return;
        _track.UpdateTiming(item.Id, item.StartSeconds, item.EndSeconds + deltaSec);
    }

    private void PinSelectedFullClip()
    {
        if (_selectedId.HasValue)
            _track.SetFullVideo(_selectedId.Value);
    }

    private void DeleteSelected()
    {
        if (!_selectedId.HasValue)
            return;
        var id = _selectedId.Value;
        _selectedId = null;
        _track.Remove(id);
    }

    private void SetPlaying(bool play)
    {
        if (_exporting)
            play = false;
        if (play)
            CommitInlineText();

        _playing = play;
        _playIconHost.Content = LucideIcons.Create(_playing ? LucideIcon.Pause : LucideIcon.Play, Palette.Text, size: 12.5, strokeWidth: 2.0, filled: true);
        _playLabel.Text = _playing ? "Pause" : "Play";

        if (_playing)
        {
            if (_currentTime < _track.TrimStartSeconds || _currentTime >= _track.TrimEndSeconds - 0.05)
                SeekTo(_track.TrimStartSeconds);
            if (_media != null)
            {
                _media.Position = TimeSpan.FromSeconds(_currentTime);
                _media.Play();
            }
            else
            {
                _gifPlayAnchorUtc = DateTime.UtcNow;
                _gifPlayAnchorSec = _currentTime;
            }
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
            if (_media != null)
            {
                try { _media.Pause(); } catch { /* ignore */ }
            }
        }
    }

    private void SeekTo(double seconds)
    {
        _currentTime = Math.Clamp(seconds, 0, _track.DurationSeconds);
        if (_media != null && _mediaLoaded)
        {
            try { _media.Position = TimeSpan.FromSeconds(_currentTime); } catch { /* ignore */ }
        }
        else if (_gifImage != null && _gifFrames.Count > 0)
        {
            int idx = GifFrameIndexAt(_currentTime);
            _gifImage.Source = _gifFrames[idx].Source;
        }
        if (_playing && _media == null)
        {
            _gifPlayAnchorUtc = DateTime.UtcNow;
            _gifPlayAnchorSec = _currentTime;
        }
        RefreshPlaybackVisuals();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!_playing)
            return;

        if (_media != null)
        {
            _currentTime = Math.Clamp(_media.Position.TotalSeconds, 0, _track.DurationSeconds);
        }
        else
        {
            _currentTime = _gifPlayAnchorSec + (DateTime.UtcNow - _gifPlayAnchorUtc).TotalSeconds;
            if (_gifImage != null && _gifFrames.Count > 0)
                _gifImage.Source = _gifFrames[GifFrameIndexAt(_currentTime)].Source;
        }

        if (_currentTime >= _track.TrimEndSeconds)
        {
            SeekTo(_track.TrimStartSeconds);
            if (_media != null)
                _media.Play();
            else
            {
                _gifPlayAnchorUtc = DateTime.UtcNow;
                _gifPlayAnchorSec = _currentTime;
            }
            return;
        }

        RefreshPlaybackVisuals();
    }

    private void RefreshPlaybackVisuals()
    {
        string cur = FormatTime(_currentTime);
        string tot = FormatTime(_track.DurationSeconds);
        bool trimmed = _track.TrimStartSeconds > 0.05 || _track.TrimEndSeconds < _track.DurationSeconds - 0.05;
        _timeLabel.Text = trimmed
            ? $"{cur} / {tot}  (trim {FormatTime(_track.TrimStartSeconds)}–{FormatTime(_track.TrimEndSeconds)})"
            : $"{cur} / {tot}";
        _timeline.SetState(_currentTime, _selectedId);
        _overlay.InvalidateVisual();
    }

    private void RefreshUi()
    {
        foreach (var (mode, chip) in _toolButtons)
            PaintToolChip(chip, _tool == mode);

        foreach (var (hex, sw) in _swatches)
        {
            bool active = string.Equals(hex, _colorHex, StringComparison.OrdinalIgnoreCase);
            sw.BorderBrush = active ? Palette.TextBright : Palette.LineStrong;
            sw.BorderThickness = new Thickness(active ? 2 : 1);
        }

        foreach (var (size, dot) in _strokeDots)
            dot.Background = Math.Abs(_stroke - size) < 0.6 ? Palette.Elevated : Brushes.Transparent;

        foreach (var (strength, chip) in _blurChips)
            PaintSmallChip(chip, Math.Abs(_stroke - strength) < 1.1);

        foreach (var (mode, chip) in _fillChips)
            PaintSmallChip(chip, _fillMode == mode);

        _fontSizeLabel.Text = $"{_fontSize:0}px";

        var selected = _selectedId.HasValue ? _track.Find(_selectedId.Value) : null;
        var contextKind = selected?.Annotation.Kind ?? ToolToKind(_tool);

        bool showBlur = contextKind == AnnotationKind.Blur;
        bool showFont = contextKind is AnnotationKind.Text or AnnotationKind.Callout or AnnotationKind.Number;
        bool showFill = contextKind is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
        bool showStroke = contextKind is AnnotationKind.Arrow or AnnotationKind.Pen or AnnotationKind.Line or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Highlighter;

        _strokeGroup.Visibility = showStroke ? Visibility.Visible : Visibility.Collapsed;
        _blurGroup.Visibility = showBlur ? Visibility.Visible : Visibility.Collapsed;
        _fontGroup.Visibility = showFont ? Visibility.Visible : Visibility.Collapsed;
        _fillGroup.Visibility = showFill ? Visibility.Visible : Visibility.Collapsed;

        if (selected != null)
        {
            _timingGroup.Visibility = Visibility.Visible;
            _timingLabel.Text = $"{selected.StartSeconds:0.0}s – {selected.EndSeconds:0.0}s ({selected.DurationSeconds:0.0}s)";
        }
        else
        {
            _timingGroup.Visibility = Visibility.Collapsed;
        }

        _copyBtnLabel.Text = _exporting ? "Exporting…" : (_track.HasEdits ? "Export & Copy" : "Copy file");
        _timeline.Rebuild(_currentTime, _selectedId);
        RefreshPlaybackVisuals();
    }

    // ── Canvas Pointer Interaction ──────────────────────────────────────────────
    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (_exporting)
            return;
        CommitInlineText();

        if (_playing)
            SetPlaying(false);

        var pos = e.GetPosition(_overlay);
        var pt = new PointD(Math.Clamp(pos.X, 0, _overlay.Width), Math.Clamp(pos.Y, 0, _overlay.Height));

        // 1. Check resize handles on currently selected active annotation first.
        if (_selectedId.HasValue && _track.Find(_selectedId.Value) is { } sel && sel.IsActiveAt(_currentTime))
        {
            foreach (var handle in AnnotationHandles.For(sel.Annotation))
            {
                if (Dist(pt, handle.Position) <= 12)
                {
                    _resizingSelected = true;
                    _resizeHandle = handle.Kind;
                    _resizeAnchor = AnnotationHandles.AnchorFor(sel.Annotation, handle.Kind);
                    _resizeSnapshot = sel.Annotation.Clone();
                    _overlay.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        // 2. Select mode (or double-click text/callout)
        if (_tool == ToolMode.Select)
        {
            var hit = HitTestActive(pt);
            _selectedId = hit?.Id;
            if (hit != null)
            {
                if (e.ClickCount >= 2 && hit.Annotation.Kind is AnnotationKind.Text or AnnotationKind.Callout)
                {
                    BeginInlineText(hit);
                    e.Handled = true;
                    return;
                }
                _movingSelected = true;
                _dragStart = pt;
                _lastDrag = pt;
                _moveTotalDx = 0;
                _moveTotalDy = 0;
                _overlay.CaptureMouse();
            }
            RefreshUi();
            e.Handled = true;
            return;
        }

        // 3. Single-click tools: Step # and Text
        if (_tool == ToolMode.Number)
        {
            var badge = new Annotation
            {
                Kind = AnnotationKind.Number,
                ColorHex = _colorHex,
                FontSize = _fontSize,
                Points = new List<PointD> { pt }
            };
            var added = _track.Add(badge, _currentTime);
            _selectedId = added.Id;
            RefreshUi();
            e.Handled = true;
            return;
        }

        if (_tool == ToolMode.Text)
        {
            var textAnn = new Annotation
            {
                Kind = AnnotationKind.Text,
                ColorHex = _colorHex,
                FontSize = _fontSize,
                Text = "",
                Points = new List<PointD> { pt }
            };
            var added = _track.Add(textAnn, _currentTime);
            _selectedId = added.Id;
            RefreshUi();
            BeginInlineText(added);
            e.Handled = true;
            return;
        }

        // 4. Drag tools: Arrow, Pen, Box, Circle, Highlighter, Callout, Blur, Redact, Spotlight
        var kind = ToolToKind(_tool) ?? AnnotationKind.Arrow;
        _draggingNew = true;
        _dragStart = pt;
        _draft = new Annotation
        {
            Kind = kind,
            ColorHex = kind == AnnotationKind.Redact ? "#111111" : _colorHex,
            Stroke = _stroke,
            FontSize = _fontSize,
            FillMode = _fillMode,
            Text = kind == AnnotationKind.Callout ? "Note" : "",
            Points = kind is AnnotationKind.Pen or AnnotationKind.Highlighter
                ? new List<PointD> { pt }
                : new List<PointD> { pt, pt }
        };
        _overlay.CaptureMouse();
        _overlay.InvalidateVisual();
        e.Handled = true;
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(_overlay);
        var pt = new PointD(Math.Clamp(pos.X, 0, _overlay.Width), Math.Clamp(pos.Y, 0, _overlay.Height));
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_resizingSelected && _resizeSnapshot != null)
        {
            AnnotationHandles.ApplyResize(_resizeSnapshot, _resizeHandle, _resizeAnchor, pt, shift);
            _overlay.InvalidateVisual();
            return;
        }

        if (_movingSelected && _selectedId.HasValue && _track.Find(_selectedId.Value) is { } sel)
        {
            double dx = pt.X - _lastDrag.X;
            double dy = pt.Y - _lastDrag.Y;
            _lastDrag = pt;
            _moveTotalDx += dx;
            _moveTotalDy += dy;
            for (int i = 0; i < sel.Annotation.Points.Count; i++)
            {
                var p = sel.Annotation.Points[i];
                sel.Annotation.Points[i] = new PointD(p.X + dx, p.Y + dy);
            }
            _overlay.InvalidateVisual();
            return;
        }

        if (_draggingNew && _draft != null)
        {
            if (_draft.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
            {
                var last = _draft.Points[^1];
                if ((pt.X - last.X) * (pt.X - last.X) + (pt.Y - last.Y) * (pt.Y - last.Y) >= 4)
                    _draft.Points.Add(pt);
            }
            else
            {
                var end = shift
                    ? (_draft.Kind is AnnotationKind.Arrow or AnnotationKind.Line
                        ? ShapeMath.ConstrainLine(_dragStart, pt)
                        : ShapeMath.ConstrainSquare(_dragStart, pt))
                    : pt;
                _draft.Points[1] = end;
            }
            _overlay.InvalidateVisual();
        }
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_overlay.IsMouseCaptured)
            _overlay.ReleaseMouseCapture();

        if (_resizingSelected && _resizeSnapshot != null && _selectedId.HasValue)
        {
            var updated = _resizeSnapshot;
            _resizingSelected = false;
            _resizeSnapshot = null;
            _track.ReplaceAnnotation(_selectedId.Value, updated);
            e.Handled = true;
            return;
        }

        if (_movingSelected && _selectedId.HasValue && _track.Find(_selectedId.Value) is { } sel)
        {
            _movingSelected = false;
            if (Math.Abs(_moveTotalDx) > 0.5 || Math.Abs(_moveTotalDy) > 0.5)
            {
                // Revert temporary live offset then commit via undoable track operation
                for (int i = 0; i < sel.Annotation.Points.Count; i++)
                {
                    var p = sel.Annotation.Points[i];
                    sel.Annotation.Points[i] = new PointD(p.X - _moveTotalDx, p.Y - _moveTotalDy);
                }
                _track.MoveAnnotation(_selectedId.Value, _moveTotalDx, _moveTotalDy);
            }
            e.Handled = true;
            return;
        }

        if (_draggingNew && _draft != null)
        {
            _draggingNew = false;
            var mark = _draft;
            _draft = null;

            bool valid = mark.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter
                ? mark.Points.Count >= 1
                : mark.Points.Count >= 2 && (Math.Abs(mark.Points[1].X - mark.Points[0].X) >= 4 || Math.Abs(mark.Points[1].Y - mark.Points[0].Y) >= 4);

            if (valid)
            {
                double? defaultDur = mark.Kind is AnnotationKind.Blur or AnnotationKind.Redact
                    ? _track.TrimEndSeconds - _currentTime
                    : TimedAnnotation.DefaultDurationSeconds;
                var added = _track.Add(mark, _currentTime, defaultDur);
                _selectedId = added.Id;
                RefreshUi();
                if (mark.Kind == AnnotationKind.Callout)
                    BeginInlineText(added);
            }
            else
            {
                _overlay.InvalidateVisual();
            }
            e.Handled = true;
        }
    }

    private void BeginInlineText(TimedAnnotation item)
    {
        CommitInlineText();
        _editingTextId = item.Id;

        var anchor = item.Annotation.Kind == AnnotationKind.Callout && item.Annotation.Points.Count > 1
            ? item.Annotation.Points[1]
            : item.Annotation.Points[0];

        var box = new TextBox
        {
            Text = item.Annotation.Text == "Note" && item.Annotation.Kind == AnnotationKind.Callout ? "" : item.Annotation.Text,
            MinWidth = 120,
            MaxWidth = 360,
            FontFamily = Palette.Font,
            FontSize = Math.Max(12, item.Annotation.FontSize),
            Foreground = Palette.Text,
            Background = Palette.Card,
            BorderBrush = Palette.Text,
            BorderThickness = new Thickness(1.5),
            CaretBrush = Palette.Text,
            Padding = new Thickness(6, 3, 6, 3),
            AcceptsReturn = false
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                CommitInlineText();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => CommitInlineText();

        Canvas.SetLeft(box, Math.Clamp(anchor.X - 40, 8, Math.Max(8, _inlineHost.Width - 150)));
        Canvas.SetTop(box, Math.Clamp(anchor.Y - 16, 8, Math.Max(8, _inlineHost.Height - 40)));
        _inlineHost.Children.Add(box);
        _inlineEditor = box;
        box.Focus();
        box.SelectAll();
        _overlay.InvalidateVisual();
    }

    private void CommitInlineText()
    {
        if (_inlineEditor == null || !_editingTextId.HasValue)
            return;

        var box = _inlineEditor;
        var id = _editingTextId.Value;
        _inlineEditor = null;
        _editingTextId = null;
        _inlineHost.Children.Remove(box);

        if (_track.Find(id) is { } item)
        {
            string text = box.Text.Trim();
            if (string.IsNullOrEmpty(text) && item.Annotation.Kind == AnnotationKind.Text)
            {
                _track.Remove(id);
            }
            else
            {
                var updated = item.Annotation.Clone();
                updated.Text = string.IsNullOrEmpty(text) ? "Note" : text;
                _track.ReplaceAnnotation(id, updated);
            }
        }
        _overlay.InvalidateVisual();
    }

    private TimedAnnotation? HitTestActive(PointD pt)
    {
        for (int i = _track.Items.Count - 1; i >= 0; i--)
        {
            var item = _track.Items[i];
            if (!item.IsActiveAt(_currentTime))
                continue;
            var ann = item.Annotation;
            if (ann.Points.Count == 0)
                continue;

            if (ann.Kind == AnnotationKind.Number)
            {
                double r = Math.Max(14, ann.FontSize * 0.85);
                if (Dist(pt, ann.Points[0]) <= r + 6)
                    return item;
                continue;
            }
            if (ann.Kind == AnnotationKind.Text)
            {
                var p = ann.Points[0];
                if (pt.X >= p.X - 10 && pt.X <= p.X + Math.Max(80, ann.Text.Length * ann.FontSize * 0.65)
                    && pt.Y >= p.Y - 8 && pt.Y <= p.Y + ann.FontSize * 1.8)
                    return item;
                continue;
            }
            if (ann.Kind == AnnotationKind.Callout)
            {
                var bubble = AnnotationPainter.Bubble(ann, out _);
                if (pt.X >= bubble.X - 8 && pt.X <= bubble.Right + 8 && pt.Y >= bubble.Y - 8 && pt.Y <= bubble.Bottom + 8)
                    return item;
            }

            var (x, y, w, h) = ShapeMath.Bounds(ann.Points, pad: Math.Max(10, ann.Stroke * 2));
            if (pt.X >= x && pt.X <= x + w && pt.Y >= y && pt.Y <= y + h)
                return item;
        }
        return null;
    }

    private void RenderOverlay(DrawingContext dc)
    {
        var active = _track.ActiveAt(_currentTime);
        (Annotation Spot, double Opacity)? activeSpotlight = null;

        foreach (var (ann, opacity) in active)
        {
            var drawAnn = _resizingSelected && _resizeSnapshot != null && ann.Id == _resizeSnapshot.Id
                ? _resizeSnapshot
                : ann;
            bool hideText = _editingTextId.HasValue && ann.Id == _editingTextId.Value;

            if (drawAnn.Kind == AnnotationKind.Blur)
            {
                DrawBlurPreview(dc, AnnotationPainter.Box(drawAnn), drawAnn.Stroke, opacity, isDraft: false);
                continue;
            }

            if (drawAnn.Kind == AnnotationKind.Spotlight)
            {
                if (drawAnn.Points.Count >= 2)
                    activeSpotlight = (drawAnn, opacity);
                continue;
            }

            dc.PushOpacity(opacity);
            AnnotationPainter.Draw(dc, drawAnn, hideText);
            dc.Pop();
        }

        if (_draft != null)
        {
            if (_draft.Kind == AnnotationKind.Blur)
            {
                DrawBlurPreview(dc, AnnotationPainter.Box(_draft), _draft.Stroke, 1.0, isDraft: true);
            }
            else if (_draft.Kind == AnnotationKind.Spotlight && _draft.Points.Count >= 2)
            {
                activeSpotlight = (_draft, 1.0);
            }
            else
            {
                AnnotationPainter.Draw(dc, _draft);
            }
        }

        if (activeSpotlight is { } spot)
        {
            var hole = AnnotationPainter.Box(spot.Spot);
            if (hole.Width >= 2 && hole.Height >= 2)
            {
                var view = new Rect(0, 0, _overlay.Width, _overlay.Height);
                var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(view), new RectangleGeometry(hole));
                dc.PushOpacity(spot.Opacity);
                dc.DrawGeometry(Palette.Brush(spot.Spot.ColorHex, 0x8C), null, dim);
                dc.Pop();
            }
        }

        // Selection frame & resize handles
        if (_selectedId.HasValue && _track.Find(_selectedId.Value) is { } sel && sel.IsActiveAt(_currentTime))
        {
            var mark = _resizingSelected && _resizeSnapshot != null ? _resizeSnapshot : sel.Annotation;
            var (bx, by, bw, bh) = ShapeMath.Bounds(mark.Points, pad: 6);
            var selPen = new Pen(Palette.TextBright, 1.25) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, selPen, new Rect(bx, by, Math.Max(14, bw), Math.Max(14, bh)));

            var handlePen = new Pen(Palette.Ink, 1.5);
            foreach (var h in AnnotationHandles.For(mark))
                dc.DrawEllipse(Palette.TextBright, handlePen, new Point(h.Position.X, h.Position.Y), 5.5, 5.5);
        }
    }

    private static void DrawBlurPreview(DrawingContext dc, Rect rect, double stroke, double opacity, bool isDraft)
    {
        if (rect.Width < 2 || rect.Height < 2)
            return;

        double tile = Math.Clamp(Math.Round(stroke * 3), 8, 28);
        var lightTile = Palette.Freeze(0x2E, 0x2E, 0x2E, 0xCC);
        var darkTile = Palette.Freeze(0x1C, 0x1C, 0x1C, 0xCC);

        dc.PushOpacity(opacity);
        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawRectangle(darkTile, null, rect);

        int cols = (int)Math.Ceiling(rect.Width / tile);
        int rows = (int)Math.Ceiling(rect.Height / tile);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (((r + c) & 1) == 0)
                    continue;
                dc.DrawRectangle(lightTile, null, new Rect(rect.X + c * tile, rect.Y + r * tile, tile, tile));
            }
        }
        dc.Pop();

        var borderPen = new Pen(isDraft ? Palette.TextBright : Palette.LineStrong, 1.25) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, borderPen, rect);

        if (rect.Width >= 42 && rect.Height >= 20)
        {
            var tag = new FormattedText(
                "BLUR",
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(Palette.Mono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                10,
                Palette.Text,
                1.0);
            dc.DrawRoundedRectangle(Palette.Freeze(0x15, 0x15, 0x15, 0xD8), null, new Rect(rect.X + 4, rect.Y + 4, tag.Width + 8, tag.Height + 3), 3, 3);
            dc.DrawText(tag, new Point(rect.X + 8, rect.Y + 5));
        }
        dc.Pop();
    }

    // ── Export & Delivery ───────────────────────────────────────────────────────
    private async Task<RecordingResult?> EnsureExportedAsync()
    {
        CommitInlineText();
        if (_exporting)
            return null;
        if (!_track.HasEdits)
            return _source;
        if (_cachedExport != null && _cachedExportVersion == _trackVersion && File.Exists(_cachedExport.FilePath))
            return _cachedExport;

        SetPlaying(false);
        _exporting = true;
        SetActionsEnabled(false);
        _copyBtnLabel.Text = "Exporting…";
        _statusText.Foreground = Palette.Text;
        _statusText.Text = "Rendering annotated video… 0%";

        _exportCts?.Cancel();
        _exportCts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            int pct = (int)Math.Round(Math.Clamp(p, 0, 1) * 100);
            _statusText.Text = $"Rendering annotated video… {pct}%";
        });

        try
        {
            var exported = await VideoAnnotationExporter.ExportAsync(_source, _track, Dispatcher, progress, _exportCts.Token);
            _cachedExport = exported;
            _cachedExportVersion = _trackVersion;
            _statusText.Foreground = Palette.Muted;
            _statusText.Text = $"Rendered {exported.Frames} frames ({ReleaseClient.FormatBytes(exported.FileSizeBytes)}).";
            return exported;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _statusText.Foreground = Palette.Danger;
            _statusText.Text = "Export failed: " + ex.Message;
            return null;
        }
        finally
        {
            _exporting = false;
            SetActionsEnabled(true);
            RefreshUi();
        }
    }

    private async Task ExportAndCopyAsync()
    {
        var file = await EnsureExportedAsync();
        if (file == null)
            return;
        if (ImageFile.CopyFile(file.FilePath))
        {
            _statusText.Foreground = Palette.Copied;
            _statusText.Text = "Copied! Paste the annotated video anywhere with Ctrl+V.";
        }
        else
        {
            _statusText.Foreground = Palette.Warning;
            _statusText.Text = "Clipboard is busy — click Copy again.";
        }
    }

    private async Task ExportAndQuickSaveAsync()
    {
        var file = await EnsureExportedAsync();
        if (file == null)
            return;
        try
        {
            string saved = ImageFile.QuickSaveFile(file.FilePath, file.Format, _state.Settings.QuickSaveFolder);
            _statusText.Foreground = Palette.Copied;
            _statusText.Text = "Saved " + System.IO.Path.GetFileName(saved);
            _state.Tray?.Balloon(Brand.Name, "Saved to " + saved);
        }
        catch (Exception ex)
        {
            _statusText.Foreground = Palette.Danger;
            _statusText.Text = "Quick Save failed: " + ex.Message;
        }
    }

    private async Task ExportAndSaveAsAsync()
    {
        var file = await EnsureExportedAsync();
        if (file == null)
            return;
        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                string? saved = ImageFile.SaveMedia(file.FilePath, file.Format, this);
                if (saved != null)
                {
                    _statusText.Foreground = Palette.Copied;
                    _statusText.Text = "Saved " + System.IO.Path.GetFileName(saved);
                }
            }
            catch (Exception ex)
            {
                _statusText.Foreground = Palette.Danger;
                _statusText.Text = "Save failed: " + ex.Message;
            }
        }, DispatcherPriority.Input);
    }

    private void SetActionsEnabled(bool enabled)
    {
        _copyBtn.IsEnabled = enabled;
        _quickSaveBtn.IsEnabled = enabled;
        _saveAsBtn.IsEnabled = enabled;
    }

    // ── Keyboard Shortcuts ──────────────────────────────────────────────────────
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_inlineEditor != null)
            return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            if (e.Key == Key.Z) { _track.Undo(); e.Handled = true; return; }
            if (e.Key == Key.Y) { _track.Redo(); e.Handled = true; return; }
            if (e.Key == Key.C) { _ = ExportAndCopyAsync(); e.Handled = true; return; }
            if (e.Key == Key.S) { _ = ExportAndSaveAsAsync(); e.Handled = true; return; }
        }
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            _ = ExportAndQuickSaveAsync();
            e.Handled = true;
            return;
        }
        if (mods != ModifierKeys.None)
            return;

        switch (e.Key)
        {
            case Key.Space: SetPlaying(!_playing); e.Handled = true; break;
            case Key.Delete:
            case Key.Back: DeleteSelected(); e.Handled = true; break;
            case Key.Left: SeekTo(_currentTime - 0.5); e.Handled = true; break;
            case Key.Right: SeekTo(_currentTime + 0.5); e.Handled = true; break;
            case Key.V: SelectTool(ToolMode.Select); e.Handled = true; break;
            case Key.A: SelectTool(ToolMode.Arrow); e.Handled = true; break;
            case Key.P: SelectTool(ToolMode.Pen); e.Handled = true; break;
            case Key.R: SelectTool(ToolMode.Rectangle); e.Handled = true; break;
            case Key.E: SelectTool(ToolMode.Ellipse); e.Handled = true; break;
            case Key.H: SelectTool(ToolMode.Highlighter); e.Handled = true; break;
            case Key.T: SelectTool(ToolMode.Text); e.Handled = true; break;
            case Key.C: SelectTool(ToolMode.Callout); e.Handled = true; break;
            case Key.N: SelectTool(ToolMode.Number); e.Handled = true; break;
            case Key.B: SelectTool(ToolMode.Blur); e.Handled = true; break;
            case Key.D: SelectTool(ToolMode.Redact); e.Handled = true; break;
            case Key.S: SelectTool(ToolMode.Spotlight); e.Handled = true; break;
            case Key.Escape:
                if (_selectedId != null) { _selectedId = null; RefreshUi(); e.Handled = true; }
                break;
        }
    }

    // ── GIF Frame Helpers ───────────────────────────────────────────────────────
    private void LoadGifFrames(string path)
    {
        try
        {
            var decoded = VideoAnnotationExporter.DecodeGifFrames(path);
            double acc = 0;
            foreach (var (frame, delayMs) in decoded)
            {
                var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
                bmp.Freeze();
                _gifFrames.Add((frame, delayMs, bmp));
                acc += Math.Max(0.02, delayMs / 1000.0);
                _gifFrameStarts.Add(acc);
            }
        }
        catch { /* leave empty */ }
    }

    private int GifFrameIndexAt(double seconds)
    {
        if (_gifFrameStarts.Count == 0)
            return 0;
        for (int i = 0; i < _gifFrameStarts.Count; i++)
        {
            if (seconds <= _gifFrameStarts[i])
                return i;
        }
        return _gifFrameStarts.Count - 1;
    }

    // ── UI Helpers ──────────────────────────────────────────────────────────────
    private static AnnotationKind? ToolToKind(ToolMode mode) => mode switch
    {
        ToolMode.Arrow => AnnotationKind.Arrow,
        ToolMode.Pen => AnnotationKind.Pen,
        ToolMode.Rectangle => AnnotationKind.Rectangle,
        ToolMode.Ellipse => AnnotationKind.Ellipse,
        ToolMode.Highlighter => AnnotationKind.Highlighter,
        ToolMode.Text => AnnotationKind.Text,
        ToolMode.Callout => AnnotationKind.Callout,
        ToolMode.Number => AnnotationKind.Number,
        ToolMode.Blur => AnnotationKind.Blur,
        ToolMode.Redact => AnnotationKind.Redact,
        ToolMode.Spotlight => AnnotationKind.Spotlight,
        _ => null
    };

    private static double Dist(PointD a, PointD b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
    }

    private static Border RailDivider() => new()
    {
        Width = 1,
        Height = 16,
        Background = Palette.Divider,
        Margin = new Thickness(5, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border ToolChip(LucideIcon glyph, string keyHint, string tip, Action click)
    {
        var icon = LucideIcons.Create(glyph, Palette.Text, size: 14.5, strokeWidth: 1.9);
        icon.Margin = new Thickness(0, 0, 2, 2);
        var hint = new TextBlock
        {
            Text = keyHint,
            Foreground = Palette.Subtle,
            FontFamily = Palette.Font,
            FontSize = 7.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2.5, 1),
            IsHitTestVisible = false
        };
        var chip = new Border
        {
            Width = 30,
            Height = 28,
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = new Grid { Children = { icon, hint } }
        };
        chip.MouseEnter += (_, _) => { if (chip.Background == Brushes.Transparent) chip.Background = Palette.Hover; };
        chip.MouseLeave += (_, _) => { if (chip.Background == Palette.Hover) chip.Background = Brushes.Transparent; };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static void PaintToolChip(Border chip, bool active)
    {
        chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
        if (chip.Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Viewbox vb)
                    LucideIcons.Recolor(vb, active ? Palette.Ink : Palette.Text);
                else if (child is TextBlock tb)
                    tb.Foreground = active ? Palette.Ink : Palette.Subtle;
            }
        }
    }

    private static Border SmallIconChip(LucideIcon glyph, string tip, Action click)
    {
        var chip = new Border
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = LucideIcons.Create(glyph, Palette.Text, size: 14.5, strokeWidth: 1.9)
        };
        chip.MouseEnter += (_, _) => chip.Background = Palette.Hover;
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static Border SmallChip(string label, string tip, Action click)
    {
        var chip = new Border
        {
            Height = 24,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(5),
            Background = Palette.Elevated,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock
            {
                Text = label,
                Foreground = Palette.Text,
                FontFamily = Palette.Font,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        chip.MouseEnter += (_, _) => { if (chip.Background == Palette.Elevated) chip.Background = Palette.Hover; };
        chip.MouseLeave += (_, _) => { if (chip.Background == Palette.Hover) chip.Background = Palette.Elevated; };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private static void PaintSmallChip(Border chip, bool active)
    {
        chip.Background = active ? Palette.ActiveSurface : Palette.Elevated;
        if (chip.Child is TextBlock tb)
            tb.Foreground = active ? Palette.Ink : Palette.Text;
    }

    private static Border ActionPill(LucideIcon icon, string label, string tip, Action click, bool primary) =>
        ActionPillWithContent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                LucideIcons.Create(icon, primary ? Palette.Ink : Palette.Text, size: 13.5, strokeWidth: 1.9),
                new TextBlock
                {
                    Text = label,
                    Foreground = primary ? Palette.Ink : Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                }
            }
        }, tip, click, primary);

    private static Border ActionPillWithContent(UIElement content, string tip, Action click, bool primary)
    {
        var idle = primary ? Palette.ActiveSurface : Palette.Elevated;
        var hover = primary ? Palette.ActiveHover : Palette.Hover;
        var chip = new Border
        {
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(3, 0, 2, 0),
            CornerRadius = new CornerRadius(7),
            Background = idle,
            BorderBrush = primary ? Palette.ActiveSurface : Palette.Line,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content
        };
        chip.MouseEnter += (_, _) => { if (chip.IsEnabled) chip.Background = hover; };
        chip.MouseLeave += (_, _) => chip.Background = idle;
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (chip.IsEnabled) click(); };
        return chip;
    }

    private sealed class VideoOverlayCanvas : FrameworkElement
    {
        private readonly VideoEditorWindow _owner;
        public VideoOverlayCanvas(VideoEditorWindow owner)
        {
            _owner = owner;
            Cursor = Cursors.Cross;
        }
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
            new PointHitTestResult(this, hitTestParameters.HitPoint);
        protected override void OnRender(DrawingContext dc) => _owner.RenderOverlay(dc);
    }

    // ── Interactive Timeline + Trim + Annotation Clip Lanes ─────────────────────
    private sealed class TimelineStrip : StackPanel
    {
        private enum DragMode { None, Seek, TrimStart, TrimEnd, ClipMove, ClipLeft, ClipRight }

        private readonly VideoAnnotationTrack _track;
        private readonly Canvas _scrubCanvas;
        private readonly Rectangle _trimLeftShade;
        private readonly Rectangle _trimRightShade;
        private readonly Border _trimStartHandle;
        private readonly Border _trimEndHandle;
        private readonly Rectangle _playhead;
        private readonly StackPanel _clipsHost;

        private double _playheadSec;
        private Guid? _selectedId;
        private DragMode _drag;
        private Guid _dragClipId;
        private double _dragAnchorX;
        private double _dragOrigStart;
        private double _dragOrigEnd;

        public event Action<double>? SeekRequested;
        public event Action<Guid?>? SelectionRequested;

        public TimelineStrip(VideoAnnotationTrack track)
        {
            _track = track;
            Orientation = Orientation.Vertical;

            _scrubCanvas = new Canvas
            {
                Height = 26,
                Background = Palette.Field,
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };
            _trimLeftShade = new Rectangle { Fill = Palette.Freeze(0, 0, 0, 0xA8), Height = 26 };
            _trimRightShade = new Rectangle { Fill = Palette.Freeze(0, 0, 0, 0xA8), Height = 26 };
            _trimStartHandle = TrimHandle("Drag to set clip start");
            _trimEndHandle = TrimHandle("Drag to set clip end");
            _playhead = new Rectangle { Width = 2, Height = 26, Fill = Palette.TextBright, IsHitTestVisible = false };

            _scrubCanvas.Children.Add(_trimLeftShade);
            _scrubCanvas.Children.Add(_trimRightShade);
            _scrubCanvas.Children.Add(_trimStartHandle);
            _scrubCanvas.Children.Add(_trimEndHandle);
            _scrubCanvas.Children.Add(_playhead);

            _scrubCanvas.MouseLeftButtonDown += OnScrubDown;
            _scrubCanvas.MouseMove += OnScrubMove;
            _scrubCanvas.MouseLeftButtonUp += OnScrubUp;
            _scrubCanvas.SizeChanged += (_, _) => LayoutScrubber();

            var scrubBorder = new Border
            {
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = _scrubCanvas
            };

            _clipsHost = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var clipsScroll = new ScrollViewer
            {
                MaxHeight = 86,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _clipsHost
            };

            Children.Add(scrubBorder);
            Children.Add(clipsScroll);
        }

        public void SetState(double playheadSec, Guid? selectedId)
        {
            _playheadSec = playheadSec;
            _selectedId = selectedId;
            LayoutScrubber();
        }

        public void Rebuild(double playheadSec, Guid? selectedId)
        {
            _playheadSec = playheadSec;
            _selectedId = selectedId;
            LayoutScrubber();
            RebuildClips();
        }

        private void LayoutScrubber()
        {
            double w = _scrubCanvas.ActualWidth;
            if (w <= 4)
                return;

            double dur = Math.Max(0.1, _track.DurationSeconds);
            double trimStartX = (_track.TrimStartSeconds / dur) * w;
            double trimEndX = (_track.TrimEndSeconds / dur) * w;
            double playX = Math.Clamp((_playheadSec / dur) * w, 0, Math.Max(0, w - 2));

            Canvas.SetLeft(_trimLeftShade, 0);
            _trimLeftShade.Width = Math.Max(0, trimStartX);

            Canvas.SetLeft(_trimRightShade, trimEndX);
            _trimRightShade.Width = Math.Max(0, w - trimEndX);

            Canvas.SetLeft(_trimStartHandle, Math.Clamp(trimStartX - 3, 0, Math.Max(0, w - 7)));
            Canvas.SetLeft(_trimEndHandle, Math.Clamp(trimEndX - 4, 0, Math.Max(0, w - 7)));
            Canvas.SetLeft(_playhead, playX);
        }

        private void RebuildClips()
        {
            _clipsHost.Children.Clear();
            double w = Math.Max(200, _scrubCanvas.ActualWidth);
            double dur = Math.Max(0.1, _track.DurationSeconds);

            if (_track.Items.Count == 0)
            {
                _clipsHost.Children.Add(new TextBlock
                {
                    Text = "No timed annotations yet — pick Arrow (A), Box (R), Step # (N), or Text (T) and draw on the video.",
                    Foreground = Palette.Subtle,
                    FontFamily = Palette.Font,
                    FontSize = 11,
                    Margin = new Thickness(2, 2, 2, 0)
                });
                return;
            }

            foreach (var item in _track.Items)
            {
                var captured = item;
                bool isSel = _selectedId == item.Id;

                var lane = new Canvas
                {
                    Height = 22,
                    Margin = new Thickness(0, 2, 0, 2),
                    Background = Palette.Field,
                    ClipToBounds = true
                };

                double left = (item.StartSeconds / dur) * w;
                double width = Math.Max(34, ((item.EndSeconds - item.StartSeconds) / dur) * w);
                if (left + width > w)
                    left = Math.Max(0, w - width);

                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Palette.Brush(item.Annotation.ColorHex),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                var label = new TextBlock
                {
                    Text = $"{VideoAnnotationTrack.ShortLabel(item.Annotation)}  {item.StartSeconds:0.0}–{item.EndSeconds:0.0}s",
                    Foreground = isSel ? Palette.Ink : Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 10.5,
                    FontWeight = isSel ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var bar = new Border
                {
                    Width = width,
                    Height = 20,
                    Background = isSel ? Palette.ActiveSurface : Palette.Elevated,
                    BorderBrush = isSel ? Palette.TextBright : Palette.LineStrong,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 0, 6, 0),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Click to select · Drag to shift timing · Drag left/right edges to resize duration",
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { dot, label }
                    }
                };
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, 1);

                bar.MouseLeftButtonDown += (_, e) =>
                {
                    var local = e.GetPosition(bar);
                    _dragClipId = captured.Id;
                    _dragOrigStart = captured.StartSeconds;
                    _dragOrigEnd = captured.EndSeconds;
                    _dragAnchorX = e.GetPosition(lane).X;
                    if (local.X <= 8)
                        _drag = DragMode.ClipLeft;
                    else if (local.X >= bar.Width - 8)
                        _drag = DragMode.ClipRight;
                    else
                        _drag = DragMode.ClipMove;

                    SelectionRequested?.Invoke(captured.Id);
                    bar.CaptureMouse();
                    e.Handled = true;
                };
                bar.MouseMove += (_, e) =>
                {
                    if (_drag is not (DragMode.ClipMove or DragMode.ClipLeft or DragMode.ClipRight) || _dragClipId != captured.Id)
                    {
                        var local = e.GetPosition(bar);
                        bar.Cursor = (local.X <= 8 || local.X >= bar.Width - 8) ? Cursors.SizeWE : Cursors.SizeAll;
                        return;
                    }
                    double curW = Math.Max(100, lane.ActualWidth);
                    double dxSec = ((e.GetPosition(lane).X - _dragAnchorX) / curW) * _track.DurationSeconds;
                    double newStart = _dragOrigStart;
                    double newEnd = _dragOrigEnd;
                    if (_drag == DragMode.ClipMove)
                    {
                        double span = _dragOrigEnd - _dragOrigStart;
                        newStart = Math.Clamp(_dragOrigStart + dxSec, 0, Math.Max(0, _track.DurationSeconds - span));
                        newEnd = newStart + span;
                    }
                    else if (_drag == DragMode.ClipLeft)
                    {
                        newStart = Math.Clamp(_dragOrigStart + dxSec, 0, _dragOrigEnd - TimedAnnotation.MinDurationSeconds);
                    }
                    else if (_drag == DragMode.ClipRight)
                    {
                        newEnd = Math.Clamp(_dragOrigEnd + dxSec, _dragOrigStart + TimedAnnotation.MinDurationSeconds, _track.DurationSeconds);
                    }

                    // Live preview position on lane
                    Canvas.SetLeft(bar, (newStart / _track.DurationSeconds) * curW);
                    bar.Width = Math.Max(34, ((newEnd - newStart) / _track.DurationSeconds) * curW);
                    label.Text = $"{VideoAnnotationTrack.ShortLabel(captured.Annotation)}  {newStart:0.0}–{newEnd:0.0}s";
                };
                bar.MouseLeftButtonUp += (_, e) =>
                {
                    if (_drag is DragMode.ClipMove or DragMode.ClipLeft or DragMode.ClipRight && _dragClipId == captured.Id)
                    {
                        double curW = Math.Max(100, lane.ActualWidth);
                        double dxSec = ((e.GetPosition(lane).X - _dragAnchorX) / curW) * _track.DurationSeconds;
                        double newStart = _dragOrigStart;
                        double newEnd = _dragOrigEnd;
                        if (_drag == DragMode.ClipMove)
                        {
                            double span = _dragOrigEnd - _dragOrigStart;
                            newStart = Math.Clamp(_dragOrigStart + dxSec, 0, Math.Max(0, _track.DurationSeconds - span));
                            newEnd = newStart + span;
                        }
                        else if (_drag == DragMode.ClipLeft)
                        {
                            newStart = Math.Clamp(_dragOrigStart + dxSec, 0, _dragOrigEnd - TimedAnnotation.MinDurationSeconds);
                        }
                        else if (_drag == DragMode.ClipRight)
                        {
                            newEnd = Math.Clamp(_dragOrigEnd + dxSec, _dragOrigStart + TimedAnnotation.MinDurationSeconds, _track.DurationSeconds);
                        }
                        _drag = DragMode.None;
                        if (bar.IsMouseCaptured)
                            bar.ReleaseMouseCapture();
                        _track.UpdateTiming(captured.Id, newStart, newEnd);
                        e.Handled = true;
                    }
                };

                lane.Children.Add(bar);
                _clipsHost.Children.Add(lane);
            }
        }

        private void OnScrubDown(object sender, MouseButtonEventArgs e)
        {
            double w = Math.Max(1, _scrubCanvas.ActualWidth);
            double x = e.GetPosition(_scrubCanvas).X;
            double trimStartX = (_track.TrimStartSeconds / Math.Max(0.1, _track.DurationSeconds)) * w;
            double trimEndX = (_track.TrimEndSeconds / Math.Max(0.1, _track.DurationSeconds)) * w;

            if (Math.Abs(x - trimStartX) <= 8)
                _drag = DragMode.TrimStart;
            else if (Math.Abs(x - trimEndX) <= 8)
                _drag = DragMode.TrimEnd;
            else
                _drag = DragMode.Seek;

            _scrubCanvas.CaptureMouse();
            ApplyScrubDrag(x, w);
            e.Handled = true;
        }

        private void OnScrubMove(object sender, MouseEventArgs e)
        {
            if (_drag is not (DragMode.Seek or DragMode.TrimStart or DragMode.TrimEnd))
                return;
            ApplyScrubDrag(e.GetPosition(_scrubCanvas).X, Math.Max(1, _scrubCanvas.ActualWidth));
        }

        private void OnScrubUp(object sender, MouseButtonEventArgs e)
        {
            if (_drag is DragMode.Seek or DragMode.TrimStart or DragMode.TrimEnd)
            {
                _drag = DragMode.None;
                if (_scrubCanvas.IsMouseCaptured)
                    _scrubCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ApplyScrubDrag(double x, double w)
        {
            double sec = Math.Clamp(x / w, 0, 1) * _track.DurationSeconds;
            if (_drag == DragMode.Seek)
                SeekRequested?.Invoke(sec);
            else if (_drag == DragMode.TrimStart)
                _track.SetTrim(sec, _track.TrimEndSeconds);
            else if (_drag == DragMode.TrimEnd)
                _track.SetTrim(_track.TrimStartSeconds, sec);
        }

        private static Border TrimHandle(string tip) => new()
        {
            Width = 7,
            Height = 26,
            Background = Palette.ActiveSurface,
            CornerRadius = new CornerRadius(2),
            Cursor = Cursors.SizeWE,
            ToolTip = tip
        };
    }
}
