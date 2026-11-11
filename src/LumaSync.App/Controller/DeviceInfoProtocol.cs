namespace LumaSync;

public sealed record ControllerDeviceInfo(string DeviceId, byte DisplaySize, byte LedCount, string Uuid, Version Firmware);

public static class DeviceInfoProtocol
{
    public static byte[] BuildRequest(byte sequence)
    {
        if (sequence is 0 or 255) throw new ArgumentOutOfRangeException(nameof(sequence));
        byte[] request = [0x52, 0x42, 0x06, sequence, 0x82, 0x00];
        byte sum = 0;
        for (var i = 0; i < request.Length - 1; i++) sum = unchecked((byte)(sum + request[i]));
        request[^1] = sum;
        return request;
    }

    public static bool TryParseReply(ReadOnlySpan<byte> report, byte sequence, out ControllerDeviceInfo? info)
    {
        info = null;
        var start = report.Length >= 3 && report[0] == 0 && report[1] == 0x52 && report[2] == 0x42 ? 1
            : report.Length >= 2 && report[0] == 0x52 && report[1] == 0x42 ? 0 : -1;
        if (start < 0 || report.Length - start < 24) return false;
        var packet = report[start..];
        if (packet[2] < 24 || packet[2] > packet.Length || packet[3] != sequence || packet[4] != 0x82) return false;
        var firmware = new Version(packet[21], packet[22], packet[23]);
        info = new ControllerDeviceInfo(Convert.ToHexString(packet.Slice(5, 3)), packet[8], packet[11], Convert.ToHexString(packet.Slice(12, 8)), firmware);
        return true;
    }
}
