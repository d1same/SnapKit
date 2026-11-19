using System.Windows;
using System.Windows.Input;

namespace SnapKit;

internal static class OcrFlow
{
    private static int _busy;

    public static void Start(PixelBuffer image, Action<string>? status, Window? owner)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            status?.Invoke("OCR is already running.");
            return;
        }

        PixelBuffer snapshot;
        try
        {
            snapshot = image.Clone();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _busy, 0);
            status?.Invoke(ex.Message);
            return;
        }

        Unstick(owner);
        status?.Invoke("Reading text…");
        _ = Finish(snapshot, status, owner);
    }

    private static async Task Finish(PixelBuffer snapshot, Action<string>? status, Window? owner)
    {
        try
        {
            var work = OcrClient.RecognizeAsync(snapshot);
            var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(25)));
            if (finished != work)
            {
                _ = work.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                throw new TimeoutException("OCR took too long. You can keep editing.");
            }
            var text = await work;
            Present(owner, text, null, status);
        }
        catch (Exception ex)
        {
            Present(owner, "", ex.Message, status);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            Unstick(owner);
        }
    }

    private static void Present(Window? owner, string text, string? error, Action<string>? status)
    {
        void Show()
        {
            Unstick(owner);
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { Clipboard.SetText(text); }
                catch { /* Another app has the clipboard. The panel still shows the text. */ }
            }

            status?.Invoke(error ?? (string.IsNullOrWhiteSpace(text) ? "No text found" : "Text copied"));
            bool restoreTopmost = owner is { Topmost: true };
            if (restoreTopmost && owner != null)
                owner.Topmost = false;
            try
            {
                var window = new OcrWindow(text, error);
                if (restoreTopmost && owner != null)
                    window.Closed += (_, _) => owner.Topmost = true;
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                if (restoreTopmost && owner != null)
                    owner.Topmost = true;
                status?.Invoke(ex.Message);
            }
        }

        var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            Show();
        else
            dispatcher.BeginInvoke(Show);
    }

    private static void Unstick(Window? owner)
    {
        try
        {
            Mouse.Capture(null);
            if (owner != null)
                owner.IsEnabled = true;
        }
        catch
        {
            // Input is already free.
        }
    }
}
