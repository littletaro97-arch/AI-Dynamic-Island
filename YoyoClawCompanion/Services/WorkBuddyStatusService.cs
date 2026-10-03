using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoyoClawCompanion.Services;

internal sealed record WorkBuddyStatus(
    bool IsRunning,
    bool DataAvailable,
    bool IsBusy,
    string Summary,
    string? RecentResponse = null,
    string? RecentResponseId = null,
    DateTimeOffset? RecentResponseAt = null,
    bool RequiresConfirmation = false,
    string? ConfirmationId = null,
    string? ConfirmationPrompt = null,
    IReadOnlyList<TaskCompletionEvent>? Completions = null);

internal sealed partial class WorkBuddyStatusService : IDisposable
{
    private readonly string _projectsRoot = Path.Combine(ProductPaths.WorkBuddyConfigDirectory, "projects");
    private RecentSessionIndex? _sessionIndex; // Lazy: child session readers never enumerate directories.
    public void Dispose() => _sessionIndex?.Dispose();
    private readonly Dictionary<string, WorkBuddyStatusService> _sessionReaders = new(StringComparer.OrdinalIgnoreCase);
    private string? _cachedSessionPath;
    private string? _cachedTask;
    private WorkBuddyStatus? _cachedStatus;
    private long _cachedStatusLength;
    private DateTime _cachedStatusWriteUtc;
    private bool _cachedPendingActions;
    private JsonLineCursor? _sessionCursor;
    private SessionParseState? _parseState;
    private long _observedRevision, _cachedRevision;
    private sealed record SessionParseState(string? Task, string? Action, string? Response, string? ResponseId,
        DateTimeOffset? ResponseAt, string? ConfirmationId, string? ConfirmationPrompt,
        HashSet<string> PendingCalls, bool Completed, IReadOnlyList<TaskCompletionEvent> Completions);

    internal void ResetCache()
    {
        _sessionIndex?.Invalidate();
        _sessionReaders.Clear();
        _cachedSessionPath = null;
        _cachedTask = null;
        _cachedStatus = null;
        _cachedStatusLength = 0;
        _cachedStatusWriteUtc = default;
        _sessionCursor = null;
        _parseState = null;
        _observedRevision = _cachedRevision = 0;
    }

    public Task<WorkBuddyStatus> ReadAsync(bool? isRunning = null) => Task.Run(() => Read(isRunning));

