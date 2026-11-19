using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapKit;

internal interface IScrollStatus
{
    void Report(string text);
    void Suspend();
    void Resume();
}

internal static class ScrollCapture
{
    public const int MaxHeight = 16000;

    public static async Task<PixelBuffer?> RunAsync(IntPtr hwnd, IScrollStatus status, CancellationToken token)
    {
        NativeMethods.GetCursorPos(out var saved);
        PixelBuffer? result = null;
        try
        {
            NativeMethods.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.keybd_event(0x12, 0, NativeMethods.KeyEventKeyUp, UIntPtr.Zero);
            await Task.Delay(140, token);

            var bounds = WindowFinder.GetBounds(hwnd);
            if (bounds.Width < 40 || bounds.Height < 40)
                return null;

            int notches = 3;
            int ignore = Math.Max(28, bounds.Width / 18);
            MoveCursor(bounds, null);
            await Task.Delay(90, token);

            var first = await Grab(hwnd, bounds, status, token);
            result = first;
            status.Report("Scrolling…");
            await Wheel(hwnd, bounds, notches, token);
            bounds = WindowFinder.GetBounds(hwnd);
            var second = await Grab(hwnd, bounds, status, token);
            var band = ImageStitcher.DetectChangedBand(first, second, ignore);
            var match = band.IsEmpty
                ? new ImageStitcher.ScrollMatch(false, 0, 0)
                : ImageStitcher.FindScroll(first, second, band, ignore);
            if (!match.Success)
            {
                await Task.Delay(200, token);
                second = await Grab(hwnd, bounds, status, token);
                band = ImageStitcher.DetectChangedBand(first, second, ignore);
                if (!band.IsEmpty)
                    match = ImageStitcher.FindScroll(first, second, band, ignore);
            }
            if (band.IsEmpty || !match.Success)
                return first;

            PixelBuffer? header = band.Top > 0 ? first.Crop(0, 0, first.Width, band.Top) : null;
            int footerHeight = first.Height - band.Bottom;
            PixelBuffer? footer = footerHeight > 0 ? first.Crop(0, band.Bottom, first.Width, footerHeight) : null;
            var content = first.Crop(0, band.Top, first.Width, band.Height);
            content = ImageStitcher.AppendNewRows(content, second, band, match.Delta);
            result = ImageStitcher.Compose(header, content, footer);
            var previous = second;
            int stalls = 0;
            MoveCursor(bounds, band);

            while (!token.IsCancellationRequested && result.Height < MaxHeight)
            {
                if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkEscape) & 0x8000) != 0)
                    break;
                if (match.Delta < band.Height * 0.16)
                    notches = Math.Min(6, notches + 1);
                else if (match.Delta > band.Height * 0.5)
                    notches = Math.Max(1, notches - 1);

                await Wheel(hwnd, bounds, notches, token);
                var now = WindowFinder.GetBounds(hwnd);
                if (now.Width != bounds.Width || now.Height != bounds.Height)
                    break;
                bounds = now;
                var frame = await Grab(hwnd, bounds, status, token);
                var next = ImageStitcher.FindScroll(previous, frame, band, ignore);
                if (!next.Success || next.Delta < 2)
                {
                    stalls++;
                    if (stalls >= 2)
                        break;
                    continue;
                }
                stalls = 0;
                match = next;
                int room = MaxHeight - result.Height;
                int delta = Math.Min(next.Delta, Math.Max(0, room));
                if (delta < 2)
                    break;
                content = ImageStitcher.AppendNewRows(content, frame, band, delta);
                previous = frame;
                result = ImageStitcher.Compose(header, content, footer);
                status.Report($"Scrolling…  {result.Height:n0} px    ·    Esc stops");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return result;
        }
        finally
        {
            NativeMethods.SetCursorPos(saved.X, saved.Y);
        }
    }

    private static async Task<PixelBuffer> Grab(IntPtr hwnd, NativeMethods.Rect bounds, IScrollStatus status, CancellationToken token)
    {
        status.Suspend();
        await Task.Delay(40, token);
        var frame = ScreenGrabber.CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        status.Resume();
        return frame;
    }

    private static async Task Wheel(IntPtr hwnd, NativeMethods.Rect bounds, int notches, CancellationToken token)
    {
        int delta = -120 * Math.Clamp(notches, 1, 8);
        var input = new NativeMethods.Input
        {
            Type = 0,
            Mouse = new NativeMethods.MouseInput
            {
                Flags = NativeMethods.MouseEventWheel,
                MouseData = delta
            }
        };
        NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
        IntPtr wParam = (IntPtr)((delta & 0xFFFF) << 16);
        var point = new NativeMethods.Point
        {
            X = bounds.Left + bounds.Width / 2,
            Y = bounds.Top + (int)(bounds.Height * 0.62)
        };
        // Chrome and Edge scroll a child window, not the top-level frame.
        IntPtr target = NativeMethods.WindowFromPoint(point);
        if (target == IntPtr.Zero)
            target = hwnd;
        NativeMethods.ScreenToClient(target, ref point);
        IntPtr lParam = (IntPtr)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
        NativeMethods.PostMessage(target, NativeMethods.WmMouseWheel, wParam, lParam);
        await Task.Delay(300, token);
    }

    private static void MoveCursor(NativeMethods.Rect bounds, ImageStitcher.ChangeBand? band)
    {
        int x = bounds.Left + bounds.Width / 2;
        int y = band.HasValue
            ? bounds.Top + band.Value.Top + Math.Max(8, band.Value.Height / 2)
            : bounds.Top + (int)(bounds.Height * 0.62);
        NativeMethods.SetCursorPos(x, y);
    }
}

internal sealed class ScrollHud : Window, IScrollStatus
{
    private readonly TextBlock _text;
    private readonly CancellationTokenSource _cancel = new();

    public ScrollHud()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _text = new TextBlock
        {
            Text = "Scrolling…    Esc stops",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13
        };
        Content = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            Child = _text
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            _cancel.Cancel();
            e.Handled = true;
        };
        Closed += (_, _) =>
        {
            if (!_cancel.IsCancellationRequested)
                _cancel.Cancel();
        };
    }

    public CancellationToken Token => _cancel.Token;

    public void Report(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(text));
            return;
        }
        _text.Text = text;
    }

    public void Suspend()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Suspend);
            return;
        }
        Hide();
    }

    public void Resume()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Resume);
            return;
        }
        if (!_cancel.IsCancellationRequested)
            Show();
    }
}
