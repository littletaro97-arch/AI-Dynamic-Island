using System.Drawing;

namespace MagicStatusbarAdapter;

internal sealed record HitArea(RectangleF Bounds,string? Provider);
internal sealed class InteractionMap
{
    internal readonly List<HitArea> Areas=[];
    internal HitArea? Find(Point point)=>Areas.FirstOrDefault(a=>a.Bounds.Contains(point));
}
