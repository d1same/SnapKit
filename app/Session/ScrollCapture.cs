using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal interface IScrollStatus
{
    void Report(string text);
    void Suspend();
    void Resume();
}

internal readonly record struct ScrollCaptureResult(PixelBuffer? Image, bool Scrolled, int FramesStitched);

internal static class ScrollCapture
{
    public const int MaxHeight = 20000;

    public static async Task<ScrollCaptureResult> RunAsync(
        IntPtr hwnd,
        int? clickX,
        int? clickY,
        IScrollStatus status,
        CancellationToken token)
    {
        NativeMethods.GetCursorPos(out var savedCursor);
        PixelBuffer? first = null;
        PixelBuffer? header = null;
        PixelBuffer? content = null;
        PixelBuffer? previous = null;
        var band = ImageStitcher.ChangeBand.Empty;
        int framesStitched = 1;
        bool scrolled = false;

        try
        {
            // Give DWM a moment to finish removing the closed OverlayWindow before grabbing frame 0.
            await DelayCheckingEsc(160, token);

            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
            FocusTargetIfNeeded(hwnd);

            var bounds = ClampToVirtualScreen(await WaitForStableBounds(hwnd, token));
            if (bounds.Width < 40 || bounds.Height < 40)
                return new ScrollCaptureResult(null, false, 0);

            int ignore = Math.Clamp(bounds.Width / 28, 22, 48);
            var (scrollX, scrollY) = ChooseInitialScrollPoint(bounds, clickX, clickY, ignore);

            NativeMethods.SetCursorPos(scrollX, scrollY);
            await DelayCheckingEsc(90, token);

            first = await SettledGrab(bounds, ignore, token);
            previous = first;
            status.Report("Scrolling…");

            // Calibration phase: try SendInput wheel first (works across Chromium/Edge/Firefox/Electron/UWP/Win32),
            // adjusting notches or falling back to PostMessage / window center only if no movement occurs.
            var anchor = first;
            PixelBuffer second = first;
            var match = new ImageStitcher.ScrollMatch(false, 0, 0);
            int notches = 3;
            bool usePosted = false;

            for (int attempt = 0; attempt < 5 && !match.Success; attempt++)
            {
                ThrowIfStopped(token);

                int sendNotches = notches;
                if (attempt == 1 && !scrolled)
                {
                    sendNotches = 5;
                }
                else if (attempt == 2 && !scrolled)
                {
                    // Legacy Win32 controls sometimes expect WM_MOUSEWHEEL posted directly to child HWND.
                    usePosted = true;
                    sendNotches = 4;
                }
                else if (attempt == 3 && !scrolled)
                {
                    // User may have clicked a non-scrollable header/sidebar; retry at window center with SendInput.
                    usePosted = false;
                    scrollX = bounds.Left + bounds.Width / 2;
                    scrollY = bounds.Top + Math.Clamp((int)(bounds.Height * 0.58), 80, Math.Max(80, bounds.Height - 40));
                    sendNotches = 4;
                }

                await SendWheelAsync(hwnd, scrollX, scrollY, sendNotches, usePosted, token);
                bounds = ClampToVirtualScreen(WindowFinder.GetBounds(hwnd));
                second = await SettledGrab(bounds, ignore, token);
                if (second.Width != anchor.Width || second.Height != anchor.Height)
                    return new ScrollCaptureResult(first, false, 1);

                double changedRatio = await Task.Run(() => ImageStitcher.ChangedPixelRatio(anchor, second, null, ignore), token);
                if (changedRatio < 0.0015)
                    continue;

                var detected = await Task.Run(() =>
                {
                    var b = ImageStitcher.DetectChangedBand(anchor, second, ignore);
                    var m = b.IsEmpty
                        ? new ImageStitcher.ScrollMatch(false, 0, double.MaxValue)
                        : ImageStitcher.FindScroll(anchor, second, b, ignore);
                    return (b, m);
                }, token);

                band = detected.b;
                match = detected.m;

                if (!match.Success)
                {
                    // The window moved, possibly overshooting > 1 full viewport; reduce notches and retry from new anchor.
                    anchor = second;
                    first = second;
                    previous = second;
                    notches = Math.Max(1, sendNotches / 2);
                }
                else
                {
                    notches = sendNotches;
                }
            }

            if (band.IsEmpty || !match.Success)
                return new ScrollCaptureResult(first, false, 1);

            first = anchor;
            header = band.Top > 0 ? first.Crop(0, 0, first.Width, band.Top) : null;
            content = first.Crop(0, band.Top, first.Width, band.Height);
            content = await Task.Run(() => ImageStitcher.AppendNewRows(content, second, band, match.Delta), token);
            previous = second;
            scrolled = true;
            framesStitched = 2;

            // Keep cursor inside the active scrollable region horizontally and vertically.
            scrollX = ClampScrollX(bounds, band, scrollX, ignore);
            scrollY = ClampScrollY(bounds, band, scrollY);
            NativeMethods.SetCursorPos(scrollX, scrollY);

            int currentHeight = EstimateHeight(header, content, first.Height - band.Bottom);
            status.Report($"Scrolling…  {currentHeight:n0} px");

            int stalls = 0;
            int footerHeight = first.Height - band.Bottom;

            while (!token.IsCancellationRequested && currentHeight < MaxHeight)
            {
                if (IsEscapeDown())
                    break;

                // Tune wheel notches to advance ~35%-55% of viewport per step (fast + generous overlap).
                if (match.Delta < band.Height * 0.22)
                    notches = Math.Min(6, notches + 1);
                else if (match.Delta > band.Height * 0.58)
                    notches = Math.Max(1, notches - 1);

                await SendWheelAsync(hwnd, scrollX, scrollY, notches, usePosted, token);

                var nowBounds = ClampToVirtualScreen(WindowFinder.GetBounds(hwnd));
                if (Math.Abs(nowBounds.Width - bounds.Width) > 4 || Math.Abs(nowBounds.Height - bounds.Height) > 4)
                    break;
                bounds = nowBounds;

                var frame = await SettledGrab(bounds, ignore, token);
                if (frame.Width != previous.Width || frame.Height != previous.Height)
                    break;

                double changed = await Task.Run(() => ImageStitcher.ChangedPixelRatio(previous, frame, band, ignore), token);
                if (changed < 0.0012)
                {
                    // Likely reached bottom of scroll area; allow 1 retry in case of lazy-loaded content.
                    stalls++;
                    if (stalls >= 2)
                        break;
                    continue;
                }

                var nextMatch = await Task.Run(() => ImageStitcher.FindScroll(previous, frame, band, ignore), token);
                if (!nextMatch.Success || nextMatch.Delta < 2)
                {
                    // Smooth scroll or lazy images may still be settling; wait briefly and re-grab once.
                    await DelayCheckingEsc(180, token);
                    frame = await SettledGrab(bounds, ignore, token);
                    if (frame.Width != previous.Width || frame.Height != previous.Height)
                        break;
                    nextMatch = await Task.Run(() => ImageStitcher.FindScroll(previous, frame, band, ignore), token);
                }

                if (!nextMatch.Success || nextMatch.Delta < 2)
                {
                    stalls++;
                    if (notches > 1)
                        notches = Math.Max(1, notches - 1);
                    if (stalls >= 2)
                        break;
                    continue;
                }

                stalls = 0;
                match = nextMatch;

                int room = MaxHeight - currentHeight;
                int delta = Math.Min(nextMatch.Delta, Math.Max(0, room));
                if (delta < 2)
                    break;

                var prevContent = content;
                content = await Task.Run(() => ImageStitcher.AppendNewRows(prevContent, frame, band, delta), token);
                previous = frame;
                framesStitched++;
                currentHeight = EstimateHeight(header, content, footerHeight);
                status.Report($"Scrolling…  {currentHeight:n0} px");
            }

            var finalImage = await Task.Run(() => BuildFinal(first, header, content, previous, band), token);
            return new ScrollCaptureResult(finalImage, scrolled, framesStitched);
        }
        catch (OperationCanceledException)
        {
            if (scrolled && first != null && content != null && previous != null)
            {
                var partial = BuildFinal(first, header, content, previous, band);
                return new ScrollCaptureResult(partial, true, framesStitched);
            }
            return new ScrollCaptureResult(first, false, first != null ? 1 : 0);
        }
        finally
        {
            NativeMethods.SetCursorPos(savedCursor.X, savedCursor.Y);
        }
    }

