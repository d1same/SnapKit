using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class OcrWindow : Window
{
    public OcrWindow(string text, string? error = null)
    {
        Title = Brand.Name + " OCR";
        Width = 440;
        Height = 320;
        MinWidth = 320;
        MinHeight = 220;
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
        var status = error ?? (string.IsNullOrWhiteSpace(body) ? "No text found in that area." : "Copied to the clipboard.");
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
            CaretBrush = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 14,
            Padding = new Thickness(10),
            MinHeight = 160
        };
        var copy = new Button
        {
            Content = "Copy again",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Palette.Text,
            Foreground = Palette.Ink,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Font
        };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(body))
                Clipboard.SetText(body);
        };
        var close = new Button
        {
            Content = "Close",
            Padding = new Thickness(12, 6, 12, 6),
            Background = Palette.Elevated,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            FontFamily = Palette.Font
        };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { copy, close }
        };
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children =
            {
                At(new TextBlock
                {
                    Text = status,
                    Foreground = error == null ? Palette.Muted : Palette.Danger,
                    FontFamily = Palette.Font,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8)
                }, Dock.Top),
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
