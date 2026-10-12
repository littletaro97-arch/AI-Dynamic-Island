namespace MagicStatusbarAdapter;

internal static class TextPulse
{
    internal const int PeriodMs=700;
    internal const double MinimumAlpha=0.12;
    // Keep the ink color fixed. Only its opacity changes, with a readable full-bright phase.
    internal static double Factor(long milliseconds)=>MinimumAlpha+(1-MinimumAlpha)*(0.5+0.5*Math.Cos(milliseconds*2*Math.PI/PeriodMs));
}
