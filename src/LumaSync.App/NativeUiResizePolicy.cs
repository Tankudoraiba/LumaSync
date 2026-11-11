namespace LumaSync;

/// <summary>Constrains user-driven window resizing to the canvas aspect ratio.</summary>
public static class NativeUiResizePolicy
{
    public static (int Left, int Top, int Right, int Bottom) Constrain(
        int left, int top, int right, int bottom, uint sizingEdge, int frameWidth, int frameHeight)
    {
        if (right <= left || bottom <= top || frameWidth < 0 || frameHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(right), "Window bounds and frame dimensions must be valid.");

        var clientWidth = Math.Max(1, right - left - frameWidth);
        var clientHeight = Math.Max(1, bottom - top - frameHeight);
        var ratio = NativeUiWindowPolicy.LogicalWidth / (double)NativeUiWindowPolicy.LogicalHeight;
        var widthEdge = sizingEdge is 1 or 4 or 7;
        var rightEdge = sizingEdge is 2 or 5 or 8;
        var topEdge = sizingEdge is 3 or 4 or 5;
        var bottomEdge = sizingEdge is 6 or 7 or 8;

        if (clientWidth / (double)clientHeight > ratio)
        {
            var newHeight = (int)Math.Round(clientWidth / ratio) + frameHeight;
            if (topEdge) top = bottom - newHeight;
            else if (bottomEdge) bottom = top + newHeight;
            else
            {
                var center = top + (bottom - top) / 2;
                top = center - newHeight / 2;
                bottom = top + newHeight;
            }
        }
        else
        {
            var newWidth = (int)Math.Round(clientHeight * ratio) + frameWidth;
            if (widthEdge) left = right - newWidth;
            else if (rightEdge) right = left + newWidth;
            else
            {
                var center = left + (right - left) / 2;
                left = center - newWidth / 2;
                right = left + newWidth;
            }
        }

        return (left, top, right, bottom);
    }
}
