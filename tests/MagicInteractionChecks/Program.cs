using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using MagicStatusbarAdapter;
using YoyoClawCompanion;
using Forms=System.Windows.Forms;
class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    sealed class CaptionTrap:Forms.NativeWindow,IDisposable{
        internal int Reads;
        internal CaptionTrap(){CreateHandle(new Forms.CreateParams{Caption="owned-caption-trap",Width=700,Height=32});}
        protected override void WndProc(ref Forms.Message message){if(message.Msg==0xd){Reads++;message.Result=IntPtr.Zero;return;}base.WndProc(ref message);}
        public void Dispose()=>DestroyHandle();
    }
    private delegate bool EnumCallback(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr handle,System.Text.StringBuilder text,int length);
    [StructLayout(LayoutKind.Sequential)] struct MouseData{internal int X,Y;internal uint Mouse,Flags,Time;internal nuint Extra;}
    [STAThread] static void Main(){
        using var trap=new CaptionTrap();
        var host=typeof(MainWindow);host.GetField("_magicKnownBar",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,IntPtr.Zero);
        var bar=(IntPtr)host.GetMethod("MagicBarHandle",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
        Check(trap.Reads==0,"host discovery never synchronously queries own window captions");
        if(bar==IntPtr.Zero){Console.WriteLine("No live Magic toolbar: remaining desktop checks skipped.");return;}
        var primaryMethod=host.GetMethod("IsPrimaryMagicBar",BindingFlags.Static|BindingFlags.NonPublic)!;
        Check((bool)primaryMethod.Invoke(null,new object[]{bar})!,"live toolbar selection prefers primary monitor");
        IntPtr secondary=IntPtr.Zero;
        EnumWindows((h,_)=>{GetWindowThreadProcessId(h,out var pid);if(pid==(uint)Environment.ProcessId)return true;var caption=new System.Text.StringBuilder(64);GetWindowText(h,caption,64);if(caption.ToString()=="Magictopbar"&&!(bool)primaryMethod.Invoke(null,new object[]{h})!)secondary=h;return true;},IntPtr.Zero);
        if(secondary!=IntPtr.Zero){host.GetField("_magicKnownBar",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,secondary);var selected=(IntPtr)host.GetMethod("MagicBarHandle",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;Check(selected==bar,"cached secondary toolbar cannot displace existing primary toolbar");}
        int notificationCalls=0,menuCalls=0;string? target=null;Action<string,int,int> openMenu=(selected,x,y)=>menuCalls++;Func<string,string,string,Task> mute=(_,_,_)=>Task.CompletedTask;
        var adapter=new MagicAdapter(_=>{},()=>{},()=>{},id=>{notificationCalls++;target=id;return Task.Run(async()=>{await Task.Delay(40);return (string?)"无法打开来源应用，请使用原系统通知或手动打开应用。";});},bar.ToInt64(),openMenu,mute);
        var form=(Forms.Form)typeof(MagicAdapter).GetField("form",Flags)!.GetValue(adapter)!;
        adapter.Configure("{}");adapter.Update(JsonSerializer.Serialize(new{updatedAt=DateTimeOffset.Now,codex=new{running=true,busy=false,fiveHourRemainingPercent=64}}));Pump(1000);
        Check(trap.Reads==0,"adapter attach avoids own hidden and SystemEvents windows");
        var pixels=form.GetType().GetField("pixels",Flags)!.GetValue(form);Check(pixels!=null,"known toolbar handle produces rendered frame");
        form.GetType().GetMethod("ShowProviderMenu",Flags)!.Invoke(form,new object?[]{"Codex",new System.Drawing.Point(5,32)});Pump(50);
        Check(menuCalls==1,"right click delegates to shared WPF menu callback");
        adapter.NotifySystem("Test app","title","body","notice-id","Test.App");Pump(200); var probeModel=form.GetType().GetField("model",Flags)!.GetValue(form)!; probeModel.GetType().GetMethod("SetVisible",Flags)!.Invoke(probeModel,new object[]{true,DateTimeOffset.Now});form.GetType().GetMethod("Render",Flags)!.Invoke(form,null);
        var hits=form.GetType().GetField("hits",Flags)!.GetValue(form)!;var areas=(System.Collections.IEnumerable)hits.GetType().GetField("Areas",Flags)!.GetValue(hits)!;
        var area=areas.Cast<object>().First();var bounds=(System.Drawing.RectangleF)area.GetType().GetProperty("Bounds")!.GetValue(area)!;
        form.GetType().GetMethod("OnMouseUp",Flags)!.Invoke(form,new object[]{new Forms.MouseEventArgs(Forms.MouseButtons.Left,1,(int)(bounds.Left+bounds.Width/2),(int)(bounds.Top+bounds.Height/2),0)});Pump(100);
        Check(notificationCalls==1&&target=="Test.App","notification click forwards exact app identity to existing launcher callback");
        var model=form.GetType().GetField("model",Flags)!.GetValue(form)!;var notice=model.GetType().GetProperty("CurrentNotice",Flags)!.GetValue(model)??model.GetType().GetField("paused",Flags)!.GetValue(model)!;
        Check((string)notice.GetType().GetProperty("Source")!.GetValue(notice)! =="Test app"&&((string)notice.GetType().GetProperty("Text")!.GetValue(notice)!).Contains("无法打开来源应用"),"failed launch replaces content while source remains");
        adapter.Stop();Pump(300);Check(form.IsDisposed,"overlay render timers are disposed on exit");
        for(int i=0;i<3;i++){var next=new MagicAdapter(_=>{},()=>{},()=>{},_=>Task.FromResult<string?>(null),bar.ToInt64(),openMenu,mute);Pump(80);next.Stop();Pump(200);}
        Check(trap.Reads==0,"repeated mode transitions never query owned caption trap");
        var iconType=typeof(App).Assembly.GetType("YoyoClawCompanion.Services.MagicIconService")!;var icon=iconType.GetMethod("Load",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        Check(icon is System.Windows.Media.ImageSource,"official icon extracted from installed Magic executable");
        Console.WriteLine($"{checks} Magic interaction checks passed; menu appearance and dismissal still require manual desktop acceptance.");
    }
}
