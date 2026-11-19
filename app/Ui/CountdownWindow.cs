using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapKit;

internal sealed class CountdownWindow : Window
{
    private readonly TextBlock _number;
    private readonly MonitorInfo _monitor;

    public CountdownWindow(MonitorInfo monitor, int seconds)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Width = 180;
        Height = 180;
        Content = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    (_number = new TextBlock
                    {
                        Text = seconds.ToString(),
                        Foreground = Palette.Text,
                        FontFamily = Palette.Font,
                        FontSize = 72,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }),
                    new TextBlock
                    {
                        Text = Brand.Name,
                        Foreground = Palette.Muted,
                        FontFamily = Palette.Font,
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        SourceInitialized += (_, _) => Place();
    }

    public void SetNumber(int value) => _number.Text = value.ToString();

    private void Place()
    {
        double scale = _monitor.Scale <= 0 ? 1 : _monitor.Scale;
        int size = (int)Math.Round(180 * scale);
        int x = _monitor.X + (_monitor.Width - size) / 2;
        int y = _monitor.Y + (_monitor.Height - size) / 2;
        WindowChrome.Place(this, x, y, size, size, scale, topmost: true, activate: true);
    }
}
