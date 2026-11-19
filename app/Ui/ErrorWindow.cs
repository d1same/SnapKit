using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class ErrorWindow : Window
{
    public ErrorWindow(string message)
    {
        Title = Brand.Name;
        Width = 460;
        Height = 280;
        MinWidth = 320;
        MinHeight = 200;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = false;
        var box = new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Palette.Field,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            FontFamily = Palette.Font,
            FontSize = 13,
            Padding = new Thickness(10)
        };
        var copy = new Button
        {
            Content = "Copy error",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Palette.Text,
            Foreground = Palette.Ink,
            BorderThickness = new Thickness(0),
            FontFamily = Palette.Font
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(message); } catch { /* clipboard busy */ }
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
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true,
            Children =
            {
                At(new TextBlock
                {
                    Text = "Something went wrong. The capture overlay has been closed.",
                    Foreground = Palette.Danger,
                    FontFamily = Palette.Font,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                }, Dock.Top),
                At(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { copy, close }
                }, Dock.Bottom),
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
