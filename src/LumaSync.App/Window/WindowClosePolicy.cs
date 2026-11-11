namespace LumaSync;

/// <summary>Decides whether a window close hides the app or proceeds to process shutdown.</summary>
public static class WindowClosePolicy
{
    public static bool ShouldMinimizeToTray(bool preferenceEnabled, bool trayAvailable, bool trayRetryPending,
        bool explicitExitRequested, bool windowsSessionEnding) =>
        preferenceEnabled && (trayAvailable || trayRetryPending) && !explicitExitRequested && !windowsSessionEnding;
}
