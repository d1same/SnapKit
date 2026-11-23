using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class ErrorWindow : Window
{
    public ErrorWindow(string message)
    {
        Title = Brand.Name;
        Width = 480;
        Height = 300;
        MinWidth = 340;
        MinHeight = 220;
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
            BorderThickness = new Thickness(1),
            FontFamily = Palette.Mono,
            FontSize = 12,
            Padding = new Thickness(12, 10, 12, 10)
        };
        var copy = UiChrome.ActionButton("Copy error", () =>
        {
            try { Clipboard.SetText(message); } catch { /* clipboard busy */ }
        }, ButtonTone.Primary, minWidth: 108);
        copy.Margin = new Thickness(0, 0, 8, 0);
        var close = UiChrome.ActionButton("Close", Close, ButtonTone.Secondary, minWidth: 84);

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
            Margin = new Thickness(18),
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
                    Margin = new Thickness(0, 0, 0, 10)
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
