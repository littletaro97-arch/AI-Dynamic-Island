using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
namespace MagicStatusbarAdapter;

internal sealed class StatusPainter : IDisposable
{
    internal record Frame(Bitmap Pixels,InteractionMap Hits,string FitMode);
    private record Segment(string Text,bool Label,string? Dot=null,string? Provider=null,bool Separator=false);
    private record Run(string Text,Font Font,float Width);
    private readonly Bitmap measuring=new(1,1);
    private readonly Graphics measure;
    private readonly StringFormat format;
    private readonly SolidBrush ink=new(Color.FromArgb(247,249,252));
    private Font? normal,label,digits;
    private double currentScale;
    internal double CornerRadius {get;set;}=24;
    internal StatusPainter(){measure=Graphics.FromImage(measuring);format=(StringFormat)StringFormat.GenericTypographic.Clone();format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces|StringFormatFlags.NoWrap;}
    private void Fonts(double scale){if(normal!=null&&currentScale==scale)return;normal?.Dispose();label?.Dispose();digits?.Dispose();currentScale=scale;normal=new("Segoe UI",(float)(14*scale),FontStyle.Regular,GraphicsUnit.Pixel);label=new("Segoe UI Semibold",(float)(14*scale),FontStyle.Regular,GraphicsUnit.Pixel);digits=new("Segoe UI",(float)(14*scale),FontStyle.Bold,GraphicsUnit.Pixel);}
    internal static bool Numeric(char c)=>char.IsDigit(c)||c is '.' or '%' or ',';
    private List<Run> Runs(string text,bool bold){var runs=new List<Run>();for(int start=0;start<text.Length;){var numeric=Numeric(text[start]);int end=start+1;while(end<text.Length&&Numeric(text[end])==numeric)end++;var value=text[start..end];var font=numeric?digits!:bold?label!:normal!;runs.Add(new(value,font,measure.MeasureString(value,font,PointF.Empty,format).Width));start=end;}return runs;}
    internal Frame Paint(Snapshot snapshot,Notice? notice,int available,int height,double scale,int pending){
        Fonts(scale);var mute=notice?.AppId!=null&&notice.State=="notification";
        var buttonDesired=measure.MeasureString("不再通知",label!,PointF.Empty,format).Width+(float)(12*scale);
        var geometry=NotificationGeometry.Calculate(available,scale,buttonDesired);
        var textAvailable=mute?geometry.TextWidth:available-2;
        List<Segment> Segments(int pass){
            if(notice!=null)return [new(notice.Source+(notice.Count>1?" ×"+notice.Count:"")+(pending>0?" （+"+pending+"）":"")+" · ",true,Provider:ProviderLabels.ProcessName(notice.Source)!=null?notice.Source:null),new(notice.Text,false,Provider:ProviderLabels.ProcessName(notice.Source)!=null?notice.Source:null)];
            var segments=new List<Segment>();foreach(var item in snapshot.Items){if(segments.Count>0)segments.Add(new("",false,Separator:true));segments.Add(new((pass>0?ProviderLabels.Short(item.Name):item.Name)+" ",true,Provider:item.Name));var value=pass>=3||(pass>=2&&item.Name=="DeepSeek Harness")?"":pass==0?item.Value:item.ShortValue;segments.Add(new(value+(value.Length>0?"  ":""),false,item.Status,item.Name));}if(snapshot.Warning!=null)segments.Add(new("  "+snapshot.Warning,false));return segments;
        }
        var gap=27*scale;List<(Segment Segment,List<Run> Runs,float Width)> prepared=[];float total=0;string fit="full";
        for(int pass=0;pass<4;pass++){
            gap=(pass switch{0=>27,1=>18,2=>10,_=>7})*scale;
            prepared=Segments(pass).Select(s=>{var runs=Runs(s.Text,s.Label);return(s,runs,s.Separator?(float)gap:runs.Sum(r=>r.Width)+(s.Dot==null?0:(float)(6*scale)));}).ToList();total=prepared.Sum(s=>s.Width);fit=pass switch{0=>"full",1=>"short",2=>"compact",_=>"names"};if(total<=textAvailable)break;
        }
        bool ellipsis=total>textAvailable;
        if(ellipsis){var text=notice!=null?notice.Source+(notice.Count>1?" ×"+notice.Count:"")+(pending>0?" （+"+pending+"）":"")+" · "+notice.Text:string.Join(" · ",snapshot.Items.Select(i=>ProviderLabels.Short(i.Name)));var segment=new Segment(text,false,Provider:notice!=null&&ProviderLabels.ProcessName(notice.Source)!=null?notice.Source:null);prepared=[(segment,Runs(text,false),textAvailable)];total=textAvailable;fit="ellipsis";}
        var width=mute?available:Math.Max(1,Math.Min(available,(int)Math.Ceiling(total)+2));
        var bitmap=new Bitmap(width,Math.Max(1,height),PixelFormat.Format32bppPArgb);var hits=new InteractionMap();
        using var g=Graphics.FromImage(bitmap);g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        float x=(width-total)/2;float textRight=x+total;
        using var clickable=new SolidBrush(Color.FromArgb(1,0,0,0));
        foreach(var entry in prepared){
            var s=entry.Segment;
            if(s.Separator){using var pen=new Pen(Color.FromArgb(110,116,121,131),(float)scale);g.DrawLine(pen,x+(float)gap/2,(float)(10.5*scale),x+(float)gap/2,(float)(21.5*scale));x+=entry.Width;continue;}
            var bounds=new RectangleF(Math.Max(0,x-2*(float)scale),(float)(3*scale),Math.Min(width,x+entry.Width+2*(float)scale)-Math.Max(0,x-2*(float)scale),height-(float)(6*scale));if(bounds.Width>0){hits.Areas.Add(new(bounds,s.Provider));g.FillRectangle(clickable,bounds);}
            foreach(var run in entry.Runs){
                var value=run.Text;var length=run.Width;
                if(ellipsis&&x+length>textRight){var dots=measure.MeasureString("…",run.Font,PointF.Empty,format).Width;int lo=0,hi=value.Length;while(lo<hi){int mid=(lo+hi+1)/2;var w=measure.MeasureString(value[..mid],run.Font,PointF.Empty,format).Width;if(w+dots<=textRight-x)lo=mid;else hi=mid-1;}if(lo>0&&char.IsHighSurrogate(value[lo-1]))lo--;value=value[..lo]+"…";length=measure.MeasureString(value,run.Font,PointF.Empty,format).Width;g.SetClip(new RectangleF(x,0,Math.Max(1,textRight-x),height));g.DrawString(value,run.Font,ink,new PointF(x,(height-run.Font.GetHeight(g))/2),format);g.ResetClip();x=textRight;break;}
                g.DrawString(value,run.Font,ink,new PointF(x,(height-run.Font.GetHeight(g))/2),format);x+=length;
            }
            if(s.Dot is { } state){var color=state switch{"busy"=>Color.FromArgb(242,203,118),"attention"=>Color.FromArgb(242,149,131),"offline" or "stale" or "unknown"=>Color.FromArgb(161,164,174),_=>Color.FromArgb(113,216,186)};var size=(float)(6*scale);var rect=new RectangleF(x,(height-size)/2,size,size);using var brush=new SolidBrush(color);if(state=="busy"){using var pen=new Pen(color,(float)scale);g.DrawEllipse(pen,rect.X+(float)scale/2,rect.Y+(float)scale/2,size-(float)scale,size-(float)scale);}else g.FillEllipse(brush,rect);x+=size;}
            if(ellipsis)break;
        }
        if(mute){
            var button=new RectangleF(geometry.ButtonLeft,(float)(4*scale),geometry.ButtonWidth,height-(float)(8*scale));using var fill=new SolidBrush(Color.FromArgb(28,255,255,255));using var border=new Pen(Color.FromArgb(150,247,249,252),(float)scale);var buttonRadius=(float)YoyoClawCompanion.Services.NotificationAppearance.MuteRadius(CornerRadius*scale,button.Height);
            using var shape=RoundedButton(button,buttonRadius);g.FillPath(fill,shape);g.DrawPath(border,shape);
            if(geometry.FullLabel){var size=measure.MeasureString("不再通知",label!,PointF.Empty,format);g.DrawString("不再通知",label!,ink,new PointF(button.X+(button.Width-size.Width)/2,(height-label!.GetHeight(g))/2),format);}else{var cx=button.X+button.Width/2;var cy=height/2f;var radius=Math.Min(button.Width/3,(float)(6*scale));g.DrawEllipse(border,cx-radius,cy-radius,2*radius,2*radius);g.DrawLine(border,cx-radius,cy+radius,cx+radius,cy-radius);}
            hits.Areas.Add(new(button,null,HitAction.Mute));
        }
        return new(bitmap,hits,fit);
    }
    private static GraphicsPath RoundedButton(RectangleF bounds,float radius){
        var path=new GraphicsPath();var diameter=Math.Min(radius*2,Math.Min(bounds.Width-1,bounds.Height-1));
        if(diameter<=0){path.AddRectangle(bounds);return path;}
        var right=bounds.Right-1;var bottom=bounds.Bottom-1;
        path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);
        path.AddArc(right-diameter,bounds.Top,diameter,diameter,270,90);
        path.AddArc(right-diameter,bottom-diameter,diameter,diameter,0,90);
        path.AddArc(bounds.Left,bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
    public void Dispose(){normal?.Dispose();label?.Dispose();digits?.Dispose();format.Dispose();ink.Dispose();measure.Dispose();measuring.Dispose();}
}
