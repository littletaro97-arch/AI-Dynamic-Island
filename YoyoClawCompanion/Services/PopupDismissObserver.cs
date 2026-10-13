using System.Runtime.InteropServices;

namespace YoyoClawCompanion.Services;

// Exists only while a non-activating Magic context menu is open. Never consumes input.
internal sealed class PopupDismissObserver : IDisposable
{
    internal readonly record struct Region(double Left,double Top,double Width,double Height,double Radius)
    {
        internal bool Contains(double x,double y){
            if(x<Left||y<Top||x>=Left+Width||y>=Top+Height)return false;
            var radius=Math.Max(0,Math.Min(Radius,Math.Min(Width,Height)/2));
            var nearX=Math.Clamp(x,Left+radius,Left+Width-radius);
            var nearY=Math.Clamp(y,Top+radius,Top+Height-radius);
            return (x-nearX)*(x-nearX)+(y-nearY)*(y-nearY)<=radius*radius;
        }
    }
    private readonly Func<Region?> bounds;
    private readonly Action<Action> dispatch;
    private readonly Action close;
    private readonly Hook callback;
    private nint handle;
    private bool disposed,queued;
    private delegate nint Hook(int code,nint message,nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData {internal int X,Y;internal uint Mouse,Flags,Time;internal nuint Extra;}
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int type,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    internal PopupDismissObserver(Func<Region?> bounds,Action<Action> dispatch,Action close,bool install=true){
        this.bounds=bounds;this.dispatch=dispatch;this.close=close;callback=Observe;
        if(install){handle=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);if(handle==0)throw new System.ComponentModel.Win32Exception();}
    }
    private nint Observe(int code,nint message,nint data){
        try{if(code>=0 && message is 0x201 or 0x204 or 0x207){var point=Marshal.PtrToStructure<MouseData>(data);ObserveClick((int)message,point.X,point.Y);}}
        catch(InvalidOperationException){ /* Popup may detach during shutdown; still pass the click onward. */ }
        return CallNextHookEx(handle,code,message,data);
    }
    internal void ObserveClick(int message,double x,double y){
        if(disposed||queued||message is not (0x201 or 0x204 or 0x207))return;
        if(bounds() is not { } region||region.Contains(x,y))return;
        queued=true;dispatch(()=>{if(!disposed)close();});
    }
    public void Dispose(){disposed=true;if(handle!=0){UnhookWindowsHookEx(handle);handle=0;}GC.KeepAlive(callback);}
}
