using System.Windows;
using System.Windows.Threading;

namespace SnapKit;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private AppState? _state;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;

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
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _state?.Tray?.Balloon(Brand.Name, e.Exception.Message);
        }
        catch
        {
            // The tray may not exist yet. Keep the process alive anyway.
        }
        e.Handled = true;
    }

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
