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
    DateTimeOffset? RecentResponseAt);

internal sealed class CodexStatusService
{
    private readonly string _sessionsRoot = Path.Combine(ProductPaths.CodexHome, "sessions");
    private DateTimeOffset _lastLimitRead;
    private (int? FiveHour, int? Weekly)? _cachedLimits;
    private readonly Dictionary<string, (long Length, string? Lifecycle)> _lifecycleCache = new(StringComparer.OrdinalIgnoreCase);
    internal string? LastError { get; private set; }

    public Task<CodexStatus> ReadAsync(bool detectActivity, bool readLimits) => Task.Run(async () =>
    {
        var running = IsCodexRunning();
        var busy = running && detectActivity && ReadBusyState();
        var completion = ReadRecentCompletion();
        if (readLimits && (DateTimeOffset.Now - _lastLimitRead > TimeSpan.FromSeconds(60) || _cachedLimits is null))
        {
            var limits = await ReadLimitsAsync();
            _lastLimitRead = DateTimeOffset.Now;
            if (limits is not null) _cachedLimits = limits;
        }
        return new CodexStatus(running, busy, readLimits && _cachedLimits is not null, _cachedLimits?.FiveHour, _cachedLimits?.Weekly,
            completion.Response, completion.Id, completion.Timestamp);
    });

    private (string? Response, string? Id, DateTimeOffset? Timestamp) ReadRecentCompletion()
    {
        try
        {
            if (!Directory.Exists(_sessionsRoot)) return default;
            string? latestResponse = null, latestId = null;
            DateTimeOffset? latestTimestamp = null;
            foreach (var file in new DirectoryInfo(_sessionsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(item => item.LastWriteTimeUtc).Take(8))
            {
                string? response = null, id = null;
                DateTimeOffset? timestamp = null;
                foreach (var line in ReadTailLines(file.FullName, 2 * 1024 * 1024))
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
                if (response is not null && timestamp is not null && (latestTimestamp is null || timestamp > latestTimestamp))
                {
                    latestResponse = response;
                    latestId = id;
                    latestTimestamp = timestamp;
                }
            }
            return (latestResponse, latestId, latestTimestamp);
        }
        catch { }
        return default;
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

    private bool ReadBusyState()
    {
        try
        {
            if (!Directory.Exists(_sessionsRoot)) return false;
            var newest = new DirectoryInfo(_sessionsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc).Take(8);
            foreach (var file in newest)
            {
                var lifecycle = ReadLifecycle(file);
                if (lifecycle is not null) return lifecycle == "task_started";
            }
            return false;
        }
        catch { return false; }
    }

    private string? ReadLifecycle(FileInfo file)
    {
        _lifecycleCache.TryGetValue(file.FullName, out var cached);
        if (cached.Length == file.Length) return cached.Lifecycle;
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = cached.Length > 0 && cached.Length <= stream.Length ? Math.Max(0, cached.Length - 256) : 0;
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var content = reader.ReadToEnd();
        var lifecycle = cached.Lifecycle;
        var bestIndex = -1;
        foreach (var kind in new[] { "task_started", "task_complete", "turn_aborted" })
        {
            var marker = $"\"type\":\"event_msg\",\"payload\":{{\"type\":\"{kind}\"";
            var index = content.LastIndexOf(marker, StringComparison.Ordinal);
            if (index > bestIndex) { bestIndex = index; lifecycle = kind; }
        }
        _lifecycleCache[file.FullName] = (file.Length, lifecycle);
        return lifecycle;
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
}
