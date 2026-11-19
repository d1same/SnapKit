using System.Drawing;
using System.Windows.Forms;

namespace SnapKit;

internal sealed class TrayHost : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _recent;

    public TrayHost(AppState state)
    {
        _state = state;
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false
        };
        _menu.Opening += (_, _) => RebuildRecent();

        Add("Area", () => state.Coordinator.Start(CaptureAction.Area));
        Add("Window", () => state.Coordinator.Start(CaptureAction.Window));
        Add("Full screen", () => state.Coordinator.Start(CaptureAction.FullScreen));
        Add("All monitors", () => state.Coordinator.StartAllMonitors());
        Add("Scrolling capture", () => state.Coordinator.Start(CaptureAction.Scrolling));
        var delayed = new ToolStripMenuItem("Delayed capture") { ForeColor = Color.White };
        delayed.DropDown.Renderer = new DarkMenuRenderer();
        delayed.DropDownItems.Add(Item("3 seconds", () => state.Coordinator.StartDelayed(3)));
        delayed.DropDownItems.Add(Item("5 seconds", () => state.Coordinator.StartDelayed(5)));
        _menu.Items.Add(delayed);
        Add("OCR", () => state.Coordinator.Start(CaptureAction.Ocr));
        _menu.Items.Add(new ToolStripSeparator());
        _recent = new ToolStripMenuItem("Recent captures") { ForeColor = Color.White };
        _recent.DropDown.Renderer = new DarkMenuRenderer();
        _menu.Items.Add(_recent);
        _menu.Items.Add(new ToolStripSeparator());
        Add("Settings / Shortcuts", ShowSettings);
        Add("Exit", Failsafe.Kill);

        _icon = new NotifyIcon
        {
            Icon = IconFactory.Load(),
            Visible = true,
            Text = Brand.Name + " — click to capture",
            ContextMenuStrip = _menu
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                state.Coordinator.Start(CaptureAction.Area);
        };
    }

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text.Length > 240 ? text[..240] : text;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private void ShowSettings()
    {
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window is SettingsWindow settings)
            {
                settings.Activate();
                return;
            }
        }
        new SettingsWindow(_state).Show();
    }

    private void RebuildRecent()
    {
        _recent.DropDownItems.Clear();
        if (_state.Recent.Items.Count == 0)
        {
            _recent.DropDownItems.Add(Item("Nothing captured yet", () => { }, withDisabled: true));
            return;
        }
        foreach (var capture in _state.Recent.Items)
        {
            var item = capture;
            _recent.DropDownItems.Add(Item(item.Label, () => EditorWindow.Open(item.Image, item, _state)));
        }
        _recent.DropDownItems.Add(new ToolStripSeparator());
        _recent.DropDownItems.Add(Item("Clear", () => _state.Recent.Clear()));
    }

    private void Add(string text, Action action) => _menu.Items.Add(Item(text, action));

    private static ToolStripMenuItem Item(string text, Action action, bool withDisabled = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.White,
            Enabled = !withDisabled
        };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(244, 244, 245);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(46, 46, 46);
        public override Color MenuItemBorder => Color.FromArgb(58, 58, 58);
        public override Color ToolStripDropDownBackground => Color.FromArgb(22, 22, 22);
        public override Color ImageMarginGradientBegin => Color.FromArgb(22, 22, 22);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(22, 22, 22);
        public override Color ImageMarginGradientEnd => Color.FromArgb(22, 22, 22);
        public override Color MenuBorder => Color.FromArgb(42, 42, 42);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(46, 46, 46);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(46, 46, 46);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(58, 58, 58);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(58, 58, 58);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(58, 58, 58);
        public override Color SeparatorDark => Color.FromArgb(42, 42, 42);
        public override Color SeparatorLight => Color.FromArgb(42, 42, 42);
    }
}
