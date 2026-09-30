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

    internal static IEnumerable<string> Read(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var byteCount = (int)Math.Min(maxBytes, stream.Length);
        if (byteCount <= 0) yield break;

        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var fileStart = stream.Length - byteCount;
            stream.Seek(fileStart, SeekOrigin.Begin);
            var read = 0;
            while (read < byteCount)
            {
                var count = stream.Read(buffer, read, byteCount - read);
                if (count == 0) break;
                read += count;
            }

            var lineStart = 0;
            if (fileStart > 0)
            {
                while (lineStart < read && buffer[lineStart] != (byte)'\n') lineStart++;
                if (lineStart < read) lineStart++;
            }

            for (var index = lineStart; index <= read; index++)
            {
                if (index < read && buffer[index] != (byte)'\n') continue;
                var length = index - lineStart;
                if (length > 0 && buffer[lineStart + length - 1] == (byte)'\r') length--;
                if (length is > 0 and <= MaxStatusRecordBytes)
                {
                    var line = Encoding.UTF8.GetString(buffer, lineStart, length);
                    yield return lineStart == 0 ? line.TrimStart('\uFEFF') : line;
                }
                lineStart = index + 1;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
