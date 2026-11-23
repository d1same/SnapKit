using SnapKit;

namespace SnapKit.Tests;

public class SettingsTests
{
    [Fact]
    public void Roundtrip_keeps_shortcuts_and_toggles()
    {
        var settings = AppSettings.CreateDefault();
        settings.ShutterSound = true;
        settings.CaptureFlash = false;
        settings.StartWithWindows = true;
        settings.CloseOnClickAway = false;
        settings.Ocr = new HotkeyGesture { VirtualKey = 0x4F, Ctrl = true, Shift = true };

        var path = Path.Combine(Path.GetTempPath(), "snapkit-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.True(loaded.ShutterSound);
            Assert.False(loaded.CaptureFlash);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.CloseOnClickAway);
            Assert.True(loaded.Ocr.EqualsGesture(settings.Ocr));
            Assert.Equal(0x2C, loaded.Area.VirtualKey);
            Assert.False(loaded.Area.Shift);
            Assert.True(loaded.Window.Shift);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Startup_stays_on_when_the_file_does_not_mention_it()
    {
        var path = Path.Combine(Path.GetTempPath(), "snapkit-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ \"captureFlash\": false }");
            var loaded = SettingsStore.Load(path);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.CaptureFlash);
            Assert.True(loaded.CloseOnClickAway);
            Assert.True(AppSettings.CreateDefault().StartWithWindows);
            Assert.True(AppSettings.CreateDefault().CloseOnClickAway);

            File.WriteAllText(path, "{ \"startWithWindows\": false }");
            Assert.True(SettingsStore.Load(path).StartWithWindows);

            File.WriteAllText(path, "{ \"startWithWindows\": false, \"revision\": 2 }");
            Assert.False(SettingsStore.Load(path).StartWithWindows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hotkey_names_match_the_defaults()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal("PrtSc", settings.Area.Display);
        Assert.Equal("Shift+PrtSc", settings.Window.Display);
        Assert.Equal("Alt+PrtSc", settings.FullScreen.Display);
        Assert.Equal("Ctrl+Shift+PrtSc", settings.Scrolling.Display);
        Assert.Equal("Ctrl+Shift+O", settings.Ocr.Display);
        Assert.Equal("Ctrl+Alt+PrtSc", settings.RepeatLastArea.Display);
    }

    [Fact]
    public void Roundtrip_keeps_persisted_style_defaults_and_last_area()
    {
        var settings = AppSettings.CreateDefault();
        settings.LastTool = "Redact";
        settings.LastColorHex = "#0A84FF";
        settings.LastStroke = 8;
        settings.LastFontSize = 24;
        settings.LastFillMode = ShapeFillMode.Tint;
        settings.LastBackdrop = BackdropPreset.Clean;
        settings.QuickSaveFolder = @"C:\Docs\Shots";
        settings.LastArea = new SavedRegion { X = 120, Y = 80, Width = 640, Height = 360 };

        var path = Path.Combine(Path.GetTempPath(), "snapkit-style-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.Equal("Redact", loaded.LastTool);
            Assert.Equal("#0A84FF", loaded.LastColorHex);
            Assert.Equal(8, loaded.LastStroke);
            Assert.Equal(24, loaded.LastFontSize);
            Assert.Equal(ShapeFillMode.Tint, loaded.LastFillMode);
            Assert.Equal(BackdropPreset.Clean, loaded.LastBackdrop);
            Assert.Equal(@"C:\Docs\Shots", loaded.QuickSaveFolder);
            Assert.NotNull(loaded.LastArea);
            Assert.Equal(640, loaded.LastArea!.Width);
            Assert.True(loaded.LastArea.IsValid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hex_parse_accepts_a_hash()
    {
        Assert.True(HexColor.TryParse("#1A2B3C", out var r, out var g, out var b));
        Assert.Equal(0x1A, r);
        Assert.Equal(0x2B, g);
        Assert.Equal(0x3C, b);
        Assert.False(HexColor.TryParse("nope", out _, out _, out _));
    }

    [Fact]
    public void Brand_name_comes_from_brand_file()
    {
        var root = FindRoot();
        var expected = File.ReadAllText(Path.Combine(root, "brand.name")).Trim();
        Assert.True(string.Equals(expected, Brand.Name, StringComparison.Ordinal), Brand.Name);
        Assert.Contains(expected, Brand.MutexName);
        Assert.Contains(expected, SettingsStore.FilePath);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "brand.name")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("brand.name was not found above the test output.");
    }
}
