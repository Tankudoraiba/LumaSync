namespace LumaSync;

/// <summary>Builds the all-black frame sent when LumaSync exits or Windows ends the session.</summary>
public static class ShutdownFrameSender
{
    public static byte[] BuildBlackFrame(int ledCount, byte sequence)
    {
        if (ledCount is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(ledCount));
        return ScreenSyncFrameBuilder.Build(new ColorRgb[ledCount], sequence);
    }
}
