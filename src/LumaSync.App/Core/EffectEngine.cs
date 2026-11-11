namespace LumaSync;

public enum LightingEffect { Rainbow, ColorCycle, Breathing }

/// <summary>Pure software effects that produce one color per physical strip index.</summary>
public static class EffectEngine
{
    public static ColorRgb[] Render(LightingEffect effect, TimeSpan elapsed, int ledCount,
        ColorRgb baseColor, double brightness = 1, double speed = .5)
    {
        if (ledCount is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(ledCount));
        var colors = new ColorRgb[ledCount];
        RenderInto(effect, elapsed, colors, baseColor, brightness, speed);
        return colors;
    }

    /// <summary>Renders into caller-owned storage for low-allocation real-time LED updates.</summary>
    public static void RenderInto(LightingEffect effect, TimeSpan elapsed, Span<ColorRgb> colors,
        ColorRgb baseColor, double brightness = 1, double speed = .5)
    {
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (colors.Length is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(colors));
        if (!double.IsFinite(brightness) || brightness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(brightness));
        if (!double.IsFinite(speed) || speed is < .05 or > 1) throw new ArgumentOutOfRangeException(nameof(speed));

        var seconds = elapsed.TotalSeconds;
        if (effect == LightingEffect.Breathing)
        {
            var wave = .04 + .96 * (1 - Math.Cos(2 * Math.PI * seconds * (.1 + speed * .9))) * .5;
            colors.Fill(baseColor.Scale(brightness * wave));
            return;
        }

        var rotation = seconds * speed * .12;
        for (var i = 0; i < colors.Length; i++)
        {
            var hue = effect == LightingEffect.Rainbow ? i / (double)colors.Length + rotation : rotation;
            colors[i] = FromHsv(hue, 1, brightness);
        }
    }

    private static ColorRgb FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 1 + 1) % 1;
        var sector = hue * 6;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(sector % 2 - 1));
        var (r, g, b) = (int)sector switch
        {
            0 => (chroma, x, 0d), 1 => (x, chroma, 0d), 2 => (0d, chroma, x),
            3 => (0d, x, chroma), 4 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        var m = value - chroma;
        return new ColorRgb(ToByte((r + m) * 255), ToByte((g + m) * 255), ToByte((b + m) * 255));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
