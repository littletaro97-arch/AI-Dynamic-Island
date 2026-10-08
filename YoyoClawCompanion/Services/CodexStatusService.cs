using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record CodexStatus(
    bool IsRunning,
    bool IsBusy,
    bool LimitsAvailable,
    bool LimitsLoading,
    string? LimitsError,
    int? FiveHourRemainingPercent,
    int? WeeklyRemainingPercent,
    DateTimeOffset? FiveHourResetsAt,
    DateTimeOffset? WeeklyResetsAt,
    string? RecentResponse,
    string? RecentResponseId,
    DateTimeOffset? RecentResponseAt,
    long ActivityReadMilliseconds,
    long? LimitsReadMilliseconds,
    int SessionsScanned,
    int ActiveSessions,
    DateTimeOffset? LatestLifecycleAt,
    bool HasStaleStarted,
    IReadOnlyList<TaskCompletionEvent>? Completions = null,
    bool RequiresConfirmation = false,
    string? ConfirmationId = null,
    string? ConfirmationPrompt = null);

internal sealed class CodexStatusService : IDisposable
{
    private sealed record RateLimitsSnapshot(int? FiveHour, int? Weekly, DateTimeOffset? FiveHourResetsAt, DateTimeOffset? WeeklyResetsAt);
    private const int ActivityCandidateLimit = 32;
    private const int CompletionCandidateLimit = 16;
    private const int LifecycleTailBytes = 1024 * 1024;
    private const int ExpandedLifecycleTailBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan LimitRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LimitFailureRetryInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LimitsTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StaleStartedAfter = TimeSpan.FromHours(2);
    private static readonly TimeSpan ExpandedScanRecency = TimeSpan.FromMinutes(10);

    private readonly string _sessionsRoot = Path.Combine(ProductPaths.CodexHome, "sessions");
    private readonly RecentSessionIndex _sessionIndex = new(Path.Combine(ProductPaths.CodexHome, "sessions"), "*.jsonl");
    private readonly object _limitsGate = new();
    private DateTimeOffset _lastLimitAttempt = DateTimeOffset.MinValue;
    private RateLimitsSnapshot? _cachedLimits;
    private Task? _limitRefreshTask;
    private int _limitGeneration;
    private long? _lastLimitReadMilliseconds;
    private string? _lastLimitError;
    private readonly Dictionary<string, LifecycleCacheEntry> _lifecycleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CompletionCacheEntry> _completionCache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record DecisionCacheEntry(long Length, DateTime WriteAt, long Revision, JsonLineCursor Cursor, CodexDecisionReader Reader);
    private readonly Dictionary<string, DecisionCacheEntry> _decisionCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TaskCompletionEvent> _recentCompletions = [];
    private FileInfo[]? _candidateFiles;
    private readonly Dictionary<string, (DateTime Created, long Length, bool Guardian)> _sourceCache = new(StringComparer.OrdinalIgnoreCase);
    internal string? LastError { get; private set; }
    internal event EventHandler? LimitsUpdated;

    internal void ResetCache()
    {
        _sessionIndex.Invalidate();
        lock (_limitsGate)
        {
            _limitGeneration++;
            _lastLimitAttempt = DateTimeOffset.MinValue;
            _cachedLimits = null;
            _lastLimitReadMilliseconds = null;
            _lastLimitError = null;
            _limitRefreshTask = null;
        }
        _lifecycleCache.Clear();
        _completionCache.Clear();
        _decisionCache.Clear();
        _sourceCache.Clear();
        LastError = null;
    }

    public async Task<CodexStatus> ReadAsync(bool detectActivity, bool readLimits, bool? isRunning = null)
    {
        if (readLimits) EnsureLimitRefreshStarted();
        return await Task.Run(() => ReadStatus(detectActivity, readLimits, isRunning));
    }

