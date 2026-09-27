using System.Windows;
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
        _island.ApplySettings(current);
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        CornerValue.Text = $"{CornerSlider.Value:0} px";
        OpacityValue.Text = $"{OpacitySlider.Value:0}%";
        WidthValue.Text = $"{WidthSlider.Value:0} px";
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => _island.ResetPosition();

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        var current = _island.CurrentSettings;
        current.CornerRadius = 24; current.Opacity = .92; current.IslandWidth = 224;
        current.ShowShadow = true; current.Topmost = true; current.ShowQuota = true;
        current.ShowYoyo = true; current.ShowCodex = true; current.ShowWorkBuddy = true;
        LoadValues(current);
        _loading = false;
        _island.ApplySettings(current);
        UpdateLabels();
    }
}
