using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using System.Drawing;

class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process,uint flags);
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [STAThread] static void Main(string[] args) {
        int count=args.Contains("--repeat")?5:1;
        for(int i=0;i<count;i++){Run(args);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
    }
    static void Run(string[] args)
    {
        var initialGdi=GetGuiResources(Process.GetCurrentProcess().Handle,0);
        var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type=assembly.GetType("MagicStatusbarAdapter.StatusForm")!;
        var constructor=type.GetConstructors(Flags).Single();
        var arguments=constructor.GetParameters().Select(p=>p.ParameterType==typeof(Action<string>)?(object)(Action<string>)(_=>{}):
            p.ParameterType==typeof(Action)?(Action)(()=>{}):p.ParameterType==typeof(long)?0L:
            p.ParameterType==typeof(Func<string,Task<string>>)?(Func<string,Task<string?>>)(_=>Task.FromResult<string?>(null)):
            p.ParameterType==typeof(Action<string,int,int>)?(Action<string,int,int>)((_,_,_)=>{}):
            p.ParameterType==typeof(Func<string,string,string,Task>)?(Func<string,string,string,Task>)((_,_,_)=>Task.CompletedTask):throw new Exception("Unsupported constructor "+p.ParameterType)).ToArray();
        var form=(Form)constructor.Invoke(arguments);
        // This form stays hidden and its surface is located outside all monitors. No desktop interaction.
        var native=assembly.GetType("MagicStatusbarAdapter.Native")!;
        var rect=Activator.CreateInstance(native.GetNestedType("Rect",BindingFlags.NonPublic)!)!;
        foreach(var (name,value) in new[]{("Left",0),("Top",-10000),("Right",3072),("Bottom",-9936)})rect.GetType().GetField(name)!.SetValue(rect,value);
        var window=Activator.CreateInstance(native.GetNestedType("Window",BindingFlags.NonPublic)!,Flags,null,new object[]{IntPtr.Zero,0,"fixture",rect,(uint)192,true},null)!;
        type.GetField("bar",Flags)!.SetValue(form,window);
        var layout=Activator.CreateInstance(native.GetNestedType("Layout",BindingFlags.NonPublic)!)!;
        layout.GetType().GetField("Left")!.SetValue(layout,2070d);layout.GetType().GetField("BrandRight")!.SetValue(layout,156d);layout.GetType().GetField("Visible")!.SetValue(layout,1);
        type.GetField("layout",Flags)!.SetValue(form,layout);
        var model=type.GetField("model",Flags)!.GetValue(form)!;
        var modelType=model.GetType();var parse=modelType.GetMethod("Parse",BindingFlags.Static|BindingFlags.NonPublic)!;var apply=modelType.GetMethod("Apply",Flags)!;
        var now=DateTimeOffset.UtcNow;
        void Snapshot(int quota){var json=JsonSerializer.Serialize(new{updatedAt=now,codex=new{running=true,busy=true,fiveHourRemainingPercent=quota},workBuddy=new{running=true,credits=3051.27,summary=new string('x',2000)},yoyo=new{running=true,points=417.66,result=new string('y',2000)}});apply.Invoke(model,new[]{parse.Invoke(null,new object[]{json,now}),now});}
        var render=type.GetMethod("Render",Flags)!.CreateDelegate<Action>(form);var submit=type.GetMethod("Submit",Flags)!.CreateDelegate<Action>(form);
        type.GetField("shown",Flags)!.SetValue(form,true);Snapshot(100);render();for(int i=0;i<100;i++)render();
        object Measure(string name,int count,Action work){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();var bytes=GC.GetAllocatedBytesForCurrentThread();var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime.TotalSeconds;var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)work();watch.Stop();process.Refresh();return new{scenario=name,count,elapsedMs=watch.Elapsed.TotalMilliseconds,cpuSeconds=process.TotalProcessorTime.TotalSeconds-cpu,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes,privateMB=process.PrivateMemorySize64/1048576d,workingSetMB=process.WorkingSet64/1048576d};}
        if(args.Length>2 && args[2]!="--repeat"){Directory.CreateDirectory(args[2]);((System.Drawing.Bitmap)type.GetField("pixels",Flags)!.GetValue(form)!).Save(Path.Combine(args[2],"quota.png"));}
        var idle=Measure("unchanged-render-key",10000,render);
        Snapshot(99);render();
        var surfaceField=assembly.GetType("MagicStatusbarAdapter.PixelSurface")?.GetField("Created",BindingFlags.Static|BindingFlags.NonPublic);
        var beforeSubmissions=surfaceField?.GetValue(null);
        var animated=Measure("transition-submit",500,submit);
        var surfaceCount=surfaceField?.GetValue(null);
        if(args.Length>2 && args[2]!="--repeat"){modelType.GetMethod("NewSystemNotice",Flags)!.Invoke(model,new object?[]{"QQ",now,"好友","这是测试通知内容，完整的长文字在安全区域内使用省略号显示，不会与右侧原生控件重叠。","fixture-notice","QQ.App"});render();((System.Drawing.Bitmap)type.GetField("pixels",Flags)!.GetValue(form)!).Save(Path.Combine(args[2],"notification.png"));}
        bool? hiddenBuffersReleased=null;
        if(surfaceField!=null){
            type.GetField("shown",Flags)!.SetValue(form,false);
            foreach(var field in new[]{"fadeStart","contentStart"})type.GetField(field,Flags)!.SetValue(form,Environment.TickCount64-200);
            type.GetField("fadeTarget",Flags)!.SetValue(form,0d);
            type.GetMethod("Animate",Flags)!.Invoke(form,null);
            hiddenBuffersReleased=new[]{"pixels","oldPixels","mixedPixels","surface"}.All(name=>type.GetField(name,Flags)!.GetValue(form)==null);
            if(hiddenBuffersReleased!=true)throw new Exception("Hidden buffers were retained");
            type.GetField("shown",Flags)!.SetValue(form,true);render();
            if(type.GetField("pixels",Flags)!.GetValue(form)==null)throw new Exception("Hidden render did not rebuild on return");
        }
        type.GetMethod("Cleanup",Flags)!.Invoke(form,null);form.Dispose();
        var finalGdi=GetGuiResources(Process.GetCurrentProcess().Handle,0);
        Console.WriteLine(JsonSerializer.Serialize(new{hiddenBuffersReleased,initialGdi,finalGdi,pixelSurfacesBeforeSubmissions=beforeSubmissions,pixelSurfacesAfterSubmissions=surfaceCount,label=args.Length>1?args[1]:"",module=assembly.Location,dpi=192,visible=false,idle,animated},new JsonSerializerOptions{WriteIndented=true}));
    }
}
