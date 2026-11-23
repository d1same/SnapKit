using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapKit;

internal sealed class TrayHost : IDisposable
{
    private static readonly Font MenuFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private static readonly Font MenuFontBold = new("Segoe UI", 9.5f, FontStyle.Bold);

    private readonly AppState _state;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _areaItem;
    private readonly ToolStripMenuItem _repeatLast;
    private readonly ToolStripMenuItem _windowItem;
    private readonly ToolStripMenuItem _fullScreenItem;
    private readonly ToolStripMenuItem _scrollingItem;
    private readonly ToolStripMenuItem _ocrItem;
    private readonly ToolStripMenuItem _recordVideoItem;
    private readonly ToolStripMenuItem _recordGifItem;
    private readonly ToolStripMenuItem _recent;
    private readonly ToolStripMenuItem _updateItem;
    private Icon _currentIcon;
    private bool _checkingUpdate;

    public TrayHost(AppState state)
    {
        _state = state;
        _menu = CreatePopupMenu();
        _menu.Opening += (_, _) => RebuildDynamicItems();

        _areaItem = Add("Area", () => state.Coordinator.Start(CaptureAction.Area));
        _repeatLast = Item("Repeat last area", () => state.Coordinator.StartRepeatLastArea(), withDisabled: true);
        _menu.Items.Add(_repeatLast);
        _windowItem = Add("Window", () => state.Coordinator.Start(CaptureAction.Window));
        _fullScreenItem = Add("Full screen", () => state.Coordinator.Start(CaptureAction.FullScreen));
        Add("All monitors", () => state.Coordinator.StartAllMonitors());
        _scrollingItem = Add("Scrolling capture", () => state.Coordinator.Start(CaptureAction.Scrolling));

        var delayed = SubmenuItem("Delayed capture");
        delayed.DropDownItems.Add(Item("3 seconds", () => state.Coordinator.StartDelayed(3)));
        delayed.DropDownItems.Add(Item("5 seconds", () => state.Coordinator.StartDelayed(5)));
        delayed.DropDownItems.Add(Item("10 seconds", () => state.Coordinator.StartDelayed(10)));
        _menu.Items.Add(delayed);

        _ocrItem = Add("OCR", () => state.Coordinator.Start(CaptureAction.Ocr));
        _menu.Items.Add(new ToolStripSeparator());
        _recordVideoItem = Add("Record video (MP4)", () => state.Coordinator.Start(CaptureAction.RecordVideo));
        _recordGifItem = Add("Record GIF", () => state.Coordinator.Start(CaptureAction.RecordGif));
        _menu.Items.Add(new ToolStripSeparator());

        _recent = SubmenuItem("Recent captures");
        _menu.Items.Add(_recent);
        _menu.Items.Add(new ToolStripSeparator());

        _updateItem = Item("Check for updates…", OnUpdateClicked);
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
            _updateItem.Text = $"Update available (v{ver})  ●";
            _updateItem.Font = MenuFontBold;
            _updateItem.ForeColor = Color.FromArgb(242, 242, 242);
            _icon.Text = ClampTooltip($"{Brand.Name} {ReleaseClient.Display} · v{ver} ready");

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
                _updateItem.Text = "Check for updates…";
                Balloon(Brand.Name, $"{Brand.Name} v{ReleaseClient.Display} is up to date.");
            }
        }
        catch (Exception ex)
        {
            _updateItem.Text = "Check for updates…";
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
        ApplyShortcut(_areaItem, CaptureAction.Area);
        ApplyShortcut(_repeatLast, CaptureAction.RepeatLastArea);
        ApplyShortcut(_windowItem, CaptureAction.Window);
        ApplyShortcut(_fullScreenItem, CaptureAction.FullScreen);
        ApplyShortcut(_scrollingItem, CaptureAction.Scrolling);
        ApplyShortcut(_ocrItem, CaptureAction.Ocr);
        ApplyShortcut(_recordVideoItem, CaptureAction.RecordVideo);
        ApplyShortcut(_recordGifItem, CaptureAction.RecordGif);

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

    private void ApplyShortcut(ToolStripMenuItem item, CaptureAction action)
    {
        var gesture = _state.Settings.GestureFor(action);
        item.ShortcutKeyDisplayString = gesture.IsEmpty ? "" : gesture.Display;
    }

    private static ContextMenuStrip CreatePopupMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = MenuFont,
            BackColor = Color.FromArgb(34, 34, 34),
            ForeColor = Color.FromArgb(242, 242, 242),
            Padding = new Padding(5, 6, 5, 6)
        };
        AttachRoundedChrome(menu);
        return menu;
    }

    private static ToolStripMenuItem SubmenuItem(string text)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(242, 242, 242),
            Font = MenuFont,
            Padding = new Padding(4, 5, 6, 5)
        };
        item.DropDown.Renderer = new DarkMenuRenderer();
        item.DropDown.BackColor = Color.FromArgb(34, 34, 34);
        item.DropDown.ForeColor = Color.FromArgb(242, 242, 242);
        item.DropDown.Font = MenuFont;
        item.DropDown.Padding = new Padding(5, 6, 5, 6);
        if (item.DropDown is ToolStripDropDownMenu dropMenu)
            dropMenu.ShowImageMargin = false;
        AttachRoundedChrome(item.DropDown);
        return item;
    }

    private static void AttachRoundedChrome(ToolStripDropDown dropDown)
    {
        dropDown.HandleCreated += (_, _) => WindowChrome.UseDarkPopup(dropDown.Handle);
        dropDown.Opening += (_, _) =>
        {
            if (dropDown.IsHandleCreated)
                WindowChrome.UseDarkPopup(dropDown.Handle);
        };
    }

    private static string ClampTooltip(string text) => text.Length > 63 ? text[..63] : text;

    private ToolStripMenuItem Add(string text, Action action)
    {
        var item = Item(text, action);
        _menu.Items.Add(item);
        return item;
    }

    private static ToolStripMenuItem Item(string text, Action action, bool withDisabled = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(242, 242, 242),
            Font = MenuFont,
            Padding = new Padding(4, 5, 6, 5),
            Enabled = !withDisabled
        };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Surface = Color.FromArgb(34, 34, 34);
        private static readonly Color BorderColor = Color.FromArgb(46, 46, 46);
        private static readonly Color HoverFill = Color.FromArgb(50, 50, 50);
        private static readonly Color HoverBorder = Color.FromArgb(62, 62, 62);
        private static readonly Color TextPrimary = Color.FromArgb(242, 242, 242);
        private static readonly Color TextMuted = Color.FromArgb(138, 138, 138);
        private static readonly Color TextDisabled = Color.FromArgb(102, 102, 102);

        public DarkMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(BorderColor, 1);
            var rect = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var g = e.Graphics;
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(2, 1, Math.Max(4, e.Item.Width - 5), Math.Max(4, e.Item.Height - 3));
            using var path = RoundedRect(rect, 6);
            using var fill = new SolidBrush(HoverFill);
            using var stroke = new Pen(HoverBorder, 1);
            g.FillPath(fill, path);
            g.DrawPath(stroke, path);
            g.SmoothingMode = prev;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(BorderColor, 1);
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var g = e.Graphics;
            var prev = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color color = e.Item is { Enabled: false } ? TextDisabled : TextPrimary;
            int cx = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2;
            int cy = e.ArrowRectangle.Top + e.ArrowRectangle.Height / 2;
            using var pen = new Pen(color, 1.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(cx - 2.2f, cy - 3.6f),
                new PointF(cx + 1.8f, cy),
                new PointF(cx - 2.2f, cy + 3.6f)
            });
            g.SmoothingMode = prev;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool isShortcut = e.Item is ToolStripMenuItem mi
                && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
                && string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal);

            if (!e.Item.Enabled)
                e.TextColor = TextDisabled;
            else if (isShortcut)
                e.TextColor = TextMuted;
            else
                e.TextColor = TextPrimary;

            base.OnRenderItemText(e);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(34, 34, 34);
        private static readonly Color BorderColor = Color.FromArgb(46, 46, 46);

        public override Color MenuItemSelected => Color.FromArgb(50, 50, 50);
        public override Color MenuItemBorder => Color.FromArgb(62, 62, 62);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 50, 50);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(50, 50, 50);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(58, 58, 58);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(58, 58, 58);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(58, 58, 58);
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => BorderColor;
    }
}
