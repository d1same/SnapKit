using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal enum ButtonTone
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

internal static class UiChrome
{
    private static readonly ControlTemplate FlatButtonTemplate = BuildButtonTemplate();

    public static Button ActionButton(string label, Action onClick, ButtonTone tone = ButtonTone.Secondary, double minWidth = 0)
    {
        var button = CreateButton(label, tone, minWidth);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Button CreateButton(string label, ButtonTone tone = ButtonTone.Secondary, double minWidth = 0)
    {
        var (bg, hover, fg, border) = ColorsFor(tone);
        var button = new Button
        {
            Content = label,
            Template = FlatButtonTemplate,
            Background = bg,
            Foreground = fg,
            BorderBrush = border,
            BorderThickness = new Thickness(tone == ButtonTone.Primary ? 0 : 1),
            Padding = new Thickness(14, 7, 14, 7),
            FontFamily = Palette.Font,
            FontSize = 13,
            FontWeight = tone == ButtonTone.Primary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            OverridesDefaultStyle = true,
            MinWidth = minWidth
        };
        button.Tag = hover;
        button.MouseEnter += (_, _) =>
        {
            if (button.IsEnabled)
                button.Background = hover;
        };
        button.MouseLeave += (_, _) => button.Background = bg;
        button.IsEnabledChanged += (_, _) =>
        {
            button.Opacity = button.IsEnabled ? 1.0 : 0.48;
            if (!button.IsEnabled)
                button.Background = bg;
        };
        return button;
    }

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Palette.Card,
        BorderBrush = Palette.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(11),
        Padding = padding ?? new Thickness(14, 12, 14, 12),
        SnapsToDevicePixels = true,
        Child = child
    };

    public static Border Pill(string text, SolidColorBrush? bg = null, SolidColorBrush? fg = null, SolidColorBrush? border = null, bool mono = false) => new()
    {
        Background = bg ?? Palette.Group,
        BorderBrush = border ?? Palette.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3, 8, 3),
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
        Child = new TextBlock
        {
            Text = text,
            Foreground = fg ?? Palette.Text,
            FontFamily = mono ? Palette.Mono : Palette.Font,
            FontSize = mono ? 12 : 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    public static Border ToggleRow(string title, string? subtitle, bool initial, Action<bool> onChanged)
    {
        bool state = initial;
        var track = new Border
        {
            Width = 38,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = state ? Palette.Accent : Palette.Elevated,
            BorderBrush = state ? Palette.Accent : Palette.Line,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        var knob = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Brushes.White,
            HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2)
        };
        track.Child = knob;

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13.5
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            labels.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 12, 0)
            });
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(track, 1);
        grid.Children.Add(labels);
        grid.Children.Add(track);

        var row = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 7, 6, 7),
            Cursor = Cursors.Hand,
            Child = grid
        };
        row.MouseEnter += (_, _) => row.Background = Palette.Group;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            state = !state;
            track.Background = state ? Palette.Accent : Palette.Elevated;
            track.BorderBrush = state ? Palette.Accent : Palette.Line;
            knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            onChanged(state);
            e.Handled = true;
        };
        return row;
    }

    public static Border DarkProgress(out ProgressBar bar)
    {
        bar = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Foreground = Palette.Accent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        return new Border
        {
            Background = Palette.Elevated,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            Child = bar
        };
    }

    public static TextBlock SectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = Palette.Subtle,
        FontFamily = Palette.Font,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 12, 0, 6)
    };

    private static (SolidColorBrush Bg, SolidColorBrush Hover, SolidColorBrush Fg, SolidColorBrush Border) ColorsFor(ButtonTone tone) => tone switch
    {
        ButtonTone.Primary => (Palette.Accent, Palette.AccentHover, Palette.Text, Palette.Accent),
        ButtonTone.Ghost => (Brushes.Transparent == null ? Palette.Card : Palette.Freeze(0, 0, 0, 0), Palette.Hover, Palette.Text, Palette.Freeze(0, 0, 0, 0)),
        ButtonTone.Danger => (Palette.DangerSoft, Palette.Danger, Palette.Text, Palette.Danger),
        _ => (Palette.Elevated, Palette.Hover, Palette.Text, Palette.Line)
    };

    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
