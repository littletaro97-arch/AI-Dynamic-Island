using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record CodexStatus(
    bool IsRunning,
    bool IsBusy,
    bool LimitsAvailable,
    int? FiveHourRemainingPercent,
    int? WeeklyRemainingPercent,
    string? RecentResponse,
    string? RecentResponseId,
    DateTimeOffset? RecentResponseAt,
    long ActivityReadMilliseconds,
    long? LimitsReadMilliseconds,
    int SessionsScanned,
    int ActiveSessions,
    DateTimeOffset? LatestLifecycleAt,
    bool HasStaleStarted);

internal sealed class CodexStatusService
{
    private const int ActivityCandidateLimit = 32;
    private const int CompletionCandidateLimit = 16;
    private const int LifecycleTailBytes = 2 * 1024 * 1024;
    private const int ExpandedLifecycleTailBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan LimitRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleStartedAfter = TimeSpan.FromHours(2);
    private static readonly TimeSpan ExpandedScanRecency = TimeSpan.FromMinutes(10);

    private readonly string _sessionsRoot = Path.Combine(ProductPaths.CodexHome, "sessions");
    private readonly object _limitsGate = new();
    private DateTimeOffset _lastLimitAttempt = DateTimeOffset.MinValue;
    private (int? FiveHour, int? Weekly)? _cachedLimits;
    private Task? _limitRefreshTask;
    private long? _lastLimitReadMilliseconds;
    private readonly Dictionary<string, LifecycleCacheEntry> _lifecycleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CompletionCacheEntry> _completionCache = new(StringComparer.OrdinalIgnoreCase);
    internal string? LastError { get; private set; }

    public async Task<CodexStatus> ReadAsync(bool detectActivity, bool readLimits)
    {
        if (readLimits) EnsureLimitRefreshStarted();
        return await Task.Run(() => ReadStatus(detectActivity, readLimits));
    }

    private CodexStatus ReadStatus(bool detectActivity, bool readLimits)
    {
        var watch = Stopwatch.StartNew();
        var running = IsCodexRunning();
        var activity = running && detectActivity ? ReadActivityState() : default;
        var completion = ReadRecentCompletion();
        (int? FiveHour, int? Weekly)? limits;
        long? limitReadMilliseconds;
        lock (_limitsGate)
        {
            limits = _cachedLimits;
            limitReadMilliseconds = _lastLimitReadMilliseconds;
        }

        watch.Stop();
        return new CodexStatus(
            running,
            activity.IsBusy,
            readLimits && limits is not null,
            limits?.FiveHour,
            limits?.Weekly,
            completion.Response,
            completion.Id,
            completion.Timestamp,
            watch.ElapsedMilliseconds,
            limitReadMilliseconds,
            activity.SessionsScanned,
            activity.ActiveSessions,
            activity.LatestLifecycleAt,
            activity.HasStaleStarted);
    }

    private void EnsureLimitRefreshStarted()
    {
        lock (_limitsGate)
        {
            if (_limitRefreshTask is { IsCompleted: false }) return;
            if (DateTimeOffset.UtcNow - _lastLimitAttempt < LimitRefreshInterval) return;
            _lastLimitAttempt = DateTimeOffset.UtcNow;
            _limitRefreshTask = Task.Run(RefreshLimitsAsync);
        }
    }

    private async Task RefreshLimitsAsync()
    {
        var watch = Stopwatch.StartNew();
        var limits = await ReadLimitsAsync();
        watch.Stop();
        lock (_limitsGate)
        {
            _lastLimitReadMilliseconds = watch.ElapsedMilliseconds;
            if (limits is not null) _cachedLimits = limits;
        }
    }

    private (string? Response, string? Id, DateTimeOffset? Timestamp) ReadRecentCompletion()
    {
        try
        {
            if (!Directory.Exists(_sessionsRoot)) return default;
            string? latestResponse = null, latestId = null;
            DateTimeOffset? latestTimestamp = null;
            var candidates = EnumerateCandidateFiles(CompletionCandidateLimit).ToList();
            PruneCache(_completionCache, candidates.Select(file => file.FullName));
            foreach (var file in candidates)
            {
                var completion = ReadCompletion(file);
                if (completion.Response is not null && completion.Timestamp is not null && (latestTimestamp is null || completion.Timestamp > latestTimestamp))
                {
                    latestResponse = completion.Response;
                    latestId = completion.Id;
                    latestTimestamp = completion.Timestamp;
                }
            }
            return (latestResponse, latestId, latestTimestamp);
        }
        catch { }
        return default;
    }

