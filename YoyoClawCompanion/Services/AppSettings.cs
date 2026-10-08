using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed class IslandSettings
{
    internal const double MinimumTextSize = 12;
    public double? X { get; set; }
    public double? Y { get; set; }
    public double CornerRadius { get; set; } = 24;
    public double Opacity { get; set; } = 0.92;
    public double IslandWidth { get; set; } = 224;
    public double? ExpandedIslandWidth { get; set; }
    public double IslandHeight { get; set; } = 48;
    public bool Topmost { get; set; } = true;
    public bool ShowYoyo { get; set; } = true;
    public bool ShowCodex { get; set; } = true;
    public bool ShowWorkBuddy { get; set; } = true;
    public bool ShowDeepSeek { get; set; } = true;
    public string[] SuppressedOfflineProviders { get; set; } = [];
    public bool ShowShadow { get; set; } = true;
    public bool ShowClockWhenReady { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoCheckForUpdates { get; set; } = true;
    public bool SettingsNavigationCollapsed { get; set; }
    public Dictionary<string, string[]> SettingCardOrders { get; set; } = new();
    public string ThemeMode { get; set; } = "system";
    public bool EnableCodexActivityDetection { get; set; } = true;
    public bool ShowYoyoCredits { get; set; } = true;
    public bool ShowCodexLimits { get; set; } = true;
    public bool ShowWorkBuddyCredits { get; set; } = true;
    public bool EnableCodexResetReminder { get; set; } = true;
    public bool EnableYoyoAutoCheckin { get; set; }
    public bool LaunchYoyoForAutoCheckin { get; set; }
    public bool EnableAppLaunch { get; set; } = true;
    public bool EnableHoverExpansion { get; set; } = true;
    public bool EnableSpringAnimation { get; set; } = true;
    public bool EnableCompletionNotifications { get; set; } = true;
    public bool EnableConfirmationNotifications { get; set; } = true;
    public bool EnableCodexConfirmationNotifications { get; set; } = true;
    public bool EnableSystemNotifications { get; set; }
    public double SystemNotificationDisplaySeconds { get; set; } = 10;
    public NotificationTitleRule[] IgnoredNotificationTitles { get; set; } = [];
    public bool EnableReverseHover { get; set; }
    public bool EnableFullscreenActiveOnly { get; set; }
    public bool EnableUnchangedAutoHide { get; set; }
    public bool PutReplyFirstWhenExpandedUp { get; set; }
    public bool AllowExpandedBeyondScreen { get; set; } = true;
    public string PositionPreset { get; set; } = "custom";
    public string? PreferredDisplayId { get; set; }
    public DisplayPosition? PrimaryDisplayPosition { get; set; }
    public Dictionary<string, DisplayPosition> SecondaryDisplayPositions { get; set; } = new();
    public double UnchangedAutoHideMinutes { get; set; } = 5;
    public double HoverDelayMs { get; set; } = 70;
    public double QuotaScrollSpeed { get; set; } = 24;
    public double CompletionDisplaySeconds { get; set; } = 10;
    public double CodexResetReminderMinutes { get; set; } = 15;
    public int MaxResponseLines { get; set; } = 3;
    public double TextSize { get; set; } = MinimumTextSize;
    public string DisplayMode { get; set; } = "always";
    public string ProviderOrder { get; set; } = "yoyo,codex,workbuddy";
    public string? YoyoExecutablePath { get; set; }
    public string? CodexExecutablePath { get; set; }
    public string? WorkBuddyExecutablePath { get; set; }
    public string? DeepSeekExecutablePath { get; set; }

    internal void NormalizeInteraction()
    {
        HoverDelayMs = NormalizeHoverDelay(HoverDelayMs);
        TextSize = double.IsFinite(TextSize) ? Math.Clamp(TextSize, MinimumTextSize, 16) : MinimumTextSize;
    }

    internal static double NormalizeHoverDelay(double value)
        => double.IsFinite(value) ? Math.Clamp(Math.Round(value / 5, MidpointRounding.AwayFromZero) * 5, 20, 130) : 70;
}

internal static class AppSettings
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoyoClawCompanion");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly object SaveGate = new();
    private static long _saveVersion;
    private static long _writtenVersion;

    public static IslandSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<IslandSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new IslandSettings();
            settings.NormalizeInteraction();
            return settings;
        }
        catch { return new IslandSettings(); }
    }

    public static void Save(IslandSettings settings)
    {
        try
        {
            var snapshot = JsonSerializer.Serialize(settings, JsonOptions);
            Write(snapshot, Interlocked.Increment(ref _saveVersion));
        }
        catch { }
    }

    // Capture a stable snapshot before dispatching the file write; never enumerate live UI settings on a worker.
    public static Task SaveAsync(IslandSettings settings)
    {
        try
        {
            var snapshot = JsonSerializer.Serialize(settings, JsonOptions);
            var version = Interlocked.Increment(ref _saveVersion);
            return Task.Run(() => Write(snapshot, version));
        }
        catch { return Task.CompletedTask; }
    }

    private static void Write(string snapshot, long version)
    {
        lock (SaveGate)
        {
            if (version < _writtenVersion) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, snapshot);
                File.Move(temp, FilePath, true);
                _writtenVersion = version;
            }
            catch { }
        }
    }
}

