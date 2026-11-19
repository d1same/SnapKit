using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class InstallWindow : Window
{
    private readonly TextBlock _status;

    public InstallWindow(bool updating)
    {
        Title = updating ? "Updating " + Brand.Name : "Installing " + Brand.Name;
        Width = 440;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Palette.Bg;

        _status = new TextBlock
        {
            Text = updating
                ? "Replacing the copy that sits beside the clock."
                : "Copying SnapKit for this user. It will sit beside the clock and start with Windows.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = updating ? "Updating SnapKit" : "Installing SnapKit",
                    Foreground = Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold
                },
                _status,
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 8,
                    Margin = new Thickness(0, 20, 0, 0),
                    Foreground = Palette.Text,
                    Background = Palette.Elevated,
                    BorderThickness = new Thickness(0)
                }
            }
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
    }

    public void SetStatus(string text) => _status.Text = text;

    public static void Run(bool updating, Action<Action<string>> body)
    {
        var window = new InstallWindow(updating);
        window.Show();
        Pump(window);
        body(text =>
        {
            window.SetStatus(text);
            Pump(window);
        });
        Pump(window);
        window.Close();
    }

    private static void Pump(Window window)
    {
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }
}
