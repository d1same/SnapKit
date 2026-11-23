using System.Windows;

namespace SnapKit;

internal sealed class CaptureCoordinator
{
    private readonly AppState _state;
    private readonly List<Window> _hidden = new();
    private RecordHudWindow? _activeRecordHud;
    private bool _busy;

    public IScreenRecorder? Recorder { get; set; }

    public CaptureCoordinator(AppState state) => _state = state;

    public void Start(CaptureAction action)
    {
        switch (action)
        {
            case CaptureAction.Area:
                _ = RunOverlay(OverlayMode.Area, OverlayPurpose.Edit);
                break;
            case CaptureAction.Window:
                _ = RunOverlay(OverlayMode.Window, OverlayPurpose.Edit);
                break;
            case CaptureAction.FullScreen:
                _ = RunFullScreen();
                break;
            case CaptureAction.Scrolling:
                _ = RunOverlay(OverlayMode.ScrollPick, OverlayPurpose.Scroll);
                break;
            case CaptureAction.Ocr:
                _ = RunOverlay(OverlayMode.Area, OverlayPurpose.Ocr);
                break;
            case CaptureAction.RepeatLastArea:
                _ = RunRepeatLastArea();
                break;
            case CaptureAction.RecordVideo:
                StartRecord(RecordingFormat.Mp4);
                break;
            case CaptureAction.RecordGif:
                StartRecord(RecordingFormat.Gif);
                break;
        }
    }

    public void StartRecord(RecordingFormat format)
    {
        if (_activeRecordHud != null)
        {
            _activeRecordHud.StopFromHotkey();
            return;
        }
        _ = RunOverlay(OverlayMode.Area, format == RecordingFormat.Gif ? OverlayPurpose.RecordGif : OverlayPurpose.RecordVideo);
    }

    public void StartRepeatLastArea() => _ = RunRepeatLastArea();

    public void StartAllMonitors() => _ = RunAllMonitors();

    public void StartDelayed(int seconds) => _ = RunDelayed(seconds);

    private async Task RunOverlay(OverlayMode mode, OverlayPurpose purpose)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            await PrepareScreenAsync();
            var shots = ScreenGrabber.CaptureAll();
            RestoreScreen();
            if (shots.Count == 0)
                throw new InvalidOperationException("No monitors were found.");