    private static PixelBuffer BuildFinal(
        PixelBuffer first,
        PixelBuffer? header,
        PixelBuffer content,
        PixelBuffer lastFrame,
        ImageStitcher.ChangeBand band)
    {
        int footerHeight = first.Height - band.Bottom;
        // Take footer from the final frame so end-of-page content that scrolled into originally-blank
        // bottom rows is preserved cleanly, while fixed footers stay identical.
        PixelBuffer? footer = footerHeight > 0
            ? lastFrame.Crop(0, band.Bottom, lastFrame.Width, footerHeight)
            : null;
        return ImageStitcher.Compose(header, content, footer);
    }

    private static int EstimateHeight(PixelBuffer? header, PixelBuffer content, int footerHeight) =>
        (header?.Height ?? 0) + content.Height + Math.Max(0, footerHeight);

    private static async Task<PixelBuffer> SettledGrab(NativeMethods.Rect bounds, int ignoreRightPx, CancellationToken token)
    {
        // Wait long enough for smooth-scroll animations (Chrome/Edge/Firefox/Electron) to get underway.
        await DelayCheckingEsc(160, token);
        var frame = ScreenGrabber.CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

        for (int i = 0; i < 9; i++)
        {
            await DelayCheckingEsc(70, token);
            var next = ScreenGrabber.CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (next.Width != frame.Width || next.Height != frame.Height)
                return next;

            double changed = await Task.Run(() => ImageStitcher.ChangedPixelRatio(frame, next, null, ignoreRightPx), token);
            if (changed < 0.0015)
                return next;
            frame = next;
        }
        return frame;
    }

