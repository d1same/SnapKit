using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SnapKit;

internal static class DarkMenu
{
    private static readonly ControlTemplate MenuTemplate = BuildMenu();
    private static readonly ControlTemplate ItemTemplate = BuildItem();
    private static readonly ControlTemplate SubmenuTemplate = BuildSubmenuItem();

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

    public static TextBlock SectionHeader(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        Foreground = Palette.Subtle,
        FontFamily = Palette.Font,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(10, 5, 10, 3),
        IsHitTestVisible = false
    };

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

    public static MenuItem Submenu(string text, string? hint = null)
    {
        return new MenuItem
        {
            Header = BuildSubmenuRow(text, hint),
            Template = SubmenuTemplate,
            Background = Brushes.Transparent,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            OverridesDefaultStyle = true,
            SnapsToDevicePixels = true
        };
    }

    public static void SetLabel(MenuItem item, string text, string? shortcut = null)
    {
        item.Header = item.HasItems ? BuildSubmenuRow(text, shortcut) : BuildRow(text, shortcut);
    }

    public static Border Separator() => new()
    {
        Height = 1,
        Background = Palette.Divider,
        Margin = new Thickness(8, 5, 8, 5),
        IsHitTestVisible = false
    };

    private static Grid BuildRow(string text, string? shortcut)
    {
        var grid = new Grid { MinWidth = 196 };
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

    private static Grid BuildSubmenuRow(string text, string? valueHint)
    {
        var grid = new Grid { MinWidth = 196 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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

        if (!string.IsNullOrEmpty(valueHint))
        {
            var hint = new TextBlock
            {
                Text = valueHint,
                Foreground = Palette.Muted,
                FontFamily = Palette.Mono,
                FontSize = 10.5,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hint, 1);
            grid.Children.Add(hint);
        }

        var arrow = new TextBlock
        {
            Text = "›",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 14,
            Margin = new Thickness(8, -1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        return grid;
    }

    private static ControlTemplate BuildMenu()
    {
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        outer.SetValue(Border.PaddingProperty, new Thickness(10));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Palette.Card);
        border.SetValue(Border.BorderBrushProperty, Palette.Line);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new Thickness(5, 6, 5, 6));
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

    private static ControlTemplate BuildSubmenuItem()
    {
        var root = new FrameworkElementFactory(typeof(Grid));

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
        root.AppendChild(border);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
        popup.SetValue(Popup.HorizontalOffsetProperty, -4.0);
        popup.SetValue(Popup.VerticalOffsetProperty, -6.0);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsSubmenuOpen")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });

        var popOuter = new FrameworkElementFactory(typeof(Border));
        popOuter.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        popOuter.SetValue(Border.PaddingProperty, new Thickness(8));

        var popCard = new FrameworkElementFactory(typeof(Border));
        popCard.SetValue(Border.BackgroundProperty, Palette.Card);
        popCard.SetValue(Border.BorderBrushProperty, Palette.Line);
        popCard.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popCard.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        popCard.SetValue(Border.PaddingProperty, new Thickness(5, 6, 5, 6));
        popCard.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            BlurRadius = 14,
            ShadowDepth = 2,
            Opacity = 0.5,
            Color = Colors.Black
        });

        var itemsHost = new FrameworkElementFactory(typeof(StackPanel));
        itemsHost.SetValue(Panel.IsItemsHostProperty, true);
        popCard.AppendChild(itemsHost);
        popOuter.AppendChild(popCard);
        popup.AppendChild(popOuter);
        root.AppendChild(popup);

        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = root };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Palette.Hover, "Row"));
        template.Triggers.Add(hover);
        return template;
    }
}
