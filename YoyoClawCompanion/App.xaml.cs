using System.IO;
using System.Threading;
using System.Windows;

namespace YoyoClawCompanion;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\YoyoClawCompanion.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        CleanupStaleSnapshots();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
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
