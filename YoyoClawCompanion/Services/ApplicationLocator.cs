using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace YoyoClawCompanion.Services;

internal static class ApplicationLocator
{
    internal readonly record struct ProviderPresence(bool Codex, bool WorkBuddy, bool Yoyo, bool DeepSeek);

    internal static ProviderPresence CaptureProviderPresence()
    {
        var codex = false; var workBuddy = false; var yoyo = false; var deepSeek = false;
        // Toolhelp supplies names/IDs without allocating a managed Process plus
        // thread/module metadata for every unrelated process on the computer.
        using var snapshot = CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (snapshot.IsInvalid || !Process32First(snapshot, ref entry))
        {
            var candidates = Process.GetProcessesByName("ChatGPT");
            try { codex = candidates.Any(IsCodexDesktopProcess); }
            finally { foreach (var process in candidates) process.Dispose(); }
            return new(codex, IsProcessRunning("WorkBuddy"), IsProcessRunning("HnMagicClawUI"), IsProcessRunning("DeepSeek Harness"));
        }
        do
        {
            var name = entry.Executable;
            if (name.Equals("WorkBuddy.exe", StringComparison.OrdinalIgnoreCase)) workBuddy = true;
            else if (name.Equals("HnMagicClawUI.exe", StringComparison.OrdinalIgnoreCase)) yoyo = true;
            else if (name.Equals("DeepSeek Harness.exe", StringComparison.OrdinalIgnoreCase)) deepSeek = true;
            else if (name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) && !codex)
            {
                try
                {
                    using var process = Process.GetProcessById((int)entry.ProcessId);
                    codex = IsCodexDesktopProcess(process);
                }
                catch (ArgumentException) { } // Process exited during snapshot.
            }
        }
        while (Process32Next(snapshot, ref entry));
        return new(codex, workBuddy, yoyo, deepSeek);
    }

    private static bool IsCodexDesktopProcess(Process process)
    {
        try { return process.MainModule?.FileName?.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true; }
        catch { try { return process.MainWindowTitle.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);

    public static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static string? FindRunningExecutable(string processName, Func<Process, bool>? predicate = null)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (predicate?.Invoke(process) == false) continue;
                    var path = process.MainModule?.FileName;
                    if (IsExecutable(path)) return Path.GetFullPath(path!);
                }
                catch { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return null;
    }

    public static string? FindYoyoExecutable(string? rememberedPath)
    {
        var running = FindRunningExecutable("HnMagicClawUI");
        if (running is not null) return running;
        if (IsExpectedExecutable(rememberedPath, "HnMagicClawUI.exe")) return Path.GetFullPath(rememberedPath!);
        var registered = FindAppPath("HnMagicClawUI.exe") ?? FindUninstallExecutable("HnMagicClawUI.exe", "MagicClaw", "YOYO");
        if (registered is not null) return registered;

        foreach (var root in CandidateDirectories("HONOR", "MagicClaw"))
        {
            var resolved = ResolveYoyoCurrent(root);
            if (resolved is not null) return resolved;
        }
        return null;
    }

    public static string? FindWorkBuddyExecutable(string? rememberedPath)
    {
        var running = FindRunningExecutable("WorkBuddy");
        if (running is not null) return running;
        if (IsExpectedExecutable(rememberedPath, "WorkBuddy.exe")) return Path.GetFullPath(rememberedPath!);
        var registered = FindAppPath("WorkBuddy.exe") ?? FindUninstallExecutable("WorkBuddy.exe", "WorkBuddy");
        if (registered is not null) return registered;

        foreach (var root in CandidateDirectories("WorkBuddy"))
        {
            var candidate = Path.Combine(root, "WorkBuddy.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static string? FindCodexDesktopExecutable(string? rememberedPath, Func<Process, bool> predicate)
    {
        var running = FindRunningExecutable("ChatGPT", predicate);
        if (running is not null) return running;
        if (IsExpectedExecutable(rememberedPath, "ChatGPT.exe")) return Path.GetFullPath(rememberedPath!);
        return FindAppPath("ChatGPT.exe");
    }

    public static bool IsCodexDesktopInstalled(string? rememberedPath, Func<Process, bool> predicate)
        => FindCodexDesktopExecutable(rememberedPath, predicate) is not null || HasPackagedApp("OpenAI.Codex_");

    public static bool IsExpectedProviderExecutable(string provider, string? path)
        => provider.ToLowerInvariant() switch
        {
            "yoyo" => IsExpectedExecutable(path, "HnMagicClawUI.exe"),
            "codex" => IsExpectedExecutable(path, "ChatGPT.exe"),
            "workbuddy" => IsExpectedExecutable(path, "WorkBuddy.exe"),
            "deepseek" => IsExpectedExecutable(path, "DeepSeek Harness.exe"),
            _ => false
        };

    public static string? FindDeepSeekExecutable(string? rememberedPath)
        => FindRunningExecutable("DeepSeek Harness")
            ?? (IsExpectedExecutable(rememberedPath, "DeepSeek Harness.exe") ? Path.GetFullPath(rememberedPath!) : null)
            ?? FindAppPath("DeepSeek Harness.exe")
            ?? FindUninstallExecutable("DeepSeek Harness.exe", "DeepSeek Harness");

    public static string? FindCodexCliExecutable()
    {
        var running = FindRunningExecutable("codex");
        if (running is not null) return running;
        var pathExecutable = FindOnPath("codex.exe");
        if (pathExecutable is not null) return pathExecutable;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var candidate in new[]
        {
            Path.Combine(local, "Microsoft", "WindowsApps", "codex.exe"),
            Path.Combine(local, "OpenAI", "Codex", "bin", "codex.exe")
        }) if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        try
        {
            var root = Path.Combine(local, "OpenAI", "Codex", "bin");
            return Directory.Exists(root)
                ? new DirectoryInfo(root).EnumerateFiles("codex.exe", SearchOption.AllDirectories).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
        }
        catch { return null; }
    }

    private static string? ResolveYoyoCurrent(string root)
    {
        try
        {
            var currentFile = Path.Combine(root, "current.json");
            if (File.Exists(currentFile))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(currentFile));
                if (document.RootElement.TryGetProperty("path", out var value))
                {
                    var configured = value.GetString();
                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        var resolved = Path.IsPathFullyQualified(configured) ? Path.GetFullPath(configured) : Path.GetFullPath(Path.Combine(root, configured));
                        if (IsExpectedExecutable(resolved, "HnMagicClawUI.exe")) return resolved;
                    }
                }
            }
            var launcher = Path.Combine(root, "HnMagicClawUI.exe");
            return File.Exists(launcher) ? Path.GetFullPath(launcher) : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> CandidateDirectories(params string[] segments)
    {
        var bases = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        foreach (var basePath in bases.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(new[] { basePath }.Concat(segments).ToArray());
            if (segments.Length == 1) yield return Path.Combine(basePath, "Programs", segments[0]);
        }
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), fileName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }
        return null;
    }

    private static string? FindAppPath(string fileName)
    {
        const string prefix = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(prefix + fileName);
                var path = CleanRegistryPath(key?.GetValue(null)?.ToString());
                if (IsExecutable(path)) return Path.GetFullPath(path!);
            }
            catch { }
        }
        return null;
    }

    private static string? FindUninstallExecutable(string fileName, params string[] productNames)
    {
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(uninstall);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var item = root.OpenSubKey(name);
                    var displayName = item?.GetValue("DisplayName")?.ToString() ?? "";
                    if (!productNames.Any(product => displayName.Contains(product, StringComparison.OrdinalIgnoreCase))) continue;
                    var icon = CleanRegistryPath(item?.GetValue("DisplayIcon")?.ToString());
                    if (IsExpectedExecutable(icon, fileName)) return Path.GetFullPath(icon!);
                    var location = item?.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        var candidate = Path.Combine(location, fileName);
                        if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private static bool HasPackagedApp(string packagePrefix)
    {
        const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(packages);
            return root?.GetSubKeyNames().Any(name => name.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch { return false; }
    }

    private static string? CleanRegistryPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim().Trim('"');
        var comma = path.LastIndexOf(',');
        if (comma > 2 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma].Trim().Trim('"');
        return Environment.ExpandEnvironmentVariables(path);
    }

    private static bool IsExecutable(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path);
    private static bool IsExpectedExecutable(string? path, string fileName) => IsExecutable(path) && string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase);
}
