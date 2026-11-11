namespace LumaSync;

/// <summary>Selects displays in stable enumeration order and recovers when the saved display disappears.</summary>
public static class DisplaySelectionPolicy
{
    public static int NextIndex(int displayCount, int currentIndex)
    {
        if (displayCount < 1) throw new ArgumentOutOfRangeException(nameof(displayCount));
        if (currentIndex < -1 || currentIndex >= displayCount) throw new ArgumentOutOfRangeException(nameof(currentIndex));
        return currentIndex < 0 ? 0 : (currentIndex + 1) % displayCount;
    }
}