    private CodexStatus ReadStatus(bool detectActivity, bool readLimits, bool? isRunning)
    {
        _candidateFiles = null;
        var watch = Stopwatch.StartNew();
        var running = isRunning ?? IsCodexRunning();
        var activity = running && detectActivity ? ReadActivityState() : default;
        var completion = running ? ReadRecentCompletion() : default;
        var decision = running && detectActivity ? ReadDecisionState() : null;
        RateLimitsSnapshot? limits;
        long? limitReadMilliseconds;
        bool limitsLoading;
        string? limitsError;
        lock (_limitsGate)
        {
            limits = _cachedLimits;
            limitReadMilliseconds = _lastLimitReadMilliseconds;
            limitsLoading = readLimits && _limitRefreshTask is { IsCompleted: false };
            limitsError = _lastLimitError;
        }

        watch.Stop();
        return new CodexStatus(
            running,
            activity.IsBusy,
            readLimits && limits is not null,
            limitsLoading,
            limitsError,
            limits?.FiveHour,
            limits?.Weekly,
            limits?.FiveHourResetsAt,
            limits?.WeeklyResetsAt,
            completion.Response,
            completion.Id,
            completion.Timestamp,
            watch.ElapsedMilliseconds,
            limitReadMilliseconds,
            activity.SessionsScanned,
            activity.ActiveSessions,
            activity.LatestLifecycleAt,
            activity.HasStaleStarted,
            running ? _recentCompletions : [], decision is not null, decision?.Id, decision?.Prompt);
    }

    private CodexDecision? ReadDecisionState()
    {
        var candidates = EnumerateCandidateFiles(ActivityCandidateLimit).ToArray();
        PruneCache(_decisionCache, candidates.Select(f => f.FullName));
        var pending = new List<CodexDecision>();
        foreach (var file in candidates)
        {
            try
            {
                file.Refresh();
                var revision = _sessionIndex.RevisionFor(file.FullName);
                if (!_decisionCache.TryGetValue(file.FullName, out var cached) || cached.Length != file.Length || cached.WriteAt != file.LastWriteTimeUtc || cached.Revision != revision)
                {
                    var cursor = cached?.Cursor ?? new JsonLineCursor();
                    var reader = cached?.Reader ?? new CodexDecisionReader();
                    var markers = new[] { "request_user_input", "send_user_message_question_reply", "turn_aborted", "answers" }.Concat(reader.PendingCallIds).ToArray();
                    var lines = cursor.Read(file.FullName, LifecycleTailBytes, markers);
                    if (cursor.Rebuild) reader.Reset();
                    reader.Apply(lines, file.FullName);
                    _decisionCache[file.FullName] = cached = new(file.Length, file.LastWriteTimeUtc, revision, cursor, reader);
                }
                if (cached.Reader.Current is { } decision && DateTimeOffset.UtcNow - decision.CreatedAt < StaleStartedAfter) pending.Add(decision);
            }
            catch (IOException) { }
        }
        return pending.OrderBy(d => d.CreatedAt).FirstOrDefault();
    }

    private void EnsureLimitRefreshStarted()
    {
        lock (_limitsGate)
        {
            if (_limitRefreshTask is { IsCompleted: false }) return;
            var retryInterval = _lastLimitError is null ? LimitRefreshInterval : LimitFailureRetryInterval;
            if (DateTimeOffset.UtcNow - _lastLimitAttempt < retryInterval) return;
            _lastLimitAttempt = DateTimeOffset.UtcNow;
            var generation = _limitGeneration;
            _limitRefreshTask = Task.Run(() => RefreshLimitsAsync(generation));
        }
    }

    private async Task RefreshLimitsAsync(int generation)
    {
        var watch = Stopwatch.StartNew();
        var limits = await ReadLimitsAsync();
        watch.Stop();
        string? failure = null;
        lock (_limitsGate)
        {
            if (generation != _limitGeneration) return;
            _lastLimitReadMilliseconds = watch.ElapsedMilliseconds;
            if (limits is not null)
            {
                _cachedLimits = limits;
                _lastLimitError = null;
            }
            else failure = _lastLimitError = LastError ?? "unknown";
        }
        if (failure is not null) WriteDiagnostic($"codex-limits: {failure}");
        try { LimitsUpdated?.Invoke(this, EventArgs.Empty); } catch { }
    }

    private (string? Response, string? Id, DateTimeOffset? Timestamp) ReadRecentCompletion()
    {
        try
        {
            if (!Directory.Exists(_sessionsRoot)) return default;
            string? latestResponse = null, latestId = null;
            DateTimeOffset? latestTimestamp = null;
            var candidates = EnumerateCandidateFiles(CompletionCandidateLimit).ToList();
            var completions = new List<TaskCompletionEvent>();
            PruneCache(_completionCache, candidates.Select(file => file.FullName));
            foreach (var file in candidates)
            {
                var completion = ReadCompletion(file);
                completions.AddRange(completion.Events ?? []);
                if (completion.Response is not null && completion.Timestamp is not null && (latestTimestamp is null || completion.Timestamp > latestTimestamp))
                {
                    latestResponse = completion.Response;
                    latestId = completion.Id;
                    latestTimestamp = completion.Timestamp;
                }
            }
            _recentCompletions = completions;
            return (latestResponse, latestId, latestTimestamp);
        }
        catch { }
        return default;
    }

