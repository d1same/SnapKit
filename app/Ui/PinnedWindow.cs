using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SnapKit;

internal sealed class PinnedWindow : Window
{
    private readonly BitmapSource _image;
    private readonly AppState? _state;
    private readonly double _defaultWidth;
    private readonly double _defaultHeight;
    private readonly double _aspect;
    private readonly Border _hoverBar;
    private readonly TextBlock _opacityText;
    private readonly Border _toast;
    private readonly TextBlock _toastText;
    private readonly DispatcherTimer _toastTimer;

    public PinnedWindow(BitmapSource image, AppState? state = null)
    {
        _image = image;
        _state = state;
        _aspect = Math.Max(1, image.PixelWidth) / (double)Math.Max(1, image.PixelHeight);

        Title = Brand.Name + " Pin";
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

        // Floating hover bar
        _opacityText = new TextBlock
        {
            Text = "100%",
            Foreground = Palette.Muted,
            FontFamily = Palette.Mono,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var opacityPill = HoverChip(_opacityText, "Opacity (Ctrl+Scroll to adjust, click to cycle)", CycleOpacity, accent: false);
        var copyChip = HoverChip(HoverLabel("Copy", Brushes.White), "Copy image  (Ctrl+C or double-click)", TryCopy, accent: true);
        var saveChip = HoverChip(HoverLabel("Save", Palette.Text), "Save image  (Ctrl+S, or Ctrl+Shift+S for Quick Save)", () => SaveAs(), accent: false);
        var closeChip = HoverChip(HoverLabel("×", Palette.Text, 14), "Close pin  (Esc)", Close, accent: false);
        closeChip.MinWidth = 24;

        _hoverBar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Padding = new Thickness(4, 3, 4, 3),
            CornerRadius = new CornerRadius(9),
            Background = Palette.Freeze(0x16, 0x18, 0x1E, 0xE8),
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { copyChip, saveChip, opacityPill, closeChip }
            }
        };

