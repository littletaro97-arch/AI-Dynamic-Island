using System.Windows;
using System.Windows.Media.Animation;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private readonly SystemNotificationService _systemNotifications = new();
    private readonly Queue<SystemToast> _pendingSystemToasts = new();
    private SystemToast? _activeWindowsToast;
    private bool _systemNotificationEnabled;
    internal SystemNotificationService SystemNotifications => _systemNotifications;

    private void InitializeSystemNotifications()
    {
        _systemNotifications.Received += (_, toast) =>
        {
            if (!_settings.EnableSystemNotifications) return;
            if (_pendingSystemToasts.Count >= 20) _pendingSystemToasts.Dequeue();
            _pendingSystemToasts.Enqueue(toast);
            TryShowNextSystemToast();
        };
        Closed += (_, _) => _systemNotifications.Dispose();
    }

    internal async Task ConfigureSystemNotificationsAsync(bool requestAccess = false)
    {
        _systemNotificationEnabled = _settings.EnableSystemNotifications;
        if (_settings.EnableSystemNotifications) await _systemNotifications.StartAsync(requestAccess);
        else
        {
            _systemNotifications.Stop(); _pendingSystemToasts.Clear();
            if (_activeWindowsToast is not null) { _activeWindowsToast = null; EndCompletionNotice(); }
        }
    }

    private bool TryShowNextSystemToast()
    {
        if (!_settings.EnableSystemNotifications || _pendingSystemToasts.Count == 0 || _activeWindowsToast is not null
            || _activeConfirmationNotice is not null || _activeCompletionNotice is not null || _activeSystemNotice is not null) return false;
        _activeWindowsToast = _pendingSystemToasts.Dequeue();
        RecentResultText.Text = ActiveNoticeText;
        SetSystemToastHeader();
        _completionTimer.Stop();
        _completionTimer.Interval = TimeSpan.FromSeconds(_settings.SystemNotificationDisplaySeconds);
        _notificationHoldActive = true;
        _completionTimer.Start();
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
        return true;
    }

    private void SetSystemToastHeader()
    {
        if (_activeWindowsToast is null) return;
        var changed = HeadlineText.Text != "系统通知" || SummaryText.Text != _activeWindowsToast.Source;
        SetOfflineDismissTarget(null);
        HeadlineText.Text = "系统通知";
        HeadlineText.Foreground = AccentBrush;
        SetPlainSummary(_activeWindowsToast.Source, AccentBrush);
        if (changed) HeadlineText.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void PauseSystemToastForDecision()
    {
        if (_activeWindowsToast is null) return;
        if (_pendingSystemToasts.Count < 20) _pendingSystemToasts.Enqueue(_activeWindowsToast);
        _activeWindowsToast = null;
    }
}
