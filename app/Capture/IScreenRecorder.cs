namespace SnapKit;

/// <summary>
/// v2 recording plugs in here. The intended stack is Windows.Graphics.Capture for frames
/// and FFmpeg for GIF or MP4. v1 keeps this unused so the tray, hotkey table, and settings
/// file can grow a Record action without a new shell.
/// </summary>
public interface IScreenRecorder
{
    bool IsRecording { get; }
    void Start(IntPtr monitorHandle);
    Task StopAsync();
}