    private CompletionSnapshot ReadCompletion(FileInfo file)
    {
        file.Refresh();
        if (_completionCache.TryGetValue(file.FullName, out var cached)
            && cached.Length == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
            return cached.Snapshot;

        string? response = null, id = null;
        DateTimeOffset? timestamp = null;
        foreach (var line in ReadTailLines(file.FullName, LifecycleTailBytes))
        {
            if (!line.Contains("\"phase\":\"final_answer\"", StringComparison.Ordinal) || !line.Contains("\"role\":\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (Text(root, "type") != "response_item" || !root.TryGetProperty("payload", out var payload) || Text(payload, "type") != "message" || Text(payload, "role") != "assistant" || Text(payload, "phase") != "final_answer") continue;
                var extracted = ExtractOutputText(payload);
                if (string.IsNullOrWhiteSpace(extracted)) continue;
                response = NormalizeResponse(extracted);
                timestamp = DateTimeOffset.TryParse(Text(root, "timestamp"), out var parsed) ? parsed : file.LastWriteTimeUtc;
                id = Text(payload, "id");
                if (string.IsNullOrWhiteSpace(id)) id = $"{file.FullName}|{timestamp:O}";
            }
            catch (JsonException) { }
        }

        var snapshot = response is null && cached is not null && file.Length >= cached.Length
            ? cached.Snapshot
            : new CompletionSnapshot(response, id, timestamp);
        _completionCache[file.FullName] = new CompletionCacheEntry(file.Length, file.LastWriteTimeUtc, snapshot);
        return snapshot;
    }

    private ActivityState ReadActivityState()
    {
        try
        {
            if (!Directory.Exists(_sessionsRoot)) return default;
            var now = DateTimeOffset.UtcNow;
            var scanned = 0;
            var active = 0;
            var staleStarted = false;
            DateTimeOffset? latestLifecycleAt = null;

            var candidates = EnumerateCandidateFiles(ActivityCandidateLimit).ToList();
            PruneCache(_lifecycleCache, candidates.Select(file => file.FullName));
            foreach (var file in candidates)
            {
                scanned++;
                var lifecycle = ReadLifecycle(file, now);
                if (lifecycle.LifecycleAt is DateTimeOffset lifecycleAt
                    && (latestLifecycleAt is null || lifecycleAt > latestLifecycleAt))
                    latestLifecycleAt = lifecycleAt;

                if (!string.Equals(lifecycle.State, "task_started", StringComparison.Ordinal)) continue;
                var lastActivityAt = lifecycle.LatestActivityAt ?? lifecycle.LifecycleAt ?? file.LastWriteTimeUtc;
                if (now - lastActivityAt <= StaleStartedAfter) active++;
                else staleStarted = true;
            }

            return new ActivityState(active > 0, scanned, active, latestLifecycleAt, staleStarted);
        }
        catch
        {
            return default;
        }
    }

    private IEnumerable<FileInfo> EnumerateCandidateFiles(int limit)
        => new DirectoryInfo(_sessionsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(limit);

    private static void PruneCache<T>(Dictionary<string, T> cache, IEnumerable<string> retainedPaths)
    {
        var retained = new HashSet<string>(retainedPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in cache.Keys.Where(path => !retained.Contains(path)).ToList()) cache.Remove(path);
    }

    private LifecycleSnapshot ReadLifecycle(FileInfo file, DateTimeOffset now)
    {
        file.Refresh();
        if (_lifecycleCache.TryGetValue(file.FullName, out var cached)
            && cached.Length == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
            return cached.Snapshot;

        var snapshot = ParseLifecycleTail(file.FullName, LifecycleTailBytes);
        if (snapshot.State is null && now - file.LastWriteTimeUtc <= ExpandedScanRecency && file.Length > LifecycleTailBytes)
            snapshot = ParseLifecycleTail(file.FullName, ExpandedLifecycleTailBytes);

        if (snapshot.State is null && cached is not null && file.Length >= cached.Length)
            snapshot = snapshot with { State = cached.Snapshot.State, LifecycleAt = cached.Snapshot.LifecycleAt };

        _lifecycleCache[file.FullName] = new LifecycleCacheEntry(file.Length, file.LastWriteTimeUtc, snapshot);
        return snapshot;
    }

    private static LifecycleSnapshot ParseLifecycleTail(string path, int maxBytes)
    {
        string? state = null;
        DateTimeOffset? lifecycleAt = null;
        DateTimeOffset? latestActivityAt = null;
        foreach (var line in ReadTailLines(path, maxBytes))
        {
            var timestamp = TryReadTimestamp(line);
            if (timestamp is not null && (latestActivityAt is null || timestamp > latestActivityAt)) latestActivityAt = timestamp;
            if (!TryReadLifecycle(line, out var candidate)) continue;
            if (timestamp is null || lifecycleAt is null || timestamp >= lifecycleAt)
            {
                state = candidate;
                lifecycleAt = timestamp ?? lifecycleAt;
            }
        }
        return new LifecycleSnapshot(state, lifecycleAt, latestActivityAt);
    }

    private static bool TryReadLifecycle(string line, out string? lifecycle)
    {
        lifecycle = null;
        if (!line.Contains("\"type\":\"event_msg\"", StringComparison.Ordinal)) return false;
        foreach (var candidate in new[] { "task_started", "task_complete", "turn_aborted" })
        {
            if (!line.Contains($"\"type\":\"{candidate}\"", StringComparison.Ordinal)) continue;
            lifecycle = candidate;
            return true;
        }
        return false;
    }

    private static DateTimeOffset? TryReadTimestamp(string line)
    {
        const string marker = "\"timestamp\":\"";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = line.IndexOf('"', start);
        return end > start && DateTimeOffset.TryParse(line[start..end], out var timestamp) ? timestamp : null;
    }

    private static IEnumerable<string> ReadTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        if (start > 0) reader.ReadLine();
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static string? ExtractOutputText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        return string.Join(" ", content.EnumerateArray().Where(item => Text(item, "type") == "output_text").Select(item => Text(item, "text")).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string NormalizeResponse(string value)
    {
        var oneLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 600 ? oneLine : oneLine[..599] + "…";
    }

    private async Task<(int? FiveHour, int? Weekly)?> ReadLimitsAsync()
    {
        LastError = "locate";
        var executable = FindCodexExecutable();
        if (executable is null) return null;
        Process? process = null;
        try
        {
            LastError = "start";
            process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, "app-server --stdio")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start()) return null;
            _ = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"yoyo-island\",\"version\":\"0.7\"},\"capabilities\":{\"experimentalApi\":true}}}");
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            LastError = "initialize";
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } initLine)
            {
                using var initDocument = JsonDocument.Parse(initLine);
                if (initDocument.RootElement.TryGetProperty("id", out var initId) && initId.TryGetInt32(out var value) && value == 1) break;
            }
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true}}");
            await process.StandardInput.FlushAsync();

