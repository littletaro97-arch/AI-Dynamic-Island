using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record UpdateSnapshot(string Status, bool IsBusy, bool UpdateAvailable, string CurrentVersion, string? LatestVersion, int Progress, string? ReleaseUrl);

internal sealed class GitHubUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/littletaro97-arch/AI-Dynamic-Island/releases/latest";
    private const string ReleaseManifestUrl = "https://raw.githubusercontent.com/littletaro97-arch/AI-Dynamic-Island/HEAD/docs/update.json";
    internal static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
    internal static string ExpectedAssetName(string version) => $"AI-Dynamic-Island-v{version}-win-x64-{(IsInstalled ? "setup.exe" : "portable.zip")}";
    private readonly HttpClient _client;
    private ReleaseAsset? _availableAsset;

    public GitHubUpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }) { }

    internal GitHubUpdateService(HttpClient client)
    {
        _client = client;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"AI-Dynamic-Island-Updater/{CurrentVersion}");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        Snapshot = new("尚未检查更新", false, false, CurrentVersion, null, 0, null);
    }

    public event EventHandler? Changed;
    public UpdateSnapshot Snapshot { get; private set; }
    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task CheckAsync()
    {
        if (Snapshot.IsBusy) return;
        Set(new("正在后台检查更新…", true, false, CurrentVersion, Snapshot.LatestVersion, 0, Snapshot.ReleaseUrl));
        try
        {
            using var apiResponse = await _client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead);
            using var manifestResponse = apiResponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                ? await _client.GetAsync(ReleaseManifestUrl, HttpCompletionOption.ResponseHeadersRead) : null;
            var response = manifestResponse ?? apiResponse;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                _availableAsset = null;
                Set(new("GitHub 暂时限制了更新请求，请稍后重试", false, false, CurrentVersion, null, 0, null));
                return;
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                _availableAsset = null;
                Set(new("当前仓库暂不可匿名检查；公开后即可使用", false, false, CurrentVersion, null, 0, null));
                return;
            }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v') ?? "0.0.0";
            var releaseUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;
            ReleaseAsset? asset = null;
            foreach (var item in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(item.GetProperty("name").GetString(), ExpectedAssetName(tag), StringComparison.OrdinalIgnoreCase)) continue;
                asset = new(tag, item.GetProperty("browser_download_url").GetString()!, item.TryGetProperty("digest", out var digest) ? digest.GetString() : null, releaseUrl);
                break;
            }
            var newer = Version.TryParse(tag, out var latest) && Version.TryParse(CurrentVersion, out var current) && latest > current;
            _availableAsset = newer ? asset : null;
            var status = !newer ? $"已是最新版本（{CurrentVersion}）"
                : asset is null ? $"发现 {tag}，但缺少 {ExpectedAssetName(tag)}"
                : !HasValidDigest(asset.Digest) ? $"发现 {tag}，但发布包缺少有效 SHA-256 摘要"
                : $"发现新版本 {tag}";
            Set(new(status, false, newer && asset is not null && HasValidDigest(asset.Digest), CurrentVersion, tag, 0, releaseUrl));
        }
        catch (Exception error)
        {
            _availableAsset = null;
            Set(new($"检查失败：{error.Message}", false, false, CurrentVersion, null, 0, null));
        }
    }

    public async Task DownloadAndInstallAsync()
    {
        var asset = _availableAsset;
        if (asset is null || Snapshot.IsBusy || !HasValidDigest(asset.Digest)) return;
        Set(Snapshot with { Status = "正在后台下载更新…", IsBusy = true, Progress = 0 });
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion", "updates", asset.Version);
            var zip = Path.Combine(root, ExpectedAssetName(asset.Version));
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(root);
            using (var response = await _client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long readTotal = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    readTotal += read;
                    if (total > 0) Set(Snapshot with { Progress = (int)Math.Clamp(readTotal * 100 / total.Value, 0, 100) });
                }
            }
            await using var packageStream = File.OpenRead(zip);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(packageStream));
            var expected = asset.Digest![7..].ToUpperInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
                throw new InvalidDataException("发布包 SHA-256 校验失败");
            if (IsInstalled)
            {
                var installer = Process.Start(new ProcessStartInfo(zip)
                {
                    UseShellExecute = true,
                    ArgumentList = { "/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "/NORESTART" }
                }) ?? throw new IOException("无法启动安装向导");
                System.Windows.Application.Current.Shutdown();
                return;
            }
            if (Directory.Exists(payload)) Directory.Delete(payload, true);
            ZipFile.ExtractToDirectory(zip, payload);
            var helper = Path.Combine(payload, Path.GetFileName(Environment.ProcessPath) ?? "YoyoClawCompanion.exe");
            if (!File.Exists(helper)) throw new InvalidDataException("发布包中缺少主程序");
            Set(Snapshot with { Status = "下载完成，正在应用更新…", Progress = 100 });
            Process.Start(new ProcessStartInfo(helper)
            {
                UseShellExecute = true,
                ArgumentList = { "--apply-update", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), Environment.ProcessId.ToString(), Path.GetFileName(helper) }
            });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Set(Snapshot with { Status = $"更新失败：{error.Message}", IsBusy = false });
        }
    }

    public static bool TryApplyUpdate(string[] args)
    {
        if (args.Length < 4 || !string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase)) return false;
        var changed = new List<(string Destination, string? Backup)>();
        try
        {
            var target = Path.GetFullPath(args[1]);
            if (!string.Equals(args[3], "YoyoClawCompanion.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新主程序名称无效");
            var sourceRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (target.TrimEnd(Path.DirectorySeparatorChar).Equals(sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新暂存目录不能是目标目录");
            var backupRoot = Path.Combine(Path.GetDirectoryName(sourceRoot)!, "backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            if (int.TryParse(args[2], out var pid))
            {
                try
                {
                    if (!Process.GetProcessById(pid).WaitForExit(60000))
                        throw new IOException("旧版本仍在运行，未替换文件");
                }
                catch (ArgumentException) { }
            }
            foreach (var directory in Directory.EnumerateDirectories(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(AppContext.BaseDirectory, directory)));
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(AppContext.BaseDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? backup = null;
                if (File.Exists(destination))
                {
                    backup = Path.Combine(backupRoot, Path.GetRelativePath(AppContext.BaseDirectory, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, false);
                }
                changed.Add((destination, backup));
                File.Copy(file, destination, true);
            }
            Process.Start(new ProcessStartInfo(Path.Combine(target, args[3])) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            foreach (var (destination, backup) in Enumerable.Reverse(changed))
            {
                try
                {
                    if (backup is not null) File.Copy(backup, destination, true);
                    else File.Delete(destination);
                }
                catch { /* Keep the backup for manual recovery; log the update error below. */ }
            }
            try
            {
                var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion", "update.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}");
            }
            catch { }
        }
        return true;
    }

    private void Set(UpdateSnapshot value) { Snapshot = value; Changed?.Invoke(this, EventArgs.Empty); }
    internal static bool HasValidDigest(string? digest) => digest is not null
        && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
        && digest.Length == 71 && digest[7..].All(Uri.IsHexDigit);
    private sealed record ReleaseAsset(string Version, string DownloadUrl, string? Digest, string? ReleaseUrl);
}
