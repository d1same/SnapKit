using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class UpdateWindow : Window
{
    private readonly ReleaseInfo _info;
    private readonly TextBlock _status;
    private readonly Button _update;
    private readonly Button _later;

    public UpdateWindow(ReleaseInfo info)
    {
        _info = info;
        Title = "Update " + Brand.Name;
        Width = 440;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Palette.Bg;

        _status = new TextBlock
        {
            Text = "SnapKit " + info.Tag.TrimStart('v', 'V') + " is ready. This copy is " + ReleaseClient.Display + ".",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _update = Button("Update", true);
        _later = Button("Later", false);
        _update.Click += async (_, _) => await Apply();
        _later.Click += (_, _) => Close();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        actions.Children.Add(_later);
        actions.Children.Add(_update);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = "Update SnapKit",
                    Foreground = Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold
                },
                _status,
                actions
            }
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
    }

    public static void Offer(ReleaseInfo info)
    {
        if (System.Windows.Application.Current?.Windows.OfType<UpdateWindow>().Any() == true)
            return;
        new UpdateWindow(info).Show();
    }

    public static async Task CheckNow(Window owner)
    {
        ReleaseInfo? info;
        try
        {
            info = await ReleaseClient.LatestAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (info == null)
        {
            MessageBox.Show(
                owner,
                "No SnapKit release was found on GitHub yet. A published version of " + ReleaseClient.Owner + "/" + ReleaseClient.Repo + " will show up here.",
                Brand.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ReleaseClient.IsNewer(info.Version))
        {
            MessageBox.Show(owner, "SnapKit " + ReleaseClient.Display + " is the latest version.", Brand.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Offer(info);
    }

    private async Task Apply()
    {
        _update.IsEnabled = false;
        _later.IsEnabled = false;
        try
        {
            var exe = await ReleaseClient.DownloadAsync(_info, new Progress<string>(text => _status.Text = text));
            _status.Text = "Installing " + _info.Tag + "…";
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _update.IsEnabled = true;
            _later.IsEnabled = true;
        }
    }

    private static Button Button(string label, bool primary) => new()
    {
        Content = label,
        Width = 110,
        Margin = new Thickness(8, 0, 0, 0),
        Padding = new Thickness(8, 6, 8, 6),
        Background = primary ? Palette.Text : Palette.Elevated,
        Foreground = primary ? Palette.Ink : Palette.Text,
        BorderBrush = Palette.Line,
        FontFamily = Palette.Font
    };
}
