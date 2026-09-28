using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;

namespace YoyoClawCompanion.Services;

internal static class NativeWindow
{
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
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
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
