using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;

namespace YoyoClawCompanion.Services;

internal static class NativeWindow
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static bool IsAltPressed() => (GetAsyncKeyState(0x12) & 0x8000) != 0;
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        MonitorEnum callback = (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = "" };
            if (!GetMonitorInfoEx(monitor, ref info)) return true;
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            var id = EnumDisplayDevices(info.Device, 0, ref device, 1) && !string.IsNullOrEmpty(device.DeviceId)
                ? device.DeviceId : info.Device;
            displays.Add(new DisplayInfo(id, info.Device, (info.Flags & 1) != 0,
                new Rect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top)));
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return displays.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.DeviceName, StringComparer.Ordinal).ToArray();
    }
    public static void RestoreAndActivate(IntPtr handle) { ShowWindow(handle, 9); SetForegroundWindow(handle); }
    public static Rect GetMonitorWorkArea(Point screenPoint)
    {
        var monitor = MonitorFromPoint(new NativePoint((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        return SystemParameters.WorkArea;
    }

    public static Rect GetMonitorBounds(Point screenPoint)
    {
        var monitor = MonitorFromPoint(new NativePoint((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            return new Rect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        return new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
    }

    public static Point GetCursorPosition() => GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);

    public static void SetTopmostWithoutActivation(IntPtr window, bool topmost)
    {
        if (window == IntPtr.Zero) return;
        SetWindowPos(window, topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static bool IntersectsTaskbar(Rect screenBounds)
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (IntersectsWindow(primary, screenBounds)) return true;
        var current = IntPtr.Zero;
        while ((current = FindWindowEx(IntPtr.Zero, current, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            if (IntersectsWindow(current, screenBounds)) return true;
        return false;
    }

    private static bool IntersectsWindow(IntPtr window, Rect screenBounds)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window) || !GetWindowRect(window, out var rect)) return false;
        return screenBounds.IntersectsWith(new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
    }

    public static bool IsForegroundWindowFullscreen(IntPtr ownWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow || foreground == GetShellWindow()
            || foreground == GetDesktopWindow() || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        const int styleIndex = -16, captionStyle = 0x00C00000;
        if (IsZoomed(foreground) && (GetWindowLong(foreground, styleIndex) & captionStyle) != 0) return false;
        if (!GetWindowRect(foreground, out var window)) return false;
        var monitor = MonitorFromWindow(foreground, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;

        const int tolerance = 3;
        return window.Left <= info.Monitor.Left + tolerance
            && window.Top <= info.Monitor.Top + tolerance
            && window.Right >= info.Monitor.Right - tolerance
            && window.Bottom >= info.Monitor.Bottom - tolerance;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx
    {
        public int Size; public NativeRect Monitor, Work; public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice displayDevice, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