    private CompletionSnapshot ReadCompletion(FileInfo file)
    {
        file.Refresh();
        var revision = _sessionIndex.RevisionFor(file.FullName);
        if (_completionCache.TryGetValue(file.FullName, out var cached)
            && cached.Length == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc && cached.Revision == revision)
            return cached.Snapshot;

        string? response = null, id = null;
        DateTimeOffset? timestamp = null;
        var cursor = cached?.Cursor ?? new JsonLineCursor();
        var lines = cursor.Read(file.FullName, LifecycleTailBytes, ["final_answer"]);
        var events = cursor.NewGeneration ? new List<TaskCompletionEvent>() : new List<TaskCompletionEvent>(cached?.Snapshot.Events ?? []);
        foreach (var line in lines)
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
                events.Add(new(id, response, timestamp.Value));
            }
            catch (JsonException) { }
        }

        var snapshot = response is null && cached is not null && !cursor.NewGeneration
            ? cached.Snapshot
            : new CompletionSnapshot(response, id, timestamp, events.DistinctBy(item => item.Id).TakeLast(32).ToArray());
        _completionCache[file.FullName] = new CompletionCacheEntry(file.Length, file.LastWriteTimeUtc, snapshot, cursor, revision);
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
    {
        if (_candidateFiles is null)
        {
            var files = _sessionIndex.Find(ActivityCandidateLimit);
            PruneCache(_sourceCache, files.Select(file => file.FullName));
            _candidateFiles = files.Where(IsUserSession).ToArray();
        }
        return _candidateFiles.Take(limit);
    }

    private bool IsUserSession(FileInfo file)
    {
        file.Refresh();
        if (_sourceCache.TryGetValue(file.FullName, out var cached)
            && cached.Created == file.CreationTimeUtc && file.Length >= cached.Length)
        {
            _sourceCache[file.FullName] = cached with { Length = file.Length };
            return !cached.Guardian;
        }
        var guardian = CodexSessionSource.IsGuardian(file);
        if (guardian is bool known) _sourceCache[file.FullName] = (file.CreationTimeUtc, file.Length, known);
        return guardian != true;
    }

    public void Dispose() => _sessionIndex.Dispose();

    private static void PruneCache<T>(Dictionary<string, T> cache, IEnumerable<string> retainedPaths)
    {
        var retained = new HashSet<string>(retainedPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in cache.Keys.Where(path => !retained.Contains(path)).ToList()) cache.Remove(path);
    }

    private LifecycleSnapshot ReadLifecycle(FileInfo file, DateTimeOffset now)
    {
        file.Refresh();
        var revision = _sessionIndex.RevisionFor(file.FullName);
        if (_lifecycleCache.TryGetValue(file.FullName, out var cached)
            && cached.Length == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc && cached.Revision == revision)
            return cached.Snapshot;

        var cursor = cached?.Cursor ?? new JsonLineCursor();
        var lines = cursor.Read(file.FullName, LifecycleTailBytes, ["task_started", "task_complete", "turn_aborted"]);
        var snapshot = ParseLifecycleLines(lines, file.LastWriteTimeUtc);
        if (cursor.Rebuild && snapshot.State is null && now - file.LastWriteTimeUtc <= ExpandedScanRecency && file.Length > LifecycleTailBytes)
            snapshot = ParseLifecycleTail(file.FullName, ExpandedLifecycleTailBytes);

        if (snapshot.State is null && cached is not null && !cursor.NewGeneration)
            snapshot = snapshot with { State = cached.Snapshot.State, LifecycleAt = cached.Snapshot.LifecycleAt };

        _lifecycleCache[file.FullName] = new LifecycleCacheEntry(file.Length, file.LastWriteTimeUtc, snapshot, cursor, revision);
        return snapshot;
    }

    private static LifecycleSnapshot ParseLifecycleTail(string path, int maxBytes)
        => ParseLifecycleLines(JsonLineTailReader.Read(path, maxBytes, ["task_started", "task_complete", "turn_aborted"]), File.GetLastWriteTimeUtc(path));

    private static LifecycleSnapshot ParseLifecycleLines(IEnumerable<string> lines, DateTime lastWriteUtc)
    {
        string? state = null;
        DateTimeOffset? lifecycleAt = null;
        DateTimeOffset? latestActivityAt = null;
        foreach (var line in lines)
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
        // File write time covers intervening tool/stream records without decoding their large payloads.
        return new LifecycleSnapshot(state, lifecycleAt, lastWriteUtc);
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

    private async Task<RateLimitsSnapshot?> ReadLimitsAsync()
    {
        LastError = "locate";
        var executable = FindCodexExecutable();
        if (executable is null) return null;
        Process? process = null;
        OwnedProcessScope? processScope = null;
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
            processScope = OwnedProcessScope.Attach(process);
            _ = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                id = 1, method = "initialize", @params = new
                {
                    clientInfo = new { name = "ai-dynamic-island", version = GitHubUpdateService.CurrentVersion },
                    capabilities = new { experimentalApi = true }
                }
            }));
            await process.StandardInput.FlushAsync();
            LastError = "initialize";
            var initialized = false;
            using (var initializeTimeout = new CancellationTokenSource(InitializeTimeout))
            {
                while (await process.StandardOutput.ReadLineAsync(initializeTimeout.Token) is { } initLine)
                {
                    using var initDocument = JsonDocument.Parse(initLine);
                    var root = initDocument.RootElement;
                    if (!root.TryGetProperty("id", out var initId) || !initId.TryGetInt32(out var value) || value != 1) continue;
                    if (root.TryGetProperty("error", out _)) { LastError = "initialize:rpc-error"; return null; }
                    initialized = root.TryGetProperty("result", out _);
                    break;
                }
            }
            if (!initialized) { LastError = "initialize:no-response"; return null; }
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true}}");
            await process.StandardInput.FlushAsync();

            LastError = "limits";
            using var limitsTimeout = new CancellationTokenSource(LimitsTimeout);
            while (await process.StandardOutput.ReadLineAsync(limitsTimeout.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var id) || !id.TryGetInt32(out var responseId) || responseId != 2) continue;
                if (root.TryGetProperty("error", out _)) { LastError = "limits:rpc-error"; return null; }
                if (!root.TryGetProperty("result", out var result)) continue;
                var parsed = ParseLimits(result);
                if (parsed.FiveHour is null && parsed.Weekly is null) { LastError = "limits:unsupported-payload"; return null; }
                LastError = null;
                return parsed;
            }
        }
        catch (Exception error) { LastError = $"{LastError}:{error.GetType().Name}"; }
        finally
        {
            if (process is not null) await OwnedProcessScope.StopAsync(process, processScope, TimeSpan.FromSeconds(1));
            process?.Dispose();
        }
        return null;
    }

    private static RateLimitsSnapshot ParseLimits(JsonElement root)
    {
        int? fiveHour = null, weekly = null;
        DateTimeOffset? fiveHourResetsAt = null, weeklyResetsAt = null;
        if (root.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) root = codex;
        else if (root.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object) root = legacy;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) continue;
            var duration = Number(window, "windowDurationMins");
            var used = Number(window, "usedPercent");
            if (duration is null || used is null) continue;
            var remaining = (int)Math.Round(Math.Clamp(100 - used.Value, 0, 100));
            var resetsAt = UnixTime(window, "resetsAt");
            if (Math.Abs(duration.Value - 300) < 1) { fiveHour = remaining; fiveHourResetsAt = resetsAt; }
            if (Math.Abs(duration.Value - 10080) < 1) { weekly = remaining; weeklyResetsAt = resetsAt; }
        }
        return new(fiveHour, weekly, fiveHourResetsAt, weeklyResetsAt);
    }

    private static DateTimeOffset? UnixTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.TryGetInt64(out var seconds))
        {
            try { return seconds > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(seconds) : DateTimeOffset.FromUnixTimeSeconds(seconds); } catch { return null; }
        }
        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
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

    private static void WriteDiagnostic(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "diagnostics.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private sealed record LifecycleSnapshot(string? State, DateTimeOffset? LifecycleAt, DateTimeOffset? LatestActivityAt);
    private sealed record LifecycleCacheEntry(long Length, DateTime LastWriteTimeUtc, LifecycleSnapshot Snapshot, JsonLineCursor Cursor, long Revision);
    private sealed record CompletionSnapshot(string? Response, string? Id, DateTimeOffset? Timestamp, IReadOnlyList<TaskCompletionEvent>? Events = null);
    private sealed record CompletionCacheEntry(long Length, DateTime LastWriteTimeUtc, CompletionSnapshot Snapshot, JsonLineCursor Cursor, long Revision);
    private readonly record struct ActivityState(bool IsBusy, int SessionsScanned, int ActiveSessions, DateTimeOffset? LatestLifecycleAt, bool HasStaleStarted);
}
