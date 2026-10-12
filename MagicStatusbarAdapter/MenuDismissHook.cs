using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MagicStatusbarAdapter;
internal sealed class MenuDismissHook : IDisposable
{
    private readonly ContextMenuStrip menu;
    private readonly Action<Action> dispatch;
    private readonly Hook callback;
    private nint handle;
    private delegate nint Hook(int code,nint message,nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData{internal Native.Point Point;internal uint Mouse,Flags,Time;internal nuint Extra;}
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int type,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    internal MenuDismissHook(ContextMenuStrip menu,Action<Action> dispatch){this.menu=menu;this.dispatch=dispatch;callback=Observe;handle=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);}
    internal static bool Outside(System.Drawing.Rectangle bounds,System.Drawing.Point point)=>!bounds.Contains(point);
    private nint Observe(int code,nint message,nint data){
        if(code>=0&&message is 0x201 or 0x204 or 0x207 && menu.Visible){
            var point=Marshal.PtrToStructure<MouseData>(data).Point;
            if(Native.GetWindowRect(menu.Handle,out var rect)&&Outside(new(rect.Left,rect.Top,rect.Width,rect.Height),new(point.X,point.Y)))dispatch(()=>{if(menu.Visible)menu.Close(ToolStripDropDownCloseReason.AppClicked);});
        }
        return CallNextHookEx(handle,code,message,data);
    }
    public void Dispose(){if(handle!=0){UnhookWindowsHookEx(handle);handle=0;}GC.KeepAlive(callback);}
}
