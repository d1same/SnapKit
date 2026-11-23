namespace SnapKit;

internal sealed class AppState
{
    public required AppSettings Settings { get; init; }
    public RecentCaptures Recent { get; } = new();
    public HotkeyHost Hotkeys { get; set; } = null!;
    public TrayHost? Tray { get; set; }
    public CaptureCoordinator Coordinator { get; set; } = null!;

    public IReadOnlyList<string> SaveSettings()
    {
        SettingsStore.Save(Settings);
        try
        {
            StartupRegistration.Apply(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            Tray?.Balloon(Brand.Name, ex.Message);
        }
        return Hotkeys.RegisterAll();
    }

    public void RememberLastArea(int x, int y, int width, int height)
    {
        if (width < SelectionAdjust.MinSize || height < SelectionAdjust.MinSize)
            return;
        Settings.LastArea = new SavedRegion
        {
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        try
        {
            SettingsStore.Save(Settings);
        }
        catch
        {
            // Keep the region in memory even if settings.json is locked.
        }
    }
}
