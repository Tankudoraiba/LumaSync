using LumaSync;
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var ledColors = Enumerable.Range(0, 54).Select(i => new ColorRgb((byte)i, 20, 30)).ToArray();
var frame = ScreenSyncFrameBuilder.Build(ledColors, 7);
Check(frame.Length == 277 && frame[0] == 0x53 && frame[1] == 0x43, "SC frame header and length");
Check(frame[6] == 1 && frame[7] == 0 && frame[8] == 20 && frame[9] == 30, "First LED RGB serialization");
Check(frame[271] == 54 && frame[272] == 53, "Last LED index and RGB serialization");
byte checksum = 0;
for (var i = 0; i < frame.Length - 1; i++) checksum = unchecked((byte)(checksum + frame[i]));
Check(frame[^1] == checksum, "Frame checksum");
Check(ScreenSyncFrameBuilder.ToWindowsHidReports(frame).Count == 5, "HID report chunk count");
try { _ = ScreenSyncFrameBuilder.Build(ledColors, 0); throw new InvalidOperationException("Reserved sequence accepted"); }
catch (ArgumentOutOfRangeException) { }

var layout = new LayoutOptions().Create();
Check(layout.Count == 54 && layout.Count(x => x.Edge == LedEdge.Top) == 26 && layout.Count(x => x.Edge == LedEdge.Right) == 14 && layout.Count(x => x.Edge == LedEdge.Left) == 14, "Default 54 LED edge layout");
Check(layout[0].Edge == LedEdge.Left && layout[0].U < .1 && layout[0].V > .5, "Default strip starts bottom-left and proceeds clockwise");
Check(NativeUiWindowPolicy.CanResize(NativeUiWindowPolicy.ResizableWindowStyle) && !NativeUiWindowPolicy.CanMaximize(NativeUiWindowPolicy.ResizableWindowStyle), "Window allows controlled resizing without maximize");

var settings = new AppSettings();
Check(settings.CaptureRate == 75 && OutputPacing.EffectiveRate(settings.CaptureRate, settings.OutputRate) == 75,
    "Default Screen Sync captures and sends frames at the 75 Hz monitor rate");
Check(AppSettings.TryParse(settings.ToJson(), out var parsed, out _) && parsed == settings, "Settings JSON roundtrip");
Check(!AppSettings.TryParse("""{"Top":999}""", out _, out _), "Invalid settings rejected");
Check(SettingsResetPolicy.Reset(settings with { Brightness = 10 }) == settings, "Reset returns the complete default settings profile");
Check(OutputPacing.EffectiveRate(75, 3) == 75 && OutputPacing.EffectiveRate(75, 1) == 30, "Capture and output pacing rates");
Check(new ColorRgb(200, 100, 50).Scale(.5) == new ColorRgb(100, 50, 25), "Brightness scaling");

var audioLayout = layout.ToArray();
var audioColors = new ColorRgb[audioLayout.Length];
var silentBands = new float[8];
var leftOnly = new AudioAnalysis(.4f, silentBands, silentBands, silentBands, .8f, 0);
AudioEffectEngine.RenderInto(leftOnly, audioLayout, audioColors);
var leftEdgeLit = audioLayout.Select((point, index) => (point, color: audioColors[index]))
    .Count(item => item.point.Edge == LedEdge.Left && item.color != default);
var rightEdgeLitByLeft = audioLayout.Select((point, index) => (point, color: audioColors[index]))
    .Count(item => item.point.Edge == LedEdge.Right && item.color != default);
Check(leftEdgeLit == 14 && rightEdgeLitByLeft == 0, "Music Sync pans left-channel audio to the left edge");
var stereo = new AudioAnalysis(.8f, silentBands, silentBands, silentBands, .8f, .8f);
AudioEffectEngine.RenderInto(stereo, audioLayout, audioColors);
Check(audioColors.All(color => color != default), "Music Sync can illuminate every LED at high audio level");
var quietStereo = new AudioAnalysis(.05f, silentBands, silentBands, silentBands, .05f, .05f);
AudioEffectEngine.RenderInto(quietStereo, audioLayout, audioColors, sensitivity: 2);
var quietLitCount = audioColors.Count(color => color != default);
Check(quietLitCount is > 0 and < 54, "Quiet music produces a dim partial progression without filling the strip");

const int width = 32, height = 32, stride = width * 4;
var pixels = new byte[stride * height];
for (var i = 0; i < pixels.Length; i += 4)
{
    pixels[i] = 200; pixels[i + 1] = 150; pixels[i + 2] = 100; pixels[i + 3] = 255;
}
var singleEdge = new LayoutOptions(Top: 1, Right: 0, Bottom: 0, Left: 0, Depth: .2, StartCorner: LedCorner.TopLeft);
var processor = new FrameProcessor();
unsafe
{
    fixed (byte* pixelAddress = pixels)
    {
        var firstSample = processor.Sample(pixelAddress, width, height, stride, singleEdge, smoothing: 0)[0];
        Check(firstSample == new ColorRgb(100, 150, 200), "BGRA screen sampling produces the expected RGB color");
        Array.Clear(pixels);
        var smoothed = processor.Sample(pixelAddress, width, height, stride, singleEdge, smoothing: .5)[0];
        Check(smoothed == new ColorRgb(50, 75, 100), "Temporal smoothing blends successive screen samples");
    }
}

Console.WriteLine("LumaSync core regression checks passed.");
