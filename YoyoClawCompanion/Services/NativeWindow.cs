using System.Runtime.InteropServices;

namespace YoyoClawCompanion.Services;

internal static class NativeWindow
{
    public static void RestoreAndActivate(IntPtr handle) { ShowWindow(handle, 9); SetForegroundWindow(handle); }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
