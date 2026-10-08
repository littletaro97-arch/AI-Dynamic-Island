using System.Text.Json;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (args.Contains("--identities") && access == UserNotificationListenerAccessStatus.Allowed)
            {
                var notifications = listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(notifications.Select(n => new { id = n.AppInfo.AppUserModelId, source = n.AppInfo.DisplayInfo.DisplayName }).Distinct()));
                return; // Application identities only: never print notification bodies.
            }
            var count = access == UserNotificationListenerAccessStatus.Allowed
                ? listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().GetAwaiter().GetResult().Count : -1;
            string eventStatus;
            void Changed(UserNotificationListener sender, UserNotificationChangedEventArgs args) { }
            try { listener.NotificationChanged += Changed; listener.NotificationChanged -= Changed; eventStatus = "available"; }
            catch (Exception e) { eventStatus = $"{e.GetType().Name}:0x{e.HResult:X8}"; }
            Console.WriteLine(JsonSerializer.Serialize(new { access = access.ToString(), count, eventStatus, note = "Read-only probe: no permission request, no notification mutation" }));
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { error = error.GetType().Name, hresult = $"0x{error.HResult:X8}", error.Message }));
        }
    }
}
