namespace LumaSync;

public readonly record struct BaseColorPreset(string Name, string Hex);

/// <summary>Named base colors shown by the native UI and cycled by the Static Color control.</summary>
public static class BaseColorPresets
{
    private static readonly BaseColorPreset[] presets =
    [
        new("WHITE", "FFFFFF"),
        new("MINT", "43F4E0"),
        new("PINK", "FF36A2"),
        new("AMBER", "FFB347"),
        new("VIOLET", "7C4DFF")
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
