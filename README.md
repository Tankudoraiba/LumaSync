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

## Color calibration

The color picker includes calibration swatches that preview directly on the LEDs. Previewing a swatch does not save it as the static color; choose **Save Color** to do that. Hue correction applies to Screen Sync and the other lighting modes. It supports three adjustable hue bands, each with its own hue range and red, green, and blue gains.

All three hue bands start with neutral 100% red, green, and blue gains, including after **Reset to Defaults**. The first band covers violet hues from 260° to 290°; the other ranges start at 0°–120° and 120°–240°. The bands let users compensate for a controller or LED strip that reproduces particular hues unevenly. Set each band's hue range to target a color family, then adjust its RGB gains: values above 100% boost a channel, values below 100% reduce it, and 100% leaves it unchanged. Adjusting a band only affects colors within its hue range. Preview calibration swatches on the LEDs while tuning; **Neutral RGB** restores all gains to 100% without changing the ranges.

## Compatible controller

LumaSync works with the 54-LED USB HID controller identified as VID `1A86`, PID `FE07`, device `000603`, firmware `1.8.5`. By default, capture and LED output both target 75 Hz for the 75 Hz monitor. You can change the capture and output rates to 30, 60, or 75 Hz. Other controller models and firmware are not claimed as compatible.

## Privacy and settings

Settings are stored locally at `%LOCALAPPDATA%\LumaSync\settings.json`. Startup registration is optional and uses the current user's Windows startup entry. Normal operation does not require administrator privileges or an internet connection.

## Build and run

Requirements: Windows 10 or 11 and the .NET 10 SDK. The framework-dependent release requires the .NET 10 x64 runtime installed separately. Package restore is needed for the first build; the application itself works offline.

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
