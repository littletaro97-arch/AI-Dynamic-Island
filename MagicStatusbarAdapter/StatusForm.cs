using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace MagicStatusbarAdapter;

internal sealed class StatusForm : Form
{
    private readonly System.Windows.Forms.Timer fast=new(){Interval=150},fallback=new(){Interval=2000},debounce=new(){Interval=35},animation=new(){Interval=15},noticeTimer=new(),blink=new(){Interval=50};
    private readonly StatusModel model=new();
    private InteractionMap hits=new();
    private readonly Action<string,int,int> openMenu;
    private readonly Func<string,string,string,Task> muteNotification;
    private readonly StatusPainter painter=new();
    private readonly RenderStamp stamp=new();
    private PixelSurface? surface;
    private Bitmap? mixedPixels;
    private List<ProviderItem>? lastObservedItems;
    private readonly Native.WinEventCallback winCallback;
    private readonly Native.LayoutChangeCallback layoutCallback;

    private readonly Action<string> openProvider;

    private readonly Func<string,Task<string?>> openNotification;
    private readonly nint targetBar;
    private readonly LayoutRecovery layoutRecovery=new();
    private bool notificationOpening;
    private AdapterOptions options=new();
    private Snapshot? latest;
    private nint winHook,layoutWatch;
    private Native.Window? bar;
    private Native.Layout? layout;
    private Bitmap? pixels,oldPixels;
    private long blinkStart;
    private string blinkKey="";
    private Native.Point lastOrigin;
    private Bitmap? cachedSource;
    private bool closing,shown,stopping;
    private string fitMode="full";
    private int? lastLayoutError;
    private static readonly object diagnosticGate=new();
    private int layoutReads,layoutEvents,winEvents;
    private double alpha,fadeFrom,fadeTarget,lastDispatchMs;
    private long fadeStart,contentStart;
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x08000000|0x80;p.ExStyle&=~0x20;return p;}}
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x21){message.Result=(nint)3;return;} // Click does not activate the overlay itself.
        if(message.Msg==0x84){var point=new Point((short)(message.LParam.ToInt64()&0xffff),(short)((message.LParam.ToInt64()>>16)&0xffff));point.Offset(-lastOrigin.X,-lastOrigin.Y);message.Result=(nint)(shown&&hits.Find(point)!=null?1:-1);return;}
        base.WndProc(ref message);
    }
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);var area=hits.Find(e.Location);if(area==null)return;
        if(e.Button==MouseButtons.Right)ShowProviderMenu(area.Provider,e.Location);
        else if(e.Button==MouseButtons.Left){
            if(area.Action==HitAction.Mute&&model.CurrentNotice is { AppId: not null } muted){
                if(notificationOpening)return;notificationOpening=true;
                try{await muteNotification(muted.AppId,muted.Title??"",muted.OriginalSource??muted.Source).ConfigureAwait(false);Dispatch(()=>{notificationOpening=false;model.RemoveIgnored(muted.AppId,muted.Title??"",DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();});}
                catch{Dispatch(()=>{notificationOpening=false;model.Feedback(muted.Key,muted.MessageId,"未能保存屏蔽设置，请重试。");Render();});}
            }else if(model.CurrentNotice is { AppId: not null } notice){
                if(notificationOpening)return;notificationOpening=true;model.BeginOpen(notice.Key,DateTimeOffset.Now);noticeTimer.Stop();
                model.Feedback(notice.Key,notice.MessageId,"正在打开来源应用…");Render();
                string? feedback;
                try{feedback=await openNotification(notice.AppId).ConfigureAwait(false);}catch{feedback="无法打开来源应用，请使用原系统通知或手动打开应用。";}
                Dispatch(()=>{notificationOpening=false;if(closing||stopping)return;model.EndOpen(DateTimeOffset.Now);
                if(feedback==null)model.Dismiss(notice.Key,notice.MessageId,DateTimeOffset.Now);else model.Feedback(notice.Key,notice.MessageId,feedback);
                Render();FastGeometry();ScheduleNotice();});
            }else if(area.Provider!=null)OpenProvider(area.Provider);else ShowProviderMenu(null,e.Location);
        }
    }
    private void OpenProvider(string name){if(options.EnableAppLaunch)openProvider(name);}
    private void ShowProviderMenu(string? selected,Point position)=>openMenu(selected??"",lastOrigin.X+position.X,lastOrigin.Y+position.Y);
    private void KeepAboveBar(){if(shown&&bar!=null){var previous=Native.GetWindow(bar.Handle,3);if(previous!=Handle)Native.SetWindowPos(Handle,previous==0?(nint)(-1):previous,0,0,0,0,0x13);}}
    internal StatusForm(Action<string> launch,Action settings,Action disable,Func<string,Task<string?>> openNotice,long barHandle,Action<string,int,int> menu,Func<string,string,string,Task> mute)
    {
        openProvider=launch;openNotification=openNotice;targetBar=(nint)barHandle;openMenu=menu;muteNotification=mute;
        winCallback=WinEvent;layoutCallback=LayoutEvent;
        Text="AI Dynamic Island · Magic";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;
        Size=new(800,64);StartPosition=FormStartPosition.Manual;Location=new(0,0);
        Shown+=(_,_)=>Initialize();FormClosed+=(_,_)=>Cleanup();
        fast.Tick+=(_,_)=>FastGeometry();fallback.Tick+=(_,_)=>{if(bar==null||!Native.IsWindow(bar.Handle))Attach();FastGeometry();};
        debounce.Tick+=(_,_)=>{debounce.Stop();ReadLayoutCache();};
        animation.Tick+=(_,_)=>Animate();blink.Tick+=(_,_)=>{if(!shown){blink.Stop();return;}if(model.CurrentNotice?.State!="attention"&&Environment.TickCount64-blinkStart>=options.MagicBlinkPeriodMs*2){blink.Stop();Submit();return;}Submit();};
        noticeTimer.Tick+=(_,_)=>{noticeTimer.Stop();model.Expire(DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();};
    }
    internal void Configure(string json){var previous=options;options=JsonSerializer.Deserialize<AdapterOptions>(json)??new();options.LastChangeAt=previous.LastChangeAt;options.Fingerprint=previous.Fingerprint;model.RoundSeconds=options.MagicNotificationSeconds;painter.CornerRadius=options.CornerRadius;if(!options.ShowCodex||!options.EnableCodexConfirmationNotifications)model.SetCodexQuestion(null,null,DateTimeOffset.Now);if(latest!=null)ApplyLatest();stamp.Reset();Render();FastGeometry();}
    internal void Update(string json)=>Dispatch(()=>{latest=StatusModel.Parse(json,DateTimeOffset.Now);ApplyLatest();});
    private void ApplyLatest(){
        if(latest==null)return;
        var items=latest.Items.Where(i=>options.Visible(i.Name)).Select(i=>options.Credits(i.Name)?i:i with{Value="",ShortValue=""}).OrderBy(i=>options.Order(i.Name)).ToList();
        if(lastObservedItems==null||!items.SequenceEqual(lastObservedItems)){lastObservedItems=items;options.LastChangeAt=DateTimeOffset.Now;}
        items=items.Select(i=>i.Status=="attention"&&!options.EnableConfirmationNotifications?i with{Status="idle",Value="",ShortValue=""}:i).ToList();
        model.Apply(new(items,latest.Warning),DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();
    }
    internal void NotifySystem(string source,string title,string body,string key,string appId){model.NewSystemNotice(source,DateTimeOffset.Now,title,body,key,appId);Render();FastGeometry();ScheduleNotice();}
    internal void Notify(string source,string title,string body,string key){model.NewSystemNotice(source,DateTimeOffset.Now,title,body,key);Render();FastGeometry();ScheduleNotice();}
    internal void Question(string? id,string? prompt){model.SetCodexQuestion(options.ShowCodex&&options.EnableCodexConfirmationNotifications?id:null,prompt,DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();}
    internal void Stop(){if(stopping)return;stopping=true;fast.Stop();fallback.Stop();debounce.Stop();model.SetVisible(false,DateTimeOffset.Now);SetShown(false);var done=new System.Windows.Forms.Timer{Interval=100};done.Tick+=(_,_)=>{done.Stop();done.Dispose();Close();};done.Start();}
    private void Dispatch(Action action){if(closing||stopping||!IsHandleCreated)return;try{BeginInvoke(()=>{if(!closing&&!stopping)action();});}catch(InvalidOperationException){}}
    private void Initialize(){model.SetVisible(false,DateTimeOffset.Now);Attach();fast.Start();fallback.Start();}
    private void Attach()
    {
        Native.Window? candidate=null;
        if(Native.IsWindow(targetBar)&&Native.GetWindowRect(targetBar,out var rect)){Native.GetWindowThreadProcessId(targetBar,out var pid);candidate=new(targetBar,(int)pid,"Magictopbar",rect,Native.GetDpiForWindow(targetBar),Native.IsWindowVisible(targetBar));}
        if(candidate==null){bar=null;SetShown(false);return;}
        if(winHook!=0)Native.UnhookWinEvent(winHook);
        if(layoutWatch!=0)Native.StopLayoutWatch(layoutWatch);
        bar=candidate;layout=null;layoutWatch=0;
        winHook=Native.SetWinEventHook(0x8000,0x8017,0,winCallback,(uint)bar.Pid,0,2);
        layoutWatch=Native.StartLayoutWatch(bar.Handle,layoutCallback);
    }
    private void WinEvent(nint hook,uint type,nint hwnd,int obj,int child,uint thread,uint time)
    {
        if(closing)return;
        if(bar==null||hwnd!=bar.Handle)return;winEvents++;
        if(type is 0x8002 or 0x8003 or 0x800B){lastDispatchMs=unchecked((uint)Environment.TickCount64-time);FastGeometry();}
        if(type is 0x8000 or 0x8001 or 0x8004)Native.RefreshLayout(layoutWatch);
    }
    private void LayoutEvent()=>Dispatch(()=>{layoutEvents++;FastGeometry();if(!debounce.Enabled)debounce.Start();});
    private void ReadLayoutCache()
    {
        if(closing||layoutWatch==0)return;
        var hr=Native.ReadCachedLayout(layoutWatch,out var value);layoutReads++;
        var physical=bar!=null&&Native.OnScreen(bar)&&Native.BarExposed(bar);
        if(hr==0){
            if(lastLayoutError!=null){Diagnostic("layout recovered");lastLayoutError=null;}
            // UIA offscreen can be transient during tray tree updates; actual HWND exposure wins.
            if(physical)value.Visible=1;
            layout=value;layoutRecovery.Valid();Render();FastGeometry();
        }else if(hr!=unchecked((int)0x8000000A)){
            if(lastLayoutError!=hr){lastLayoutError=hr;Diagnostic($"layout failure hr={hr:X8} physical={physical}");}
            if(layoutRecovery.Retain(layout!=null,physical,Environment.TickCount64)){
                Native.RefreshLayout(layoutWatch);debounce.Interval=70;debounce.Start();
            }else{layout=null;SetShown(false);}
        }
        WriteEvidence();
    }
    private void FastGeometry()
    {
        if(closing||bar==null)return;
        if(!Native.IsWindow(bar.Handle)||!Native.OnScreen(bar)||!Native.BarExposed(bar)){Native.SetWatchActive(layoutWatch,false);model.SetVisible(false,DateTimeOffset.Now);SetShown(false);return;}
        Native.SetWatchActive(layoutWatch,true);
        Native.GetWindowRect(bar.Handle,out var bounds);var moved=bounds.Left!=bar.Bounds.Left||bounds.Top!=bar.Bounds.Top||bounds.Width!=bar.Bounds.Width;
        var dpi=Native.GetDpiForWindow(bar.Handle);if(moved||dpi!=bar.Dpi)bar=bar with{Bounds=bounds,Dpi=dpi};
        if(moved)Native.RefreshLayout(layoutWatch);
        var exposed=layout is {Visible: not 0} l&&l.Left-l.BrandRight>100;
        var before=model.CurrentNotice;model.SetVisible(exposed,DateTimeOffset.Now);
        if(before!=model.CurrentNotice){Render();ScheduleNotice();}
        SetShown(exposed);
        KeepAboveBar();
        if(pixels!=null&&moved)Submit();
    }
    private void Render()
    {
        if(closing||bar==null||layout is not { } l||(!shown&&!animation.Enabled))return;
        var span=CenteredSpan.Calculate(bar.Bounds.Left,bar.Bounds.Width,l.BrandRight,l.Left,bar.Dpi/96.0);var available=Math.Max(1,span.Width);
        if(!stamp.Changed(model.Current,model.CurrentNotice,available,bar.Bounds.Height,bar.Dpi/96.0,model.PendingCount))return;
        var next=painter.Paint(model.Current,model.CurrentNotice,available,bar.Bounds.Height,bar.Dpi/96.0,model.PendingCount);
        hits=next.Hits;fitMode=next.FitMode;oldPixels?.Dispose();oldPixels=pixels;pixels=next.Pixels;contentStart=Environment.TickCount64;
        if(oldPixels!=null)animation.Start();UpdateBlink();Submit();
    }
    private void SetShown(bool visible)
    {
        if(visible&&bar!=null&&layout is { } l)visible=CenteredSpan.Calculate(bar.Bounds.Left,bar.Bounds.Width,l.BrandRight,l.Left,bar.Dpi/96.0).Width>=80;
        visible=visible&&(model.CurrentNotice!=null||(model.Current.Items.Count>0&&(!options.ActiveOnly||model.Current.Items.Any(i=>i.Status=="busy"))&&(!options.EnableUnchangedAutoHide||DateTimeOffset.Now-options.LastChangeAt<TimeSpan.FromMinutes(options.UnchangedAutoHideMinutes))));
        if(shown==visible)return;Diagnostic($"visible={visible} barY={bar?.Bounds.Top} left={layout?.BrandRight} right={layout?.Left}");shown=visible;fadeFrom=alpha;fadeTarget=visible?1:0;fadeStart=Environment.TickCount64;animation.Start();if(visible)Render();UpdateBlink();ScheduleNotice();WriteEvidence();
    }
    private void UpdateBlink(){var notice=model.CurrentNotice;var key=notice?.Key??notice?.Source+"|"+notice?.Text;if(key!=blinkKey){blinkKey=key??"";blinkStart=Environment.TickCount64;}if(shown&&notice!=null&&(notice.State=="attention"||Environment.TickCount64-blinkStart<options.MagicBlinkPeriodMs*2))blink.Start();else blink.Stop();}
    private void Animate()
    {
        var now=Environment.TickCount64;var ratio=Math.Clamp((now-fadeStart)/(fadeTarget==0?70.0:100.0),0,1);alpha=fadeFrom+(fadeTarget-fadeFrom)*ratio;
        if(oldPixels!=null&&now-contentStart>=140){oldPixels.Dispose();oldPixels=null;}
        Submit();if(ratio>=1&&oldPixels==null){
            animation.Stop();
            if(!shown){ReleaseCached();pixels?.Dispose();pixels=null;mixedPixels?.Dispose();mixedPixels=null;stamp.Reset();}
        }
    }
    private void Submit()
    {
        if(pixels==null||closing)return;
        var frame=pixels;var transient=oldPixels!=null;
        if(transient){if(mixedPixels==null||mixedPixels.Size!=pixels.Size){mixedPixels?.Dispose();mixedPixels=new Bitmap(pixels.Width,pixels.Height,PixelFormat.Format32bppPArgb);}using var g=Graphics.FromImage(mixedPixels);g.Clear(Color.Transparent);var t=(float)Math.Clamp((Environment.TickCount64-contentStart)/140.0,0,1);DrawAlpha(g,oldPixels!,1-t);DrawAlpha(g,pixels,t);frame=mixedPixels;}
        if(surface==null||surface.Width!=frame.Width||surface.Height!=frame.Height){surface?.Dispose();surface=new(frame.Width,frame.Height);cachedSource=null;}
        if(transient||!ReferenceEquals(cachedSource,frame)){surface.Upload(frame);cachedSource=frame;}
        if(bar!=null)lastOrigin=new Native.Point((int)Math.Round(bar.Bounds.Left+bar.Bounds.Width/2.0-frame.Width/2.0),bar.Bounds.Top);
        var dst=lastOrigin;var size=new Native.Size(frame.Width,frame.Height);var src=new Native.Point();var pulse=blink.Enabled?TextPulse.Factor((Environment.TickCount64-blinkStart)*700/options.MagicBlinkPeriodMs):1;var blend=new Native.Blend{Alpha=(byte)Math.Clamp((int)Math.Round(alpha*pulse*255),0,255),Format=1};
        if(!Native.UpdateLayeredWindow(Handle,0,ref dst,ref size,surface.DC,ref src,0,ref blend,2))Log(new System.ComponentModel.Win32Exception());
    }
    private void ReleaseCached(){surface?.Dispose();surface=null;cachedSource=null;}
    private static void DrawAlpha(Graphics g,Bitmap bitmap,float alpha){using var attrs=new ImageAttributes();attrs.SetColorMatrix(new ColorMatrix{Matrix33=alpha});g.DrawImage(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attrs);}
    private void ScheduleNotice(){noticeTimer.Stop();if(shown&&model.NextTransitionAt is { } at){noticeTimer.Interval=Math.Max(1,(int)Math.Min(int.MaxValue,(at-DateTimeOffset.Now).TotalMilliseconds));noticeTimer.Start();}}
    private static void Diagnostic(string text)=>_ = Task.Run(()=>{
        try{lock(diagnosticGate){var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YoyoClawCompanion","magic-layout.log");Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(File.Exists(path)&&new FileInfo(path).Length>65536)File.WriteAllText(path,"");File.AppendAllText(path,$"{DateTimeOffset.Now:O} {text}\n");}}catch{}
    });
    private void WriteEvidence() {}
    private void Log(Exception ex)=>System.Diagnostics.Trace.WriteLine("Magic adapter: "+ex.GetType().Name+" "+ex.Message);
    private void Cleanup()
    {
        closing=true;
        foreach(var timer in new[]{fast,fallback,debounce,animation,blink,noticeTimer}){timer.Stop();timer.Dispose();}
        if(winHook!=0)Native.UnhookWinEvent(winHook);
        if(layoutWatch!=0){Native.StopLayoutWatch(layoutWatch);layoutWatch=0;}
        ReleaseCached();mixedPixels?.Dispose();painter.Dispose();pixels?.Dispose();oldPixels?.Dispose();GC.KeepAlive(layoutCallback);GC.KeepAlive(winCallback);
    }
}
