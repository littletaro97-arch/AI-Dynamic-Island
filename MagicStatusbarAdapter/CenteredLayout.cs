namespace MagicStatusbarAdapter;

internal readonly record struct CenteredSpan(double Center,int Width)
{
    internal static CenteredSpan Calculate(int barLeft,int barWidth,double brandRight,double systemLeft,double scale)
    {
        var center=barLeft+barWidth/2.0;
        var left=brandRight+22*scale;var right=systemLeft-22*scale;
        // Keep true screen center, never drift into a vendor control to fit a longer line.
        return new(center,(int)Math.Max(0,Math.Floor(2*Math.Min(center-left,right-center))));
    }
}