            if (purpose is not (OverlayPurpose.Scroll or OverlayPurpose.RecordVideo or OverlayPurpose.RecordGif))
                Feedback(shots.Select(shot => shot.Monitor));
            var session = new OverlaySession(shots, mode, purpose)
            {
                CloseOnClickAway = () => _state.Settings.CloseOnClickAway
            };
            session.Finished += pick => OnOverlayFinished(session, purpose, pick);
            foreach (var shot in shots)
            {
                var window = new OverlayWindow(session, shot);
                session.Add(window);
                window.Show();
            }
            session.FocusUnderCursor();
        }
        catch (Exception ex)
        {
            _busy = false;
            RestoreScreen();
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
    }

    private void OnOverlayFinished(OverlaySession session, OverlayPurpose purpose, CapturePick? pick)
    {
        if (purpose == OverlayPurpose.Scroll)
        {
            if (pick == null || pick.WindowHandle == IntPtr.Zero)
            {
                _busy = false;
                return;
            }
            _ = FinishScroll(pick.WindowHandle);
            return;
        }
        _busy = false;
        if (pick?.Image == null || pick.Monitor == null)
            return;
        if (purpose is OverlayPurpose.RecordVideo or OverlayPurpose.RecordGif)
        {
            var format = purpose == OverlayPurpose.RecordGif ? RecordingFormat.Gif : RecordingFormat.Mp4;
            var picked = pick;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var hud = new RecordHudWindow(
                    _state,
                    picked.Monitor!,
                    picked.ScreenX,
                    picked.ScreenY,
                    picked.Image!.Width,
                    picked.Image.Height,
                    format,
                    () => _activeRecordHud = null);
                _activeRecordHud = hud;
                hud.ShowWithFrame();
            }));
            return;
        }
        if (purpose == OverlayPurpose.Ocr)
        {
            var image = pick.Image;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                OcrFlow.Start(image, message => _state.Tray?.Balloon(Brand.Name, message), null, () => _state.Settings.CloseOnClickAway)));
            return;
        }
        _state.RememberLastArea(pick.ScreenX, pick.ScreenY, pick.Image.Width, pick.Image.Height);
        InPlaceEditorWindow? editor = null;
        session.Dismissed += () =>
        {
            try { editor?.Discard(); }
            finally { session.CloseAll(); }
        };
        session.FocusEditor += () =>
        {
            try
            {
                if (editor is { IsVisible: true })
                    editor.Activate();
            }
            catch
            {
                // The failsafe hotkey still ends the process.
            }
        };
        try
        {
            var shot = session.Shots.FirstOrDefault(item => item.Monitor.Handle == pick.Monitor.Handle);
            var source = shot?.Pixels ?? pick.Image;
            int originX = shot?.Monitor.X ?? pick.ScreenX;
            int originY = shot?.Monitor.Y ?? pick.ScreenY;
            var view = new IntRect(pick.ScreenX - originX, pick.ScreenY - originY, pick.Image.Width, pick.Image.Height);
            editor = InPlaceEditorWindow.Show(source, view, originX, originY, pick.Monitor, _state, rect => session.Reframe(pick.Monitor, rect));
            editor.Closed += (_, _) => session.CloseAll();
            editor.Activate();
        }
        catch (Exception ex)
        {
            session.CloseAll();
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
    }

    private async Task RunRepeatLastArea()
    {
        var region = _state.Settings.LastArea;
        if (region == null || !region.IsValid)
        {
            _state.Tray?.Balloon(Brand.Name, "No previous area yet. Capture an area first.");
            return;
        }
        if (_busy)
            return;
        _busy = true;
        try
        {
            await PrepareScreenAsync();
            var shots = ScreenGrabber.CaptureAll();
            RestoreScreen();
            if (shots.Count == 0)
                throw new InvalidOperationException("No monitors were found.");

            int centerX = region.X + region.Width / 2;
            int centerY = region.Y + region.Height / 2;
            var shot = shots.FirstOrDefault(item =>
                centerX >= item.Monitor.X && centerX < item.Monitor.Right &&
                centerY >= item.Monitor.Y && centerY < item.Monitor.Bottom)
                ?? shots.FirstOrDefault(item => item.Monitor.Handle == MonitorQuery.UnderCursor().Handle)
                ?? shots[0];

            var local = new IntRect(region.X - shot.Monitor.X, region.Y - shot.Monitor.Y, region.Width, region.Height)
                .Clamp(shot.Pixels.Width, shot.Pixels.Height, SelectionAdjust.MinSize);
            Feedback(new[] { shot.Monitor });
            var editor = InPlaceEditorWindow.Show(shot.Pixels, local, shot.Monitor.X, shot.Monitor.Y, shot.Monitor, _state, null);
            editor.Activate();
        }
        catch (Exception ex)
        {
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task FinishScroll(IntPtr hwnd)
    {
        var hud = new ScrollHud();
        try
        {
            var monitor = MonitorQuery.UnderCursor();
            hud.WindowStartupLocation = WindowStartupLocation.Manual;
            hud.Show();
            double scale = monitor.Scale <= 0 ? 1 : monitor.Scale;
            hud.Dispatcher.Invoke(() =>
            {
                // The hud sizes to content after show; nudge it to the bottom of the monitor.
                int width = Math.Max(220, (int)(hud.ActualWidth * scale));
                int height = Math.Max(36, (int)(hud.ActualHeight * scale));
                WindowChrome.Place(hud, monitor.X + 16, monitor.Y + 16, width, height, scale, topmost: true, activate: false);
            });
            var image = await ScrollCapture.RunAsync(hwnd, hud, hud.Token);
            if (image == null)
            {
                _state.Tray?.Balloon(Brand.Name, "Scrolling capture was cancelled.");
                return;
            }
            if (_state.Settings.ShutterSound)
                Shutter.Play();
            EditorWindow.OpenNew(image, _state);
        }
        catch (Exception ex)
        {
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
        finally
        {
            if (hud.IsVisible)
                hud.Close();
            _busy = false;
        }
    }

    private async Task RunFullScreen()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            await PrepareScreenAsync();
            var shots = ScreenGrabber.CaptureAll();
            RestoreScreen();
            var monitor = MonitorQuery.UnderCursor();
            var shot = shots.FirstOrDefault(item => item.Monitor.Handle == monitor.Handle) ?? shots.FirstOrDefault();
            if (shot == null)
                throw new InvalidOperationException("No monitors were found.");
            Feedback(new[] { shot.Monitor });
            var editor = InPlaceEditorWindow.Show(shot.Pixels, shot.Monitor.X, shot.Monitor.Y, shot.Monitor, _state);
            editor.Closed += (_, _) => { };
        }
        catch (Exception ex)
        {
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunAllMonitors()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            await PrepareScreenAsync();
            var shots = ScreenGrabber.CaptureAll();
            RestoreScreen();
            if (shots.Count == 0)
                throw new InvalidOperationException("No monitors were found.");
            int left = shots.Min(shot => shot.Monitor.X);
            int top = shots.Min(shot => shot.Monitor.Y);
            int right = shots.Max(shot => shot.Monitor.Right);
            int bottom = shots.Max(shot => shot.Monitor.Bottom);
            var image = new PixelBuffer(Math.Max(1, right - left), Math.Max(1, bottom - top));
            foreach (var shot in shots)
                image.CopyPixelsFrom(shot.Pixels, 0, 0, shot.Monitor.X - left, shot.Monitor.Y - top, shot.Pixels.Width, shot.Pixels.Height);
            image.ForceOpaque();
            Feedback(shots.Select(shot => shot.Monitor));
            EditorWindow.OpenNew(image, _state);
        }
        catch (Exception ex)
        {
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunDelayed(int seconds)
    {
        if (_busy)
            return;
        _busy = true;
        CountdownWindow? countdown = null;
        try
        {
            await PrepareScreenAsync();
            RestoreScreen();
            countdown = new CountdownWindow(MonitorQuery.UnderCursor(), seconds);
            countdown.Show();
            for (int value = seconds; value >= 1; value--)
            {
                countdown.SetNumber(value);
                await Task.Delay(1000);
            }
            countdown.Close();
            countdown = null;
            await Task.Delay(70);
            _busy = false;
            await RunOverlay(OverlayMode.Area, OverlayPurpose.Edit);
        }
        catch (Exception ex)
        {
            _busy = false;
            _state.Tray?.Balloon(Brand.Name, ex.Message);
        }
        finally
        {
            if (countdown != null && countdown.IsVisible)
                countdown.Close();
        }
    }

    private async Task PrepareScreenAsync()
    {
        bool hid = false;
        foreach (Window window in System.Windows.Application.Current.Windows.OfType<Window>().ToList())
        {
            switch (window)
            {
                case OverlayWindow overlay:
                    overlay.ForceClose();
                    hid = true;
                    break;
                case InPlaceEditorWindow:
                case CountdownWindow:
                case FlashWindow:
                    window.Close();
                    hid = true;
                    break;
                default:
                    if (window.IsVisible)
                    {
                        window.Hide();
                        _hidden.Add(window);
                        hid = true;
                    }
                    break;
            }
        }
        if (hid)
            await Task.Delay(90);
    }

    private void RestoreScreen()
    {
        foreach (var window in _hidden.ToList())
        {
            try
            {
                if (!window.IsVisible)
                    window.Show();
            }
            catch
            {
                // The window may have closed while it was hidden.
            }
        }
        _hidden.Clear();
    }

    private void Feedback(IEnumerable<MonitorInfo> monitors)
    {
        if (_state.Settings.ShutterSound)
            Shutter.Play();
        if (_state.Settings.CaptureFlash)
            FlashWindow.Play(monitors);
    }
}
