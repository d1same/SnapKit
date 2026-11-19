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

    public static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Template = ItemTemplate,
            Background = Palette.Card,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13,
            OverridesDefaultStyle = true,
            SnapsToDevicePixels = true
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static ControlTemplate BuildMenu()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Palette.Card);
        border.SetValue(Border.BorderBrushProperty, Palette.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(0, 4, 0, 4));
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
        border.SetValue(Border.BackgroundProperty, Palette.Card);
        border.SetValue(Border.PaddingProperty, new Thickness(14, 8, 18, 8));
        border.SetValue(TextElement.ForegroundProperty, Palette.Text);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(TextElement.ForegroundProperty, Palette.Text);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Hover, "Row"));
        template.Triggers.Add(hover);
        return template;
    }
}
