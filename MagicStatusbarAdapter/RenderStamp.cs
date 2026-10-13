namespace MagicStatusbarAdapter;
// Compare displayed values only. Preview bodies, timestamps and expiry dates do not change pixels.
internal sealed class RenderStamp
{
    private ProviderItem[] items=[];
    private Notice? notice;
    private string? warning;
    private int width,height,pending;
    private double scale;
    private bool initialized;
    internal bool Changed(Snapshot snapshot,Notice? current,int w,int h,double dpi,int queued){
        var same=initialized&&width==w&&height==h&&scale==dpi&&pending==queued&&warning==snapshot.Warning&&items.Length==snapshot.Items.Count&&SameNotice(notice,current);
        if(same)for(int i=0;i<items.Length;i++)if(!SameItem(items[i],snapshot.Items[i])){same=false;break;}
        if(same)return false;
        initialized=true;width=w;height=h;scale=dpi;pending=queued;warning=snapshot.Warning;notice=current;items=snapshot.Items.ToArray();return true;
    }
    internal void Reset()=>initialized=false;
    private static bool SameItem(ProviderItem a,ProviderItem b)=>a.Name==b.Name&&a.Value==b.Value&&a.ShortValue==b.ShortValue&&a.Status==b.Status;
    private static bool SameNotice(Notice? a,Notice? b)=>ReferenceEquals(a,b)||(a!=null&&b!=null&&a.Source==b.Source&&a.Text==b.Text&&a.State==b.State&&a.Count==b.Count&&a.Key==b.Key&&a.MessageId==b.MessageId&&a.AppId==b.AppId&&a.Title==b.Title&&a.OriginalSource==b.OriginalSource);
}
