using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record CodexDecision(string Id, string Prompt, DateTimeOffset CreatedAt);

internal sealed class CodexDecisionReader
{
    private sealed record Pending(CodexDecision Decision, int Count, HashSet<int> Answered);
    private readonly Dictionary<string, Pending> _pending = new();
    internal IEnumerable<string> PendingCallIds => _pending.Keys;
    public CodexDecision? Current => _pending.Values.Where(p => DateTimeOffset.UtcNow - p.Decision.CreatedAt < TimeSpan.FromHours(2)).OrderBy(p => p.Decision.CreatedAt).FirstOrDefault()?.Decision;
    public void Reset() => _pending.Clear();

    public void Apply(IEnumerable<string> lines, string sessionId)
    {
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("payload", out var p)) continue;
                var record = Text(root, "type");
                var type = Text(p, "type");
                if (record == "event_msg" && type == "turn_aborted") { Reset(); continue; }
                if (record != "response_item") continue;
                if (type is "function_call" or "custom_tool_call")
                {
                    var name = Text(p, "name").Split('.').Last();
                    if (name is not ("request_user_input" or "request_user_input_async")) continue;
                    var id = Text(p, "call_id");
                    if (string.IsNullOrEmpty(id)) continue;
                    var argumentText = Text(p, type == "custom_tool_call" ? "input" : "arguments");
                    using var args = JsonDocument.Parse(argumentText);
                    if (!args.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array) continue;
                    var prompts = questions.EnumerateArray().Select(q => Text(q, "question") is { Length: > 0 } question ? question : Text(q, "title")).Where(q => q.Length > 0).ToArray();
                    if (prompts.Length == 0) continue;
                    var at = DateTimeOffset.TryParse(Text(root, "timestamp"), out var timestamp) ? timestamp : DateTimeOffset.UtcNow;
                    _pending[id] = new Pending(new CodexDecision($"{sessionId}|{id}", string.Join("\n", prompts), at), questions.GetArrayLength(), new());
                }
                else if (type is "function_call_output" or "custom_tool_call_output")
                {
                    var id = Text(p, "call_id");
                    // Async accepted=true only confirms the UI prompt was delivered; it is not the user's answer.
                    if (!_pending.ContainsKey(id)) continue;
                    var output = Text(p, "output");
                    try
                    {
                        using var result = JsonDocument.Parse(output);
                        if (result.RootElement.TryGetProperty("answers", out _)
                            || (result.RootElement.TryGetProperty("error", out _) && !result.RootElement.TryGetProperty("accepted", out _))) _pending.Remove(id);
                    }
                    catch (JsonException) { }
                }
                else if (type == "message" && Text(p, "role") == "user" && p.TryGetProperty("content", out var content))
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        var text = Text(item, "text");
                        const string startTag = "<send_user_message_question_reply>", endTag = "</send_user_message_question_reply>";
                        if (!text.TrimStart().StartsWith(startTag, StringComparison.Ordinal)) continue;
                        var start = text.IndexOf(startTag, StringComparison.Ordinal) + startTag.Length;
                        var end = text.IndexOf(endTag, start, StringComparison.Ordinal);
                        if (end < 0) continue;
                        using var reply = JsonDocument.Parse(text[start..end]);
                        foreach (var answer in reply.RootElement.EnumerateArray())
                        {
                            using var key = JsonDocument.Parse(Text(answer, "questionItemId"));
                            var parts = key.RootElement;
                            if (parts.GetArrayLength() < 3) continue;
                            var call = parts[1].GetString() ?? "";
                            if (!_pending.TryGetValue(call, out var pending)) continue;
                            pending.Answered.Add(parts[2].GetInt32());
                            if (pending.Answered.Count >= pending.Count) _pending.Remove(call);
                        }
                    }
                }
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }
    }
    private static string Text(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
