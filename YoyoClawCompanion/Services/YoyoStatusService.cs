using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record YoyoStatus(
    bool IsYoyoRunning,
    bool TaskStatusAvailable,
    bool IsBusy,
    double? RemainingPoints,
    double? TotalPoints,
    string RecentResult,
    bool LastTaskFailed,
    DateTimeOffset? RecentUpdatedAt,
    IReadOnlyList<TaskCompletionEvent>? Completions = null);

internal sealed class YoyoStatusService
{
    private readonly YoyoQuotaService _quota = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private MagicoreSnapshot? _cachedSnapshot;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    internal void ResetCache()
    {
        _cachedSnapshot = null;
        _cachedAt = DateTimeOffset.MinValue;
    }

    public async Task<YoyoStatus> ReadAsync(bool? isRunning = null)
    {
        var running = isRunning ?? ApplicationLocator.IsProcessRunning("HnMagicClawUI");
        // Local files and the bounded billing-log regex must not block WPF's animation thread.
        var points = await Task.Run(_quota.Read);
        var snapshot = running ? await ReadMagicoreAsync() : _cachedSnapshot;
        var updatedAt = ParseDate(snapshot?.UpdatedAt);
        var recentFailure = snapshot?.LastTaskFailed == true
            && updatedAt is not null
            && DateTimeOffset.UtcNow - updatedAt.Value <= TimeSpan.FromMinutes(30);
        return new YoyoStatus(
            running,
            snapshot?.Ok == true,
            running && snapshot?.Busy == true,
            points.Remaining,
            points.Total,
            snapshot?.Ok == true ? Normalize(snapshot.RecentResult) : "任务状态不可用",
            recentFailure,
            updatedAt,
            snapshot?.Completions);
    }

    private async Task<MagicoreSnapshot?> ReadMagicoreAsync()
    {
        if (_cachedSnapshot is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(5)) return _cachedSnapshot;
        await _refreshLock.WaitAsync();
        try
        {
            if (_cachedSnapshot is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(5)) return _cachedSnapshot;
            _cachedSnapshot = await InvokeBridgeAsync();
            _cachedAt = DateTimeOffset.UtcNow;
            return _cachedSnapshot;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<MagicoreSnapshot?> InvokeBridgeAsync()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "magicore-bridge", "status.mjs");
        var node = FindNode();
        if (!File.Exists(script))
        {
            WriteDiagnostic($"bridge-missing: {script}");
            return null;
        }
        if (node is null)
        {
            WriteDiagnostic("node-missing");
            return null;
        }

        Process? process = null;
        string? resultPath = null;
        try
        {
            var resultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
            Directory.CreateDirectory(resultDirectory);
            resultPath = Path.Combine(resultDirectory, $"magicore-status-{Environment.ProcessId}.json");
            var startInfo = new ProcessStartInfo
            {
                FileName = node,
                WorkingDirectory = Path.GetDirectoryName(script)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add(resultPath);
            process = Process.Start(startInfo);
            if (process is null) return null;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            var payload = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath, timeout.Token) : output;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(payload))
            {
                WriteDiagnostic($"bridge-exit={process.ExitCode}; {error}");
                return null;
            }
            var snapshot = ParseSnapshot(payload);
            if (snapshot?.Ok != true) WriteDiagnostic("bridge-returned-unavailable");
            return snapshot;
        }
        catch (Exception error)
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
            WriteDiagnostic($"{error.GetType().Name}: {error.Message}");
            return null;
        }
        finally
        {
            process?.Dispose();
            try { if (resultPath is not null && File.Exists(resultPath)) File.Delete(resultPath); } catch { }
        }
    }

    private static MagicoreSnapshot? ParseSnapshot(string output)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var candidate = line.Trim();
            if (!candidate.StartsWith('{') || !candidate.EndsWith('}')) continue;
            try
            {
                var snapshot = JsonSerializer.Deserialize<MagicoreSnapshot>(candidate, options);
                if (snapshot is not null) return snapshot;
            }
            catch (JsonException) { }
        }
        WriteDiagnostic("bridge-json-not-found");
        return null;
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "diagnostics.log"), $"{DateTimeOffset.Now:O} {Normalize(message)}{Environment.NewLine}");
        }
        catch { }
    }

    private static string? FindNode()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(installed)) return installed;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "node.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "尚未检测到任务记录";
        var oneLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 600 ? oneLine : oneLine[..599] + "…";
    }

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed record MagicoreSnapshot(bool Ok, bool Busy, string? RecentResult, bool LastTaskFailed, string? UpdatedAt, IReadOnlyList<TaskCompletionEvent>? Completions = null);
}
