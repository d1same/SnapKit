using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Exports a recorded MP4 or GIF with timed tutorial annotations (arrows, boxes, text, callouts,
/// step numbers, blur, redact, spotlight) and optional trim bounds using zero external dependencies.
/// </summary>
internal static class VideoAnnotationExporter
{
    public static Task<RecordingResult> ExportAsync(
        RecordingResult source,
        VideoAnnotationTrack track,
        Dispatcher uiDispatcher,
        IProgress<double>? progress,
        CancellationToken token)
    {
        if (!track.HasEdits)
            return Task.FromResult(source);

        string tempDir = Path.Combine(Path.GetTempPath(), "SnapKit-recordings");
        Directory.CreateDirectory(tempDir);
        string ext = source.Format == RecordingFormat.Gif ? ".gif" : ".mp4";
        string outputPath = Path.Combine(tempDir, $"{Brand.Name}-annotated-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}{ext}");

        return source.Format == RecordingFormat.Gif
            ? ExportGifAsync(source, track, outputPath, uiDispatcher, progress, token)
            : ExportMp4Async(source, track, outputPath, uiDispatcher, progress, token);
    }

    private static Task<RecordingResult> ExportMp4Async(
        RecordingResult source,
        VideoAnnotationTrack track,
        string outputPath,
        Dispatcher uiDispatcher,
        IProgress<double>? progress,
        CancellationToken token)
    {
        // All Media Foundation COM work (Mp4SourceReader + Mp4SinkWriter) runs strictly in MTA.
        return Task.Run(() =>
        {
            long trimStart100Ns = (long)Math.Round(track.TrimStartSeconds * 10_000_000.0);
            long trimEnd100Ns = (long)Math.Round(track.TrimEndSeconds * 10_000_000.0);
            double spanSec = Math.Max(0.1, track.TrimEndSeconds - track.TrimStartSeconds);

            using var reader = new Mp4SourceReader(source.FilePath);
            int width = Math.Max(16, reader.Width & ~1);
            int height = Math.Max(16, reader.Height & ~1);
            int fps = Math.Clamp(reader.Fps, 10, 60);
            long defaultDur = (long)Math.Round(10_000_000.0 / fps);

            using var writer = new Mp4SinkWriter(outputPath, width, height, fps, enableAudio: reader.HasAudio);

            long cachedOverlayKey = 0;
            byte[]? cachedOverlay = null;
            int framesWritten = 0;
            long lastVideoTs = -1;
            long lastAudioTs = -1;
            long firstKeptVideoTs = -1;
            long lastKeptVideoTs = 0;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var sample = reader.ReadNext();
                if (sample.Kind == DecodedSampleKind.EndOfStream)
                    break;

                if (sample.Timestamp100Ns < trimStart100Ns)
                    continue;
                if (sample.Timestamp100Ns > trimEnd100Ns + defaultDur)
                {
                    if (sample.Kind == DecodedSampleKind.Video)
                        break;
                    continue;
                }

                long rebasedTs = Math.Max(0, sample.Timestamp100Ns - trimStart100Ns);

                if (sample.Kind == DecodedSampleKind.Video && sample.VideoFrame != null)
                {
                    var frame = sample.VideoFrame;
                    double timeSec = sample.Timestamp100Ns / 10_000_000.0;
                    ApplyAnnotations(frame, track, timeSec, uiDispatcher, ref cachedOverlayKey, ref cachedOverlay);

                    if (rebasedTs <= lastVideoTs)
                        rebasedTs = lastVideoTs + 10_000;
                    lastVideoTs = rebasedTs;

                    if (firstKeptVideoTs < 0)
                        firstKeptVideoTs = sample.Timestamp100Ns;
                    lastKeptVideoTs = sample.Timestamp100Ns;

                    long dur = sample.Duration100Ns > 0 ? sample.Duration100Ns : defaultDur;
                    writer.WriteVideoFrame(frame, rebasedTs, dur);
                    framesWritten++;

                    double pct = Math.Clamp((timeSec - track.TrimStartSeconds) / spanSec, 0, 0.98);
                    progress?.Report(pct);
                }
                else if (sample.Kind == DecodedSampleKind.Audio && sample.AudioPcm != null && writer.HasAudio)
                {
                    if (rebasedTs <= lastAudioTs)
                        rebasedTs = lastAudioTs + 10_000;
                    lastAudioTs = rebasedTs;
                    long dur = sample.Duration100Ns > 0
                        ? sample.Duration100Ns
                        : (long)Math.Round(sample.AudioPcm.Length / (double)AudioMixer.BytesPerSecond * 10_000_000.0);
                    writer.WriteAudioPcm(sample.AudioPcm, rebasedTs, Math.Max(10_000, dur));
                }
            }

            if (framesWritten == 0)
                throw new InvalidOperationException("No video frames fell inside the selected trim range.");

            writer.Finish();
            progress?.Report(1.0);

            long bytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            if (bytes < 64)
                throw new InvalidOperationException("Annotated MP4 file could not be finalized.");

            double finalDuration = firstKeptVideoTs >= 0
                ? Math.Max(0.2, (lastKeptVideoTs - firstKeptVideoTs + defaultDur) / 10_000_000.0)
                : spanSec;

            return new RecordingResult(
                outputPath,
                RecordingFormat.Mp4,
                width,
                height,
                TimeSpan.FromSeconds(finalDuration),
                framesWritten,
                bytes,
                writer.AudioFallbackNote);
        }, token);
    }

    private static Task<RecordingResult> ExportGifAsync(
        RecordingResult source,
        VideoAnnotationTrack track,
        string outputPath,
        Dispatcher uiDispatcher,
        IProgress<double>? progress,
        CancellationToken token)
    {
        return Task.Run(() =>
        {
            var decodedFrames = uiDispatcher.Invoke(() => DecodeGifFrames(source.FilePath));
            if (decodedFrames.Count == 0)
                throw new InvalidOperationException("Could not decode source GIF frames.");

            int width = decodedFrames[0].Frame.Width;
            int height = decodedFrames[0].Frame.Height;

            long cachedOverlayKey = 0;
            byte[]? cachedOverlay = null;
            int written = 0;
            double keptSeconds = 0;

            using (var stream = File.Create(outputPath))
            using (var encoder = new GifEncoder(stream, width, height, leaveOpen: false))
            {
                double t = 0;
                for (int i = 0; i < decodedFrames.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var (frame, delayMs) = decodedFrames[i];
                    double delaySec = Math.Max(0.02, delayMs / 1000.0);

                    if (t + delaySec >= track.TrimStartSeconds && t <= track.TrimEndSeconds)
                    {
                        var copy = frame.Clone();
                        ApplyAnnotations(copy, track, t, uiDispatcher, ref cachedOverlayKey, ref cachedOverlay);
                        encoder.AddFrame(copy, delayMs);
                        written++;
                        keptSeconds += delaySec;
                    }

                    t += delaySec;
                    progress?.Report(Math.Clamp((i + 1) / (double)decodedFrames.Count, 0, 0.98));
                }

                if (written == 0)
                {
                    var fallback = decodedFrames[0].Frame.Clone();
                    ApplyAnnotations(fallback, track, track.TrimStartSeconds, uiDispatcher, ref cachedOverlayKey, ref cachedOverlay);
                    encoder.AddFrame(fallback, decodedFrames[0].DelayMs);
                    written = 1;
                    keptSeconds = decodedFrames[0].DelayMs / 1000.0;
                }

                encoder.Finish();
            }

            progress?.Report(1.0);
            long bytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            return new RecordingResult(
                outputPath,
                RecordingFormat.Gif,
                width,
                height,
                TimeSpan.FromSeconds(Math.Max(0.1, keptSeconds)),
                written,
                bytes);
        }, token);
    }

    public static List<(PixelBuffer Frame, int DelayMs)> DecodeGifFrames(string path)
    {
        var result = new List<(PixelBuffer, int)>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        foreach (var frame in decoder.Frames)
        {
            int delayMs = 80;
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctlext/Delay"))
            {
                if (meta.GetQuery("/grctlext/Delay") is ushort cs && cs > 0)
                    delayMs = Math.Clamp(cs * 10, 20, 5000);
            }

            BitmapSource bgra = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth;
            int h = bgra.PixelHeight;
            var pb = new PixelBuffer(w, h);
            bgra.CopyPixels(pb.Pixels, pb.Stride, 0);
            result.Add((pb, delayMs));
        }

        return result;
    }

    private static void ApplyAnnotations(
        PixelBuffer frame,
        VideoAnnotationTrack track,
        double timeSec,
        Dispatcher uiDispatcher,
        ref long cachedKey,
        ref byte[]? cachedOverlay)
    {
        var active = track.ActiveAt(timeSec);
        if (active.Count == 0)
            return;

        // 1. Pixelate any active Blur regions directly on the video frame.
        foreach (var (ann, op) in active)
        {
            if (ann.Kind != AnnotationKind.Blur || ann.Points.Count < 2 || op <= 0.1)
                continue;
            var (x, y, bw, bh) = ShapeMath.Bounds(ann.Points);
            int ix = (int)Math.Round(x);
            int iy = (int)Math.Round(y);
            int iw = Math.Max(1, (int)Math.Round(bw));
            int ih = Math.Max(1, (int)Math.Round(bh));
            int block = Math.Clamp((int)Math.Round(ann.Stroke * 3), 6, 36);
            frame.Pixelate(ix, iy, iw, ih, block);
        }

        // 2. Render & cache vector overlay layer (arrows, boxes, text, callouts, steps, redact, spotlight).
        long key = track.OverlayCacheKeyAt(timeSec);
        if (key == 0)
            return;

        if (key != cachedKey || cachedOverlay == null || cachedOverlay.Length != frame.Pixels.Length)
        {
            int w = frame.Width;
            int h = frame.Height;
            cachedOverlay = uiDispatcher.Invoke(() => RenderOverlayLayer(w, h, active));
            cachedKey = key;
        }

        BlendPremultipliedOver(frame.Pixels, cachedOverlay);
    }

    private static byte[] RenderOverlayLayer(int width, int height, IReadOnlyList<(Annotation Annotation, double Opacity)> active)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            (Annotation Spot, double Opacity)? activeSpotlight = null;

            foreach (var (annotation, opacity) in active)
            {
                if (annotation.Kind == AnnotationKind.Blur)
                    continue;
                if (annotation.Kind == AnnotationKind.Spotlight)
                {
                    if (annotation.Points.Count >= 2)
                        activeSpotlight = (annotation, opacity);
                    continue;
                }
                dc.PushOpacity(opacity);
                AnnotationPainter.Draw(dc, annotation);
                dc.Pop();
            }

            if (activeSpotlight is { } spot)
            {
                var hole = AnnotationPainter.Box(spot.Spot);
                if (hole.Width >= 2 && hole.Height >= 2)
                {
                    var view = new Rect(0, 0, width, height);
                    var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(view), new RectangleGeometry(hole));
                    dc.PushOpacity(spot.Opacity);
                    dc.DrawGeometry(Palette.Brush(spot.Spot.ColorHex, 0x8C), null, dim);
                    dc.Pop();
                }
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[width * height * 4];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    internal static void BlendPremultipliedOver(byte[] dest, byte[] src)
    {
        int length = Math.Min(dest.Length, src.Length);
        for (int i = 0; i < length; i += 4)
        {
            int alpha = src[i + 3];
            if (alpha == 0)
                continue;
            if (alpha == 255)
            {
                dest[i] = src[i];
                dest[i + 1] = src[i + 1];
                dest[i + 2] = src[i + 2];
                dest[i + 3] = 255;
                continue;
            }
            int keep = 255 - alpha;
            dest[i] = (byte)Math.Min(255, src[i] + (dest[i] * keep + 127) / 255);
            dest[i + 1] = (byte)Math.Min(255, src[i + 1] + (dest[i + 1] * keep + 127) / 255);
            dest[i + 2] = (byte)Math.Min(255, src[i + 2] + (dest[i + 2] * keep + 127) / 255);
            dest[i + 3] = 255;
        }
    }
}
