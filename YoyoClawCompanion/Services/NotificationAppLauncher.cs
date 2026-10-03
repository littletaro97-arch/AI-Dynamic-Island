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
        if (!IsValidAppId(id) || !TryResolveRegisteredTarget(id, out var target)) return NotificationOpenResult.Failed;
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
                started.GetValueOrDefault((int)pid, long.MaxValue)));
            return true;
        }, IntPtr.Zero);
        var selected = SelectWindow(windows);
        if (selected is not null)
        {
            // Hidden tray windows are real windows too; MainWindowHandle misses them.
            ShowWindowAsync(selected.Handle, IsIconic(selected.Handle) ? 9 : 5);
            SetForegroundWindow(selected.Handle);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (GetForegroundWindow() == selected.Handle) return NotificationOpenResult.Opened;
                Thread.Sleep(30); // bounded wait on this click's worker only
            }
            return NotificationOpenResult.RunningButUnavailable;
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
        bool Owned, bool Tool, int Width, int Height, bool HasTitle, string ClassName, long Started);

    internal static AppWindow? SelectWindow(IEnumerable<AppWindow> windows) => windows
        .Where(w => !w.Owned && !w.Tool && w.HasTitle && w.Width >= 120 && w.Height >= 80
            && !w.ClassName.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(w => w.ExactIdentity).ThenByDescending(w => w.Visible)
        // Prefer the existing session over a newer accidentally launched login process.
        .ThenBy(w => w.Started).ThenByDescending(w => (long)w.Width * w.Height).FirstOrDefault();

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
