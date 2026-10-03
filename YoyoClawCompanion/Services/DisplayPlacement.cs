using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace YoyoClawCompanion.Services;

internal sealed record DisplayInfo(string Id, string DeviceName, bool IsPrimary, Rect Bounds)
{
    public bool IsPortrait => Bounds.Height > Bounds.Width;
    public string Label => $"{(IsPrimary ? "主屏幕" : "副屏幕")} · {(IsPortrait ? "竖屏" : "横屏")}";
}

internal sealed class DisplayPosition
{
    public string Preset { get; set; } = "topCenter";
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
}

internal static class DisplayPlacement
{
    internal const string PrimaryId = "primary";
    public static DisplayInfo Resolve(IReadOnlyList<DisplayInfo> displays, string? preferredId)
        => displays.FirstOrDefault(d => d.Id == preferredId)
           ?? displays.First(d => d.IsPrimary);

    public static Point Target(Rect display, Size island, DisplayPosition position)
    {
        const double padding = 8;
        var preset = position.Preset;
        var x = preset switch
        {
            "topLeft" or "bottomLeft" => padding,
            "topRight" or "bottomRight" => display.Width - island.Width - padding,
            "custom" => position.OffsetX ?? (display.Width - island.Width) / 2,
            _ => (display.Width - island.Width) / 2
        };
        var y = preset == "custom" ? position.OffsetY ?? padding
            : preset.StartsWith("bottom", StringComparison.Ordinal) ? display.Height - island.Height - padding : padding;
        return new Point(display.Left + Math.Clamp(double.IsFinite(x) ? x : padding, 0, Math.Max(0, display.Width - island.Width)),
            display.Top + Math.Clamp(double.IsFinite(y) ? y : padding, 0, Math.Max(0, display.Height - island.Height)));
    }
}
