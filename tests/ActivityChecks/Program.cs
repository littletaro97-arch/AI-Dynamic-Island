using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoyoClawCompanion;
using YoyoClawCompanion.Services;

class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Flags)!.Invoke(target,args);
    static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Flags)!.SetValue(target,value);
    static void Assert(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static void CaptureLayout(FrameworkElement root,string name)
    {
        if(Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR") is not {Length:>0} directory) return;
        Directory.CreateDirectory(directory);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth*1.5),(int)Math.Ceiling(root.ActualHeight*1.5),144,144,PixelFormats.Pbgra32);
        bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
    }
    [STAThread] static void Main()
    {
        var app=new App();var main=new MainWindow();
        ((HashSet<string>)typeof(MainWindow).GetField("_suppressedProviders",Flags)!.GetValue(main)!).Clear();
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
        var rowElements=new[]{new[]{"StateDot","YoyoLabel","StateText","YoyoRowButton"},new[]{"CodexDot","CodexLabel","CodexStateText","CodexRowButton"},new[]{"WorkBuddyDot","WorkBuddyLabel","WorkBuddyStateText","WorkBuddyRowButton"},new[]{"DeepSeekDot","DeepSeekLabel","DeepSeekStateText","DeepSeekRowButton"}};
        foreach(var upward in new[]{false,true})
        for(int mask=0;mask<16;mask++)
        {
            Set(main,"_expandUp",upward);
            var settings=main.CurrentSettings;
            settings.ShowYoyo=(mask&1)!=0;settings.ShowCodex=(mask&2)!=0;settings.ShowWorkBuddy=(mask&4)!=0;settings.ShowDeepSeek=(mask&8)!=0;
            Call(main,"ApplyProviderVisibility");
            var occupied=new HashSet<int>();
            for(int index=0;index<4;index++)
            {
                var visible=(mask&(1<<index))!=0;
                foreach(var name in rowElements[index])
                    Assert(((UIElement)main.FindName(name)).Visibility==(visible?Visibility.Visible:Visibility.Collapsed),"whole row visibility "+name+" mask="+mask+" up="+upward);
                if(visible) Assert(occupied.Add(Grid.GetRow((UIElement)main.FindName(rowElements[index][0]))),"visible providers have distinct slots");
            }
            Assert(!occupied.Contains(Grid.GetRow((UIElement)main.FindName("RecentBorder"))),"reply does not overlap provider rows");
        }
        main.CurrentSettings.ShowYoyo=main.CurrentSettings.ShowCodex=main.CurrentSettings.ShowWorkBuddy=main.CurrentSettings.ShowDeepSeek=true;
        Set(main,"_expandUp",false);Call(main,"ApplyProviderVisibility");Call(main,"ApplyTypography");
        var island=(Border)main.FindName("Island");island.Width=475;island.Height=350;
        var expanded=(FrameworkElement)main.FindName("ExpandedPanel");expanded.Visibility=Visibility.Visible;
        var root=(FrameworkElement)main.Content;root.Measure(new Size(550,500));root.Arrange(new Rect(0,0,550,500));root.UpdateLayout();
        double CenterY(string name){var element=(FrameworkElement)main.FindName(name);return element.TranslatePoint(new Point(element.ActualWidth/2,element.ActualHeight/2),root).Y;}
        var dotCenters=new[]{"YoyoMiniDot","CodexMiniDot","WorkBuddyMiniDot","DeepSeekMiniDot"}.Select(CenterY).ToArray();
        Assert(dotCenters.Max()-dotCenters.Min()<.01,"all four header points share a horizontal center");
        var label=(TextBlock)main.FindName("DeepSeekLabel");
        var formatted=new FormattedText(label.Text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(label.FontFamily,label.FontStyle,label.FontWeight,label.FontStretch),label.FontSize,label.Foreground,1);
        Assert(label.ActualWidth>=formatted.Width-.5,"Harness label receives its full text width");
        main.CurrentSettings.ThemeMode="light";Call(main,"ApplyTheme");expanded.Opacity=1;
        ((TextBlock)main.FindName("RecentResultText")).Text="验收：四个状态点水平对齐；DeepSeek Harness 名称完整显示。";
        root.UpdateLayout();CaptureLayout(root,"four-provider-layout");
        main.CurrentSettings.ProviderOrder="codex,workbuddy,deepseek,yoyo";
        var suppression=(HashSet<string>)typeof(MainWindow).GetField("_suppressedProviders",Flags)!.GetValue(main)!;
        suppression.Add("deepseek");Call(main,"ApplyProviderOrder");Call(main,"ApplyProviderVisibility");root.UpdateLayout();
        Assert(rowElements[3].All(name=>((UIElement)main.FindName(name)).Visibility==Visibility.Collapsed) && ((UIElement)main.FindName("DeepSeekIndicatorButton")).Visibility==Visibility.Collapsed,"dismissed Harness removes every row element and status point");
        Assert(Grid.GetRow((UIElement)main.FindName("YoyoLabel"))==2,"remaining YOYO row moves into the removed Harness slot");
        ((TextBlock)main.FindName("RecentResultText")).Text="验收：DeepSeek 已暂时隐藏，只显示 Codex、WorkBuddy 和 YOYO。";
        CaptureLayout(root,"dismissed-harness-layout");
        suppression.Clear();Call(main,"ApplyProviderVisibility");
        // Drain earlier queued layout/status callbacks before the isolated marquee fixture.
        var drain=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>drain.Continue=false));Dispatcher.PushFrame(drain);
        var viewport=(FrameworkElement)main.FindName("SummaryViewport");viewport.Measure(new Size(120,20));viewport.Arrange(new Rect(0,0,120,20));
        ((TextBlock)main.FindName("SummaryText")).Text=new string('W',120);
        Set(main,"_isBalanceSummary",true);Set(main,"_expanded",false);Set(main,"_focusModeHidden",false);Call(main,"UpdateSummaryMarquee");
        var primary=(TranslateTransform)main.FindName("SummaryPrimaryTranslate");var clone=(TranslateTransform)main.FindName("SummaryCloneTranslate");
        Assert(primary.HasAnimatedProperties && clone.HasAnimatedProperties,"overflow marquee uses WPF animation clocks");
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        Console.WriteLine($"MARQUEE primary={primary.X} clone={clone.X} animated={primary.HasAnimatedProperties}/{clone.HasAnimatedProperties} width={viewport.ActualWidth}");
        Assert(primary.X<0 && Math.Abs(primary.X-clone.X)<.001,"marquee copies scroll continuously in phase");
        Set(main,"_expanded",true);Call(main,"UpdateSummaryMarquee");
        Assert(!primary.HasAnimatedProperties && !clone.HasAnimatedProperties && primary.X==0,"expansion removes unused animation clocks");
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
