using System.Drawing;
using System.Windows.Forms;

namespace SnapKit;

internal sealed class TrayHost : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _repeatLast;
    private readonly ToolStripMenuItem _recent;
    private readonly ToolStripMenuItem _updateItem;
    private Icon _currentIcon;
    private bool _checkingUpdate;

    public TrayHost(AppState state)
    {
        _state = state;
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false
        };
        _menu.Opening += (_, _) => RebuildDynamicItems();

        Add("Area", () => state.Coordinator.Start(CaptureAction.Area));
        _repeatLast = Item("Repeat last area", () => state.Coordinator.StartRepeatLastArea(), withDisabled: true);
        _menu.Items.Add(_repeatLast);
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
        _updateItem = Item("Check for updates", OnUpdateClicked);
        _menu.Items.Add(_updateItem);
        Add("Settings / Shortcuts", ShowSettings);
        Add("Exit", Failsafe.Kill);

        _currentIcon = IconFactory.Load();
        _icon = new NotifyIcon
        {
            Icon = _currentIcon,
            Visible = true,
            Text = ClampTooltip(Brand.Name + " " + ReleaseClient.Display),
            ContextMenuStrip = _menu
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                state.Coordinator.Start(CaptureAction.Area);
        };
        UpdateCheck.UpdateAvailable += OnUpdateAvailable;
        if (UpdateCheck.Available is { } existing)
            OnUpdateAvailable(existing);
        TrayPromoter.KeepBesideClock(RefreshIcon);
    }

    private void OnUpdateAvailable(ReleaseInfo info)
    {
        try
        {
            string ver = info.Tag.TrimStart('v', 'V');
            _updateItem.Text = $"Update available (v{ver}) ●";
            _updateItem.ForeColor = Color.FromArgb(10, 132, 255);
            _icon.Text = ClampTooltip($"{Brand.Name} {ReleaseClient.Display} • Update v{ver} ready");

            var badgeIcon = IconFactory.LoadWithUpdateDot();
            var previous = _currentIcon;
            _currentIcon = badgeIcon;
            _icon.Icon = badgeIcon;
            previous.Dispose();
        }
        catch
        {
            // Tray icon may already be disposed during shutdown.
        }
    }

    private async void OnUpdateClicked()
    {
        if (UpdateCheck.Available is { } ready)
        {
            UpdateWindow.Offer(ready);
            return;
        }
        if (_checkingUpdate)
            return;
        _checkingUpdate = true;
        _updateItem.Text = "Checking for updates…";
        _updateItem.Enabled = false;
        try
        {
            var info = await UpdateCheck.CheckAsync();
            if (info != null && ReleaseClient.IsNewer(info.Version))
            {
                OnUpdateAvailable(info);
                UpdateWindow.Offer(info);
            }
            else
            {
                _updateItem.Text = "Check for updates";
                Balloon(Brand.Name, $"{Brand.Name} v{ReleaseClient.Display} is up to date.");
            }
        }
        catch (Exception ex)
        {
            _updateItem.Text = "Check for updates";
            Balloon(Brand.Name, "Update check failed: " + ex.Message);
        }
        finally
        {
            _checkingUpdate = false;
            _updateItem.Enabled = true;
        }
    }

    private void RefreshIcon()
    {
        try
        {
            _icon.Visible = false;
            _icon.Visible = true;
        }
        catch
        {
            // The icon can already be gone if the app is exiting.
        }
    }

    public void Balloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text.Length > 240 ? text[..240] : text;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        UpdateCheck.UpdateAvailable -= OnUpdateAvailable;
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon.Dispose();
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

    private void RebuildDynamicItems()
    {
        var last = _state.Settings.LastArea;
        if (last != null && last.IsValid)
        {
            _repeatLast.Enabled = true;
            _repeatLast.Text = $"Repeat last area ({last.Width}×{last.Height})";
        }
        else
        {
            _repeatLast.Enabled = false;
            _repeatLast.Text = "Repeat last area";
        }

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

    private static string ClampTooltip(string text) => text.Length > 63 ? text[..63] : text;

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
            if (!e.Item.Enabled)
                e.TextColor = Color.FromArgb(120, 125, 140);
            else if (e.Item.ForeColor != Color.White && e.Item.ForeColor != Color.Empty)
                e.TextColor = e.Item.ForeColor;
            else
                e.TextColor = Color.FromArgb(244, 245, 247);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(46, 50, 62);
        public override Color MenuItemBorder => Color.FromArgb(60, 66, 82);
        public override Color ToolStripDropDownBackground => Color.FromArgb(24, 26, 32);
        public override Color ImageMarginGradientBegin => Color.FromArgb(24, 26, 32);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 26, 32);
        public override Color ImageMarginGradientEnd => Color.FromArgb(24, 26, 32);
        public override Color MenuBorder => Color.FromArgb(51, 56, 69);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(46, 50, 62);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(46, 50, 62);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(56, 62, 76);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(56, 62, 76);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(56, 62, 76);
        public override Color SeparatorDark => Color.FromArgb(42, 46, 58);
        public override Color SeparatorLight => Color.FromArgb(42, 46, 58);
    }
}
