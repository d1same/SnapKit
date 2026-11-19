using System.Windows.Threading;
using Microsoft.Win32;

namespace SnapKit;

/// <summary>
/// Windows 11 hides a new tray icon in the overflow. IsPromoted puts it beside the clock.
/// Explorer creates the registry row after the icon appears, so this retries briefly.
/// </summary>
internal static class TrayPromoter
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";

    public static void KeepBesideClock(Action refresh)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var result = Promote(path);
                if (result == PromoteResult.Updated)
                {
                    try
                    {
                        _ = dispatcher.BeginInvoke(refresh);
                    }
                    catch
                    {
                        // The app is already shutting down.
                    }
                    return;
                }
                if (result == PromoteResult.Already)
                    return;
                await Task.Delay(500);
            }
        });
    }

    internal static PromoteResult Promote(string exePath)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (root == null)
                return PromoteResult.Missing;
            var found = false;
            var changed = false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(name, writable: true);
                if (sub?.GetValue("ExecutablePath") is not string existing)
                    continue;
                if (!string.Equals(existing, exePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                found = true;
                if (sub.GetValue("IsPromoted") is int promoted && promoted == 1)
                    continue;
                sub.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                changed = true;
            }
            if (!found)
                return PromoteResult.Missing;
            return changed ? PromoteResult.Updated : PromoteResult.Already;
        }
        catch
        {
            return PromoteResult.Missing;
        }
    }

    internal enum PromoteResult
    {
        Missing,
        Already,
        Updated
    }
}
