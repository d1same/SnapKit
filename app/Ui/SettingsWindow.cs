using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SnapKit;

internal sealed class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly Dictionary<CaptureAction, TextBlock> _labels = new();
    private readonly TextBlock _note;
    private readonly TextBlock _quickFolder;
    private readonly TextBlock _updateStatus;
    private CaptureAction? _listening;
    private KeyChordListener? _hook;

    public SettingsWindow(AppState state)
    {
        _state = state;
        Title = Brand.Name + " Settings";
        Width = 600;
        Height = 680;
        MinWidth = 520;
        MinHeight = 460;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 22) };

        // Header
        var header = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock
        {
            Text = Brand.Name + " Settings",
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var versionPill = UiChrome.Pill("v" + ReleaseClient.Display, Palette.Elevated, Palette.Text, Palette.Line, mono: true);
        versionPill.Margin = new Thickness(10, 2, 0, 0);
        header.Children.Add(versionPill);
        stack.Children.Add(header);

        // Shortcuts Card
        stack.Children.Add(UiChrome.SectionLabel("Global shortcuts"));
        var shortcutsContent = new StackPanel();
        shortcutsContent.Children.Add(new TextBlock
        {
            Text = "Click Change, then press your shortcut. Press Esc to clear it.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 8)
        });
        var actions = new[]
        {
            CaptureAction.Area,
            CaptureAction.Window,
            CaptureAction.FullScreen,
            CaptureAction.Scrolling,
            CaptureAction.Ocr,
            CaptureAction.RepeatLastArea
        };
        for (int i = 0; i < actions.Length; i++)
        {
            if (i > 0)
                shortcutsContent.Children.Add(Hairline());
            shortcutsContent.Children.Add(ShortcutRow(actions[i]));
        }
        stack.Children.Add(UiChrome.Card(shortcutsContent, new Thickness(14, 10, 14, 10)));

        // Behavior Card
        stack.Children.Add(UiChrome.SectionLabel("Behavior"));
        var behaviorContent = new StackPanel();
        behaviorContent.Children.Add(UiChrome.ToggleRow(
            "Start with Windows",
            "Keep SnapKit ready in the system tray when you sign in.",
            state.Settings.StartWithWindows,
            value =>
            {
                state.Settings.StartWithWindows = value;
                state.SaveSettings();
            }));
        behaviorContent.Children.Add(Hairline());
        behaviorContent.Children.Add(UiChrome.ToggleRow(
            "Capture flash",
            "Brief white flash when grabbing the screen.",
            state.Settings.CaptureFlash,
            value =>
            {
                state.Settings.CaptureFlash = value;
                state.SaveSettings();
            }));
        behaviorContent.Children.Add(Hairline());
        behaviorContent.Children.Add(UiChrome.ToggleRow(
            "Shutter sound",
            "Play a soft camera click on capture.",
            state.Settings.ShutterSound,
            value =>
            {
                state.Settings.ShutterSound = value;
                state.SaveSettings();
            }));
        behaviorContent.Children.Add(Hairline());
        behaviorContent.Children.Add(UiChrome.ToggleRow(
            "Close shot on click-away",
            "Clicking outside an active screenshot closes it automatically.",
            state.Settings.CloseOnClickAway,
            value =>
            {
                state.Settings.CloseOnClickAway = value;
                state.SaveSettings();
            }));
        stack.Children.Add(UiChrome.Card(behaviorContent, new Thickness(10, 6, 10, 6)));

        // Quick Save Card
        stack.Children.Add(UiChrome.SectionLabel("Quick save (Ctrl+Shift+S)"));
        _quickFolder = new TextBlock
        {
            Text = ImageFile.ResolveQuickSaveFolder(state.Settings.QuickSaveFolder),
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var chooseFolder = UiChrome.ActionButton("Choose…", PickQuickSaveFolder, ButtonTone.Secondary, minWidth: 84);
        var resetFolder = UiChrome.ActionButton("Reset", ResetQuickSaveFolder, ButtonTone.Ghost, minWidth: 64);
        resetFolder.Margin = new Thickness(6, 0, 0, 0);
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_quickFolder, 0);
        Grid.SetColumn(chooseFolder, 1);
        Grid.SetColumn(resetFolder, 2);
        folderRow.Children.Add(_quickFolder);
        folderRow.Children.Add(chooseFolder);
        folderRow.Children.Add(resetFolder);

        var quickSaveContent = new StackPanel();
        quickSaveContent.Children.Add(new TextBlock
        {
            Text = "Instant PNG save location without opening a file dialog.",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        });
        quickSaveContent.Children.Add(folderRow);
        stack.Children.Add(UiChrome.Card(quickSaveContent));

        // Updates Card
        stack.Children.Add(UiChrome.SectionLabel("Updates"));
        _updateStatus = new TextBlock
        {
            Text = UpdateCheck.Available is { } available
                ? $"Update {available.Tag} is available."
                : $"Running {Brand.Name} v{ReleaseClient.Display} from {ReleaseClient.Owner}/{ReleaseClient.Repo}.",
            Foreground = UpdateCheck.Available != null ? Palette.Text : Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var check = UiChrome.CreateButton(
            UpdateCheck.Available != null ? "View update" : "Check for updates",
            UpdateCheck.Available != null ? ButtonTone.Primary : ButtonTone.Secondary,
            minWidth: 132);
        check.Click += async (_, _) =>
        {
            if (UpdateCheck.Available is { } pending)
            {
                UpdateWindow.Offer(pending);
                return;
            }
            check.IsEnabled = false;
            check.Content = "Checking…";
            _updateStatus.Foreground = Palette.Muted;
            _updateStatus.Text = "Checking GitHub for the latest release…";
            string message = await UpdateWindow.CheckNow(this);
            _updateStatus.Text = message;
            _updateStatus.Foreground = UpdateCheck.Available != null ? Palette.Text : Palette.Muted;
            check.Content = UpdateCheck.Available != null ? "View update" : "Check for updates";
            check.IsEnabled = true;
        };
        var updateRow = new Grid();
        updateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        updateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_updateStatus, 0);
        Grid.SetColumn(check, 1);
        updateRow.Children.Add(_updateStatus);
        updateRow.Children.Add(check);
        stack.Children.Add(UiChrome.Card(updateRow));

        _note = new TextBlock
        {
            Text = $"Settings saved in {SettingsStore.FilePath}. If PrtSc still opens Windows Snipping Tool, disable it in Settings → Accessibility → Keyboard.",
            Foreground = Palette.Subtle,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 14, 2, 0)
        };
        stack.Children.Add(_note);

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Palette.Bg
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _listening == null)
            {
                Close();
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        Closed += (_, _) => StopListening();
    }

    private void PickQuickSaveFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose Quick Save folder",
            InitialDirectory = ImageFile.ResolveQuickSaveFolder(_state.Settings.QuickSaveFolder)
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;
        _state.Settings.QuickSaveFolder = dialog.FolderName;
        _quickFolder.Text = dialog.FolderName;
        _state.SaveSettings();
        _note.Foreground = Palette.Muted;
        _note.Text = "Quick Save folder updated.";
    }

    private void ResetQuickSaveFolder()
    {
        _state.Settings.QuickSaveFolder = "";
        _quickFolder.Text = ImageFile.DefaultQuickSaveFolder;
        _state.SaveSettings();
        _note.Foreground = Palette.Muted;
        _note.Text = "Quick Save folder reset to default.";
    }

    private UIElement ShortcutRow(CaptureAction action)
    {
        var label = new TextBlock
        {
            Text = _state.Settings.GestureFor(action).Display,
            Foreground = Palette.Text,
            FontFamily = Palette.Mono,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _labels[action] = label;

        var keycap = new Border
        {
            Background = Palette.Field,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 9, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = label
        };

        var button = UiChrome.CreateButton("Change", ButtonTone.Secondary, minWidth: 88);
        button.Padding = new Thickness(10, 5, 10, 5);
        button.FontSize = 12;
        button.Click += (_, _) => BeginListen(action, button);

        var grid = new Grid { Margin = new Thickness(2, 6, 2, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = NameOf(action),
            Foreground = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(keycap, 1);
        Grid.SetColumn(button, 2);
        grid.Children.Add(name);
        grid.Children.Add(keycap);
        grid.Children.Add(button);
        return grid;
    }

    private void BeginListen(CaptureAction action, Button button)
    {
        StopListening();
        _listening = action;
        button.Content = "Press keys…";
        button.Background = Palette.ActiveSurface;
        button.Foreground = Palette.Ink;
        _state.Hotkeys.Unregister();
        _hook = new KeyChordListener();
        _hook.Captured += gesture => Dispatcher.Invoke(() => ApplyGesture(action, gesture, button));
        _hook.Start();
    }

    private void ApplyGesture(CaptureAction action, HotkeyGesture gesture, Button button)
    {
        StopListening();
        button.Content = "Change";
        button.Background = Palette.Elevated;
        button.Foreground = Palette.Text;
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
        var failed = _state.SaveSettings();
        if (failed.Count > 0)
        {
            _note.Foreground = Palette.Danger;
            _note.Text = "Couldn't register " + string.Join(", ", failed) + ". Another app may already own that key.";
            return;
        }
        _note.Foreground = Palette.Text;
        _note.Text = "Shortcut saved.";
    }

    private void StopListening()
    {
        _hook?.Dispose();
        _hook = null;
        _listening = null;
    }

    private static Border Hairline() => new()
    {
        Height = 1,
        Background = Palette.Divider,
        Margin = new Thickness(4, 2, 4, 2)
    };

    private static string NameOf(CaptureAction action) => action switch
    {
        CaptureAction.Area => "Area",
        CaptureAction.Window => "Window",
        CaptureAction.FullScreen => "Full screen",
        CaptureAction.Scrolling => "Scrolling capture",
        CaptureAction.Ocr => "OCR",
        CaptureAction.RepeatLastArea => "Repeat last area",
        _ => action.ToString()
    };
}
