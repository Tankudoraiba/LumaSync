using Microsoft.Win32;
using System.IO;

namespace LumaSync;

/// <summary>Optional per-user Windows Run registration; no service or elevation is used.</summary>
public static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LumaSync";

    public static string CommandLineFor(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var fullPath = Path.GetFullPath(executablePath);
        if (fullPath.Contains('"')) throw new ArgumentException("Executable path contains an invalid quote.", nameof(executablePath));
        return $"\"{fullPath}\"";
    }

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return string.Equals(value, CommandLineFor(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new IOException("Could not open the current user's Windows startup settings.");
            key.SetValue(ValueName, CommandLineFor(executablePath), RegistryValueKind.String);
            return;
        }
        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
