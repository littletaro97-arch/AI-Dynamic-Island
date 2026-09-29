using System.IO;
using System.Runtime.InteropServices;
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
        menu.Items.Add("重置并重新检测", null, (_, _) => Dispatcher.BeginInvoke(RefreshStatus));
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
        using var bitmap = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(24, 32, 51));
            using var shape = RoundedRectangle(new System.Drawing.RectangleF(4, 4, 56, 56), 16);
            graphics.FillPath(background, shape);
            using var green = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(62, 213, 152));
            using var purple = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(143, 160, 255));
            using var yellow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(242, 201, 76));
            graphics.FillEllipse(green, 16, 27, 10, 10);
            graphics.FillEllipse(purple, 27, 27, 10, 10);
            graphics.FillEllipse(yellow, 38, 27, 10, 10);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var source = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)source.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(System.Drawing.RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
