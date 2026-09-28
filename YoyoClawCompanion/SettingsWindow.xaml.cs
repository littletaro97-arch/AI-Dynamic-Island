using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using YoyoClawCompanion.Services;
using Brush = System.Windows.Media.Brush;

namespace YoyoClawCompanion;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _island;
    private bool _loading = true;

    internal SettingsWindow(MainWindow island)
    {
        _island = island;
        InitializeComponent();
        LoadValues(island.CurrentSettings);
        _loading = false;
        UpdateLabels();
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
        QuotaCheck.IsChecked = value.ShowQuota;
        YoyoCheck.IsChecked = value.ShowYoyo;
        CodexCheck.IsChecked = value.ShowCodex;
        WorkBuddyCheck.IsChecked = value.ShowWorkBuddy;
        TrayIconCheck.IsChecked = value.ShowTrayIcon;
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
        UnchangedAutoHideSlider.Value = value.UnchangedAutoHideMinutes;
        HoverDelaySlider.Value = value.HoverDelayMs;
        QuotaScrollSpeedSlider.Value = value.QuotaScrollSpeed;
        CompletionDisplaySlider.Value = value.CompletionDisplaySeconds;
        foreach (ComboBoxItem item in MaxResponseLinesCombo.Items) if (item.Tag?.ToString() == value.MaxResponseLines.ToString()) MaxResponseLinesCombo.SelectedItem = item;
        if (MaxResponseLinesCombo.SelectedIndex < 0) MaxResponseLinesCombo.SelectedIndex = 2;
        foreach (ComboBoxItem item in DisplayModeCombo.Items) if (string.Equals(item.Tag?.ToString(), value.DisplayMode, StringComparison.OrdinalIgnoreCase)) DisplayModeCombo.SelectedItem = item;
        if (DisplayModeCombo.SelectedIndex < 0) DisplayModeCombo.SelectedIndex = 0;
        foreach (ComboBoxItem item in ProviderOrderCombo.Items) if (string.Equals(item.Tag?.ToString(), value.ProviderOrder, StringComparison.OrdinalIgnoreCase)) ProviderOrderCombo.SelectedItem = item;
        if (ProviderOrderCombo.SelectedIndex < 0) ProviderOrderCombo.SelectedIndex = 0;
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
        current.ShowQuota = QuotaCheck.IsChecked == true;
        current.ShowYoyo = YoyoCheck.IsChecked == true;
        current.ShowCodex = CodexCheck.IsChecked == true;
        current.ShowWorkBuddy = WorkBuddyCheck.IsChecked == true;
        current.ShowTrayIcon = TrayIconCheck.IsChecked == true;
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
        current.UnchangedAutoHideMinutes = UnchangedAutoHideSlider.Value;
        current.HoverDelayMs = HoverDelaySlider.Value;
        current.QuotaScrollSpeed = QuotaScrollSpeedSlider.Value;
        current.CompletionDisplaySeconds = CompletionDisplaySlider.Value;
        current.MaxResponseLines = int.TryParse((MaxResponseLinesCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var lines) ? lines : 3;
        current.DisplayMode = (DisplayModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "always";
        current.ProviderOrder = (ProviderOrderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "yoyo,codex,workbuddy";
        var refreshStatus = ReferenceEquals(sender, CodexActivityCheck)
            || ReferenceEquals(sender, CodexLimitsCheck)
            || ReferenceEquals(sender, WorkBuddyCreditsCheck)
            || ReferenceEquals(sender, ConfirmationNotificationsCheck)
            || ReferenceEquals(sender, ProviderOrderCombo);
        _island.ApplySettings(current, refreshStatus: refreshStatus, preserveMarquee: ReferenceEquals(sender, QuotaScrollSpeedSlider));
        UpdateLabels();
        ApplyPanelTheme();
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
        UnchangedAutoHideSlider.IsEnabled = UnchangedAutoHideCheck.IsChecked == true;
        PreviewIsland.Width = Math.Min(330, Math.Max(190, WidthSlider.Value));
        PreviewIsland.Height = Math.Min(72, Math.Max(32, HeightSlider.Value));
        PreviewIsland.CornerRadius = new CornerRadius(Math.Min(CornerSlider.Value, PreviewIsland.Height / 2));
        PreviewIsland.Opacity = OpacitySlider.Value / 100;
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => _island.ResetPosition();

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var current = _island.CurrentSettings;
        current.CornerRadius = 24; current.Opacity = .92; current.IslandWidth = 224; current.IslandHeight = 48;
        current.TextSize = 11; current.MaxResponseLines = 3;
        current.ShowShadow = true; current.Topmost = true; current.ShowQuota = true;
        current.ShowYoyo = true; current.ShowCodex = true; current.ShowWorkBuddy = true; current.ShowTrayIcon = true;
        current.ThemeMode = "system"; current.EnableCodexActivityDetection = true;
        current.ShowCodexLimits = true; current.ShowWorkBuddyCredits = true; current.EnableAppLaunch = true;
        current.EnableHoverExpansion = true; current.EnableSpringAnimation = true; current.HoverDelayMs = 70;
        current.EnableCompletionNotifications = true;
        current.EnableConfirmationNotifications = true;
        current.EnableReverseHover = false;
        current.EnableFullscreenActiveOnly = false;
        current.EnableUnchangedAutoHide = false; current.UnchangedAutoHideMinutes = 5;
        current.QuotaScrollSpeed = 24; current.CompletionDisplaySeconds = 10; current.DisplayMode = "always";
        current.ProviderOrder = "yoyo,codex,workbuddy";
        LoadValues(current);
        _loading = false;
        _island.ApplySettings(current, refreshStatus: true);
        UpdateLabels();
        ApplyPanelTheme();
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
        TitleText.Foreground = primary; SubtitleText.Foreground = secondary;
        NavTitle.Foreground = primary; NavSubtitle.Foreground = secondary;
        AppearanceCard.Background = card; ComponentCard.Background = card; FeatureCard.Background = card; NotificationCard.Background = card; PositionCard.Background = card;
        PreviewSurface.Background = Brush(light ? "#EEF1F7" : "#111620");
        PreviewIsland.Background = Brush(light ? "#F4FFFFFF" : "#EB0E121C");
        PreviewIsland.BorderBrush = Brush(light ? "#24182033" : "#1AFFFFFF");
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string target } && FindName(target) is FrameworkElement section)
            section.BringIntoView();
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

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
