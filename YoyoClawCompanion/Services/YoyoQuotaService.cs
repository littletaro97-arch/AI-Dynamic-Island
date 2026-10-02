using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed class YoyoQuotaService
{
    public (double? Remaining, double? Total) Read()
    {
        var now = DateTimeOffset.UtcNow;
        var updated = DateTimeOffset.MinValue;
        (double? Remaining, double? Total) local = (null, null);
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hclaw", "billing", "quota.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            local = Parse(json.RootElement, now);
            if (json.RootElement.TryGetProperty("updated_at", out var timestamp))
                DateTimeOffset.TryParse(timestamp.GetString(), out updated);
        }
        catch { }
        // YOYO can refresh its in-memory subscription without updating quota.json.
        // Its own balance diagnostic is a read-only source, not an independent login session.
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Comms", "MagicClaw", "log", "MagicClaw.log");
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 256 * 1024), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return ParseBalanceLog(reader.ReadToEnd(), updated, now) ?? local;
        }
        catch { return local; }
    }

    internal static (double? Remaining, double? Total)? ParseBalanceLog(string text, DateTimeOffset newerThan, DateTimeOffset now)
    {
        (double? Remaining, double? Total)? latest = null;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text,
            @"(?m)^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[INFO \] \[Billing\] cloud subscription balance received (?<json>\{[^{}]{0,2048}\})",
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            if (!DateTimeOffset.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var at)
                || at <= newerThan || at > now) continue;
            try
            {
                using var json = JsonDocument.Parse(match.Groups["json"].Value);
                var root = json.RootElement;
                if (Number(root, "displayed_remaining_points") is { } remaining)
                {
                    latest = (remaining, Number(root, "model_total_points"));
                    newerThan = at;
                }
            }
            catch (JsonException) { }
        }
        return latest;
    }

    internal static (double? Remaining, double? Total) Parse(JsonElement root, DateTimeOffset now)
    {
        if (root.TryGetProperty("quota", out var quota) && quota.ValueKind == JsonValueKind.Object) root = quota;
        var fallback = (Number(root, "model_remaining_points"), Number(root, "model_total_points"));
        if (!root.TryGetProperty("quota_grants", out var grants) || grants.ValueKind != JsonValueKind.Array || grants.GetArrayLength() == 0)
            return fallback;

        double remaining = 0, total = 0;
        bool completeTotal = true;
        foreach (var grant in grants.EnumerateArray())
        {
            if (grant.ValueKind != JsonValueKind.Object) return fallback;
            var status = grant.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (!string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase)) continue;
            if (grant.TryGetProperty("window_expires_at", out var expiry)
                && expiry.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expiry.GetString(), out var expiresAt) && expiresAt <= now) continue;
            if (Number(grant, "remaining_points") is not { } balance) return fallback;
            remaining += balance;
            if (Number(grant, "total_points") is { } limit) total += limit;
            else completeTotal = false;
        }
        return (remaining, completeTotal ? total : null);
    }

    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        double number;
        var valid = value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number)
            : double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number);
        return valid && double.IsFinite(number) && number >= 0 ? number : null;
    }
}
