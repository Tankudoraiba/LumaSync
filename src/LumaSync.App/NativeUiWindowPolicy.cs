namespace LumaSync;

/// <summary>Client-size policy and Win32 style for the low-memory main window.</summary>
public static class NativeUiWindowPolicy
{
    private const uint Caption = 0x00C00000;
    private const uint SystemMenu = 0x00080000;
    private const uint MinimizeBox = 0x00020000;
    private const uint SizeBox = 0x00040000;
    private const uint MaximizeBox = 0x00010000;

    public const int LogicalWidth = 920;
    public const int LogicalHeight = 800;
    public const int InitialClientWidth = 1012;
    public const int InitialClientHeight = 880;
    public const int MinimumClientWidth = LogicalWidth;
    public const int MinimumClientHeight = LogicalHeight;
    public const uint SizeMessage = 0x0005;
    public const uint SizingMessage = 0x0214;
    public const uint ResizableWindowStyle = Caption | SystemMenu | MinimizeBox | SizeBox;

    public static bool CanResize(uint style) => (style & SizeBox) != 0;
    public static bool CanMaximize(uint style) => (style & MaximizeBox) != 0;
    public static bool IsResizeMessage(uint message) => message == SizeMessage;
    public static bool IsSizingMessage(uint message) => message == SizingMessage;
}
