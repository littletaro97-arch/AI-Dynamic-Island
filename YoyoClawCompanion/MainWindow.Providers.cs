using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class MainWindow
{
    private readonly DeepSeekStatusService _deepSeekStatusService = new();
    private DeepSeekStatus _deepSeekStatus = new(false, false, "unknown");
    private bool _deepSeekInstalled;
    private string? _deepSeekConfirmationId;
    private readonly HashSet<string> _suppressedProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _providerRunning = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenCompletionEvents = new(StringComparer.Ordinal);
    private readonly Queue<CompletionNotice> _pendingCompletions = new();
    private readonly DateTimeOffset _monitorStartedAt = DateTimeOffset.UtcNow;
    private string? _offlineDismissTarget;
    private int _dismissVersion, _providerVisibilityVersion;

    private void InitializeProviderSuppression()
        => _suppressedProviders.UnionWith((_settings.SuppressedOfflineProviders ?? []).Where(ProviderCatalog.IsKnown));

    private void SaveProviderSuppression()
    {
        _settings.SuppressedOfflineProviders = _suppressedProviders.ToArray();
        if (IsLoaded) AppSettings.Save(_settings);
    }

    private void UpdateSuppressedProviders(bool yoyo, bool codex, bool buddy, bool deepSeek)
    {
        _providerRunning["yoyo"] = yoyo; _providerRunning["codex"] = codex;
        _providerRunning["workbuddy"] = buddy; _providerRunning["deepseek"] = deepSeek;
        var changed = false;
        foreach (var pair in _providerRunning)
            if (pair.Value) changed |= _suppressedProviders.Remove(pair.Key);
        if (changed) { SaveProviderSuppression(); TransitionProviderVisibility(); }
    }

    private void SetOfflineDismissTarget(string? provider)
    {
        if (_offlineDismissTarget == provider) return;
        _offlineDismissTarget = provider;
        var version = ++_dismissVersion;
        DismissOfflineButton.IsHitTestVisible = provider is not null;
        if (provider is not null) DismissOfflineButton.ToolTip = $"暂时隐藏 {ProviderLabel(provider)}，重新运行后恢复";
        var from = DismissOfflineButton.Visibility == Visibility.Visible ? DismissOfflineButton.Opacity : 0;
        DismissOfflineButton.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(from, provider is null ? 0 : 1, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { if (version == _dismissVersion && provider is null) DismissOfflineButton.Visibility = Visibility.Collapsed; };
        DismissOfflineButton.BeginAnimation(OpacityProperty, fade);
    }

    private void DismissOffline_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_offlineDismissTarget is not { } provider || !_providerRunning.TryGetValue(provider, out var running) || running) return;
        _suppressedProviders.Add(provider);
        SaveProviderSuppression();
        var pending = _pendingCompletions.Where(notice => notice.Provider != ProviderLabel(provider)).ToArray();
        _pendingCompletions.Clear();
        foreach (var notice in pending) _pendingCompletions.Enqueue(notice);
        if (_highlightedProvider == ProviderLabel(provider))
        {
            _activeCompletionNotice = _activeConfirmationNotice = _activeSystemNotice = null;
            ClearProviderHighlight();
        }
        SetOfflineDismissTarget(null);
        TransitionProviderVisibility();
        _ = RefreshStatusAsync();
    }

    private void TransitionProviderVisibility()
    {
        if (!IsLoaded) { ApplyProviderVisibility(); return; }
        var version = ++_providerVisibilityVersion;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) =>
        {
            if (version != _providerVisibilityVersion) return;
            ApplyProviderVisibility();
            if (_expanded) Island.BeginAnimation(System.Windows.Controls.Border.HeightProperty,
                new DoubleAnimation(GetExpandedHeight(), TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            IndicatorPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1,TimeSpan.FromMilliseconds(160)));
            ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(_expanded ? 1 : 0,TimeSpan.FromMilliseconds(160)));
        };
        IndicatorPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0,TimeSpan.FromMilliseconds(100)));
        ExpandedPanel.BeginAnimation(OpacityProperty, fade);
    }

    private void ApplyDeepSeekState()
    {
        var state = !_deepSeekStatus.IsRunning ? "未运行" : !_deepSeekStatus.Available ? "状态不可用"
            : _deepSeekStatus.State switch { "running" => "执行中", "decision" => "待确认", "completed" => "已完成", "error" => "任务异常", "cancelled" => "已停止", "unknown" => "状态待核实", _ => "空闲" };
        DeepSeekStateText.Text = state;
        DeepSeekMiniDot.Fill = !_deepSeekStatus.IsRunning ? OfflineBrush : !_deepSeekStatus.Available || _deepSeekStatus.State is "error" or "unknown" ? ErrorBrush : _deepSeekStatus.IsBusy ? BusyBrush : OnlineBrush;
        DeepSeekDot.Fill = DeepSeekMiniDot.Fill;
        DeepSeekStateText.Foreground = DeepSeekMiniDot.Fill;
        DeepSeekStateText.ToolTip = _deepSeekStatus.Error;
    }

    private void ShowDeepSeekDecision()
    {
        PauseSystemToastForDecision();
        _activeConfirmationNotice = "DeepSeek Harness 需要你的决策 · 请打开应用查看审批或问题";
        if (_deepSeekConfirmationId == _deepSeekStatus.ConfirmationId) return;
        _deepSeekConfirmationId = _deepSeekStatus.ConfirmationId;
        _activeCompletionNotice = _activeSystemNotice = null;
        HighlightProvider("DeepSeek Harness");
        RecentResultText.Text = ActiveNoticeText;
        BeginNotificationHold(); RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden); ExpandIsland(true);
    }

    private bool OpenDeepSeek()
    {
        var executable = ApplicationLocator.FindDeepSeekExecutable(_settings.DeepSeekExecutablePath);
        if (executable is null) return false;
        try { Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    private void OpenDeepSeek_Click(object sender, RoutedEventArgs e)
        => OpenProviderAndAcknowledge("DeepSeek Harness", OpenDeepSeek, e);
}
