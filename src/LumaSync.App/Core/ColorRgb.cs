namespace LumaSync;
public readonly record struct ColorRgb(byte R, byte G, byte B)
{
    public static ColorRgb Blend(ColorRgb from, ColorRgb to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new((byte)Math.Round(from.R + (to.R-from.R)*amount), (byte)Math.Round(from.G + (to.G-from.G)*amount), (byte)Math.Round(from.B + (to.B-from.B)*amount));
    }
    public ColorRgb Scale(double brightness) => new((byte)Math.Clamp(Math.Round(R*Math.Clamp(brightness,0,1)),0,255),(byte)Math.Clamp(Math.Round(G*Math.Clamp(brightness,0,1)),0,255),(byte)Math.Clamp(Math.Round(B*Math.Clamp(brightness,0,1)),0,255));
    public ColorRgb Saturate(double amount)
    {
        amount = Math.Clamp(amount, 0, 2);
        var luma = .2126 * R + .7152 * G + .0722 * B;
        return new(
            (byte)Math.Clamp(Math.Round(luma + (R - luma) * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(luma + (G - luma) * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(luma + (B - luma) * amount), 0, 255));
    }

    public ColorRgb WithColorTemperature(double kelvin)
    {
        if (!double.IsFinite(kelvin) || kelvin is < 2500 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(kelvin), "Color temperature must be between 2500 K and 10000 K.");
        var offset = kelvin - 6500;
        var redScale = 1 - offset * .00005;
        var blueScale = 1 + offset * .00005;
        return new(
            (byte)Math.Clamp(Math.Round(R * redScale), 0, 255),
            G,
            (byte)Math.Clamp(Math.Round(B * blueScale), 0, 255));
    }
}
