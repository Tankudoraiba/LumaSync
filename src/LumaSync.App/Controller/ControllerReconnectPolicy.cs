namespace LumaSync;

/// <summary>Limits HID reconnect pressure while recovering quickly from an unplug/replug.</summary>
public static class ControllerReconnectPolicy
{
    public static TimeSpan DelayForFailure(int consecutiveFailures)
    {
        if (consecutiveFailures < 1) throw new ArgumentOutOfRangeException(nameof(consecutiveFailures));
        var exponent = Math.Min(consecutiveFailures - 1, 4);
        return TimeSpan.FromMilliseconds(250 * (1 << exponent));
    }
}
