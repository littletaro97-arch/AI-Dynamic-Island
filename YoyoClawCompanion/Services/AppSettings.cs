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
    public bool Topmost { get; set; } = true;
    public bool ShowQuota { get; set; } = true;
    public bool ShowYoyo { get; set; } = true;
    public bool ShowCodex { get; set; } = true;
    public bool ShowWorkBuddy { get; set; } = true;
    public bool ShowShadow { get; set; } = true;
    public string ThemeMode { get; set; } = "system";
    public bool EnableCodexActivityDetection { get; set; } = true;
    public bool ShowCodexLimits { get; set; } = true;
    public bool ShowWorkBuddyCredits { get; set; } = true;
    public bool EnableAppLaunch { get; set; } = true;
    public bool EnableHoverExpansion { get; set; } = true;
    public bool EnableSpringAnimation { get; set; } = true;
    public double HoverDelayMs { get; set; } = 70;
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
