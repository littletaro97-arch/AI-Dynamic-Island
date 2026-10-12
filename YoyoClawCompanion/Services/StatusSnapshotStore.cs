using System.IO;
using System.Text.Json;

namespace YoyoClawCompanion.Services;

internal static class StatusSnapshotStore
{
    internal static event Action<string>? Published;
    public static void Write(object snapshot)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
        var json = JsonSerializer.Serialize(snapshot);
        Published?.Invoke(json);
        WriteJson(directory, json);
    }

    internal static void WriteTo(string directory, object snapshot)
    {
        WriteJson(directory, JsonSerializer.Serialize(snapshot));
    }

    private static void WriteJson(string directory, string json)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "status.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
    }
}