internal sealed class SettingsPresetSlot
{
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }
    public IslandSettings Settings { get; set; } = new();
}

internal static class SettingsPresetStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoyoClawCompanion");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "presets.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<SettingsPresetSlot> Load()
    {
        try
        {
            return (JsonSerializer.Deserialize<List<SettingsPresetSlot>>(File.ReadAllText(FilePath), JsonOptions) ?? [])
                .Where(item => item.Slot is >= 1 and <= 3)
                .GroupBy(item => item.Slot)
                .Select(group => group.OrderByDescending(item => item.SavedAt).First())
                .OrderBy(item => item.Slot)
                .ToArray();
        }
        catch { return []; }
    }

    public static SettingsPresetSlot Save(int slot, IslandSettings current)
    {
        if (slot is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        var loaded = Load();
        var existing = loaded.FirstOrDefault(item => item.Slot == slot);
        var presets = loaded.Where(item => item.Slot != slot).ToList();
        var saved = new SettingsPresetSlot { Slot = slot, Name = NormalizeName(existing?.Name, slot), SavedAt = DateTimeOffset.Now, Settings = Snapshot(current) };
        presets.Add(saved);
        Write(presets.OrderBy(item => item.Slot));
        return saved;
    }

    public static void Rename(int slot, string? name)
    {
        if (slot is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        var presets = Load().ToList();
        var preset = presets.FirstOrDefault(item => item.Slot == slot);
        if (preset is null) return;
        preset.Name = NormalizeName(name, slot);
        Write(presets.OrderBy(item => item.Slot));
    }

    public static string DisplayName(SettingsPresetSlot preset) => NormalizeName(preset.Name, preset.Slot);

    public static IslandSettings Apply(SettingsPresetSlot preset, IslandSettings current)
    {
        var applied = Clone(preset.Settings);
        // Installation discovery belongs to the current machine, not to a visual/behaviour preset.
        applied.YoyoExecutablePath = current.YoyoExecutablePath;
        applied.CodexExecutablePath = current.CodexExecutablePath;
        applied.WorkBuddyExecutablePath = current.WorkBuddyExecutablePath;
        applied.DeepSeekExecutablePath = current.DeepSeekExecutablePath;
        applied.SuppressedOfflineProviders = current.SuppressedOfflineProviders;
        applied.IgnoredNotificationTitles = current.IgnoredNotificationTitles;
        applied.SettingsNavigationCollapsed = current.SettingsNavigationCollapsed;
        applied.SettingCardOrders = current.SettingCardOrders;
        applied.NormalizeInteraction();
        return applied;
    }

    private static IslandSettings Snapshot(IslandSettings current)
    {
        var snapshot = Clone(current);
        snapshot.YoyoExecutablePath = null;
        snapshot.CodexExecutablePath = null;
        snapshot.WorkBuddyExecutablePath = null;
        snapshot.DeepSeekExecutablePath = null;
        snapshot.SuppressedOfflineProviders = [];
        snapshot.IgnoredNotificationTitles = [];
        return snapshot;
    }

    private static IslandSettings Clone(IslandSettings value)
        => JsonSerializer.Deserialize<IslandSettings>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions) ?? new IslandSettings();

    private static string NormalizeName(string? value, int slot)
    {
        var normalized = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized)) return $"方案 {slot}";
        return normalized.Length <= 60 ? normalized : normalized[..60];
    }

    private static void Write(IEnumerable<SettingsPresetSlot> presets)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(presets.Take(3), JsonOptions));
        File.Move(temp, FilePath, true);
    }
}
