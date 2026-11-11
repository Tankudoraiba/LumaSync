# Testing

## Automated regression harness

The test executable covers the portable and policy portions of the application, including color serialization, color transforms, LED edge mapping and ordering, brightness, smoothing, settings validation and reset, output pacing, UI coordinate/resizing policy, and device reconnect behavior.

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project .\tests\LumaSync.Tests\LumaSync.Tests.csproj -c Release
```

The harness is a plain executable rather than a test-framework project; a nonzero exit indicates a failed assertion.

## Recorded environment observations

- DXGI capture smoke test succeeded on the selected 2560x1440 display and produced a 54-color frame; a display-switch recovery smoke test also succeeded.
- Short actual-device updates were exercised at 30, 60, and 75 Hz for three seconds per rate, with roughly 10 ms average write time.
- WASAPI loopback capture started successfully near silence (observed level approximately 0.001). A loud left/right channel progression test has not been recorded in this repo snapshot.
- No extended run with a demanding full-screen game or repeated multi-hour disconnect/reconnect soak is claimed.

These observations are inherited development results, not a guarantee for every Windows machine. The standalone rebrand should be rebuilt and the regression harness rerun before release.

## Manual release checklist

1. Verify app opens and displays the controller status without elevation.
2. Check static red, green, blue, and off, then verify all 54 LEDs update.
3. Enable Screen Sync and verify top, side, corners, order, and monitor selection.
4. Resize in both directions and confirm the UI retains its designed ratio and redraws.
5. Unplug/reconnect the controller and verify bounded recovery.
6. Check minimize-to-tray, optional startup, saved settings, and clean shutdown behavior.
7. Observe CPU, memory, capture/output rates, and latency while a game is running.
8. Verify no outbound network connections and that no captured frames/audio are written.