            LastError = "limits";
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var responseId) || responseId != 2 || !root.TryGetProperty("result", out var result)) continue;
                var parsed = ParseLimits(result);
                LastError = null;
                return parsed;
            }
        }
        catch (Exception error) { LastError = $"{LastError}:{error.GetType().Name}"; }
        finally
        {
            try { if (process is { HasExited: false }) process.Kill(true); } catch { }
            process?.Dispose();
        }
        return null;
    }

    private static (int? FiveHour, int? Weekly) ParseLimits(JsonElement root)
    {
        int? fiveHour = null, weekly = null;
        if (root.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) root = codex;
        else if (root.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object) root = legacy;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) continue;
            var duration = Number(window, "windowDurationMins");
            var used = Number(window, "usedPercent");
            if (duration is null || used is null) continue;
            var remaining = (int)Math.Round(Math.Clamp(100 - used.Value, 0, 100));
            if (Math.Abs(duration.Value - 300) < 1) fiveHour = remaining;
            if (Math.Abs(duration.Value - 10080) < 1) weekly = remaining;
        }
        return (fiveHour, weekly);
    }

    private static double? Number(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool IsCodexRunning()
    {
        var processes = Process.GetProcessesByName("ChatGPT");
        try { return processes.Any(IsCodexProcess); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool IsCodexProcess(Process process)
    {
        try { return process.MainModule?.FileName?.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true; }
        catch { return process.MainWindowTitle.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase); }
    }

    private static string? FindCodexExecutable()
        => ApplicationLocator.FindCodexCliExecutable();

    private sealed record LifecycleSnapshot(string? State, DateTimeOffset? LifecycleAt, DateTimeOffset? LatestActivityAt);
    private sealed record LifecycleCacheEntry(long Length, DateTime LastWriteTimeUtc, LifecycleSnapshot Snapshot);
    private sealed record CompletionSnapshot(string? Response, string? Id, DateTimeOffset? Timestamp);
    private sealed record CompletionCacheEntry(long Length, DateTime LastWriteTimeUtc, CompletionSnapshot Snapshot);
    private readonly record struct ActivityState(bool IsBusy, int SessionsScanned, int ActiveSessions, DateTimeOffset? LatestLifecycleAt, bool HasStaleStarted);
}
