namespace YoyoClawCompanion.Services;

internal sealed record NotificationTitleRule(string AppUserModelId, string Title, string Source = "")
{
    public bool Matches(SystemToast toast) => AppUserModelId == toast.AppUserModelId && Title == toast.Title.Trim();
    public static NotificationTitleRule From(SystemToast toast) => new(toast.AppUserModelId, toast.Title.Trim(), toast.Source);
}

// Only pending messages are retained. Views contain at most three rows, not one view per queued toast.
internal sealed class SystemNotificationQueue
{
    private readonly LinkedList<SystemToast> _items = new();
    internal const int Limit = 512;
    public int Count => _items.Count;
    public int DroppedCount { get; private set; }
    public static int BatchSize(int lines) => Math.Max(1, Math.Clamp(lines, 1, 6) / 2);

    public void Enqueue(SystemToast toast)
    {
        for (var node = _items.First; node is not null; node = node.Next)
            if (node.Value.Id == toast.Id) { node.Value = toast; return; }
        if (_items.Count == Limit) { _items.RemoveFirst(); DroppedCount++; }
        _items.AddLast(toast);
    }

    public SystemToast[] Take(int count)
    {
        var result = new List<SystemToast>(Math.Min(count, _items.Count));
        while (result.Count < count && _items.First is { } node) { result.Add(node.Value); _items.RemoveFirst(); }
        return result.ToArray();
    }

    public void Prepend(IEnumerable<SystemToast> toasts)
    {
        var batch = toasts.ToArray();
        foreach (var toast in batch) Remove(t => t.Id == toast.Id);
        foreach (var toast in batch.Reverse()) _items.AddFirst(toast);
        while (_items.Count > Limit) { _items.RemoveLast(); DroppedCount++; }
    }

    public void Remove(Func<SystemToast, bool> predicate)
    {
        for (var node = _items.First; node is not null;)
        {
            var next = node.Next;
            if (predicate(node.Value)) _items.Remove(node);
            node = next;
        }
    }

    public void Clear() { _items.Clear(); DroppedCount = 0; }
}
