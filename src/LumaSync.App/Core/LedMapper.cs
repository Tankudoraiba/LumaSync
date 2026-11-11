namespace LumaSync;
public enum LedEdge { Top, Right, Bottom, Left }
public enum LedCorner { TopLeft, TopRight, BottomRight, BottomLeft }
public readonly record struct LedSample(LedEdge Edge, double U, double V);
public sealed record LayoutOptions(int Top=26, int Right=14, int Bottom=0, int Left=14, double Depth=.08, bool Clockwise=true, LedCorner StartCorner=LedCorner.BottomLeft)
{
    public IReadOnlyList<LedSample> Create()
    {
        if (Top<0 || Top>120 || Right<0 || Right>120 || Bottom<0 || Bottom>120 || Left<0 || Left>120 || Top+Right+Bottom+Left is <1 or >240) throw new ArgumentOutOfRangeException(nameof(Top),"LED counts must total 1–240; each edge must be 0–120.");
        if (Depth is <0.005 or >0.5) throw new ArgumentOutOfRangeException(nameof(Depth));
        ReadOnlySpan<LedEdge> order=Clockwise ? [LedEdge.Top,LedEdge.Right,LedEdge.Bottom,LedEdge.Left] : [LedEdge.Left,LedEdge.Bottom,LedEdge.Right,LedEdge.Top];
        int first=Clockwise ? (int)StartCorner : StartCorner switch { LedCorner.TopLeft=>0, LedCorner.TopRight=>3, LedCorner.BottomRight=>2, _=>1 };
        var result=new LedSample[Top+Right+Bottom+Left];
        var position=0;
        for(var step=0;step<order.Length;step++)
        {
            var edge=order[(first+step)%order.Length];
            var count=edge switch { LedEdge.Top=>Top, LedEdge.Right=>Right, LedEdge.Bottom=>Bottom, _=>Left };
            for(int i=0;i<count;i++)
            {
                var t=(i+.5)/count;
                result[position++]=edge switch
                {
                    LedEdge.Top => new(edge,Clockwise?t:1-t,Depth*.5),
                    LedEdge.Right => new(edge,1-Depth*.5,Clockwise?t:1-t),
                    LedEdge.Bottom => new(edge,Clockwise?1-t:t,1-Depth*.5),
                    _ => new(edge,Depth*.5,Clockwise?1-t:t)
                };
            }
        }
        return result;
    }
}

public static class LedMapper
{
    /// <summary>Reorders strip-indexed colors into left-to-right or top-to-bottom preview order.</summary>
    public static ColorRgb[] ArrangeEdgePreview(IReadOnlyList<LedSample> layout, IReadOnlyList<ColorRgb> colors, LedEdge edge)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(colors);
        if (layout.Count != colors.Count) throw new ArgumentException("There must be one color per mapped LED.", nameof(colors));
        var horizontal = edge is LedEdge.Top or LedEdge.Bottom;
        return Enumerable.Range(0, layout.Count)
            .Where(i => layout[i].Edge == edge)
            .OrderBy(i => horizontal ? layout[i].U : layout[i].V)
            .Select(i => colors[i])
            .ToArray();
    }
}
