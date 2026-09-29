using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoyoClawCompanion.Services;

// Derived from LittleTaro's YOYOClawCheckin v1.3 workflow.
// Personal-account use only; no proxy/batch check-in, rate-limit bypass, or commercial use.
// See THIRD_PARTY_NOTICES/YOYOClawCheckin-LICENSE.txt.
internal sealed record YoyoCheckinResult(bool ShouldNotify, bool Success, string Message);

internal sealed partial class YoyoCheckinService
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("https://all-scenario-device.rnd.honor.com"), Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string UserData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hclaw");
    private static readonly string LocalStatePath = Path.Combine(UserData, "Local State");
    private static readonly string SessionPath = Path.Combine(UserData, "billing", "session.bin");
    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YOYOClawCheckin");
    private static readonly string StatePath = Path.Combine(StateDirectory, "state.json");

    public async Task<YoyoCheckinResult> RunAfterNetworkAsync(string? rememberedExecutable, bool launchYoyoWhenNeeded, CancellationToken cancellationToken)
    {
        if (SignedTodayFromState()) return new(false, true, "今日已经签到");
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (!NetworkInterface.GetIsNetworkAvailable() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        if (!NetworkInterface.GetIsNetworkAvailable()) return new(true, false, "YOYO Claw 自动签到失败 · 等待联网超时");

        using var mutex = new Semaphore(1, 1, @"Local\YOYOClawCheckin.SingleRun");
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(0);
            if (!acquired) return new(false, true, "签到程序正在运行");
            if (launchYoyoWhenNeeded) EnsureYoyoRunning(rememberedExecutable);
            var session = await WaitForSessionAsync(cancellationToken);
            if (session is null) return new(true, false, "YOYO Claw 自动签到失败 · 未检测到有效登录状态");

            string? lastFailure = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try { return await CheckInOnceAsync(session, cancellationToken); }
                catch (HttpRequestException error) { lastFailure = error.Message; }
                catch (JsonException error) { lastFailure = $"服务端数据格式异常: {error.Message}"; }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { lastFailure = "网络请求超时"; }
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return new(true, false, $"YOYO Claw 自动签到失败 · {Short(lastFailure ?? "网络异常")}");
        }
        catch (OperationCanceledException) { return new(false, false, "签到已取消"); }
        catch (Exception error) { return new(true, false, $"YOYO Claw 自动签到异常 · {error.GetType().Name}"); }
        finally { if (acquired) try { mutex.Release(); } catch { } }
    }

    private static async Task<YoyoCheckinResult> CheckInOnceAsync(SessionInfo session, CancellationToken token)
    {
        var month = DateTime.Now.ToString("yyyyMM");
        using var calendar = await SendAsync(session, HttpMethod.Get, $"/yoyoclaw/points/sign-in/calendar?month={month}", null, token);
        if (calendar.StatusCode == HttpStatusCode.Unauthorized)
            return new(true, false, "YOYO Claw 自动签到失败 · 登录状态已失效");
        if (calendar.StatusCode != HttpStatusCode.OK || !TryData(calendar.Document.RootElement, out var calendarData))
            throw new HttpRequestException($"查询签到状态失败 ({(int)calendar.StatusCode})");
        if (SignedToday(calendarData))
        {
            WriteState(true, null, true);
            return new(false, true, "今日已经签到");
        }

        using var checkin = await SendAsync(session, HttpMethod.Post, "/yoyoclaw/points/sign-in", "{}", token);
        if (checkin.StatusCode != HttpStatusCode.OK || !TryData(checkin.Document.RootElement, out var checkinData))
            throw new HttpRequestException($"领取签到积分失败 ({(int)checkin.StatusCode})");
        var granted = checkinData.TryGetProperty("grantedPoints", out var grantedValue) && grantedValue.TryGetDouble(out var points) ? points : (double?)null;

        using var verify = await SendAsync(session, HttpMethod.Get, $"/yoyoclaw/points/sign-in/calendar?month={month}", null, token);
        var verified = verify.StatusCode == HttpStatusCode.OK && TryData(verify.Document.RootElement, out var verifyData) && SignedToday(verifyData);
        WriteState(verified, granted, false);
        return verified
            ? new(true, true, granted is double value ? $"YOYO Claw 签到成功 · 获得 {value:0.##} 积分" : "YOYO Claw 签到成功")
            : new(true, false, "YOYO Claw 已提交签到，但服务端复核尚未完成");
    }

    private static async Task<SessionInfo?> WaitForSessionAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt <= 20; attempt++)
        {
            var session = ReadSession();
            if (session is not null && session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return session;
            if (attempt < 20) await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
        return null;
    }

    private static SessionInfo? ReadSession()
    {
        try
        {
            if (!File.Exists(LocalStatePath) || !File.Exists(SessionPath)) return null;
            using var state = JsonDocument.Parse(File.ReadAllText(LocalStatePath));
            var encodedKey = state.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString();
            if (string.IsNullOrWhiteSpace(encodedKey)) return null;
            var protectedKey = Convert.FromBase64String(encodedKey);
            var key = Unprotect(protectedKey.AsSpan(Math.Min(5, protectedKey.Length)).ToArray());
            var raw = File.ReadAllBytes(SessionPath);
            if (raw.Length < 3 + 12 + 16) return null;
            var payload = raw.AsSpan(3);
            var nonce = payload[..12].ToArray();
            var ciphertext = payload[12..^16].ToArray();
            var tag = payload[^16..].ToArray();
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, ciphertext, tag, plaintext);
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement.TryGetProperty("session", out var session) ? session : document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var token) ? token.GetString() : null;
            var fingerprint = root.TryGetProperty("device_fingerprint", out var fp) ? fp.GetString() : null;
            var expiresText = root.TryGetProperty("expires_at", out var expires) ? expires.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken) || !FingerprintRegex().IsMatch(fingerprint ?? "")) return null;
            var expiresAt = DateTimeOffset.TryParse(expiresText, out var parsed) ? parsed : DateTimeOffset.MaxValue;
            return new(accessToken, fingerprint!, expiresAt);
        }
        catch { return null; }
    }

    private static void EnsureYoyoRunning(string? rememberedExecutable)
    {
        if (ApplicationLocator.IsProcessRunning("HnMagicClawUI")) return;
        var executable = ApplicationLocator.FindYoyoExecutable(rememberedExecutable);
        if (executable is null) return;
        try { Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = true }); } catch { }
    }

    private static async Task<ApiResponse> SendAsync(SessionInfo session, HttpMethod method, string path, string? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("X-HCLAW-Device-Fingerprint", session.DeviceFingerprint);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, token);
        var json = await response.Content.ReadAsStringAsync(token);
        return new(response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json));
    }

    private static bool TryData(JsonElement root, out JsonElement data)
    {
        if (root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Object) return true;
        data = default;
        return false;
    }

    private static bool SignedToday(JsonElement data)
    {
        var today = data.TryGetProperty("today", out var todayValue) ? todayValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(today)) return false;
        if (data.TryGetProperty("lastSignDate", out var last) && last.GetString() == today) return true;
        if (!data.TryGetProperty("signedRanges", out var ranges) || ranges.ValueKind != JsonValueKind.Array) return false;
        return ranges.EnumerateArray().Any(range =>
            range.TryGetProperty("startDate", out var start) && range.TryGetProperty("endDate", out var end)
            && string.CompareOrdinal(start.GetString(), today) <= 0 && string.CompareOrdinal(end.GetString(), today) >= 0);
    }

    private static bool SignedTodayFromState()
    {
        try
        {
            using var state = JsonDocument.Parse(File.ReadAllText(StatePath));
            return state.RootElement.TryGetProperty("date", out var date) && date.GetString() == DateTime.Today.ToString("yyyy-MM-dd")
                && state.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }
        catch { return false; }
    }

    private static void WriteState(bool ok, double? granted, bool alreadySigned)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            var value = new { date = DateTime.Today.ToString("yyyy-MM-dd"), ok, grantedPoints = granted, alreadySigned, at = DateTime.Now.ToString("s") };
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, StatePath, true);
        }
        catch { }
    }

    private static byte[] Unprotect(byte[] data)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.Data = Marshal.AllocHGlobal(data.Length);
            input.Length = data.Length;
            Marshal.Copy(data, 0, input.Data, data.Length);
            if (!CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    private static string Short(string value) => value.Length <= 80 ? value : value[..79] + "…";

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintRegex();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    private sealed record SessionInfo(string AccessToken, string DeviceFingerprint, DateTimeOffset ExpiresAt);
    private sealed record ApiResponse(HttpStatusCode StatusCode, JsonDocument Document) : IDisposable { public void Dispose() => Document.Dispose(); }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, string? description, IntPtr optionalEntropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