    private static async Task SendWheelAsync(IntPtr hwnd, int x, int y, int notches, bool posted, CancellationToken token)
    {
        FocusTargetIfNeeded(hwnd);
        NativeMethods.SetCursorPos(x, y);
        int count = Math.Clamp(notches, 1, 8);

        if (!posted)
        {
            int size = Marshal.SizeOf<NativeMethods.Input>();
            var inputs = new[]
            {
                new NativeMethods.Input
                {
                    Type = 0,
                    Mouse = new NativeMethods.MouseInput
                    {
                        Flags = NativeMethods.MouseEventWheel,
                        MouseData = -120
                    }
                }
            };
            // Send discrete -120 notches spaced slightly apart so both smooth-scroll browsers
            // and line-scroll Win32 controls advance predictably without clamping.
            for (int i = 0; i < count; i++)
            {
                ThrowIfStopped(token);
                NativeMethods.SendInput(1, inputs, size);
                if (i + 1 < count)
                    await DelayCheckingEsc(16, token);
            }
        }
        else
        {
            var point = new NativeMethods.Point { X = x, Y = y };
            IntPtr target = NativeMethods.WindowFromPoint(point);
            if (target == IntPtr.Zero)
                target = hwnd;
            IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            IntPtr wParam = (IntPtr)(((-120) & 0xFFFF) << 16);
            for (int i = 0; i < count; i++)
            {
                ThrowIfStopped(token);
                NativeMethods.PostMessage(target, NativeMethods.WmMouseWheel, wParam, lParam);
                if (i + 1 < count)
                    await DelayCheckingEsc(16, token);
            }
        }

        await DelayCheckingEsc(60, token);
    }

