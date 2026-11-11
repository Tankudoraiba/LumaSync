namespace LumaSync;

/// <summary>Maps client coordinates onto the fixed logical canvas used by the low-memory native UI.</summary>
public readonly record struct NativeUiCoordinateTransform(int LogicalWidth, int LogicalHeight, int ClientWidth, int ClientHeight)
{
    public NativeUiCoordinateTransform Validate()
    {
        if (LogicalWidth <= 0 || LogicalHeight <= 0 || ClientWidth <= 0 || ClientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(LogicalWidth), "UI canvas dimensions must be positive.");
        return this;
    }

    public (int X, int Y) ToLogical(int clientX, int clientY)
    {
        Validate();
        var scale = Math.Min(ClientWidth / (double)LogicalWidth, ClientHeight / (double)LogicalHeight);
        var viewportWidth = LogicalWidth * scale;
        var viewportHeight = LogicalHeight * scale;
        var offsetX = (ClientWidth - viewportWidth) * .5;
        var offsetY = (ClientHeight - viewportHeight) * .5;
        return ((int)Math.Floor((clientX - offsetX) / scale),
            (int)Math.Floor((clientY - offsetY) / scale));
    }
}
