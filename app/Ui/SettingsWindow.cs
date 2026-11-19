using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal sealed class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly Dictionary<CaptureAction, TextBlock> _labels = new();
    private readonly TextBlock _note;
    private CaptureAction? _listening;
    private KeyChordListener? _hook;

    public SettingsWindow(AppState state)
    {
        _state = state;
        Title = Brand.Name + " settings";
        Width = 560;
        Height = 520;
        MinWidth = 480;
        MinHeight = 420;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(Heading("Shortcuts"));
        stack.Children.Add(Hint("Click Change, then press the keys. Esc clears that shortcut."));
        foreach (var action in new[] { CaptureAction.Area, CaptureAction.Window, CaptureAction.FullScreen, CaptureAction.Scrolling, CaptureAction.Ocr })
            stack.Children.Add(Row(action));

        stack.Children.Add(Heading("Behavior"));
        stack.Children.Add(Check("Start with Windows", state.Settings.StartWithWindows, value =>
        {
            state.Settings.StartWithWindows = value;
            state.SaveSettings();
        }));
        stack.Children.Add(Check("Capture flash", state.Settings.CaptureFlash, value =>
        {
            state.Settings.CaptureFlash = value;
            state.SaveSettings();
        }));
        stack.Children.Add(Check("Shutter sound", state.Settings.ShutterSound, value =>
        {
            state.Settings.ShutterSound = value;
            state.SaveSettings();
        }));
        stack.Children.Add(Check("Close the shot when you click outside it", state.Settings.CloseOnClickAway, value =>
        {
            state.Settings.CloseOnClickAway = value;
            state.SaveSettings();
        }));

        _note = new TextBlock
        {
            Text = $"Saved in {SettingsStore.FilePath}. If PrtSc still opens the Windows snipping tool, turn that off under Settings → Accessibility → Keyboard.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        };
        stack.Children.Add(_note);

        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        Closed += (_, _) => StopListening();
    }

    private UIElement Row(CaptureAction action)
    {
        var label = new TextBlock
        {
            Text = _state.Settings.GestureFor(action).Display,
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 180
        };
        _labels[action] = label;
        var button = new Button
        {
            Content = "Change",
            Width = 88,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            Background = Palette.Elevated,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            FontFamily = Palette.Font
        };
        button.Click += (_, _) => BeginListen(action, button);
        return new Grid
        {
            Margin = new Thickness(0, 6, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(160) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                Place(new TextBlock
                {
                    Text = NameOf(action),
                    Foreground = Palette.Text,
                    FontFamily = Palette.Font,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                }, 0),
                Place(label, 1),
                Place(button, 2)
            }
        };
    }

    private void BeginListen(CaptureAction action, Button button)
    {
        StopListening();
        _listening = action;
        button.Content = "Press keys…";
        _state.Hotkeys.Unregister();
        _hook = new KeyChordListener();
        _hook.Captured += gesture => Dispatcher.Invoke(() => ApplyGesture(action, gesture, button));
        _hook.Start();
    }

    private void ApplyGesture(CaptureAction action, HotkeyGesture gesture, Button button)
    {
        StopListening();
        button.Content = "Change";
        var conflict = _state.Settings.Gestures()
            .FirstOrDefault(pair => pair.Action != action && !gesture.IsEmpty && pair.Gesture.EqualsGesture(gesture));
        if (conflict.Gesture != null)
        {
            _note.Text = $"That shortcut is already used by {NameOf(conflict.Action)}.";
            _note.Foreground = Palette.Danger;
            _state.Hotkeys.RegisterAll();
            return;
        }
        _state.Settings.SetGesture(action, gesture);
        _labels[action].Text = gesture.Display;
        _note.Foreground = Palette.Muted;
        _note.Text = "Shortcut saved.";
        _state.SaveSettings();
    }

    private void StopListening()
    {
        _hook?.Dispose();
        _hook = null;
        _listening = null;
    }

    private static UIElement Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox
        {
            Content = text,
            IsChecked = value,
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0)
        };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        Foreground = Palette.Text,
        FontFamily = Palette.Font,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4)
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Palette.Muted,
        FontFamily = Palette.Font,
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static UIElement Place(UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        return element;
    }

    private static string NameOf(CaptureAction action) => action switch
    {
        CaptureAction.Area => "Area",
        CaptureAction.Window => "Window",
        CaptureAction.FullScreen => "Full screen",
        CaptureAction.Scrolling => "Scrolling capture",
        CaptureAction.Ocr => "OCR",
        _ => action.ToString()
    };
}
