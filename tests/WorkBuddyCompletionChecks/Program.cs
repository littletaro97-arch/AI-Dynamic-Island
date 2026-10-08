using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using YoyoClawCompanion;
using YoyoClawCompanion.Services;

class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    sealed class TestApp : App
    {
        public TestApp() => typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        protected override void OnStartup(StartupEventArgs e) { }
    }
    static void Assert(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static WorkBuddyStatus Read(WorkBuddyStatusService reader, string path)
        => (WorkBuddyStatus)typeof(WorkBuddyStatusService).GetMethod("ReadSession", Flags)!.Invoke(reader, [new FileInfo(path), true])!;
    static string Message(string id, string text, string metadata = "") => JsonSerializer.Serialize(new
    {
        type = "message", role = "assistant", status = "completed", id, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        content = new[] { new { type = "output_text", text } }
    }).TrimEnd('}') + (metadata.Length > 0 ? "," + metadata : "") + "}\n";

    [STAThread] static void Main(string[] args)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (args.FirstOrDefault() == "--replay") { Replay(args[1], directory); return; }
        var path = Path.Combine(directory, "turn.jsonl");
        using var reader = new WorkBuddyStatusService();
        File.WriteAllText(path, Message("progress", "第一步已完成，还需继续修改"));
        var progress = Read(reader, path);
        Assert(progress.IsBusy && progress.Completions?.Count == 0 && progress.RecentResponse is null, "completed commentary alone neither finishes nor notifies");
        File.AppendAllText(path, "{\"type\":\"function_call\",\"callId\":\"tool\",\"name\":\"Read\"}\n");
        var continuing = Read(reader, path);
        Assert(continuing.IsBusy && continuing.Completions?.Count == 0, "following tool preserves busy without premature completion");
        File.AppendAllText(path, Message("premature-final", "有结束统计但工具还未返回", "\"providerData\":{\"usage\":{\"outputTokens\":2}}"));
        Assert(Read(reader, path).Completions?.Count == 0, "pending tool prevents a completion even with terminal metadata");
        File.AppendAllText(path, "{\"type\":\"function_call_result\",\"callId\":\"tool\"}\n" + Message("final", "全部修改完成", "\"providerData\":{\"usage\":{\"outputTokens\":20}}"));
        var finished = Read(reader, path);
        Assert(!finished.IsBusy && finished.Completions?.Single().Id == "final" && finished.RecentResponse == "全部修改完成", "actual final changes idle and exposes only final text");
        Assert(Read(reader, path).Completions?.Count == 1, "unchanged final does not duplicate events");
        File.AppendAllText(path, "{\"type\":\"message\",\"role\":\"user\",\"content\":[]}\n" + Message("next-progress", "继续检查下一项"));
        var next = Read(reader, path);
        Assert(next.IsBusy && next.Completions?.Single().Id == "final", "new task never promotes its progress or loses previous final");

        foreach (var (label, metadata, expected) in new[]
        {
            ("raw usage", "\"providerData\":{\"rawUsage\":{\"completion_tokens\":7}}", true),
            ("message usage", "\"message\":{\"usage\":{\"output_tokens\":7}}", true),
            ("empty usage", "\"providerData\":{\"usage\":{}}", false),
            ("invalid usage", "\"providerData\":{\"usage\":{\"outputTokens\":\"7\"}}", false),
            ("null usage", "\"providerData\":null,\"message\":null", false)
        })
        {
            var fixture = Path.Combine(directory, label + ".jsonl");
            File.WriteAllText(fixture, Message(label, "答复", metadata));
            using var service = new WorkBuddyStatusService();
            var status = Read(service, fixture);
            Assert((status.Completions?.Count == 1) == expected && status.IsBusy != expected, label + " detection");
        }

        var largePath = Path.Combine(directory, "large-result.jsonl");
        var largeBody = new string('x', 300 * 1024);
        var largeCall = "{\"type\":\"function_call\",\"callId\":\"large-tool\",\"name\":\"Read\"}\n";
        var largeResult = JsonSerializer.Serialize(new { type = "function_call_result", output = largeBody, callId = "large-tool" }) + "\n";
        File.WriteAllText(largePath, largeCall + largeResult + Message("large-final", "任务结束", "\"providerData\":{\"usage\":{\"outputTokens\":20}}"));
        using var largeReader = new WorkBuddyStatusService();
        var largeFinished = Read(largeReader, largePath);
        Assert(!largeFinished.IsBusy && largeFinished.Completions?.Single().Id == "large-final", "oversized tool result clears pending call before actual final");
        var envelopes = JsonLineTailReader.Read(largePath, 1024 * 1024).ToArray();
        Assert(envelopes.All(line => line.Length < 4096), "large output is not materialized into returned status strings");
        Assert(!Read(largeReader, largePath).IsBusy, "cached completed large result remains idle");
        var streamingPath = Path.Combine(directory, "large-streaming.jsonl");
        File.WriteAllText(streamingPath, largeCall);
        using var streamingReader = new WorkBuddyStatusService();
        Assert(Read(streamingReader, streamingPath).IsBusy, "pending large tool remains busy before result");
        File.AppendAllText(streamingPath, largeResult.TrimEnd('\n'));
        Assert(Read(streamingReader, streamingPath).IsBusy, "unterminated oversized record cannot close a pending call");
        File.AppendAllText(streamingPath, "\n" + Message("still-commentary", "继续处理"));
        Assert(Read(streamingReader, streamingPath).IsBusy && Read(streamingReader, streamingPath).Completions?.Count == 0, "large result followed by commentary does not fake completion");
        File.AppendAllText(streamingPath, Message("stream-final", "结束", "\"message\":{\"usage\":{\"output_tokens\":10}}"));
        Assert(!Read(streamingReader, streamingPath).IsBusy, "incremental final after large result becomes idle");
        var malformedPath = Path.Combine(directory, "large-malformed.jsonl");
        File.WriteAllText(malformedPath, largeCall + largeResult.TrimEnd('\n', '}') + ",broken}\n" + Message("blocked-final", "不能作为完成", "\"providerData\":{\"usage\":{\"outputTokens\":20}}"));
        using var malformedReader = new WorkBuddyStatusService();
        Assert(Read(malformedReader, malformedPath).IsBusy && Read(malformedReader, malformedPath).Completions?.Count == 0, "malformed large result does not resolve a pending call");

        var app = new TestApp(); var main = new MainWindow(); // Never Show: no persistence or monitoring.
        typeof(MainWindow).GetField("_monitorStartedAt", Flags)!.SetValue(main, DateTimeOffset.UtcNow.AddMinutes(-1));
        typeof(MainWindow).GetField("_settings", Flags)!.SetValue(main, new IslandSettings());
        foreach (var field in new[] { "_yoyoInstalled", "_codexInstalled", "_workBuddyInstalled", "_deepSeekInstalled" }) typeof(MainWindow).GetField(field, Flags)!.SetValue(main, true);
        ((HashSet<string>)typeof(MainWindow).GetField("_suppressedProviders", Flags)!.GetValue(main)!).Clear();
        var yoyo = new YoyoStatus(false, false, false, null, null, "", false, null, []);
        var codex = new CodexStatus(false, false, false, false, null, null, null, null, null, null, null, null, 0, null, 0, 0, null, false, []);
        var detect = typeof(MainWindow).GetMethod("DetectCompletion", Flags)!;
        Assert(detect.Invoke(main, [yoyo, codex, progress]) is null, "notification baseline ignores commentary");
        Assert(detect.Invoke(main, [yoyo, codex, continuing]) is null, "intermediate text cannot reach island completion notification");
        Assert(detect.Invoke(main, [yoyo, codex, finished]) is not null, "confirmed final reaches island notification");
        Assert(detect.Invoke(main, [yoyo, codex, finished]) is null, "confirmed island notification deduplicated");
        var parallelRoot = Path.Combine(directory, "parallel-root");
        Directory.CreateDirectory(Path.Combine(parallelRoot, "projects"));
        File.WriteAllText(Path.Combine(parallelRoot, "projects", "done.jsonl"), Message("parallel-final", "一项已完成", "\"message\":{\"usage\":{\"output_tokens\":8}}"));
        File.WriteAllText(Path.Combine(parallelRoot, "projects", "busy.jsonl"), "{\"type\":\"function_call\",\"callId\":\"long\",\"name\":\"Bash\"}\n");
        var previousRoot = Environment.GetEnvironmentVariable("WORKBUDDY_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("WORKBUDDY_CONFIG_DIR", parallelRoot);
            using var aggregate = new WorkBuddyStatusService();
            var combined = aggregate.ReadAsync(true).GetAwaiter().GetResult();
            Assert(combined.IsBusy && combined.Completions?.Single().Id == "parallel-final", "aggregate retains other session busy and only actual completion");
            Assert(detect.Invoke(main, [yoyo, codex, combined]) is not null && combined.IsBusy, "parallel actual completion still notifies with busy headline");
        }
        finally { Environment.SetEnvironmentVariable("WORKBUDDY_CONFIG_DIR", previousRoot); }
        app.Shutdown();
    }

    static void Replay(string source, string directory)
    {
        var rows = File.ReadAllLines(source);
        var first = Array.FindIndex(rows, line =>
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            return root.TryGetProperty("role", out var role) && role.GetString() == "assistant"
                && root.TryGetProperty("content", out var content) && content.GetRawText().Contains("我收紧一点重做", StringComparison.Ordinal);
        });
        if (first < 0) throw new Exception("Reported progress record not found");
        var path = Path.Combine(directory, "replay.jsonl");
        File.WriteAllLines(path, rows.Take(first + 1));
        using var reader = new WorkBuddyStatusService();
        var progress = Read(reader, path);
        Assert(progress.IsBusy && progress.Completions?.Count == 0, "reported real progress checkpoint does not finish or notify");
        var finalIndex = Enumerable.Range(first + 1, rows.Length - first - 1).First(i =>
        {
            using var doc = JsonDocument.Parse(rows[i]); var root = doc.RootElement;
            return root.TryGetProperty("role", out var role) && role.GetString() == "assistant" && root.TryGetProperty("message", out var message) && message.TryGetProperty("usage", out _);
        });
        File.AppendAllLines(path, rows.Skip(first + 1).Take(finalIndex - first - 1));
        Assert(Read(reader, path).IsBusy && Read(reader, path).Completions?.Count == 0, "real tools after progress remain running without completion");
        File.AppendAllLines(path, [rows[finalIndex]]);
        var final = Read(reader, path);
        Assert(!final.IsBusy && final.Completions?.Count == 1 && final.RecentResponseId != progress.RecentResponseId, "reported real final checkpoint becomes idle with exactly one completion");
        File.AppendAllLines(path, rows.Skip(finalIndex + 1));
        var full = Read(reader, path);
        Assert(full.Completions?.All(item => item.Response != "字幕带上下边界稍微带了黑边，我收紧一点重做。") == true, "full replay never resurrects reported commentary");
    }
}