    private WorkBuddyStatus Read(bool? isRunning)
    {
        var running = isRunning ?? ApplicationLocator.IsProcessRunning("WorkBuddy");
        if (!running) return new(false, false, false, "未运行");
        try
        {
            var sessions = (_sessionIndex ??= new(_projectsRoot, "*.jsonl")).Find(24);
            var retained = sessions.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _sessionReaders.Keys.Where(path => !retained.Contains(path)).ToArray()) _sessionReaders.Remove(path);
            var states = sessions.Select(file =>
            {
                if (!_sessionReaders.TryGetValue(file.FullName, out var reader)) _sessionReaders[file.FullName] = reader = new WorkBuddyStatusService();
                reader._observedRevision = _sessionIndex.RevisionFor(file.FullName);
                return reader.ReadSession(file, running);
            }).ToArray();
            if (states.Length == 0) return new(running, false, false, "无会话数据");
            var latest = states.OrderByDescending(state => state.RecentResponseAt).First();
            var current = states.FirstOrDefault(state => state.RequiresConfirmation) ?? states.FirstOrDefault(state => state.IsBusy) ?? states[0];
            return current with
            {
                IsBusy = states.Any(state => state.IsBusy),
                RecentResponse = latest.RecentResponse, RecentResponseId = latest.RecentResponseId, RecentResponseAt = latest.RecentResponseAt,
                Completions = states.SelectMany(state => state.Completions ?? []).ToArray()
            };
        }
        catch { return new(running, false, false, "状态不可用"); }
    }

    private WorkBuddyStatus ReadSession(FileInfo session, bool running)
    {
        try
        {
            session.Refresh();
            if (_cachedStatus is not null
                && string.Equals(_cachedSessionPath, session.FullName, StringComparison.OrdinalIgnoreCase)
                && _cachedStatusLength == session.Length
                && _cachedStatusWriteUtc == session.LastWriteTimeUtc && _cachedRevision == _observedRevision)
            {
                var stillFresh = DateTime.UtcNow - session.LastWriteTimeUtc < TimeSpan.FromSeconds(30);
                var pendingFresh = DateTime.UtcNow - session.LastWriteTimeUtc < TimeSpan.FromHours(2);
                return _cachedStatus with { IsRunning = running, IsBusy = running && (_cachedStatus.RequiresConfirmation || (_cachedPendingActions && pendingFresh) || (stillFresh && _cachedStatus.IsBusy)) };
            }

            var cursor = _sessionCursor ??= new JsonLineCursor();
            var lines = cursor.Read(session.FullName, 1024 * 1024);
            var previous = cursor.Rebuild ? null : _parseState;
            if (cursor.NewGeneration) { _cachedTask = null; _cachedSessionPath = null; }
            string? latestTask = previous?.Task;
            string? latestAction = previous?.Action;
            string? latestResponse = previous?.Response;
            string? latestResponseId = previous?.ResponseId;
            DateTimeOffset? latestResponseAt = previous?.ResponseAt;
            string? confirmationId = previous?.ConfirmationId;
            string? confirmationPrompt = previous?.ConfirmationPrompt;
            var pendingCalls = new HashSet<string>(previous?.PendingCalls ?? [], StringComparer.Ordinal);
            var completed = previous?.Completed ?? false;
            var completions = new List<TaskCompletionEvent>(previous?.Completions ?? []);

            foreach (var line in lines)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var type = Text(root, "type");
                    if (type == "message" && Text(root, "role") == "user")
                    {
                        completed = false;
                        var extracted = ExtractUserQuery(root);
                        if (!string.IsNullOrWhiteSpace(extracted)) latestTask = extracted;
                    }
                    if (type == "function_call")
                    {
                        var callId = Text(root, "callId");
                        if (!string.IsNullOrWhiteSpace(callId)) pendingCalls.Add(callId);
                        var name = Text(root, "name");
                        latestAction = FriendlyAction(name);
                        if (name == "AskUserQuestion")
                        {
                            confirmationId = callId;
                            confirmationPrompt = ExtractConfirmationPrompt(root);
                        }
                    }
                    if (type == "function_call_result")
                    {
                        var callId = Text(root, "callId");
                        if (!string.IsNullOrWhiteSpace(callId)) pendingCalls.Remove(callId);
                        if (!string.IsNullOrWhiteSpace(callId) && callId == confirmationId)
                        {
                            confirmationId = null;
                            confirmationPrompt = null;
                        }
                    }
                    if (type == "message" && Text(root, "role") == "assistant")
                    {
                        // WorkBuddy also marks streamed commentary as "completed".
                        // Its final response carries turn usage metadata; message status
                        // alone only says that this individual message finished streaming.
                        completed = Text(root, "status") == "completed" && HasFinalResponseMetadata(root)
                            && pendingCalls.Count == 0 && string.IsNullOrWhiteSpace(confirmationId);
                        if (completed)
                        {
                            var extracted = ExtractAssistantResponse(root);
                            if (!string.IsNullOrWhiteSpace(extracted))
                            {
                                latestResponse = NormalizeResponse(extracted);
                                latestResponseAt = ReadTimestamp(root) ?? session.LastWriteTimeUtc;
                                latestResponseId = Text(root, "id");
                                if (string.IsNullOrWhiteSpace(latestResponseId)) latestResponseId = $"{session.FullName}|{latestResponseAt:O}";
                                completions.Add(new(latestResponseId, latestResponse, latestResponseAt.Value));
                            }
                        }
                    }
                    else if (type is "function_call" or "function_call_result" or "reasoning") completed = false;
                }
                catch (JsonException) { }
            }

            if (latestTask is null)
            {
                if (string.Equals(_cachedSessionPath, session.FullName, StringComparison.OrdinalIgnoreCase)) latestTask = _cachedTask;
                else
                {
                    latestTask = ReadTaskFromStart(session.FullName);
                    _cachedSessionPath = session.FullName;
                    _cachedTask = latestTask;
                }
            }

            var requiresConfirmation = !string.IsNullOrWhiteSpace(confirmationId) && pendingCalls.Contains(confirmationId);
            var hasPendingAction = pendingCalls.Count > 0;
            var fresh = DateTime.UtcNow - session.LastWriteTimeUtc < TimeSpan.FromSeconds(30);
            var busy = running && (requiresConfirmation || (hasPendingAction && DateTime.UtcNow - session.LastWriteTimeUtc < TimeSpan.FromHours(2)) || (fresh && !completed));
            var summary = requiresConfirmation
                ? confirmationPrompt ?? "等待你的确认"
                : busy
                ? latestAction ?? "正在处理任务"
                : latestTask is not null ? $"最近 · {latestTask}" : session.Directory?.Name ?? "已检测到会话";
            var status = new WorkBuddyStatus(running, true, busy, Normalize(summary), latestResponse, latestResponseId, latestResponseAt,
                requiresConfirmation, confirmationId, confirmationPrompt, completions.DistinctBy(item => item.Id).TakeLast(32).ToArray());
            _parseState = new(latestTask, latestAction, latestResponse, latestResponseId, latestResponseAt,
                confirmationId, confirmationPrompt, pendingCalls, completed, status.Completions!);
            _cachedSessionPath = session.FullName;
            _cachedTask = latestTask;
            _cachedStatusLength = session.Length;
            _cachedStatusWriteUtc = session.LastWriteTimeUtc;
            _cachedRevision = _observedRevision;
            _cachedStatus = status;
            _cachedPendingActions = hasPendingAction;
            return status;
        }
        catch
        {
            return new(running, false, false, running ? "状态不可用" : "未运行");
        }
    }

    private static string? ReadTaskFromStart(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            for (var index = 0; index < 12 && reader.ReadLine() is { } line; index++)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (Text(root, "type") == "message" && Text(root, "role") == "user")
                {
                    var task = ExtractUserQuery(root);
                    if (!string.IsNullOrWhiteSpace(task)) return task;
                }
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractUserQuery(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in content.EnumerateArray())
        {
            if (Text(item, "type") != "input_text") continue;
            var text = Text(item, "text");
            var match = UserQueryRegex().Match(text);
            if (match.Success) return match.Groups[1].Value;
            if (!text.StartsWith("<system-reminder", StringComparison.Ordinal)) return text;
        }
        return null;
    }

    private static string? ExtractAssistantResponse(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        return string.Join(" ", content.EnumerateArray().Where(item => Text(item, "type") == "output_text").Select(item => Text(item, "text")).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool HasFinalResponseMetadata(JsonElement root)
    {
        // Observed in Space-Bunny, GLM, DeepSeek and Kimi local session records.
        // Accept either WorkBuddy's normalized statistics or the raw provider copy.
        return root.TryGetProperty("providerData", out var provider) &&
                (HasTokenCount(provider, "usage", "outputTokens") || HasTokenCount(provider, "rawUsage", "completion_tokens"))
            || root.TryGetProperty("message", out var message) && HasTokenCount(message, "usage", "output_tokens");
    }

    private static bool HasTokenCount(JsonElement parent, string name, string counter)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var usage)
            && usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(counter, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var tokens) && tokens >= 0;

    private static string? ExtractConfirmationPrompt(JsonElement root)
    {
        var arguments = Text(root, "arguments");
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (!document.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array) return null;
            foreach (var question in questions.EnumerateArray())
            {
                var prompt = Text(question, "question");
                if (!string.IsNullOrWhiteSpace(prompt)) return NormalizeResponse(prompt);
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds)) return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static string FriendlyAction(string value) => value switch
    {
        "Read" => "正在读取文件",
        "Write" => "正在写入文件",
        "Edit" => "正在修改文件",
        "Bash" or "Shell" => "正在执行命令",
        "AskUserQuestion" => "等待你的确认",
        _ when !string.IsNullOrWhiteSpace(value) => $"正在执行 {value}",
        _ => "正在处理任务"
    };

    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Normalize(string value)
    {
        value = ImageReferenceRegex().Replace(value, "");
        value = QuotedMentionRegex().Replace(value, "");
        var oneLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 28 ? oneLine : oneLine[..27] + "…";
    }

    private static string NormalizeResponse(string value)
    {
        value = ImageReferenceRegex().Replace(value, "");
        value = QuotedMentionRegex().Replace(value, "");
        var oneLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 600 ? oneLine : oneLine[..599] + "…";
    }

    [GeneratedRegex("<user_query>(.*?)</user_query>", RegexOptions.Singleline)]
    private static partial Regex UserQueryRegex();

    [GeneratedRegex("@image#\\d+:\\S+\\s*")]
    private static partial Regex ImageReferenceRegex();

    [GeneratedRegex("@\"[^\"]+\"\\s*")]
    private static partial Regex QuotedMentionRegex();
}
