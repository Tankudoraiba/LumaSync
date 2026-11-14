using System.IO;

namespace LumaSync;

public enum ScreenSyncTuning
{
    Brightness,
    Smoothing,
    SamplingDepth,
    Gamma,
    Saturation,
    ColorTemperature,
    CaptureRate,
    OutputRate,
    LetterboxDetection,
    CropLeft,
    CropRight,
    CropTop,
    CropBottom
}

/// <summary>Pure, bounded adjustment rules shared by the native Screen Sync controls and tests.</summary>
public static class ScreenSyncSettingsEditor
{
    public static bool TryMapControl(int x, int y, out ScreenSyncTuning tuning, out int direction)
    {
        tuning = default;
        direction = 0;
        var row = y is >= 466 and <= 500 ? 0 : y is >= 504 and <= 538 ? 1 : y is >= 542 and <= 576 ? 2 : -1;
        if (row < 0) return false;
        var rowStart = row == 0 ? 32 : 116;
        if (x < rowStart || x >= rowStart + (row == 0 ? 5 : 4) * 168) return false;
        var column = (x - rowStart) / 168;
        var within = (x - rowStart) % 168;
        if (column > (row == 0 ? 4 : 3)) return false;
        if (within is >= 2 and <= 27) direction = -1;
        else if (within is >= 134 and <= 159) direction = 1;
        else if (within >= 162) return false;
        tuning = (ScreenSyncTuning)(row switch { 0 => column, 1 => column + 5, _ => column + 9 });
        return true;
    }

    public static AppSettings Adjust(AppSettings settings, ScreenSyncTuning tuning, int direction)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        return tuning switch
        {
            ScreenSyncTuning.Brightness => settings with { Brightness = Math.Clamp(settings.Brightness + direction * 5, 0, 100) },
            ScreenSyncTuning.Smoothing => settings with { Smoothing = Math.Clamp(settings.Smoothing + direction * 5, 0, 90) },
            ScreenSyncTuning.SamplingDepth => settings with { SamplingDepth = Math.Clamp(settings.SamplingDepth + direction, 2, 20) },
            ScreenSyncTuning.Gamma => settings with { Gamma = Math.Clamp(Math.Round(settings.Gamma + direction * .1, 1), .5, 2.5) },
            ScreenSyncTuning.Saturation => settings with { Saturation = Math.Clamp(settings.Saturation + direction * 10, 0, 200) },
            ScreenSyncTuning.ColorTemperature => settings with { ColorTemperature = Math.Clamp(settings.ColorTemperature + direction * 250, 2500, 10000) },
            ScreenSyncTuning.CaptureRate => settings with
            {
                CaptureRate = settings.CaptureRate switch
                {
                    30 => direction > 0 ? 60 : 75,
                    60 => direction > 0 ? 75 : 30,
                    75 => direction > 0 ? 30 : 60,
                    _ => throw new InvalidDataException("Capture rate must be 30, 60, or 75 Hz.")
                }
            },
            ScreenSyncTuning.OutputRate => settings with { OutputRate = Math.Clamp(settings.OutputRate + direction, 0, 3) },
            ScreenSyncTuning.LetterboxDetection => settings with { DetectLetterbox = !settings.DetectLetterbox },
            ScreenSyncTuning.CropLeft => settings with { CropLeftPercent = Math.Clamp(settings.CropLeftPercent + direction, 0, 20) },
            ScreenSyncTuning.CropRight => settings with { CropRightPercent = Math.Clamp(settings.CropRightPercent + direction, 0, 20) },
            ScreenSyncTuning.CropTop => settings with { CropTopPercent = Math.Clamp(settings.CropTopPercent + direction, 0, 20) },
            ScreenSyncTuning.CropBottom => settings with { CropBottomPercent = Math.Clamp(settings.CropBottomPercent + direction, 0, 20) },
            _ => throw new ArgumentOutOfRangeException(nameof(tuning))
        };
    }
}
