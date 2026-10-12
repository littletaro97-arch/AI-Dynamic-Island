using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MagicStatusbarAdapter;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint h,uint command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint h,uint flags);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] internal static extern nint SendMessage(nint h,uint message,nint wparam,nint lparam);
    [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y; public Point(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] internal struct Size {public int Width,Height;public Size(int w,int h){Width=w;Height=h;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend {public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll")] internal static extern nint GetDC(nint h);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint h,nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(nint h,nint dstDc,ref Point dst,ref Size size,nint srcDc,ref Point src,uint key,ref Blend blend,uint flags);
    internal delegate void WinEventCallback(nint hook,uint eventType,nint hwnd,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min,uint max,nint module,WinEventCallback callback,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [StructLayout(LayoutKind.Sequential)] internal struct Layout { public double Left,BrandRight;public int Visible,Controls;public double QueryMs; }
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int ReadBarLayout(nint h,out Layout layout);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void LayoutChangeCallback();
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern nint StartLayoutWatch(nint h,LayoutChangeCallback callback);
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void StopLayoutWatch(nint watch);
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int ReadCachedLayout(nint watch,out Layout value);
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void RefreshLayout(nint watch);
    [DllImport("MagicBarLayout.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern void SetWatchActive(nint watch,bool active);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint h,uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    internal static bool OnScreen(Window window) {
        if(!IsWindow(window.Handle)||!IsWindowVisible(window.Handle))return false;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        GetMonitorInfo(MonitorFromWindow(window.Handle,2),ref info);GetWindowRect(window.Handle,out var r);
        return r.Bottom>info.Monitor.Top+8 && r.Top<info.Monitor.Top+info.Monitor.Height-8;
    }
    internal static bool BarExposed(Window window)
    {
        GetWindowRect(window.Handle,out var r);
        // Visibility flags remain true when a fullscreen foreground window covers the fixed bar.
        foreach(var x in new[]{r.Left+60,r.Right-160}){var top=WindowFromPoint(new Point(x,r.Top+r.Height/2));GetWindowThreadProcessId(top,out var pid);if(pid==(uint)window.Pid)return true;}
        return false;
    }
    internal delegate bool Callback(nint h, nint state);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right-Left; public int Height => Bottom-Top; }
    internal record Window(nint Handle, int Pid, string Title, Rect Bounds, uint Dpi, bool Visible);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(Callback callback, nint state);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint h, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint h,int command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint h, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint h,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint h,int attribute,out int value,int size);

    internal static List<Window> Windows()
    {
        var result = new List<Window>();
        EnumWindows((h,_) => {
            var text = new StringBuilder(256); GetWindowText(h,text,text.Capacity);
            if (text.ToString() is "Magictopbar" or "AI Dynamic Island" or "Magic Statusbar Experiment") {
                GetWindowThreadProcessId(h,out var pid); GetWindowRect(h,out var r);
                result.Add(new(h,(int)pid,text.ToString(),r,GetDpiForWindow(h),IsWindowVisible(h)));
            }
            return true;
        },0);
        return result;
    }
    internal static void Cloak(nint h,bool cloak) { int value=cloak?1:0; if(IsWindow(h)) DwmSetWindowAttribute(h,13,ref value,4); }
    internal static void Restore(nint h) { if(IsWindow(h)){Cloak(h,false);ShowWindow(h,8);} }
    internal static void Capture(Rect rect,string path)
    {
        using var image=new System.Drawing.Bitmap(rect.Width,rect.Height);
        using var g=System.Drawing.Graphics.FromImage(image);
        g.CopyFromScreen(rect.Left,rect.Top,0,0,image.Size);
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
}
