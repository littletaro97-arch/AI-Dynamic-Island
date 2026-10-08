using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal static class CodexSessionSource
{
    // Source metadata precedes instructions. Never load the instruction/history body.
    internal static bool? IsGuardian(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[16 * 1024];
            var count = stream.Read(bytes);
            return Read(bytes.AsSpan(0, count));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static bool? Read(ReadOnlySpan<byte> header)
    {
        try
        {
            var reader = new Utf8JsonReader(header, isFinalBlock: false, state: default);
            var metadata = false;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                if (reader.CurrentDepth == 1 && reader.ValueTextEquals("type"))
                {
                    if (!reader.Read()) return null;
                    metadata = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("session_meta");
                    if (!metadata) return false; // Older fixtures/logs without a metadata header stay eligible.
                }
                else if (metadata && reader.CurrentDepth == 2 && reader.ValueTextEquals("source"))
                {
                    if (!reader.Read() || !JsonDocument.TryParseValue(ref reader, out var source)) return null;
                    using (source)
                    {
                        var root = source.RootElement;
                        return root.ValueKind == JsonValueKind.Object
                            && root.TryGetProperty("subagent", out var agent) && agent.ValueKind == JsonValueKind.Object
                            && agent.TryGetProperty("other", out var kind) && kind.ValueKind == JsonValueKind.String
                            && string.Equals(kind.GetString(), "guardian", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (JsonException) { }
        return null;
    }
}
