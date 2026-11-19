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
            // Restore only a minimized window. SW_RESTORE on a maximized window
            // shrinks it back to the restored size and the stitch grabs that.
            NativeMethods.keybd_event(NativeMethods.VkMenu, 0, 0, UIntPtr.Zero);
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.keybd_event(NativeMethods.VkMenu, 0, NativeMethods.KeyEventKeyUp, UIntPtr.Zero);

            var bounds = await WaitForStableBounds(hwnd, token);
            if (bounds.Width < 40 || bounds.Height < 40)
                return null;

            int notches = 2;
            int ignore = Math.Max(28, bounds.Width / 18);
            bool posted = false;
            MoveCursor(bounds, null);
            await Task.Delay(90, token);

            var first = await SettledGrab(hwnd, bounds, status, token);
            result = first;
            status.Report("Scrolling…");
            PixelBuffer second = first;
            var band = ImageStitcher.ChangeBand.Empty;
            var match = new ImageStitcher.ScrollMatch(false, 0, 0);
            for (var attempt = 0; attempt < 3 && !match.Success; attempt++)
            {
                if (attempt == 1)
                    posted = true;
                await Wheel(hwnd, bounds, attempt == 0 ? notches : 1, posted, token);
                bounds = WindowFinder.GetBounds(hwnd);
                second = await SettledGrab(hwnd, bounds, status, token);
                if (second.Width != first.Width || second.Height != first.Height)
                    return first;
                if (MeanDelta(first, second) < 2.5)
                    continue;
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

                await Wheel(hwnd, bounds, notches, posted, token);
                var now = WindowFinder.GetBounds(hwnd);
                if (Math.Abs(now.Width - bounds.Width) > 4 || Math.Abs(now.Height - bounds.Height) > 4)
                    break;
                bounds = now;
                var frame = await SettledGrab(hwnd, bounds, status, token);
                if (frame.Width != previous.Width || frame.Height != previous.Height)
                    break;
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

    private static async Task<PixelBuffer> SettledGrab(IntPtr hwnd, NativeMethods.Rect bounds, IScrollStatus status, CancellationToken token)
    {
        status.Suspend();
        try
        {
            await Task.Delay(110, token);
            var frame = ScreenGrabber.CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            for (var i = 0; i < 7; i++)
            {
                await Task.Delay(80, token);
                var next = ScreenGrabber.CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
                if (next.Width != frame.Width || next.Height != frame.Height)
                    return next;
                if (MeanDelta(frame, next) < 2.2)
                    return next;
                frame = next;
            }
            return frame;
        }
        finally
        {
            status.Resume();
        }
    }

    private static double MeanDelta(PixelBuffer left, PixelBuffer right)
    {
        if (left.Width != right.Width || left.Height != right.Height || left.Width < 8 || left.Height < 8)
            return 999;
        int stepX = Math.Max(8, left.Width / 48);
        int stepY = Math.Max(8, left.Height / 36);
        long sum = 0;
        int count = 0;
        int x1 = left.Width - stepX;
        int y1 = left.Height - stepY;
        for (int y = stepY; y < y1; y += stepY)
        {
            int rowL = y * left.Stride;
            int rowR = y * right.Stride;
            for (int x = stepX; x < x1; x += stepX)
            {
                int i = rowL + x * 4;
                int j = rowR + x * 4;
                sum += Math.Abs(left.Pixels[i] - right.Pixels[j]);
                sum += Math.Abs(left.Pixels[i + 1] - right.Pixels[j + 1]);
                sum += Math.Abs(left.Pixels[i + 2] - right.Pixels[j + 2]);
                count += 3;
            }
        }
        return count == 0 ? 999 : sum / (double)count;
    }

    private static async Task Wheel(IntPtr hwnd, NativeMethods.Rect bounds, int notches, bool posted, CancellationToken token)
    {
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkMenu) & 0x8000) != 0)
        {
            NativeMethods.keybd_event(NativeMethods.VkMenu, 0, NativeMethods.KeyEventKeyUp, UIntPtr.Zero);
            await Task.Delay(40, token);
        }
        NativeMethods.SetForegroundWindow(hwnd);
        int delta = -120 * Math.Clamp(notches, 1, 8);
        int x = bounds.Left + bounds.Width / 2;
        int y = bounds.Top + Math.Clamp((int)(bounds.Height * 0.55), 80, Math.Max(80, bounds.Height - 40));
        NativeMethods.SetCursorPos(x, y);
        if (!posted)
        {
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
        }
        else
        {
            // WM_MOUSEWHEEL carries screen coordinates, not client coordinates.
            var point = new NativeMethods.Point { X = x, Y = y };
            IntPtr target = NativeMethods.WindowFromPoint(point);
            if (target == IntPtr.Zero)
                target = hwnd;
            IntPtr wParam = (IntPtr)((delta & 0xFFFF) << 16);
            IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            NativeMethods.PostMessage(target, NativeMethods.WmMouseWheel, wParam, lParam);
        }
        await Task.Delay(80, token);
    }

    private static async Task<NativeMethods.Rect> WaitForStableBounds(IntPtr hwnd, CancellationToken token)
    {
        var bounds = WindowFinder.GetBounds(hwnd);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(70, token);
            var next = WindowFinder.GetBounds(hwnd);
            if (next.Width == bounds.Width && next.Height == bounds.Height && next.Left == bounds.Left && next.Top == bounds.Top)
                return next;
            bounds = next;
        }
        return bounds;
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
        ShowActivated = false;
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
