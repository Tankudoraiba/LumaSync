namespace LumaSync;

/// <summary>
/// Reference-derived 64-byte HID static-color report for 1A86:FE07.
/// This reference-derived serializer is used by the explicit static-color action for 1A86:FE07.
/// </summary>
public static class CandidateHidColorReport
{
    public const int ReportLength = 64;

    public static byte[] Build(ColorRgb color, byte sequence)
    {
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Reference examples use a nonzero sequence byte.");
        byte[] report = new byte[ReportLength];
        byte[] header = [0x52, 0x42, 0x10, sequence, 0x86, 0x01, color.R, color.G, color.B, 0x3F, 0x40, 0x00, 0x00, 0x00, 0xFE];
        header.CopyTo(report, 0);
        byte checksum = 0;
        for (var i = 0; i < header.Length; i++) checksum = unchecked((byte)(checksum + header[i]));
        report[15] = checksum;
        return report;
    }
}
