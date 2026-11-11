# LumaSync

LumaSync is an offline Windows ambient-lighting controller. It samples the edges of a selected display and sends spatially mapped colors to a supported USB HID LED controller. The application is written in C# and uses a compact Win32 user interface to keep its desktop footprint small.

## What it does

- Screen Sync samples edge regions and maps them to the configured LED layout.
- The default layout is 26 top, 14 right, no bottom, and 14 left LEDs, starting at the bottom-left and proceeding clockwise.
- Static color and built-in lighting and music effects are available in the current native UI.
- It supports display selection, color and sampling controls, import/export and reset of settings, optional startup registration, and tray behavior.
- The UI can resize while maintaining its designed aspect ratio; it repaints as the client size changes.
- Screen capture and desktop-audio loopback stay on the PC. Captured frames and audio are not saved by the app.

## Hardware

The implementation was developed against a USB HID controller reported as VID `1A86`, PID `FE07`, device identifier `000603`, firmware `1.8.5`, with 54 LEDs. The configured physical layout is the 54-LED arrangement above. Other controllers and firmware are not assumed compatible.

A short controlled hardware run measured 30, 60, and 75 frame updates per second for three seconds per rate, with approximately 10 ms average write time. This is evidence that the device accepted those short runs; it is not a long-duration stability or full-screen gaming benchmark. See [PROTOCOL.md](PROTOCOL.md) and [TESTING.md](TESTING.md).

## Privacy

LumaSync has no telemetry, cloud service, account, or update client. It does not require administrator privileges for normal use. Settings are stored locally under `%LOCALAPPDATA%\LumaSync\settings.json`; user-initiated import/export uses a JSON file. Optional Windows startup is a per-user Run registry entry and is off unless enabled in the app. The screen and audio pipelines are local.

## Build

Requirements: Windows 10 or 11, .NET 10 SDK, and network access to restore the pinned Vortice package on the first build. Normal operation is offline. A Windows Desktop .NET 10 runtime is required when using the framework-dependent release.

```powershell
dotnet build .\LumaSync.slnx -c Release
dotnet run --project .\tests\LumaSync.Tests\LumaSync.Tests.csproj -c Release
dotnet publish .\src\LumaSync.App\LumaSync.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

The executable is `publish\LumaSync.exe`. The project includes a Windows GitHub Actions workflow that builds, runs the test harness, and uploads a publish artifact.

## Status

This is an early standalone rebrand of a working low-memory implementation. Core mapping, processing, settings, UI policies, and device packet behavior have automated checks. The known 54-LED controller has been exercised directly. Visual validation on other display scaling settings, long-duration 75 Hz operation under game load, non-silent audio behavior, HDR edge cases, and other controllers remain unverified.

## Repository map

- `src/LumaSync.App`: native Win32 UI, DXGI capture, color processing, HID device code, local settings.
- `tests/LumaSync.Tests`: dependency-light executable regression test harness.
- `SECURITY_REPORT.md`: replacement security and privacy review.
- `ARCHITECTURE.md`: module boundaries and data flow.
- `PROTOCOL.md`: confirmed controller facts and known limits.
- `TESTING.md`: automated and hardware test scope.

## License

MIT. See [LICENSE](LICENSE). The application depends on Vortice.Direct3D11, which is restored from NuGet under its own license; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
