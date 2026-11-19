using System.Windows;
using System.Windows.Input;

namespace SnapKit;

internal static class OcrFlow
{
    private static int _busy;

    public static void Start(PixelBuffer image, Action<string>? status, Window? owner, Func<bool>? closeOnClickAway = null)
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
        _ = Finish(snapshot, status, owner, closeOnClickAway);
    }

    private static async Task Finish(PixelBuffer snapshot, Action<string>? status, Window? owner, Func<bool>? closeOnClickAway)
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
            var page = await work;
            Present(owner, snapshot, page, null, status, closeOnClickAway);
        }
        catch (Exception ex)
        {
            Present(owner, snapshot, null, ex.Message, status, closeOnClickAway);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            Unstick(owner);
        }
    }

    private static void Present(Window? owner, PixelBuffer image, OcrPage? page, string? error, Action<string>? status, Func<bool>? closeOnClickAway)
    {
        void Show()
        {
            Unstick(owner);
            status?.Invoke(error ?? (page == null || page.Words.Count == 0 ? "No text found" : "Drag across the words you want."));
            try
            {
                Window window = error != null || page == null
                    ? new OcrWindow("", error ?? "OCR failed.")
                    : new OcrSelectWindow(image, page);
                ClickAway.Attach(window, window.Close, closeOnClickAway ?? (() => false));
                var pause = owner == null ? null : ClickAway.Pause(owner);
                if (pause != null)
                    window.Closed += (_, _) => pause.Dispose();
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
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
