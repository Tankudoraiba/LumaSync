namespace LumaSync;

/// <summary>Applies one-step edge-count edits while preserving all other saved settings.</summary>
public static class LedLayoutEditor
{
    public static AppSettings ChangeOrientation(AppSettings settings, bool clockwise, LedCorner startCorner)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(startCorner)) throw new ArgumentOutOfRangeException(nameof(startCorner));
        var changed = settings with { Clockwise = clockwise, StartCorner = startCorner };
        changed.Validate();
        return changed;
    }

    public static AppSettings ChangeEdgeCount(AppSettings settings, LedEdge edge, int delta)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(edge)) throw new ArgumentOutOfRangeException(nameof(edge));
        if (delta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(delta));
        var changed = edge switch
        {
            LedEdge.Top => settings with { Top = settings.Top + delta },
            LedEdge.Right => settings with { Right = settings.Right + delta },
            LedEdge.Bottom => settings with { Bottom = settings.Bottom + delta },
            LedEdge.Left => settings with { Left = settings.Left + delta },
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
        changed.Validate();
        return changed;
    }
}
