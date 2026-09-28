using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed class IslandSettings
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double CornerRadius { get; set; } = 24;
    public double Opacity { get; set; } = 0.92;
    public double IslandWidth { get; set; } = 224;
    public double IslandHeight { get; set; } = 48;
    public bool Topmost { get; set; } = true;
    public bool ShowYoyo { get; set; } = true;
    public bool ShowCodex { get; set; } = true;
    public bool ShowWorkBuddy { get; set; } = true;
    public bool ShowShadow { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string ThemeMode { get; set; } = "system";
    public bool EnableCodexActivityDetection { get; set; } = true;
    public bool ShowCodexLimits { get; set; } = true;
    public bool ShowWorkBuddyCredits { get; set; } = true;
    public bool EnableAppLaunch { get; set; } = true;
    public bool EnableHoverExpansion { get; set; } = true;
    public bool EnableSpringAnimation { get; set; } = true;
    public bool EnableCompletionNotifications { get; set; } = true;
    public bool EnableConfirmationNotifications { get; set; } = true;
    public bool EnableReverseHover { get; set; }
    public bool EnableFullscreenActiveOnly { get; set; }
    public bool EnableUnchangedAutoHide { get; set; }
    public bool PutReplyFirstWhenExpandedUp { get; set; }
    public bool AllowExpandedBeyondScreen { get; set; } = true;
    public string PositionPreset { get; set; } = "custom";
    public double UnchangedAutoHideMinutes { get; set; } = 5;
    public double HoverDelayMs { get; set; } = 70;
    public double QuotaScrollSpeed { get; set; } = 24;
    public double CompletionDisplaySeconds { get; set; } = 10;
    public int MaxResponseLines { get; set; } = 3;
    public double TextSize { get; set; } = 11;
    public string DisplayMode { get; set; } = "always";
    public string ProviderOrder { get; set; } = "yoyo,codex,workbuddy";
    public string? YoyoExecutablePath { get; set; }
    public string? CodexExecutablePath { get; set; }
    public string? WorkBuddyExecutablePath { get; set; }
}

internal static class AppSettings
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoyoClawCompanion");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static IslandSettings Load()
    {
        try { return JsonSerializer.Deserialize<IslandSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new IslandSettings(); }
        catch { return new IslandSettings(); }
    }

    public static void Save(IslandSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, true);
        }
        catch { }
    }
}
