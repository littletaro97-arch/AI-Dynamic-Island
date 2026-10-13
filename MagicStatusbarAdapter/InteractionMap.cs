using System.Drawing;

namespace MagicStatusbarAdapter;

internal enum HitAction { Content, Mute }
internal sealed record HitArea(RectangleF Bounds,string? Provider,HitAction Action=HitAction.Content);
internal sealed class InteractionMap
{
    internal readonly List<HitArea> Areas=[];
    internal HitArea? Find(Point point){foreach(var area in Areas)if(area.Bounds.Contains(point))return area;return null;}
}