        // Transient status toast pill
        _toastText = new TextBlock
        {
            Text = "",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold
        };
        _toast = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10),
            Padding = new Thickness(9, 4, 9, 4),
            CornerRadius = new CornerRadius(7),
            Background = Palette.Freeze(0x14, 0x16, 0x1C, 0xEB),
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = _toastText
        };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.Visibility = Visibility.Collapsed;
        };

        var root = new Grid();
        root.Children.Add(picture);
        root.Children.Add(_hoverBar);
        root.Children.Add(_toast);
        root.MouseEnter += (_, _) =>
        {
            _hoverBar.Opacity = 1;
            _hoverBar.IsHitTestVisible = true;
        };
        root.MouseLeave += (_, _) =>
        {
            _hoverBar.Opacity = 0;
            _hoverBar.IsHitTestVisible = false;
        };

        Content = new Border
        {
            Background = Palette.Bg,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                Opacity = 0.52,
                ShadowDepth = 3,
                Color = Colors.Black
            }
        };

        double width = image.PixelWidth;
        double height = image.PixelHeight;
        double maxW = SystemParameters.WorkArea.Width * 0.72;
        double maxH = SystemParameters.WorkArea.Height * 0.72;
        double scale = Math.Min(1, Math.Min(maxW / Math.Max(1, width), maxH / Math.Max(1, height)));
        _defaultWidth = Math.Max(96, width * scale);
        _defaultHeight = Math.Max(56, height * scale);
        Width = _defaultWidth;
        Height = _defaultHeight;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled)
                return;
            if (e.ClickCount == 2)
            {
                TryCopy();
                return;
            }
            try { DragMove(); } catch { /* mouse already released */ }
        };
        PreviewMouseWheel += OnWheel;
        PreviewKeyDown += (_, e) =>
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.C)
            {
                TryCopy();
                e.Handled = true;
            }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                QuickSave();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.S)
            {
                SaveAs();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key is Key.D0 or Key.NumPad0)
            {
                ResetView();
                e.Handled = true;
            }
        };

        // Clean right-click context menu (no competing MouseRightButtonUp handler)
        var menu = DarkMenu.Create();
        menu.Items.Add(DarkMenu.Item("Copy", TryCopy, "Ctrl+C"));
        menu.Items.Add(DarkMenu.Item("Quick save", QuickSave, "Ctrl+Shift+S"));
        menu.Items.Add(DarkMenu.Item("Save as…", SaveAs, "Ctrl+S"));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Opacity 100%", () => SetOpacity(1.0)));
        menu.Items.Add(DarkMenu.Item("Opacity 75%", () => SetOpacity(0.75)));
        menu.Items.Add(DarkMenu.Item("Opacity 50%", () => SetOpacity(0.50)));
        menu.Items.Add(DarkMenu.Item("Reset size & opacity", ResetView, "Ctrl+0"));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Close", Close, "Esc"));
        ContextMenu = menu;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            SetOpacity(Opacity + delta);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            ResizeKeepingCenter(Width * factor);
            e.Handled = true;
        }
    }

    private void ResizeKeepingCenter(double desiredWidth)
    {
        double maxW = SystemParameters.WorkArea.Width * 0.94;
        double maxH = SystemParameters.WorkArea.Height * 0.94;
        double minW = Math.Max(96, 56 * _aspect);
        double clampedW = Math.Clamp(desiredWidth, minW, Math.Min(maxW, maxH * _aspect));
        double clampedH = Math.Max(56, clampedW / _aspect);

        double centerX = Left + Width / 2;
        double centerY = Top + Height / 2;
        Width = clampedW;
        Height = clampedH;
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;

        double pct = Math.Round((Width / Math.Max(1, _image.PixelWidth)) * 100);
        ShowToast($"{Math.Round(Width)} × {Math.Round(Height)}  ·  {pct:0}%");
    }

    private void SetOpacity(double value)
    {
        Opacity = Math.Clamp(Math.Round(value, 2), 0.25, 1.0);
        int pct = (int)Math.Round(Opacity * 100);
        _opacityText.Text = $"{pct}%";
        ShowToast($"Opacity {pct}%");
    }

    private void CycleOpacity()
    {
        if (Opacity > 0.85)
            SetOpacity(0.75);
        else if (Opacity > 0.60)
            SetOpacity(0.50);
        else
            SetOpacity(1.0);
    }

    private void ResetView()
    {
        double centerX = Left + Width / 2;
        double centerY = Top + Height / 2;
        Width = _defaultWidth;
        Height = _defaultHeight;
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
        Opacity = 1.0;
        _opacityText.Text = "100%";
        ShowToast("Reset size & opacity");
    }

    private void TryCopy()
    {
        if (ImageFile.Copy(_image))
            ShowToast("Copied to clipboard");
        else
            ShowToast("Clipboard busy — try again");
    }

    private void SaveAs()
    {
        if (ImageFile.Save(_image, this))
            ShowToast("Saved");
    }

    private void QuickSave()
    {
        try
        {
            string path = ImageFile.QuickSave(_image, _state?.Settings.QuickSaveFolder);
            ShowToast("Saved " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            ShowToast("Save failed: " + ex.Message);
        }
    }

    private void ShowToast(string message)
    {
        _toastText.Text = message;
        _toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static TextBlock HoverLabel(string text, Brush brush, double fontSize = 11.5) => new()
    {
        Text = text,
        Foreground = brush,
        FontFamily = Palette.Font,
        FontSize = fontSize,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border HoverChip(UIElement content, string tip, Action click, bool accent)
    {
        var idleBg = accent ? Palette.Accent : Palette.Elevated;
        var hoverBg = accent ? Palette.AccentHover : Palette.Hover;
        var chip = new Border
        {
            Height = 24,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(6),
            Background = idleBg,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = content,
            SnapsToDevicePixels = true
        };
        chip.MouseEnter += (_, _) => chip.Background = hoverBg;
        chip.MouseLeave += (_, _) => chip.Background = idleBg;
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return chip;
    }
}
