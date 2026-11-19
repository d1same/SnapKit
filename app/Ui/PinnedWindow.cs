using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal sealed class PinnedWindow : Window
{
    private readonly BitmapSource _image;

    public PinnedWindow(BitmapSource image)
    {
        _image = image;
        Title = Brand.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var picture = new Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var close = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Cursor = Cursors.Hand,
            Opacity = 0,
            Child = new TextBlock
            {
                Text = "×",
                Foreground = Palette.Text,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = "Close pin"
        };
        close.MouseLeftButtonUp += (_, _) => Close();

        var root = new Grid();
        root.Children.Add(picture);
        root.Children.Add(close);
        root.MouseEnter += (_, _) => close.Opacity = 1;
        root.MouseLeave += (_, _) => close.Opacity = 0;
        Content = new Border
        {
            Background = Palette.Bg,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                Opacity = 0.5,
                ShadowDepth = 2,
                Color = Colors.Black
            }
        };

        double width = image.PixelWidth;
        double height = image.PixelHeight;
        double maxW = SystemParameters.WorkArea.Width * 0.7;
        double maxH = SystemParameters.WorkArea.Height * 0.7;
        double scale = Math.Min(1, Math.Min(maxW / width, maxH / height));
        Width = Math.Max(80, width * scale);
        Height = Math.Max(48, height * scale);

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                ImageFile.Copy(_image);
                return;
            }
            try { DragMove(); } catch { /* mouse already released */ }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                ImageFile.Copy(_image);
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                ImageFile.Save(_image, this);
        };

        MouseRightButtonUp += (_, _) => ImageFile.Copy(_image);
        var menu = new ContextMenu
        {
            Background = Palette.Card,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1)
        };
        menu.Items.Add(Item("Copy", () => ImageFile.Copy(_image)));
        menu.Items.Add(Item("Save as…", () => ImageFile.Save(_image, this)));
        menu.Items.Add(Item("Close", Close));
        ContextMenu = menu;
        ToolTip = "Right-click copies the picture. Drag to move. Esc closes.";
    }

    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Background = Palette.Card,
            Foreground = Palette.Text,
            FontFamily = Palette.Font
        };
        item.Click += (_, _) => action();
        return item;
    }
}
