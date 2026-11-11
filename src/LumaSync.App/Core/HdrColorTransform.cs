namespace LumaSync;

/// <summary>Converts HDR10 PQ/Rec.2020 RGB10 pixels to tone-mapped sRGB for RGB LEDs.</summary>
public static class HdrColorTransform
{
    private const double ReferenceWhiteNits = 203d;
    private static readonly double[] PqToNits = Enumerable.Range(0, 1024).Select(DecodePq).ToArray();
    private static readonly byte[] LinearToSrgb = Enumerable.Range(0, 16385)
        .Select(i => EncodeSrgb(i / 16384d)).ToArray();

    public static ColorRgb Convert(uint packedR10G10B10A2)
    {
        var r2020 = PqToNits[(int)(packedR10G10B10A2 & 0x3FF)];
        var g2020 = PqToNits[(int)((packedR10G10B10A2 >> 10) & 0x3FF)];
        var b2020 = PqToNits[(int)((packedR10G10B10A2 >> 20) & 0x3FF)];

        var r709 = Math.Max(0, 1.660491 * r2020 - .587641 * g2020 - .072850 * b2020);
        var g709 = Math.Max(0, -.124550 * r2020 + 1.132900 * g2020 - .008349 * b2020);
        var b709 = Math.Max(0, -.018151 * r2020 - .100579 * g2020 + 1.118730 * b2020);
        var luminance = .2126 * r709 + .7152 * g709 + .0722 * b709;
        if (luminance <= 0) return new ColorRgb(0, 0, 0);

        var x = luminance / ReferenceWhiteNits;
        var mappedLuminance = Math.Clamp(x * (2.51 * x + .03) / (x * (2.43 * x + .59) + .14), 0, 1);
        var scale = mappedLuminance / luminance;
        return new ColorRgb(ToSrgb(r709 * scale), ToSrgb(g709 * scale), ToSrgb(b709 * scale));
    }

    private static double DecodePq(int code)
    {
        var encoded = code / 1023d;
        const double m1 = 2610d / 16384;
        const double m2 = 2523d / 32;
        const double c1 = 3424d / 4096;
        const double c2 = 2413d / 128;
        const double c3 = 2392d / 128;
        var p = Math.Pow(encoded, 1 / m2);
        var numerator = Math.Max(p - c1, 0);
        var denominator = c2 - c3 * p;
        return denominator <= 0 ? 10000 : 10000 * Math.Pow(numerator / denominator, 1 / m1);
    }

    private static byte ToSrgb(double linear)
    {
        var index = (int)Math.Clamp(Math.Round(Math.Clamp(linear, 0, 1) * (LinearToSrgb.Length - 1)), 0, LinearToSrgb.Length - 1);
        return LinearToSrgb[index];
    }

    private static byte EncodeSrgb(double linear)
    {
        var encoded = linear <= .0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }
}
