using System.Diagnostics;
using System.IO;

namespace SnapKit;

/// <summary>
/// Records a screen region to H.264 MP4 (with optional WASAPI mic + system audio)
/// or animated GIF89a, with live pause/resume and optional cursor + click highlight rings.
/// All Media Foundation & WASAPI COM objects are created, used, and finalized strictly
/// on MTA background threads (never on WPF's STA UI thread) to avoid cross-apartment COM failures.
/// </summary>
internal sealed class RegionRecorder : IScreenRecorder
{
    private readonly List<ClickEvent> _clicks = new();
    private CancellationTokenSource? _cts;
    private Task? _videoTask;
    private Task? _audioTask;
    private Mp4SinkWriter? _mp4;
    private GifEncoder? _gif;
    private WasapiCapture? _mic;
    private WasapiCapture? _loopback;
    private RecordingOptions? _options;
    private string _outputPath = "";
    private string? _warning;
    private Exception? _fatalError;
    private volatile bool _paused;
    private volatile IntRect[] _liveBlurRegions = Array.Empty<IntRect>();
    private long _elapsedMs;
    private int _frames;
    private bool _prevLeftDown;
    private bool _prevRightDown;
    private bool _stopped;

    public bool IsRecording => _videoTask != null && !_stopped;
    public bool IsPaused => _paused;
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Volatile.Read(ref _elapsedMs));
    public int FramesCaptured => Volatile.Read(ref _frames);
    public bool HasActiveMic => _mic != null;
    public float CurrentMicLevel => _mic?.PeakLevel ?? 0f;

    public void SetLiveBlurRegions(IReadOnlyList<IntRect> regions)
    {
        _liveBlurRegions = regions.Count == 0 ? Array.Empty<IntRect>() : regions.ToArray();
    }

    public void Start(RecordingOptions options, string outputPath)
    {
        if (IsRecording)
            throw new InvalidOperationException("A recording is already running.");

        int width = options.Format == RecordingFormat.Mp4
            ? Math.Max(16, options.Width & ~1)
            : Math.Max(8, options.Width);
        int height = options.Format == RecordingFormat.Mp4
            ? Math.Max(16, options.Height & ~1)
            : Math.Max(8, options.Height);
        int fps = options.Format == RecordingFormat.Mp4
            ? Math.Clamp(options.Fps, 10, 60)
            : Math.Clamp(options.Fps, 5, 25);

        _options = options with { Width = width, Height = height, Fps = fps };
        _outputPath = outputPath;
        _paused = false;
        _stopped = false;
        _warning = null;
        _fatalError = null;
        Volatile.Write(ref _elapsedMs, 0);
        Volatile.Write(ref _frames, 0);
        _clicks.Clear();

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (_options.Format == RecordingFormat.Mp4)
        {
            // Initialize Media Foundation & WASAPI strictly on an MTA thread (Task.Run),
            // matching the MTA apartment of VideoLoop, AudioLoop, and StopAsync finalization.
            Task.Run(() =>
            {
                var audioNotes = new List<string>();
                if (_options.RecordMic)
                {
                    _mic = WasapiCapture.TryCreate(loopback: false, out var micErr);
                    if (_mic == null && !string.IsNullOrWhiteSpace(micErr))
                        audioNotes.Add(micErr);
                }
                if (_options.RecordSystemAudio)
                {
                    _loopback = WasapiCapture.TryCreate(loopback: true, out var sysErr);
                    if (_loopback == null && !string.IsNullOrWhiteSpace(sysErr))
                        audioNotes.Add(sysErr);
                }

                bool wantAudio = _mic != null || _loopback != null;
                _mp4 = new Mp4SinkWriter(outputPath, width, height, fps, enableAudio: wantAudio);
                if (!_mp4.HasAudio)
                {
                    _mic?.Dispose();
                    _mic = null;
                    _loopback?.Dispose();
                    _loopback = null;
                    if (!string.IsNullOrWhiteSpace(_mp4.AudioFallbackNote))
                        audioNotes.Add(_mp4.AudioFallbackNote);
                }
                else
                {
                    _mic?.Start();
                    _loopback?.Start();
                }

                if (audioNotes.Count > 0)
                    _warning = string.Join(" ", audioNotes);
            }).GetAwaiter().GetResult();
        }
        else
        {
            var file = File.Create(outputPath);
            _gif = new GifEncoder(file, width, height, leaveOpen: false);
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _videoTask = Task.Run(() => VideoLoop(token), token);
        if (_mp4 is { HasAudio: true } && (_mic != null || _loopback != null))
            _audioTask = Task.Run(() => AudioLoop(token), token);
    }

    public void SetPaused(bool paused) => _paused = paused;

    public async Task<RecordingResult> StopAsync()
    {
        if (_stopped)
        {
            long existingSize = File.Exists(_outputPath) ? new FileInfo(_outputPath).Length : 0;
            return new RecordingResult(
                _outputPath,
                _options?.Format ?? RecordingFormat.Mp4,
                _options?.Width ?? 0,
                _options?.Height ?? 0,
                Elapsed,
                FramesCaptured,
                existingSize,
                _warning);
        }

        _stopped = true;
        _cts?.Cancel();

        if (_videoTask != null)
        {
            try { await _videoTask; } catch { /* handled via _fatalError */ }
            _videoTask = null;
        }
        if (_audioTask != null)
        {
            try { await _audioTask; } catch { /* cancelled */ }
            _audioTask = null;
        }

        Exception? finalizeError = null;
        await Task.Run(() =>
        {
            _mic?.Dispose();
            _mic = null;
            _loopback?.Dispose();
            _loopback = null;

            if (_mp4 != null)
            {
                try
                {
                    _mp4.Finish();
                }
                catch (Exception ex)
                {
                    finalizeError = ex;
                }
                finally
                {
                    _mp4.Dispose();
                    _mp4 = null;
                }
            }

            if (_gif != null)
            {
                try
                {
                    _gif.Finish();
                }
                catch (Exception ex)
                {
                    finalizeError ??= ex;
                }
                finally
                {
                    _gif.Dispose();
                    _gif = null;
                }
            }
        });

        _cts?.Dispose();
        _cts = null;

        var error = _fatalError ?? finalizeError;
        if (error != null)
            throw error;

        if (FramesCaptured == 0)
            throw new InvalidOperationException("No frames were captured before recording stopped.");

        long bytes = File.Exists(_outputPath) ? new FileInfo(_outputPath).Length : 0;
        if (bytes < 64)
            throw new InvalidOperationException("The recording file could not be finalized.");

        return new RecordingResult(
            _outputPath,
            _options?.Format ?? RecordingFormat.Mp4,
            _options?.Width ?? 0,
            _options?.Height ?? 0,
            Elapsed,
            FramesCaptured,
            bytes,
            _warning);
    }

    public void Dispose()
    {
        if (!_stopped)
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        }
    }

    private void VideoLoop(CancellationToken token)
    {
        try
        {
            var opts = _options!;
            double frameMs = 1000.0 / opts.Fps;
            long frameDuration100Ns = (long)Math.Round(10_000_000.0 / opts.Fps);
            var clock = Stopwatch.StartNew();
            long pausedTotalMs = 0;
            long pauseStartMs = -1;
            long nextTickMs = 0;
            long lastFrameTimeMs = -1;

            while (!token.IsCancellationRequested)
            {
                long nowMs = clock.ElapsedMilliseconds;
                if (_paused)
                {
                    if (pauseStartMs < 0)
                        pauseStartMs = nowMs;
                    Thread.Sleep(25);
                    continue;
                }
                if (pauseStartMs >= 0)
                {
                    pausedTotalMs += nowMs - pauseStartMs;
                    pauseStartMs = -1;
                    nextTickMs = nowMs - pausedTotalMs;
                }

                long activeMs = Math.Max(0, nowMs - pausedTotalMs);
                if (activeMs < nextTickMs)
                {
                    int sleep = (int)Math.Min(15, nextTickMs - activeMs);
                    if (sleep > 0)
                        Thread.Sleep(sleep);
                    continue;
                }

                var frame = ScreenGrabber.CaptureRect(opts.ScreenX, opts.ScreenY, opts.Width, opts.Height);
                var blurs = _liveBlurRegions;
                if (blurs.Length > 0)
                {
                    foreach (var r in blurs)
                    {
                        if (!r.IsEmpty)
                            frame.Pixelate(r.X, r.Y, r.Width, r.Height, block: 14);
                    }
                }
                if (opts.ShowCursor)
                    CompositeCursorAndClicks(frame, opts, activeMs);

                long deltaMs = lastFrameTimeMs < 0 ? (long)Math.Round(frameMs) : Math.Max(10, activeMs - lastFrameTimeMs);
                lastFrameTimeMs = activeMs;

                if (_mp4 != null)
                {
                    long ts100Ns = activeMs * 10_000L;
                    _mp4.WriteVideoFrame(frame, ts100Ns, Math.Max(frameDuration100Ns, deltaMs * 10_000L));
                }
                else if (_gif != null)
                {
                    _gif.AddFrame(frame, (int)Math.Clamp(deltaMs, 20, 5000));
                }

                Volatile.Write(ref _elapsedMs, activeMs);
                Interlocked.Increment(ref _frames);

                nextTickMs = activeMs + (long)Math.Round(frameMs);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
        catch (Exception ex)
        {
            _fatalError = ex;
        }
    }

    private void AudioLoop(CancellationToken token)
    {
        try
        {
            const int sliceMs = 20;
            int chunkBytes = AudioMixer.BytesForMilliseconds(sliceMs);
            long slice100Ns = sliceMs * 10_000L;
            long audioTimestamp100Ns = 0;
            var clock = Stopwatch.StartNew();
            long nextTickMs = sliceMs;

            while (!token.IsCancellationRequested)
            {
                if (_paused)
                {
                    _mic?.DiscardPending();
                    _loopback?.DiscardPending();
                    Thread.Sleep(25);
                    nextTickMs = clock.ElapsedMilliseconds + sliceMs;
                    continue;
                }

                long now = clock.ElapsedMilliseconds;
                if (now < nextTickMs)
                {
                    Thread.Sleep((int)Math.Min(10, nextTickMs - now));
                    continue;
                }

                var micData = _mic?.ReadChunk(chunkBytes) ?? Array.Empty<byte>();
                var sysData = _loopback?.ReadChunk(chunkBytes) ?? Array.Empty<byte>();
                var mixed = AudioMixer.MixPcm16(sysData, micData, chunkBytes);
                _mp4?.WriteAudioPcm(mixed, audioTimestamp100Ns, slice100Ns);

                audioTimestamp100Ns += slice100Ns;
                nextTickMs += sliceMs;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
        catch (Exception ex)
        {
            _warning = string.IsNullOrWhiteSpace(_warning)
                ? "Audio stopped early: " + ex.Message
                : _warning;
        }
    }

    private void CompositeCursorAndClicks(PixelBuffer frame, RecordingOptions opts, long activeMs)
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return;

        int rx = pt.X - opts.ScreenX;
        int ry = pt.Y - opts.ScreenY;
        bool inside = rx >= 0 && ry >= 0 && rx < opts.Width && ry < opts.Height;

        bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkLButton) & 0x8000) != 0;
        bool rightDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkRButton) & 0x8000) != 0;

        if (inside)
        {
            if (leftDown && !_prevLeftDown)
                _clicks.Add(new ClickEvent(rx, ry, activeMs, Secondary: false));
            if (rightDown && !_prevRightDown)
                _clicks.Add(new ClickEvent(rx, ry, activeMs, Secondary: true));
        }
        _prevLeftDown = leftDown;
        _prevRightDown = rightDown;

        long maxAgeMs = (long)(RecordingOverlay.PulseDurationSeconds * 1000);
        _clicks.RemoveAll(c => activeMs - c.AtMs > maxAgeMs);
        if (_clicks.Count > 0)
        {
            var pulses = new List<ClickPulse>(_clicks.Count);
            foreach (var c in _clicks)
                pulses.Add(new ClickPulse(c.X, c.Y, (activeMs - c.AtMs) / 1000.0, c.Secondary));
            RecordingOverlay.DrawClickPulses(frame, pulses);
        }

        if (inside)
            RecordingOverlay.DrawCursor(frame, rx, ry, opts.DisplayScale);
    }

    private readonly record struct ClickEvent(double X, double Y, long AtMs, bool Secondary);
}
