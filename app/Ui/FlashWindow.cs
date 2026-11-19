using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SnapKit;

internal sealed class FlashWindow : Window
{
    private FlashWindow(MonitorInfo monitor)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.White;
        Opacity = 0.18;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        ShowActivated = false;
        SourceInitialized += (_, _) =>
            WindowChrome.Place(this, monitor.X, monitor.Y, monitor.Width, monitor.Height, monitor.Scale, topmost: true, activate: false);
        Loaded += (_, _) =>
        {
            var animation = new DoubleAnimation(0.18, 0, TimeSpan.FromMilliseconds(140));
            animation.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, animation);
        };
    }

    public static void Play(IEnumerable<MonitorInfo> monitors)
    {
        foreach (var monitor in monitors)
            new FlashWindow(monitor).Show();
    }
}
