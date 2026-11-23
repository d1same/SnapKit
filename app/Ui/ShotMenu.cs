using System.Windows;
using System.Windows.Threading;

namespace SnapKit;

internal static class ShotMenu
{
    public static void Attach(FrameworkElement host, ShotSurface surface, EditorSession session, Window owner)
    {
        var menu = DarkMenu.Create();
        menu.Items.Add(DarkMenu.Item("Copy", () =>
        {
            surface.CommitEditing();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(session.Copy));
        }, "Ctrl+C"));
        menu.Items.Add(DarkMenu.Item("Quick save", () =>
        {
            surface.CommitEditing();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => session.QuickSave()));
        }, "Ctrl+Shift+S"));
        menu.Items.Add(DarkMenu.Item("Save as…", () =>
        {
            surface.CommitEditing();
            session.Save(owner);
        }, "Ctrl+S"));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Duplicate selected", () =>
        {
            surface.CommitEditing();
            if (surface.DuplicateSelection())
                session.Flash("Duplicated selection.");
        }, "Ctrl+D"));
        menu.Items.Add(DarkMenu.Item("Clear all marks", () =>
        {
            surface.CommitEditing();
            session.ClearAll();
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
}
