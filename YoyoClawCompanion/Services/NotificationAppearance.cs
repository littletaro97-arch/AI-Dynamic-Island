namespace YoyoClawCompanion.Services;

// Shared by WPF notifications and the optional native Magic renderer.
internal static class NotificationAppearance
{
    internal static double MuteRadius(double cornerRadius, double rowHeight)
        => Math.Max(0, Math.Min(cornerRadius, rowHeight / 4));
}
