using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HasDropShadow = false,
            Padding = new Thickness(0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            OverridesDefaultStyle = true
        };
    }

    public static MenuItem Item(string text, Action action, string? shortcut = null)
    {
        var item = new MenuItem
        {
            Header = BuildRow(text, shortcut),
            Template = ItemTemplate,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            OverridesDefaultStyle = true,
            SnapsToDevicePixels = true
        };
        item.Click += (_, _) => action();
        return item;
    }

    public static void SetLabel(MenuItem item, string text, string? shortcut = null)
    {
        item.Header = BuildRow(text, shortcut);
    }

    public static Border Separator() => new()
    {
        Height = 1,
        Background = Palette.Divider,
        Margin = new Thickness(8, 4, 8, 4),
        IsHitTestVisible = false
    };

    private static Grid BuildRow(string text, string? shortcut)
    {
        var grid = new Grid { MinWidth = 186 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = text,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        if (!string.IsNullOrEmpty(shortcut))
        {
            var hint = new TextBlock
            {
                Text = shortcut,
                Foreground = Palette.Muted,
                FontFamily = Palette.Mono,
                FontSize = 10.5,
                Margin = new Thickness(20, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 1);
            grid.Children.Add(hint);
        }

        return grid;
    }

    private static ControlTemplate BuildMenu()
    {
        // Outer transparent wrapper gives room for the rounded DropShadowEffect without square clipping.
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        outer.SetValue(Border.PaddingProperty, new Thickness(10));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Palette.Card);
        border.SetValue(Border.BorderBrushProperty, Palette.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new Thickness(4, 5, 4, 5));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 3,
            Opacity = 0.5,
            Color = Colors.Black
        });

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(Panel.IsItemsHostProperty, true);
        border.AppendChild(stack);
        outer.AppendChild(border);
        return new ControlTemplate(typeof(ContextMenu)) { VisualTree = outer };
    }

    private static ControlTemplate BuildItem()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Row";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 6.5, 11, 6.5));
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
