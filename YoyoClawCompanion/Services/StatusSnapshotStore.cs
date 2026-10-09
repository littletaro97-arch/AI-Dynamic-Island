using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal static class StatusSnapshotStore
{
    public static void Write(object snapshot)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
        WriteTo(directory, snapshot);
    }

    internal static void WriteTo(string directory, object snapshot)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "status.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
        File.Move(temporary, path, true);
    }
}
