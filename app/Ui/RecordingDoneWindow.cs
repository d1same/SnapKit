using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SnapKit;

/// <summary>
/// Style 3 post-recording card offering Copy File (Ctrl+C), Quick Save (Ctrl+Shift+S),
/// Save as… (Ctrl+S), Play, and Discard/Close.
/// </summary>
internal sealed class RecordingDoneWindow : Window
{
    private readonly RecordingResult _result;
    private readonly AppState _state;
    private readonly TextBlock _status;

    public RecordingDoneWindow(RecordingResult result, AppState state)
    {
        _result = result;
        _state = state;
        string kind = result.Format == RecordingFormat.Gif ? "GIF" : "MP4";

        Title = $"{Brand.Name} {kind} Ready";
        Width = 490;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = Palette.Bg;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = $"{kind} recording ready",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var badge = UiChrome.Pill(kind, Palette.ActiveSurface, Palette.Ink, Palette.ActiveSurface, mono: true);
        badge.Margin = new Thickness(10, 1, 0, 0);
        header.Children.Add(badge);

        string durText = result.Duration.TotalHours >= 1
            ? result.Duration.ToString(@"hh\:mm\:ss")
            : result.Duration.ToString(@"mm\:ss");
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        meta.Children.Add(MetaPill(durText));
        meta.Children.Add(MetaPill($"{result.Width}×{result.Height}"));
        meta.Children.Add(MetaPill(ReleaseClient.FormatBytes(result.FileSizeBytes)));
        meta.Children.Add(MetaPill($"{result.Frames} frames"));

        _status = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(result.Warning)
                ? result.Warning
                : "Copy pastes the file into Slack, Teams, Discord, GitHub, or Explorer.",
            Foreground = !string.IsNullOrWhiteSpace(result.Warning) ? Palette.Warning : Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var copyBtn = UiChrome.ActionButton("Copy file", CopyFile, ButtonTone.Primary, minWidth: 108);
        copyBtn.ToolTip = "Copy file to clipboard  (Ctrl+C)";
        copyBtn.Margin = new Thickness(0, 0, 6, 0);

        var quickBtn = UiChrome.ActionButton("Quick Save", QuickSave, ButtonTone.Secondary, minWidth: 96);
        quickBtn.ToolTip = "Save directly to your Quick Save folder  (Ctrl+Shift+S)";
        quickBtn.Margin = new Thickness(0, 0, 6, 0);

        var saveBtn = UiChrome.ActionButton("Save as…", SaveAs, ButtonTone.Secondary, minWidth: 86);
        saveBtn.ToolTip = "Choose save location  (Ctrl+S)";
        saveBtn.Margin = new Thickness(0, 0, 6, 0);

        var playBtn = UiChrome.ActionButton("Play", OpenPreview, ButtonTone.Secondary, minWidth: 64);
        playBtn.ToolTip = "Open in default player";
        playBtn.Margin = new Thickness(0, 0, 6, 0);

        var closeBtn = UiChrome.ActionButton("Close", Close, ButtonTone.Ghost, minWidth: 60);
        closeBtn.ToolTip = "Close  (Esc)";

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(copyBtn);
        actions.Children.Add(quickBtn);
        actions.Children.Add(saveBtn);
        actions.Children.Add(playBtn);
        actions.Children.Add(closeBtn);

        var card = UiChrome.Card(new StackPanel
        {
            Children = { header, meta, _status, actions }
        }, new Thickness(18, 16, 18, 18));
        card.Margin = new Thickness(14);
        Content = card;

        PreviewKeyDown += (_, e) =>
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.C)
            {
                CopyFile();
                e.Handled = true;
            }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                QuickSave();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.S)
            {
                SaveAs();
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
    }

    private void CopyFile()
    {
        if (ImageFile.CopyFile(_result.FilePath))
        {
            _status.Foreground = Palette.Copied;
            _status.Text = "Copied! Paste the file anywhere with Ctrl+V.";
        }
        else
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Clipboard is busy — click Copy file again.";
        }
    }

    private void QuickSave()
    {
        try
        {
            string saved = ImageFile.QuickSaveFile(_result.FilePath, _result.Format, _state.Settings.QuickSaveFolder);
            _status.Foreground = Palette.Copied;
            _status.Text = "Saved " + Path.GetFileName(saved);
            _state.Tray?.Balloon(Brand.Name, "Saved to " + saved);
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Danger;
            _status.Text = "Quick Save failed: " + ex.Message;
        }
    }

    private void SaveAs()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            try
            {
                string? saved = ImageFile.SaveMedia(_result.FilePath, _result.Format, this);
                if (saved != null)
                {
                    _status.Foreground = Palette.Copied;
                    _status.Text = "Saved " + Path.GetFileName(saved);
                }
            }
            catch (Exception ex)
            {
                _status.Foreground = Palette.Danger;
                _status.Text = "Save failed: " + ex.Message;
            }
        }));
    }

    private void OpenPreview()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_result.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Danger;
            _status.Text = ex.Message;
        }
    }

    private static Border MetaPill(string text)
    {
        var pill = UiChrome.Pill(text, Palette.Field, Palette.Muted, Palette.Line, mono: true);
        pill.Margin = new Thickness(0, 0, 6, 0);
        return pill;
    }
}
