namespace YoyoClawCompanion.Services;

// One worker plus at most one pending immutable snapshot. A slow disk cannot grow a task queue.
internal sealed class LatestSnapshotQueue<T>(Action<T> write, Func<T, T, T>? selectLatest = null) where T : class
{
    private readonly object _gate = new();
    private T? _pending;
    private bool _running, _closed;
    private Task _worker = Task.CompletedTask;

    public Task Submit(T snapshot)
    {
        lock (_gate)
        {
            if (_closed) return _worker;
            _pending = _pending is null ? snapshot : selectLatest?.Invoke(_pending, snapshot) ?? snapshot;
            if (!_running)
            {
                _running = true;
                _worker = Task.Run(Drain);
            }
            return _worker;
        }
    }

    // Dropping queued diagnostic work on close never waits on the UI thread.
    public void Stop()
    {
        lock (_gate) { _closed = true; _pending = null; }
    }

    private void Drain()
    {
        while (true)
        {
            T? snapshot;
            lock (_gate)
            {
                snapshot = _pending;
                _pending = null;
                if (snapshot is null) { _running = false; return; }
            }
            try { write(snapshot); }
            catch { /* Best-effort persistence must not fault a fire-and-forget worker. */ }
        }
    }
}
