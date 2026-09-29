using System.Collections.ObjectModel;
using System.ComponentModel;
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
using DataObject = System.Windows.DataObject;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace YoyoClawCompanion;

public partial class SettingsWindow : Window
{
    private const string ProviderOrderDragFormat = "AI.DynamicIsland.ProviderOrder";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private static readonly DependencyProperty AnimatedScrollOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedScrollOffset), typeof(double), typeof(SettingsWindow),
        new PropertyMetadata(0d, OnAnimatedScrollOffsetChanged));

    private readonly MainWindow _island;
    private bool _loading = true;
    private System.Windows.Point _providerDragStart;
    private ProviderOrderItem? _providerDragCandidate;
    private IReadOnlyList<SettingsPresetSlot> _presets = [];

    public ObservableCollection<ProviderOrderItem> ProviderOrderItems { get; } = [];

    internal SettingsWindow(MainWindow island)
    {
        _island = island;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        _island.PositionChanged += Island_PositionChanged;
        Closed += (_, _) => _island.PositionChanged -= Island_PositionChanged;
        LoadValues(island.CurrentSettings);
        _loading = false;
        UpdateLabels();
        UpdateDependencyStates();
        RefreshPresetCards();
        ApplyPanelTheme();
    }

    private void LoadValues(IslandSettings value)
    {
        CornerSlider.Value = value.CornerRadius;
        OpacitySlider.Value = value.Opacity * 100;
        WidthSlider.Value = value.IslandWidth;
        HeightSlider.Value = value.IslandHeight;
        TextSizeSlider.Value = value.TextSize;
        ShadowCheck.IsChecked = value.ShowShadow;
        TopmostCheck.IsChecked = value.Topmost;
        YoyoCheck.IsChecked = value.ShowYoyo;
        CodexCheck.IsChecked = value.ShowCodex;
        WorkBuddyCheck.IsChecked = value.ShowWorkBuddy;
        TrayIconCheck.IsChecked = value.ShowTrayIcon;
        StartupCheck.IsChecked = value.StartWithWindows;
        foreach (ComboBoxItem item in ThemeCombo.Items) if (string.Equals(item.Tag?.ToString(), value.ThemeMode, StringComparison.OrdinalIgnoreCase)) ThemeCombo.SelectedItem = item;
        if (ThemeCombo.SelectedIndex < 0) ThemeCombo.SelectedIndex = 0;
        CodexActivityCheck.IsChecked = value.EnableCodexActivityDetection;
        CodexLimitsCheck.IsChecked = value.ShowCodexLimits;
        WorkBuddyCreditsCheck.IsChecked = value.ShowWorkBuddyCredits;
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
        var current = _island.CurrentSettings;
        current.CornerRadius = CornerSlider.Value;
        current.Opacity = OpacitySlider.Value / 100;
        current.IslandWidth = WidthSlider.Value;
        current.IslandHeight = HeightSlider.Value;
        current.TextSize = TextSizeSlider.Value;
        current.ShowShadow = ShadowCheck.IsChecked == true;
        current.Topmost = TopmostCheck.IsChecked == true;
        current.ShowYoyo = YoyoCheck.IsChecked == true;
        current.ShowCodex = CodexCheck.IsChecked == true;
        current.ShowWorkBuddy = WorkBuddyCheck.IsChecked == true;
        current.ShowTrayIcon = TrayIconCheck.IsChecked == true;
        current.StartWithWindows = StartupCheck.IsChecked == true;
        current.ThemeMode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        current.EnableCodexActivityDetection = CodexActivityCheck.IsChecked == true;
        current.ShowCodexLimits = CodexLimitsCheck.IsChecked == true;
        current.ShowWorkBuddyCredits = WorkBuddyCreditsCheck.IsChecked == true;
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
        current.MaxResponseLines = int.TryParse((MaxResponseLinesCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var lines) ? lines : 3;
        current.DisplayMode = (DisplayModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "always";
        var refreshStatus = ReferenceEquals(sender, CodexActivityCheck)
            || ReferenceEquals(sender, CodexLimitsCheck)
            || ReferenceEquals(sender, WorkBuddyCreditsCheck)
            || ReferenceEquals(sender, ConfirmationNotificationsCheck);
        _island.ApplySettings(current, refreshStatus: refreshStatus, preserveMarquee: ReferenceEquals(sender, QuotaScrollSpeedSlider));
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
            status.Text = preset is null ? "尚未保存" : $"已保存 {preset.SavedAt.LocalDateTime:MM-dd HH:mm}";
            apply.IsEnabled = preset is not null;
        }
    }

    private void UpdateLabels()
    {
        CornerValue.Text = $"{CornerSlider.Value:0} px";
        OpacityValue.Text = $"{OpacitySlider.Value:0}%";
        WidthValue.Text = $"{WidthSlider.Value:0} px";
        HeightValue.Text = $"{HeightSlider.Value:0} px";
        TextSizeValue.Text = $"{TextSizeSlider.Value:0.#} px";
        HoverDelayValue.Text = $"{HoverDelaySlider.Value:0} ms";
        QuotaScrollSpeedValue.Text = $"{QuotaScrollSpeedSlider.Value:0} px/s";
        CompletionDisplayValue.Text = $"{CompletionDisplaySlider.Value:0} 秒";
        UnchangedAutoHideValue.Text = $"{UnchangedAutoHideSlider.Value:0} 分钟";
        PreviewIsland.Width = Math.Min(400, Math.Max(190, WidthSlider.Value));
        PreviewIsland.Height = Math.Min(72, Math.Max(32, HeightSlider.Value));
        PreviewIsland.CornerRadius = new CornerRadius(Math.Min(CornerSlider.Value, PreviewIsland.Height / 2));
        PreviewIsland.Opacity = OpacitySlider.Value / 100;
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
        ProviderOrderItems.Clear();
        foreach (var key in ProviderCatalog.ParseOrder(ProviderCatalog.NormalizeOrder(storedOrder)))
            ProviderOrderItems.Add(new ProviderOrderItem(key, ProviderCatalog.DisplayName(key), ProviderIcon(key)));
        RefreshProviderOrderState();
    }

    private Geometry ProviderIcon(string key)
        => (Geometry)FindResource(key switch
        {
            "yoyo" => "IconCircle",
            "codex" => "IconChart",
            "workbuddy" => "IconCoin",
            _ => "IconCircle"
        });

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

    private void ProviderOrderList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SettingsScroll.ScrollToVerticalOffset(SettingsScroll.VerticalOffset - e.Delta);
        e.Handled = true;
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
        current.ProviderOrder = string.Join(',', ProviderOrderItems.Select(item => item.Key));
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
        PresetCard.Background = card; AppearanceCard.Background = card; ComponentCard.Background = card; FeatureCard.Background = card; NotificationCard.Background = card; PositionCard.Background = card;
        PreviewSurface.Background = Brush(light ? "#EEF1F7" : "#111620");
        PreviewIsland.Background = Brush(light ? "#F4FFFFFF" : "#EB0E121C");
        PreviewIsland.BorderBrush = Brush(light ? "#24182033" : "#1AFFFFFF");
        PositionPreviewSurface.Background = Brush(light ? "#EEF1F7" : "#111620");
        PositionPreviewSurface.BorderBrush = Brush(light ? "#263A4557" : "#4A596E");
        ApplyTitleBarTheme(light);
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

        internal ProviderOrderItem(string key, string displayName, Geometry iconData)
        {
            Key = key;
            DisplayName = displayName;
            IconData = iconData;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public Geometry IconData { get; }
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
