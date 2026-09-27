using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using YoyoClawCompanion.Services;

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
        ShadowCheck.IsChecked = value.ShowShadow;
        TopmostCheck.IsChecked = value.Topmost;
        QuotaCheck.IsChecked = value.ShowQuota;
        YoyoCheck.IsChecked = value.ShowYoyo;
        CodexCheck.IsChecked = value.ShowCodex;
        WorkBuddyCheck.IsChecked = value.ShowWorkBuddy;
        foreach (ComboBoxItem item in ThemeCombo.Items) if (string.Equals(item.Tag?.ToString(), value.ThemeMode, StringComparison.OrdinalIgnoreCase)) ThemeCombo.SelectedItem = item;
        if (ThemeCombo.SelectedIndex < 0) ThemeCombo.SelectedIndex = 0;
        CodexActivityCheck.IsChecked = value.EnableCodexActivityDetection;
        CodexLimitsCheck.IsChecked = value.ShowCodexLimits;
        WorkBuddyCreditsCheck.IsChecked = value.ShowWorkBuddyCredits;
        AppLaunchCheck.IsChecked = value.EnableAppLaunch;
        HoverExpansionCheck.IsChecked = value.EnableHoverExpansion;
        SpringAnimationCheck.IsChecked = value.EnableSpringAnimation;
        HoverDelaySlider.Value = value.HoverDelayMs;
    }

    private void Setting_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var current = _island.CurrentSettings;
        current.CornerRadius = CornerSlider.Value;
        current.Opacity = OpacitySlider.Value / 100;
        current.IslandWidth = WidthSlider.Value;
        current.ShowShadow = ShadowCheck.IsChecked == true;
        current.Topmost = TopmostCheck.IsChecked == true;
        current.ShowQuota = QuotaCheck.IsChecked == true;
        current.ShowYoyo = YoyoCheck.IsChecked == true;
        current.ShowCodex = CodexCheck.IsChecked == true;
        current.ShowWorkBuddy = WorkBuddyCheck.IsChecked == true;
        current.ThemeMode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        current.EnableCodexActivityDetection = CodexActivityCheck.IsChecked == true;
        current.ShowCodexLimits = CodexLimitsCheck.IsChecked == true;
        current.ShowWorkBuddyCredits = WorkBuddyCreditsCheck.IsChecked == true;
        current.EnableAppLaunch = AppLaunchCheck.IsChecked == true;
        current.EnableHoverExpansion = HoverExpansionCheck.IsChecked == true;
        current.EnableSpringAnimation = SpringAnimationCheck.IsChecked == true;
        current.HoverDelayMs = HoverDelaySlider.Value;
        _island.ApplySettings(current);
        UpdateLabels();
        ApplyPanelTheme();
    }

    private void UpdateLabels()
    {
        CornerValue.Text = $"{CornerSlider.Value:0} px";
        OpacityValue.Text = $"{OpacitySlider.Value:0}%";
        WidthValue.Text = $"{WidthSlider.Value:0} px";
        HoverDelayValue.Text = $"{HoverDelaySlider.Value:0} ms";
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => _island.ResetPosition();

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var current = _island.CurrentSettings;
        current.CornerRadius = 24; current.Opacity = .92; current.IslandWidth = 224;
        current.ShowShadow = true; current.Topmost = true; current.ShowQuota = true;
        current.ShowYoyo = true; current.ShowCodex = true; current.ShowWorkBuddy = true;
        current.ThemeMode = "system"; current.EnableCodexActivityDetection = true;
        current.ShowCodexLimits = true; current.ShowWorkBuddyCredits = true; current.EnableAppLaunch = true;
        current.EnableHoverExpansion = true; current.EnableSpringAnimation = true; current.HoverDelayMs = 70;
        LoadValues(current);
        _loading = false;
        _island.ApplySettings(current);
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
        TitleText.Foreground = primary; SubtitleText.Foreground = secondary;
        AppearanceCard.Background = card; ComponentCard.Background = card; FeatureCard.Background = card; PositionCard.Background = card;
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
