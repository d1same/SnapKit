using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SnapKit;

internal sealed class CapturedStep
{
    public PixelBuffer Image { get; init; } = null!;
    public int ClickX { get; init; }
    public int ClickY { get; init; }
    public string Caption { get; set; } = "";
    public string WindowTitle { get; init; } = "";
}

/// <summary>
/// Lightweight click-triggered step recorder for creating MSP client tutorials & SOPs.
/// Every left-click outside SnapKit's own floating HUD captures the target window (or a focused
/// crop around the click) and marks the exact click position for numbered step badges.
/// </summary>
internal sealed class StepGuideRecorder : Window
{
    private static readonly SolidColorBrush RecRed = Palette.Freeze(0xEF, 0x44, 0x44);

    private readonly AppState _state;
    private readonly List<CapturedStep> _steps = new();
    private readonly TextBlock _countLabel;
    private readonly TextBlock _pauseLabel;
    private readonly Border _finishChip;
    private readonly Ellipse _dot;
    private readonly NativeMethods.LowLevelKeyboardProc _mouseProc;
    private IntPtr _hook = IntPtr.Zero;
    private IntPtr _selfHwnd = IntPtr.Zero;
    private bool _paused;
    private bool _closing;
    private long _lastClickTick;

    public StepGuideRecorder(AppState state)
    {
        _state = state;
        _mouseProc = OnMouseHook;

        Title = Brand.Name + " Step Guide";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = RecRed,
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _countLabel = new TextBlock
        {
            Text = "Click anywhere to capture Step 1",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        _pauseLabel = new TextBlock
        {
            Text = "Pause",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var pauseBtn = PillButton(_pauseLabel, "Pause or resume capturing clicks", TogglePause, primary: false);
        var undoBtn = PillButton(
            LucideIcons.Create(LucideIcon.Undo, Palette.Text, size: 13.5, strokeWidth: 1.9),
            "Remove last captured step",
            UndoLast,
            primary: false);

        var finishRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                LucideIcons.Create(LucideIcon.Check, Palette.Ink, size: 13.5, strokeWidth: 2.1),
                new TextBlock
                {
                    Text = "Finish",
                    Foreground = Palette.Ink,
                    FontFamily = Palette.Font,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                }
            }
        };
        _finishChip = PillButton(finishRow, "Open the Step-by-Step Guide editor", FinishAndOpen, primary: true);
        var closeBtn = PillButton(
            LucideIcons.Create(LucideIcon.Close, Palette.Muted, size: 13.5, strokeWidth: 1.9),
            "Cancel step recording  (Esc)",
            Cancel,
            primary: false);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _dot,
                _countLabel,
                Hairline(),
                pauseBtn,
                undoBtn,
                _finishChip,
                closeBtn
            }
        };

        Content = new Border
        {
            Margin = new Thickness(10),
            Background = Palette.Toolbar,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 6, 8, 6),
            SnapsToDevicePixels = true,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.5,
                Color = Colors.Black
            },
            Child = row
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled)
                return;
            try { DragMove(); } catch { /* ignore */ }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        };

        SourceInitialized += (_, _) =>
        {
            _selfHwnd = new WindowInteropHelper(this).Handle;
            if (_selfHwnd != IntPtr.Zero)
                NativeMethods.SetWindowDisplayAffinity(_selfHwnd, NativeMethods.WdaExcludeFromCapture);
            PositionTopCenter();
            InstallHook();
        };
        Closed += (_, _) => UninstallHook();
    }

    public static void Launch(AppState state)
    {
        foreach (Window win in Application.Current.Windows)
        {
            if (win is StepGuideRecorder active)
            {
                active.Activate();
                return;
            }
        }
        var recorder = new StepGuideRecorder(state);
        recorder.Show();
        recorder.Activate();
    }

    private void PositionTopCenter()
    {
        var mon = MonitorQuery.UnderCursor();
        double scale = mon.Scale <= 0 ? 1 : mon.Scale;
        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : 440;
        Left = mon.X / scale + Math.Max(12, (mon.Width / scale - w) * 0.5);
        Top = mon.Y / scale + 14;
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero)
            return;
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule;
        IntPtr hMod = NativeMethods.GetModuleHandle(mod?.ModuleName);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseProc, hMod, 0);
    }

    private void UninstallHook()
    {
        if (_hook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativeMethods.Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private IntPtr OnMouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam.ToInt32() == NativeMethods.WmLButtonDown && !_paused && !_closing)
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            // Ignore injected synthetic clicks and rapid double-click duplicates (< 280ms).
            bool injected = (info.Flags & 0x01u) != 0;
            long now = Environment.TickCount64;
            if (!injected && now - _lastClickTick >= 280)
            {
                int clickX = info.Pt.X;
                int clickY = info.Pt.Y;
                // Ignore clicks on SnapKit's own floating HUD!
                if (!IsPointInsideSelf(clickX, clickY))
                {
                    _lastClickTick = now;
                    // Capture immediately before the target app navigates away from the clicked button!
                    Dispatcher.BeginInvoke(() => CaptureClickStep(clickX, clickY));
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool IsPointInsideSelf(int screenX, int screenY)
    {
        if (_selfHwnd == IntPtr.Zero)
            return false;
        if (!NativeMethods.GetWindowRect(_selfHwnd, out var r))
            return false;
        return screenX >= r.Left && screenX <= r.Right && screenY >= r.Top && screenY <= r.Bottom;
    }

    private void CaptureClickStep(int screenX, int screenY)
    {
        if (_closing || _paused)
            return;

        try
        {
            var win = WindowFinder.At(screenX, screenY);
            var mon = MonitorQuery.FromPoint(screenX, screenY);

            int capX, capY, capW, capH;
            string title = "";

            if (win != null && win.Bounds.Width >= 80 && win.Bounds.Height >= 60)
            {
                title = (win.Title ?? "").Trim();
                // Clamp capture to monitor bounds and keep huge full-screen windows nicely focused around the click.
                int bx0 = Math.Max(mon.X, win.Bounds.Left);
                int by0 = Math.Max(mon.Y, win.Bounds.Top);
                int bx1 = Math.Min(mon.Right, win.Bounds.Right);
                int by1 = Math.Min(mon.Bottom, win.Bounds.Bottom);
                int bw = Math.Max(80, bx1 - bx0);
                int bh = Math.Max(60, by1 - by0);

                const int maxW = 1120;
                const int maxH = 720;
                if (bw > maxW)
                {
                    capW = maxW;
                    capX = Math.Clamp(screenX - maxW / 2, bx0, bx1 - maxW);
                }
                else
                {
                    capW = bw;
                    capX = bx0;
                }

                if (bh > maxH)
                {
                    capH = maxH;
                    capY = Math.Clamp(screenY - maxH / 2, by0, by1 - maxH);
                }
                else
                {
                    capH = bh;
                    capY = by0;
                }
            }
            else
            {
                // Desktop / taskbar fallback: focused 860x540 area centered around click.
                capW = Math.Min(860, mon.Width);
                capH = Math.Min(540, mon.Height);
                capX = Math.Clamp(screenX - capW / 2, mon.X, mon.Right - capW);
                capY = Math.Clamp(screenY - capH / 2, mon.Y, mon.Bottom - capH);
            }

            var shot = ScreenGrabber.CaptureRect(capX, capY, capW, capH);
            int relX = Math.Clamp(screenX - capX, 8, Math.Max(8, shot.Width - 8));
            int relY = Math.Clamp(screenY - capY, 8, Math.Max(8, shot.Height - 8));

            string cleanTitle = ShortenTitle(title);
            string caption = string.IsNullOrEmpty(cleanTitle)
                ? $"Click here (Step {_steps.Count + 1})"
                : $"Click in {cleanTitle}";

            _steps.Add(new CapturedStep
            {
                Image = shot,
                ClickX = relX,
                ClickY = relY,
                WindowTitle = cleanTitle,
                Caption = caption
            });
            RefreshStatus();
        }
        catch
        {
            // Ignore transient capture failure (e.g. UAC secure desktop).
        }
    }

    private static string ShortenTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";
        string t = title.Trim();
        return t.Length > 48 ? t[..47] + "…" : t;
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseLabel.Text = _paused ? "Resume" : "Pause";
        _dot.Fill = _paused ? Palette.Muted : RecRed;
        RefreshStatus();
    }

    private void UndoLast()
    {
        if (_steps.Count == 0)
            return;
        _steps.RemoveAt(_steps.Count - 1);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_steps.Count == 0)
            _countLabel.Text = _paused ? "Paused · 0 steps" : "Click anywhere to capture Step 1";
        else
            _countLabel.Text = _paused
                ? $"Paused · {_steps.Count} {(_steps.Count == 1 ? "step" : "steps")}"
                : $"Recording · {_steps.Count} {(_steps.Count == 1 ? "step" : "steps")} captured";
        _finishChip.Opacity = _steps.Count > 0 ? 1.0 : 0.55;
    }

    private void FinishAndOpen()
    {
        if (_steps.Count == 0)
        {
            _countLabel.Text = "Click at least once to record a step";
            return;
        }
        _closing = true;
        UninstallHook();
        var captured = _steps.ToList();
        Close();

        var editor = new StepGuideWindow(captured, _state);
        editor.Show();
        editor.Activate();
    }

    private void Cancel()
    {
        _closing = true;
        UninstallHook();
        Close();
    }

    private static Border Hairline() => new()
    {
        Width = 1,
        Height = 16,
        Background = Palette.Divider,
        Margin = new Thickness(4, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border PillButton(UIElement content, string tip, Action onClick, bool primary)
    {
        var baseBg = primary ? Palette.ActiveSurface : Palette.Group;
        var hoverBg = primary ? Palette.ActiveHover : Palette.Hover;
        var border = new Border
        {
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(3, 0, 2, 0),
            CornerRadius = new CornerRadius(7),
            Background = baseBg,
            BorderBrush = primary ? Palette.ActiveSurface : Palette.Line,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content
        };
        border.MouseEnter += (_, _) => border.Background = hoverBg;
        border.MouseLeave += (_, _) => border.Background = baseBg;
        border.MouseLeftButtonDown += (_, e) => e.Handled = true;
        border.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return border;
    }
}
