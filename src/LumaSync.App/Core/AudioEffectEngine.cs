namespace LumaSync;

/// <summary>Maps short-lived WASAPI spectrum levels to physical strip-index colors.</summary>
public static class AudioEffectEngine
{
    private static readonly ColorRgb[] BandColors =
    [
        new(255, 24, 0), new(255, 112, 0), new(255, 220, 0), new(40, 255, 0),
        new(0, 230, 180), new(0, 100, 255), new(80, 0, 255), new(220, 0, 255)
    ];

    public static ColorRgb[] Render(AudioAnalysis analysis, int ledCount, double brightness = 1, double sensitivity = 1)
    {
        if (ledCount is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(ledCount));
        var output = new ColorRgb[ledCount];
        RenderInto(analysis, output, brightness, sensitivity);
        return output;
    }

    /// <summary>Maps the latest local audio spectrum into caller-owned storage.</summary>
    public static void RenderInto(AudioAnalysis analysis, Span<ColorRgb> output, double brightness = 1, double sensitivity = 1)
        => RenderCore(analysis, default, output, brightness, sensitivity, useLayout: false);

    /// <summary>Maps stereo audio spatially across the physical layout.</summary>
    public static void RenderInto(AudioAnalysis analysis, ReadOnlySpan<LedSample> layout, Span<ColorRgb> output, double brightness = 1, double sensitivity = 1)
    {
        if (layout.Length != output.Length) throw new ArgumentException("LED layout and output lengths must match.", nameof(layout));
        RenderCore(analysis, layout, output, brightness, sensitivity, useLayout: true);
    }

    private static void RenderCore(AudioAnalysis analysis, ReadOnlySpan<LedSample> layout, Span<ColorRgb> output,
        double brightness, double sensitivity, bool useLayout)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (output.Length is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(output));
        if (!double.IsFinite(brightness) || brightness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(brightness));
        if (!double.IsFinite(sensitivity) || sensitivity is < .1 or > 2) throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (analysis.Bands.Length != BandColors.Length || analysis.LeftBands.Length != BandColors.Length || analysis.RightBands.Length != BandColors.Length)
            throw new ArgumentException("Expected eight audio spectrum bands per channel.", nameof(analysis));

        // Channel response must not depend on where a frequency band happens to
        // land in strip-index order. With a bottom-left start, bass-only audio
        // previously lit the left side while the right edge received only the
        // highest bands and looked dead. Keep the spectrum palette spatial, but
        // drive stereo panning from each channel's strongest audible band.
        var leftChannelLevel = analysis.LeftLevel;
        var rightChannelLevel = analysis.RightLevel;
        var leftFill = FillFromLevel(analysis.LeftLevel * sensitivity);
        var rightFill = FillFromLevel(analysis.RightLevel * sensitivity);

        for (var i = 0; i < output.Length; i++)
        {
            var band = Math.Min(BandColors.Length - 1, i * BandColors.Length / output.Length);
            var pan = 0d;
            if (useLayout)
            {
                var sample = layout[i];
                var horizontal = sample.Edge == LedEdge.Left ? 0 : sample.Edge == LedEdge.Right ? 1 : sample.U;
                pan = Math.Clamp(horizontal * 2 - 1, -1, 1);
                var leftWeight = (1 - pan) * .5;
                var rightWeight = (1 + pan) * .5;
                var fill = leftFill * leftWeight + rightFill * rightWeight;
                if (DistanceFromTopCenter(sample) > fill)
                {
                    output[i] = new ColorRgb(0, 0, 0);
                    continue;
                }
                var spatialLevel = Math.Clamp((leftChannelLevel * leftWeight + rightChannelLevel * rightWeight) * sensitivity, 0, 1);
                output[i] = BandColors[band].Scale(spatialLevel * brightness);
                continue;
            }
            var level = Math.Clamp(analysis.Bands[band] * sensitivity, 0, 1);
            output[i] = BandColors[band].Scale(level * brightness);
        }
    }

    private static double FillFromLevel(double level)
    {
        // Suppress idle-device noise, then turn playback RMS into a visible bar length.
        const double noiseFloor = .04;
        const double fullScale = .6;
        return Math.Clamp((level - noiseFloor) / (fullScale - noiseFloor), 0, 1);
    }

    private static double DistanceFromTopCenter(LedSample sample) => sample.Edge switch
    {
        // The top half runs from distance 0 at center to .5 at each corner;
        // the adjoining side then continues .5→1 down toward its bottom corner.
        LedEdge.Top => Math.Clamp(Math.Abs(sample.U - .5), 0, .5),
        LedEdge.Left or LedEdge.Right => Math.Clamp(.5 + sample.V * .5, 0, 1),
        _ => 1
    };
}
