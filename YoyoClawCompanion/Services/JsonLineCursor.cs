using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace YoyoClawCompanion.Services;

// Offset follows complete lines; an unfinished UTF-8/JSON record is reread on append.
// File identity, shrinking and an append anchor detect rotation and in-place rewrite.
internal sealed class JsonLineCursor
{
    private string? _path;
    private (uint Volume, ulong Index) _identity;
    private long _length, _offset;
    private byte[] _anchor = [];
    internal bool NewGeneration { get; private set; }
    internal bool Rebuild { get; private set; }
    internal long BytesRead { get; private set; }

    internal string[] Read(string path, int maxBytes, string[]? markers = null)
        => JsonLineTailReader.Read(path, maxBytes, markers, this).ToArray();

    internal long Prepare(FileStream stream, int maxBytes)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var information))
            throw new IOException("Cannot identify session log", Marshal.GetLastWin32Error());
        var identity = (information.Volume, ((ulong)information.IndexHigh << 32) | information.IndexLow);
        var length = stream.Length;
        NewGeneration = _path != stream.Name || _identity != identity || length < _length;
        if (!NewGeneration && _anchor.Length > 0)
        {
            stream.Position = _length - _anchor.Length;
            Span<byte> anchor = stackalloc byte[64];
            var read = stream.Read(anchor[.._anchor.Length]);
            NewGeneration = read != _anchor.Length || !anchor[..read].SequenceEqual(_anchor);
        }
        Rebuild = NewGeneration || length - _offset > maxBytes;
        var start = Rebuild ? Math.Max(0, length - maxBytes) : _offset;
        _path = stream.Name;
        _identity = identity;
        _length = length;
        var anchorLength = (int)Math.Min(length, 64);
        _anchor = new byte[anchorLength];
        stream.Position = length - anchorLength;
        stream.ReadExactly(_anchor);
        BytesRead = 0;
        return start;
    }

    internal void Advance(long offset, int bytesRead)
    { _offset = offset; BytesRead = bytesRead; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
