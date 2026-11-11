namespace LumaSync;

/// <summary>Eight-band Hann-windowed radix-2 FFT analyzer. Instances are intended for one capture thread.</summary>
public sealed class AudioSpectrumAnalyzer
{
    public const int WindowSize = 1024;
    private readonly double[] real = new double[WindowSize];
    private readonly double[] imaginary = new double[WindowSize];

    public float[] Analyze(ReadOnlySpan<float> samples, uint sampleRate)
    {
        if (samples.Length != WindowSize) throw new ArgumentException($"Expected {WindowSize} audio samples.", nameof(samples));
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        return AnalyzeChannel(samples, sampleRate);
    }

    public AudioAnalysis AnalyzeStereo(ReadOnlySpan<float> left, ReadOnlySpan<float> right, uint sampleRate)
    {
        if (left.Length != WindowSize || right.Length != WindowSize)
            throw new ArgumentException($"Expected {WindowSize} samples per channel.");
        if (sampleRate is < 8000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var leftBands = AnalyzeChannel(left, sampleRate);
        var rightBands = AnalyzeChannel(right, sampleRate);
        var combinedBands = new float[leftBands.Length];
        for (var i = 0; i < combinedBands.Length; i++)
            combinedBands[i] = (float)Math.Sqrt((leftBands[i] * leftBands[i] + rightBands[i] * rightBands[i]) * .5);

        double leftPower = 0, rightPower = 0;
        for (var i = 0; i < WindowSize; i++)
        {
            var l = float.IsFinite(left[i]) ? Math.Clamp(left[i], -1, 1) : 0;
            var r = float.IsFinite(right[i]) ? Math.Clamp(right[i], -1, 1) : 0;
            leftPower += l * l;
            rightPower += r * r;
        }
        var leftLevel = (float)Math.Clamp(Math.Sqrt(leftPower / WindowSize) * 3.5, 0, 1);
        var rightLevel = (float)Math.Clamp(Math.Sqrt(rightPower / WindowSize) * 3.5, 0, 1);
        var level = (float)Math.Sqrt((leftLevel * leftLevel + rightLevel * rightLevel) * .5);
        return new AudioAnalysis(level, combinedBands, leftBands, rightBands, leftLevel, rightLevel);
    }

    private float[] AnalyzeChannel(ReadOnlySpan<float> samples, uint sampleRate)
    {
        for (var i = 0; i < WindowSize; i++)
        {
            var sample = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1, 1) : 0;
            real[i] = sample * (.5 - .5 * Math.Cos(2 * Math.PI * i / (WindowSize - 1)));
            imaginary[i] = 0;
        }
        TransformFft();

        ReadOnlySpan<double> edges = [45, 90, 180, 360, 720, 1440, 2880, 5760];
        var bands = new float[8];
        for (var band = 0; band < bands.Length; band++)
        {
            var lowFrequency = edges[band];
            var highFrequency = band == bands.Length - 1 ? sampleRate * .5 : edges[band + 1];
            var first = Math.Max(1, (int)Math.Floor(lowFrequency * WindowSize / sampleRate));
            var last = Math.Min(WindowSize / 2, Math.Max(first + 1, (int)Math.Ceiling(highFrequency * WindowSize / sampleRate)));
            double peakPower = 0;
            for (var bin = first; bin < last; bin++)
            {
                var power = real[bin] * real[bin] + imaginary[bin] * imaginary[bin];
                if (power > peakPower) peakPower = power;
            }
            // A peak bin keeps a single note from being diluted by the number of
            // bins in a wide band, which made ordinary music look almost silent.
            var magnitude = Math.Sqrt(peakPower) * (2d / WindowSize) * 8;
            bands[band] = (float)Math.Clamp(magnitude, 0, 1);
        }
        return bands;
    }

    private void TransformFft()
    {
        var j = 0;
        for (var i = 1; i < WindowSize; i++)
        {
            var bit = WindowSize >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]); }
        }
        for (var length = 2; length <= WindowSize; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle); var stepImaginary = Math.Sin(angle);
            for (var start = 0; start < WindowSize; start += length)
            {
                double wr = 1, wi = 0;
                for (var k = 0; k < length / 2; k++)
                {
                    var even = start + k; var odd = even + length / 2;
                    var tr = wr * real[odd] - wi * imaginary[odd];
                    var ti = wr * imaginary[odd] + wi * real[odd];
                    real[odd] = real[even] - tr; imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr; imaginary[even] += ti;
                    (wr, wi) = (wr * stepReal - wi * stepImaginary, wr * stepImaginary + wi * stepReal);
                }
            }
        }
    }
}
