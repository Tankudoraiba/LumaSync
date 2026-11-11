namespace LumaSync;

public interface ILightingController : IDisposable
{
    ControllerDeviceInfo Info { get; }
    void WriteScreenSyncFrame(ReadOnlySpan<byte> frame);
    void WriteStaticColor(ColorRgb color, byte sequence);
}
