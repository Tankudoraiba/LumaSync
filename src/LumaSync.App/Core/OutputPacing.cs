namespace LumaSync;

public static class OutputPacing
{
    public static int EffectiveRate(int captureRate, int selection)
    {
        if (captureRate is not (30 or 60 or 75)) throw new ArgumentOutOfRangeException(nameof(captureRate));
        return selection switch
        {
            0 => Math.Min(captureRate, 60),
            1 => 30,
            2 => 60,
            3 => 75,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
    }

    public static bool IsDue(long nowTicks, long lastQueuedTicks, int rateHz, long stopwatchFrequency)
    {
        if (nowTicks < 0 || lastQueuedTicks < 0 || stopwatchFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(nowTicks));
        if (rateHz is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(rateHz));
        if (lastQueuedTicks == 0) return true;
        if (nowTicks < lastQueuedTicks) return false;
        var interval = (long)Math.Ceiling(stopwatchFrequency / (double)rateHz);
        return nowTicks - lastQueuedTicks >= interval;
    }

    /// <summary>
    /// The capture loop already bounds candidate frames at its configured rate. Avoid
    /// applying a second clock gate when output can accept every captured frame; two
    /// slightly different clocks otherwise drop frames at equal 75 Hz targets.
    /// </summary>
    public static bool IsDueForCapture(long nowTicks, long lastQueuedTicks, int outputRateHz,
        int captureRateHz, long stopwatchFrequency)
    {
        if (captureRateHz is not (30 or 60 or 75)) throw new ArgumentOutOfRangeException(nameof(captureRateHz));
        if (outputRateHz is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(outputRateHz));
        return outputRateHz >= captureRateHz || IsDue(nowTicks, lastQueuedTicks, outputRateHz, stopwatchFrequency);
    }
}
