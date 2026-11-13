using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace LumaSync;

public sealed record AppSettings(
    int Top = 26, int Right = 14, int Bottom = 0, int Left = 14,
    bool Clockwise = true, LedCorner StartCorner = LedCorner.BottomLeft,
    double Brightness = 100, double Smoothing = 0, double SamplingDepth = 8,
    double Gamma = 1, double Saturation = 100,
    int CaptureRate = 75, int OutputRate = 3, string? DisplayDeviceName = null, double ColorTemperature = 6500,
    bool DetectLetterbox = false, int SchemaVersion = 9,
    double CropLeftPercent = 0, double CropRightPercent = 0, double CropTopPercent = 0, double CropBottomPercent = 0,
    bool StartMinimizedToTray = false, bool LightingEnabled = false,
    int ModeIndex = 0, double EffectSpeed = 50, string StaticColorHex = "FFFFFF", bool MinimizeToTrayOnClose = false,
    int HueCorrectionStart = 260, int HueCorrectionEnd = 290,
    int HueCorrectionRedGain = 100, int HueCorrectionGreenGain = 100, int HueCorrectionBlueGain = 100,
    int HueCorrection2Start = 0, int HueCorrection2End = 120,
    int HueCorrection2RedGain = 100, int HueCorrection2GreenGain = 100, int HueCorrection2BlueGain = 100,
    int HueCorrection3Start = 120, int HueCorrection3End = 240,
    int HueCorrection3RedGain = 100, int HueCorrection3GreenGain = 100, int HueCorrection3BlueGain = 100)
{
    internal void Validate()
    {
        if (new[] { Top, Right, Bottom, Left }.Any(x => x is < 0 or > 120) || Top + Right + Bottom + Left is < 1 or > 240)
            throw new InvalidDataException("LED edge counts must total 1–240, with no edge above 120.");
        if (!Enum.IsDefined(StartCorner)) throw new InvalidDataException("Invalid strip starting corner.");
        if (Brightness is < 0 or > 100 || Smoothing is < 0 or > 90 || SamplingDepth is < 2 or > 20 ||
            Gamma is < .5 or > 2.5 || Saturation is < 0 or > 200)
            throw new InvalidDataException("Color and sampling settings are outside their supported ranges.");
        if (!double.IsFinite(ColorTemperature) || ColorTemperature is < 2500 or > 10000)
            throw new InvalidDataException("Color temperature must be between 2500 K and 10000 K.");
        try { new ScreenCrop(CropLeftPercent / 100, CropRightPercent / 100, CropTopPercent / 100, CropBottomPercent / 100).Validate(); }
        catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException(ex.Message, ex); }
        if (CaptureRate is not (30 or 60 or 75) || OutputRate is < 0 or > 3)
            throw new InvalidDataException("Capture/output rates are invalid.");
        if (ModeIndex is < 0 or > 5 || EffectSpeed is < 5 or > 100 || StaticColorHex is null ||
            StaticColorHex.Length != 6 || !StaticColorHex.All(Uri.IsHexDigit))
            throw new InvalidDataException("Mode, effect speed, or static RGB color is invalid.");
        if (SchemaVersion is < 1 or > 9) throw new InvalidDataException("Settings schema version is unsupported.");
        if (!ValidHueCorrection(HueCorrectionStart, HueCorrectionEnd, HueCorrectionRedGain, HueCorrectionGreenGain, HueCorrectionBlueGain) ||
            !ValidHueCorrection(HueCorrection2Start, HueCorrection2End, HueCorrection2RedGain, HueCorrection2GreenGain, HueCorrection2BlueGain) ||
            !ValidHueCorrection(HueCorrection3Start, HueCorrection3End, HueCorrection3RedGain, HueCorrection3GreenGain, HueCorrection3BlueGain))
            throw new InvalidDataException("Hue correction range or channel gains are invalid.");
        if (DisplayDeviceName?.Length > 256) throw new InvalidDataException("Display device name is too long.");
    }

    private static bool ValidHueCorrection(int start, int end, int red, int green, int blue) =>
        start is >= 0 and <= 359 && end is >= 1 and <= 360 && start < end &&
        red is >= 0 and <= 200 && green is >= 0 and <= 200 && blue is >= 0 and <= 200;

    public static bool TryParse(string json, out AppSettings settings, out string? error)
    {
        settings = new AppSettings(); error = null;
        if (json.Length > 32_768) { error = "Settings file is larger than 32 KiB."; return false; }
        try
        {
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (parsed is null) { error = "Settings document is empty."; return false; }
            parsed.Validate(); settings = parsed; return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { error = ex.Message; return false; }
    }

    public string ToJson() { Validate(); return JsonSerializer.Serialize(this, Options); }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public static class SettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaSync", "settings.json");

    public static AppSettings Load(out string? warning, string? path = null)
    {
        warning = null;
        path ??= DefaultPath;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            if (new FileInfo(path).Length > 32_768) { warning = "Settings file is larger than 32 KiB."; return new AppSettings(); }
            var json = File.ReadAllText(path);
            if (AppSettings.TryParse(json, out var settings, out warning))
            {
                var sourceVersion = 1;
                using (var document = JsonDocument.Parse(json))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Settings root must be a JSON object.");
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!string.Equals(property.Name, "SchemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!property.Value.TryGetInt32(out sourceVersion))
                            throw new InvalidDataException("Settings schema version must be an integer.");
                        break;
                    }
                }
                if (sourceVersion < 2)
                {
                    // Upgrade the prototype layout if it was never customized; keep all other user layout counts.
                    if (settings is { Top: 20, Right: 7, Bottom: 20, Left: 7, Clockwise: true, StartCorner: LedCorner.TopLeft })
                        settings = settings with { Top = 26, Right = 14, Bottom = 0, Left = 14, StartCorner = LedCorner.BottomLeft };
                    settings = settings with { CaptureRate = 75 };
                }
                if (sourceVersion < 4) settings = settings with { SchemaVersion = 4 };
                if (sourceVersion < 5) settings = settings with { SchemaVersion = 5, StartMinimizedToTray = false };
                if (sourceVersion < 6) settings = settings with { SchemaVersion = 6, LightingEnabled = false };
                if (sourceVersion < 7) settings = settings with { SchemaVersion = 7, MinimizeToTrayOnClose = false };
                if (sourceVersion < 8) settings = settings with
                {
                    SchemaVersion = 8,
                    HueCorrectionStart = 260,
                    HueCorrectionEnd = 290,
                    HueCorrectionRedGain = 100,
                    HueCorrectionGreenGain = 100,
                    HueCorrectionBlueGain = 100
                };
                if (sourceVersion < 9) settings = settings with
                {
                    SchemaVersion = 9,
                    HueCorrection2Start = 0,
                    HueCorrection2End = 120,
                    HueCorrection2RedGain = 100,
                    HueCorrection2GreenGain = 100,
                    HueCorrection2BlueGain = 100,
                    HueCorrection3Start = 120,
                    HueCorrection3End = 240,
                    HueCorrection3RedGain = 100,
                    HueCorrection3GreenGain = 100,
                    HueCorrection3BlueGain = 100
                };
                return settings;
            }
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { warning = ex.Message; return new AppSettings(); }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Settings path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, (settings with { SchemaVersion = 9 }).ToJson());
        File.Move(temporary, path, true);
    }
}
