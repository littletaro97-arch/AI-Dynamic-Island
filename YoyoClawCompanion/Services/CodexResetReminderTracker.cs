using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record CodexResetReminder(string Kind, DateTimeOffset At, int? Remaining);

// One remembered reset per quota window, independent of preferences/cache resets.
// All decisions are in memory; the caller performs load/save on a worker only.
internal sealed class CodexResetReminderTracker
{
    internal DateTimeOffset? FiveHourResetAt { get; set; }
    internal DateTimeOffset? WeeklyResetAt { get; set; }
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(2);

    internal CodexResetReminder? Take(CodexStatus status, DateTimeOffset now, TimeSpan lead)
    {
        var candidates = new[]
        {
            new CodexResetReminder("5 小时额度", status.FiveHourResetsAt ?? DateTimeOffset.MinValue, status.FiveHourRemainingPercent),
            new CodexResetReminder("周额度", status.WeeklyResetsAt ?? DateTimeOffset.MinValue, status.WeeklyRemainingPercent)
        };
        foreach (var candidate in candidates.OrderBy(c => c.At))
        {
            if (candidate.At <= now || candidate.At - now > lead) continue;
            var previous = candidate.Kind == "5 小时额度" ? FiveHourResetAt : WeeklyResetAt;
            // A second-level timestamp correction is the same reset, even across
            // timezone representations or a process restart. While a notified
            // reset is still pending, a reschedule must not create another alert.
            if (previous is { } at && ((candidate.At - at).Duration() <= TimestampTolerance || now < at)) continue;
            if (candidate.Kind == "5 小时额度") FiveHourResetAt = candidate.At;
            else WeeklyResetAt = candidate.At;
            return candidate;
        }
        return null;
    }

    private sealed record State(DateTimeOffset? FiveHourResetAt, DateTimeOffset? WeeklyResetAt);
    internal static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion", "codex-reset-reminders.json");

    internal static CodexResetReminderTracker Load(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            return new() { FiveHourResetAt = state?.FiveHourResetAt, WeeklyResetAt = state?.WeeklyResetAt };
        }
        catch { return new(); }
    }

    internal void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new State(FiveHourResetAt, WeeklyResetAt)));
            File.Move(temporary, path, true);
        }
        catch { /* A storage failure must not interrupt monitoring. */ }
    }
}
