using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapKit;

internal static class DarkMenu
{
    private static readonly ControlTemplate MenuTemplate = BuildMenu();
    private static readonly ControlTemplate ItemTemplate = BuildItem();

    public static ContextMenu Create()
    {
        return new ContextMenu
        {
            Template = MenuTemplate,
            Background = Palette.Card,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            OverridesDefaultStyle = true
        };
    }

    public static MenuItem Item(string text, Action action, string? shortcut = null)
    {
        object header = string.IsNullOrEmpty(shortcut)
            ? text
            : BuildRow(text, shortcut);

        var item = new MenuItem
        {
            Header = header,
            Template = ItemTemplate,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13,
            OverridesDefaultStyle = true,
            SnapsToDevicePixels = true
        };
        item.Click += (_, _) => action();
        return item;
    }

    public static Border Separator() => new()
    {
        Height = 1,
        Background = Palette.Divider,
        Margin = new Thickness(8, 4, 8, 4),
        IsHitTestVisible = false
    };

    private static Grid BuildRow(string text, string shortcut)
    {
        var grid = new Grid { MinWidth = 176 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = text,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var hint = new TextBlock
        {
            Text = shortcut,
            Foreground = Palette.Subtle,
            FontFamily = Palette.Mono,
            FontSize = 11,
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(hint, 1);
        grid.Children.Add(label);
        grid.Children.Add(hint);
        return grid;
    }

    private static ControlTemplate BuildMenu()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Palette.Card);
        border.SetValue(Border.BorderBrushProperty, Palette.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new Thickness(4, 5, 4, 5));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(Panel.IsItemsHostProperty, true);
        border.AppendChild(stack);
        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
    }

    private static ControlTemplate BuildItem()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Row";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 12, 7));
        border.SetValue(TextElement.ForegroundProperty, Palette.Text);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(TextElement.ForegroundProperty, Palette.Text);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Hover, "Row"));
        template.Triggers.Add(hover);
        return template;
    }
}
