using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record DeepSeekStatus(bool IsRunning, bool Available, string State, string? ConfirmationId = null,
    IReadOnlyList<TaskCompletionEvent>? Completions = null, string? Error = null)
{
    internal bool IsBusy => IsRunning && State is "running" or "decision";
    internal bool RequiresConfirmation => IsRunning && Available && State == "decision";
}

internal sealed class DeepSeekStatusService : IDisposable
{
    private readonly RecentSessionIndex _sessionIndex = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "sessions"), "session.v4.jsonl.zstd");
    public void Dispose() => _sessionIndex.Dispose();
    private string? _fingerprint;
    private DeepSeekStatus _cached = new(false, false, "unknown");
    private DateTimeOffset _retryAt;
    internal void ResetCache() { _fingerprint = null; _retryAt = default; _sessionIndex.Invalidate(); }

    internal async Task<DeepSeekStatus> ReadAsync(string? executable, bool? isRunning = null)
    {
        if (!(isRunning ?? ApplicationLocator.IsProcessRunning("DeepSeek Harness"))) return _cached with { IsRunning = false };
        try
        {
            var files = _sessionIndex.Find(24);
            var fingerprint = string.Join('|', files.Select(file => $"{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}:{_sessionIndex.RevisionFor(file.FullName)}"));
            if (_fingerprint == fingerprint && ((_cached.Available && !_cached.IsBusy && _cached.State != "unknown") || DateTimeOffset.UtcNow < _retryAt)) return _cached;
            var node = Path.Combine(Path.GetDirectoryName(executable) ?? "", "resources", "runtime", "primary-runtime", "dependencies", "node", "bin", "node.exe");
            var script = Path.Combine(AppContext.BaseDirectory, "harness-bridge", "status.mjs");
            if (!File.Exists(node) || !File.Exists(script)) return new(true, false, "unknown", Error: "缺少支持 Zstd 的 Harness 内置运行时或检测脚本");
            var start = new ProcessStartInfo(node) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--no-warnings"); start.ArgumentList.Add(script);
            foreach (var file in files) start.ArgumentList.Add(file.FullName);
            using var process = Process.Start(start) ?? throw new IOException("bridge-start");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(); } catch { } throw; }
            await stderr;
            using var json = JsonDocument.Parse(await output);
            if (process.ExitCode != 0) throw new IOException("bridge-exit");
            var data = json.RootElement;
            var completions = data.GetProperty("completions").EnumerateArray().Select(item => new TaskCompletionEvent(
                item.GetProperty("id").GetString()!, item.GetProperty("response").GetString()!, item.GetProperty("completedAt").GetDateTimeOffset())).ToArray();
            _cached = new(true, data.GetProperty("available").GetBoolean(), data.GetProperty("state").GetString()!,
                data.GetProperty("confirmationId").GetString(), completions, data.GetProperty("error").GetString());
            _fingerprint = fingerprint;
            _retryAt = DateTimeOffset.UtcNow.AddSeconds(30);
            return _cached;
        }
        catch (Exception error) { return new(true, false, "unknown", Error: error.GetType().Name); }
    }
}
