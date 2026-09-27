using System.IO;

namespace YoyoClawCompanion.Services;

internal static class ProductPaths
{
    public static string WorkBuddyConfigDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("WORKBUDDY_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy");
        }
    }

    public static string CodexHome
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }
    }
}
