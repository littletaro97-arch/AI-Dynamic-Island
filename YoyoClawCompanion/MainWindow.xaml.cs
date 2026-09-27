using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class MainWindow : Window
{
    private static readonly Brush OnlineBrush = Brush("#3ED598"), BusyBrush = Brush("#FFA63D"), OfflineBrush = Brush("#727C90"), ErrorBrush = Brush("#F2686F"), AccentBrush = Brush("#8FA0FF");
    private readonly YoyoStatusService _statusService = new();
    private readonly WorkBuddyStatusService _workBuddyStatusService = new();
    private readonly WorkBuddyCreditsService _workBuddyCreditsService = new();
    private readonly CodexStatusService _codexStatusService = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private readonly DispatcherTimer _enterTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private IslandSettings _settings = AppSettings.Load();
    private SettingsWindow? _settingsWindow;
    private Point _dragCursorStart;
    private Point _dragWindowStart;
    private bool _holdArmed, _dragging, _refreshing, _expanded, _finishingGesture;
    private Brush _primaryTextBrush = Brush("#F2F5FF");

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
        LocationChanged += (_, _) => SavePosition();
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _holdTimer.Tick += (_, _) => ArmDrag();
        _enterTimer.Tick += (_, _) => { _enterTimer.Stop(); ExpandIsland(); };
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); CollapseIsland(); };
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
    }

    internal IslandSettings CurrentSettings => _settings;

    internal void ApplySettings(IslandSettings settings, bool persist = true)
    {
        settings.CornerRadius = Math.Clamp(settings.CornerRadius, 0, 24);
        settings.Opacity = Math.Clamp(settings.Opacity, 0.55, 1);
        settings.IslandWidth = Math.Clamp(settings.IslandWidth, 190, 340);
        settings.HoverDelayMs = Math.Clamp(settings.HoverDelayMs, 20, 400);
        settings.ThemeMode = settings.ThemeMode is "light" or "dark" ? settings.ThemeMode : "system";
        _settings = settings;
        Island.CornerRadius = new CornerRadius(settings.CornerRadius);
        Island.Opacity = settings.Opacity;
        if (!_expanded) Island.Width = settings.IslandWidth;
        Topmost = settings.Topmost;
        QuotaDial.Visibility = settings.ShowQuota ? Visibility.Visible : Visibility.Collapsed;
        YoyoIndicatorButton.Visibility = settings.ShowYoyo ? Visibility.Visible : Visibility.Collapsed;
        CodexIndicatorButton.Visibility = settings.ShowCodex ? Visibility.Visible : Visibility.Collapsed;
        WorkBuddyIndicatorButton.Visibility = settings.ShowWorkBuddy ? Visibility.Visible : Visibility.Collapsed;
        _enterTimer.Interval = TimeSpan.FromMilliseconds(settings.HoverDelayMs);
        SetLaunchControls(settings.EnableAppLaunch);
        if (!settings.EnableHoverExpansion && _expanded) CollapseIsland(true);
        ApplyTheme();
        Island.Effect = settings.ShowShadow ? (System.Windows.Media.Effects.Effect)FindResource("IslandShadow") : null;
        if (persist) AppSettings.Save(_settings);
        if (persist && IsLoaded) _ = RefreshStatusAsync();
    }

    internal void ResetPosition()
    {
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top;
        SavePosition();
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084) return IntPtr.Zero;
        var packed = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
        var bounds = Island.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Island.ActualWidth, Island.ActualHeight));
        if (bounds.Contains(point)) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings(_settings, false);
        if (_settings.X is double x && _settings.Y is double y)
        {
            Left = Math.Clamp(x, SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width);
            Top = Math.Clamp(y, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height);
        }
        else ResetPosition();
        await RefreshStatusAsync();
        _refreshTimer.Start();
    }

    private void SavePosition()
    {
        if (!IsLoaded) return;
        _settings.X = Left; _settings.Y = Top;
        AppSettings.Save(_settings);
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var workBuddyTask = _workBuddyStatusService.ReadAsync();
            var workBuddyCreditsTask = _workBuddyCreditsService.ReadAsync(_settings.ShowWorkBuddyCredits);
            var codexTask = _codexStatusService.ReadAsync(_settings.EnableCodexActivityDetection, _settings.ShowCodexLimits);
            var yoyoTask = _statusService.ReadAsync();
            var status = await yoyoTask;
            var workBuddy = await workBuddyTask;
            var workBuddyCredits = await workBuddyCreditsTask;
            var codex = await codexTask;
            CodexMiniDot.Fill = !codex.IsRunning ? OfflineBrush : codex.IsBusy ? BusyBrush : OnlineBrush;
            WorkBuddyMiniDot.Fill = !workBuddy.IsRunning ? OfflineBrush : !workBuddy.DataAvailable ? ErrorBrush : workBuddy.IsBusy ? BusyBrush : OnlineBrush;
            YoyoMiniDot.Fill = !status.IsYoyoRunning ? OfflineBrush : !status.TaskStatusAvailable ? ErrorBrush : status.IsBusy ? BusyBrush : status.LastTaskFailed ? ErrorBrush : OnlineBrush;
            StateDot.Fill = YoyoMiniDot.Fill;
            CodexDot.Fill = CodexMiniDot.Fill;
            WorkBuddyDot.Fill = WorkBuddyMiniDot.Fill;
            PointsText.Text = status.RemainingPoints is double remaining ? $"{remaining:0.##} 积分" : "积分 --";
            StateText.Text = !status.IsYoyoRunning ? "未运行" : !status.TaskStatusAvailable ? "接口不可用" : status.IsBusy ? "忙碌中" : "空闲";
            CodexStateText.Text = CodexSummary(codex);
            WorkBuddyStateText.Text = WorkBuddySummary(workBuddy, workBuddyCredits);
            RecentResultText.Text = status.RecentResult;
            UpdateQuotaDial(status);
            UpdateHeadline(status, codex, workBuddy);
            UpdateBusyAnimation(status.IsBusy);
            WriteStatusSnapshot(status, codex, workBuddy, workBuddyCredits);
        }
        catch (Exception error) { HeadlineText.Text = "状态刷新失败"; SummaryText.Text = error.GetType().Name; }
        finally { _refreshing = false; }
    }

    private void UpdateHeadline(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy)
    {
        var points = yoyo.RemainingPoints is double value ? $"{value:0.##} 积分" : "积分 --";
        HeadlineText.Foreground = yoyo.LastTaskFailed || (yoyo.IsYoyoRunning && !yoyo.TaskStatusAvailable) ? ErrorBrush : _primaryTextBrush;
        if (!yoyo.IsYoyoRunning) { HeadlineText.Text = "YOYO 未运行"; SummaryText.Text = codex.IsRunning || workBuddy.IsRunning ? "其他助手已就绪" : "未检测到运行实例"; }
        else if (!yoyo.TaskStatusAvailable) { HeadlineText.Text = "YOYO 状态不可用"; SummaryText.Text = points; }
        else if (yoyo.IsBusy) { HeadlineText.Text = "YOYO 执行中"; SummaryText.Text = yoyo.RecentResult; }
        else if (codex.IsBusy) { HeadlineText.Text = "Codex 执行中"; SummaryText.Text = CodexSummary(codex); }
        else if (workBuddy.IsBusy) { HeadlineText.Text = "WorkBuddy 执行中"; SummaryText.Text = workBuddy.Summary; }
        else if (yoyo.LastTaskFailed) { HeadlineText.Text = "1 项需要处理"; SummaryText.Text = yoyo.RecentResult; }
        else { HeadlineText.Text = "全部就绪"; SummaryText.Text = points; }
    }

    private void UpdateQuotaDial(YoyoStatus status)
    {
        var hasQuota = status.RemainingPoints is double && status.TotalPoints is > 0;
        var ratio = hasQuota ? Math.Clamp(status.RemainingPoints!.Value / status.TotalPoints!.Value, 0, 1) : 0;
        QuotaArc.Stroke = !status.IsYoyoRunning || !hasQuota ? OfflineBrush : ratio < .1 ? ErrorBrush : ratio < .3 ? BusyBrush : AccentBrush;
        const double c = 16, r = 15;
        if (ratio <= 0) { QuotaArc.Data = Geometry.Empty; return; }
        if (ratio >= .999) { QuotaArc.Data = new EllipseGeometry(new Point(c, c), r, r); return; }
        var angle = ratio * 360; var radians = (angle - 90) * Math.PI / 180;
        var figure = new PathFigure { StartPoint = new Point(c, c - r) };
        figure.Segments.Add(new ArcSegment(new Point(c + r * Math.Cos(radians), c + r * Math.Sin(radians)), new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true));
        QuotaArc.Data = new PathGeometry(new[] { figure });
    }

    private void UpdateBusyAnimation(bool busy)
    {
        BusyGlyph.Opacity = busy ? 1 : .45;
        if (!busy) { BusyGlyph.RenderTransform = Transform.Identity; return; }
        BusyGlyph.RenderTransformOrigin = new Point(.5, .5);
        var rotate = new RotateTransform(); BusyGlyph.RenderTransform = rotate;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.4)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _enterTimer.Stop(); _leaveTimer.Stop();
        _dragging = false; _holdArmed = false;
        _holdTimer.Start();
    }

    private void ArmDrag()
    {
        _holdTimer.Stop();
        if (Mouse.LeftButton != MouseButtonState.Pressed || !Island.IsMouseOver) return;
        _holdArmed = true;
        _dragCursorStart = PointToScreen(Mouse.GetPosition(this));
        _dragWindowStart = new Point(Left, Top);
        Island.CaptureMouse();
        Island.Cursor = Cursors.SizeAll; Island.Opacity = Math.Max(.55, _settings.Opacity - .15);
    }

    private void Island_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_holdArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = PointToScreen(e.GetPosition(this));
        var dx = cursor.X - _dragCursorStart.X;
        var dy = cursor.Y - _dragCursorStart.Y;
        if (!_dragging && Math.Abs(dx) < 2 && Math.Abs(dy) < 2) return;
        _dragging = true;
        Left = Math.Clamp(_dragWindowStart.X + dx, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(_dragWindowStart.Y + dy, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
    }

    private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FinishPointerGesture();
    private void Island_LostMouseCapture(object sender, MouseEventArgs e) => FinishPointerGesture();
    private void FinishPointerGesture()
    {
        if (_finishingGesture) return;
        _finishingGesture = true;
        _holdTimer.Stop(); _holdArmed = false; Island.Cursor = Cursors.Arrow; Island.Opacity = _settings.Opacity;
        if (Island.IsMouseCaptured) Island.ReleaseMouseCapture();
        if (_dragging) SavePosition();
        _dragging = false;
        _finishingGesture = false;
        if (Island.IsMouseOver) _enterTimer.Start();
    }

    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop();
        if (_settings.EnableHoverExpansion && !_dragging && !_holdArmed && !_expanded) _enterTimer.Start();
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _enterTimer.Stop();
        if (!_dragging && !_holdArmed && _expanded && Island.ContextMenu?.IsOpen != true) _leaveTimer.Start();
    }

    private void ExpandIsland()
    {
        if (!_settings.EnableHoverExpansion || _expanded || _dragging || _holdArmed) return;
        _expanded = true;
        ExpandedPanel.Visibility = Visibility.Visible;
        IEasingFunction easing = _settings.EnableSpringAnimation
            ? new BackEase { Amplitude = .28, EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(Island.ActualWidth, Math.Max(408, _settings.IslandWidth), TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(Island.ActualHeight, 172, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.FromMilliseconds(70) });
    }

    private void CollapseIsland(bool force = false)
    {
        if (!_expanded || (!force && (_dragging || _holdArmed))) return;
        _expanded = false;
        ExpandedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(ExpandedPanel.Opacity, 0, TimeSpan.FromMilliseconds(100)));
        IEasingFunction easing = _settings.EnableSpringAnimation
            ? new BackEase { Amplitude = .22, EasingMode = EasingMode.EaseIn }
            : new CubicEase { EasingMode = EasingMode.EaseIn };
        var width = new DoubleAnimation(Island.ActualWidth, _settings.IslandWidth, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing };
        var height = new DoubleAnimation(Island.ActualHeight, 48, TimeSpan.FromMilliseconds(210)) { EasingFunction = easing };
        height.Completed += (_, _) => ExpandedPanel.Visibility = Visibility.Collapsed;
        Island.BeginAnimation(WidthProperty, width);
        Island.BeginAnimation(HeightProperty, height);
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

    private static void OpenYoyo()
    {
        if (ActivateProcess("HnMagicClawUI")) return;
        var executable = FindYoyoExecutable();
        if (executable is not null) Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private static void OpenCodex()
    {
        if (ActivateProcess("ChatGPT", IsCodexProcess)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true });
    }

    private static void OpenWorkBuddy()
    {
        if (ActivateProcess("WorkBuddy")) return;
        const string path = @"E:\D-diskExpansionCabin\workbuddy\WorkBuddy.exe";
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string? FindYoyoExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "HONOR", "MagicClaw");
        try
        {
            var currentFile = Path.Combine(root, "current.json");
            if (File.Exists(currentFile))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(currentFile));
                if (document.RootElement.TryGetProperty("path", out var value))
                {
                    var resolved = Path.GetFullPath(Path.Combine(root, value.GetString() ?? ""));
                    if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(resolved)) return resolved;
                }
            }
            var launcher = Path.Combine(root, "HnMagicClawUI.exe"); return File.Exists(launcher) ? launcher : null;
        }
        catch { return null; }
    }

    private string CodexSummary(CodexStatus status)
    {
        if (!status.IsRunning) return "未运行";
        var state = _settings.EnableCodexActivityDetection ? (status.IsBusy ? "执行中" : "空闲") : "已打开";
        if (!_settings.ShowCodexLimits) return state;
        if (!status.LimitsAvailable) return $"{state} · 限额不可用";
        var pieces = new List<string> { state };
        if (status.FiveHourRemainingPercent is int fiveHour) pieces.Add($"5小时 {fiveHour}%");
        if (status.WeeklyRemainingPercent is int weekly) pieces.Add($"本周 {weekly}%");
        return string.Join(" · ", pieces);
    }

    private string WorkBuddySummary(WorkBuddyStatus status, WorkBuddyCredits credits)
    {
        if (!status.IsRunning) return "未运行";
        var state = status.IsBusy ? "执行中" : "空闲";
        if (!_settings.ShowWorkBuddyCredits) return state;
        return credits.Available && credits.Remaining is double remaining ? $"{state} · {remaining:0.##} 积分" : $"{state} · 积分不可用";
    }

    private void SetLaunchControls(bool enabled)
    {
        var cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        YoyoButton.Cursor = cursor;
        YoyoIndicatorButton.Cursor = cursor;
        CodexIndicatorButton.Cursor = cursor;
        WorkBuddyIndicatorButton.Cursor = cursor;
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
        _primaryTextBrush = primary;
        Island.Background = Brush(light ? "#F4FFFFFF" : "#EB0E121C");
        Island.BorderBrush = Brush(light ? "#24182033" : "#1AFFFFFF");
        HeadlineText.Foreground = primary;
        SummaryText.Foreground = secondary;
        YoyoLabel.Foreground = primary;
        CodexLabel.Foreground = primary;
        WorkBuddyLabel.Foreground = primary;
        StateText.Foreground = secondary;
        CodexStateText.Foreground = secondary;
        WorkBuddyStateText.Foreground = secondary;
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

    private static void WriteStatusSnapshot(YoyoStatus yoyo, CodexStatus codex, WorkBuddyStatus workBuddy, WorkBuddyCredits credits)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion"); Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new { updatedAt = DateTimeOffset.Now, yoyo = new { running = yoyo.IsYoyoRunning, available = yoyo.TaskStatusAvailable, busy = yoyo.IsBusy, points = yoyo.RemainingPoints, totalPoints = yoyo.TotalPoints, result = yoyo.RecentResult }, codex = new { running = codex.IsRunning, busy = codex.IsBusy, fiveHourRemainingPercent = codex.FiveHourRemainingPercent, weeklyRemainingPercent = codex.WeeklyRemainingPercent }, workBuddy = new { running = workBuddy.IsRunning, available = workBuddy.DataAvailable, busy = workBuddy.IsBusy, summary = workBuddy.Summary, credits = credits.Remaining, totalCredits = credits.Total } });
            File.WriteAllText(Path.Combine(directory, "status.json"), json); File.WriteAllText(Path.Combine(directory, $"status-{Environment.ProcessId}.json"), json);
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
    private void OpenHome_Click(object sender, RoutedEventArgs e) => OpenHome();
    private void OpenYoyo_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenYoyo(); e.Handled = true; }
    private void OpenCodex_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenCodex(); e.Handled = true; }
    private void OpenWorkBuddy_Click(object sender, RoutedEventArgs e) { if (_settings.EnableAppLaunch && !_dragging) OpenWorkBuddy(); e.Handled = true; }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