    private static void FocusTargetIfNeeded(IntPtr hwnd)
    {
        if (NativeMethods.GetForegroundWindow() == hwnd)
            return;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        uint current = NativeMethods.GetCurrentThreadId();
        bool attachedForeground = foregroundThread != 0 && foregroundThread != current && NativeMethods.AttachThreadInput(current, foregroundThread, true);
        bool attachedTarget = targetThread != 0 && targetThread != current && targetThread != foregroundThread && NativeMethods.AttachThreadInput(current, targetThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedForeground)
                NativeMethods.AttachThreadInput(current, foregroundThread, false);
            if (attachedTarget)
                NativeMethods.AttachThreadInput(current, targetThread, false);
        }
    }

    private static async Task<NativeMethods.Rect> WaitForStableBounds(IntPtr hwnd, CancellationToken token)
    {
        var bounds = WindowFinder.GetBounds(hwnd);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await DelayCheckingEsc(60, token);
            var next = WindowFinder.GetBounds(hwnd);
            if (next.Width == bounds.Width && next.Height == bounds.Height && next.Left == bounds.Left && next.Top == bounds.Top)
                return next;
            bounds = next;
        }
        return bounds;
    }

    private static NativeMethods.Rect ClampToVirtualScreen(NativeMethods.Rect rect)
    {
        int vx = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        int vy = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        int vw = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        int vh = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (vw <= 0 || vh <= 0)
            return rect;

        int left = Math.Max(vx, rect.Left);
        int top = Math.Max(vy, rect.Top);
        int right = Math.Min(vx + vw, rect.Right);
        int bottom = Math.Min(vy + vh, rect.Bottom);
        return new NativeMethods.Rect { Left = left, Top = top, Right = Math.Max(left, right), Bottom = Math.Max(top, bottom) };
    }

    private static (int X, int Y) ChooseInitialScrollPoint(NativeMethods.Rect bounds, int? clickX, int? clickY, int ignoreRightPx)
    {
        int minX = bounds.Left + Math.Min(24, bounds.Width / 4);
        int maxX = Math.Max(minX, bounds.Right - Math.Max(ignoreRightPx + 12, 32));
        int minY = bounds.Top + Math.Min(48, bounds.Height / 4);
        int maxY = Math.Max(minY, bounds.Bottom - 24);

        if (clickX.HasValue && clickY.HasValue
            && clickX.Value >= bounds.Left && clickX.Value < bounds.Right
            && clickY.Value >= bounds.Top && clickY.Value < bounds.Bottom)
        {
            return (Math.Clamp(clickX.Value, minX, maxX), Math.Clamp(clickY.Value, minY, maxY));
        }

        int fallbackX = bounds.Left + bounds.Width / 2;
        int fallbackY = bounds.Top + Math.Clamp((int)(bounds.Height * 0.58), 80, Math.Max(80, bounds.Height - 40));
        return (Math.Clamp(fallbackX, minX, maxX), Math.Clamp(fallbackY, minY, maxY));
    }

    private static int ClampScrollX(NativeMethods.Rect bounds, ImageStitcher.ChangeBand band, int currentX, int ignoreRightPx)
    {
        int minX = bounds.Left + (band.Right > band.Left ? band.Left + 12 : 24);
        int maxX = bounds.Left + (band.Right > band.Left ? band.Right - 12 : bounds.Width - ignoreRightPx - 12);
        if (maxX < minX)
        {
            minX = bounds.Left + 16;
            maxX = Math.Max(minX, bounds.Right - ignoreRightPx - 12);
        }
        return Math.Clamp(currentX, minX, maxX);
    }

    private static int ClampScrollY(NativeMethods.Rect bounds, ImageStitcher.ChangeBand band, int currentY)
    {
        int minY = bounds.Top + band.Top + Math.Min(24, band.Height / 4);
        int maxY = bounds.Top + band.Bottom - Math.Min(24, band.Height / 4);
        if (maxY < minY)
            return bounds.Top + band.Top + band.Height / 2;
        return Math.Clamp(currentY, minY, maxY);
    }

    private static bool IsEscapeDown() => (NativeMethods.GetAsyncKeyState(NativeMethods.VkEscape) & 0x8000) != 0;

    private static void ThrowIfStopped(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsEscapeDown())
            throw new OperationCanceledException(token);
    }

    private static async Task DelayCheckingEsc(int ms, CancellationToken token)
    {
        int remaining = ms;
        while (remaining > 0)
        {
            ThrowIfStopped(token);
            int step = Math.Min(30, remaining);
            await Task.Delay(step, token);
            remaining -= step;
        }
        ThrowIfStopped(token);
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

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        _text = new TextBlock
        {
            Text = "Scrolling…",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var stopBtn = new Border
        {
            Background = Palette.Elevated,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(12, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "Stop (Esc)",
                Foreground = Palette.Text,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                FontWeight = FontWeights.Medium
            }
        };
        stopBtn.MouseEnter += (_, _) => stopBtn.Background = Palette.Hover;
        stopBtn.MouseLeave += (_, _) => stopBtn.Background = Palette.Elevated;
        stopBtn.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (!_cancel.IsCancellationRequested)
                _cancel.Cancel();
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { dot, _text, stopBtn }
        };

        Content = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 7, 10, 7),
            Child = row
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
            ex |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(ex));
            // Exclude HUD from BitBlt screen capture so it never flickers or leaks into stitched frames.
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            if (!_cancel.IsCancellationRequested)
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
            Dispatcher.BeginInvoke(() => Report(text));
            return;
        }
        _text.Text = text;
    }

    // No-op: HUD is excluded from capture via WDA_EXCLUDEFROMCAPTURE so hide/show flicker is eliminated.
    public void Suspend() { }
    public void Resume() { }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }
        return IntPtr.Zero;
    }
}
