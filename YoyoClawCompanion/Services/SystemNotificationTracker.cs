namespace YoyoClawCompanion.Services;

internal sealed record SystemToast(string Id, string Source, string Title, string Body, DateTimeOffset CreatedAt)
{
    public string Text => string.Join("\n", new[] { Title, Body }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

internal sealed class SystemNotificationTracker
{
    private Dictionary<string, SystemToast>? _previous;
    private DateTimeOffset _startedAt;
    public void Reset() { _previous = null; _startedAt = DateTimeOffset.UtcNow; }
    public IReadOnlyList<SystemToast> Accept(IReadOnlyList<SystemToast> snapshot)
    {
        var current = snapshot.OrderByDescending(t => t.CreatedAt).Take(512).DistinctBy(t => t.Id).ToDictionary(t => t.Id);
        var newToasts = _previous is null ? [] : current.Values.Where(t => t.CreatedAt >= _startedAt
            && (!_previous.TryGetValue(t.Id, out var old) || old != t)
            && !t.Source.Contains("AI Dynamic Island", StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.CreatedAt).ToArray();
        _previous = current;
        return newToasts;
    }
}
