using System.Windows;

namespace SnapKit;

internal static class ShotMenu
{
    public static void Attach(FrameworkElement host, ShotSurface surface, EditorSession session, Window owner)
    {
        var menu = DarkMenu.Create();
        menu.Items.Add(DarkMenu.Item("Copy", () => Copy(surface, session)));
        menu.Items.Add(DarkMenu.Item("Save as…", () =>
        {
            surface.CommitEditing();
            session.Save(owner);
        }));
        IDisposable? pause = null;
        menu.Opened += (_, _) =>
        {
            pause?.Dispose();
            pause = ClickAway.Pause(owner);
        };
        menu.Closed += (_, _) =>
        {
            pause?.Dispose();
            pause = null;
        };
        host.ContextMenu = menu;
    }

    private static void Copy(ShotSurface surface, EditorSession session)
    {
        surface.CommitEditing();
        session.Copy();
    }
}
