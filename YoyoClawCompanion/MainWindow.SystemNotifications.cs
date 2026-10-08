using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YoyoClawCompanion.Services;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private readonly SystemNotificationService _systemNotifications = new();
    private readonly SystemNotificationQueue _pendingSystemToasts = new();
    private readonly List<SystemToast> _activeSystemToastBatch = new(3);
    private SystemToast? _activeWindowsToast;
    private bool _systemNotificationEnabled;
    private string? _notificationLaunchFeedback;
    private string? _notificationFeedbackId;
    private bool _notificationOpening;
    private bool _notificationFlushPending;
    private string? _notificationRowsKey;
    private SystemToast[] _renderedSystemToasts = [];
    internal SystemNotificationService SystemNotifications => _systemNotifications;
    internal event EventHandler? NotificationFiltersChanged;

    private void InitializeSystemNotifications()
    {
        _systemNotifications.Received += (_, toast) => ReceiveSystemToast(toast);
        Closed += (_, _) => { _systemNotifications.Dispose(); DiscardSystemToasts(); };
    }

    private bool IsIgnoredNotification(SystemToast toast)
        => (_settings.IgnoredNotificationTitles ?? []).Any(rule => rule is not null && rule.Matches(toast));

    private void ReceiveSystemToast(SystemToast toast)
    {
        if (!_settings.EnableSystemNotifications || IsIgnoredNotification(toast)) return;
        var index = _activeSystemToastBatch.FindIndex(item => item.Id == toast.Id);
        if (index >= 0)
        {
            _activeSystemToastBatch[index] = toast;
            _activeWindowsToast = _activeSystemToastBatch[0];
        }
        else _pendingSystemToasts.Enqueue(toast);
        // All notifications from one listener snapshot reach the queue before a single UI update.
        if (_notificationFlushPending) return;
        _notificationFlushPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _notificationFlushPending = false;
            if (_lifetimeCancellation.IsCancellationRequested) return;
            if (_activeWindowsToast is not null && _activeConfirmationNotice is null)
            {
                // Later snapshots wait for the next batch, so they get a full display interval.
                UpdateRecentNotice();
                SetSystemToastHeader();
            }
            else TryShowNextSystemToast();
        }, DispatcherPriority.Background);
    }

    internal async Task ConfigureSystemNotificationsAsync(bool requestAccess = false)
    {
        _systemNotificationEnabled = _settings.EnableSystemNotifications;
        if (_settings.EnableSystemNotifications) await _systemNotifications.StartAsync(requestAccess);
        else
        {
            _systemNotifications.Stop(); _pendingSystemToasts.Clear();
            if (_activeWindowsToast is not null) EndCompletionNotice();
        }
    }

    private void FillSystemToastBatch()
    {
        _pendingSystemToasts.Remove(IsIgnoredNotification);
        var capacity = SystemNotificationQueue.BatchSize(_settings.MaxResponseLines);
        if (_activeSystemToastBatch.Count > capacity)
        {
            _pendingSystemToasts.Prepend(_activeSystemToastBatch.Skip(capacity));
            _activeSystemToastBatch.RemoveRange(capacity, _activeSystemToastBatch.Count - capacity);
        }
        _activeSystemToastBatch.AddRange(_pendingSystemToasts.Take(capacity - _activeSystemToastBatch.Count));
        _activeWindowsToast = _activeSystemToastBatch.FirstOrDefault();
    }

    private bool TryShowNextSystemToast()
    {
        if (!_settings.EnableSystemNotifications || _pendingSystemToasts.Count == 0 || _activeWindowsToast is not null
            || _activeConfirmationNotice is not null || _activeCompletionNotice is not null || _activeSystemNotice is not null) return false;
        FillSystemToastBatch();
        if (_activeWindowsToast is null) return false;
        _notificationLaunchFeedback = null;
        _notificationFeedbackId = null;
        UpdateRecentNotice();
        SetSystemToastHeader();
        BeginNotificationCountdown(TimeSpan.FromSeconds(_settings.SystemNotificationDisplaySeconds));
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
        return true;
    }

    private void SetSystemToastHeader()
    {
        if (_activeWindowsToast is null) return;
        var sources = string.Join("、", CurrentToastBatch().Select(t => t.Source).Distinct());
        var pending = _pendingSystemToasts.Count;
        var summary = sources + (pending > 0 ? $" · 还有 {pending} 条" : "");
        if (_pendingSystemToasts.DroppedCount > 0) summary += $" · {_pendingSystemToasts.DroppedCount} 条请查看通知中心";
        var changed = HeadlineText.Text != "系统通知" || SummaryText.Text != summary;
        SetOfflineDismissTarget(null);
        HeadlineText.Text = "系统通知";
        HeadlineText.Foreground = AccentBrush;
        SetPlainSummary(summary, AccentBrush);
        if (changed) HeadlineText.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(160)));
    }

    private IReadOnlyList<SystemToast> CurrentToastBatch()
        => _activeSystemToastBatch.Count > 0 ? _activeSystemToastBatch : _activeWindowsToast is { } toast ? [toast] : [];

    private void ClearSystemToastBatch()
    {
        _activeSystemToastBatch.Clear();
        _activeWindowsToast = null;
        _notificationRowsKey = null;
        _renderedSystemToasts = [];
        _notificationLaunchFeedback = null;
        _notificationFeedbackId = null;
    }

    private void DiscardSystemToasts()
    {
        _pendingSystemToasts.Clear();
        ClearSystemToastBatch();
    }

    private void PauseSystemToastForDecision()
    {
        if (_activeWindowsToast is null) return;
        _pendingSystemToasts.Prepend(CurrentToastBatch());
        ClearSystemToastBatch();
    }

    private void UpdateRecentNotice()
    {
        var text = ActiveNoticeText;
        if (RecentResultText.Text != text) RecentResultText.Text = text;
        var canOpen = _activeWindowsToast is not null && _activeConfirmationNotice is null;
        var wasShowing = NotificationBatchPanel.Visibility == Visibility.Visible;
        RecentResultText.Visibility = canOpen ? Visibility.Collapsed : Visibility.Visible;
        NotificationBatchPanel.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
        if (wasShowing && !canOpen) RecentBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(160)));
        if (!canOpen) { NotificationBatchPanel.Children.Clear(); _notificationRowsKey = null; _renderedSystemToasts = []; return; }
        RenderSystemToastBatch();
    }

    private void RenderSystemToastBatch()
    {
        var batch = CurrentToastBatch();
        var lineHeight = RecentResultText.LineHeight;
        var compact = _settings.MaxResponseLines == 1;
        var key = $"{lineHeight}|{RecentResultText.FontSize}|{RecentResultText.Foreground}|{compact}|{_notificationLaunchFeedback}|{_notificationFeedbackId}|{_notificationOpening}";
        if (key == _notificationRowsKey && batch.SequenceEqual(_renderedSystemToasts)) return;
        _notificationRowsKey = key;
        _renderedSystemToasts = batch.ToArray();
        NotificationBatchPanel.Children.Clear();
        foreach (var toast in batch)
        {
            var row = new Grid { Height = lineHeight * (compact ? 1 : 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            var content = new StackPanel();
            var feedback = _notificationFeedbackId == toast.Id ? _notificationLaunchFeedback : null;
            var title = $"{toast.Source} · {OneLine(toast.Title)}";
            var body = OneLine(feedback ?? toast.Body);
            content.Children.Add(NotificationLine(compact ? title + " · " + body : title));
            if (!compact) content.Children.Add(NotificationLine(body));
            var open = new Button { Content = content, Tag = toast, Cursor = Cursors.Hand, IsEnabled = !_notificationOpening,
                Style = (Style)FindResource("NotificationRowAction"), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                ToolTip = $"点击打开 {toast.Source}", Padding = new Thickness(0) };
            open.Click += NotificationOpen_Click;
            row.Children.Add(open);
            var ignore = new Button { Content = "×", Tag = toast, Cursor = Cursors.Hand,
                Style = (Style)FindResource("NotificationRowAction"), Height = lineHeight, VerticalAlignment = VerticalAlignment.Top,
                ToolTip = $"不再提醒此标题：{toast.Title}（可在设置中恢复）", Padding = new Thickness(0) };
            System.Windows.Automation.AutomationProperties.SetName(ignore, "屏蔽此通知标题");
            ignore.Click += IgnoreNotification_Click;
            Grid.SetColumn(ignore, 1); row.Children.Add(ignore);
            NotificationBatchPanel.Children.Add(row);
        }
        NotificationBatchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(160)));
    }

    private TextBlock NotificationLine(string text) => new()
    {
        Text = text, Height = RecentResultText.LineHeight, LineHeight = RecentResultText.LineHeight,
        FontSize = RecentResultText.FontSize, FontWeight = RecentResultText.FontWeight, Foreground = RecentResultText.Foreground,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private async void IgnoreNotification_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: SystemToast toast }) return;
        if (!IsIgnoredNotification(toast))
            _settings.IgnoredNotificationTitles = (_settings.IgnoredNotificationTitles ?? []).Append(NotificationTitleRule.From(toast)).ToArray();
        _pendingSystemToasts.Remove(IsIgnoredNotification);
        _activeSystemToastBatch.RemoveAll(IsIgnoredNotification);
        NotificationFiltersChanged?.Invoke(this, EventArgs.Empty);
        if (_activeSystemToastBatch.Count == 0) EndCompletionNotice();
        else { FillSystemToastBatch(); UpdateRecentNotice(); SetSystemToastHeader(); }
        await AppSettings.SaveAsync(_settings);
    }

    internal async Task RestoreNotificationTitleAsync(NotificationTitleRule rule)
    {
        _settings.IgnoredNotificationTitles = (_settings.IgnoredNotificationTitles ?? []).Where(item => item != rule).ToArray();
        NotificationFiltersChanged?.Invoke(this, EventArgs.Empty);
        await AppSettings.SaveAsync(_settings);
    }

    private async void NotificationOpen_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var toast = (sender as Button)?.Tag as SystemToast ?? _activeWindowsToast;
        if (_notificationOpening || toast is null || _activeConfirmationNotice is not null) return;
        _notificationOpening = true;
        _notificationFeedbackId = toast.Id;
        _notificationLaunchFeedback = "正在打开来源应用…";
        UpdateRecentNotice();
        var result = await NotificationAppLauncher.OpenAsync(toast.AppUserModelId);
        _notificationOpening = false;
        if (!CurrentToastBatch().Any(item => item.Id == toast.Id)) { UpdateRecentNotice(); return; }
        if (result == NotificationOpenResult.Opened)
        {
            _activeSystemToastBatch.RemoveAll(item => item.Id == toast.Id);
            _notificationLaunchFeedback = null;
            if (_activeSystemToastBatch.Count == 0) EndCompletionNotice();
            else { _activeWindowsToast = _activeSystemToastBatch[0]; UpdateRecentNotice(); SetSystemToastHeader(); }
        }
        else
        {
            _notificationLaunchFeedback = result == NotificationOpenResult.RunningButUnavailable
                ? "来源应用已运行，但无法唤起窗口，请从系统托盘打开。"
                : "无法打开来源应用，请使用原系统通知或手动打开应用。";
            UpdateRecentNotice();
        }
    }
}
