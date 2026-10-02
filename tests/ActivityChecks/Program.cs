using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using YoyoClawCompanion;
using YoyoClawCompanion.Services;

class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Flags)!.Invoke(target,args);
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Flags)!.SetValue(target,value);
    static void Assert(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    [STAThread] static void Main()
    {
        var app=new App();var main=new MainWindow();
        foreach(var field in new[]{"_yoyoInstalled","_codexInstalled","_workBuddyInstalled","_deepSeekInstalled"})Set(main,field,true);
        Set(main,"_settings",new IslandSettings());
        var now=DateTimeOffset.UtcNow.AddSeconds(1);
        var yoyo=new YoyoStatus(true,true,true,10,20,"执行中",false,null,[]);
        var codex=new CodexStatus(true,true,false,false,null,null,null,null,null,null,null,null,0,null,2,2,null,false,[]);
        var buddy=new WorkBuddyStatus(true,true,true,"执行中",Completions:[]);
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is null,"startup baseline does not replay history");
        var a=new TaskCompletionEvent("a","任务 A 完成",now);
        codex=codex with{Completions=[a]};
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is not null,"Codex completion while same app is busy");
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is null,"busy completion deduplicated");
        buddy=buddy with{Completions=[new("b","任务 B 完成",now)]};
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is not null,"WorkBuddy completion while same app is busy");
        yoyo=yoyo with{Completions=[new("y","任务 Y 完成",now)]};
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is not null,"YOYO verified completion while busy");
        Set(main,"_activeConfirmationNotice","等待用户");
        codex=codex with{Completions=[a,new("c","任务 C 完成",now)]};
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is null,"decision notice retains priority");
        Set(main,"_activeConfirmationNotice",null);
        Assert(Call(main,"DetectCompletion",yoyo,codex,buddy) is not null,"completed task queued during decision is retained");
        Call(main,"UpdateHeadline",yoyo,codex,buddy,new WorkBuddyCredits(false,null,null));
        Assert(((TextBlock)main.FindName("HeadlineText")).Text.Contains("执行中"),"completion does not replace running headline");
        Call(main,"UpdateSuppressedProviders",false,true,true,false);
        Call(main,"SetOfflineDismissTarget","yoyo");
        Set(main,"_refreshing",true); // Keep this control test isolated from live providers and saved paths.
        Call(main,"DismissOffline_Click",main,new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(!(bool)Call(main,"IsProviderVisible","yoyo")!,"dismissed offline source is hidden");
        Assert(main.CurrentSettings.SuppressedOfflineProviders.Contains("yoyo"),"offline suppression stored in current configuration");
        Assert(((FrameworkElement)main.FindName("YoyoIndicatorButton")).Visibility==Visibility.Collapsed,"dismissed source status point disappears");
        Call(main,"UpdateSuppressedProviders",false,true,true,false);
        Assert(!(bool)Call(main,"IsProviderVisible","yoyo")!,"offline suppression survives polling");
        Call(main,"UpdateSuppressedProviders",true,true,true,false);
        Assert((bool)Call(main,"IsProviderVisible","yoyo")!,"restarted source resumes detection");
        Assert(!main.CurrentSettings.SuppressedOfflineProviders.Contains("yoyo"),"restart clears saved suppression");
        Call(main,"ApplyExpandedContentOrder",(object?)null);
        Assert(Grid.GetRow((UIElement)main.FindName("DeepSeekLabel"))==3 && Grid.GetRow((UIElement)main.FindName("RecentBorder"))==4,"four providers and reply have distinct rows");
        var fixture=Path.Combine(AppContext.BaseDirectory,"fixtures");Directory.CreateDirectory(fixture);
        var log=Path.Combine(fixture,"large.jsonl");
        File.WriteAllText(log,string.Join('\n',Enumerable.Range(0,300).Select(i=>"{\"type\":\"tool\",\"text\":\""+new string('x',3000)+"\"}"))+"\n{\"type\":\"task_complete\"}\n");
        long Measure(string[]? markers){var before=GC.GetAllocatedBytesForCurrentThread();_ = JsonLineTailReader.Read(log,1024*1024,markers).ToArray();return GC.GetAllocatedBytesForCurrentThread()-before;}
        _=Measure(null);_=Measure(["task_complete"]);
        var unfiltered=Measure(null);var filtered=Measure(["task_complete"]);
        Assert(filtered<unfiltered/10,"irrelevant tool payloads skipped before decoding");
        Console.WriteLine($"ALLOC tail unfiltered={unfiltered} filtered={filtered} bytes");
        var pendingPath=Path.Combine(fixture,"buddy-pending.jsonl");
        File.WriteAllText(pendingPath,"{\"type\":\"function_call\",\"callId\":\"p\",\"name\":\"Bash\"}\n");
        File.SetLastWriteTimeUtc(pendingPath,DateTime.UtcNow.AddSeconds(-50));
        var reader=new WorkBuddyStatusService();
        Assert(((WorkBuddyStatus)Call(reader,"ReadSession",new FileInfo(pendingPath),true)!).IsBusy,"WorkBuddy long tool stays busy beyond thirty seconds");
        Assert(((WorkBuddyStatus)Call(reader,"ReadSession",new FileInfo(pendingPath),true)!).IsBusy,"unchanged WorkBuddy cache retains long tool busy state");
        var codexPath=Path.Combine(fixture,"codex-completed.jsonl");
        File.WriteAllText(codexPath,$"{{\"timestamp\":\"{now:O}\",\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"final_answer\",\"id\":\"f\",\"content\":[{{\"type\":\"output_text\",\"text\":\"完成\"}}]}}}}\n");
        var codexService=new CodexStatusService();
        var completion=Call(codexService,"ReadCompletion",new FileInfo(codexPath))!;
        Assert((string)completion.GetType().GetProperty("Id")!.GetValue(completion)! == "f","Codex parser emits structured final response completion");
        if(Environment.GetEnvironmentVariable("ISLAND_LIVE_HARNESS_CHECK")=="1")
        {
            var service=new DeepSeekStatusService();var result=service.ReadAsync(ApplicationLocator.FindDeepSeekExecutable(null)).GetAwaiter().GetResult();
            Console.WriteLine($"LIVE Harness running={result.IsRunning} available={result.Available} state={result.State} error={result.Error}");
        }
        main.Close();app.Shutdown();
    }
}
