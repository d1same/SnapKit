using System.Windows;
using System.Windows.Controls;

namespace SnapKit;

internal static class ShotMenu
{
    public static void Attach(FrameworkElement host, ShotSurface surface, EditorSession session, Window owner)
    {
        var menu = new ContextMenu
        {
            Background = Palette.Card,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1)
        };
        menu.Items.Add(Item("Copy", () => Copy(surface, session)));
        menu.Items.Add(Item("Save as…", () =>
        {
            surface.CommitEditing();
            session.Save(owner);
        }));
        host.ContextMenu = menu;
        host.ContextMenuOpening += (_, _) => Copy(surface, session);
    }

    private static void Copy(ShotSurface surface, EditorSession session)
    {
        surface.CommitEditing();
        session.Copy();
    }

    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Background = Palette.Card,
            Foreground = Palette.Text,
            FontFamily = Palette.Font
        };
        item.Click += (_, _) => action();
        return item;
    }
}
