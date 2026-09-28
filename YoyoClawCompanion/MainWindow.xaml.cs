using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using YoyoClawCompanion.Services;
using Brush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;
using NativeWindow = YoyoClawCompanion.Services.NativeWindow;
using TextBlock = System.Windows.Controls.TextBlock;
using Canvas = System.Windows.Controls.Canvas;

namespace YoyoClawCompanion;

public partial class MainWindow : Window
{
    private const double IslandMargin = 16;
    private static readonly Brush OnlineBrush = Brush("#3ED598"), BusyBrush = Brush("#F2C94C"), OfflineBrush = Brush("#727C90"), ErrorBrush = Brush("#F2686F"), AccentBrush = Brush("#8FA0FF");
    private readonly YoyoStatusService _statusService = new();
    private readonly WorkBuddyStatusService _workBuddyStatusService = new();
    private readonly WorkBuddyCreditsService _workBuddyCreditsService = new();
    private readonly CodexStatusService _codexStatusService = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private readonly DispatcherTimer _enterTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _completionTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _passThroughTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _zOrderTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private IslandSettings _settings = AppSettings.Load();
    private SettingsWindow? _settingsWindow;
    private bool _holdArmed, _dragging, _refreshing, _expanded, _finishingGesture;
    private bool _manualCollapseUntilPointerExit, _refreshAfterCurrent;
    private bool _collapseHandlePressed, _suppressCollapseHandleClick;
    private bool _completionBaselineReady, _notificationHoldActive;
    private string? _yoyoCompletionId, _codexCompletionId, _workBuddyCompletionId;
    private string? _activeCompletionNotice;
    private string? _activeConfirmationNotice, _workBuddyConfirmationId;
    private string _latestCombinedResult = "尚未检测到任务结果";
    private Brush _secondaryTextBrush = Brush("#9AA5BC");
    private bool _isBalanceSummary, _focusModeHidden, _expandUp;
    private bool _anyBusy;
    private string? _balanceSummaryKey;
    private double _marqueeOffset, _marqueeCycleWidth;
    private long _lastMarqueeTick;
    private bool _marqueeRenderingSubscribed;
    private string? _appliedProviderOrder;
    private bool _reverseHoverHidden;
    private Rect _reverseHoverBoundsPixels;
    private bool _fullscreenOverrideActive, _islandAnimationInProgress;
    private bool _inactivityHidden;
    private bool _taskbarTopmostOverride;
    private bool _horizontalExpansionCompensated;
    private bool _positionInitialized;
    private int _islandAnimationVersion, _reverseFadeVersion, _focusAnimationVersion;
    private double _collapsedAnchorTop;
    private double _collapsedLeftBeforeExpansion;
    private string? _stateFingerprint;
    private DateTimeOffset _lastStateChangeAt = DateTimeOffset.Now;
    private bool _appliedUnchangedAutoHide;
    private double _appliedUnchangedAutoHideMinutes;
    private DateTimeOffset _lastPathCapture = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _holdTimer.Tick += (_, _) => ArmDrag();
        _enterTimer.Tick += (_, _) => { _enterTimer.Stop(); ExpandIsland(); };
        _leaveTimer.Tick += (_, _) => TryCollapseAfterPointerExit();
        _completionTimer.Tick += (_, _) => EndCompletionNotice();
        _passThroughTimer.Tick += (_, _) => CheckReverseHoverExit();
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenOverride();
        _zOrderTimer.Tick += (_, _) => EnsureTaskbarZOrder();
        _codexStatusService.LimitsUpdated += CodexLimitsUpdated;
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Closed += (_, _) => { StopSummaryMarquee(); _passThroughTimer.Stop(); _fullscreenTimer.Stop(); _zOrderTimer.Stop(); _codexStatusService.LimitsUpdated -= CodexLimitsUpdated; SystemEvents.UserPreferenceChanged -= SystemThemeChanged; };
    }

    internal IslandSettings CurrentSettings => _settings;
    internal event EventHandler? PositionChanged;
    private double CollapsedHeight => _settings.IslandHeight;
    internal void OpenHomeFromExternalRequest() => OpenHome();
    internal void RefreshFromExternalRequest() => _ = RefreshStatusAsync();
    internal void WakeFromTray()
    {
        if (!_inactivityHidden) return;
        _lastStateChangeAt = DateTimeOffset.Now;
        _inactivityHidden = false;
        ShowIslandForFocusMode(animate: true);
    }

    internal void ApplySettings(IslandSettings settings, bool persist = true, bool refreshStatus = false, bool preserveMarquee = false)
    {
        settings.CornerRadius = Math.Clamp(settings.CornerRadius, 0, 24);
        settings.Opacity = Math.Clamp(settings.Opacity, 0.55, 1);
        settings.IslandWidth = Math.Clamp(settings.IslandWidth, 190, 400);
        settings.IslandHeight = Math.Clamp(settings.IslandHeight, 32, 72);
        settings.HoverDelayMs = Math.Clamp(settings.HoverDelayMs, 20, 400);
        settings.QuotaScrollSpeed = Math.Clamp(settings.QuotaScrollSpeed, 8, 80);
        settings.CompletionDisplaySeconds = Math.Clamp(settings.CompletionDisplaySeconds, 3, 30);
        settings.MaxResponseLines = Math.Clamp(settings.MaxResponseLines, 1, 6);
        settings.TextSize = Math.Clamp(settings.TextSize, 9, 16);
        settings.UnchangedAutoHideMinutes = Math.Clamp(settings.UnchangedAutoHideMinutes, 1, 60);
        settings.ThemeMode = settings.ThemeMode is "light" or "dark" ? settings.ThemeMode : "system";
        settings.DisplayMode = settings.DisplayMode == "activeOnly" ? "activeOnly" : "always";
        settings.ProviderOrder = NormalizeProviderOrder(settings.ProviderOrder);
        settings.PositionPreset = NormalizePositionPreset(settings.PositionPreset);
        _settings = settings;
        if (_appliedUnchangedAutoHide != settings.EnableUnchangedAutoHide
            || Math.Abs(_appliedUnchangedAutoHideMinutes - settings.UnchangedAutoHideMinutes) > .01)
        {
            var wasHiddenByInactivity = _inactivityHidden;
            _appliedUnchangedAutoHide = settings.EnableUnchangedAutoHide;
            _appliedUnchangedAutoHideMinutes = settings.UnchangedAutoHideMinutes;
            _lastStateChangeAt = DateTimeOffset.Now;
            _inactivityHidden = false;
            if (wasHiddenByInactivity && IsLoaded) ShowIslandForFocusMode(animate: true);
        }
        if (!settings.EnableReverseHover) RestoreReverseHoverIsland();
        else if (!_notificationHoldActive)
        {
            if (_expanded) CollapseIsland(true);
            if (IsLoaded && Island.IsMouseOver) Dispatcher.BeginInvoke(HideIslandForReverseHover, DispatcherPriority.Input);
        }
        Island.CornerRadius = new CornerRadius(settings.CornerRadius);
        var selectionRadius = new CornerRadius(settings.CornerRadius);
        YoyoRowButton.Tag = selectionRadius;
        CodexRowButton.Tag = selectionRadius;
        WorkBuddyRowButton.Tag = selectionRadius;
        if (!_reverseHoverHidden)
        {
            Island.BeginAnimation(OpacityProperty, null);
            Island.Opacity = settings.Opacity;
        }
        if (!_expanded)
        {
            Island.BeginAnimation(WidthProperty, null);
            Island.BeginAnimation(HeightProperty, null);
            Island.Width = settings.IslandWidth;
            Island.Height = CollapsedHeight;
        }
        Topmost = settings.Topmost;
        YoyoIndicatorButton.Visibility = settings.ShowYoyo ? Visibility.Visible : Visibility.Collapsed;
        CodexIndicatorButton.Visibility = settings.ShowCodex ? Visibility.Visible : Visibility.Collapsed;
        WorkBuddyIndicatorButton.Visibility = settings.ShowWorkBuddy ? Visibility.Visible : Visibility.Collapsed;
        ApplyProviderOrder();
        ApplyExpandedContentOrder();
        _enterTimer.Interval = TimeSpan.FromMilliseconds(settings.HoverDelayMs);
        _completionTimer.Interval = TimeSpan.FromSeconds(settings.CompletionDisplaySeconds);
        ApplyTypography();
        SetLaunchControls(settings.EnableAppLaunch);
        if (!settings.EnableHoverExpansion && _expanded) CollapseIsland(true);
        ApplyTheme();
        Island.Effect = settings.ShowShadow ? (System.Windows.Media.Effects.Effect)FindResource("IslandShadow") : null;
        ((App)Application.Current).SetTrayIconVisible(settings.ShowTrayIcon);
        StartupRegistration.SetEnabled(settings.StartWithWindows);
        if (settings.EnableFullscreenActiveOnly) _fullscreenTimer.Start();
        else
        {
            _fullscreenTimer.Stop();
            _fullscreenOverrideActive = false;
        }
        if (persist) AppSettings.Save(_settings);
        if (refreshStatus && IsLoaded) _ = RefreshStatusAsync();
        if (IsLoaded && _positionInitialized)
        {
            if (!_expanded)
            {
                ClampCollapsedPosition();
                UpdateCollapsedAnchorFromCurrentGeometry();
                OrientCollapsedIsland();
                SavePosition();
            }
            UpdateFullscreenOverride();
            UpdateDisplayMode();
            EnsureTaskbarZOrder();
        }
        if (!preserveMarquee) ScheduleSummaryMarquee();
    }

    private void ApplyTypography()
    {
        var textSize = _settings.TextSize;
        var showTwoRows = (textSize + 2) * 1.3 + textSize * 1.3 <= CollapsedHeight - 4;
        HeadlineText.FontSize = showTwoRows ? textSize + 2 : textSize + 1;
        SummaryText.FontSize = SummaryTextClone.FontSize = showTwoRows ? textSize : textSize + 1;
        YoyoLabel.FontSize = CodexLabel.FontSize = WorkBuddyLabel.FontSize = textSize + 1;
        StateText.FontSize = PointsText.FontSize = CodexStateText.FontSize = WorkBuddyStateText.FontSize = textSize;
        RecentResultText.FontSize = textSize;

        var lineHeight = Math.Ceiling(textSize * 1.45);
        var rowHeight = Math.Max(24, Math.Ceiling(textSize * 1.7));
        RecentResultText.LineHeight = lineHeight;
        RecentResultText.MaxHeight = lineHeight * _settings.MaxResponseLines;
        ApplyExpandedContentOrder(rowHeight);
        HeaderRow.Height = new GridLength(Math.Max(30, CollapsedHeight - 2));

        HeadlineText.Visibility = Visibility.Visible;
        HeadlineText.VerticalAlignment = VerticalAlignment.Center;
        SummaryViewport.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Controls.Grid.SetRow(HeadlineText, 0);
        System.Windows.Controls.Grid.SetColumn(HeadlineText, 0);
        System.Windows.Controls.Grid.SetColumnSpan(HeadlineText, showTwoRows ? 2 : 1);
        System.Windows.Controls.Grid.SetRow(SummaryViewport, showTwoRows ? 1 : 0);
        System.Windows.Controls.Grid.SetColumn(SummaryViewport, showTwoRows ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(SummaryViewport, showTwoRows ? 2 : 1);
        SummaryViewport.Margin = showTwoRows ? new Thickness(0) : new Thickness(10, 0, 0, 0);
        SummaryViewport.Height = Math.Ceiling(SummaryText.FontSize * 1.45);

        if (_expanded)
        {
            Island.BeginAnimation(HeightProperty, null);
            Island.Height = GetExpandedHeight();
        }
    }

    private double GetExpandedHeight()
    {
        var rowHeight = Math.Max(24, Math.Ceiling(_settings.TextSize * 1.7));
        var lineHeight = Math.Ceiling(_settings.TextSize * 1.45);
        return Math.Min(Height - IslandMargin * 2, 42 + CollapsedHeight + rowHeight * 3 + lineHeight * _settings.MaxResponseLines);
    }

    private static string NormalizeProviderOrder(string? value)
    {
        var providers = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant()).ToArray() ?? [];
        return providers.Length == 3 && providers.Distinct(StringComparer.Ordinal).Count() == 3
            && providers.All(item => item is "yoyo" or "codex" or "workbuddy")
            ? string.Join(',', providers)
            : "yoyo,codex,workbuddy";
    }

    private static string NormalizePositionPreset(string? value)
        => value is "topLeft" or "topCenter" or "topRight" or "bottomLeft" or "bottomCenter" or "bottomRight"
            ? value
            : "custom";

    private string[] ProviderOrder() => _settings.ProviderOrder.Split(',');

    private static string ProviderLabel(string provider) => provider switch
    {
        "yoyo" => "YOYO Claw",
        "codex" => "Codex",
        _ => "WorkBuddy"
    };

    private void ApplyProviderOrder()
    {
        if (string.Equals(_appliedProviderOrder, _settings.ProviderOrder, StringComparison.Ordinal))
        {
            ApplyExpandedContentOrder();
            return;
        }
        var indicators = new Dictionary<string, UIElement>
        {
            ["yoyo"] = YoyoIndicatorButton,
            ["codex"] = CodexIndicatorButton,
            ["workbuddy"] = WorkBuddyIndicatorButton
        };
        foreach (var indicator in indicators.Values) IndicatorPanel.Children.Remove(indicator);
        foreach (var provider in ProviderOrder()) IndicatorPanel.Children.Add(indicators[provider]);

        _appliedProviderOrder = _settings.ProviderOrder;
        ApplyExpandedContentOrder();
        _balanceSummaryKey = null;
    }

    private void ApplyExpandedContentOrder(double? statusRowHeight = null)
    {
        var rowHeight = statusRowHeight ?? Math.Max(24, Math.Ceiling(_settings.TextSize * 1.7));
        var replyFirst = _expandUp && _settings.PutReplyFirstWhenExpandedUp;
        ExpandedRow0.Height = replyFirst ? new GridLength(1, GridUnitType.Star) : new GridLength(rowHeight);
        ExpandedRow1.Height = new GridLength(rowHeight);
        ExpandedRow2.Height = new GridLength(rowHeight);
        ExpandedRow3.Height = replyFirst ? new GridLength(rowHeight) : new GridLength(1, GridUnitType.Star);

        var rows = new Dictionary<string, UIElement[]>
        {
            ["yoyo"] = [StateDot, YoyoLabel, StateText, PointsText, YoyoRowButton],
            ["codex"] = [CodexDot, CodexLabel, CodexStateText, CodexRowButton],
            ["workbuddy"] = [WorkBuddyDot, WorkBuddyLabel, WorkBuddyStateText, WorkBuddyRowButton]
        };
        var firstStatusRow = replyFirst ? 1 : 0;
        var order = ProviderOrder();
        for (var index = 0; index < order.Length; index++)
            foreach (var element in rows[order[index]]) System.Windows.Controls.Grid.SetRow(element, firstStatusRow + index);
        System.Windows.Controls.Grid.SetRow(RecentBorder, replyFirst ? 0 : 3);
        RecentBorder.Margin = replyFirst ? new Thickness(0, 0, 0, 6) : new Thickness(0, 6, 0, 0);
        ApplyCollapseHandlePosition();
    }

    private void ApplyCollapseHandlePosition()
    {
        System.Windows.Controls.Grid.SetRow(CollapseHandleButton, _expandUp ? 0 : 1);
        CollapseHandleButton.VerticalAlignment = _expandUp ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        CollapseHandleButton.VerticalContentAlignment = _expandUp ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        CollapseHandleButton.Margin = _expandUp ? new Thickness(0, -8, 0, 0) : new Thickness(0, 0, 0, -8);
    }

    internal void ResetPosition()
        => MoveToPositionPreset("topCenter");

    internal void MoveToPositionPreset(string preset)
    {
        preset = NormalizePositionPreset(preset);
        if (preset == "custom") return;
        if (_expanded || _islandAnimationInProgress) CompleteCollapseImmediately();
        Island.UpdateLayout();
        var bounds = GetIslandScreenPixelBounds();
        var monitor = NativeWindow.GetMonitorBounds(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        const double edgePadding = 8;
        var targetLeft = preset switch
        {
            "topLeft" or "bottomLeft" => monitor.Left + edgePadding,
            "topRight" or "bottomRight" => monitor.Right - bounds.Width - edgePadding,
            _ => monitor.Left + (monitor.Width - bounds.Width) / 2
        };
        var targetTop = preset.StartsWith("bottom", StringComparison.Ordinal)
            ? monitor.Bottom - bounds.Height - edgePadding
            : monitor.Top + edgePadding;
        var correction = DevicePixelsToDips(new Vector(targetLeft - bounds.Left, targetTop - bounds.Top));
        Left += correction.X;
        Top += correction.Y;
        _settings.PositionPreset = preset;
        UpdateCollapsedAnchorFromCurrentGeometry();
        OrientCollapsedIsland();
        ClampCollapsedPosition();
        UpdateCollapsedAnchorFromCurrentGeometry();
        SavePosition();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void NudgePosition(double horizontal, double vertical)
    {
        if (_expanded || _islandAnimationInProgress) CompleteCollapseImmediately();
        Left += horizontal;
        Top += vertical;
        _settings.PositionPreset = "custom";
        ClampCollapsedPosition();
        UpdateCollapsedAnchorFromCurrentGeometry();
        OrientCollapsedIsland();
        SavePosition();
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084) return IntPtr.Zero;
        if (_focusModeHidden || _reverseHoverHidden || Island.Visibility != Visibility.Visible)
        {
            handled = true;
            return new IntPtr(-1);
        }
        var packed = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
        var bounds = Island.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Island.ActualWidth, Island.ActualHeight));
        if (bounds.Contains(point)) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var savedX = _settings.X;
        var savedY = _settings.Y;
        ApplySettings(_settings, false);
        if (savedX is double x && savedY is double y)
        {
            Left = x;
            Top = y;
            await Dispatcher.InvokeAsync(ClampCollapsedPosition, DispatcherPriority.Loaded);
        }
        else ResetPosition();
        _collapsedAnchorTop = Top;
        OrientCollapsedIsland();
        _positionInitialized = true;
        if (NormalizePositionPreset(_settings.PositionPreset) != "custom")
            MoveToPositionPreset(_settings.PositionPreset);
        else SavePosition();
        await RefreshStatusAsync();
        _refreshTimer.Start();
        _zOrderTimer.Start();
        if (_settings.EnableFullscreenActiveOnly) _fullscreenTimer.Start();
    }

    private void SavePosition()
    {
        if (!IsLoaded) return;
        _settings.X = Left;
        _settings.Y = _expandUp ? _collapsedAnchorTop : Top;
        AppSettings.Save(_settings);
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            CaptureExecutablePaths();
            var workBuddyTask = MeasureAsync(_workBuddyStatusService.ReadAsync());
            var workBuddyCreditsTask = MeasureAsync(_workBuddyCreditsService.ReadAsync(_settings.ShowWorkBuddyCredits));
            var codexTask = MeasureAsync(_codexStatusService.ReadAsync(_settings.EnableCodexActivityDetection, _settings.ShowCodexLimits));
            var yoyoTask = MeasureAsync(_statusService.ReadAsync());

            // Codex activity is intentionally applied first. Slow YOYO bridge or quota reads must not
            // delay the visible busy state or active-only island wake-up.
            var codexResult = await codexTask;
            var codex = codexResult.Value;
            ApplyCodexVisualState(codex, provisional: true);

            var statusResult = await yoyoTask;
            var workBuddyResult = await workBuddyTask;
            var workBuddyCreditsResult = await workBuddyCreditsTask;
            var status = statusResult.Value;
            var workBuddy = workBuddyResult.Value;
            var workBuddyCredits = workBuddyCreditsResult.Value;
            WorkBuddyMiniDot.Fill = !workBuddy.IsRunning ? OfflineBrush : !workBuddy.DataAvailable ? ErrorBrush : workBuddy.IsBusy ? BusyBrush : OnlineBrush;
            YoyoMiniDot.Fill = !status.IsYoyoRunning ? OfflineBrush : !status.TaskStatusAvailable ? ErrorBrush : status.IsBusy ? BusyBrush : status.LastTaskFailed ? ErrorBrush : OnlineBrush;
            StateDot.Fill = YoyoMiniDot.Fill;
            WorkBuddyDot.Fill = WorkBuddyMiniDot.Fill;
            PointsText.Inlines.Clear();
            PointsText.Inlines.Add(new Run("· ") { Foreground = Brush("#182033") });
            PointsText.Inlines.Add(new Run(status.RemainingPoints is double remaining ? $"{remaining:0.##} 积分" : "积分 --") { Foreground = AccentBrush });
            StateText.Text = !status.IsYoyoRunning ? "未运行" : !status.TaskStatusAvailable ? "接口不可用" : status.IsBusy ? "忙碌中" : status.LastTaskFailed ? "最近任务失败" : "空闲";
            StateText.Foreground = YoyoMiniDot.Fill;
            ApplyCodexVisualState(codex, provisional: false);
            SetWorkBuddyStateText(workBuddy, workBuddyCredits);
            _latestCombinedResult = SelectLatestResponse(status, codex, workBuddy);
            UpdateConfirmationNotice(workBuddy);
            var completion = DetectCompletion(status, codex, workBuddy);
            RecentResultText.Text = _activeConfirmationNotice ?? _activeCompletionNotice ?? _latestCombinedResult;
            _anyBusy = status.IsBusy || codex.IsBusy || workBuddy.IsBusy;
            UpdateInactivityState(status, codex, workBuddy, workBuddyCredits);
            _refreshTimer.Interval = TimeSpan.FromSeconds(_anyBusy ? 2 : 5);
            UpdateHeadline(status, codex, workBuddy, workBuddyCredits);
            WriteStatusSnapshot(status, codex, workBuddy, workBuddyCredits,
                new RefreshTimings(statusResult.ElapsedMilliseconds, codexResult.ElapsedMilliseconds,
                    workBuddyResult.ElapsedMilliseconds, workBuddyCreditsResult.ElapsedMilliseconds));
            if (_activeConfirmationNotice is null && completion is not null && _settings.EnableCompletionNotifications) ShowCompletionNotice(completion);
            else UpdateDisplayMode();
        }
        catch (Exception error)
        {
            HeadlineText.Text = "状态刷新失败";
            HeadlineText.Foreground = ErrorBrush;
            SetPlainSummary(error.GetType().Name);
        }
        finally
        {
            _refreshing = false;
            if (_refreshAfterCurrent)
            {
                _refreshAfterCurrent = false;
                _ = RefreshStatusAsync();
            }
        }
    }

    private void CodexLimitsUpdated(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_refreshing) _refreshAfterCurrent = true;
            else _ = RefreshStatusAsync();
        });

    private void ApplyCodexVisualState(CodexStatus codex, bool provisional)
    {
        CodexMiniDot.Fill = !codex.IsRunning ? OfflineBrush : codex.IsBusy ? BusyBrush : OnlineBrush;
        CodexDot.Fill = CodexMiniDot.Fill;
        SetCodexStateText(codex);
        if (!provisional || !codex.IsBusy) return;

        _anyBusy = true;
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        HeadlineText.Text = "Codex 执行中";
        HeadlineText.Foreground = BusyBrush;
        SetPlainSummary(CodexSummary(codex), BusyBrush);
        if (UsesActiveOnlyDisplay && !_notificationHoldActive) ShowIslandForFocusMode();
    }

    private static async Task<TimedResult<T>> MeasureAsync<T>(Task<T> task)
    {
        var watch = Stopwatch.StartNew();
        var value = await task;
        watch.Stop();
        return new TimedResult<T>(value, watch.ElapsedMilliseconds);
    }

    private void UpdateHeadline(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy, WorkBuddyCredits workBuddyCredits)
    {
        var points = yoyo.RemainingPoints is double value ? $"{value:0.##} 积分" : "积分 --";
        var busyByProvider = new Dictionary<string, bool> { ["yoyo"] = yoyo.IsBusy, ["codex"] = codex.IsBusy, ["workbuddy"] = workBuddy.IsBusy };
        var busyProviders = ProviderOrder().Where(provider => busyByProvider[provider]).Select(ProviderLabel).ToList();
        if (workBuddy.RequiresConfirmation) { HeadlineText.Text = "WorkBuddy 待确认"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(workBuddy.Summary, BusyBrush); }
        else if (!yoyo.IsYoyoRunning) { HeadlineText.Text = "YOYO 未运行"; HeadlineText.Foreground = OfflineBrush; SetPlainSummary(codex.IsRunning || workBuddy.IsRunning ? "其他助手已就绪" : "未检测到运行实例"); }
        else if (!yoyo.TaskStatusAvailable) { HeadlineText.Text = "YOYO 状态不可用"; HeadlineText.Foreground = ErrorBrush; SetPlainSummary(points, AccentBrush); }
        else if (busyProviders.Count > 1) { HeadlineText.Text = $"{busyProviders.Count} 个助手执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(string.Join("、", busyProviders), BusyBrush); }
        else if (yoyo.IsBusy) { HeadlineText.Text = "YOYO 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(yoyo.RecentResult); }
        else if (codex.IsBusy) { HeadlineText.Text = "Codex 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(CodexSummary(codex)); }
        else if (workBuddy.IsBusy) { HeadlineText.Text = "WorkBuddy 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(workBuddy.Summary); }
        else if (yoyo.LastTaskFailed) { HeadlineText.Text = "1 项需要处理"; HeadlineText.Foreground = ErrorBrush; SetPlainSummary(yoyo.RecentResult); }
        else { HeadlineText.Text = "全部就绪"; HeadlineText.Foreground = OnlineBrush; SetBalanceSummary(yoyo, codex, workBuddyCredits); }
    }

    private void SetBalanceSummary(YoyoStatus yoyo, CodexStatus codex, WorkBuddyCredits workBuddy)
    {
        var values = new Dictionary<string, string>
        {
            ["yoyo"] = yoyo.RemainingPoints is double yoyoPoints ? $"{yoyoPoints:0.##} 积分" : "--",
            ["codex"] = codex.FiveHourRemainingPercent is int fiveHour ? $"5小时 {fiveHour}%" : codex.WeeklyRemainingPercent is int weekly ? $"本周 {weekly}%" : "--",
            ["workbuddy"] = workBuddy.Available && workBuddy.Remaining is double credits ? $"{credits:0.##} 积分" : "--"
        };
        var key = $"{_settings.ProviderOrder}|{string.Join('|', ProviderOrder().Select(provider => values[provider]))}";
        if (_isBalanceSummary && string.Equals(_balanceSummaryKey, key, StringComparison.Ordinal)) return;

        _isBalanceSummary = true;
        _balanceSummaryKey = key;
        PopulateBalanceSummary(SummaryText, values);
        PopulateBalanceSummary(SummaryTextClone, values);
        ScheduleSummaryMarquee();
    }

    private void PopulateBalanceSummary(TextBlock target, IReadOnlyDictionary<string, string> values)
    {
        target.Inlines.Clear();
        var first = true;
        foreach (var provider in ProviderOrder())
        {
            if (!first) target.Inlines.Add(new Run("  |  ") { Foreground = _secondaryTextBrush });
            AddSummaryPart(target, ProviderLabel(provider) + " ", values[provider]);
            first = false;
        }
    }

    private void AddSummaryPart(TextBlock target, string label, string value)
    {
        target.Inlines.Add(new Run(label) { Foreground = _secondaryTextBrush });
        target.Inlines.Add(new Run(value) { Foreground = AccentBrush, FontWeight = FontWeights.SemiBold });
    }

    private void SetPlainSummary(string value, Brush? foreground = null)
    {
        _isBalanceSummary = false;
        _balanceSummaryKey = null;
        StopSummaryMarquee();
        SummaryText.Inlines.Clear();
        SummaryTextClone.Inlines.Clear();
        SummaryTextClone.Visibility = Visibility.Collapsed;
        var run = new Run(value);
        if (foreground is not null) run.Foreground = foreground;
        SummaryText.Inlines.Add(run);
    }

    private static string SelectLatestResponse(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy)
    {
        var candidates = new List<CompletionNotice>();
        if (yoyo.RecentUpdatedAt is DateTimeOffset yoyoAt && !string.IsNullOrWhiteSpace(yoyo.RecentResult) && yoyo.TaskStatusAvailable)
            candidates.Add(new("YOYO Claw", yoyo.RecentResult, yoyoAt));
        if (codex.RecentResponseAt is DateTimeOffset codexAt && !string.IsNullOrWhiteSpace(codex.RecentResponse))
            candidates.Add(new("Codex", codex.RecentResponse!, codexAt));
        if (workBuddy.RecentResponseAt is DateTimeOffset workBuddyAt && !string.IsNullOrWhiteSpace(workBuddy.RecentResponse))
            candidates.Add(new("WorkBuddy", workBuddy.RecentResponse!, workBuddyAt));
        var latest = candidates.OrderByDescending(item => item.CompletedAt).FirstOrDefault();
        return latest is null ? yoyo.RecentResult : $"{latest.Provider} · {latest.Response}";
    }

    private CompletionNotice? DetectCompletion(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy)
    {
        var yoyoId = !yoyo.IsBusy && yoyo.TaskStatusAvailable && yoyo.RecentUpdatedAt is DateTimeOffset yoyoAt
            ? $"{yoyoAt:O}|{yoyo.RecentResult}" : null;
        var codexId = !codex.IsBusy ? codex.RecentResponseId : null;
        var workBuddyId = !workBuddy.IsBusy ? workBuddy.RecentResponseId : null;

        if (!_completionBaselineReady)
        {
            _yoyoCompletionId = yoyoId;
            _codexCompletionId = codexId;
            _workBuddyCompletionId = workBuddyId;
            _completionBaselineReady = true;
            return null;
        }

        var notices = new List<CompletionNotice>();
        if (yoyoId is not null && yoyoId != _yoyoCompletionId && !string.IsNullOrWhiteSpace(yoyo.RecentResult))
            notices.Add(new("YOYO Claw", yoyo.RecentResult, yoyo.RecentUpdatedAt ?? DateTimeOffset.Now));
        if (codexId is not null && codexId != _codexCompletionId && !string.IsNullOrWhiteSpace(codex.RecentResponse))
            notices.Add(new("Codex", codex.RecentResponse!, codex.RecentResponseAt ?? DateTimeOffset.Now));
        if (workBuddyId is not null && workBuddyId != _workBuddyCompletionId && !string.IsNullOrWhiteSpace(workBuddy.RecentResponse))
            notices.Add(new("WorkBuddy", workBuddy.RecentResponse!, workBuddy.RecentResponseAt ?? DateTimeOffset.Now));

        if (yoyoId is not null) _yoyoCompletionId = yoyoId;
        if (codexId is not null) _codexCompletionId = codexId;
        if (workBuddyId is not null) _workBuddyCompletionId = workBuddyId;
        return notices.OrderByDescending(item => item.CompletedAt).FirstOrDefault();
    }

    private void ShowCompletionNotice(CompletionNotice notice)
    {
        _activeCompletionNotice = $"{notice.Provider} 完成了任务 · {notice.Response}";
        RecentResultText.Text = _activeCompletionNotice;
        BeginNotificationHold();
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
    }

    private void UpdateConfirmationNotice(WorkBuddyStatus workBuddy)
    {
        if (!workBuddy.RequiresConfirmation || !_settings.EnableConfirmationNotifications)
        {
            var wasActive = _activeConfirmationNotice is not null;
            _activeConfirmationNotice = null;
            _workBuddyConfirmationId = null;
            if (wasActive)
            {
                _completionTimer.Stop();
                _notificationHoldActive = false;
                if (!Island.IsMouseOver) CollapseIsland(true);
            }
            return;
        }

        _activeConfirmationNotice = $"WorkBuddy 需要你的确认 · {workBuddy.ConfirmationPrompt ?? "请打开 WorkBuddy 查看并选择"}";
        if (workBuddy.ConfirmationId == _workBuddyConfirmationId) return;
        _workBuddyConfirmationId = workBuddy.ConfirmationId;
        _activeCompletionNotice = null;
        RecentResultText.Text = _activeConfirmationNotice;
        BeginNotificationHold();
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
    }

    private void BeginNotificationHold()
    {
        _completionTimer.Stop();
        _completionTimer.Interval = TimeSpan.FromSeconds(_settings.CompletionDisplaySeconds);
        _notificationHoldActive = true;
        _completionTimer.Start();
    }

    private void EndCompletionNotice()
    {
        _completionTimer.Stop();
        _notificationHoldActive = false;
        _activeCompletionNotice = null;
        RecentResultText.Text = _activeConfirmationNotice ?? _latestCombinedResult;
        if (_settings.EnableReverseHover && Island.IsMouseOver)
        {
            CollapseIsland(true);
            HideIslandForReverseHover();
            return;
        }
        if (_inactivityHidden || (UsesActiveOnlyDisplay && !_anyBusy))
        {
            CollapseIsland(true);
            HideIslandForFocusMode(animate: _inactivityHidden);
        }
        else if (!Island.IsMouseOver) CollapseIsland();
    }

    private void CollapseHandle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressCollapseHandleClick)
        {
            _suppressCollapseHandleClick = false;
            e.Handled = true;
            return;
        }
        _manualCollapseUntilPointerExit = true;
        _completionTimer.Stop();
        _notificationHoldActive = false;
        _activeCompletionNotice = null;
        RecentResultText.Text = _activeConfirmationNotice ?? _latestCombinedResult;
        CollapseIsland(true);
        e.Handled = true;
    }

    private void UpdateDisplayMode()
    {
        if (_activeCompletionNotice is not null || _activeConfirmationNotice is not null)
        {
            ShowIslandForFocusMode(animate: _inactivityHidden);
            return;
        }
        if (_inactivityHidden)
        {
            HideIslandForFocusMode(animate: true);
            return;
        }
        if (!UsesActiveOnlyDisplay)
        {
            ShowIslandForFocusMode();
            return;
        }
        if (_anyBusy)
        {
            ShowIslandForFocusMode();
        }
        else HideIslandForFocusMode();
    }

    private void UpdateInactivityState(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy, WorkBuddyCredits credits)
    {
        var fingerprint = string.Join('|',
            yoyo.IsYoyoRunning, yoyo.TaskStatusAvailable, yoyo.IsBusy, yoyo.LastTaskFailed, yoyo.RemainingPoints, yoyo.RecentResult,
            codex.IsRunning, codex.IsBusy, codex.FiveHourRemainingPercent, codex.WeeklyRemainingPercent, codex.RecentResponse,
            workBuddy.IsRunning, workBuddy.DataAvailable, workBuddy.IsBusy, workBuddy.RequiresConfirmation, workBuddy.Summary,
            workBuddy.RecentResponse, credits.Available, credits.Remaining);
        var now = DateTimeOffset.Now;
        if (!string.Equals(_stateFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _stateFingerprint = fingerprint;
            _lastStateChangeAt = now;
            var wasHidden = _inactivityHidden;
            _inactivityHidden = false;
            if (wasHidden)
            {
                ShowIslandForFocusMode(animate: true);
                if (_expanded) CollapseIsland(true);
            }
            return;
        }

        if (!_settings.EnableUnchangedAutoHide)
        {
            _inactivityHidden = false;
            return;
        }
        if (now - _lastStateChangeAt >= TimeSpan.FromMinutes(_settings.UnchangedAutoHideMinutes))
            _inactivityHidden = true;
    }

    private bool UsesActiveOnlyDisplay => _settings.DisplayMode == "activeOnly" || _fullscreenOverrideActive;

    private void UpdateFullscreenOverride()
    {
        var active = _settings.EnableFullscreenActiveOnly
            && IsLoaded
            && NativeWindow.IsForegroundWindowFullscreen(new WindowInteropHelper(this).Handle);
        if (active == _fullscreenOverrideActive) return;
        _fullscreenOverrideActive = active;
        UpdateDisplayMode();
    }

    private void EnsureTaskbarZOrder()
    {
        if (!IsLoaded) return;
        var visible = Island.Visibility == Visibility.Visible && !_focusModeHidden && !_reverseHoverHidden;
        var overlapsTaskbar = visible && NativeWindow.IntersectsTaskbar(GetIslandScreenPixelBounds());
        var desiredInterval = TimeSpan.FromMilliseconds(overlapsTaskbar ? 120 : 750);
        if (_zOrderTimer.Interval != desiredInterval) _zOrderTimer.Interval = desiredInterval;
        var handle = new WindowInteropHelper(this).Handle;
        if (overlapsTaskbar)
        {
            NativeWindow.SetTopmostWithoutActivation(handle, true);
            _taskbarTopmostOverride = !_settings.Topmost;
        }
        else if (_taskbarTopmostOverride)
        {
            NativeWindow.SetTopmostWithoutActivation(handle, false);
            _taskbarTopmostOverride = false;
        }
    }

    private void HideIslandForFocusMode(bool animate = false)
    {
        if (_focusModeHidden) return;
        if (_expanded) CollapseIsland(true);
        _enterTimer.Stop();
        _leaveTimer.Stop();
        StopSummaryMarquee();
        _reverseFadeVersion++;
        var animationVersion = ++_focusAnimationVersion;
        _focusModeHidden = true;
        Island.BeginAnimation(OpacityProperty, null);
        Island.Opacity = _settings.Opacity;
        IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        IslandVisibilityScale.ScaleX = 1;
        if (!animate)
        {
            Island.Visibility = Visibility.Hidden;
            return;
        }

        var squeeze = new DoubleAnimation(1, .02, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        squeeze.Completed += (_, _) =>
        {
            if (animationVersion != _focusAnimationVersion || !_focusModeHidden) return;
            Island.Visibility = Visibility.Hidden;
            Island.BeginAnimation(OpacityProperty, null);
            Island.Opacity = _settings.Opacity;
            IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            IslandVisibilityScale.ScaleX = 1;
        };
        Island.BeginAnimation(OpacityProperty, new DoubleAnimation(_settings.Opacity, 0, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        }, HandoffBehavior.SnapshotAndReplace);
        IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, squeeze, HandoffBehavior.SnapshotAndReplace);
    }

    private void ShowIslandForFocusMode(bool animate = false)
    {
        if (!_focusModeHidden) return;
        _focusModeHidden = false;
        var animationVersion = ++_focusAnimationVersion;
        if (!_reverseHoverHidden)
        {
            Island.Visibility = Visibility.Visible;
            Island.BeginAnimation(OpacityProperty, null);
            IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            if (animate)
            {
                Island.Opacity = 0;
                IslandVisibilityScale.ScaleX = .02;
                var reveal = new DoubleAnimation(.02, 1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new BackEase { Amplitude = .18, EasingMode = EasingMode.EaseOut }
                };
                reveal.Completed += (_, _) =>
                {
                    if (animationVersion != _focusAnimationVersion || _focusModeHidden) return;
                    IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    IslandVisibilityScale.ScaleX = 1;
                };
                IslandVisibilityScale.BeginAnimation(ScaleTransform.ScaleXProperty, reveal, HandoffBehavior.SnapshotAndReplace);
                Island.BeginAnimation(OpacityProperty, new DoubleAnimation(0, _settings.Opacity, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                Island.Opacity = _settings.Opacity;
                IslandVisibilityScale.ScaleX = 1;
            }
            ScheduleSummaryMarquee();
            EnsureTaskbarZOrder();
        }
    }

    private void HideIslandForReverseHover()
    {
        if (!_settings.EnableReverseHover || _reverseHoverHidden || _notificationHoldActive) return;
        _enterTimer.Stop();
        _leaveTimer.Stop();
        StopSummaryMarquee();
        _reverseHoverBoundsPixels = GetIslandScreenPixelBounds();
        _reverseHoverBoundsPixels.Inflate(6, 6);
        _reverseHoverHidden = true;
        _passThroughTimer.Start();
        var fadeVersion = ++_reverseFadeVersion;
        var fade = new DoubleAnimation(Island.Opacity, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (fadeVersion != _reverseFadeVersion || !_reverseHoverHidden) return;
            Island.Visibility = Visibility.Hidden;
            Island.BeginAnimation(OpacityProperty, null);
            Island.Opacity = _settings.Opacity;
        };
        Island.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private void CheckReverseHoverExit()
    {
        if (!_reverseHoverHidden) { _passThroughTimer.Stop(); return; }
        var cursor = NativeWindow.GetCursorPosition();
        if (double.IsNaN(cursor.X) || _reverseHoverBoundsPixels.Contains(cursor)) return;
        RestoreReverseHoverIsland();
    }

    private void RestoreReverseHoverIsland()
    {
        if (!_reverseHoverHidden) return;
        _passThroughTimer.Stop();
        _reverseHoverHidden = false;
        var fadeVersion = ++_reverseFadeVersion;
        if (!_focusModeHidden)
        {
            Island.Visibility = Visibility.Visible;
            Island.BeginAnimation(OpacityProperty, null);
            Island.Opacity = 0;
            var fade = new DoubleAnimation(0, _settings.Opacity, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) =>
            {
                if (fadeVersion != _reverseFadeVersion || _reverseHoverHidden) return;
                Island.BeginAnimation(OpacityProperty, null);
                Island.Opacity = _settings.Opacity;
            };
            Island.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            ScheduleSummaryMarquee();
            EnsureTaskbarZOrder();
        }
    }

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _suppressCollapseHandleClick = false;
        _collapseHandlePressed = _expanded && CollapseHandleButton.IsMouseOver;
        _enterTimer.Stop(); _leaveTimer.Stop();
        _dragging = false; _holdArmed = false;
        _holdTimer.Start();
    }

    private void ArmDrag()
    {
        _holdTimer.Stop();
        if (Mouse.LeftButton != MouseButtonState.Pressed || !Island.IsMouseOver) return;
        if (_collapseHandlePressed) _suppressCollapseHandleClick = true;
        _holdArmed = true;
        if (_expanded || _expandUp) CollapseIslandImmediatelyForDrag();
        _dragging = true;
        Island.Cursor = Cursors.SizeAll; Island.Opacity = Math.Max(.55, _settings.Opacity - .15);
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally { FinishPointerGesture(); }
    }

    private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FinishPointerGesture();
    private void Island_LostMouseCapture(object sender, MouseEventArgs e) => FinishPointerGesture();
    private void FinishPointerGesture()
    {
        if (_finishingGesture) return;
        _finishingGesture = true;
        _holdTimer.Stop(); _holdArmed = false; Island.Cursor = Cursors.Arrow; Island.Opacity = _settings.Opacity;
        _collapseHandlePressed = false;
        if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
        if (_dragging)
        {
            _settings.PositionPreset = "custom";
            ClampCollapsedPosition();
            SnapToHorizontalCenter();
            UpdateCollapsedAnchorFromCurrentGeometry();
            OrientCollapsedIsland();
            SavePosition();
            PositionChanged?.Invoke(this, EventArgs.Empty);
            EnsureTaskbarZOrder();
        }
        _dragging = false;
        _finishingGesture = false;
        if (Island.IsMouseOver)
        {
            if (_settings.EnableReverseHover) HideIslandForReverseHover();
            else _enterTimer.Start();
        }
    }

    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop();
        if (_manualCollapseUntilPointerExit) return;
        if (_settings.EnableReverseHover && !_notificationHoldActive)
        {
            HideIslandForReverseHover();
            return;
        }
        if (_settings.EnableHoverExpansion && !_dragging && !_holdArmed && !_expanded && !_islandAnimationInProgress) _enterTimer.Start();
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _manualCollapseUntilPointerExit = false;
        _enterTimer.Stop();
        if (!_dragging && !_holdArmed && _expanded && !_islandAnimationInProgress && Island.ContextMenu?.IsOpen != true) _leaveTimer.Start();
    }

    private void TryCollapseAfterPointerExit()
    {
        _leaveTimer.Stop();
        if (!_expanded || _dragging || _holdArmed || _islandAnimationInProgress || Island.ContextMenu?.IsOpen == true) return;
        var bounds = GetIslandScreenPixelBounds();
        bounds.Inflate(8, 8);
        if (bounds.Contains(NativeWindow.GetCursorPosition()))
        {
            _leaveTimer.Start();
            return;
        }
        CollapseIsland();
    }

    private void CollapseIslandImmediatelyForDrag()
    {
        if (!_expanded && !_expandUp) return;
        _islandAnimationVersion++;
        _islandAnimationInProgress = false;
        var bounds = GetIslandWindowBounds();
        var pointer = Mouse.GetPosition(Island);
        var pointerScreen = new Point(Left + bounds.Left + pointer.X, Top + bounds.Top + pointer.Y);

        BeginAnimation(LeftProperty, null);
        _horizontalExpansionCompensated = false;
        Island.BeginAnimation(WidthProperty, null);
        Island.BeginAnimation(HeightProperty, null);
        ExpandedPanel.BeginAnimation(OpacityProperty, null);
        ExpandedPanel.Opacity = 0;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapseHandleButton.Visibility = Visibility.Collapsed;
        Island.Width = _settings.IslandWidth;
        Island.Height = CollapsedHeight;
        Island.VerticalAlignment = VerticalAlignment.Top;
        Island.Margin = new Thickness(0, IslandMargin, 0, 0);
        _expanded = false;
        _expandUp = false;
        ApplyExpandedContentOrder();

        var collapsedLeft = (Width - _settings.IslandWidth) / 2;
        var targetX = Math.Clamp(pointer.X, 10, _settings.IslandWidth - 10);
        var targetY = Math.Clamp(pointer.Y, 8, CollapsedHeight - 8);
        Left = pointerScreen.X - collapsedLeft - targetX;
        Top = pointerScreen.Y - IslandMargin - targetY;
        _collapsedAnchorTop = Top;
        ScheduleSummaryMarquee();
    }

    private void ExpandIsland(bool force = false)
    {
        if ((!force && (!_settings.EnableHoverExpansion || _settings.EnableReverseHover)) || _expanded || _dragging || _holdArmed) return;
        if (_islandAnimationInProgress)
        {
            if (!force) return;
            CompleteCollapseImmediately();
        }
        StopSummaryMarquee();
        SummaryTextClone.Visibility = Visibility.Collapsed;
        if (!_expandUp) _collapsedAnchorTop = Top;
        OrientCollapsedIsland();
        _collapsedLeftBeforeExpansion = Left;
        _horizontalExpansionCompensated = false;
        _expanded = true;
        _islandAnimationInProgress = true;
        var animationVersion = ++_islandAnimationVersion;
        ExpandedPanel.Visibility = Visibility.Visible;
        CollapseHandleButton.Visibility = Visibility.Visible;
        StartExpandAnimations(animationVersion);
    }

    private void StartExpandAnimations(int animationVersion)
    {
        var targetWidth = Math.Max(408, _settings.IslandWidth);
        var constrainToScreen = !_settings.AllowExpandedBeyondScreen;
        IEasingFunction easing = constrainToScreen
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : _settings.EnableSpringAnimation
            ? new BackEase { Amplitude = .28, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        if (constrainToScreen)
        {
            var targetLeft = GetConstrainedExpansionLeft(targetWidth);
            if (Math.Abs(targetLeft - Left) > .1)
            {
                _horizontalExpansionCompensated = true;
                BeginAnimation(LeftProperty, new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
            }
        }
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(Island.ActualWidth, targetWidth, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        var height = new DoubleAnimation(Island.ActualHeight, GetExpandedHeight(), TimeSpan.FromMilliseconds(260)) { EasingFunction = easing };
        height.Completed += (_, _) =>
        {
            if (animationVersion != _islandAnimationVersion || !_expanded) return;
            _islandAnimationInProgress = false;
            if (!IsCursorNearIsland()) _leaveTimer.Start();
        };
        Island.BeginAnimation(HeightProperty, height);
        ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.FromMilliseconds(70) });
    }

    private double GetConstrainedExpansionLeft(double targetWidth)
    {
        var bounds = GetIslandScreenPixelBounds();
        var monitor = NativeWindow.GetMonitorBounds(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        var expansionPixels = Math.Abs(DipsToDevicePixels(new Vector(Math.Max(0, targetWidth - Island.ActualWidth), 0)).X);
        var projectedLeft = bounds.Left - expansionPixels / 2;
        var projectedRight = bounds.Right + expansionPixels / 2;
        const double edgePadding = 2;
        var correctionPixels = projectedLeft < monitor.Left + edgePadding
            ? monitor.Left + edgePadding - projectedLeft
            : projectedRight > monitor.Right - edgePadding
                ? monitor.Right - edgePadding - projectedRight
                : 0;
        return Left + DevicePixelsToDips(new Vector(correctionPixels, 0)).X;
    }

    private void CompleteCollapseImmediately()
    {
        _islandAnimationVersion++;
        BeginAnimation(LeftProperty, null);
        if (_horizontalExpansionCompensated) Left = _collapsedLeftBeforeExpansion;
        _horizontalExpansionCompensated = false;
        Island.BeginAnimation(WidthProperty, null);
        Island.BeginAnimation(HeightProperty, null);
        ExpandedPanel.BeginAnimation(OpacityProperty, null);
        Island.Width = _settings.IslandWidth;
        Island.Height = CollapsedHeight;
        ExpandedPanel.Opacity = 0;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapseHandleButton.Visibility = Visibility.Collapsed;
        Island.VerticalAlignment = _expandUp ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Island.Margin = _expandUp ? new Thickness(0, 0, 0, IslandMargin) : new Thickness(0, IslandMargin, 0, 0);
        _expanded = false;
        _islandAnimationInProgress = false;
    }

    private void CollapseIsland(bool force = false)
    {
        if (!_expanded || (!force && (_dragging || _holdArmed || _notificationHoldActive))) return;
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _expanded = false;
        _islandAnimationInProgress = true;
        CollapseHandleButton.Visibility = Visibility.Collapsed;
        var animationVersion = ++_islandAnimationVersion;
        ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(ExpandedPanel.Opacity, 0, TimeSpan.FromMilliseconds(100)));
        IEasingFunction easing = _horizontalExpansionCompensated
            ? new CubicEase { EasingMode = EasingMode.EaseIn }
            : _settings.EnableSpringAnimation
            ? new BackEase { Amplitude = .22, EasingMode = EasingMode.EaseIn }
            : new CubicEase { EasingMode = EasingMode.EaseIn };
        if (_horizontalExpansionCompensated)
            BeginAnimation(LeftProperty, new DoubleAnimation(Left, _collapsedLeftBeforeExpansion, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        var width = new DoubleAnimation(Island.ActualWidth, _settings.IslandWidth, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing };
        var height = new DoubleAnimation(Island.ActualHeight, CollapsedHeight, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing };
        height.Completed += (_, _) =>
        {
            if (animationVersion != _islandAnimationVersion || _expanded) return;
            Island.BeginAnimation(WidthProperty, null);
            Island.Width = _settings.IslandWidth;
            Island.BeginAnimation(HeightProperty, null);
            Island.Height = CollapsedHeight;
            BeginAnimation(LeftProperty, null);
            if (_horizontalExpansionCompensated) Left = _collapsedLeftBeforeExpansion;
            _horizontalExpansionCompensated = false;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            _islandAnimationInProgress = false;
            ScheduleSummaryMarquee();
            if ((_inactivityHidden || (UsesActiveOnlyDisplay && !_anyBusy)) && _activeCompletionNotice is null && _activeConfirmationNotice is null) HideIslandForFocusMode();
            else if (!_manualCollapseUntilPointerExit && IsCursorNearIsland() && _settings.EnableHoverExpansion && !_settings.EnableReverseHover) _enterTimer.Start();
        };
        Island.BeginAnimation(WidthProperty, width);
        Island.BeginAnimation(HeightProperty, height);
    }

    private bool IsCursorNearIsland()
    {
        var bounds = GetIslandScreenPixelBounds();
        bounds.Inflate(8, 8);
        return bounds.Contains(NativeWindow.GetCursorPosition());
    }

    private Rect GetIslandWindowBounds()
    {
        if (Island.ActualWidth <= 0 || Island.ActualHeight <= 0)
            return new Rect((Width - _settings.IslandWidth) / 2, IslandMargin, _settings.IslandWidth, CollapsedHeight);
        return Island.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Island.ActualWidth, Island.ActualHeight));
    }

    private Rect GetIslandScreenPixelBounds()
    {
        var topLeft = Island.PointToScreen(new Point(0, 0));
        var bottomRight = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    private Vector DevicePixelsToDips(Vector pixels)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is null ? pixels : source.CompositionTarget.TransformFromDevice.Transform(pixels);
    }

    private Vector DipsToDevicePixels(Vector dips)
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is null ? dips : source.CompositionTarget.TransformToDevice.Transform(dips);
    }

    private double UpwardHostOffset => Height - (IslandMargin * 2 + CollapsedHeight);

    private void UpdateCollapsedAnchorFromCurrentGeometry()
        => _collapsedAnchorTop = _expandUp ? Top + UpwardHostOffset : Top;

    private void OrientCollapsedIsland()
    {
        if (_expanded || _islandAnimationInProgress) return;
        var expandUp = ShouldExpandUp();
        if (expandUp == _expandUp) return;

        Opacity = 0;
        if (expandUp)
        {
            _collapsedAnchorTop = Top;
            Island.VerticalAlignment = VerticalAlignment.Bottom;
            Island.Margin = new Thickness(0, 0, 0, IslandMargin);
            Top = _collapsedAnchorTop - UpwardHostOffset;
        }
        else
        {
            Top = _collapsedAnchorTop;
            Island.VerticalAlignment = VerticalAlignment.Top;
            Island.Margin = new Thickness(0, IslandMargin, 0, 0);
        }
        _expandUp = expandUp;
        ApplyExpandedContentOrder();
        Island.UpdateLayout();
        Dispatcher.BeginInvoke(() => Opacity = 1, DispatcherPriority.Render);
    }

    private void ClampCollapsedPosition()
    {
        var bounds = GetIslandScreenPixelBounds();
        var monitorBounds = NativeWindow.GetMonitorBounds(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        var correctionX = bounds.Left < monitorBounds.Left ? monitorBounds.Left - bounds.Left : bounds.Right > monitorBounds.Right ? monitorBounds.Right - bounds.Right : 0;
        var correctionY = bounds.Top < monitorBounds.Top ? monitorBounds.Top - bounds.Top : bounds.Bottom > monitorBounds.Bottom ? monitorBounds.Bottom - bounds.Bottom : 0;
        var correction = DevicePixelsToDips(new Vector(correctionX, correctionY));
        Left += correction.X;
        Top += correction.Y;
    }

    private void SnapToHorizontalCenter()
    {
        var bounds = GetIslandScreenPixelBounds();
        var workArea = NativeWindow.GetMonitorWorkArea(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        var islandCenter = bounds.Left + bounds.Width / 2;
        var screenCenter = workArea.Left + workArea.Width / 2;
        var threshold = Math.Abs(DipsToDevicePixels(new Vector(36, 0)).X);
        if (Math.Abs(islandCenter - screenCenter) <= threshold)
            Left += DevicePixelsToDips(new Vector(screenCenter - islandCenter, 0)).X;
    }

    private bool ShouldExpandUp()
    {
        var bounds = GetIslandScreenPixelBounds();
        var workArea = NativeWindow.GetMonitorWorkArea(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        var availableBelow = workArea.Bottom - bounds.Bottom;
        var required = Math.Abs(DipsToDevicePixels(new Vector(0, GetExpandedHeight() - CollapsedHeight + 12)).Y);
        return availableBelow < required;
    }

    private static bool ActivateProcess(string name, Func<Process, bool>? predicate = null)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            var process = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero && (predicate?.Invoke(p) ?? true));
            if (process is null) return false;
            NativeWindow.RestoreAndActivate(process.MainWindowHandle); return true;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool IsCodexProcess(Process process)
    {
        try { return process.MainModule?.FileName?.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true; }
        catch { return process.MainWindowTitle.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase); }
    }

    private void OpenYoyo()
    {
        if (ActivateProcess("HnMagicClawUI")) return;
        var executable = ApplicationLocator.FindYoyoExecutable(_settings.YoyoExecutablePath);
        if (executable is not null) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private void OpenCodex()
    {
        if (ActivateProcess("ChatGPT", IsCodexProcess)) return;
        var executable = ApplicationLocator.FindCodexDesktopExecutable(_settings.CodexExecutablePath, IsCodexProcess);
        if (executable is not null)
        {
            try { Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }); return; }
            catch { }
        }
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true });
    }

    private void OpenWorkBuddy()
    {
        if (ActivateProcess("WorkBuddy")) return;
        var executable = ApplicationLocator.FindWorkBuddyExecutable(_settings.WorkBuddyExecutablePath);
        if (executable is not null) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private void CaptureExecutablePaths()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPathCapture < TimeSpan.FromMinutes(1)) return;
        _lastPathCapture = now;
        var yoyo = ApplicationLocator.FindRunningExecutable("HnMagicClawUI");
        var codex = ApplicationLocator.FindRunningExecutable("ChatGPT", IsCodexProcess);
        var workBuddy = ApplicationLocator.FindRunningExecutable("WorkBuddy");
        var changed = false;
        if (yoyo is not null && !string.Equals(_settings.YoyoExecutablePath, yoyo, StringComparison.OrdinalIgnoreCase)) { _settings.YoyoExecutablePath = yoyo; changed = true; }
        if (codex is not null && !string.Equals(_settings.CodexExecutablePath, codex, StringComparison.OrdinalIgnoreCase)) { _settings.CodexExecutablePath = codex; changed = true; }
        if (workBuddy is not null && !string.Equals(_settings.WorkBuddyExecutablePath, workBuddy, StringComparison.OrdinalIgnoreCase)) { _settings.WorkBuddyExecutablePath = workBuddy; changed = true; }
        if (changed) AppSettings.Save(_settings);
    }

    private string CodexSummary(CodexStatus status)
    {
        if (!status.IsRunning) return "未运行";
        var state = _settings.EnableCodexActivityDetection ? (status.IsBusy ? "执行中" : "空闲") : "已打开";
        if (!_settings.ShowCodexLimits) return state;
        if (!status.LimitsAvailable) return $"{state} · {(status.LimitsLoading ? "限额读取中" : "限额不可用")}";
        var pieces = new List<string> { state };
        if (status.FiveHourRemainingPercent is int fiveHour) pieces.Add($"5小时 {fiveHour}%");
        if (status.WeeklyRemainingPercent is int weekly) pieces.Add($"本周 {weekly}%");
        return string.Join(" · ", pieces);
    }

    private void SetCodexStateText(CodexStatus status)
    {
        CodexStateText.Inlines.Clear();
        var state = !status.IsRunning ? "未运行" : !_settings.EnableCodexActivityDetection ? "已打开" : status.IsBusy ? "执行中" : "空闲";
        var stateBrush = !status.IsRunning ? OfflineBrush : status.IsBusy ? BusyBrush : OnlineBrush;
        CodexStateText.Inlines.Add(new Run(state) { Foreground = stateBrush });
        if (!_settings.ShowCodexLimits) return;
        CodexStateText.Inlines.Add(new Run(" · ") { Foreground = _secondaryTextBrush });
        if (!status.LimitsAvailable)
        {
            CodexStateText.Inlines.Add(new Run(status.LimitsLoading ? "限额读取中" : "限额不可用") { Foreground = status.LimitsLoading ? _secondaryTextBrush : ErrorBrush });
            return;
        }
        var quota = new List<string>();
        if (status.FiveHourRemainingPercent is int fiveHour) quota.Add($"5小时 {fiveHour}%");
        if (status.WeeklyRemainingPercent is int weekly) quota.Add($"本周 {weekly}%");
        CodexStateText.Inlines.Add(new Run(quota.Count > 0 ? string.Join(" · ", quota) : "限额不可用") { Foreground = quota.Count > 0 ? AccentBrush : ErrorBrush });
    }

    private void SetWorkBuddyStateText(WorkBuddyStatus status, WorkBuddyCredits credits)
    {
        WorkBuddyStateText.Inlines.Clear();
        var state = !status.IsRunning ? "未运行" : !status.DataAvailable ? "状态不可用" : status.RequiresConfirmation ? "待确认" : status.IsBusy ? "执行中" : "空闲";
        var stateBrush = !status.IsRunning ? OfflineBrush : !status.DataAvailable ? ErrorBrush : status.IsBusy ? BusyBrush : OnlineBrush;
        WorkBuddyStateText.Inlines.Add(new Run(state) { Foreground = stateBrush });
        if (!_settings.ShowWorkBuddyCredits) return;
        WorkBuddyStateText.Inlines.Add(new Run(" · ") { Foreground = _secondaryTextBrush });
        WorkBuddyStateText.Inlines.Add(new Run(credits.Available && credits.Remaining is double remaining ? $"{remaining:0.##} 积分" : "积分不可用")
        {
            Foreground = credits.Available ? AccentBrush : ErrorBrush
        });
    }

    private void ScheduleSummaryMarquee()
    {
        Dispatcher.BeginInvoke(UpdateSummaryMarquee, DispatcherPriority.Loaded);
    }

    private void UpdateSummaryMarquee()
    {
        StopSummaryMarquee();
        if (!_isBalanceSummary || _expanded || _focusModeHidden || SummaryViewport.ActualWidth <= 0) return;
        SummaryText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = SummaryText.DesiredSize.Width;
        if (textWidth <= SummaryViewport.ActualWidth)
        {
            SummaryTextClone.Visibility = Visibility.Collapsed;
            return;
        }
        var gap = SummaryText.FontSize * 5;
        SummaryTextClone.Visibility = Visibility.Visible;
        _marqueeCycleWidth = textWidth + gap;
        _marqueeOffset = 0;
        Canvas.SetLeft(SummaryText, 0);
        Canvas.SetLeft(SummaryTextClone, _marqueeCycleWidth);
        SummaryPrimaryTranslate.X = 0;
        SummaryCloneTranslate.X = 0;
        _lastMarqueeTick = Stopwatch.GetTimestamp();
        if (!_marqueeRenderingSubscribed)
        {
            CompositionTarget.Rendering += AdvanceSummaryMarquee;
            _marqueeRenderingSubscribed = true;
        }
    }

    private void AdvanceSummaryMarquee(object? sender, EventArgs e)
    {
        if (!_isBalanceSummary || _expanded || _focusModeHidden || _marqueeCycleWidth <= 0)
        {
            StopSummaryMarquee();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(.1, Math.Max(0, (now - _lastMarqueeTick) / (double)Stopwatch.Frequency));
        _lastMarqueeTick = now;
        _marqueeOffset = (_marqueeOffset + _settings.QuotaScrollSpeed * elapsed) % _marqueeCycleWidth;
        SummaryPrimaryTranslate.X = -_marqueeOffset;
        SummaryCloneTranslate.X = -_marqueeOffset;
    }

    private void StopSummaryMarquee()
    {
        if (_marqueeRenderingSubscribed)
        {
            CompositionTarget.Rendering -= AdvanceSummaryMarquee;
            _marqueeRenderingSubscribed = false;
        }
        _marqueeOffset = 0;
        _marqueeCycleWidth = 0;
        Canvas.SetLeft(SummaryText, 0);
        Canvas.SetLeft(SummaryTextClone, 0);
        SummaryPrimaryTranslate.X = 0;
        SummaryCloneTranslate.X = 0;
    }

    private void SetLaunchControls(bool enabled)
    {
        var cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        YoyoIndicatorButton.Cursor = cursor;
        CodexIndicatorButton.Cursor = cursor;
        WorkBuddyIndicatorButton.Cursor = cursor;
        YoyoRowButton.Cursor = cursor;
        CodexRowButton.Cursor = cursor;
        WorkBuddyRowButton.Cursor = cursor;
    }

    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeMode == "system") Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        var light = _settings.ThemeMode == "light" || (_settings.ThemeMode == "system" && SystemUsesLightTheme());
        var primary = Brush(light ? "#182033" : "#F2F5FF");
        var secondary = Brush(light ? "#5E687A" : "#9AA5BC");
        _secondaryTextBrush = secondary;
        Island.Background = Brush(light ? "#F4FFFFFF" : "#EB0E121C");
        Island.BorderBrush = Brush(light ? "#24182033" : "#1AFFFFFF");
        SummaryText.Foreground = secondary;
        SummaryTextClone.Foreground = secondary;
        if (_isBalanceSummary)
        {
            foreach (var textBlock in new[] { SummaryText, SummaryTextClone })
            foreach (var run in textBlock.Inlines.OfType<Run>().Where(run => !ReferenceEquals(run.Foreground, AccentBrush))) run.Foreground = secondary;
        }
        YoyoLabel.Foreground = primary;
        CodexLabel.Foreground = primary;
        WorkBuddyLabel.Foreground = primary;
        RecentResultText.Foreground = Brush(light ? "#59657A" : "#B9C2D4");
        RecentBorder.Background = Brush(light ? "#CCEAF0F7" : "#B8182033");
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch { return true; }
    }

    private static void WriteStatusSnapshot(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy, WorkBuddyCredits credits, RefreshTimings timings)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion"); Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new
            {
                updatedAt = DateTimeOffset.Now,
                yoyo = new { running = yoyo.IsYoyoRunning, available = yoyo.TaskStatusAvailable, busy = yoyo.IsBusy, points = yoyo.RemainingPoints, totalPoints = yoyo.TotalPoints, result = yoyo.RecentResult, recentResponseAt = yoyo.RecentUpdatedAt, readMilliseconds = timings.YoyoReadMilliseconds },
                codex = new
                {
                    running = codex.IsRunning,
                    busy = codex.IsBusy,
                    fiveHourRemainingPercent = codex.FiveHourRemainingPercent,
                    weeklyRemainingPercent = codex.WeeklyRemainingPercent,
                    limitsLoading = codex.LimitsLoading,
                    limitsError = codex.LimitsError,
                    recentResponseAt = codex.RecentResponseAt,
                    readMilliseconds = timings.CodexReadMilliseconds,
                    activityReadMilliseconds = codex.ActivityReadMilliseconds,
                    limitsReadMilliseconds = codex.LimitsReadMilliseconds,
                    sessionsScanned = codex.SessionsScanned,
                    activeSessions = codex.ActiveSessions,
                    latestLifecycleAt = codex.LatestLifecycleAt,
                    hasStaleStarted = codex.HasStaleStarted
                },
                workBuddy = new { running = workBuddy.IsRunning, available = workBuddy.DataAvailable, busy = workBuddy.IsBusy, requiresConfirmation = workBuddy.RequiresConfirmation, confirmationId = workBuddy.ConfirmationId, summary = workBuddy.Summary, credits = credits.Remaining, totalCredits = credits.Total, recentResponseAt = workBuddy.RecentResponseAt, readMilliseconds = timings.WorkBuddyReadMilliseconds, creditsReadMilliseconds = timings.WorkBuddyCreditsReadMilliseconds }
            });
            File.WriteAllText(Path.Combine(directory, "status.json"), json);
        }
        catch { }
    }

    private void OpenHome()
    {
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(); _settingsWindow.Activate();
    }

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private sealed record CompletionNotice(string Provider, string Response, DateTimeOffset CompletedAt);
    private readonly record struct TimedResult<T>(T Value, long ElapsedMilliseconds);
    private readonly record struct RefreshTimings(long YoyoReadMilliseconds, long CodexReadMilliseconds, long WorkBuddyReadMilliseconds, long WorkBuddyCreditsReadMilliseconds);
    private void OpenHome_Click(object sender, RoutedEventArgs e) => OpenHome();
    private void OpenYoyo_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenYoyo(); e.Handled = true; }
    private void OpenCodex_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenCodex(); e.Handled = true; }
    private void OpenWorkBuddy_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenWorkBuddy(); e.Handled = true; }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
