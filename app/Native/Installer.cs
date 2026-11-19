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

        bool updating = File.Exists(ExePath);
        string? error = null;
        InstallWindow.Run(updating, report =>
        {
            report(updating ? "Closing the copy that is already running…" : "Preparing the install folder…");
            Directory.CreateDirectory(DirectoryPath);
            StopOtherCopies();
            report("Copying SnapKit…");
            if (!TryReplace(current, ExePath))
            {
                error = "The installed " + Brand.Name + " file is still locked. End " + Brand.Name + " in Task Manager, then run this file again.";
                return;
            }

            report("Adding the Start menu shortcut…");
            try
            {
                CreateShortcut(ShortcutPath, ExePath);
                RegisterUninstall(ExePath);
            }
            catch
            {
                // The copy is in place. Shortcuts can be repaired by running the file again.
            }

            report(updating ? "Starting the updated SnapKit…" : "Starting SnapKit…");
        });

        if (error != null)
        {
            System.Windows.MessageBox.Show(
                error,
                Brand.Name,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
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

        StopOtherCopies();
        RemoveInstallFolder();
    }

    private static void RemoveInstallFolder()
    {
        if (!Directory.Exists(DirectoryPath))
            return;

        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                NativeMethods.MoveFileEx(file, null, NativeMethods.MoveFileDelayUntilReboot);
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
                Directory.Delete(DirectoryPath, recursive: false);
            else
                NativeMethods.MoveFileEx(DirectoryPath, null, NativeMethods.MoveFileDelayUntilReboot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NativeMethods.MoveFileEx(DirectoryPath, null, NativeMethods.MoveFileDelayUntilReboot);
        }
    }

    private static bool TryReplace(string source, string destination)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                    File.SetAttributes(destination, FileAttributes.Normal);
                File.Copy(source, destination, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StopOtherCopies();
                Thread.Sleep(300);
            }
        }
        return false;
    }

    private static void StopOtherCopies()
    {
        string name = Path.GetFileNameWithoutExtension(ExePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(4000);
            }
            catch
            {
                // That copy has already exited.
            }
            finally
            {
                process.Dispose();
            }
        }
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
