namespace LumaSync;

/// <summary>Builds reference-shaped SC per-LED frames; it does not perform I/O.</summary>
public static class ScreenSyncFrameBuilder
{
    public static byte[] Build(IReadOnlyList<ColorRgb> colors, byte sequence)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(colors), "Expected 1–240 LED colors.");
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence zero is reserved by the reference protocol.");

        var length = checked(7 + colors.Count * 5);
        var frame = new byte[length];
        BuildInto(colors, sequence, frame);
        return frame;
    }

    /// <summary>Serializes into caller-owned storage for a steady-state allocation-free frame loop.</summary>
    public static void BuildInto(IReadOnlyList<ColorRgb> colors, byte sequence, Span<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(colors), "Expected 1–240 LED colors.");
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence zero is reserved by the reference protocol.");
        var length = checked(7 + colors.Count * 5);
        if (frame.Length < length) throw new ArgumentException("Destination is shorter than the serialized frame.", nameof(frame));
        frame = frame[..length];
        frame[0] = 0x53; frame[1] = 0x43;
        frame[2] = (byte)(length >> 8); frame[3] = (byte)length;
        frame[4] = sequence; frame[5] = 0x80;
        for (var i = 0; i < colors.Count; i++)
        {
            var offset = 6 + i * 5;
            var led = checked((byte)(i + 1));
            frame[offset] = led;
            frame[offset + 1] = colors[i].R;
            frame[offset + 2] = colors[i].G;
            frame[offset + 3] = colors[i].B;
            frame[offset + 4] = led;
        }
        byte checksum = 0;
        for (var i = 0; i < frame.Length - 1; i++) checksum = unchecked((byte)(checksum + frame[i]));
        frame[^1] = checksum;
    }

    /// <summary>Applies the verified HID report-ID-zero prefix and 64-byte payload chunking.</summary>
    public static IReadOnlyList<byte[]> ToWindowsHidReports(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty) throw new ArgumentException("Frame cannot be empty.", nameof(frame));
        var reports = new List<byte[]>((frame.Length + 63) / 64);
        for (var offset = 0; offset < frame.Length; offset += 64)
        {
            var report = new byte[65]; // report ID plus the 64-byte HID output payload
            var count = Math.Min(64, frame.Length - offset);
            frame.Slice(offset, count).CopyTo(report.AsSpan(1));
            reports.Add(report);
        }
        return reports;
    }
}
