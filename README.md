# LumaSync

LumaSync is a working, independent low-memory replacement for the DX Light Windows application. It provides offline ambient lighting for a USB LED strip, including real-time screen sync, static colors, lighting effects, and local desktop-audio mode.

The app uses a compact native Windows interface and processes display and audio data on the PC. It does not upload or save captured screen frames or audio.

## Features

- Real-time screen-edge sampling with a separate color region for each LED.
- LED layout, monitor selection, crop, sampling depth, brightness, smoothing, saturation, gamma, and color temperature controls.
- Static color picker, built-in effects, and desktop-audio reactive lighting.
- On/off control, saved settings, reset and import/export, notification-area controls, and optional startup with Windows.
- Resizable interface that maintains its designed proportions and redraws on resize.
- Local-only operation with no telemetry, account, advertisements, or automatic updater.

The default strip layout is 26 LEDs across the top, 14 down the right, 14 up the left, and none along the bottom, starting at the bottom-left and proceeding clockwise.

## Compatible controller

LumaSync works with the 54-LED USB HID controller identified as VID `1A86`, PID `FE07`, device `000603`, firmware `1.8.5`. The lighting frame output supports 30, 60, and 75 Hz targets. Other controller models and firmware are not claimed as compatible.

## Privacy and settings

Settings are stored locally at `%LOCALAPPDATA%\LumaSync\settings.json`. Startup registration is optional and uses the current user's Windows startup entry. Normal operation does not require administrator privileges or an internet connection.

## Build and run

Requirements: Windows 10 or 11 and the .NET 10 SDK. The framework-dependent release requires the .NET 10 Windows Desktop Runtime. Package restore is needed for the first build; the application itself works offline.

```powershell
dotnet build .\LumaSync.slnx -c Release
dotnet run --project .\src\LumaSync.App\LumaSync.App.csproj -c Release
dotnet publish .\src\LumaSync.App\LumaSync.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

The published executable is `publish\LumaSync.exe`. A Windows GitHub Actions workflow builds the app and publishes an x64 artifact.

## Project layout

- `src/LumaSync.App`: Windows UI, display capture, color processing, HID controller, audio loopback, and settings.
- `tests/LumaSync.Tests`: automated regression checks for core behavior.
- `ARCHITECTURE.md`: application components and data flow.
- `PROTOCOL.md`: supported controller and lighting frame format.
- `THIRD_PARTY_NOTICES.md`: external package information.

## License

MIT. See [LICENSE](LICENSE).
