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

    public static Point GetCursorPosition() => GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(double.NaN, double.NaN);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; public NativePoint(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
