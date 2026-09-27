using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal sealed record WorkBuddyCredits(bool Available, double? Remaining, double? Total);

internal sealed class WorkBuddyCreditsService
{
    private readonly string _discoveryPath = Path.Combine(ProductPaths.WorkBuddyConfigDirectory, "wbipc", "endpoint.json");
    private DateTimeOffset _lastRead;
    private WorkBuddyCredits _cached = new(false, null, null);
    internal string? LastError { get; private set; }

    public async Task<WorkBuddyCredits> ReadAsync(bool enabled)
    {
        if (!enabled) return new(false, null, null);
        if (DateTimeOffset.Now - _lastRead < TimeSpan.FromSeconds(60)) return _cached;
        _lastRead = DateTimeOffset.Now;
        try { _cached = await ReadFromHostAsync(); if (_cached.Available) LastError = null; }
        catch (Exception error) { LastError = $"{LastError}:{error.GetType().Name}"; _cached = new(false, null, null); }
        return _cached;
    }

    private async Task<WorkBuddyCredits> ReadFromHostAsync()
    {
        LastError = "discovery";
        using var discovery = JsonDocument.Parse(await File.ReadAllTextAsync(_discoveryPath));
        var endpoint = discovery.RootElement.GetProperty("endpoint").GetString() ?? "";
        var ticket = discovery.RootElement.GetProperty("ticket").GetString() ?? "";
        const string prefix = @"\\.\pipe\";
        if (!endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(ticket)) return new(false, null, null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var pipe = new NamedPipeClientStream(".", endpoint[prefix.Length..], PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, true) { AutoFlush = true, NewLine = "\n" };

        var clientNonce = Base64Url(RandomNumberGenerator.GetBytes(16));
        var ticketId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket))).ToLowerInvariant()[..16];
        await WriteAsync(writer, new { type = "session_hello", protocol_min = 1, protocol_max = 1, client_nonce = clientNonce, ticket_id = ticketId, client = new { kind = "skill", id = "library-skills", version = "0.7" } });
        LastError = "challenge";
        using var challengeDoc = JsonDocument.Parse(await ReadLineAsync(reader, timeout.Token));
        var challenge = challengeDoc.RootElement;
        if (Text(challenge, "type") != "session_challenge") return new(false, null, null);
        var serverNonce = Text(challenge, "server_nonce");
        var expected = Proof(ticket, "server", endpoint, clientNonce, serverNonce);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(Text(challenge, "server_proof")))) return new(false, null, null);
        await WriteAsync(writer, new { type = "session_prove", client_proof = Proof(ticket, "client", endpoint, clientNonce, serverNonce) });
        LastError = "ack";
        using var ackDoc = JsonDocument.Parse(await ReadLineAsync(reader, timeout.Token));
        if (Text(ackDoc.RootElement, "type") != "session_hello_ack") return new(false, null, null);

        LastError = "bind";
        await WriteAsync(writer, new { jsonrpc = "2.0", id = 1, method = "broker/GetPipe", @params = new { pipe = "wb.request" } });
        using var pipeDoc = JsonDocument.Parse(await ReadResponseAsync(reader, 1, timeout.Token));
        var result = pipeDoc.RootElement.GetProperty("result");
        var channel = Text(result, "channel");
        if (string.IsNullOrWhiteSpace(channel)) return new(false, null, null);

        LastError = "fetch";
        await WriteAsync(writer, new
        {
            jsonrpc = "2.0", id = 2, method = $"{channel}/http.fetch", mode = "call",
            @params = new { method = "POST", path = "/billing/meter/get-user-resource-summary", headers = new Dictionary<string, string> { ["accept"] = "*/*", ["content-type"] = "application/json" }, body_b64 = "e30=" }
        });
        using var responseDoc = JsonDocument.Parse(await ReadResponseAsync(reader, 2, timeout.Token));
        LastError = "parse";
        if (!responseDoc.RootElement.TryGetProperty("result", out var fetchResult))
        {
            var code = responseDoc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var value) ? value.ToString() : "unknown";
            LastError = $"fetch-rejected-{code}";
            return new(false, null, null);
        }
        var body64 = Text(fetchResult, "body_b64");
        using var body = JsonDocument.Parse(Convert.FromBase64String(body64));
        var data = body.RootElement.TryGetProperty("data", out var envelope) ? envelope : body.RootElement;
        if (!data.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array) return new(false, null, null);
        double remaining = 0, total = 0;
        foreach (var package in packages.EnumerateArray())
        {
            if (!string.Equals(Text(package, "CapacityUnit"), "credits", StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(Text(package, "CycleRemainCapacity"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var left)) remaining += left;
            if (double.TryParse(Text(package, "CycleTotalCapacity"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var capacity)) total += capacity;
        }
        return total > 0 ? new(true, remaining, total) : new(false, null, null);
    }

    private static async Task<string> ReadResponseAsync(StreamReader reader, int id, CancellationToken token)
    {
        while (true)
        {
            var line = await ReadLineAsync(reader, token);
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var value) && value.TryGetInt32(out var responseId) && responseId == id) return line;
        }
    }

    private static async Task<string> ReadLineAsync(StreamReader reader, CancellationToken token) => await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
    private static Task WriteAsync(StreamWriter writer, object value) => writer.WriteLineAsync(JsonSerializer.Serialize(value));
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Proof(string ticket, string role, string endpoint, string clientNonce, string serverNonce)
    {
        using var stream = new MemoryStream();
        var length = new byte[4];
        foreach (var part in new[] { role == "server" ? "wbipc-s" : "wbipc-c", "1", endpoint, clientNonce, serverNonce })
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            stream.Write(length); stream.Write(bytes);
        }
        return Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ticket), stream.ToArray()));
    }
}
