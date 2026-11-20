using System.Windows.Input;

namespace SnapKit;

internal static class EditorKeys
{
    public static bool Handle(KeyEventArgs e, EditorSession session, ShotSurface surface, Action close, Action? fit)
    {
        if (surface.IsEditingText)
            return false;

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        bool ctrlShift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if (ctrl && e.Key == Key.C)
        {
            surface.CommitEditing();
            session.Copy();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key == Key.S)
        {
            session.Save(surface.IsVisible ? WindowOf(surface) : System.Windows.Application.Current.MainWindow!);
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
        if (e.Key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            surface.DeleteSelection();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            close();
            e.Handled = true;
            return true;
        }
        if (ctrl && e.Key is Key.OemPlus or Key.Add && fit != null)
        {
            fit();
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers != ModifierKeys.None)
            return false;

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
            Key.S => EditorTool.Spotlight,
            Key.X => EditorTool.Crop,
            Key.I => EditorTool.Picker,
            Key.OemOpenBrackets => null,
            _ => null
        };
        if (e.Key == Key.OemOpenBrackets)
        {
            session.SetStroke(session.Stroke - 1);
            surface.StrokeWidth = session.Stroke;
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.OemCloseBrackets)
        {
            session.SetStroke(session.Stroke + 1);
            surface.StrokeWidth = session.Stroke;
            e.Handled = true;
            return true;
        }
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
