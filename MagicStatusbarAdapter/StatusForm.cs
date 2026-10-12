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
    private ContextMenuStrip? providerMenu;
    private readonly Native.WinEventCallback winCallback;
    private readonly Native.LayoutChangeCallback layoutCallback;
    
    private readonly Action<string> openProvider;
    private readonly Action openSettings,disableAdapter;
    private AdapterOptions options=new();
    private Snapshot? latest;
    private nint winHook,layoutWatch;
    private Native.Window? bar;
    private Native.Layout? layout;
    private Bitmap? pixels,oldPixels;
    private long blinkStart;
    private string blinkKey="";
    private Native.Point lastOrigin;
    private nint cachedDC,cachedBitmap,cachedOriginal;
    private Bitmap? cachedSource;
    private bool closing,shown,stopping;
    private string renderKey="",fitMode="full";
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
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);var area=hits.Find(e.Location);if(area==null)return;
        if(e.Button==MouseButtons.Right)ShowProviderMenu(area.Provider,e.Location);
        else if(e.Button==MouseButtons.Left){if(area.Provider!=null)OpenProvider(area.Provider);else ShowProviderMenu(null,e.Location);}
    }
    private void OpenProvider(string name){if(options.EnableAppLaunch)openProvider(name);}
    private void ShowProviderMenu(string? selected,Point position)
    {
        providerMenu?.Dispose();providerMenu=new ContextMenuStrip();
        if(selected!=null){providerMenu.Items.Add("打开 "+selected,null,(_,_)=>OpenProvider(selected));providerMenu.Items.Add(new ToolStripSeparator());}
        foreach(var item in model.Current.Items.Where(i=>ProviderLabels.ProcessName(i.Name)!=null))providerMenu.Items.Add("打开 "+item.Name,null,(_,_)=>OpenProvider(item.Name));
        providerMenu.Items.Add(new ToolStripSeparator());providerMenu.Items.Add("设置",null,(_,_)=>openSettings());providerMenu.Items.Add("关闭 Magic 适配",null,(_,_)=>disableAdapter());providerMenu.Show(this,position);
    }
    private void KeepAboveBar(){if(shown&&bar!=null&&providerMenu?.Visible!=true){var previous=Native.GetWindow(bar.Handle,3);if(previous!=Handle)Native.SetWindowPos(Handle,previous==0?(nint)(-1):previous,0,0,0,0,0x13);}}
    internal StatusForm(Action<string> launch,Action settings,Action disable)
    {
        openProvider=launch;openSettings=settings;disableAdapter=disable;
        winCallback=WinEvent;layoutCallback=LayoutEvent;
        Text="AI Dynamic Island · Magic";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;AutoScaleMode=AutoScaleMode.None;
        Size=new(800,64);StartPosition=FormStartPosition.Manual;Location=new(0,0);
        Shown+=(_,_)=>Initialize();FormClosed+=(_,_)=>Cleanup();
        fast.Tick+=(_,_)=>FastGeometry();fallback.Tick+=(_,_)=>{if(bar==null||!Native.IsWindow(bar.Handle))Attach();FastGeometry();};
        debounce.Tick+=(_,_)=>{debounce.Stop();ReadLayoutCache();};
        animation.Tick+=(_,_)=>Animate();blink.Tick+=(_,_)=>{if(!shown){blink.Stop();return;}if(model.CurrentNotice?.State!="attention"&&Environment.TickCount64-blinkStart>=options.MagicBlinkPeriodMs*2){blink.Stop();Submit();return;}Submit();};
        noticeTimer.Tick+=(_,_)=>{noticeTimer.Stop();model.Expire(DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();};
    }
    internal void Configure(string json){var previous=options;options=JsonSerializer.Deserialize<AdapterOptions>(json)??new();options.LastChangeAt=previous.LastChangeAt;options.Fingerprint=previous.Fingerprint;model.RoundSeconds=options.MagicNotificationSeconds;if(!options.ShowCodex||!options.EnableCodexConfirmationNotifications)model.SetCodexQuestion(null,null,DateTimeOffset.Now);if(latest!=null)ApplyLatest();renderKey="";Render();FastGeometry();}
    internal void Update(string json)=>Dispatch(()=>{latest=StatusModel.Parse(json,DateTimeOffset.Now);ApplyLatest();});
    private void ApplyLatest(){
        if(latest==null)return;
        var items=latest.Items.Where(i=>options.Visible(i.Name)).Select(i=>options.Credits(i.Name)?i:i with{Value="",ShortValue=""}).OrderBy(i=>options.Order(i.Name)).ToList();
        var fingerprint=JsonSerializer.Serialize(items);if(fingerprint!=options.Fingerprint){options.Fingerprint=fingerprint;options.LastChangeAt=DateTimeOffset.Now;}
        items=items.Select(i=>i.Status=="attention"&&!options.EnableConfirmationNotifications?i with{Status="idle",Value="",ShortValue=""}:i).ToList();
        model.Apply(new(items,latest.Warning),DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();
    }
    internal void Notify(string source,string title,string body,string key){model.NewSystemNotice(source,DateTimeOffset.Now,title,body,key);Render();FastGeometry();ScheduleNotice();}
    internal void Question(string? id,string? prompt){model.SetCodexQuestion(options.ShowCodex&&options.EnableCodexConfirmationNotifications?id:null,prompt,DateTimeOffset.Now);Render();FastGeometry();ScheduleNotice();}
    internal void Stop(){if(stopping)return;stopping=true;fast.Stop();fallback.Stop();debounce.Stop();model.SetVisible(false,DateTimeOffset.Now);SetShown(false);var done=new System.Windows.Forms.Timer{Interval=100};done.Tick+=(_,_)=>{done.Stop();done.Dispose();Close();};done.Start();}
    private void Dispatch(Action action){if(closing||stopping||!IsHandleCreated)return;try{BeginInvoke(()=>{if(!closing&&!stopping)action();});}catch(InvalidOperationException){}}
    private void Initialize(){model.SetVisible(false,DateTimeOffset.Now);Attach();fast.Start();fallback.Start();}
    private void Attach()
    {
        var candidate=Native.Windows().Where(w=>w.Title=="Magictopbar"&&w.Bounds.Width>600).OrderBy(w=>Math.Abs(w.Bounds.Top)).FirstOrDefault();
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
        if(hr==0){layout=value;Render();FastGeometry();}else if(hr!=unchecked((int)0x8000000A)){layout=null;SetShown(false);}
        WriteEvidence();
    }
    private void FastGeometry()
    {
        if(closing||bar==null)return;
        if(!Native.IsWindow(bar.Handle)||!Native.OnScreen(bar)||!Native.BarExposed(bar)){Native.SetWatchActive(layoutWatch,false);model.SetVisible(false,DateTimeOffset.Now);SetShown(false);return;}
        Native.SetWatchActive(layoutWatch,true);
        Native.GetWindowRect(bar.Handle,out var bounds);var moved=bounds.Left!=bar.Bounds.Left||bounds.Top!=bar.Bounds.Top||bounds.Width!=bar.Bounds.Width;
        bar=bar with{Bounds=bounds,Dpi=Native.GetDpiForWindow(bar.Handle)};
        if(moved)Native.RefreshLayout(layoutWatch);
        var exposed=layout is {Visible: not 0} l&&l.Left-l.BrandRight>100;
        var before=model.CurrentNotice;model.SetVisible(exposed,DateTimeOffset.Now);
        if(before!=model.CurrentNotice){Render();ScheduleNotice();}
        SetShown(exposed);
        KeepAboveBar();
        if(pixels!=null&&moved)Submit();
    }
    private record Segment(string Text,bool Bold,string? Dot=null,string? Provider=null);
    private List<Segment> Segments(int pass)
    {
        if(model.CurrentNotice is { } notice){var provider=ProviderLabels.ProcessName(notice.Source)!=null?notice.Source:null;return[new(NoticePrefix(notice),true,Provider:provider),new(notice.Text,false,Provider:provider)];}
        var items=model.Current.Items;
        var result=new List<Segment>();
        foreach(var item in items){if(result.Count>0)result.Add(new("separator",false));result.Add(new((pass>0?ProviderLabels.Short(item.Name):item.Name)+" ",true,Provider:item.Name));var value=pass>=3||(pass>=2&&item.Name=="DeepSeek Harness")?"":pass==0?item.Value:item.ShortValue;result.Add(new(value+(value.Length>0?"  ":""),false,item.Status,item.Name));}
        if(model.Current.Warning!=null)result.Add(new("  "+model.Current.Warning,false));return result;
    }
    private string NoticePrefix(Notice notice)=>notice.Source+(notice.Count>1?" ×"+notice.Count:"")+(model.PendingCount>0?" （+"+model.PendingCount+"）":"")+" · ";
    private void Render()
    {
        if(closing||bar==null||layout is not { } l)return;
        var scale=bar.Dpi/96.0;var span=CenteredSpan.Calculate(bar.Bounds.Left,bar.Bounds.Width,l.BrandRight,l.Left,scale);var available=Math.Max(1,span.Width);
        var key=JsonSerializer.Serialize(model.Current)+JsonSerializer.Serialize(model.CurrentNotice)+available+"|"+scale+"|"+model.PendingCount;if(key==renderKey)return;renderKey=key;
        using var canvas=new Bitmap(1,1);using var measure=Graphics.FromImage(canvas);using var bold=new Font("Segoe UI Semibold",(float)(14*scale),FontStyle.Regular,GraphicsUnit.Pixel);using var normal=new Font("Segoe UI",(float)(14*scale),FontStyle.Regular,GraphicsUnit.Pixel);
        using var format=(StringFormat)StringFormat.GenericTypographic.Clone();format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces|StringFormatFlags.NoWrap;
        var separatorDip=27;
        float Measure(Segment s)=>s.Text=="separator"?(float)(separatorDip*scale):measure.MeasureString(s.Text,s.Bold?bold:normal,PointF.Empty,format).Width+(s.Dot==null?0:(float)(6*scale));
        List<Segment> segments=[];float width=0;fitMode="full";
        for(var pass=0;pass<4;pass++){separatorDip=pass switch{0=>27,1=>18,2=>10,_=>7};segments=Segments(pass);width=segments.Sum(Measure);fitMode=pass switch{0=>"full",1=>"short",2=>"compact",_=>"names"};if(width+2<=available)break;}
        if(width+2>available){segments=[new(model.CurrentNotice is { } n?NoticePrefix(n)+n.Text:string.Join(" · ",model.Current.Items.Select(i=>ProviderLabels.Short(i.Name))),false,Provider:model.CurrentNotice is { } notice&&ProviderLabels.ProcessName(notice.Source)!=null?notice.Source:null)];width=available-2;fitMode="ellipsis";format.Trimming=StringTrimming.EllipsisCharacter;}
        var next=new Bitmap(Math.Max(1,Math.Min(available,(int)Math.Ceiling(width)+2)),Math.Max(1,bar.Bounds.Height),PixelFormat.Format32bppPArgb);
        var nextHits=new InteractionMap();float hitX=0;
        foreach(var segment in segments){var length=fitMode=="ellipsis"?next.Width:Measure(segment);if(segment.Text!="separator"){var left=Math.Max(0,hitX-2*(float)scale);var right=Math.Min(next.Width,hitX+length+2*(float)scale);if(right>left)nextHits.Areas.Add(new(new RectangleF(left,(float)(3*scale),right-left,next.Height-(float)(6*scale)),segment.Provider));}hitX+=length;}
        using(var g=Graphics.FromImage(next)){g.Clear(Color.Transparent);using(var clickable=new SolidBrush(Color.FromArgb(1,0,0,0)))foreach(var area in nextHits.Areas)g.FillRectangle(clickable,area.Bounds);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;float x=0;using var ink=new SolidBrush(Color.FromArgb(247,249,252));
            foreach(var segment in segments){if(segment.Text=="separator"){using var line=new Pen(Color.FromArgb(110,116,121,131),(float)scale);var middle=(float)(separatorDip*scale/2);g.DrawLine(line,x+middle,(float)(10.5*scale),x+middle,(float)(21.5*scale));x+=Measure(segment);continue;}
                var font=segment.Bold?bold:normal;var y=(next.Height-font.GetHeight(g))/2;
                if(fitMode=="ellipsis")g.DrawString(segment.Text,font,ink,new RectangleF(0,y,next.Width,next.Height-y),format);else g.DrawString(segment.Text,font,ink,new PointF(x,y),format);
                x+=measure.MeasureString(segment.Text,font,PointF.Empty,format).Width;
                if(segment.Dot is { } state){var color=state switch{"busy"=>Color.FromArgb(242,203,118),"attention"=>Color.FromArgb(242,149,131),"offline" or "stale" or "unknown"=>Color.FromArgb(161,164,174),_=>Color.FromArgb(113,216,186)};var size=(float)(6*scale);var rect=new RectangleF(x,(next.Height-size)/2,size,size);using var dot=new SolidBrush(color);if(state=="busy"){using var pen=new Pen(color,(float)scale);g.DrawEllipse(pen,rect.X+(float)scale/2,rect.Y+(float)scale/2,rect.Width-(float)scale,rect.Height-(float)scale);}else g.FillEllipse(dot,rect);x+=size;}
            }}
        hits=nextHits;oldPixels?.Dispose();oldPixels=pixels;pixels=next;contentStart=Environment.TickCount64;
        if(oldPixels!=null)animation.Start();UpdateBlink();Submit();
    }
    private void SetShown(bool visible)
    {
        if(visible&&bar!=null&&layout is { } l)visible=CenteredSpan.Calculate(bar.Bounds.Left,bar.Bounds.Width,l.BrandRight,l.Left,bar.Dpi/96.0).Width>=80;
        visible=visible&&(model.CurrentNotice!=null||(model.Current.Items.Count>0&&(!options.ActiveOnly||model.Current.Items.Any(i=>i.Status=="busy"))&&(!options.EnableUnchangedAutoHide||DateTimeOffset.Now-options.LastChangeAt<TimeSpan.FromMinutes(options.UnchangedAutoHideMinutes))));
        if(shown==visible)return;shown=visible;fadeFrom=alpha;fadeTarget=visible?1:0;fadeStart=Environment.TickCount64;animation.Start();if(visible)Render();UpdateBlink();ScheduleNotice();WriteEvidence();
    }
    private void UpdateBlink(){var notice=model.CurrentNotice;var key=notice?.Key??notice?.Source+"|"+notice?.Text;if(key!=blinkKey){blinkKey=key??"";blinkStart=Environment.TickCount64;}if(shown&&notice!=null&&(notice.State=="attention"||Environment.TickCount64-blinkStart<options.MagicBlinkPeriodMs*2))blink.Start();else blink.Stop();}
    private void Animate()
    {
        var now=Environment.TickCount64;var ratio=Math.Clamp((now-fadeStart)/(fadeTarget==0?70.0:100.0),0,1);alpha=fadeFrom+(fadeTarget-fadeFrom)*ratio;
        if(oldPixels!=null&&now-contentStart>=140){oldPixels.Dispose();oldPixels=null;}
        Submit();if(ratio>=1&&oldPixels==null)animation.Stop();
    }
    private void Submit()
    {
        if(pixels==null||closing)return;
        var frame=pixels;
        Bitmap? mixed=null;
        if(oldPixels!=null){mixed=new Bitmap(pixels.Width,pixels.Height,PixelFormat.Format32bppPArgb);using var g=Graphics.FromImage(mixed);var t=(float)Math.Clamp((Environment.TickCount64-contentStart)/140.0,0,1);DrawAlpha(g,oldPixels,1-t);DrawAlpha(g,pixels,t);frame=mixed;}
        var transient=mixed!=null;var dc=Native.GetDC(0);nint memory,bitmap=0,previous=0;
        if(transient){memory=Native.CreateCompatibleDC(dc);bitmap=frame.GetHbitmap(Color.FromArgb(0));previous=Native.SelectObject(memory,bitmap);}
        else{if(!ReferenceEquals(cachedSource,frame)){ReleaseCached();cachedDC=Native.CreateCompatibleDC(dc);cachedBitmap=frame.GetHbitmap(Color.FromArgb(0));cachedOriginal=Native.SelectObject(cachedDC,cachedBitmap);cachedSource=frame;}memory=cachedDC;}
        try{if(bar!=null)lastOrigin=new Native.Point((int)Math.Round(bar.Bounds.Left+bar.Bounds.Width/2.0-frame.Width/2.0),bar.Bounds.Top);var dst=lastOrigin;var size=new Native.Size(frame.Width,frame.Height);var src=new Native.Point();var pulse=blink.Enabled ? TextPulse.Factor((Environment.TickCount64-blinkStart)*700/options.MagicBlinkPeriodMs) : 1;var blend=new Native.Blend{Alpha=(byte)Math.Clamp((int)Math.Round(alpha*pulse*255),0,255),Format=1};if(!Native.UpdateLayeredWindow(Handle,dc,ref dst,ref size,memory,ref src,0,ref blend,2))Log(new System.ComponentModel.Win32Exception());}
        finally{if(transient){Native.SelectObject(memory,previous);Native.DeleteObject(bitmap);Native.DeleteDC(memory);}Native.ReleaseDC(0,dc);mixed?.Dispose();}
    }
    private void ReleaseCached(){if(cachedDC!=0){Native.SelectObject(cachedDC,cachedOriginal);Native.DeleteObject(cachedBitmap);Native.DeleteDC(cachedDC);}cachedDC=0;cachedBitmap=0;cachedSource=null;}
    private static void DrawAlpha(Graphics g,Bitmap bitmap,float alpha){using var attrs=new ImageAttributes();attrs.SetColorMatrix(new ColorMatrix{Matrix33=alpha});g.DrawImage(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attrs);}
    private void ScheduleNotice(){noticeTimer.Stop();if(shown&&model.NextTransitionAt is { } at){noticeTimer.Interval=Math.Max(1,(int)Math.Min(int.MaxValue,(at-DateTimeOffset.Now).TotalMilliseconds));noticeTimer.Start();}}
    private void WriteEvidence() {}
    private void Log(Exception ex)=>System.Diagnostics.Trace.WriteLine("Magic adapter: "+ex.GetType().Name+" "+ex.Message);
    private void Cleanup()
    {
        closing=true;
        foreach(var timer in new[]{fast,fallback,debounce,animation,blink,noticeTimer}){timer.Stop();timer.Dispose();}
        if(winHook!=0)Native.UnhookWinEvent(winHook);
        if(layoutWatch!=0){Native.StopLayoutWatch(layoutWatch);layoutWatch=0;}
        providerMenu?.Dispose();ReleaseCached();pixels?.Dispose();oldPixels?.Dispose();GC.KeepAlive(layoutCallback);GC.KeepAlive(winCallback);
    }
}
