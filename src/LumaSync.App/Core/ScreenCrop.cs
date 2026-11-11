namespace LumaSync;

/// <summary>Fractional margins excluded from the captured screen before edge LED mapping.</summary>
public readonly record struct ScreenCrop(double Left = 0, double Right = 0, double Top = 0, double Bottom = 0)
{
    public void Validate()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Right) || !double.IsFinite(Top) || !double.IsFinite(Bottom)
            || Left is < 0 or > .2 || Right is < 0 or > .2 || Top is < 0 or > .2 || Bottom is < 0 or > .2
            || Left + Right >= .8 || Top + Bottom >= .8)
            throw new ArgumentOutOfRangeException(nameof(Left), "Each exclusion margin must be between 0 and 20%; opposing margins must leave a visible screen region.");
    }
}
