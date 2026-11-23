using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SnapKit;

internal static class ShotMenu
{
    public static void Attach(
        FrameworkElement host,
        ShotSurface surface,
        EditorSession session,
        Window owner,
        Action onOcr,
        Action onPin,
        Action onClose)
    {
        var menu = DarkMenu.Create();
        menu.Items.Add(DarkMenu.Item("Copy", () =>
        {
            surface.CommitEditing();
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(session.Copy));
        }, "Ctrl+C"));
        menu.Items.Add(DarkMenu.Item("Quick Save", () =>
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
        menu.Items.Add(DarkMenu.Item("Copy Text (OCR)", () =>
        {
            surface.CommitEditing();
            onOcr();
        }));
        menu.Items.Add(DarkMenu.Item("Pin", () =>
        {
            surface.CommitEditing();
            onPin();
        }));
        var toggleFrame = DarkMenu.Item("Toggle Frame", () =>
        {
            var next = session.Document.Backdrop switch
            {
                BackdropPreset.None => BackdropPreset.Clean,
                BackdropPreset.Clean => BackdropPreset.Dark,
                BackdropPreset.Dark => BackdropPreset.Slate,
                _ => BackdropPreset.None
            };
            session.SetBackdrop(next);
        });
        menu.Items.Add(toggleFrame);
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Clear Marks", () =>
        {
            surface.CommitEditing();
            session.ClearAll();
        }));
        menu.Items.Add(DarkMenu.Item("Close", onClose, "Esc"));

        IDisposable? pause = null;
        menu.Opened += (_, _) =>
        {
            pause?.Dispose();
            pause = ClickAway.Pause(owner);
            string frameState = session.Document.Backdrop == BackdropPreset.None
                ? "Off"
                : session.Document.Backdrop.ToString();
            DarkMenu.SetLabel(toggleFrame, "Toggle Frame", frameState);
        };
        menu.Closed += (_, _) =>
        {
            pause?.Dispose();
            pause = null;
        };
        host.ContextMenu = menu;
    }
}
