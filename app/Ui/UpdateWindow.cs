using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SnapKit;

internal sealed class UpdateWindow : Window
{
    private readonly ReleaseInfo _info;
    private readonly TextBlock _status;
    private readonly TextBlock _percentText;
    private readonly Border _progressWrap;
    private readonly ProgressBar _progressBar;
    private readonly Button _update;
    private readonly Button _later;
    private CancellationTokenSource? _downloadCts;

    public UpdateWindow(ReleaseInfo info)
    {
        _info = info;
        Title = "Update " + Brand.Name;
        Width = 510;
        Height = 440;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = Palette.Bg;

        var title = new TextBlock
        {
            Text = "Update available",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        };

        string targetVer = info.Tag.TrimStart('v', 'V');
        var versionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        versionRow.Children.Add(UiChrome.Pill("v" + ReleaseClient.Display, Palette.Group, Palette.Muted, Palette.Line, mono: true));
        versionRow.Children.Add(new TextBlock
        {
            Text = "→",
            Foreground = Palette.Subtle,
            FontFamily = Palette.Font,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        });
        versionRow.Children.Add(UiChrome.Pill("v" + targetVer, Palette.ActiveSurface, Palette.Ink, Palette.ActiveSurface, mono: true));
        if (info.AssetSize > 0)
        {
            versionRow.Children.Add(new TextBlock
            {
                Text = ReleaseClient.FormatBytes(info.AssetSize),
                Foreground = Palette.Muted,
                FontFamily = Palette.Mono,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
        }

        string formattedNotes = CleanNotes(info.Notes);
        var notesBox = new TextBox
        {
            Text = formattedNotes,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Palette.Field,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            FontFamily = Palette.Font,
            FontSize = 12.5,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var notesHeader = new DockPanel { Margin = new Thickness(0, 14, 0, 4) };
        var notesHeading = new TextBlock
        {
            Text = "WHAT'S NEW",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(notesHeading, Dock.Left);
        notesHeader.Children.Add(notesHeading);

        if (!string.IsNullOrWhiteSpace(info.HtmlUrl))
        {
            var openLink = new TextBlock
            {
                Text = "View on GitHub ↗",
                Foreground = Palette.Text,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            openLink.MouseEnter += (_, _) => openLink.TextDecorations = TextDecorations.Underline;
            openLink.MouseLeave += (_, _) => openLink.TextDecorations = null;
            openLink.MouseLeftButtonUp += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true }); }
                catch { /* browser unavailable */ }
            };
            DockPanel.SetDock(openLink, Dock.Right);
            notesHeader.Children.Add(openLink);
        }

        _status = new TextBlock
        {
            Text = $"{Brand.Name} {targetVer} is ready to download and install.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _percentText = new TextBlock
        {
            Text = "",
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var statusRow = new Grid { Margin = new Thickness(0, 12, 0, 6) };
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_status, 0);
        Grid.SetColumn(_percentText, 1);
        statusRow.Children.Add(_status);
        statusRow.Children.Add(_percentText);

        _progressWrap = UiChrome.DarkProgress(out _progressBar);
        _progressWrap.Visibility = Visibility.Collapsed;

        _update = UiChrome.ActionButton("Update now", () => _ = Apply(), ButtonTone.Primary, minWidth: 122);
        _later = UiChrome.ActionButton("Later", OnLaterOrCancel, ButtonTone.Secondary, minWidth: 96);
        _later.Margin = new Thickness(0, 0, 8, 0);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { _later, _update }
        };

        var bottom = new StackPanel
        {
            Children = { statusRow, _progressWrap, actions }
        };

        var top = new StackPanel
        {
            Children = { title, versionRow, notesHeader }
        };

        var layout = new DockPanel { Margin = new Thickness(22), LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(notesBox);
        Content = layout;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                OnLaterOrCancel();
                e.Handled = true;
            }
        };
        Closed += (_, _) => _downloadCts?.Cancel();
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
    }

    public static void Offer(ReleaseInfo info)
    {
        var existing = System.Windows.Application.Current?.Windows.OfType<UpdateWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }
        new UpdateWindow(info).Show();
    }

    public static async Task<string> CheckNow(Window? owner)
    {
        ReleaseInfo? info;
        try
        {
            info = await UpdateCheck.CheckAsync();
        }
        catch (Exception ex)
        {
            if (owner == null)
                return "Update check failed: " + ex.Message;
            return "Couldn't reach GitHub: " + ex.Message;
        }

        if (info == null)
            return $"No release was found on GitHub for {ReleaseClient.Owner}/{ReleaseClient.Repo} yet.";

        if (!ReleaseClient.IsNewer(info.Version))
            return $"{Brand.Name} v{ReleaseClient.Display} is up to date.";

        Offer(info);
        return $"Update {info.Tag} is available.";
    }

    private void OnLaterOrCancel()
    {
        if (_downloadCts != null)
        {
            _downloadCts.Cancel();
            return;
        }
        Close();
    }

    private async Task Apply()
    {
        _update.IsEnabled = false;
        _later.Content = "Cancel";
        _progressWrap.Visibility = Visibility.Visible;
        _progressBar.IsIndeterminate = true;
        _percentText.Text = "";
        _downloadCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                _status.Text = p.Stage;
                if (p.Percent.HasValue)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = p.Percent.Value;
                    _percentText.Text = p.TotalBytes is > 0
                        ? $"{p.Percent.Value:0}%  ({ReleaseClient.FormatBytes(p.BytesRead)} / {ReleaseClient.FormatBytes(p.TotalBytes.Value)})"
                        : $"{p.Percent.Value:0}%";
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                    _percentText.Text = p.BytesRead > 0 ? ReleaseClient.FormatBytes(p.BytesRead) : "";
                }
            });

            var exe = await ReleaseClient.DownloadAsync(_info, progress, _downloadCts.Token);
            _status.Text = "Launching installer for " + _info.Tag + "…";
            _progressBar.IsIndeterminate = true;
            _percentText.Text = "";
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Download cancelled.";
            _percentText.Text = "";
            _progressWrap.Visibility = Visibility.Collapsed;
            _update.IsEnabled = true;
            _later.Content = "Later";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.Foreground = Palette.Danger;
            _percentText.Text = "";
            _progressWrap.Visibility = Visibility.Collapsed;
            _update.IsEnabled = true;
            _later.Content = "Close";
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private static string CleanNotes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Includes the latest improvements and fixes for " + Brand.Name + ".";

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            if (line.StartsWith("### "))
                line = line[4..].Trim();
            else if (line.StartsWith("## "))
                line = line[3..].Trim();
            else if (line.StartsWith("# "))
                line = line[2..].Trim();
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                line = "• " + line[2..].Trim();

            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");
            line = Regex.Replace(line, @"`([^`]+)`", "$1");
            output.Add(line);
        }
        return string.Join(Environment.NewLine, output).Trim();
    }
}
