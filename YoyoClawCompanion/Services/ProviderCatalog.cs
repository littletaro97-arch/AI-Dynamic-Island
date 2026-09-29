namespace YoyoClawCompanion.Services;

internal static class ProviderCatalog
{
    internal const int MaximumOrderItems = 8;
    internal static readonly string[] KnownKeys = ["yoyo", "codex", "workbuddy"];

    internal static string NormalizeOrder(string? value)
    {
        var providers = ParseOrder(value).ToList();
        if (providers.Count == 0) providers.AddRange(KnownKeys);

        foreach (var known in KnownKeys)
        {
            if (providers.Count >= MaximumOrderItems) break;
            if (!providers.Contains(known, StringComparer.OrdinalIgnoreCase)) providers.Add(known);
        }

        return string.Join(',', providers);
    }

    internal static IReadOnlyList<string> ParseOrder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = raw.ToLowerInvariant();
            if (!IsValidKey(key) || !seen.Add(key)) continue;
            result.Add(key);
            if (result.Count == MaximumOrderItems) break;
        }
        return result;
    }

    internal static bool IsKnown(string key)
        => KnownKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    internal static string DisplayName(string key) => key.ToLowerInvariant() switch
    {
        "yoyo" => "YOYO Claw",
        "codex" => "Codex",
        "workbuddy" => "WorkBuddy",
        _ => key
    };

    private static bool IsValidKey(string key)
    {
        if (key.Length is < 1 or > 32 || !char.IsAsciiLetterOrDigit(key[0])) return false;
        return key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
