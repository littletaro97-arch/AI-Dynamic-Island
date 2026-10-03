using System.Runtime.InteropServices;

namespace YoyoClawCompanion.Services;

internal static class NotificationAppLauncher
{
    // Only Windows' app identity is used. Toast body, titles and URLs never become commands.
    internal static bool IsValidAppId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && id == id.Trim() && id is not ("." or "..")
        && !id.Any(c => char.IsControl(c) || c is '\\' or '/' or ':' or '"' or '<' or '>' or '|');

    internal static bool TryOpen(string id)
    {
        if (!IsValidAppId(id)) return false;
        if (SHParseDisplayName("shell:AppsFolder\\" + id, IntPtr.Zero, out var item, 0, out _) < 0 || item == IntPtr.Zero) return false;
        try
        {
            var info = new ShellExecuteInfo
            {
                Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x400 | 0x100 | 0x4, // NO_UI | NOASYNC | IDLIST
                Verb = "open", IdList = item, Show = 1
            };
            return ShellExecuteEx(ref info);
        }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    internal static Task<bool> TryOpenAsync(string id)
    {
        if (!IsValidAppId(id)) return Task.FromResult(false);
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Shell extensions can require STA and may take time to resolve an app. Do not
        // block WPF or keep a permanent worker around for an occasional user click.
        var thread = new Thread(() =>
        {
            var initialized = false;
            try { initialized = CoInitializeEx(IntPtr.Zero, 2 | 4) >= 0; result.TrySetResult(initialized && TryOpen(id)); }
            catch { result.TrySetResult(false); }
            finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "Notification app activation" };
        try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        catch { result.TrySetResult(false); }
        return result.Task;
    }

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributes, out uint actualAttributes);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
