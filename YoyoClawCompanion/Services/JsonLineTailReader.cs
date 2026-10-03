using System.Buffers;
using System.IO;
using System.Text;

namespace YoyoClawCompanion.Services;

/// <summary>
/// Reads a bounded JSONL tail without creating one large string for the whole file.
/// Oversized individual records are ignored because status probes never need binary/tool payloads.
/// </summary>
internal static class JsonLineTailReader
{
    private const int MaxStatusRecordBytes = 256 * 1024;

    internal static IEnumerable<string> Read(string path, int maxBytes, string[]? markers = null, JsonLineCursor? cursor = null)
    {
        var encodedMarkers = markers?.Select(Encoding.UTF8.GetBytes).ToArray();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var fileStart = cursor?.Prepare(stream, maxBytes) ?? Math.Max(0, stream.Length - maxBytes);
        var byteCount = (int)Math.Min(maxBytes, stream.Length - fileStart);
        if (byteCount <= 0) { cursor?.Advance(fileStart, 0); yield break; }

        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var skipPartialFirstLine = false;
            if (fileStart > 0)
            {
                stream.Position = fileStart - 1;
                skipPartialFirstLine = stream.ReadByte() != (byte)'\n';
            }
            stream.Seek(fileStart, SeekOrigin.Begin);
            var read = 0;
            while (read < byteCount)
            {
                var count = stream.Read(buffer, read, byteCount - read);
                if (count == 0) break;
                read += count;
            }

            var lineStart = 0;
            if (skipPartialFirstLine)
            {
                while (lineStart < read && buffer[lineStart] != (byte)'\n') lineStart++;
                if (lineStart < read) lineStart++;
            }

            var nextOffset = fileStart + lineStart;
            for (var index = lineStart; index <= read; index++)
            {
                if (index < read && buffer[index] != (byte)'\n') continue;
                if (index < read) nextOffset = fileStart + index + 1;
                var length = index - lineStart;
                if (length > 0 && buffer[lineStart + length - 1] == (byte)'\r') length--;
                if (length is > 0 and <= MaxStatusRecordBytes)
                {
                    if (encodedMarkers is not null && !ContainsMarker(buffer, lineStart, length, encodedMarkers))
                    {
                        lineStart = index + 1;
                        continue;
                    }
                    var line = Encoding.UTF8.GetString(buffer, lineStart, length);
                    yield return lineStart == 0 ? line.TrimStart('\uFEFF') : line;
                }
                lineStart = index + 1;
            }
            cursor?.Advance(nextOffset, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ContainsMarker(byte[] buffer, int start, int length, byte[][] markers)
    {
        foreach (var marker in markers)
            if (buffer.AsSpan(start, length).IndexOf(marker) >= 0) return true;
        return false;
    }
}
