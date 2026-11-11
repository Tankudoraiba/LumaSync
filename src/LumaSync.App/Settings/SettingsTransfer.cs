using System.IO;
using System.Text;

namespace LumaSync;

/// <summary>Reads and writes user-selected settings files with bounded, validated local I/O.</summary>
public static class SettingsTransfer
{
    public const int MaximumFileBytes = 32_768;

    public static void Export(string path, AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        var json = settings.ToJson();
        if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            throw new InvalidDataException("Settings export exceeds the 32 KiB size limit.");
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static bool TryImport(string path, out AppSettings settings, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        settings = new AppSettings();
        error = null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.SequentialScan);
        if (stream.Length > MaximumFileBytes)
        {
            error = "Settings file is larger than 32 KiB.";
            return false;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        if (!AppSettings.TryParse(json, out settings, out error))
        {
            settings = new AppSettings();
            return false;
        }
        return true;
    }
}
