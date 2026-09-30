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
    string? ConfirmationPrompt = null);

internal sealed partial class WorkBuddyStatusService
{
    private readonly string _projectsRoot = Path.Combine(ProductPaths.WorkBuddyConfigDirectory, "projects");
    private string? _cachedSessionPath;
    private string? _cachedTask;
    private WorkBuddyStatus? _cachedStatus;
    private long _cachedStatusLength;
    private DateTime _cachedStatusWriteUtc;

    internal void ResetCache()
    {
        _cachedSessionPath = null;
        _cachedTask = null;
        _cachedStatus = null;
        _cachedStatusLength = 0;
        _cachedStatusWriteUtc = default;
    }

    public Task<WorkBuddyStatus> ReadAsync() => Task.Run(Read);

    private WorkBuddyStatus Read()
    {
        var running = ApplicationLocator.IsProcessRunning("WorkBuddy");
        try
        {
            if (!Directory.Exists(_projectsRoot)) return new(running, false, false, running ? "无会话数据" : "未运行");
            var session = new DirectoryInfo(_projectsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(item => item.LastWriteTimeUtc).FirstOrDefault();
            if (session is null) return new(running, false, false, running ? "无会话数据" : "未运行");

            session.Refresh();
            if (_cachedStatus is not null
                && string.Equals(_cachedSessionPath, session.FullName, StringComparison.OrdinalIgnoreCase)
                && _cachedStatusLength == session.Length
                && _cachedStatusWriteUtc == session.LastWriteTimeUtc)
            {
                var stillFresh = DateTime.UtcNow - session.LastWriteTimeUtc < TimeSpan.FromSeconds(30);
                return _cachedStatus with { IsRunning = running, IsBusy = running && stillFresh && _cachedStatus.IsBusy };
            }

            var lines = JsonLineTailReader.Read(session.FullName, 1024 * 1024);
            string? latestTask = null;
            string? latestAction = null;
            string? latestResponse = null;
            string? latestResponseId = null;
            DateTimeOffset? latestResponseAt = null;
            string? confirmationId = null;
            string? confirmationPrompt = null;
            var pendingCalls = new HashSet<string>(StringComparer.Ordinal);
            var completed = false;

            foreach (var line in lines)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var type = Text(root, "type");
                    if (type == "message" && Text(root, "role") == "user")
                    {
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
                        completed = Text(root, "status") == "completed";
                        if (completed)
                        {
                            var extracted = ExtractAssistantResponse(root);
                            if (!string.IsNullOrWhiteSpace(extracted))
                            {
                                latestResponse = NormalizeResponse(extracted);
                                latestResponseAt = ReadTimestamp(root) ?? session.LastWriteTimeUtc;
                                latestResponseId = Text(root, "id");
                                if (string.IsNullOrWhiteSpace(latestResponseId)) latestResponseId = $"{session.FullName}|{latestResponseAt:O}";
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
            var busy = running && (hasPendingAction || (fresh && !completed));
            var summary = requiresConfirmation
                ? confirmationPrompt ?? "等待你的确认"
                : busy
                ? latestAction ?? "正在处理任务"
                : latestTask is not null ? $"最近 · {latestTask}" : session.Directory?.Name ?? "已检测到会话";
            var status = new WorkBuddyStatus(running, true, busy, Normalize(summary), latestResponse, latestResponseId, latestResponseAt,
                requiresConfirmation, confirmationId, confirmationPrompt);
            _cachedSessionPath = session.FullName;
            _cachedStatusLength = session.Length;
            _cachedStatusWriteUtc = session.LastWriteTimeUtc;
            _cachedStatus = status;
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
