using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using YoyoClawCompanion.Services;
using Brush = System.Windows.Media.Brush;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ProgressBar = System.Windows.Controls.ProgressBar;
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace YoyoClawCompanion;

public partial class SettingsWindow : Window
{
    private const string ProviderOrderDragFormat = "AI.DynamicIsland.ProviderOrder";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private static readonly DependencyProperty AnimatedScrollOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedScrollOffset), typeof(double), typeof(SettingsWindow),
        new PropertyMetadata(0d, OnAnimatedScrollOffsetChanged));
    private static readonly DependencyProperty NavigationWidthProperty = DependencyProperty.Register(
        "NavigationWidth", typeof(double), typeof(SettingsWindow),
        new PropertyMetadata(200d, (owner, args) => ((SettingsWindow)owner).NavigationColumn.Width = new GridLength((double)args.NewValue)));
    private int _navigationAnimationVersion;
    private int _updateProgressAnimationVersion;
    private bool _updateProgressVisible;

    private readonly MainWindow _island;
    private bool _loading = true;
    private bool _updatingWidthRange;
    private System.Windows.Point _providerDragStart;
    private ProviderOrderItem? _providerDragCandidate;
    private IReadOnlyList<SettingsPresetSlot> _presets = [];
    private readonly double[] _presetMarqueeOffsets = new double[4];
    private readonly double[] _presetMarqueeCycles = new double[4];
    private long _presetMarqueeLastTick;
    private bool _presetMarqueeSubscribed;

    public ObservableCollection<ProviderOrderItem> ProviderOrderItems { get; } = [];

    internal SettingsWindow(MainWindow island)
    {
        _island = island;
        InitializeComponent();
        PrepareSettingIcons();
        Icon = App.CreateWindowIcon();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        _island.PositionChanged += Island_PositionChanged;
        _island.ProviderAvailabilityChanged += Island_ProviderAvailabilityChanged;
        _island.UpdateService.Changed += UpdateService_Changed;
        Closed += (_, _) => { _island.PositionChanged -= Island_PositionChanged; _island.ProviderAvailabilityChanged -= Island_ProviderAvailabilityChanged; _island.UpdateService.Changed -= UpdateService_Changed; StopPresetMarquees(); };
        SizeChanged += (_, _) => SchedulePresetMarquees();
        Loaded += (_, _) => Dispatcher.BeginInvoke(UpdateActiveNavigation, DispatcherPriority.Loaded);
        LoadValues(island.CurrentSettings);
        _loading = false;
        UpdateLabels();
        UpdateDependencyStates();
        RefreshProviderInstallations();
        RefreshUpdateState();
        ApplyNavigationLayout();
        RefreshPresetCards();
        ApplyPanelTheme();
    }

    private void LoadValues(IslandSettings value)
    {
        CornerSlider.Value = value.CornerRadius;
        OpacitySlider.Value = value.Opacity * 100;
        WidthSlider.Value = value.IslandWidth;
        UpdateExpandedWidthRange(value.ExpandedIslandWidth ?? Math.Min(value.IslandWidth + 100, 500));
        HeightSlider.Value = value.IslandHeight;
        TextSizeSlider.Value = value.TextSize;
        ShadowCheck.IsChecked = value.ShowShadow;
        ReadyClockCheck.IsChecked = value.ShowClockWhenReady;
        TopmostCheck.IsChecked = value.Topmost;
        YoyoCheck.IsChecked = value.ShowYoyo;
        CodexCheck.IsChecked = value.ShowCodex;
        WorkBuddyCheck.IsChecked = value.ShowWorkBuddy;
        TrayIconCheck.IsChecked = value.ShowTrayIcon;
        StartupCheck.IsChecked = value.StartWithWindows;
        AutoUpdateCheck.IsChecked = value.AutoCheckForUpdates;
        foreach (ComboBoxItem item in ThemeCombo.Items) if (string.Equals(item.Tag?.ToString(), value.ThemeMode, StringComparison.OrdinalIgnoreCase)) ThemeCombo.SelectedItem = item;
        if (ThemeCombo.SelectedIndex < 0) ThemeCombo.SelectedIndex = 0;
        CodexActivityCheck.IsChecked = value.EnableCodexActivityDetection;
        YoyoCreditsCheck.IsChecked = value.ShowYoyoCredits;
        CodexLimitsCheck.IsChecked = value.ShowCodexLimits;
        WorkBuddyCreditsCheck.IsChecked = value.ShowWorkBuddyCredits;
        CodexResetReminderCheck.IsChecked = value.EnableCodexResetReminder;
        YoyoAutoCheckinCheck.IsChecked = value.EnableYoyoAutoCheckin;
        YoyoLaunchForCheckinCheck.IsChecked = value.LaunchYoyoForAutoCheckin;
        AppLaunchCheck.IsChecked = value.EnableAppLaunch;
        HoverExpansionCheck.IsChecked = value.EnableHoverExpansion;
        SpringAnimationCheck.IsChecked = value.EnableSpringAnimation;
        CompletionNotificationsCheck.IsChecked = value.EnableCompletionNotifications;
        ConfirmationNotificationsCheck.IsChecked = value.EnableConfirmationNotifications;
        ReverseHoverCheck.IsChecked = value.EnableReverseHover;
        FullscreenActiveOnlyCheck.IsChecked = value.EnableFullscreenActiveOnly;
        UnchangedAutoHideCheck.IsChecked = value.EnableUnchangedAutoHide;
        ReplyFirstWhenExpandedUpCheck.IsChecked = value.PutReplyFirstWhenExpandedUp;
        AllowExpandedBeyondScreenCheck.IsChecked = value.AllowExpandedBeyondScreen;
        UnchangedAutoHideSlider.Value = value.UnchangedAutoHideMinutes;
        HoverDelaySlider.Value = value.HoverDelayMs;
        QuotaScrollSpeedSlider.Value = value.QuotaScrollSpeed;
        CompletionDisplaySlider.Value = value.CompletionDisplaySeconds;
        CodexResetReminderSlider.Value = value.CodexResetReminderMinutes;
        foreach (ComboBoxItem item in MaxResponseLinesCombo.Items) if (item.Tag?.ToString() == value.MaxResponseLines.ToString()) MaxResponseLinesCombo.SelectedItem = item;
        if (MaxResponseLinesCombo.SelectedIndex < 0) MaxResponseLinesCombo.SelectedIndex = 2;
        foreach (ComboBoxItem item in DisplayModeCombo.Items) if (string.Equals(item.Tag?.ToString(), value.DisplayMode, StringComparison.OrdinalIgnoreCase)) DisplayModeCombo.SelectedItem = item;
        if (DisplayModeCombo.SelectedIndex < 0) DisplayModeCombo.SelectedIndex = 0;
        LoadProviderOrder(value.ProviderOrder);
        UpdatePositionControls(value.PositionPreset);
    }

    private void Setting_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (_updatingWidthRange) return;
        _loading = true;
        try
        {
            if (ReferenceEquals(sender, ReverseHoverCheck) && ReverseHoverCheck.IsChecked == true)
                HoverExpansionCheck.IsChecked = false;
            else if (ReferenceEquals(sender, HoverExpansionCheck) && HoverExpansionCheck.IsChecked == true)
                ReverseHoverCheck.IsChecked = false;
        }
        finally { _loading = false; }
        UpdateExpandedWidthRange(ExpandedWidthSlider.Value);
        var current = _island.CurrentSettings;
        current.CornerRadius = CornerSlider.Value;
        current.Opacity = OpacitySlider.Value / 100;
        current.IslandWidth = WidthSlider.Value;
        current.ExpandedIslandWidth = ExpandedWidthSlider.Value;
        current.IslandHeight = HeightSlider.Value;
        current.TextSize = TextSizeSlider.Value;
        current.ShowShadow = ShadowCheck.IsChecked == true;
        current.ShowClockWhenReady = ReadyClockCheck.IsChecked == true;
        current.Topmost = TopmostCheck.IsChecked == true;
        current.ShowYoyo = YoyoCheck.IsChecked == true;
        current.ShowCodex = CodexCheck.IsChecked == true;
        current.ShowWorkBuddy = WorkBuddyCheck.IsChecked == true;
        current.ShowTrayIcon = TrayIconCheck.IsChecked == true;
        current.StartWithWindows = StartupCheck.IsChecked == true;
        current.AutoCheckForUpdates = AutoUpdateCheck.IsChecked == true;
        current.ThemeMode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        current.EnableCodexActivityDetection = CodexActivityCheck.IsChecked == true;
        current.ShowYoyoCredits = YoyoCreditsCheck.IsChecked == true;
        current.ShowCodexLimits = CodexLimitsCheck.IsChecked == true;
        current.ShowWorkBuddyCredits = WorkBuddyCreditsCheck.IsChecked == true;
        current.EnableCodexResetReminder = CodexResetReminderCheck.IsChecked == true;
        current.EnableYoyoAutoCheckin = YoyoAutoCheckinCheck.IsChecked == true;
        current.LaunchYoyoForAutoCheckin = YoyoLaunchForCheckinCheck.IsChecked == true;
        current.EnableAppLaunch = AppLaunchCheck.IsChecked == true;
        current.EnableHoverExpansion = HoverExpansionCheck.IsChecked == true;
        current.EnableSpringAnimation = SpringAnimationCheck.IsChecked == true;
        current.EnableCompletionNotifications = CompletionNotificationsCheck.IsChecked == true;
        current.EnableConfirmationNotifications = ConfirmationNotificationsCheck.IsChecked == true;
        current.EnableReverseHover = ReverseHoverCheck.IsChecked == true;
        current.EnableFullscreenActiveOnly = FullscreenActiveOnlyCheck.IsChecked == true;
        current.EnableUnchangedAutoHide = UnchangedAutoHideCheck.IsChecked == true;
        current.PutReplyFirstWhenExpandedUp = ReplyFirstWhenExpandedUpCheck.IsChecked == true;
        current.AllowExpandedBeyondScreen = AllowExpandedBeyondScreenCheck.IsChecked == true;
        current.UnchangedAutoHideMinutes = UnchangedAutoHideSlider.Value;
        current.HoverDelayMs = HoverDelaySlider.Value;
        current.QuotaScrollSpeed = QuotaScrollSpeedSlider.Value;
        current.CompletionDisplaySeconds = CompletionDisplaySlider.Value;
        current.CodexResetReminderMinutes = CodexResetReminderSlider.Value;
        current.MaxResponseLines = int.TryParse((MaxResponseLinesCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var lines) ? lines : 3;
        current.DisplayMode = (DisplayModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "always";
        var refreshStatus = ReferenceEquals(sender, CodexActivityCheck)
            || ReferenceEquals(sender, YoyoCreditsCheck)
            || ReferenceEquals(sender, CodexLimitsCheck)
            || ReferenceEquals(sender, WorkBuddyCreditsCheck)
            || ReferenceEquals(sender, ConfirmationNotificationsCheck)
            || ReferenceEquals(sender, ReadyClockCheck);
        _island.ApplySettings(current, refreshStatus: refreshStatus, preserveMarquee: ReferenceEquals(sender, QuotaScrollSpeedSlider));
        if (ReferenceEquals(sender, YoyoAutoCheckinCheck) || ReferenceEquals(sender, YoyoLaunchForCheckinCheck)) _island.StartYoyoCheckinFromSettings();
        UpdateLabels();
        UpdateDependencyStates();
        ApplyPanelTheme();
    }

    private void UpdateDependencyStates()
    {
        SetDependentState(HoverDelayPanel, HoverExpansionCheck.IsChecked == true);
        SetDependentState(UnchangedAutoHidePanel, UnchangedAutoHideCheck.IsChecked == true);
        SetDependentState(CompletionDisplayPanel,
            CompletionNotificationsCheck.IsChecked == true || ConfirmationNotificationsCheck.IsChecked == true);
        SetDependentState(CodexResetReminderPanel, CodexResetReminderCheck.IsChecked == true);
        SetDependentState(YoyoLaunchForCheckinCheck, YoyoAutoCheckinCheck.IsChecked == true);
        var installations = _island.GetProviderInstallations().ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var yoyoInstalled = installations.TryGetValue("yoyo", out var yoyo) && yoyo.IsInstalled;
        var codexInstalled = installations.TryGetValue("codex", out var codex) && codex.IsInstalled;
        var workBuddyInstalled = installations.TryGetValue("workbuddy", out var workBuddy) && workBuddy.IsInstalled;
        foreach (var control in new UIElement[] { YoyoCheck, YoyoCreditsCheck, YoyoAutoCheckinCheck }) SetDependentState(control, yoyoInstalled);
        SetDependentState(YoyoLaunchForCheckinCheck, yoyoInstalled && YoyoAutoCheckinCheck.IsChecked == true);
        foreach (var control in new UIElement[] { CodexCheck, CodexActivityCheck, CodexLimitsCheck, CodexResetReminderCheck }) SetDependentState(control, codexInstalled);
        foreach (var control in new UIElement[] { WorkBuddyCheck, WorkBuddyCreditsCheck, ConfirmationNotificationsCheck }) SetDependentState(control, workBuddyInstalled);
        SetDependentState(CodexResetReminderPanel, codexInstalled && CodexResetReminderCheck.IsChecked == true);
    }

    private void Island_ProviderAvailabilityChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => { RefreshProviderInstallations(); UpdateDependencyStates(); });

    private void RefreshProviderInstallations()
    {
        foreach (var state in _island.GetProviderInstallations())
        {
            var target = state.Key switch { "yoyo" => YoyoInstallStatus, "codex" => CodexInstallStatus, _ => WorkBuddyInstallStatus };
            target.Text = state.IsInstalled ? state.ExecutablePath ?? "已安装（系统应用）" : "未安装 · 可手动指定";
            target.ToolTip = state.ExecutablePath;
        }
        LoadProviderOrder(_island.CurrentSettings.ProviderOrder);
    }

    private void RescanProviders_Click(object sender, RoutedEventArgs e) => _island.RescanProviderInstallations();

    private void UpdateService_Changed(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshUpdateState);

    private void RefreshUpdateState()
    {
        var state = _island.UpdateService.Snapshot;
        UpdateVersionText.Text = state.LatestVersion is null ? $"当前版本 {state.CurrentVersion}" : $"当前 {state.CurrentVersion} · 最新 {state.LatestVersion}";
        UpdateStatusText.Text = state.Status;
        TransitionUpdateProgress(state.Progress, state.IsBusy && state.Progress > 0);
        CheckUpdateButton.IsEnabled = !state.IsBusy;
        InstallUpdateButton.IsEnabled = !state.IsBusy && state.UpdateAvailable;
    }

    private void TransitionUpdateProgress(double value, bool visible)
    {
        var previous = UpdateProgress.Value;
        UpdateProgress.BeginAnimation(ProgressBar.ValueProperty, null);
        UpdateProgress.Value = Math.Clamp(value, 0, 100);
        if (visible)
            UpdateProgress.BeginAnimation(ProgressBar.ValueProperty,
                new DoubleAnimation(previous, UpdateProgress.Value, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (_updateProgressVisible == visible) return;
        _updateProgressVisible = visible;
        var version = ++_updateProgressAnimationVersion;
        var from = UpdateProgress.Visibility == Visibility.Visible ? UpdateProgress.Opacity : 0;
        UpdateProgress.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(from, visible ? 1 : 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (version != _updateProgressAnimationVersion) return;
            if (!visible) UpdateProgress.Visibility = Visibility.Collapsed;
        };
        UpdateProgress.BeginAnimation(OpacityProperty, fade);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await _island.UpdateService.CheckAsync();
    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await _island.UpdateService.DownloadAndInstallAsync();

    private void ChooseProviderPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string provider }) return;
        var expected = provider switch { "yoyo" => "HnMagicClawUI.exe", "codex" => "ChatGPT.exe", _ => "WorkBuddy.exe" };
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = $"选择 {ProviderCatalog.DisplayName(provider)} 主程序", Filter = $"{expected}|{expected}|可执行文件|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        if (_island.SetProviderExecutablePath(provider, dialog.FileName)) return;
        System.Windows.MessageBox.Show(this, $"请选择 {expected}。", "无法识别应用", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void SetDependentState(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : .42;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPresetSlot(sender, out var slot)) return;
        try
        {
            SettingsPresetStore.Save(slot, _island.CurrentSettings);
            RefreshPresetCards();
        }
        catch
        {
            var status = slot switch { 1 => Preset1Status, 2 => Preset2Status, _ => Preset3Status };
            status.Text = "保存失败";
        }
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPresetSlot(sender, out var slot)) return;
        var preset = _presets.FirstOrDefault(item => item.Slot == slot);
        if (preset is null) return;

        var applied = SettingsPresetStore.Apply(preset, _island.CurrentSettings);
        _loading = true;
        try
        {
            _island.ApplyPresetSettings(applied);
            LoadValues(applied);
        }
        finally { _loading = false; }
        UpdateLabels();
        UpdateDependencyStates();
        ApplyPanelTheme();
    }

    private static bool TryReadPresetSlot(object sender, out int slot)
        => int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out slot) && slot is >= 1 and <= 3;

    private void RefreshPresetCards()
    {
        _presets = SettingsPresetStore.Load();
        for (var slot = 1; slot <= 3; slot++)
        {
            var preset = _presets.FirstOrDefault(item => item.Slot == slot);
            var status = slot switch { 1 => Preset1Status, 2 => Preset2Status, _ => Preset3Status };
            var apply = slot switch { 1 => ApplyPreset1Button, 2 => ApplyPreset2Button, _ => ApplyPreset3Button };
            var (nameText, nameClone, editor, _, _, _) = PresetNameControls(slot);
            var displayName = preset is null ? $"方案 {slot}" : SettingsPresetStore.DisplayName(preset);
            nameText.Text = nameClone.Text = editor.Text = displayName;
            status.Text = preset is null ? "尚未保存" : $"已保存 {preset.SavedAt.LocalDateTime:MM-dd HH:mm}";
            apply.IsEnabled = preset is not null;
        }
        SchedulePresetMarquees();
    }

    private void PresetNameDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryReadPresetSlot(sender, out var slot) || _presets.All(item => item.Slot != slot)) return;
        var (_, _, editor, display, _, _) = PresetNameControls(slot);
        display.Visibility = Visibility.Collapsed;
        editor.Visibility = Visibility.Visible;
        editor.Focus();
        editor.SelectAll();
        e.Handled = true;
    }

    private void PresetNameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor) return;
        if (e.Key == Key.Enter) { FinishPresetRename(editor, true); e.Handled = true; }
        else if (e.Key == Key.Escape) { FinishPresetRename(editor, false); e.Handled = true; }
    }

    private void PresetNameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Visibility: Visibility.Visible } editor) FinishPresetRename(editor, true);
    }

    private void FinishPresetRename(TextBox editor, bool save)
    {
        if (!TryReadPresetSlot(editor, out var slot)) return;
        if (save)
        {
            try { SettingsPresetStore.Rename(slot, editor.Text); } catch { }
        }
        editor.Visibility = Visibility.Collapsed;
        var (_, _, _, display, _, _) = PresetNameControls(slot);
        display.Visibility = Visibility.Visible;
        RefreshPresetCards();
    }

    private void SchedulePresetMarquees()
        => Dispatcher.BeginInvoke(UpdatePresetMarquees, DispatcherPriority.Loaded);

    private void UpdatePresetMarquees()
    {
        StopPresetMarquees();
        var any = false;
        for (var slot = 1; slot <= 3; slot++)
        {
            var (text, clone, editor, _, canvas, transforms) = PresetNameControls(slot);
            if (editor.Visibility == Visibility.Visible || canvas.ActualWidth <= 0) continue;
            text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = text.DesiredSize.Width;
            if (width <= canvas.ActualWidth) { clone.Visibility = Visibility.Collapsed; continue; }
            var gap = text.FontSize * 5;
            _presetMarqueeCycles[slot] = width + gap;
            Canvas.SetLeft(text, 0);
            Canvas.SetLeft(clone, _presetMarqueeCycles[slot]);
            transforms.Primary.X = transforms.Clone.X = 0;
            clone.Visibility = Visibility.Visible;
            any = true;
        }
        if (!any) return;
        _presetMarqueeLastTick = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += AdvancePresetMarquees;
        _presetMarqueeSubscribed = true;
    }

    private void AdvancePresetMarquees(object? sender, EventArgs e)
    {
        if (!IsVisible) { StopPresetMarquees(); return; }
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(.1, Math.Max(0, (now - _presetMarqueeLastTick) / (double)Stopwatch.Frequency));
        _presetMarqueeLastTick = now;
        for (var slot = 1; slot <= 3; slot++)
        {
            if (_presetMarqueeCycles[slot] <= 0) continue;
            var (_, _, editor, _, _, transforms) = PresetNameControls(slot);
            if (editor.Visibility == Visibility.Visible) continue;
            _presetMarqueeOffsets[slot] = (_presetMarqueeOffsets[slot] + _island.CurrentSettings.QuotaScrollSpeed * elapsed) % _presetMarqueeCycles[slot];
            transforms.Primary.X = transforms.Clone.X = -_presetMarqueeOffsets[slot];
        }
    }

    private void StopPresetMarquees()
    {
        if (_presetMarqueeSubscribed) CompositionTarget.Rendering -= AdvancePresetMarquees;
        _presetMarqueeSubscribed = false;
        for (var slot = 1; slot <= 3; slot++)
        {
            _presetMarqueeOffsets[slot] = _presetMarqueeCycles[slot] = 0;
            var (text, clone, _, _, _, transforms) = PresetNameControls(slot);
            Canvas.SetLeft(text, 0); Canvas.SetLeft(clone, 0);
            transforms.Primary.X = transforms.Clone.X = 0;
        }
    }

    private (TextBlock Text, TextBlock Clone, TextBox Editor, Grid Display, Canvas Canvas, (TranslateTransform Primary, TranslateTransform Clone) Transforms) PresetNameControls(int slot)
        => slot switch
        {
            1 => (Preset1NameText, Preset1NameClone, Preset1NameEditor, Preset1NameDisplay, Preset1NameCanvas, (Preset1NameTranslate, Preset1NameCloneTranslate)),
            2 => (Preset2NameText, Preset2NameClone, Preset2NameEditor, Preset2NameDisplay, Preset2NameCanvas, (Preset2NameTranslate, Preset2NameCloneTranslate)),
            _ => (Preset3NameText, Preset3NameClone, Preset3NameEditor, Preset3NameDisplay, Preset3NameCanvas, (Preset3NameTranslate, Preset3NameCloneTranslate))
        };

    private void UpdateLabels()
    {
        CornerValue.Text = $"{CornerSlider.Value:0} px";
        OpacityValue.Text = $"{OpacitySlider.Value:0}%";
        WidthValue.Text = $"{WidthSlider.Value:0} px";
        ExpandedWidthValue.Text = $"{ExpandedWidthSlider.Value:0} px";
        HeightValue.Text = $"{HeightSlider.Value:0} px";
        TextSizeValue.Text = $"{TextSizeSlider.Value:0.#} px";
        HoverDelayValue.Text = $"{HoverDelaySlider.Value:0} ms";
        QuotaScrollSpeedValue.Text = $"{QuotaScrollSpeedSlider.Value:0} px/s";
        CompletionDisplayValue.Text = $"{CompletionDisplaySlider.Value:0} 秒";
        CodexResetReminderValue.Text = $"{CodexResetReminderSlider.Value:0} 分钟";
        UnchangedAutoHideValue.Text = $"{UnchangedAutoHideSlider.Value:0} 分钟";
        PreviewIsland.Width = Math.Min(400, Math.Max(190, WidthSlider.Value));
        PreviewIsland.Height = Math.Min(72, Math.Max(32, HeightSlider.Value));
        PreviewIsland.CornerRadius = new CornerRadius(Math.Min(CornerSlider.Value, PreviewIsland.Height / 2));
        PreviewIsland.Opacity = OpacitySlider.Value / 100;
    }

    private void UpdateExpandedWidthRange(double preferredValue)
    {
        _updatingWidthRange = true;
        try
        {
            var minimum = WidthSlider.Value;
            var maximum = Math.Min(500, minimum + 100);
            ExpandedWidthSlider.Minimum = minimum;
            ExpandedWidthSlider.Maximum = maximum;
            ExpandedWidthSlider.Value = Math.Clamp(preferredValue, minimum, maximum);
        }
        finally
        {
            _updatingWidthRange = false;
        }
    }

    private void PositionPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string preset }) return;
        _island.MoveToPositionPreset(preset);
        UpdatePositionControls(preset);
    }

    private void NudgePosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var parts = tag.Split(',');
        if (parts.Length != 2 || !double.TryParse(parts[0], out var horizontal) || !double.TryParse(parts[1], out var vertical)) return;
        _island.NudgePosition(horizontal, vertical);
        UpdatePositionControls("custom");
    }

    private void Island_PositionChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => UpdatePositionControls(_island.CurrentSettings.PositionPreset));

    private void UpdatePositionControls(string? preset)
    {
        var controls = new Dictionary<string, System.Windows.Controls.RadioButton>
        {
            ["topLeft"] = TopLeftPreset,
            ["topCenter"] = TopCenterPreset,
            ["topRight"] = TopRightPreset,
            ["bottomLeft"] = BottomLeftPreset,
            ["bottomCenter"] = BottomCenterPreset,
            ["bottomRight"] = BottomRightPreset
        };
        foreach (var pair in controls) pair.Value.IsChecked = string.Equals(pair.Key, preset, StringComparison.Ordinal);
        PositionModeText.Text = preset switch
        {
            "topLeft" => "标准位置 · 左上",
            "topCenter" => "标准位置 · 顶部",
            "topRight" => "标准位置 · 右上",
            "bottomLeft" => "标准位置 · 左下",
            "bottomCenter" => "标准位置 · 底部",
            "bottomRight" => "标准位置 · 右下",
            _ => "拖动岛 · 自定义位置"
        };
    }

    private void LoadProviderOrder(string? storedOrder)
    {
        var installed = _island.GetProviderInstallations().Where(item => item.IsInstalled).Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProviderOrderItems.Clear();
        foreach (var key in ProviderCatalog.ParseOrder(ProviderCatalog.NormalizeOrder(storedOrder)))
            if (installed.Contains(key)) ProviderOrderItems.Add(new ProviderOrderItem(key, ProviderCatalog.DisplayName(key), ProviderIcon(key)));
        RefreshProviderOrderState();
    }

    private ImageSource ProviderIcon(string key)
    {
        var actual = ProviderIconService.Load(key, _island.CurrentSettings);
        if (actual is not null) return actual;
        var fallback = (Geometry)FindResource(key switch
        {
            "yoyo" => "IconCircle",
            "codex" => "IconChart",
            "workbuddy" => "IconCoin",
            _ => "IconCircle"
        });
        return ProviderIconService.FromGeometry(fallback);
    }

    private void PrepareSettingIcons()
    {
        var checkBoxes = new[]
        {
            ShadowCheck, TopmostCheck, ReadyClockCheck, YoyoCheck, CodexCheck, WorkBuddyCheck, TrayIconCheck, StartupCheck,
            CodexActivityCheck, CodexLimitsCheck, YoyoCreditsCheck, WorkBuddyCreditsCheck, AppLaunchCheck,
            HoverExpansionCheck, SpringAnimationCheck, CompletionNotificationsCheck, ConfirmationNotificationsCheck,
            CodexResetReminderCheck, YoyoAutoCheckinCheck, YoyoLaunchForCheckinCheck, ReverseHoverCheck, FullscreenActiveOnlyCheck,
            UnchangedAutoHideCheck, ReplyFirstWhenExpandedUpCheck, AllowExpandedBeyondScreenCheck, AutoUpdateCheck
        };
        foreach (var checkBox in checkBoxes)
            if (checkBox.Tag is Geometry geometry)
                checkBox.Tag = ReferenceEquals(checkBox, AutoUpdateCheck)
                    ? ProviderIconService.FromFilledGeometry(geometry)
                    : ProviderIconService.FromGeometry(geometry);

        var yoyo = ProviderIconService.Load("yoyo", _island.CurrentSettings);
        var codex = ProviderIconService.Load("codex", _island.CurrentSettings);
        var workBuddy = ProviderIconService.Load("workbuddy", _island.CurrentSettings);
        if (yoyo is not null)
            foreach (var checkBox in new[] { YoyoCheck, YoyoCreditsCheck, YoyoAutoCheckinCheck, YoyoLaunchForCheckinCheck }) checkBox.Tag = yoyo;
        if (codex is not null)
            foreach (var checkBox in new[] { CodexCheck, CodexActivityCheck, CodexLimitsCheck, CodexResetReminderCheck }) checkBox.Tag = codex;
        if (workBuddy is not null)
            foreach (var checkBox in new[] { WorkBuddyCheck, WorkBuddyCreditsCheck, ConfirmationNotificationsCheck }) checkBox.Tag = workBuddy;
    }

    private void ProviderOrderList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _providerDragCandidate = null;
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not ProviderOrderItem item) return;
        _providerDragStart = e.GetPosition(ProviderOrderList);
        _providerDragCandidate = item;
        row.Focus();
    }

    private void ProviderOrderList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _providerDragCandidate is null) return;
        var current = e.GetPosition(ProviderOrderList);
        if (Math.Abs(current.X - _providerDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _providerDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _providerDragCandidate;
        _providerDragCandidate = null;
        item.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop(ProviderOrderList, new DataObject(ProviderOrderDragFormat, item.Key), DragDropEffects.Move);
        }
        finally
        {
            item.IsDragging = false;
            ClearProviderDropMarker();
        }
    }

    private void SettingsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SettingsScroll.ScrollToVerticalOffset(SettingsScroll.VerticalOffset - e.Delta * .5);
        e.Handled = true;
    }

    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateActiveNavigation();

    private void UpdateActiveNavigation()
    {
        if (!IsLoaded) return;
        var sections = new (System.Windows.Controls.RadioButton Nav, FrameworkElement Section)[]
        {
            (PositionNav, PositionCard), (AppearanceNav, AppearanceCard), (ComponentNav, ComponentCard),
            (FeatureNav, FeatureCard), (NotificationNav, NotificationCard), (PresetNav, PresetCard), (UpdateNav, UpdateCard)
        };
        var active = sections[0].Nav;
        foreach (var item in sections)
        {
            var top = item.Section.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0)).Y;
            if (top <= 72) active = item.Nav;
            else break;
        }
        if (SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 1) active = sections[^1].Nav;
        active.IsChecked = true;
    }

    private void ProviderOrderList_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ProviderOrderDragFormat))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        UpdateProviderDropMarker(ProviderInsertionIndex(e.GetPosition(ProviderOrderList)));
        e.Handled = true;
    }

    private void ProviderOrderList_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ProviderOrderDragFormat) is not string key) return;
        var item = ProviderOrderItems.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;

        var oldIndex = ProviderOrderItems.IndexOf(item);
        var insertionIndex = ProviderInsertionIndex(e.GetPosition(ProviderOrderList));
        if (insertionIndex > oldIndex) insertionIndex--;
        MoveProvider(oldIndex, Math.Clamp(insertionIndex, 0, ProviderOrderItems.Count - 1));
        ClearProviderDropMarker();
        e.Handled = true;
    }

    private void ProviderMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProviderOrderItem item })
            MoveProvider(ProviderOrderItems.IndexOf(item), ProviderOrderItems.IndexOf(item) - 1);
    }

    private void ProviderMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ProviderOrderItem item })
            MoveProvider(ProviderOrderItems.IndexOf(item), ProviderOrderItems.IndexOf(item) + 1);
    }

    private void ProviderOrderList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0 || key is not (Key.Up or Key.Down)) return;
        var row = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is not ProviderOrderItem item) return;
        var index = ProviderOrderItems.IndexOf(item);
        MoveProvider(index, index + (key == Key.Up ? -1 : 1));
        e.Handled = true;
    }

    private void MoveProvider(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0 || newIndex >= ProviderOrderItems.Count || oldIndex == newIndex) return;
        var moved = ProviderOrderItems[oldIndex];
        ProviderOrderItems.Move(oldIndex, newIndex);
        RefreshProviderOrderState();
        PersistProviderOrder();
        Dispatcher.BeginInvoke(() => AnimateProviderMove(moved, oldIndex < newIndex ? 12 : -12), DispatcherPriority.Loaded);
    }

    private void PersistProviderOrder()
    {
        if (_loading) return;
        var current = _island.CurrentSettings;
        var visible = new Queue<string>(ProviderOrderItems.Select(item => item.Key));
        var visibleKeys = visible.ToHashSet(StringComparer.OrdinalIgnoreCase);
        current.ProviderOrder = string.Join(',', ProviderCatalog.ParseOrder(ProviderCatalog.NormalizeOrder(current.ProviderOrder))
            .Select(key => visibleKeys.Contains(key) ? visible.Dequeue() : key));
        _island.ApplySettings(current);
    }

    private void RefreshProviderOrderState()
    {
        for (var index = 0; index < ProviderOrderItems.Count; index++)
        {
            var item = ProviderOrderItems[index];
            item.Position = index + 1;
            item.CanMoveUp = index > 0;
            item.CanMoveDown = index < ProviderOrderItems.Count - 1;
        }
    }

    private int ProviderInsertionIndex(System.Windows.Point pointer)
    {
        for (var index = 0; index < ProviderOrderItems.Count; index++)
        {
            if (ProviderOrderList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem row) continue;
            var top = row.TranslatePoint(new System.Windows.Point(0, 0), ProviderOrderList).Y;
            if (pointer.Y < top + row.ActualHeight / 2) return index;
        }
        return ProviderOrderItems.Count;
    }

    private void UpdateProviderDropMarker(int insertionIndex)
    {
        ClearProviderDropMarker();
        if (ProviderOrderItems.Count == 0) return;
        if (insertionIndex >= ProviderOrderItems.Count) ProviderOrderItems[^1].InsertAfter = true;
        else ProviderOrderItems[insertionIndex].InsertBefore = true;
    }

    private void ClearProviderDropMarker()
    {
        foreach (var item in ProviderOrderItems)
        {
            item.InsertBefore = false;
            item.InsertAfter = false;
        }
    }

    private void AnimateProviderMove(ProviderOrderItem item, double startY)
    {
        if (ProviderOrderList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row) return;
        var transform = new TranslateTransform(0, startY);
        row.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        row.Focus();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target) return target;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void ApplyPanelTheme()
    {
        var mode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        var light = mode == "light" || (mode == "system" && SystemUsesLightTheme());
        var background = Brush(light ? "#F5F7FB" : "#111620");
        var card = Brush(light ? "#FFFFFF" : "#1B2230");
        var primary = Brush(light ? "#182033" : "#F2F5FF");
        var secondary = Brush(light ? "#657087" : "#9AA5BC");
        Background = background; Foreground = primary;
        RightPane.Background = background;
        NavPane.Background = Brush(light ? "#E9EDF4" : "#171E2A");
        Resources["SettingsInputBackground"] = Brush(light ? "#F7F8FA" : "#242C3A");
        Resources["SettingsInputBorder"] = Brush(light ? "#E2E5EA" : "#3A4557");
        Resources["SettingsPopupBackground"] = Brush(light ? "#FFFFFF" : "#1B2230");
        Resources["SettingsTrackBackground"] = Brush(light ? "#E9EBEF" : "#354052");
        Resources["SettingsHoverBackground"] = Brush(light ? "#EEF1F3" : "#2A3444");
        TitleText.Foreground = primary; SubtitleText.Foreground = secondary;
        NavTitle.Foreground = primary; NavSubtitle.Foreground = secondary;
        PresetCard.Background = card; AppearanceCard.Background = card; ComponentCard.Background = card; FeatureCard.Background = card; NotificationCard.Background = card; UpdateCard.Background = card; PositionCard.Background = card;
        PreviewSurface.Background = Brush(light ? "#EEF1F7" : "#111620");
        PreviewIsland.Background = Brush(light ? "#F4FFFFFF" : "#EB0E121C");
        PreviewIsland.BorderBrush = Brush(light ? "#24182033" : "#1AFFFFFF");
        PositionPreviewSurface.Background = Brush(light ? "#EEF1F7" : "#111620");
        PositionPreviewSurface.BorderBrush = Brush(light ? "#263A4557" : "#4A596E");
        ApplyTitleBarTheme(light);
    }

    private void NavigationToggle_Click(object sender, RoutedEventArgs e)
    {
        var current = _island.CurrentSettings;
        current.SettingsNavigationCollapsed = !current.SettingsNavigationCollapsed;
        AppSettings.Save(current);
        TransitionNavigationLayout(true);
    }

    private void ApplyNavigationLayout()
        => TransitionNavigationLayout(false);

    private void TransitionNavigationLayout(bool animate)
    {
        var collapsed = _island.CurrentSettings.SettingsNavigationCollapsed;
        var version = ++_navigationAnimationVersion;
        var duration = TimeSpan.FromMilliseconds(280);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var labels = new List<FrameworkElement> { (FrameworkElement)NavTitle.Parent };
        var width = collapsed ? 82d : 200d;
        var fromWidth = (double)GetValue(NavigationWidthProperty);
        BeginAnimation(NavigationWidthProperty, null);
        SetValue(NavigationWidthProperty, width);
        NavPane.BeginAnimation(Border.PaddingProperty, null);
        NavPane.Padding = new Thickness(16, 24, 16, 24);
        if (animate)
        {
            BeginAnimation(NavigationWidthProperty, new DoubleAnimation(fromWidth, width, duration) { EasingFunction = easing });
        }
        NavigationBrand.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        NavigationBrand.Margin = new Thickness(4, 0, 0, 26);
        foreach (var nav in new[] { PositionNav, AppearanceNav, ComponentNav, FeatureNav, NotificationNav, PresetNav, UpdateNav })
        {
            var content = (StackPanel)nav.Content;
            labels.Add((FrameworkElement)content.Children[1]);
            ((FrameworkElement)content.Children[0]).Margin = new Thickness(0, 0, 11, 0);
            nav.Padding = new Thickness(10, 6, 10, 6);
        }
        foreach (var label in labels)
        {
            var fromOpacity = label.Visibility == Visibility.Visible ? label.Opacity : 0;
            label.BeginAnimation(OpacityProperty, null);
            label.Visibility = animate || !collapsed ? Visibility.Visible : Visibility.Hidden;
            label.Opacity = collapsed ? 0 : 1;
            if (!animate) continue;
            var fade = new DoubleAnimation(fromOpacity, label.Opacity, duration) { EasingFunction = easing };
            fade.Completed += (_, _) =>
            {
                if (version == _navigationAnimationVersion && collapsed) label.Visibility = Visibility.Hidden;
            };
            label.BeginAnimation(OpacityProperty, fade);
        }
        var rotation = NavigationToggleIcon.RenderTransform as RotateTransform ?? new RotateTransform();
        var fromAngle = rotation.Angle;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        rotation.Angle = collapsed ? 180 : 0;
        NavigationToggleIcon.RenderTransform = rotation;
        if (animate) rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(fromAngle, rotation.Angle, duration) { EasingFunction = easing });
        NavigationToggle.ToolTip = collapsed ? "展开目录" : "收起目录";
        System.Windows.Automation.AutomationProperties.SetName(NavigationToggle, (string)NavigationToggle.ToolTip);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target } || FindName(target) is not FrameworkElement section)
            return;

        BeginAnimation(AnimatedScrollOffsetProperty, null);
        AnimatedScrollOffset = SettingsScroll.VerticalOffset;
        var position = section.TransformToAncestor(SettingsScroll).Transform(new System.Windows.Point(0, 0));
        var destination = Math.Clamp(SettingsScroll.VerticalOffset + position.Y - 18, 0, SettingsScroll.ScrollableHeight);
        var scrollAnimation = new DoubleAnimation(SettingsScroll.VerticalOffset, destination, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        BeginAnimation(AnimatedScrollOffsetProperty, scrollAnimation, HandoffBehavior.SnapshotAndReplace);

        section.BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private double AnimatedScrollOffset
    {
        get => (double)GetValue(AnimatedScrollOffsetProperty);
        set => SetValue(AnimatedScrollOffsetProperty, value);
    }

    private static void OnAnimatedScrollOffsetChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is SettingsWindow window)
            window.SettingsScroll.ScrollToVerticalOffset((double)args.NewValue);
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

    private void ApplyTitleBarTheme()
    {
        var mode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        ApplyTitleBarTheme(mode == "light" || (mode == "system" && SystemUsesLightTheme()));
    }

    private void ApplyTitleBarTheme(bool light)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = light ? 0 : 1;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) < 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    public sealed class ProviderOrderItem : INotifyPropertyChanged
    {
        private int _position;
        private bool _canMoveUp;
        private bool _canMoveDown;
        private bool _isDragging;
        private bool _insertBefore;
        private bool _insertAfter;

        internal ProviderOrderItem(string key, string displayName, ImageSource iconImage)
        {
            Key = key;
            DisplayName = displayName;
            IconImage = iconImage;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public ImageSource IconImage { get; }
        public string PositionText => $"第 {Position} 位";
        public int Position { get => _position; set { if (_position == value) return; _position = value; Changed(nameof(Position)); Changed(nameof(PositionText)); } }
        public bool CanMoveUp { get => _canMoveUp; set { if (_canMoveUp == value) return; _canMoveUp = value; Changed(nameof(CanMoveUp)); } }
        public bool CanMoveDown { get => _canMoveDown; set { if (_canMoveDown == value) return; _canMoveDown = value; Changed(nameof(CanMoveDown)); } }
        public bool IsDragging { get => _isDragging; set { if (_isDragging == value) return; _isDragging = value; Changed(nameof(IsDragging)); } }
        public bool InsertBefore { get => _insertBefore; set { if (_insertBefore == value) return; _insertBefore = value; Changed(nameof(InsertBefore)); } }
        public bool InsertAfter { get => _insertAfter; set { if (_insertAfter == value) return; _insertAfter = value; Changed(nameof(InsertAfter)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
