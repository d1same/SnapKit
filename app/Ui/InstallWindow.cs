using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class InstallWindow : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _percent;
    private readonly ProgressBar _progress;
    private readonly Action<Action<string, double?>> _work;
    private bool _running = true;

    private InstallWindow(bool updating, Action<Action<string, double?>> work)
    {
        _work = work;
        Title = updating ? "Updating " + Brand.Name : "Installing " + Brand.Name;
        Width = 460;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        Background = Palette.Bg;

        var header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = updating ? "Updating " + Brand.Name : "Installing " + Brand.Name,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var pill = UiChrome.Pill("v" + ReleaseClient.Display, Palette.Elevated, Palette.Text, Palette.Line, mono: true);
        pill.Margin = new Thickness(10, 2, 0, 0);
        header.Children.Add(pill);

        _status = new TextBlock
        {
            Text = updating
                ? "Replacing the installed copy beside the clock…"
                : "Installing for your account. SnapKit will sit beside the clock and start with Windows.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        _percent = new TextBlock
        {
            Text = "",
            Foreground = Palette.Subtle,
            FontFamily = Palette.Mono,
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 5)
        };

        var barWrap = UiChrome.DarkProgress(out _progress);
        _progress.IsIndeterminate = true;

        var card = UiChrome.Card(new StackPanel
        {
            Children = { header, _status, _percent, barWrap }
        }, new Thickness(20, 18, 20, 20));
        card.Margin = new Thickness(14);
        Content = card;

        Closing += (_, e) =>
        {
            if (_running)
                e.Cancel = true;
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => _work(Report));
        }
        finally
        {
            _running = false;
            Close();
        }
    }

    private void Report(string text, double? percent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _status.Text = text;
            if (percent.HasValue)
            {
                _progress.IsIndeterminate = false;
                _progress.Value = Math.Clamp(percent.Value, 0, 100);
                _percent.Text = $"{Math.Round(_progress.Value)}%";
            }
            else
            {
                _progress.IsIndeterminate = true;
                _percent.Text = "";
            }
        });
    }

    public static void Run(bool updating, Action<Action<string, double?>> body)
    {
        var window = new InstallWindow(updating, body);
        window.ShowDialog();
    }
}
