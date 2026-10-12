namespace MagicStatusbarAdapter;
// A bounded retry for failed UIA samples; actual hide/fullscreen is never delayed.
internal sealed class LayoutRecovery
{
    private long? failedAt;
    internal void Valid()=>failedAt=null;
    internal bool Retain(bool hasLayout,bool physicallyVisible,long now){
        if(!hasLayout||!physicallyVisible){failedAt=null;return false;}
        failedAt??=now;return now-failedAt<=250;
    }
}
