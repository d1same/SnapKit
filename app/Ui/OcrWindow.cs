using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class OcrWindow : Window
{
    public OcrWindow(string text, string? error = null)
    {
        Title = Brand.Name + " OCR";
        Width = 480;
        Height = 340;
        MinWidth = 340;
        MinHeight = 230;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        var body = string.IsNullOrWhiteSpace(text) ? "" : text;
        var status = error ?? (string.IsNullOrWhiteSpace(body) ? "No text found in that area." : "Copied to clipboard.");
        var statusLabel = new TextBlock
        {
            Text = status,
            Foreground = error == null ? Palette.Muted : Palette.Danger,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var box = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(body) ? status : body,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Palette.Field,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CaretBrush = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13.5,
            Padding = new Thickness(12, 10, 12, 10),
            MinHeight = 160
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        if (!string.IsNullOrWhiteSpace(body))
        {
            var copy = UiChrome.ActionButton("Copy again", () =>
            {
                try
                {
                    Clipboard.SetText(body);
                    statusLabel.Foreground = Palette.Success;
                    statusLabel.Text = "Copied to clipboard.";
                }
                catch
                {
                    statusLabel.Foreground = Palette.Warning;
                    statusLabel.Text = "Clipboard busy — try again.";
                }
            }, ButtonTone.Primary, minWidth: 108);
            copy.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(copy);
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            var errorText = error;
            var copyError = UiChrome.ActionButton("Copy error", () =>
            {
                try { Clipboard.SetText(errorText); } catch { /* clipboard busy */ }
            }, ButtonTone.Primary, minWidth: 108);
            copyError.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(copyError);
        }
        buttons.Children.Add(UiChrome.ActionButton("Close", Close, ButtonTone.Secondary, minWidth: 84));

        Content = new DockPanel
        {
            Margin = new Thickness(18),
            LastChildFill = true,
            Children =
            {
                At(statusLabel, Dock.Top),
                At(buttons, Dock.Bottom),
                box
            }
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
    }

    private static UIElement At(UIElement element, Dock dock)
    {
        DockPanel.SetDock(element, dock);
        return element;
    }
}
