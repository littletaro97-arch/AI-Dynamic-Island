using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Controls;
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
    private readonly YoyoCheckinService _yoyoCheckinService = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly ContextMenu _islandMenu = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _checkinCancellation;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private readonly DispatcherTimer _enterTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _completionTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _passThroughTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _zOrderTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly DispatcherTimer _readyClockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IslandSettings _settings = AppSettings.Load();
    private SettingsWindow? _settingsWindow;
    private bool _holdArmed, _dragging, _dragMoved, _refreshing, _expanded, _finishingGesture;
    private bool _manualCollapseUntilPointerExit, _refreshAfterCurrent, _fullResetAfterCurrent;
    private bool _collapseHandlePressed, _suppressCollapseHandleClick;
    private bool _completionBaselineReady, _notificationHoldActive;
    private string? _yoyoCompletionId, _codexCompletionId, _workBuddyCompletionId;
    private string? _activeCompletionNotice;
    private string? _activeSystemNotice;
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
    private bool _trayWakeActive;
    private DateTimeOffset _trayWakeIgnoreDeactivateUntil;
    private bool _taskbarTopmostOverride;
    private bool _horizontalExpansionCompensated;
    private bool _positionInitialized;
    private int _islandAnimationVersion, _reverseFadeVersion, _focusAnimationVersion;
    private double _collapsedAnchorTop;
    private double _collapsedLeftBeforeExpansion;
    private Point _dragStartCursorPixels;
    private double _dragStartWindowLeft, _dragStartWindowTop;
    private string? _stateFingerprint;
    private DateTimeOffset _lastStateChangeAt = DateTimeOffset.Now;
    private bool _appliedUnchangedAutoHide;
    private double _appliedUnchangedAutoHideMinutes;
    private DateTimeOffset _lastPathCapture = DateTimeOffset.MinValue;
    private string? _codexFiveHourReminderId, _codexWeeklyReminderId;
    private string? _highlightedProvider;
    private bool _yoyoInstalled, _codexInstalled, _workBuddyInstalled;
    private readonly Dictionary<string, DateTimeOffset> _headlineBusySeenAt = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        ConfigureIslandMenu();
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
        _readyClockTimer.Tick += (_, _) => UpdateReadyClockText();
        _codexStatusService.LimitsUpdated += CodexLimitsUpdated;
        Deactivated += MainWindow_Deactivated;
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Closed += (_, _) => { _checkinCancellation?.Cancel(); _checkinCancellation?.Dispose(); _lifetimeCancellation.Cancel(); _lifetimeCancellation.Dispose(); StopSummaryMarquee(); _passThroughTimer.Stop(); _fullscreenTimer.Stop(); _zOrderTimer.Stop(); _readyClockTimer.Stop(); _codexStatusService.LimitsUpdated -= CodexLimitsUpdated; SystemEvents.UserPreferenceChanged -= SystemThemeChanged; };
    }

    internal IslandSettings CurrentSettings => _settings;
    internal GitHubUpdateService UpdateService => _updateService;
    internal event EventHandler? PositionChanged;
    internal event EventHandler? ProviderAvailabilityChanged;
    private double CollapsedHeight => _settings.IslandHeight;
    internal void OpenHomeFromExternalRequest() => OpenHome();
    internal void RefreshFromExternalRequest() => _ = ResetAndRefreshStatusAsync();
    internal void WakeFromTray()
    {
        _lastStateChangeAt = DateTimeOffset.Now;
        _inactivityHidden = false;
        _trayWakeActive = true;
        _manualCollapseUntilPointerExit = false;
        _leaveTimer.Stop();
        _trayWakeIgnoreDeactivateUntil = DateTimeOffset.Now.AddMilliseconds(450);
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: true);
        ExpandIsland(true);
        Activate();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!_trayWakeActive || DateTimeOffset.Now < _trayWakeIgnoreDeactivateUntil) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_trayWakeActive || DateTimeOffset.Now < _trayWakeIgnoreDeactivateUntil || Island.ContextMenu?.IsOpen == true) return;
            _trayWakeActive = false;
            CollapseIsland(true);
        }, DispatcherPriority.Input);
    }

    internal void StartYoyoCheckinFromSettings()
    {
        _checkinCancellation?.Cancel();
        _checkinCancellation?.Dispose();
        _checkinCancellation = null;
        if (!_settings.EnableYoyoAutoCheckin || !IsLoaded) return;
        _checkinCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _ = RunYoyoCheckinAsync(_checkinCancellation.Token);
    }

    private void ConfigureIslandMenu()
    {
        _islandMenu.Style = (Style)FindResource("IslandContextMenuStyle");
        _islandMenu.Opened += (_, _) => RebuildIslandMenu();
        _islandMenu.Closed += (_, _) =>
        {
            if (!_trayWakeActive && !Island.IsMouseOver && _expanded) _leaveTimer.Start();
        };
        Island.ContextMenu = _islandMenu;
        RebuildIslandMenu();
    }

    private void RebuildIslandMenu()
    {
        _islandMenu.Items.Clear();
        _islandMenu.Items.Add(CreateIslandMenuItem("打开主页", "MenuHomeIcon", (_, _) => OpenHome()));
        _islandMenu.Items.Add(CreateIslandSeparator());
        foreach (var provider in GetProviderMenuEntries())
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Brush(provider.Color), VerticalAlignment = VerticalAlignment.Center };
            var item = CreateIslandMenuItem($"打开 {provider.Label}", null, (_, _) => OpenProviderFromMenu(provider.Key), dot);
            item.InputGestureText = provider.CanLaunch ? "↗" : "未找到";
            item.IsEnabled = provider.CanLaunch;
            _islandMenu.Items.Add(item);
        }
        _islandMenu.Items.Add(CreateIslandSeparator());
        _islandMenu.Items.Add(CreateIslandMenuItem("重置并重新检测", "MenuRefreshIcon", async (_, _) => await ResetAndRefreshStatusAsync()));
        _islandMenu.Items.Add(CreateIslandMenuItem("复制最近结果", "MenuCopyIcon", (_, _) => CopyLatestResult()));
        var wide = CreateIslandSeparator();
        wide.Margin = new Thickness(0, 9, 0, 5);
        _islandMenu.Items.Add(wide);
        var exit = CreateIslandMenuItem("退出", "MenuExitIcon", (_, _) => Application.Current.Shutdown());
        exit.Tag = "danger";
        _islandMenu.Items.Add(exit);
    }

    private MenuItem CreateIslandMenuItem(string header, string? geometryResource, RoutedEventHandler click, UIElement? icon = null)
    {
        var item = new MenuItem { Header = header, Style = (Style)FindResource("IslandMenuItemStyle") };
        if (icon is not null) item.Icon = icon;
        else if (geometryResource is not null)
            item.Icon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource(geometryResource), Stroke = _secondaryTextBrush, StrokeThickness = 1.8,
                Stretch = Stretch.Uniform, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
        item.Click += click;
        return item;
    }

    private Separator CreateIslandSeparator()
        => new() { Style = (Style)FindResource("IslandMenuSeparatorStyle"), Background = Brush(IsLightTheme ? "#16182033" : "#16FFFFFF") };

    private void CopyLatestResult()
    {
        var text = ActiveNoticeText;
        if (string.IsNullOrWhiteSpace(text)) return;
        try { System.Windows.Clipboard.SetText(text); } catch { }
    }


    private async Task ResetAndRefreshStatusAsync()
    {
        if (_refreshing)
        {
            _fullResetAfterCurrent = true;
            return;
        }

        _refreshTimer.Stop();
        _statusService.ResetCache();
        _codexStatusService.ResetCache();
        _workBuddyStatusService.ResetCache();
        _workBuddyCreditsService.ResetCache();
        _completionBaselineReady = false;
        _yoyoCompletionId = _codexCompletionId = _workBuddyCompletionId = null;
        _workBuddyConfirmationId = null;
        _activeCompletionNotice = _activeConfirmationNotice = _activeSystemNotice = null;
        _codexFiveHourReminderId = _codexWeeklyReminderId = null;
        ClearProviderHighlight();
        _latestCombinedResult = "正在重新抓取三个应用的状态…";
        _stateFingerprint = null;
        _balanceSummaryKey = null;
        _anyBusy = false;
        _lastStateChangeAt = DateTimeOffset.Now;
        _inactivityHidden = false;
        _trayWakeActive = true;
        _completionTimer.Stop();
        _notificationHoldActive = false;
        HeadlineText.Text = "正在重新检测";
        HeadlineText.Foreground = BusyBrush;
        SetPlainSummary("旧状态已清除 · 正在重新抓取");
        RecentResultText.Text = _latestCombinedResult;
        StateText.Text = CodexStateText.Text = WorkBuddyStateText.Text = "重新检测";
        StateText.Foreground = CodexStateText.Foreground = WorkBuddyStateText.Foreground = OfflineBrush;
        PointsText.Inlines.Clear();
        PointsText.Inlines.Add(new Run("· ") { Foreground = Brush("#182033") });
        PointsText.Inlines.Add(new Run("积分 --") { Foreground = AccentBrush });
        YoyoMiniDot.Fill = CodexMiniDot.Fill = WorkBuddyMiniDot.Fill = OfflineBrush;
        StateDot.Fill = CodexDot.Fill = WorkBuddyDot.Fill = OfflineBrush;
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: true);
        ExpandIsland(true);

        await RefreshStatusAsync();
        _refreshTimer.Start();
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
        settings.CodexResetReminderMinutes = Math.Clamp(settings.CodexResetReminderMinutes, 1, 120);
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
        ApplyProviderVisibility();
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

    internal void ApplyPresetSettings(IslandSettings settings)
    {
        var requestedX = settings.X;
        var requestedY = settings.Y;
        var requestedPreset = NormalizePositionPreset(settings.PositionPreset);
        ApplySettings(settings, persist: true, refreshStatus: true);
        StartYoyoCheckinFromSettings();
        if (!IsLoaded || !_positionInitialized) return;

        if (requestedPreset != "custom")
        {
            MoveToPositionPreset(requestedPreset);
            return;
        }
        if (requestedX is not double x || requestedY is not double y) return;
        if (_expanded || _islandAnimationInProgress) CompleteCollapseImmediately();
        Left = x;
        Top = y;
        _settings.PositionPreset = "custom";
        ClampCollapsedPosition();
        UpdateCollapsedAnchorFromCurrentGeometry();
        OrientCollapsedIsland();
        SavePosition();
        PositionChanged?.Invoke(this, EventArgs.Empty);
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
        return Math.Min(Height - IslandMargin * 2, 42 + CollapsedHeight + rowHeight * VisibleProviderOrder().Length + lineHeight * _settings.MaxResponseLines);
    }

    private static string NormalizeProviderOrder(string? value)
        => ProviderCatalog.NormalizeOrder(value);

    private static string NormalizePositionPreset(string? value)
        => value is "topLeft" or "topCenter" or "topRight" or "bottomLeft" or "bottomCenter" or "bottomRight"
            ? value
            : "custom";

    private string[] ProviderOrder()
    {
        var supported = ProviderCatalog.ParseOrder(_settings.ProviderOrder).Where(ProviderCatalog.IsKnown).ToList();
        supported.AddRange(ProviderCatalog.KnownKeys.Where(key => !supported.Contains(key, StringComparer.OrdinalIgnoreCase)));
        return supported.ToArray();
    }

    private string[] VisibleProviderOrder() => ProviderOrder().Where(IsProviderVisible).ToArray();

    private bool IsProviderInstalled(string provider) => provider switch
    {
        "yoyo" => _yoyoInstalled,
        "codex" => _codexInstalled,
        "workbuddy" => _workBuddyInstalled,
        _ => false
    };

    private bool IsProviderVisible(string provider) => IsProviderInstalled(provider) && provider switch
    {
        "yoyo" => _settings.ShowYoyo,
        "codex" => _settings.ShowCodex,
        "workbuddy" => _settings.ShowWorkBuddy,
        _ => false
    };

    private static string ProviderLabel(string provider) => ProviderCatalog.DisplayName(provider);

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
        foreach (var provider in ProviderOrder())
            if (indicators.TryGetValue(provider, out var indicator)) IndicatorPanel.Children.Add(indicator);

        _appliedProviderOrder = _settings.ProviderOrder;
        ApplyExpandedContentOrder();
        _balanceSummaryKey = null;
    }

    private void ApplyProviderVisibility()
    {
        YoyoIndicatorButton.Visibility = IsProviderVisible("yoyo") ? Visibility.Visible : Visibility.Collapsed;
        CodexIndicatorButton.Visibility = IsProviderVisible("codex") ? Visibility.Visible : Visibility.Collapsed;
        WorkBuddyIndicatorButton.Visibility = IsProviderVisible("workbuddy") ? Visibility.Visible : Visibility.Collapsed;
        YoyoRowButton.Visibility = IsProviderVisible("yoyo") ? Visibility.Visible : Visibility.Collapsed;
        CodexRowButton.Visibility = IsProviderVisible("codex") ? Visibility.Visible : Visibility.Collapsed;
        WorkBuddyRowButton.Visibility = IsProviderVisible("workbuddy") ? Visibility.Visible : Visibility.Collapsed;
        PointsText.Visibility = IsProviderVisible("yoyo") && _settings.ShowYoyoCredits ? Visibility.Visible : Visibility.Collapsed;
        ApplyExpandedContentOrder();
        _balanceSummaryKey = null;
    }

    private void ApplyExpandedContentOrder(double? statusRowHeight = null)
    {
        var rowHeight = statusRowHeight ?? Math.Max(24, Math.Ceiling(_settings.TextSize * 1.7));
        var replyFirst = _expandUp && _settings.PutReplyFirstWhenExpandedUp;
        ExpandedRow0.Height = new GridLength(0);
        ExpandedRow1.Height = new GridLength(0);
        ExpandedRow2.Height = new GridLength(0);
        ExpandedRow3.Height = new GridLength(0);

        var rows = new Dictionary<string, UIElement[]>
        {
            ["yoyo"] = [StateDot, YoyoLabel, StateText, PointsText, YoyoRowButton],
            ["codex"] = [CodexDot, CodexLabel, CodexStateText, CodexRowButton],
            ["workbuddy"] = [WorkBuddyDot, WorkBuddyLabel, WorkBuddyStateText, WorkBuddyRowButton]
        };
        var order = VisibleProviderOrder();
        var replyRow = replyFirst ? 0 : order.Length;
        var definitions = new[] { ExpandedRow0, ExpandedRow1, ExpandedRow2, ExpandedRow3 };
        definitions[replyRow].Height = new GridLength(1, GridUnitType.Star);
        for (var index = 0; index < order.Length; index++)
        {
            var targetRow = replyFirst ? index + 1 : index;
            definitions[targetRow].Height = new GridLength(rowHeight);
            if (rows.TryGetValue(order[index], out var elements))
                foreach (var element in elements) System.Windows.Controls.Grid.SetRow(element, targetRow);
        }
        System.Windows.Controls.Grid.SetRow(RecentBorder, replyRow);
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
        if (_settings.AutoCheckForUpdates) _ = _updateService.CheckAsync();
        _zOrderTimer.Start();
        if (_settings.EnableFullscreenActiveOnly) _fullscreenTimer.Start();
        if (_settings.EnableYoyoAutoCheckin) StartYoyoCheckinFromSettings();
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
            if (completion is null && _activeConfirmationNotice is null) UpdateCodexResetReminder(codex);
            RecentResultText.Text = ActiveNoticeText;
            _anyBusy = (IsProviderVisible("yoyo") && status.IsBusy)
                || (IsProviderVisible("codex") && codex.IsBusy)
                || (IsProviderVisible("workbuddy") && workBuddy.IsBusy);
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
            if (_fullResetAfterCurrent)
            {
                _fullResetAfterCurrent = false;
                _refreshAfterCurrent = false;
                _ = ResetAndRefreshStatusAsync();
            }
            else if (_refreshAfterCurrent)
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
        if (!provisional || !codex.IsBusy || !IsProviderVisible("codex")) return;

        _anyBusy = true;
        _refreshTimer.Interval = TimeSpan.FromSeconds(2);
        var now = DateTimeOffset.UtcNow;
        _headlineBusySeenAt["codex"] = now;
        if (_headlineBusySeenAt.Any(pair => pair.Key != "codex" && IsProviderVisible(pair.Key) && now - pair.Value <= TimeSpan.FromSeconds(4)))
        {
            if (UsesActiveOnlyDisplay && !_notificationHoldActive) ShowIslandForFocusMode();
            return;
        }
        HeadlineText.Text = "Codex 执行中";
        HeadlineText.Foreground = BusyBrush;
        SetPlainSummary(CodexSummary(codex));
        if (UsesActiveOnlyDisplay && !_notificationHoldActive) ShowIslandForFocusMode();
    }

    internal IReadOnlyList<ProviderMenuEntry> GetProviderMenuEntries()
    {
        var entries = new List<ProviderMenuEntry>();
        foreach (var key in ProviderOrder().Where(IsProviderInstalled))
        {
            var (state, color, available) = key switch
            {
                "yoyo" => (StateText.Text, BrushColor(YoyoMiniDot.Fill), CanLaunchYoyo()),
                "codex" => (InlineText(CodexStateText), BrushColor(CodexMiniDot.Fill), CanLaunchCodex()),
                "workbuddy" => (InlineText(WorkBuddyStateText), BrushColor(WorkBuddyMiniDot.Fill), CanLaunchWorkBuddy()),
                _ => ("未知来源", "#727C90", false)
            };
            entries.Add(new ProviderMenuEntry(key, ProviderLabel(key), state, color, _settings.EnableAppLaunch && available));
        }
        return entries;
    }

    private static string InlineText(TextBlock target)
        => string.Concat(target.Inlines.OfType<Run>().Select(run => run.Text));

    private static string BrushColor(System.Windows.Media.Brush? brush)
        => brush is SolidColorBrush solid ? solid.Color.ToString() : "#727C90";

    private bool CanLaunchYoyo()
        => ApplicationLocator.IsProcessRunning("HnMagicClawUI") || ApplicationLocator.FindYoyoExecutable(_settings.YoyoExecutablePath) is not null;

    private bool CanLaunchCodex()
        => ApplicationLocator.IsProcessRunning("ChatGPT") || ApplicationLocator.FindCodexDesktopExecutable(_settings.CodexExecutablePath, IsCodexProcess) is not null;

    private bool CanLaunchWorkBuddy()
        => ApplicationLocator.IsProcessRunning("WorkBuddy") || ApplicationLocator.FindWorkBuddyExecutable(_settings.WorkBuddyExecutablePath) is not null;

    private static async Task<TimedResult<T>> MeasureAsync<T>(Task<T> task)
    {
        var watch = Stopwatch.StartNew();
        var value = await task;
        watch.Stop();
        return new TimedResult<T>(value, watch.ElapsedMilliseconds);
    }

    private void UpdateHeadline(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy, WorkBuddyCredits workBuddyCredits)
    {
        _readyClockTimer.Stop();
        var points = yoyo.RemainingPoints is double value ? $"{value:0.##} 积分" : "积分 --";
        var busyByProvider = new Dictionary<string, bool> { ["yoyo"] = yoyo.IsBusy, ["codex"] = codex.IsBusy, ["workbuddy"] = workBuddy.IsBusy };
        var runningByProvider = new Dictionary<string, bool> { ["yoyo"] = yoyo.IsYoyoRunning, ["codex"] = codex.IsRunning, ["workbuddy"] = workBuddy.IsRunning };
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in busyByProvider.Where(pair => pair.Value)) _headlineBusySeenAt[pair.Key] = now;
        var busyProviders = VisibleProviderOrder().Where(provider =>
                (busyByProvider.TryGetValue(provider, out var busy) && busy)
                || (runningByProvider.TryGetValue(provider, out var running) && running
                    && _headlineBusySeenAt.TryGetValue(provider, out var seenAt) && now - seenAt <= TimeSpan.FromSeconds(4)))
            .ToList();
        if (VisibleProviderOrder().Length == 0) { HeadlineText.Text = "未检测到助手"; HeadlineText.Foreground = OfflineBrush; SetPlainSummary("请在设置中指定安装位置"); }
        else if (IsProviderVisible("workbuddy") && workBuddy.RequiresConfirmation) { HeadlineText.Text = "WorkBuddy 待确认"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(workBuddy.Summary, BusyBrush); }
        else if (busyProviders.Count > 1) { HeadlineText.Text = $"{busyProviders.Count} 个助手执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(string.Join("  |  ", busyProviders.Select(provider => BusyMetric(provider, yoyo, codex, workBuddyCredits)))); }
        else if (busyProviders.Count == 1 && busyProviders[0] == "yoyo") { HeadlineText.Text = "YOYO 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(AppendMetric(yoyo.RecentResult, _settings.ShowYoyoCredits ? points : null)); }
        else if (busyProviders.Count == 1 && busyProviders[0] == "codex") { HeadlineText.Text = "Codex 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(CodexSummary(codex)); }
        else if (busyProviders.Count == 1 && busyProviders[0] == "workbuddy") { HeadlineText.Text = "WorkBuddy 执行中"; HeadlineText.Foreground = BusyBrush; SetPlainSummary(AppendMetric(workBuddy.Summary, WorkBuddyMetric(workBuddyCredits))); }
        else if (IsProviderVisible("yoyo") && !yoyo.IsYoyoRunning) { HeadlineText.Text = "YOYO 未运行"; HeadlineText.Foreground = OfflineBrush; SetPlainSummary(codex.IsRunning || workBuddy.IsRunning ? "其他助手已就绪" : "未检测到运行实例"); }
        else if (IsProviderVisible("yoyo") && !yoyo.TaskStatusAvailable) { HeadlineText.Text = "YOYO 状态不可用"; HeadlineText.Foreground = ErrorBrush; SetPlainSummary(points, AccentBrush); }
        else if (IsProviderVisible("yoyo") && yoyo.LastTaskFailed) { HeadlineText.Text = "1 项需要处理"; HeadlineText.Foreground = ErrorBrush; SetPlainSummary(yoyo.RecentResult); }
        else
        {
            if (_settings.ShowClockWhenReady)
            {
                UpdateReadyClockText();
                _readyClockTimer.Start();
            }
            else HeadlineText.Text = "全部就绪";
            HeadlineText.Foreground = OnlineBrush;
            SetBalanceSummary(yoyo, codex, workBuddyCredits);
        }
    }

    private void UpdateReadyClockText()
    {
        if (!_settings.ShowClockWhenReady)
        {
            _readyClockTimer.Stop();
            return;
        }
        HeadlineText.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private string BusyMetric(string provider, YoyoStatus yoyo, CodexStatus codex, WorkBuddyCredits workBuddyCredits)
    {
        var metric = provider switch
        {
            "yoyo" when _settings.ShowYoyoCredits => yoyo.RemainingPoints is double points ? $"{points:0.##} 积分" : "积分 --",
            "codex" => CodexQuotaMetric(codex),
            "workbuddy" => WorkBuddyMetric(workBuddyCredits),
            _ => null
        };
        return AppendMetric(ProviderLabel(provider), metric);
    }

    private string? CodexQuotaMetric(CodexStatus status)
    {
        if (!_settings.ShowCodexLimits) return null;
        if (!status.LimitsAvailable) return status.LimitsLoading ? "限额读取中" : "限额不可用";
        var values = new List<string>();
        if (status.FiveHourRemainingPercent is int fiveHour) values.Add($"5小时 {fiveHour}%");
        if (status.WeeklyRemainingPercent is int weekly) values.Add($"本周 {weekly}%");
        return values.Count > 0 ? string.Join(" · ", values) : "限额不可用";
    }

    private string? WorkBuddyMetric(WorkBuddyCredits credits)
        => !_settings.ShowWorkBuddyCredits ? null
            : credits.Available && credits.Remaining is double remaining ? $"{remaining:0.##} 积分" : "积分不可用";

    private static string AppendMetric(string? description, string? metric)
    {
        var text = string.IsNullOrWhiteSpace(description) ? "执行中" : description.Trim();
        return string.IsNullOrWhiteSpace(metric) ? text : $"{text} · {metric}";
    }

    private void SetBalanceSummary(YoyoStatus yoyo, CodexStatus codex, WorkBuddyCredits workBuddy)
    {
        var values = new Dictionary<string, string>();
        if (IsProviderVisible("yoyo") && _settings.ShowYoyoCredits)
            values["yoyo"] = yoyo.RemainingPoints is double yoyoPoints ? $"{yoyoPoints:0.##} 积分" : "--";
        if (IsProviderVisible("codex") && _settings.ShowCodexLimits)
            values["codex"] = codex.FiveHourRemainingPercent is int fiveHour ? $"5小时 {fiveHour}%" : codex.WeeklyRemainingPercent is int weekly ? $"本周 {weekly}%" : "--";
        if (IsProviderVisible("workbuddy") && _settings.ShowWorkBuddyCredits)
            values["workbuddy"] = workBuddy.Available && workBuddy.Remaining is double credits ? $"{credits:0.##} 积分" : "--";
        var key = $"{_settings.ProviderOrder}|{_settings.ShowYoyoCredits}|{_settings.ShowCodexLimits}|{_settings.ShowWorkBuddyCredits}|{string.Join('|', ProviderOrder().Where(values.ContainsKey).Select(provider => values[provider]))}";
        if (_isBalanceSummary && string.Equals(_balanceSummaryKey, key, StringComparison.Ordinal)) return;

        _isBalanceSummary = values.Count > 0;
        _balanceSummaryKey = key;
        PopulateBalanceSummary(SummaryText, values);
        PopulateBalanceSummary(SummaryTextClone, values);
        SummaryViewport.Visibility = values.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (values.Count > 0) ScheduleSummaryMarquee();
    }

    private void PopulateBalanceSummary(TextBlock target, IReadOnlyDictionary<string, string> values)
    {
        target.Inlines.Clear();
        var first = true;
        foreach (var provider in ProviderOrder())
        {
            if (!values.TryGetValue(provider, out var value)) continue;
            if (!first) target.Inlines.Add(new Run("  |  ") { Foreground = _secondaryTextBrush });
            AddSummaryPart(target, ProviderLabel(provider) + " ", value);
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
        SummaryViewport.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        var run = new Run(value);
        if (foreground is not null) run.Foreground = foreground;
        SummaryText.Inlines.Add(run);
    }

    private string SelectLatestResponse(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy)
    {
        var candidates = new List<CompletionNotice>();
        if (IsProviderVisible("yoyo") && yoyo.RecentUpdatedAt is DateTimeOffset yoyoAt && !string.IsNullOrWhiteSpace(yoyo.RecentResult) && yoyo.TaskStatusAvailable)
            candidates.Add(new("YOYO Claw", yoyo.RecentResult, yoyoAt));
        if (IsProviderVisible("codex") && codex.RecentResponseAt is DateTimeOffset codexAt && !string.IsNullOrWhiteSpace(codex.RecentResponse))
            candidates.Add(new("Codex", codex.RecentResponse!, codexAt));
        if (IsProviderVisible("workbuddy") && workBuddy.RecentResponseAt is DateTimeOffset workBuddyAt && !string.IsNullOrWhiteSpace(workBuddy.RecentResponse))
            candidates.Add(new("WorkBuddy", workBuddy.RecentResponse!, workBuddyAt));
        var latest = candidates.OrderByDescending(item => item.CompletedAt).FirstOrDefault();
        return latest is null ? yoyo.RecentResult : $"{latest.Provider} · {latest.Response}";
    }

    private CompletionNotice? DetectCompletion(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy)
    {
        var yoyoId = IsProviderVisible("yoyo") && !yoyo.IsBusy && yoyo.TaskStatusAvailable && yoyo.RecentUpdatedAt is DateTimeOffset yoyoAt
            ? $"{yoyoAt:O}|{yoyo.RecentResult}" : null;
        var codexId = IsProviderVisible("codex") && !codex.IsBusy ? codex.RecentResponseId : null;
        var workBuddyId = IsProviderVisible("workbuddy") && !workBuddy.IsBusy ? workBuddy.RecentResponseId : null;

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
        _activeSystemNotice = null;
        HighlightProvider(notice.Provider);
        RecentResultText.Text = ActiveNoticeText;
        BeginNotificationHold();
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
    }

    private string ActiveNoticeText => _activeConfirmationNotice ?? _activeCompletionNotice ?? _activeSystemNotice ?? _latestCombinedResult;

    private void UpdateCodexResetReminder(CodexStatus codex)
    {
        if (!IsProviderVisible("codex") || !_settings.EnableCodexResetReminder || _activeCompletionNotice is not null || _activeSystemNotice is not null) return;
        var now = DateTimeOffset.Now;
        var window = TimeSpan.FromMinutes(_settings.CodexResetReminderMinutes);
        var candidates = new List<(string Kind, DateTimeOffset At, int? Remaining)>();
        if (codex.FiveHourResetsAt is DateTimeOffset five && five > now && five - now <= window)
            candidates.Add(("5 小时额度", five, codex.FiveHourRemainingPercent));
        if (codex.WeeklyResetsAt is DateTimeOffset weekly && weekly > now && weekly - now <= window)
            candidates.Add(("周额度", weekly, codex.WeeklyRemainingPercent));
        foreach (var candidate in candidates.OrderBy(item => item.At))
        {
            var id = $"{candidate.Kind}|{candidate.At:O}";
            if (candidate.Kind.StartsWith("5", StringComparison.Ordinal) && id == _codexFiveHourReminderId) continue;
            if (candidate.Kind.StartsWith("周", StringComparison.Ordinal) && id == _codexWeeklyReminderId) continue;
            if (candidate.Kind.StartsWith("5", StringComparison.Ordinal)) _codexFiveHourReminderId = id;
            else _codexWeeklyReminderId = id;
            var minutes = Math.Max(1, (int)Math.Ceiling((candidate.At - now).TotalMinutes));
            var remaining = candidate.Remaining is int percent ? $" · 当前剩余 {percent}%" : "";
            ShowSystemNotice("Codex", $"Codex {candidate.Kind}将在 {minutes} 分钟后重置{remaining}");
            break;
        }
    }

    private async Task RunYoyoCheckinAsync(CancellationToken cancellationToken)
    {
        var result = await _yoyoCheckinService.RunAfterNetworkAsync(_settings.YoyoExecutablePath, _settings.LaunchYoyoForAutoCheckin, cancellationToken);
        if (!result.ShouldNotify || !_settings.EnableYoyoAutoCheckin || !IsLoaded) return;
        ShowSystemNotice("YOYO Claw", result.Message);
        if (result.Success) _ = RefreshStatusAsync();
    }

    private void ShowSystemNotice(string provider, string message)
    {
        _activeSystemNotice = message;
        HighlightProvider(provider);
        RecentResultText.Text = ActiveNoticeText;
        BeginNotificationHold();
        RestoreReverseHoverIsland();
        ShowIslandForFocusMode(animate: _inactivityHidden);
        ExpandIsland(true);
    }

    private void HighlightProvider(string provider)
    {
        _highlightedProvider = provider;
        YoyoRowButton.IsChecked = string.Equals(provider, "YOYO Claw", StringComparison.OrdinalIgnoreCase);
        CodexRowButton.IsChecked = string.Equals(provider, "Codex", StringComparison.OrdinalIgnoreCase);
        WorkBuddyRowButton.IsChecked = string.Equals(provider, "WorkBuddy", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearProviderHighlight()
    {
        _highlightedProvider = null;
        YoyoRowButton.IsChecked = CodexRowButton.IsChecked = WorkBuddyRowButton.IsChecked = false;
    }

    private void UpdateConfirmationNotice(WorkBuddyStatus workBuddy)
    {
        if (!IsProviderVisible("workbuddy") || !workBuddy.RequiresConfirmation || !_settings.EnableConfirmationNotifications)
        {
            var wasActive = _activeConfirmationNotice is not null;
            _activeConfirmationNotice = null;
            _workBuddyConfirmationId = null;
            if (wasActive)
            {
                _completionTimer.Stop();
                _notificationHoldActive = false;
                ClearProviderHighlight();
                if (!Island.IsMouseOver) CollapseIsland(true);
            }
            return;
        }

        _activeConfirmationNotice = $"WorkBuddy 需要你的确认 · {workBuddy.ConfirmationPrompt ?? "请打开 WorkBuddy 查看并选择"}";
        if (workBuddy.ConfirmationId == _workBuddyConfirmationId) return;
        _workBuddyConfirmationId = workBuddy.ConfirmationId;
        _activeCompletionNotice = null;
        _activeSystemNotice = null;
        HighlightProvider("WorkBuddy");
        RecentResultText.Text = ActiveNoticeText;
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
        _activeSystemNotice = null;
        if (_activeConfirmationNotice is null) ClearProviderHighlight();
        RecentResultText.Text = ActiveNoticeText;
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
        _trayWakeActive = false;
        _completionTimer.Stop();
        _notificationHoldActive = false;
        _activeCompletionNotice = null;
        _activeSystemNotice = null;
        ClearProviderHighlight();
        RecentResultText.Text = ActiveNoticeText;
        CollapseIsland(true);
        e.Handled = true;
    }

    private void UpdateDisplayMode()
    {
        if (_trayWakeActive)
        {
            ShowIslandForFocusMode(animate: _inactivityHidden);
            return;
        }
        if (_activeCompletionNotice is not null || _activeConfirmationNotice is not null || _activeSystemNotice is not null)
        {
            ShowIslandForFocusMode(animate: _inactivityHidden);
            return;
        }
        if (VisibleProviderOrder().Length == 0)
        {
            HideIslandForFocusMode(animate: true);
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
        _dragMoved = false;
        _dragStartCursorPixels = NativeWindow.GetCursorPosition();
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;
        Island.Cursor = Cursors.SizeAll; Island.Opacity = Math.Max(.55, _settings.Opacity - .15);
        Island.CaptureMouse();
    }

    private void Island_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Mouse.LeftButton != MouseButtonState.Pressed) return;
        var cursor = NativeWindow.GetCursorPosition();
        var deltaPixels = new Vector(cursor.X - _dragStartCursorPixels.X, cursor.Y - _dragStartCursorPixels.Y);
        if (!_dragMoved && Math.Abs(deltaPixels.X) < 1 && Math.Abs(deltaPixels.Y) < 1) return;
        _dragMoved = true;
        var delta = DevicePixelsToDips(deltaPixels);
        Left = _dragStartWindowLeft + delta.X;
        Top = _dragStartWindowTop + delta.Y;
        e.Handled = true;
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
            ClampCollapsedPosition();
            if (_dragMoved)
            {
                _settings.PositionPreset = "custom";
                SnapToHorizontalCenter();
            }
            UpdateCollapsedAnchorFromCurrentGeometry();
            OrientCollapsedIsland();
            if (_dragMoved)
            {
                SavePosition();
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            EnsureTaskbarZOrder();
        }
        _dragging = false;
        _dragMoved = false;
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
        if (_trayWakeActive)
        {
            _enterTimer.Stop();
            return;
        }
        _manualCollapseUntilPointerExit = false;
        _enterTimer.Stop();
        if (!_dragging && !_holdArmed && _expanded && !_islandAnimationInProgress && Island.ContextMenu?.IsOpen != true) _leaveTimer.Start();
    }

    private void TryCollapseAfterPointerExit()
    {
        _leaveTimer.Stop();
        if (_trayWakeActive || !_expanded || _dragging || _holdArmed || _islandAnimationInProgress || Island.ContextMenu?.IsOpen == true) return;
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

        // Restore the collapsed island to its saved screen anchor before DragMove starts.
        // Mapping the expanded handle's pointer position into the much shorter collapsed
        // island makes an upward-expanded island jump from the taskbar to the pointer.
        var collapsedLeft = _horizontalExpansionCompensated ? _collapsedLeftBeforeExpansion : Left;
        var collapsedTop = _expandUp ? _collapsedAnchorTop : Top;

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

        Left = collapsedLeft;
        Top = collapsedTop;
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
        ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(ExpandedPanel.Opacity, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
        IEasingFunction easing = _horizontalExpansionCompensated
            ? new CubicEase { EasingMode = EasingMode.EaseInOut }
            : _settings.EnableSpringAnimation
            ? new BackEase { Amplitude = .12, EasingMode = EasingMode.EaseInOut }
            : new CubicEase { EasingMode = EasingMode.EaseInOut };
        if (_horizontalExpansionCompensated)
            BeginAnimation(LeftProperty, new DoubleAnimation(Left, _collapsedLeftBeforeExpansion, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        var width = new DoubleAnimation(Island.ActualWidth, _settings.IslandWidth, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing };
        var height = new DoubleAnimation(Island.ActualHeight, CollapsedHeight, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing };
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
            if ((_inactivityHidden || (UsesActiveOnlyDisplay && !_anyBusy)) && _activeCompletionNotice is null && _activeConfirmationNotice is null && _activeSystemNotice is null) HideIslandForFocusMode();
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

    private bool OpenYoyo()
    {
        if (ActivateProcess("HnMagicClawUI")) return true;
        var executable = ApplicationLocator.FindYoyoExecutable(_settings.YoyoExecutablePath);
        if (executable is null) return false;
        try { return Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }) is not null; }
        catch { return false; }
    }

    private bool OpenCodex()
    {
        if (ActivateProcess("ChatGPT", IsCodexProcess)) return true;
        var executable = ApplicationLocator.FindCodexDesktopExecutable(_settings.CodexExecutablePath, IsCodexProcess);
        if (executable is not null)
        {
            try { return Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }) is not null; }
            catch { }
        }
        try { return Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true }) is not null; }
        catch { return false; }
    }

    private bool OpenWorkBuddy()
    {
        if (ActivateProcess("WorkBuddy")) return true;
        var executable = ApplicationLocator.FindWorkBuddyExecutable(_settings.WorkBuddyExecutablePath);
        if (executable is null) return false;
        try { return Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }) is not null; }
        catch { return false; }
    }

    private void CaptureExecutablePaths()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPathCapture < TimeSpan.FromMinutes(1)) return;
        _lastPathCapture = now;
        RefreshProviderInstallations();
    }

    internal IReadOnlyList<ProviderInstallationState> GetProviderInstallations()
        => ProviderOrder().Select(key => new ProviderInstallationState(key, ProviderLabel(key), IsProviderInstalled(key), key switch
        {
            "yoyo" => _settings.YoyoExecutablePath,
            "codex" => _settings.CodexExecutablePath,
            "workbuddy" => _settings.WorkBuddyExecutablePath,
            _ => null
        })).ToArray();

    internal bool SetProviderExecutablePath(string provider, string path)
    {
        if (!ApplicationLocator.IsExpectedProviderExecutable(provider, path)) return false;
        var fullPath = Path.GetFullPath(path);
        switch (provider)
        {
            case "yoyo": _settings.YoyoExecutablePath = fullPath; break;
            case "codex": _settings.CodexExecutablePath = fullPath; break;
            case "workbuddy": _settings.WorkBuddyExecutablePath = fullPath; break;
            default: return false;
        }
        AppSettings.Save(_settings);
        RefreshProviderInstallations(forceNotification: true);
        _ = RefreshStatusAsync();
        return true;
    }

    internal void RescanProviderInstallations()
    {
        _lastPathCapture = DateTimeOffset.MinValue;
        RefreshProviderInstallations(forceNotification: true);
        _ = RefreshStatusAsync();
    }

    private void RefreshProviderInstallations(bool forceNotification = false)
    {
        var yoyo = ApplicationLocator.FindYoyoExecutable(_settings.YoyoExecutablePath);
        var codex = ApplicationLocator.FindCodexDesktopExecutable(_settings.CodexExecutablePath, IsCodexProcess);
        var workBuddy = ApplicationLocator.FindWorkBuddyExecutable(_settings.WorkBuddyExecutablePath);
        var changed = false;
        if (yoyo is not null && !string.Equals(_settings.YoyoExecutablePath, yoyo, StringComparison.OrdinalIgnoreCase)) { _settings.YoyoExecutablePath = yoyo; changed = true; }
        if (codex is not null && !string.Equals(_settings.CodexExecutablePath, codex, StringComparison.OrdinalIgnoreCase)) { _settings.CodexExecutablePath = codex; changed = true; }
        if (workBuddy is not null && !string.Equals(_settings.WorkBuddyExecutablePath, workBuddy, StringComparison.OrdinalIgnoreCase)) { _settings.WorkBuddyExecutablePath = workBuddy; changed = true; }
        var yoyoInstalled = yoyo is not null;
        var codexInstalled = codex is not null || ApplicationLocator.IsCodexDesktopInstalled(_settings.CodexExecutablePath, IsCodexProcess);
        var workBuddyInstalled = workBuddy is not null;
        var availabilityChanged = yoyoInstalled != _yoyoInstalled || codexInstalled != _codexInstalled || workBuddyInstalled != _workBuddyInstalled;
        _yoyoInstalled = yoyoInstalled;
        _codexInstalled = codexInstalled;
        _workBuddyInstalled = workBuddyInstalled;
        if (changed) AppSettings.Save(_settings);
        if (availabilityChanged)
        {
            ApplyProviderVisibility();
            if (_expanded) Island.Height = GetExpandedHeight();
        }
        if (availabilityChanged || forceNotification) ProviderAvailabilityChanged?.Invoke(this, EventArgs.Empty);
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
        var light = IsLightTheme;
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
        _islandMenu.Background = Brush(light ? "#F7FFFFFF" : "#F00E131D");
        _islandMenu.BorderBrush = Brush(light ? "#24182033" : "#1FFFFFFF");
        _islandMenu.Foreground = primary;
        Resources["IslandMenuHoverBrush"] = Brush(light ? "#10182033" : "#16FFFFFF");
        if (_islandMenu.IsOpen) RebuildIslandMenu();
    }

    private bool IsLightTheme => _settings.ThemeMode == "light" || (_settings.ThemeMode == "system" && SystemUsesLightTheme());

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
                    fiveHourResetsAt = codex.FiveHourResetsAt,
                    weeklyResetsAt = codex.WeeklyResetsAt,
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
    private void OpenYoyo_Click(object sender, RoutedEventArgs e) => OpenProviderAndAcknowledge("YOYO Claw", OpenYoyo, e);
    private void OpenCodex_Click(object sender, RoutedEventArgs e) => OpenProviderAndAcknowledge("Codex", OpenCodex, e);
    private void OpenWorkBuddy_Click(object sender, RoutedEventArgs e) => OpenProviderAndAcknowledge("WorkBuddy", OpenWorkBuddy, e);

    private void OpenProviderAndAcknowledge(string provider, Func<bool> openProvider, RoutedEventArgs e)
    {
        OpenProviderAndAcknowledge(provider, openProvider);
        e.Handled = true;
    }

    internal void OpenProviderFromMenu(string key)
    {
        switch (key.ToLowerInvariant())
        {
            case "yoyo": OpenProviderAndAcknowledge("YOYO Claw", OpenYoyo); break;
            case "codex": OpenProviderAndAcknowledge("Codex", OpenCodex); break;
            case "workbuddy": OpenProviderAndAcknowledge("WorkBuddy", OpenWorkBuddy); break;
        }
    }

    private void OpenProviderAndAcknowledge(string provider, Func<bool> openProvider)
    {
        var opened = _settings.EnableAppLaunch && !_dragging && openProvider();
        if (opened
            && string.Equals(provider, _highlightedProvider, StringComparison.OrdinalIgnoreCase)
            && (_activeCompletionNotice is not null || _activeSystemNotice is not null))
        {
            _manualCollapseUntilPointerExit = true;
            _completionTimer.Stop();
            _notificationHoldActive = false;
            _activeCompletionNotice = null;
            _activeSystemNotice = null;
            ClearProviderHighlight();
            RecentResultText.Text = ActiveNoticeText;
            CollapseIsland(true);
            UpdateDisplayMode();
        }
        else if (_activeCompletionNotice is null && _activeConfirmationNotice is null && _activeSystemNotice is null)
            ClearProviderHighlight();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ResetAndRefreshStatusAsync();
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}

internal sealed record ProviderMenuEntry(string Key, string Label, string State, string Color, bool CanLaunch);
