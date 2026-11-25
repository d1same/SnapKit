using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SnapKit;

/// <summary>
/// Clean Style 3 Step-by-Step Client Guide editor: edit captions, reorder or remove steps,
/// toggle Light/Dark theme, and export directly to Clipboard (as 1 crisp vertical image OR
/// Rich HTML with embedded images for Outlook/PSA tickets) or save as PNG / HTML.
/// </summary>
internal sealed class StepGuideWindow : Window
{
    private readonly List<CapturedStep> _steps;
    private readonly AppState _state;
    private readonly TextBox _titleBox;
    private readonly StackPanel _listHost;
    private readonly TextBlock _status;
    private readonly Border _lightChip;
    private readonly Border _darkChip;
    private bool _darkTheme;

    public StepGuideWindow(List<CapturedStep> steps, AppState state)
    {
        _steps = steps;
        _state = state;
        _darkTheme = false; // Light default pastes cleanly into Outlook, Word, and PSA ticket portals

        Title = Brand.Name + " — Step-by-Step Guide";
        Width = 860;
        Height = 680;
        MinWidth = 640;
        MinHeight = 460;
        Background = Palette.Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        // ── Top Bar: Title + Light/Dark Theme + Export Actions ─────────────────
        _titleBox = new TextBox
        {
            Text = "Step-by-Step Guide",
            Background = Palette.Field,
            Foreground = Palette.Text,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CaretBrush = Palette.Text,
            FontFamily = Palette.Font,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 240,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        _lightChip = ThemeChip("Light", "Clean white layout for emails & ticket replies", () => SetTheme(false));
        _darkChip = ThemeChip("Dark", "Dark graphite layout for docs & Slack", () => SetTheme(true));
        var themeSwitch = new Border
        {
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _lightChip, _darkChip } }
        };

        var copyImgBtn = UiChrome.ActionButton("Copy as Image", CopyAsImage, ButtonTone.Primary, minWidth: 120);
        copyImgBtn.Margin = new Thickness(0, 0, 6, 0);
        copyImgBtn.ToolTip = "Combine all steps into one clean vertical image and copy to clipboard";

        var copyHtmlBtn = UiChrome.ActionButton("Copy for Email / Docs", CopyAsHtml, ButtonTone.Secondary, minWidth: 146);
        copyHtmlBtn.Margin = new Thickness(0, 0, 6, 0);
        copyHtmlBtn.ToolTip = "Copy formatted rich HTML with embedded screenshots — Ctrl+V directly into Outlook, Word, or ticket portals";

        var savePngBtn = UiChrome.ActionButton("Save PNG…", SavePng, ButtonTone.Secondary, minWidth: 88);
        savePngBtn.Margin = new Thickness(0, 0, 6, 0);

        var saveHtmlBtn = UiChrome.ActionButton("Save HTML…", SaveHtml, ButtonTone.Secondary, minWidth: 92);
        saveHtmlBtn.Margin = new Thickness(0, 0, 6, 0);

