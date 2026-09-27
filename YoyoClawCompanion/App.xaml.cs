using System.IO;
using System.Threading;
using System.Windows;

namespace YoyoClawCompanion;

public partial class App : Application
{
    private const string MutexName = @"Local\YoyoClawCompanion.SingleInstance";
    private const string ShowSettingsEventName = @"Local\YoyoClawCompanion.ShowSettings";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowSettingsEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent, (_, _) =>
            Dispatcher.BeginInvoke(() => (this.MainWindow as YoyoClawCompanion.MainWindow)?.OpenHomeFromExternalRequest()), null, Timeout.Infinite, false);
        CleanupStaleSnapshots();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _showSettingsRegistration?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void CleanupStaleSnapshots()
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
            if (!Directory.Exists(directory)) return;
            foreach (var pattern in new[] { "status-*.json", "magicore-status-*.json" })
            {
                foreach (var file in Directory.EnumerateFiles(directory, pattern))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
