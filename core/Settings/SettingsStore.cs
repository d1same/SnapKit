using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapKit;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.Name);

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load() => Load(FilePath);

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return AppSettings.CreateDefault();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? AppSettings.CreateDefault();
            settings.LastColorHex = HexColor.Normalize(settings.LastColorHex, "#FF3B30");
            settings.LastStroke = Math.Clamp(settings.LastStroke <= 0 ? 3 : settings.LastStroke, 1, 16);
            settings.LastFontSize = Math.Clamp(settings.LastFontSize <= 0 ? 18 : settings.LastFontSize, 8, 72);
            settings.QuickSaveFolder ??= "";
            settings.LastTool = string.IsNullOrWhiteSpace(settings.LastTool) ? "Arrow" : settings.LastTool;
            if (AppSettings.ApplyStartupDefault(settings))
            {
                try
                {
                    Save(settings, path);
                }
                catch
                {
                    // The in-memory setting still starts the app if the file is locked.
                }
            }
            return settings;
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public static void Save(AppSettings settings) => Save(settings, FilePath);

    public static void Save(AppSettings settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
}
