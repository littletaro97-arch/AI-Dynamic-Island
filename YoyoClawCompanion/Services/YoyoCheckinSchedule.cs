namespace YoyoClawCompanion.Services;

// Driven by the existing status refresh; no timer or disk access on each tick.
internal sealed class YoyoCheckinSchedule
{
    private DateOnly? _day;
    private bool _running, _finished;
    private DateTimeOffset _retryAt;
    public int Attempts { get; private set; }

    public bool TryStart(DateTimeOffset now)
    {
        if (_running) return false;
        var day = DateOnly.FromDateTime(now.DateTime);
        if (_day != day)
        {
            _day = day;
            Attempts = 0;
            _finished = false;
            _retryAt = DateTimeOffset.MinValue;
        }
        if (_finished || Attempts >= 3 || now < _retryAt) return false;
        Attempts++;
        _running = true;
        return true;
    }

    public void Complete(DateTimeOffset now, bool success)
    {
        _running = false;
        // A run finishing after midnight must not mark the new day as completed.
        if (_day != DateOnly.FromDateTime(now.DateTime)) return;
        _finished = success;
        _retryAt = now.AddMinutes(Attempts == 1 ? 5 : 30);
    }
}
