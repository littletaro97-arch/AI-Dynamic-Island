using System.IO;

namespace YoyoClawCompanion.Services;

internal static class RecentSessionFiles
{
    // Keep only the newest bounded set while enumerating; sorting all historical files
    // used to allocate an unbounded collection twice on each Codex refresh.
    internal static FileInfo[] Find(string root, string pattern, int limit)
    {
        if (!Directory.Exists(root)) return [];
        var newest = new PriorityQueue<FileInfo, DateTime>();
        foreach (var file in new DirectoryInfo(root).EnumerateFiles(pattern, SearchOption.AllDirectories))
        {
            newest.Enqueue(file, file.LastWriteTimeUtc);
            if (newest.Count > limit) newest.Dequeue();
        }
        return newest.UnorderedItems.Select(item => item.Element).OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
    }
}
