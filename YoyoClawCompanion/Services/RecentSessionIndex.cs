using System.Collections.Concurrent;
using System.IO;

namespace YoyoClawCompanion.Services;

// Watcher events are hints. Poll selected files and rescan history every minute,
// also on overflow, so missing notifications cannot permanently hide a session.
internal sealed class RecentSessionIndex(string root, string pattern) : IDisposable
{
    private const int Capacity = 256;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, byte> _changed = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FileInfo> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _revisions = new(StringComparer.OrdinalIgnoreCase);
    private long _revision;
    private FileSystemWatcher? _watcher;
    private DateTime _lastScan;
    private int _forceScan = 1;
    private int _watcherFailed;
    private bool _disposed;
    internal int FullScanCount { get; private set; }
    internal bool IsWatching => _watcher is not null;
    internal void Invalidate() => Interlocked.Exchange(ref _forceScan, 1);
    internal long RevisionFor(string path) { lock (_gate) return _revisions.GetValueOrDefault(path); }

    internal FileInfo[] Find(int limit, DateTime? utcNow = null)
    {
        lock (_gate)
        {
            if (_disposed) return [];
            if (Interlocked.Exchange(ref _watcherFailed, 0) != 0)
            { _watcher?.Dispose(); _watcher = null; Invalidate(); }
            var now = utcNow ?? DateTime.UtcNow;
            if (Interlocked.Exchange(ref _forceScan, 0) != 0 || now - _lastScan >= TimeSpan.FromMinutes(1))
            {
                EnsureWatcher(); // Register before scanning; queue concurrent writes.
                _files = RecentSessionFiles.Find(root, pattern, Math.Max(Capacity, limit))
                    .ToDictionary(file => file.FullName, StringComparer.OrdinalIgnoreCase);
                // Verify identities after the fallback scan even when a replacement
                // preserves both file size and timestamp.
                foreach (var path in _files.Keys) _revisions[path] = ++_revision;
                _lastScan = now;
                FullScanCount++;
            }
            foreach (var path in _changed.Keys)
                if (_changed.TryRemove(path, out _)) { Refresh(path); _revisions[path] = ++_revision; }
            foreach (var path in _files.Values.OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(limit).Select(file => file.FullName).ToArray()) Refresh(path);
            var ordered = _files.Values.OrderByDescending(file => file.LastWriteTimeUtc).Take(Math.Max(Capacity, limit)).ToArray();
            _files = ordered.ToDictionary(file => file.FullName, StringComparer.OrdinalIgnoreCase);
            foreach (var path in _revisions.Keys.Where(path => !_files.ContainsKey(path)).ToArray()) _revisions.Remove(path);
            return ordered.Take(limit).ToArray();
        }
    }

    private void Refresh(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists) _files[path] = file;
            else _files.Remove(path);
        }
        catch (IOException) { Invalidate(); }
        catch (UnauthorizedAccessException) { Invalidate(); }
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null) return;
        if (!Directory.Exists(root)) { Invalidate(); return; }
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new(root, pattern)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (_, e) => { Queue(e.OldFullPath); Queue(e.FullPath); Invalidate(); };
            watcher.Error += (_, _) => { Interlocked.Exchange(ref _watcherFailed, 1); Invalidate(); };
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { watcher?.Dispose(); }
    }

    private void Changed(object sender, FileSystemEventArgs e) => Queue(e.FullPath);
    private void Queue(string path)
    {
        if (_changed.Count >= 1024) { Invalidate(); return; }
        _changed.TryAdd(path, 0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _files.Clear();
            _revisions.Clear();
            _changed.Clear();
        }
    }
}
