using System.IO;
using System.Threading;
using System.Windows;
using YoyoClawCompanion.Services;
using Application = System.Windows.Application;

namespace YoyoClawCompanion;

public partial class App : Application
{
    private const string MutexName = @"Local\YoyoClawCompanion.SingleInstance";
    private const string ShowSettingsEventName = @"Local\YoyoClawCompanion.ShowSettings";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsRegistration;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private static readonly object CrashLogLock = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionLogging();
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

    private void RegisterGlobalExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) => WriteCrashLog("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain.UnhandledException", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) => WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
            Directory.CreateDirectory(directory);
            var entry = $"{DateTimeOffset.Now:O} [{source}]{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";
            lock (CrashLogLock)
                File.AppendAllText(Path.Combine(directory, "crash.log"), entry);
        }
        catch
        {
            // Crash logging must never replace the original exception.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _showSettingsRegistration?.Unregister(null);
        _showSettingsEvent?.Dispose();
        DisposeTrayIcon();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    internal void SetTrayIconVisible(bool visible)
    {
        if (!visible)
        {
            DisposeTrayIcon();
            return;
        }
        if (_trayIcon is not null) return;

        _trayDrawingIcon = LoadTrayIcon();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开主页", null, (_, _) => Dispatcher.BeginInvoke(OpenHome));
        menu.Items.Add("刷新全部状态", null, (_, _) => Dispatcher.BeginInvoke(RefreshStatus));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出 AI Dynamic Island", null, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = "AI Dynamic Island",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(WakeIslandFromTray);
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenHome);
    }

    private void OpenHome() => (MainWindow as YoyoClawCompanion.MainWindow)?.OpenHomeFromExternalRequest();
    private void WakeIslandFromTray() => (MainWindow as YoyoClawCompanion.MainWindow)?.WakeFromTray();
    private void RefreshStatus() => (MainWindow as YoyoClawCompanion.MainWindow)?.RefreshFromExternalRequest();

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var executable = ApplicationLocator.FindYoyoExecutable(null);
            if (executable is not null && System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } icon) return icon;
        }
        catch { }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayDrawingIcon?.Dispose();
        _trayDrawingIcon = null;
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
