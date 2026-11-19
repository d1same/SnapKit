using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SnapKit;

internal static class Installer
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        Brand.Name);

    public static string ExePath => Path.Combine(DirectoryPath, Brand.Name + ".exe");

    public static bool ShouldInstall()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current))
            return false;
        return !string.Equals(Path.GetFullPath(current), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void InstallAndRelaunch()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current))
            return;

        Directory.CreateDirectory(DirectoryPath);
        try
        {
            File.Copy(current, ExePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                Brand.Name + " is already installed and running. Exit it from the tray icon, then run this file again to update.",
                Brand.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            CreateShortcut(ShortcutPath, ExePath);
            RegisterUninstall(ExePath);
        }
        catch
        {
            // The copy is in place. Shortcuts can be repaired by running the file again.
        }

        Process.Start(new ProcessStartInfo(ExePath, "--first-run") { UseShellExecute = true });
    }

    public static void Uninstall()
    {
        try { StartupRegistration.Apply(false); } catch { /* the startup value may already be gone */ }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            key?.DeleteSubKey(Brand.Name, throwOnMissingSubKey: false);
        }
        catch
        {
            // The uninstall entry is already gone.
        }

        try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { /* shortcut in use */ }

        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExePath)))
        {
            if (process.Id == Environment.ProcessId)
                continue;
            try { process.Kill(entireProcessTree: true); } catch { /* already exiting */ }
        }

        if (!Directory.Exists(DirectoryPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + DirectoryPath + "\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Brand.Name + ".lnk");

    private static void CreateShortcut(string linkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shell shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows shell shortcuts are unavailable.");
        dynamic shortcut = shell.CreateShortcut(linkPath);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.Description = Brand.Name;
        shortcut.Save();
    }

    private static void RegisterUninstall(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Brand.Name);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        long bytes = new FileInfo(exe).Length;
        key.SetValue("DisplayName", Brand.Name);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", Brand.Name);
        key.SetValue("InstallLocation", DirectoryPath);
        key.SetValue("DisplayIcon", exe + ",0");
        key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Clamp(bytes / 1024, 1, int.MaxValue), RegistryValueKind.DWord);
    }
}
