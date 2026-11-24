using System.Windows.Input;

namespace SnapKit;

internal static class EditorKeys
{
    public static bool Handle(
        KeyEventArgs e,
        EditorSession session,
        ShotSurface surface,
        Action close,
        Action? zoomIn = null,
        Action? zoomOut = null,
        Action? zoomReset = null,
        Action? zoomActual = null)
    {
        if (surface.IsEditingText)
            return false;

        // Avoid hijacking shortcuts while typing in the toolbar hex input box.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            return false;

        var mods = Keyboard.Modifiers;
        bool ctrl = mods == ModifierKeys.Control;
        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);

        if (ctrl && e.Key == Key.C)
        {
            surface.CommitEditing();
            session.Copy();
            e.Handled = true;
            return true;
        }
        if (ctrlShift && e.Key == Key.S)
        {
            surface.CommitEditing();
            session.QuickSave();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key == Key.S)
        {
            surface.CommitEditing();
            var owner = surface.IsVisible ? WindowOf(surface) : System.Windows.Application.Current.MainWindow!;
            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => session.Save(owner)));
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key == Key.D)
        {
            if (surface.DuplicateSelection())
                session.Flash("Duplicated selection.");
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key == Key.Z)
        {
            session.Document.Undo();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key == Key.Y || ctrlShift && e.Key == Key.Z)
        {
            session.Document.Redo();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key is Key.OemPlus or Key.Add && zoomIn != null)
        {
            zoomIn();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key is Key.OemMinus or Key.Subtract && zoomOut != null)
        {
            zoomOut();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key is Key.D0 or Key.NumPad0 && zoomReset != null)
        {
            zoomReset();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key is Key.D1 or Key.NumPad1 && zoomActual != null)
        {
            zoomActual();
            e.Handled = true;
            return true;
        }
        if (e.Key is Key.Delete or Key.Back && mods == ModifierKeys.None)
        {
            surface.DeleteSelection();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Escape && mods == ModifierKeys.None)
        {
            close();
            e.Handled = true;
            return true;
        }

        // Arrow-key nudging for the selected annotation (1 px plain, 10 px with Shift)
        if (surface.HasSelection && (mods == ModifierKeys.None || mods == ModifierKeys.Shift))
        {
            double step = mods == ModifierKeys.Shift ? 10 : 1;
            (double dx, double dy) = e.Key switch
            {
                Key.Left => (-step, 0d),
                Key.Right => (step, 0d),
                Key.Up => (0d, -step),
                Key.Down => (0d, step),
                _ => (0d, 0d)
            };
            if (dx != 0 || dy != 0)
            {
                surface.NudgeSelection(dx, dy);
                e.Handled = true;
                return true;
            }
        }

        if (mods != ModifierKeys.None)
            return false;

        if (e.Key == Key.OemOpenBrackets)
        {
            session.SetStroke(session.Stroke - 1);
            surface.StrokeWidth = session.Stroke;
            surface.RestyleSelection(null, session.Stroke, null);
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.OemCloseBrackets)
        {
            session.SetStroke(session.Stroke + 1);
            surface.StrokeWidth = session.Stroke;
            surface.RestyleSelection(null, session.Stroke, null);
            e.Handled = true;
            return true;
        }

        EditorTool? tool = e.Key switch
        {
            Key.V => EditorTool.Select,
            Key.P => EditorTool.Pen,
            Key.A => EditorTool.Arrow,
            Key.R => EditorTool.Rectangle,
            Key.E => EditorTool.Ellipse,
            Key.H => EditorTool.Highlighter,
            Key.T => EditorTool.Text,
            Key.C => EditorTool.Callout,
            Key.N => EditorTool.Number,
            Key.B => EditorTool.Blur,
            Key.D => EditorTool.Redact,
            Key.S => EditorTool.Spotlight,
            Key.M => EditorTool.Magnifier,
            Key.X => EditorTool.Crop,
            Key.I => EditorTool.Picker,
            _ => null
        };
        if (tool == null)
            return false;
        session.SelectTool(tool.Value);
        surface.Tool = tool.Value;
        e.Handled = true;
        return true;
    }

    private static System.Windows.Window WindowOf(System.Windows.DependencyObject child)
    {
        var window = System.Windows.Window.GetWindow(child);
        return window ?? System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().First();
    }
}
