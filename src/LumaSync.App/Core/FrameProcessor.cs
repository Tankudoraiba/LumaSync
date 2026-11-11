namespace LumaSync;

/// <summary>Samples each LED's screen region without per-pixel GDI+ calls.</summary>
public sealed class FrameProcessor
{
    private ColorRgb[]? previous;
    private int resetGeneration;
    private int previousGeneration;
    private LayoutOptions? cachedLayout;
    private IReadOnlyList<LedSample>? cachedPoints;
    private const int MaxSamplesPerLed = 144;
    private static readonly double[] LinearLookup = Enumerable.Range(0, 256).Select(i => ToLinear(i / 255d)).ToArray();

    /// <summary>Clears temporal history after a display, crop, or LED-map change.</summary>
    public void Reset() => Interlocked.Increment(ref resetGeneration);

    private struct LinearSample
    {
        public double R, G, B, Luma;
    }

    public unsafe ColorRgb[] Sample(byte* baseAddress, int width, int height, int stride, LayoutOptions layout,
        double brightness = 1, double smoothing = .25, double gamma = 1, double saturation = 1, bool bgra = true, double colorTemperature = 6500, bool detectLetterbox = false, bool hdr10PqRec2020 = false, ScreenCrop crop = default)
    {
        var colors = new ColorRgb[checked(layout.Top + layout.Right + layout.Bottom + layout.Left)];
        SampleInto(baseAddress, width, height, stride, layout, colors, brightness, smoothing, gamma, saturation,
            bgra, colorTemperature, detectLetterbox, hdr10PqRec2020, crop);
        return colors;
    }

    /// <summary>Samples into caller-owned storage to avoid allocating a color array on each frame.</summary>
    public unsafe void SampleInto(byte* baseAddress, int width, int height, int stride, LayoutOptions layout,
        Span<ColorRgb> destination, double brightness = 1, double smoothing = .25, double gamma = 1,
        double saturation = 1, bool bgra = true, double colorTemperature = 6500,
        bool detectLetterbox = false, bool hdr10PqRec2020 = false, ScreenCrop crop = default)
    {
        if (baseAddress is null) throw new ArgumentNullException(nameof(baseAddress));
        if (width <= 0 || height <= 0 || stride < checked(width * 4)) throw new ArgumentOutOfRangeException(nameof(width));
        crop.Validate();
        if (gamma is < .3 or > 3) throw new ArgumentOutOfRangeException(nameof(gamma));
        var currentGeneration = Volatile.Read(ref resetGeneration);
        var priorColors = previousGeneration == currentGeneration ? previous : null;
        smoothing = Math.Clamp(smoothing, 0, .95);
        if (!layout.Equals(cachedLayout) || cachedPoints is null)
        {
            cachedPoints = layout.Create();
            cachedLayout = layout;
        }
        var points = cachedPoints;
        if (destination.Length < points.Count) throw new ArgumentException("Destination is shorter than the LED layout.", nameof(destination));
        Span<int> edgeIndices = stackalloc int[4];
        edgeIndices.Clear();
        var next = destination[..points.Count];
        Span<LinearSample> samples = stackalloc LinearSample[MaxSamplesPerLed];
        Span<double> luminances = stackalloc double[MaxSamplesPerLed];
        var active = detectLetterbox ? DetectLetterboxBounds(baseAddress, width, height, stride, bgra) : (Top: 0d, Bottom: 1d);
        var activeLeft = Math.Clamp((int)Math.Ceiling(crop.Left * width), 0, width - 1);
        var activeRight = Math.Clamp((int)Math.Floor((1 - crop.Right) * width), activeLeft + 1, width);
        var activeTop = Math.Clamp(Math.Max((int)Math.Floor(active.Top * height), (int)Math.Ceiling(crop.Top * height)), 0, height - 1);
        var activeBottom = Math.Clamp(Math.Min((int)Math.Ceiling(active.Bottom * height), (int)Math.Floor((1 - crop.Bottom) * height)), activeTop + 1, height);
        var activeWidth = activeRight - activeLeft;
        var activeHeight = activeBottom - activeTop;
        var sampleDepthX = Math.Clamp((int)Math.Ceiling(activeWidth * layout.Depth), 1, activeWidth);
        var sampleDepthY = Math.Clamp((int)Math.Ceiling(activeHeight * layout.Depth), 1, activeHeight);
        var bpp = 4;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var edgeIndex = edgeIndices[(int)p.Edge]++;
            var edgeCount = p.Edge switch { LedEdge.Top => layout.Top, LedEdge.Right => layout.Right, LedEdge.Bottom => layout.Bottom, _ => layout.Left };
            int x0, x1, y0, y1;
            switch (p.Edge)
            {
                case LedEdge.Top:
                    (x0, x1) = Partition(activeWidth, edgeIndex, edgeCount);
                    x0 += activeLeft; x1 += activeLeft;
                    y0 = activeTop; y1 = Math.Min(activeBottom, y0 + sampleDepthY);
                    break;
                case LedEdge.Right:
                    x0 = activeRight - sampleDepthX; x1 = activeRight;
                    (y0, y1) = Partition(activeHeight, edgeIndex, edgeCount);
                    y0 += activeTop; y1 += activeTop;
                    break;
                case LedEdge.Bottom:
                    (x0, x1) = Partition(activeWidth, edgeIndex, edgeCount);
                    x0 += activeLeft; x1 += activeLeft;
                    y0 = Math.Max(activeTop, activeBottom - sampleDepthY); y1 = activeBottom;
                    break;
                default:
                    x0 = activeLeft; x1 = activeLeft + sampleDepthX;
                    (y0, y1) = Partition(activeHeight, edgeIndex, edgeCount);
                    y0 += activeTop; y1 += activeTop;
                    break;
            }
            var stepX = Math.Max(1, (x1 - x0 + 11) / 12);
            var stepY = Math.Max(1, (y1 - y0 + 11) / 12);
            var count = 0;
            for (var y = y0; y < y1; y += stepY)
            {
                var row = baseAddress + y * stride;
                for (var x = x0; x < x1; x += stepX)
                {
                    var pixel = row + x * bpp; // BGRA8 memory layout.
                    double r, g, b;
                    if (hdr10PqRec2020)
                    {
                        var hdr = HdrColorTransform.Convert(*(uint*)pixel);
                        r = LinearLookup[hdr.R]; g = LinearLookup[hdr.G]; b = LinearLookup[hdr.B];
                    }
                    else
                    {
                        r = LinearLookup[pixel[bgra ? 2 : 0]];
                        g = LinearLookup[pixel[1]];
                        b = LinearLookup[pixel[bgra ? 0 : 2]];
                    }
                    var luma = .2126 * r + .7152 * g + .0722 * b;
                    samples[count] = new LinearSample { R = r, G = g, B = b, Luma = luma };
                    luminances[count++] = luma;
                }
            }
            luminances[..count].Sort();
            // Ignore the brightest 10% of samples to limit isolated highlights.
            var highlightCutoff = luminances[Math.Max(0, (int)Math.Floor((count - 1) * .9))];
            double red = 0, green = 0, blue = 0;
            var retained = 0;
            for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
            {
                var sample = samples[sampleIndex];
                if (sample.Luma > highlightCutoff) continue;
                red += sample.R; green += sample.G; blue += sample.B; retained++;
            }
            var inv = 1d / Math.Max(1, retained);
            var exponent = 1 / gamma;
            var color = new ColorRgb(ToChannel(ToSrgb(red * inv) * 255, exponent), ToChannel(ToSrgb(green * inv) * 255, exponent), ToChannel(ToSrgb(blue * inv) * 255, exponent))
                .Saturate(saturation).WithColorTemperature(colorTemperature).Scale(brightness);
            next[i] = priorColors is { Length: > 0 } && priorColors.Length == next.Length
                ? ColorRgb.Blend(color, priorColors[i], smoothing)
                : color;
        }

