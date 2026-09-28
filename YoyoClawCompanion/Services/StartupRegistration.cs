using System.IO;
using Microsoft.Win32;

namespace YoyoClawCompanion.Services;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIDynamicIsland";

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (!enabled)
            {
                if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return false;
            var command = $"\"{executable}\" --startup";
            if (!string.Equals(key.GetValue(ValueName)?.ToString(), command, StringComparison.Ordinal))
                key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
