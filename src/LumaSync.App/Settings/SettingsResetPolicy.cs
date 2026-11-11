namespace LumaSync;

/// <summary>Creates the complete safe default profile used by the UI reset action.</summary>
public static class SettingsResetPolicy
{
    public static AppSettings Reset(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new AppSettings();
    }
}
