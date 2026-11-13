namespace LumaSync;

public readonly record struct BaseColorPreset(string Name, string Hex);

/// <summary>Named base colors shown by the native UI and cycled by the Static Color control.</summary>
public static class BaseColorPresets
{
    private static readonly BaseColorPreset[] presets =
    [
        new("RED", "FF0000"),
        new("GREEN", "00FF00"),
        new("BLUE", "0000FF"),
        new("CYAN", "00FFFF"),
        new("MAGENTA", "FF00FF"),
        new("YELLOW", "FFFF00"),
        new("VIOLET", "9B55D2"),
        new("PINK", "FF69B4")
    ];

    public static IReadOnlyList<BaseColorPreset> All { get; } = Array.AsReadOnly(presets);

    public static string NameFor(string? hex)
    {
        if (hex is null) return "CUSTOM";
        foreach (var preset in presets)
            if (string.Equals(preset.Hex, hex, StringComparison.OrdinalIgnoreCase)) return preset.Name;
        return "CUSTOM";
    }

    public static string NextHex(string? currentHex)
    {
        var index = Array.FindIndex(presets,
            preset => string.Equals(preset.Hex, currentHex, StringComparison.OrdinalIgnoreCase));
        return presets[(index + 1) % presets.Length].Hex;
    }
}
