using Microsoft.Win32;

namespace SnapKit;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Couldn't open the Windows startup list.");
        if (!enabled)
        {
            key.DeleteValue(Brand.Name, throwOnMissingValue: false);
            return;
        }

        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Couldn't find the app path to register startup.");
        key.SetValue(Brand.Name, $"\"{path}\"");
    }
}
