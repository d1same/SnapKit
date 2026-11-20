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
        }));
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

}
