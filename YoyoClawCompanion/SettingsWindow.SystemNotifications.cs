using System.Windows;
using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion;

public partial class SettingsWindow
{
    internal const string NotificationSettingsUri = "ms-settings:notifications";

    private void NotificationFilters_Changed(object? sender, EventArgs args)
    {
        RefreshNotificationFilters();
        NotificationFiltersButton.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void RefreshNotificationFilters()
        => NotificationFiltersButton.Content = $"已屏蔽通知（{(_island.CurrentSettings.IgnoredNotificationTitles ?? []).Length}）";

    private void NotificationFilters_Click(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu { PlacementTarget = NotificationFiltersButton, Placement = PlacementMode.Bottom,
            Style = (Style)FindResource("SettingsContextMenu"), FontFamily = FontFamily, FontSize = FontSize, Language = Language };
        menu.Resources.MergedDictionaries.Add(Resources);
        foreach (var rule in _island.CurrentSettings.IgnoredNotificationTitles ?? [])
        {
            if (rule is null) continue;
            var item = new MenuItem { Header = new TextBlock { Text = $"恢复：{rule.Source} · {rule.Title}", TextTrimming = TextTrimming.CharacterEllipsis,
                    FontFamily = FontFamily, FontSize = FontSize, Language = Language },
                Tag = rule, ToolTip = $"{rule.Source} · {rule.Title}", Style = (Style)FindResource("SettingsContextMenuItem") };
            item.Click += async (_, _) => await _island.RestoreNotificationTitleAsync(rule);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "暂无屏蔽的通知标题", IsEnabled = false, Style = (Style)FindResource("SettingsContextMenuItem") });
        menu.Opened += (_, _) => menu.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(140)));
        NotificationFiltersButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void NotificationBannerSettings_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(NotificationSettingsUri) { UseShellExecute = true });
            NotificationBannerGuidance.Text = "在 Windows 中开启“请勿打扰”，保留“通知”总开关。优先通知可能仍显示弹窗。";
        }
        catch
        {
            NotificationBannerGuidance.Text = "无法打开系统设置，请手动前往 Windows 设置 → 系统 → 通知，开启“请勿打扰”。";
        }
        NotificationBannerGuidance.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void SystemNotificationStatus_Changed(object? sender, EventArgs args)
        => Dispatcher.BeginInvoke(() => SystemNotificationStatusText.Text = _island.SystemNotifications.Status);

    private async void NotificationAccess_Click(object sender, RoutedEventArgs args)
    {
        if (SystemNotificationsCheck.IsChecked != true) SystemNotificationsCheck.IsChecked = true;
        else await _island.ConfigureSystemNotificationsAsync(true);
    }
}