        if (Volatile.Read(ref resetGeneration) == currentGeneration)
        {
            if (previous is null || previous.Length != points.Count) previous = new ColorRgb[points.Count];
            next.CopyTo(previous);
            previousGeneration = currentGeneration;
        }
        else
        {
            previous = null;
            previousGeneration = Volatile.Read(ref resetGeneration);
        }
    }

    private static (int Start, int End) Partition(int length, int index, int count)
    {
        var start = Math.Min(length - 1, (int)((long)index * length / count));
        var end = Math.Max(start + 1, (int)((long)(index + 1) * length / count));
        return (start, Math.Min(length, end));
    }

    private static byte ToChannel(double value, double exponent) =>
        (byte)Math.Clamp(Math.Round(Math.Pow(Math.Clamp(value / 255, 0, 1), exponent) * 255), 0, 255);

    private static double ToLinear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    private static double ToSrgb(double value) => value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055;

    /// <summary>Detects contiguous top/bottom rows that are at least 92% near-black; bars must span at least 3% of the image.</summary>
    public static unsafe (double Top, double Bottom) DetectLetterboxBounds(byte* pixels, int width, int height, int stride, bool bgra = true)
    {
        if (pixels is null) throw new ArgumentNullException(nameof(pixels));
        if (width <= 0 || height <= 0 || stride < checked(width * 4)) throw new ArgumentOutOfRangeException(nameof(width));
        var minBar = Math.Max(2, (int)Math.Ceiling(height * .03));
        var maxBar = (int)(height * .2);
        if (maxBar < minBar * 2) return (0, 1);
        var topRows = CountBlackRows(pixels, width, height, stride, bgra, 0, 1, maxBar);
        var bottomRows = CountBlackRows(pixels, width, height, stride, bgra, height - 1, -1, maxBar);
        // A scan that reaches its depth limit never found picture content, so the bounds are unknown.
        if (topRows == maxBar || bottomRows == maxBar) return (0, 1);
        if (topRows < minBar) topRows = 0;
        if (bottomRows < minBar) bottomRows = 0;
        if (topRows + bottomRows > height - minBar) return (0, 1);
        return ((double)topRows / height, 1 - (double)bottomRows / height);
    }

    private static unsafe int CountBlackRows(byte* pixels, int width, int height, int stride, bool bgra, int start, int step, int maximum)
    {
        var samples = Math.Min(64, width);
        var count = 0;
        for (var rowIndex = 0; rowIndex < maximum; rowIndex++)
        {
            var y = start + step * rowIndex;
            if ((uint)y >= (uint)height) break;
            var black = 0;
            var row = pixels + y * stride;
            for (var i = 0; i < samples; i++)
            {
                var x = samples == 1 ? 0 : i * (width - 1) / (samples - 1);
                var pixel = row + x * 4;
                var r = pixel[bgra ? 2 : 0]; var g = pixel[1]; var b = pixel[bgra ? 0 : 2];
                if (r <= 18 && g <= 18 && b <= 18) black++;
            }
            if (black < samples * .92) break;
            count++;
        }
        return count;
    }
}
