using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace YoyoClawCompanion.Services;

internal sealed class SystemNotificationService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new();
    private readonly SystemNotificationTracker _tracker = new();
    private UserNotificationListener? _listener;
    private bool _subscribed, _polling, _enabled;
    private int _generation;
    internal string Status { get; private set; } = "未开启 · 原系统通知保留";
    internal int LastSnapshotCount { get; private set; }
    internal event EventHandler? StatusChanged;
    internal event EventHandler<SystemToast>? Received;

    public SystemNotificationService() { _timer.Tick += async (_, _) => await PollAsync(); }

    internal async Task StartAsync(bool requestAccess)
    {
        Stop();
        var generation = _generation;
        try
        {
            _listener = UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (requestAccess && access != UserNotificationListenerAccessStatus.Allowed)
                access = await _listener.RequestAccessAsync(); // Caller is the WPF UI thread.
            if (generation != _generation) return;
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                SetStatus("未获得通知读取权限 · 可点击授权或前往 Windows 设置");
                return;
            }
            _enabled = true;
            _tracker.Reset();
            await PollAsync(); // Baseline only: do not replay existing Action Center notifications.
            if (generation != _generation || !_enabled) return;
            try { _listener.NotificationChanged += ListenerChanged; _subscribed = true; }
            catch (System.Runtime.InteropServices.COMException) { _subscribed = false; }
            _timer.Interval = TimeSpan.FromSeconds(_subscribed ? 60 : 2);
            _timer.Start();
            SetStatus(_subscribed ? "已连接 Windows 通知 · 原系统通知保留" : "已连接 Windows 通知 · 每 2 秒检查新通知，原系统通知保留");
        }
        catch (Exception error) { Stop(); SetStatus($"系统通知暂不可用（0x{error.HResult:X8}）"); }
    }

    private void ListenerChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        => _dispatcher.BeginInvoke(async () => await PollAsync());

    private async Task PollAsync()
    {
        if (!_enabled || _polling || _listener is null) return;
        var generation = _generation;
        _polling = true;
        try
        {
            if (_listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            { Stop(); SetStatus("通知读取权限已撤销 · 原系统通知保留"); return; }
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (!_enabled || generation != _generation) return;
            var snapshot = new List<SystemToast>();
            foreach (var notification in notifications.OrderByDescending(n => n.CreationTime).Take(512))
            {
                try
                {
                    var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var text = binding?.GetTextElements().Select(t => t.Text).ToArray() ?? [];
                    if (text.Length == 0) continue;
                    var app = notification.AppInfo;
                    snapshot.Add(new SystemToast($"{app.AppUserModelId}|{notification.Id}", app.DisplayInfo.DisplayName,
                        text[0], string.Join("\n", text.Skip(1)), notification.CreationTime));
                }
                catch { /* One unavailable app must not prevent reading the rest. */ }
            }
            LastSnapshotCount = snapshot.Count;
            foreach (var toast in _tracker.Accept(snapshot)) Received?.Invoke(this, toast);
        }
        catch (Exception error) { SetStatus($"通知检查暂失败（0x{error.HResult:X8}），稍后重试"); }
        finally { _polling = false; }
    }

    private void SetStatus(string text) { Status = text; StatusChanged?.Invoke(this, EventArgs.Empty); }
    internal void Stop()
    {
        ++_generation; _enabled = false; _timer.Stop();
        if (_subscribed && _listener is not null) { try { _listener.NotificationChanged -= ListenerChanged; } catch { } }
        _subscribed = false; _tracker.Reset(); SetStatus("未开启 · 原系统通知保留");
    }
    public void Dispose() => Stop();
}
