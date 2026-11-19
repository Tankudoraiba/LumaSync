namespace LumaSync;

/// <summary>Builds the DX Light compatible controller command that switches the strip off.</summary>
public static class TurnOffLightsProtocol
{
    public const int SendCount = 4;

    public static byte[] BuildCommand(byte sequence)
    {
        if (sequence is 0 or 255) throw new ArgumentOutOfRangeException(nameof(sequence));
        byte[] command = [0x52, 0x42, 0x05, sequence, 0x97, 0x00];
        byte checksum = 0;
        for (var i = 0; i < command.Length - 1; i++) checksum = unchecked((byte)(checksum + command[i]));
        command[^1] = checksum;
        return command;
    }

    public static byte SequenceAt(byte firstSequence, int offset)
    {
        if (firstSequence is 0 or 255) throw new ArgumentOutOfRangeException(nameof(firstSequence));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return (byte)((firstSequence - 1 + offset) % 254 + 1);
    }
}
