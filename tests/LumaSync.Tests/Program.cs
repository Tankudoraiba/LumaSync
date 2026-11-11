using LumaSync;
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var ledColors = Enumerable.Range(0, 54).Select(i => new ColorRgb((byte)i, 20, 30)).ToArray();
var frame = ScreenSyncFrameBuilder.Build(ledColors, 7);
Check(frame.Length == 277 && frame[0] == 0x53 && frame[1] == 0x43, "SC frame header and length");
Check(frame[6] == 1 && frame[7] == 0 && frame[8] == 20 && frame[9] == 30, "First LED RGB serialization");
Check(frame[271] == 54, "Last LED index");
byte checksum = 0;
for (var i = 0; i < frame.Length - 1; i++) checksum = unchecked((byte)(checksum + frame[i]));
Check(frame[^1] == checksum, "Frame checksum");
Check(ScreenSyncFrameBuilder.ToWindowsHidReports(frame).Count == 5, "HID report chunk count");

var layout = new LayoutOptions().Create();
Check(layout.Count == 54 && layout.Count(x => x.Edge == LedEdge.Top) == 26 && layout.Count(x => x.Edge == LedEdge.Right) == 14 && layout.Count(x => x.Edge == LedEdge.Left) == 14, "Default 54 LED edge layout");
Check(layout[0].Edge == LedEdge.Left && layout[0].U < .1 && layout[0].V > .5, "Default strip starts bottom-left and proceeds clockwise");
Check(NativeUiWindowPolicy.CanResize(NativeUiWindowPolicy.ResizableWindowStyle) && !NativeUiWindowPolicy.CanMaximize(NativeUiWindowPolicy.ResizableWindowStyle), "Window allows controlled resizing without maximize");
var settings = new AppSettings();
Check(AppSettings.TryParse(settings.ToJson(), out var parsed, out _) && parsed == settings, "Settings JSON roundtrip");
Check(!AppSettings.TryParse("""{"Top":999}""", out _, out _), "Invalid settings rejected");
Console.WriteLine("LumaSync regression checks passed (protocol framing, mapping, UI resize policy, settings validation).");
