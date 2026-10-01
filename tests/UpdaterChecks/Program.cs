using System.Net;
using System.Text.Json;
using YoyoClawCompanion.Services;

internal class Program
{
    static async Task Main()
    {
        var sha = "sha256:" + new string('a', 64);
        Assert(GitHubUpdateService.HasValidDigest(sha), "valid digest");
        Assert(!GitHubUpdateService.HasValidDigest("sha256:abc"), "short digest");
        Assert(!GitHubUpdateService.HasValidDigest("sha256:" + new string('z',64)), "non-hex digest");
        foreach (var (name, digest, tag, available) in new[] {
            ("AI-Dynamic-Island-v99.0.0-win-x64-portable.zip", sha, "v99.0.0", true),
            ("AI-Dynamic-Island-win-x64.zip", sha, "v99.0.0", false),
            ("AI-Dynamic-Island-v99.0.0-win-x64-portable.zip", "sha256:bad", "v99.0.0", false),
            ("AI-Dynamic-Island-v0.0.0-win-x64-portable.zip", sha, "v0.0.0", false) })
        {
            var json = JsonSerializer.Serialize(new { tag_name = tag, html_url = "https://github.com/test/releases", assets = new[] {new { name, digest, browser_download_url = "https://github.com/test/download.zip" }} });
            var service = new GitHubUpdateService(new HttpClient(new Stub(HttpStatusCode.OK, json)));
            await service.CheckAsync();
            Assert(service.Snapshot.UpdateAvailable == available && !service.Snapshot.IsBusy, name + "/" + digest[..10]);
        }
        var denied = new GitHubUpdateService(new HttpClient(new Stub(HttpStatusCode.NotFound, "{}")));
        await denied.CheckAsync();
        Assert(!denied.Snapshot.UpdateAvailable && !denied.Snapshot.IsBusy, "private/missing repository");
        var malformed = new GitHubUpdateService(new HttpClient(new Stub(HttpStatusCode.OK, "bad json")));
        await malformed.CheckAsync();
        Assert(!malformed.Snapshot.UpdateAvailable && !malformed.Snapshot.IsBusy, "malformed response");
        var manifest = JsonSerializer.Serialize(new { tag_name = "v99.0.0", html_url = "https://github.com/test/releases", assets = new[] { new { name = "AI-Dynamic-Island-v99.0.0-win-x64-portable.zip", digest = sha, browser_download_url = "https://github.com/test/download.zip" } } });
        var fallback = new GitHubUpdateService(new HttpClient(new LimitedApi(manifest)));
        await fallback.CheckAsync();
        Assert(fallback.Snapshot.UpdateAvailable, "rate limit manifest fallback");
        var marker = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        try
        {
            File.WriteAllText(marker, "installer marker");
            Assert(GitHubUpdateService.ExpectedAssetName("99.0.0").EndsWith("setup.exe"), "installed package selection");
            var setupJson = manifest.Replace("portable.zip", "setup.exe");
            var installed = new GitHubUpdateService(new HttpClient(new Stub(HttpStatusCode.OK, setupJson)));
            await installed.CheckAsync();
            Assert(installed.Snapshot.UpdateAvailable, "installed release available");
        }
        finally { File.Delete(marker); }
        if (Environment.GetEnvironmentVariable("ISLAND_LIVE_UPDATE_CHECK") == "1")
        {
            var live = new GitHubUpdateService();
            await live.CheckAsync();
            Console.WriteLine("LIVE " + live.Snapshot.Status);
            Assert(live.Snapshot.LatestVersion == "0.7.0", "anonymous GitHub release access");
            Assert(live.Snapshot.UpdateAvailable == (Version.Parse(GitHubUpdateService.CurrentVersion) < new Version(0, 7, 0)), "live version comparison");
        }
    }
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    sealed class Stub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }
    sealed class LimitedApi(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(request.RequestUri!.Host == "api.github.com" ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
namespace System.Windows
{
    internal sealed class Application { public static Application Current { get; } = new(); public void Shutdown() => throw new InvalidOperationException("Tests must not launch or install updates"); }
}
