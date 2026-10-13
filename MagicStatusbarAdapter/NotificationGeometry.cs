namespace MagicStatusbarAdapter;
internal readonly record struct NotificationGeometry(int FrameWidth,float TextWidth,float ButtonLeft,float ButtonWidth,bool FullLabel)
{
    internal static NotificationGeometry Calculate(int available,double scale,float desiredButtonWidth){
        var button=Math.Min(desiredButtonWidth,Math.Max(1,available/4f));
        var gap=(float)Math.Min(6*scale,available/16d);
        return new(available,Math.Max(1,available-2*(button+gap)),available-button,button,button>=desiredButtonWidth-.5f);
    }
}