        var printBtn = UiChrome.ActionButton("Print…", PrintGuide, ButtonTone.Secondary, minWidth: 74);
        printBtn.ToolTip = "Print guide or save via Microsoft Print to PDF  (Ctrl+P)";

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_titleBox, 0);
        Grid.SetColumn(themeSwitch, 1);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Children = { copyImgBtn, copyHtmlBtn, savePngBtn, saveHtmlBtn, printBtn }
        };
        Grid.SetColumn(actions, 2);
        topGrid.Children.Add(_titleBox);
        topGrid.Children.Add(themeSwitch);
        topGrid.Children.Add(actions);

        var header = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12, 16, 12),
            Child = topGrid
        };

        // ── Scrollable Step Cards ──────────────────────────────────────────────
        _listHost = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(18, 14, 18, 18)
        };
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _listHost
        };

        // ── Footer Status Bar ──────────────────────────────────────────────────
        _status = new TextBlock
        {
            Text = $"{_steps.Count} {(_steps.Count == 1 ? "step" : "steps")} captured  ·  Edit any caption, then click Copy as Image or Copy for Email / Docs",
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var footer = new Border
        {
            Background = Palette.Card,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Child = _status
        };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(scroller);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PrintGuide();
                e.Handled = true;
            }
        };

        SourceInitialized += (_, _) => WindowChrome.UseDarkTitle(this);
        SetTheme(false);
        RebuildList();
    }

    private static Border ThemeChip(string label, string tip, Action onClick)
    {
        var tb = new TextBlock
        {
            Text = label,
            Foreground = Palette.Muted,
            FontFamily = Palette.Font,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold
        };
        var b = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = tb
        };
        b.MouseLeftButtonDown += (_, e) => { onClick(); e.Handled = true; };
        return b;
    }

    private void SetTheme(bool dark)
    {
        _darkTheme = dark;
        PaintThemeChip(_lightChip, !dark);
        PaintThemeChip(_darkChip, dark);
    }

    private static void PaintThemeChip(Border chip, bool active)
    {
        chip.Background = active ? Palette.ActiveSurface : Brushes.Transparent;
        if (chip.Child is TextBlock tb)
            tb.Foreground = active ? Palette.Ink : Palette.Muted;
    }

    private void RebuildList()
    {
        _listHost.Children.Clear();
        if (_steps.Count == 0)
        {
            _listHost.Children.Add(new TextBlock
            {
                Text = "All steps were removed.",
                Foreground = Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 13,
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        for (int i = 0; i < _steps.Count; i++)
        {
            int idx = i;
            var step = _steps[i];
            int stepNumber = i + 1;

            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Palette.Brush("#FF3B30"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = stepNumber.ToString(),
                    Foreground = Brushes.White,
                    FontFamily = Palette.Font,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var captionBox = new TextBox
            {
                Text = step.Caption,
                Background = Palette.Field,
                Foreground = Palette.Text,
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                CaretBrush = Palette.Text,
                FontFamily = Palette.Font,
                FontSize = 13,
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            captionBox.TextChanged += (_, _) => step.Caption = captionBox.Text ?? "";

            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (idx > 0)
                controls.Children.Add(MiniAction("↑", "Move step up", () => MoveStep(idx, -1)));
            if (idx < _steps.Count - 1)
                controls.Children.Add(MiniAction("↓", "Move step down", () => MoveStep(idx, 1)));
            controls.Children.Add(MiniAction("×", "Delete this step", () => RemoveStep(idx)));

            var headGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(captionBox, 1);
            Grid.SetColumn(controls, 2);
            headGrid.Children.Add(badge);
            headGrid.Children.Add(captionBox);
            headGrid.Children.Add(controls);

            var preview = new Image
            {
                Source = RenderStepBitmap(step, stepNumber),
                Stretch = Stretch.Uniform,
                MaxHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);

            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = preview
            };

            var card = new Border
            {
                Background = Palette.Card,
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new StackPanel { Orientation = Orientation.Vertical, Children = { headGrid, imgBorder } }
            };
            _listHost.Children.Add(card);
        }
    }

    private static Border MiniAction(string glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = Palette.Group,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = Palette.Muted,
                FontFamily = Palette.Font,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        b.MouseEnter += (_, _) => b.Background = Palette.Hover;
        b.MouseLeave += (_, _) => b.Background = Palette.Group;
        b.MouseLeftButtonDown += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    private void MoveStep(int index, int delta)
    {
        int next = index + delta;
        if (next < 0 || next >= _steps.Count)
            return;
        (_steps[index], _steps[next]) = (_steps[next], _steps[index]);
        RebuildList();
    }

    private void RemoveStep(int index)
    {
        if (index < 0 || index >= _steps.Count)
            return;
        _steps.RemoveAt(index);
        RebuildList();
        _status.Foreground = Palette.Muted;
        _status.Text = $"{_steps.Count} {(_steps.Count == 1 ? "step" : "steps")} remaining.";
    }

    private static BitmapSource RenderStepBitmap(CapturedStep step, int number)
    {
        var baseImg = BitmapConvert.ToBitmapSource(step.Image);
        int w = baseImg.PixelWidth;
        int h = baseImg.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImg, new Rect(0, 0, w, h));

            // Translucent warm highlight ring right where the tech clicked
            var pt = new Point(step.ClickX, step.ClickY);
            dc.DrawEllipse(Palette.Freeze(0xFF, 0x3B, 0x30, 0x38), new Pen(Palette.Brush("#FF3B30"), 2.5), pt, 22, 22);

            // Crisp numbered badge slightly offset so it never covers the clicked button itself
            double bx = Math.Clamp(step.ClickX + 26, 20, Math.Max(20, w - 20));
            double by = Math.Clamp(step.ClickY - 24, 20, Math.Max(20, h - 20));
            var badgeCenter = new Point(bx, by);
            dc.DrawEllipse(Palette.Freeze(0, 0, 0, 0x60), null, new Point(bx + 1, by + 2), 16, 16);
            dc.DrawEllipse(Palette.Brush("#FF3B30"), new Pen(Brushes.White, 2), badgeCenter, 15, 15);
            var ft = new FormattedText(
                number.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(Palette.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                14,
                Brushes.White,
                1.0);
            dc.DrawText(ft, new Point(bx - ft.Width / 2, by - ft.Height / 2 - 0.5));
        }

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private BitmapSource RenderCombinedVerticalImage()
    {
        const int maxImgW = 860;
        const int pad = 28;
        const int cardPad = 16;
        const int cardGap = 18;
        const int headerH = 54;
        const int stepHeadH = 38;

        var rendered = new List<(BitmapSource Img, int DrawW, int DrawH, string Caption, int Number)>(_steps.Count);
        int contentW = 640;
        for (int i = 0; i < _steps.Count; i++)
        {
            var bmp = RenderStepBitmap(_steps[i], i + 1);
            double scale = bmp.PixelWidth > maxImgW ? maxImgW / (double)bmp.PixelWidth : 1.0;
            int dw = Math.Max(1, (int)Math.Round(bmp.PixelWidth * scale));
            int dh = Math.Max(1, (int)Math.Round(bmp.PixelHeight * scale));
            if (dw > contentW) contentW = dw;
            string cap = string.IsNullOrWhiteSpace(_steps[i].Caption) ? $"Step {i + 1}" : _steps[i].Caption.Trim();
            rendered.Add((bmp, dw, dh, cap, i + 1));
        }

        int cardW = contentW + cardPad * 2;
        int totalW = cardW + pad * 2;
        int totalH = pad + headerH;
        foreach (var item in rendered)
            totalH += cardPad + stepHeadH + item.DrawH + cardPad + cardGap;
        totalH += pad - cardGap;

        var bg = _darkTheme ? Palette.Bg : Palette.Freeze(0xFF, 0xFF, 0xFF);
        var cardBg = _darkTheme ? Palette.Card : Palette.Freeze(0xF8, 0xFA, 0xFC);
        var border = _darkTheme ? Palette.Line : Palette.Freeze(0xE2, 0xE8, 0xF0);
        var ink = _darkTheme ? Palette.Text : Palette.Freeze(0x0F, 0x17, 0x2A);
        var muted = _darkTheme ? Palette.Muted : Palette.Freeze(0x64, 0x74, 0x8B);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(bg, null, new Rect(0, 0, totalW, totalH));

            string title = string.IsNullOrWhiteSpace(_titleBox.Text) ? "Step-by-Step Guide" : _titleBox.Text.Trim();
            var titleFt = new FormattedText(
                title,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(Palette.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                21,
                ink,
                1.0);
            dc.DrawText(titleFt, new Point(pad, pad));

            var subFt = new FormattedText(
                $"{rendered.Count} {(rendered.Count == 1 ? "step" : "steps")}  ·  {DateTime.Now:yyyy-MM-dd HH:mm}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(Palette.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12,
                muted,
                1.0);
            dc.DrawText(subFt, new Point(pad, pad + 30));

            double y = pad + headerH;
            var borderPen = new Pen(border, 1);
            borderPen.Freeze();

            foreach (var item in rendered)
            {
                double cardH = cardPad + stepHeadH + item.DrawH + cardPad;
                var cardRect = new Rect(pad, y, cardW, cardH);
                dc.DrawRoundedRectangle(cardBg, borderPen, cardRect, 10, 10);

                // Step badge + caption
                var bCenter = new Point(pad + cardPad + 13, y + cardPad + 13);
                dc.DrawEllipse(Palette.Brush("#FF3B30"), null, bCenter, 13, 13);
                var numFt = new FormattedText(
                    item.Number.ToString(),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(Palette.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    13,
                    Brushes.White,
                    1.0);
                dc.DrawText(numFt, new Point(bCenter.X - numFt.Width / 2, bCenter.Y - numFt.Height / 2 - 0.5));

                var capFt = new FormattedText(
                    item.Caption,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(Palette.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    15,
                    ink,
                    1.0)
                { MaxTextWidth = cardW - cardPad * 2 - 40 };
                dc.DrawText(capFt, new Point(pad + cardPad + 36, y + cardPad + 3));

                // Step image
                var imgRect = new Rect(pad + cardPad, y + cardPad + stepHeadH, item.DrawW, item.DrawH);
                var clip = new RectangleGeometry(imgRect, 6, 6);
                clip.Freeze();
                dc.PushClip(clip);
                dc.DrawImage(item.Img, imgRect);
                dc.Pop();
                dc.DrawRoundedRectangle(null, borderPen, imgRect, 6, 6);

                y += cardH + cardGap;
            }
        }

        var target = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private List<StepGuideExportItem> BuildHtmlItems()
    {
        var list = new List<StepGuideExportItem>(_steps.Count);
        for (int i = 0; i < _steps.Count; i++)
        {
            var bmp = RenderStepBitmap(_steps[i], i + 1);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            list.Add(new StepGuideExportItem(i + 1, _steps[i].Caption, ms.ToArray(), bmp.PixelWidth, bmp.PixelHeight));
        }
        return list;
    }

    private void CopyAsImage()
    {
        if (_steps.Count == 0)
            return;
        var img = RenderCombinedVerticalImage();
        if (ImageFile.Copy(img))
        {
            _state.Recent.Add(BitmapConvert.ToPixelBuffer(img));
            _status.Foreground = Palette.Copied;
            _status.Text = "Copied! Paste the full step-by-step guide directly into any ticket or chat.";
        }
        else
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Clipboard busy — try Copy as Image again.";
        }
    }

    private void CopyAsHtml()
    {
        if (_steps.Count == 0)
            return;
        try
        {
            var items = BuildHtmlItems();
            string title = _titleBox.Text ?? "Step-by-Step Guide";
            string cfHtml = StepGuideHtml.BuildClipboardCfHtml(title, items, _darkTheme);
            string plain = StepGuideHtml.BuildPlainText(title, items);
            var data = new DataObject();
            data.SetData(DataFormats.Html, cfHtml);
            data.SetData(DataFormats.UnicodeText, plain);
            Clipboard.SetDataObject(data, true);
            _status.Foreground = Palette.Copied;
            _status.Text = "Copied rich HTML! Paste directly into Outlook, Word, Notion, or ticket replies.";
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Could not copy HTML: " + ex.Message;
        }
    }

    private void SavePng()
    {
        if (_steps.Count == 0)
            return;
        var img = RenderCombinedVerticalImage();
        if (ImageFile.Save(img, this))
        {
            _status.Foreground = Palette.Copied;
            _status.Text = "Saved step-by-step PNG!";
        }
    }

    private void SaveHtml()
    {
        if (_steps.Count == 0)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save Step-by-Step Guide (HTML)",
            Filter = "HTML file (*.html)|*.html",
            FileName = $"{Brand.Name}-Guide-{DateTime.Now:yyyyMMdd-HHmmss}.html"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var items = BuildHtmlItems();
            string html = StepGuideHtml.BuildDocument(_titleBox.Text ?? "Step-by-Step Guide", items, _darkTheme);
            File.WriteAllText(dialog.FileName, html);
            _status.Foreground = Palette.Copied;
            _status.Text = "Saved " + System.IO.Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Save failed: " + ex.Message;
        }
    }

    private void PrintGuide()
    {
        if (_steps.Count == 0)
            return;
        try
        {
            var img = RenderCombinedVerticalImage();
            string title = string.IsNullOrWhiteSpace(_titleBox.Text) ? $"{Brand.Name} Guide" : _titleBox.Text.Trim();
            if (ImageFile.Print(img, this, title))
            {
                _status.Foreground = Palette.Copied;
                _status.Text = "Sent guide to printer!";
            }
        }
        catch (Exception ex)
        {
            _status.Foreground = Palette.Warning;
            _status.Text = "Print failed: " + ex.Message;
        }
    }
}
