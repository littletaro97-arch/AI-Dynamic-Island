using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YoyoClawCompanion.Services;

internal enum NotificationOpenResult { Opened, RunningButUnavailable, Failed }

internal static class NotificationAppLauncher
{
    // Ids can be registered desktop executable paths (e.g. Weixin). Never execute
    // an id as a command: require an exact match in Windows' AppsFolder catalog.
    internal static bool IsValidAppId(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.Length <= (IsExecutableIdentity(id) ? 32767 : 128) && id == id.Trim() && id is not ("." or "..")
        && !id.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|')
        && (IsExecutableIdentity(id) || !id.Any(c => c is '\\' or '/' or ':'));

    private static bool IsExecutableIdentity(string id) => Path.IsPathFullyQualified(id)
        && id.Length > 3 && char.IsAsciiLetter(id[0]) && id[1] == ':' && id[2] == '\\'
        && id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !id.Contains('/') && id.IndexOf(':', 2) < 0 && !id.Split('\\').Any(p => p is "." or "..");

    internal static bool TryOpen(string id) => Open(id) == NotificationOpenResult.Opened;
    internal static Task<bool> TryOpenAsync(string id) => OpenBooleanAsync(id);
    private static async Task<bool> OpenBooleanAsync(string id) => await OpenAsync(id) == NotificationOpenResult.Opened;

    internal static Task<NotificationOpenResult> OpenAsync(string id)
    {
        if (!IsValidAppId(id)) return Task.FromResult(NotificationOpenResult.Failed);
        var result = new TaskCompletionSource<NotificationOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        // One occasional STA worker, no polling, no Shell/process work on WPF.
        var thread = new Thread(() =>
        {
            var initialized = false;
            try { initialized = CoInitializeEx(IntPtr.Zero, 2 | 4) >= 0; result.TrySetResult(initialized ? Open(id) : NotificationOpenResult.Failed); }
            catch { result.TrySetResult(NotificationOpenResult.Failed); }
            finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "Notification app activation" };
        try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        catch { result.TrySetResult(NotificationOpenResult.Failed); }
        return result.Task;
    }

    internal static NotificationOpenResult Open(string id)
    {
        var trace = new StringBuilder();
        var result = NotificationOpenResult.Failed;
        try { return result = OpenCore(id, detail => trace.AppendLine(detail)); }
        finally
        {
            // Only notification clicks write this bounded diagnostic; no message
            // contents, continuous polling or file work on the UI thread.
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoyoClawCompanion");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "notification-open.log");
                if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path, "");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} id={id.Replace('\r', ' ').Replace('\n', ' ')} result={result}\n{trace}");
            }
            catch { /* Diagnostics must never prevent activation. */ }
        }
    }

    private static NotificationOpenResult OpenCore(string id, Action<string> trace)
    {
        if (!IsValidAppId(id) || !TryResolveRegisteredTarget(id, out var target)) return NotificationOpenResult.Failed;
        trace($"registeredTarget={target}");
        var running = new HashSet<int>();
        var started = new Dictionary<int, long>();
        var inaccessibleCandidate = false;
        if (!string.IsNullOrEmpty(target))
        {
            // Query only matching executable names; Electron helper processes are
            // included for identity, but never treated as usable windows by default.
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(target)))
            {
                using (process)
                {
                    try
                    {
                        if (!string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) continue;
                        running.Add(process.Id);
                        started[process.Id] = process.StartTime.ToUniversalTime().Ticks;
                    }
                    catch { inaccessibleCandidate = true; }
                }
            }
        }
        var executableProcesses = running.ToHashSet();
        var windows = new List<AppWindow>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var pid);
            var exact = string.Equals(ReadWindowAppId(handle), id, StringComparison.OrdinalIgnoreCase);
            if (!exact && !running.Contains((int)pid)) return true;
            running.Add((int)pid);
            if (!GetWindowRect(handle, out var rect)) return true;
            var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
            var kind = new StringBuilder(128); GetClassName(handle, kind, kind.Capacity);
            windows.Add(new(handle, (int)pid, exact, IsWindowVisible(handle),
                GetWindow(handle, 4) != IntPtr.Zero, (GetWindowLong(handle, -20) & 0x80) != 0,
                rect.Right - rect.Left, rect.Bottom - rect.Top, title.Length > 0, kind.ToString(),
                started.GetValueOrDefault((int)pid, long.MaxValue), IsWindowEnabled(handle), IsHungAppWindow(handle)));
            return true;
        }, IntPtr.Zero);
        var selected = SelectWindow(windows);
        var wakeKey = GetWakeKey(id, target, executableProcesses.Count, registeredTargetVerified: true);
        trace($"verifiedProcesses={executableProcesses.Count} selected={selected?.ClassName} visible={selected?.Visible} key={wakeKey:X}");
        // WeChat can retain a visible auxiliary window while its main interface
        // is in the tray. Let its own registered shortcut restore the interface
        // even then; QQ and other already-visible applications keep their path.
        if (wakeKey is ushort key && ShouldUseHotkey(key, selected, selected is not null && IsIconic(selected.Handle)))
            return WakeByHotkey(executableProcesses, key, trace);
        if (selected is not null)
        {
            return ActivateExistingWindow(selected.Handle);
        }
        if (running.Count > 0 || inaccessibleCandidate) return NotificationOpenResult.RunningButUnavailable;
        if (SHParseDisplayName("shell:AppsFolder\\" + id, IntPtr.Zero, out var item, 0, out _) < 0 || item == IntPtr.Zero)
            return NotificationOpenResult.Failed;
        try
        {
            var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x400 | 0x100 | 0x4,
                Verb = "open", IdList = item, Show = 1 };
            return ShellExecuteEx(ref info) ? NotificationOpenResult.Opened : NotificationOpenResult.Failed;
        }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    internal sealed record AppWindow(IntPtr Handle, int ProcessId, bool ExactIdentity, bool Visible,
        bool Owned, bool Tool, int Width, int Height, bool HasTitle, string ClassName, long Started, bool Enabled = true, bool Hung = false);

    internal static bool ShouldUseHotkey(ushort key, AppWindow? selected, bool minimized)
        => key == 0x57 || selected is null || (!selected.Visible && !minimized);

    internal static AppWindow? SelectWindow(IEnumerable<AppWindow> windows) => windows
        .Where(w => w.Enabled && !w.Hung && !w.Owned && !w.Tool && w.HasTitle && w.Width >= 120 && w.Height >= 80
            && !w.ClassName.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(w => w.ExactIdentity).ThenByDescending(w => w.Visible)
        // Prefer the existing session over a newer accidentally launched login process.
        .ThenBy(w => w.Started).ThenByDescending(w => (long)w.Width * w.Height).FirstOrDefault();

    internal static NotificationOpenResult ActivateExistingWindow(IntPtr window)
    {
        // A tray app may have hidden its framework widget / renderer as well as
        // its HWND. Showing only the HWND can leave a visible but inert shell.
        // Foreground success cannot verify framework interactivity. Never force
        // hidden windows visible or launch another instance as a fallback.
        if (!IsWindow(window) || !IsWindowEnabled(window) || IsHungAppWindow(window))
            return NotificationOpenResult.RunningButUnavailable;
        var minimized = IsIconic(window);
        if (!IsWindowVisible(window) && !minimized) return NotificationOpenResult.RunningButUnavailable;
        if (minimized) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (GetForegroundWindow() == window && IsWindowVisible(window) && !IsIconic(window)) return NotificationOpenResult.Opened;
            Thread.Sleep(30); // bounded wait on this click's worker only
        }
        return NotificationOpenResult.RunningButUnavailable;
    }

    internal static ushort? GetWakeKey(string id, string? registeredTarget, int verifiedProcessCount, bool registeredTargetVerified = false)
    {
        if (verifiedProcessCount <= 0 || !IsValidAppId(id)) return null;
        var executable = Path.GetFileName(registeredTarget);
        if (string.Equals(id, "QQ", StringComparison.OrdinalIgnoreCase) && string.Equals(executable, "QQ.exe", StringComparison.OrdinalIgnoreCase)) return 0x58;
        if ((string.Equals(executable, "Weixin.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(executable, "WeChat.exe", StringComparison.OrdinalIgnoreCase))
            && (string.Equals(id, registeredTarget, StringComparison.OrdinalIgnoreCase)
                || id.Equals("Weixin", StringComparison.OrdinalIgnoreCase) || id.Equals("WeChat", StringComparison.OrdinalIgnoreCase)
                || id.Equals("Tencent.WeChat", StringComparison.OrdinalIgnoreCase)
                // Exact AppsFolder registration plus full process path is the
                // authority for other WeChat notification identity aliases.
                || (registeredTargetVerified && !id.Equals("QQ", StringComparison.OrdinalIgnoreCase)))) return 0x57;
        return null;
    }

    internal static NotificationOpenResult WakeByHotkey(IReadOnlySet<int> verifiedProcesses, ushort key, Action<string>? trace = null)
    {
        bool AppIsForeground()
        {
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var pid);
            return verifiedProcesses.Contains((int)pid) && IsWindowVisible(foreground) && !IsIconic(foreground)
                && IsWindowEnabled(foreground) && !IsHungAppWindow(foreground);
        }
        if (AppIsForeground()) return NotificationOpenResult.Opened;
        var sent = verifiedProcesses.Count > 0 && SendHotkey(key, k =>
        {
            var state = GetAsyncKeyState(k);
            if ((state & 0x8000) != 0) trace?.Invoke($"physicalKeyHeld={k:X}");
            return state;
        }, (count, inputs, size) =>
        {
            var accepted = SendInput(count, inputs, size);
            trace?.Invoke($"inputAccepted={accepted}/{count} win32Error={(accepted == count ? 0 : Marshal.GetLastWin32Error())}");
            return accepted;
        });
        trace?.Invoke($"hotkeySent={sent}");
        if (!sent)
            return NotificationOpenResult.RunningButUnavailable;
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 1200)
        {
            if (AppIsForeground()) return NotificationOpenResult.Opened;
            Thread.Sleep(40);
        }
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
        trace?.Invoke($"hotkeyTimeout foregroundPid={foregroundPid}");
        return NotificationOpenResult.RunningButUnavailable;
    }

    internal static bool SendHotkey(ushort key, Func<int, short> keyState, Func<uint, KeyboardInput[], int, uint> send)
    {
        // Do not release or override keys the user is holding. Send the complete
        // chord once, never retry or fall back to launching another process.
        if (key is not (0x58 or 0x57) || new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, (int)key }.Any(k => (keyState(k) & 0x8000) != 0)) return false;
        var chord = Chord(key);
        var size = Marshal.SizeOf<KeyboardInput>();
        var sent = send((uint)chord.Length, chord, size);
        if (sent == chord.Length) return true;
        // If Windows accepted a prefix, release only our still-held injected keys.
        var down = new HashSet<ushort>();
        foreach (var input in chord.Take((int)Math.Min(sent, (uint)chord.Length)))
            if (input.Data.Keyboard.Flags == 0) down.Add(input.Data.Keyboard.Key); else down.Remove(input.Data.Keyboard.Key);
        var release = chord.Skip(3).Where(i => down.Contains(i.Data.Keyboard.Key)).ToArray();
        if (release.Length > 0) send((uint)release.Length, release, size);
        return false;
    }

    private static KeyboardInput[] Chord(ushort key) => new[] { Key(0x11), Key(0x12), Key(key), Key(key, 2), Key(0x12, 2), Key(0x11, 2) };
    private static KeyboardInput Key(ushort key, uint flags = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputData
    {
        [FieldOffset(0)] public KeyData Keyboard;
        [FieldOffset(0)] public MouseData Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyData { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, KeyboardInput[] inputs, int size);

    internal static bool TryResolveRegisteredTarget(string id, out string? target)
    {
        target = null;
        if (!IsValidAppId(id)) return false;
        object? shell = null, folder = null, item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).Namespace("shell:AppsFolder");
            item = ((dynamic)folder!).ParseName(id);
            if (item is null) return false;
            var registered = (string?)((dynamic)item).ExtendedProperty("System.AppUserModel.ID");
            if (!string.Equals(registered, id, StringComparison.OrdinalIgnoreCase)) return false;
            var path = (string?)((dynamic)item).ExtendedProperty("System.Link.TargetParsingPath");
            if (!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                target = Path.GetFullPath(path);
            else if (IsExecutableIdentity(id)) target = Path.GetFullPath(id);
            return true;
        }
        catch { return false; }
        finally
        {
            foreach (var owned in new[] { item, folder, shell })
                if (owned is not null && Marshal.IsComObject(owned)) Marshal.FinalReleaseComObject(owned);
        }
    }

    private static string? ReadWindowAppId(IntPtr window)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref iid, out store) < 0) return null;
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            return store.GetValue(ref key, out value) >= 0 && value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        catch { return null; }
        finally { PropVariantClear(ref value); if (store is not null) Marshal.ReleaseComObject(store); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window;
        public string? Verb, File, Parameters, Directory;
        public int Show; public IntPtr Instance, IdList;
        public string? Class; public IntPtr ClassKey; public uint HotKey;
        public IntPtr IconOrMonitor, Process;
    }
    [DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint actualAttributes);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
