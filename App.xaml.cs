using System.Windows;
using System.Windows.Threading;

namespace SnapKit;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private AppState? _state;
    private bool _firstRun;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Failsafe.Start();
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += OnTask;

        if (HasArg(e.Args, "--uninstall"))
        {
            Installer.Uninstall();
            Shutdown();
            return;
        }

        if (!HasArg(e.Args, "--portable") && Installer.ShouldInstall())
        {
            Installer.InstallAndRelaunch();
            Shutdown();
            return;
        }

        _firstRun = HasArg(e.Args, "--first-run");

        try
        {
            _mutex = new Mutex(true, Brand.MutexName, out _ownsMutex);
        }
        catch
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        var settings = SettingsStore.Load();
        _state = new AppState { Settings = settings };
        _state.Hotkeys = new HotkeyHost(_state);
        _state.Coordinator = new CaptureCoordinator(_state);
        _state.Tray = new TrayHost(_state);
        _state.Hotkeys.RegisterAll();
        if (settings.StartWithWindows)
            StartupRegistration.Apply(true);
        if (_firstRun)
            _state.Tray.Balloon(Brand.Name, "Installed. SnapKit is in the Start menu and the system tray. Ctrl+Alt+Shift+Esc quits if a window gets stuck.");
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFailure(e.Exception);
    }

    private void OnDomain(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowFailure(ex);
        else
            Failsafe.ReleaseCovers();
    }

    private static void OnTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        var app = Current;
        if (app == null)
            return;
        app.Dispatcher.BeginInvoke(() =>
        {
            Failsafe.ReleaseCovers();
            try
            {
                var window = new ErrorWindow(e.Exception.ToString());
                window.Show();
            }
            catch
            {
                // The failsafe hotkey still ends the process.
            }
        });
    }

    private void ShowFailure(Exception exception)
    {
        try
        {
            Failsafe.ReleaseCovers();
            var window = new ErrorWindow(exception.ToString());
            window.Show();
            window.Activate();
        }
        catch
        {
            try { _state?.Tray?.Balloon(Brand.Name, exception.Message); } catch { /* tray may be gone */ }
        }
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Hotkeys.Dispose();
        _state?.Tray?.Dispose();
        if (_mutex != null)
        {
            if (_ownsMutex)
            {
                try { _mutex.ReleaseMutex(); } catch { /* already released */ }
            }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
