using System.Windows;

namespace YoyoClawCompanion;

public partial class SettingsWindow
{
    private void SystemNotificationStatus_Changed(object? sender, EventArgs args)
        => Dispatcher.BeginInvoke(() => SystemNotificationStatusText.Text = _island.SystemNotifications.Status);

    private async void NotificationAccess_Click(object sender, RoutedEventArgs args)
    {
        if (SystemNotificationsCheck.IsChecked != true) SystemNotificationsCheck.IsChecked = true;
        else await _island.ConfigureSystemNotificationsAsync(true);
    }
}
