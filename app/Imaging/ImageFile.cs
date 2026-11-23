using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapKit;

internal static class ImageFile
{
    public static bool Copy(BitmapSource image)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;

        try
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            byte[] pngBytes;
            using (var png = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(png);
                pngBytes = png.ToArray();
            }

            byte[] dib = DibImage.FromTopDownBgra(pixels, width, height, stride);
            return SetClipboard(dib, pngBytes);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static int _busy;

    /// <summary>
    /// Another program (Remote Desktop, Office, a clipboard manager) can hold the clipboard
    /// for a moment. OpenClipboard then returns CLIPBRD_E_CANT_OPEN. Wait and pump so that
    /// program can let go, then try again. A hard sleep on this thread keeps the lock.
    /// </summary>
    private static bool SetClipboard(byte[] dib, byte[] png)
    {
        uint pngFormat = RegisterClipboardFormat("PNG");
        IntPtr dibMem = Alloc(dib);
        IntPtr pngMem = pngFormat == 0 ? IntPtr.Zero : Alloc(png);
        if (dibMem == IntPtr.Zero)
        {
            Free(pngMem);
            return false;
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        if (!EmptyClipboard())
                            continue;
                        if (SetClipboardData(CfDib, dibMem) == IntPtr.Zero)
                            continue;
                        dibMem = IntPtr.Zero;
                        if (pngMem != IntPtr.Zero && SetClipboardData(pngFormat, pngMem) != IntPtr.Zero)
                            pngMem = IntPtr.Zero;
                        return true;
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }

                if (attempt < 14)
                    Pump(dispatcher, 100);
            }
        }
        finally
        {
            Free(dibMem);
            Free(pngMem);
        }

        return false;
    }

    private static IntPtr Alloc(byte[] bytes)
    {
        IntPtr handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr locked = GlobalLock(handle);
        if (locked == IntPtr.Zero)
        {
            GlobalFree(handle);
            return IntPtr.Zero;
        }

        Marshal.Copy(bytes, 0, locked, bytes.Length);
        GlobalUnlock(handle);
        return handle;
    }

    private static void Free(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            GlobalFree(handle);
    }

    private static void Pump(Dispatcher dispatcher, int milliseconds)
    {
        if (dispatcher.HasShutdownStarted)
        {
            Thread.Sleep(milliseconds);
            return;
        }

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private const uint CfDib = 8;
    private const uint GmemMoveable = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    public static bool Save(BitmapSource image, Window owner)
    {
        try { System.Windows.Input.Mouse.Capture(null); } catch { /* ignore */ }
        using var pauseClickAway = ClickAway.Pause(owner);
        using var suspendTopmost = WindowChrome.SuspendTopmost(owner);
        bool saved = false;
        try
        {
            string initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save capture",
                Filter = "PNG image|*.png|JPEG image|*.jpg",
                FileName = $"{Brand.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                InitialDirectory = Directory.Exists(initialDir) ? initialDir : "",
                AddExtension = true,
                OverwritePrompt = true
            };

            // Passing a WS_EX_LAYERED transparent borderless owner can bury the Win32 IFileSaveDialog
            // behind other windows or fullscreen overlays; show it top-level for transparent windows.
            bool? result = owner.AllowsTransparency
                ? dialog.ShowDialog()
                : dialog.ShowDialog(owner);
            if (result != true)
                return false;

            using var stream = File.Create(dialog.FileName);
            BitmapEncoder encoder = dialog.FilterIndex == 2
                ? new JpegBitmapEncoder { QualityLevel = 92 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
            saved = true;
            return true;
        }
        finally
        {
            if (!saved && owner.IsVisible)
            {
                owner.Dispatcher.BeginInvoke(() =>
                {
                    try { owner.Activate(); } catch { /* closed */ }
                }, DispatcherPriority.Input);
            }
        }
    }

    public static string DefaultQuickSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Brand.Name);

    public static string ResolveQuickSaveFolder(string? customFolder) =>
        string.IsNullOrWhiteSpace(customFolder) ? DefaultQuickSaveFolder : customFolder.Trim();

    public static string QuickSave(BitmapSource image, string? customFolder = null)
    {
        string folder = ResolveQuickSaveFolder(customFolder);
        Directory.CreateDirectory(folder);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(folder, $"{Brand.Name}-{stamp}.png");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{Brand.Name}-{stamp}-{n}.png");

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return path;
    }

    public static bool CopyFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var dispatcher = Dispatcher.CurrentDispatcher;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var files = new System.Collections.Specialized.StringCollection { Path.GetFullPath(filePath) };
                Clipboard.SetFileDropList(files);
                return true;
            }
            catch
            {
                if (attempt < 9)
                    Pump(dispatcher, 80);
            }
        }
        return false;
    }

    public static string QuickSaveFile(string sourcePath, RecordingFormat format, string? customFolder = null)
    {
        string folder = ResolveQuickSaveFolder(customFolder);
        Directory.CreateDirectory(folder);
        string ext = format == RecordingFormat.Gif ? ".gif" : ".mp4";
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string path = Path.Combine(folder, $"{Brand.Name}-{stamp}{ext}");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{Brand.Name}-{stamp}-{n}{ext}");

        File.Copy(sourcePath, path, overwrite: false);
        return path;
    }

    public static string? SaveMedia(string sourcePath, RecordingFormat format, Window owner)
    {
        try { System.Windows.Input.Mouse.Capture(null); } catch { /* ignore */ }
        using var pauseClickAway = ClickAway.Pause(owner);
        using var suspendTopmost = WindowChrome.SuspendTopmost(owner);

        string ext = format == RecordingFormat.Gif ? ".gif" : ".mp4";
        string filter = format == RecordingFormat.Gif
            ? "Animated GIF|*.gif"
            : "MP4 Video|*.mp4";
        string initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!Directory.Exists(initialDir))
            initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = format == RecordingFormat.Gif ? "Save GIF recording" : "Save MP4 recording",
            Filter = filter,
            FileName = $"{Brand.Name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}",
            InitialDirectory = Directory.Exists(initialDir) ? initialDir : "",
            AddExtension = true,
            OverwritePrompt = true
        };

        bool? result = owner.AllowsTransparency
            ? dialog.ShowDialog()
            : dialog.ShowDialog(owner);
        if (result != true)
            return null;

        File.Copy(sourcePath, dialog.FileName, overwrite: true);
        return dialog.FileName;
    }
}
