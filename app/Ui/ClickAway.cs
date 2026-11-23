using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapKit;

/// <summary>
/// Closes a snapshot window after the user clicks somewhere else.
/// A save dialog or an OCR window opened from it can pause that.
/// </summary>
internal static class ClickAway
{
    private sealed class State
    {
        public int Paused;
        public bool SawActive;
        public long OpenedAt = Environment.TickCount64;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, State> Windows = new();
    private static int _globalPaused;

    public static bool IsAnyPaused => _globalPaused > 0;

    public static void Attach(Window window, Action close, Func<bool> enabled)
    {
        var state = Windows.GetOrCreateValue(window);
        window.Activated += (_, _) => state.SawActive = true;
        window.Deactivated += (_, _) =>
        {
            if (state.Paused > 0 || _globalPaused > 0 || !state.SawActive || !enabled())
                return;
            if (Environment.TickCount64 - state.OpenedAt < 400)
                return;
            window.Dispatcher.BeginInvoke(() =>
            {
                if (state.Paused > 0 || _globalPaused > 0 || !window.IsVisible || window.IsActive || MenuIsOpen(window))
                    return;
                close();
            }, DispatcherPriority.ApplicationIdle);
        };
    }

    public static IDisposable Pause(Window window)
    {
        var state = Windows.GetOrCreateValue(window);
        state.Paused++;
        _globalPaused++;
        return new Resume(state);
    }

    private static bool MenuIsOpen(DependencyObject node)
    {
        if (node is FrameworkElement element && element.ContextMenu is { IsOpen: true })
            return true;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child && MenuIsOpen(child))
                return true;
        }
        return false;
    }

    private sealed class Resume : IDisposable
    {
        private State? _state;

        public Resume(State state) => _state = state;

        public void Dispose()
        {
            if (_state == null)
                return;
            _state.Paused = Math.Max(0, _state.Paused - 1);
            _globalPaused = Math.Max(0, _globalPaused - 1);
            _state = null;
        }
    }
}
