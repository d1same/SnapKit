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

        // ── Share & Save ───────────────────────────────────────────────────────
        menu.Items.Add(DarkMenu.SectionHeader("Share & Save"));
        menu.Items.Add(DarkMenu.Item("Copy image", () =>
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
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => session.Save(owner)));
        }, "Ctrl+S"));

        // ── Smart Tools ────────────────────────────────────────────────────────
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.SectionHeader("Smart Tools"));
        menu.Items.Add(DarkMenu.Item("Copy text / table (OCR)", () =>
        {
            surface.CommitEditing();
            onOcr();
        }));
        menu.Items.Add(DarkMenu.Item("Auto-blur sensitive info", () =>
        {
            surface.CommitEditing();
            session.AutoBlurSensitive(useRedact: false);
        }));
        menu.Items.Add(DarkMenu.Item("Pin floating on top", () =>
        {
            surface.CommitEditing();
            onPin();
        }));

        // ── Appearance ─────────────────────────────────────────────────────────
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.SectionHeader("Appearance"));

        var frameMenu = DarkMenu.Submenu("Frame", "Off");
        var frameOff = DarkMenu.Item("Off", () => session.SetBackdrop(BackdropPreset.None));
        var frameClean = DarkMenu.Item("Clean (Light)", () => session.SetBackdrop(BackdropPreset.Clean));
        var frameDark = DarkMenu.Item("Dark", () => session.SetBackdrop(BackdropPreset.Dark));
        var frameSlate = DarkMenu.Item("Slate", () => session.SetBackdrop(BackdropPreset.Slate));
        frameMenu.Items.Add(frameOff);
        frameMenu.Items.Add(frameClean);
        frameMenu.Items.Add(frameDark);
        frameMenu.Items.Add(frameSlate);
        menu.Items.Add(frameMenu);

        var stampItem = DarkMenu.Item("Ticket proof stamp", () =>
        {
            session.SetStamp(!session.Document.ShowStamp);
        }, "Off");
        menu.Items.Add(stampItem);

        // ── Cleanup ────────────────────────────────────────────────────────────
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Clear all marks", () =>
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

            var cur = session.Document.Backdrop;
            string frameState = cur == BackdropPreset.None ? "Off" : cur.ToString();
            DarkMenu.SetLabel(frameMenu, "Frame", frameState);
            DarkMenu.SetLabel(frameOff, "Off", cur == BackdropPreset.None ? "✓" : null);
            DarkMenu.SetLabel(frameClean, "Clean (Light)", cur == BackdropPreset.Clean ? "✓" : null);
            DarkMenu.SetLabel(frameDark, "Dark", cur == BackdropPreset.Dark ? "✓" : null);
            DarkMenu.SetLabel(frameSlate, "Slate", cur == BackdropPreset.Slate ? "✓" : null);

            DarkMenu.SetLabel(stampItem, "Ticket proof stamp", session.Document.ShowStamp ? "On" : "Off");
        };
        menu.Closed += (_, _) =>
        {
            pause?.Dispose();
            pause = null;
        };
        host.ContextMenu = menu;
    }
}
