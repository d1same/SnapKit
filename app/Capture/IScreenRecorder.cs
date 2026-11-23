namespace SnapKit;

public enum RecordingFormat
{
    Mp4,
    Gif
}

public sealed record RecordingOptions(
    int ScreenX,
    int ScreenY,
    int Width,
    int Height,
    double DisplayScale,
    RecordingFormat Format,
    int Fps,
    bool ShowCursor,
    bool RecordMic,
    bool RecordSystemAudio);

public sealed record RecordingResult(
    string FilePath,
    RecordingFormat Format,
    int Width,
    int Height,
    TimeSpan Duration,
    int Frames,
    long FileSizeBytes,
    string? Warning = null);

public interface IScreenRecorder : IDisposable
{
    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }
    int FramesCaptured { get; }
    void Start(RecordingOptions options, string outputPath);
    void SetPaused(bool paused);
    Task<RecordingResult> StopAsync();
}
