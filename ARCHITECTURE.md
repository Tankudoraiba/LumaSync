# Architecture

## Runtime flow

1. The native Win32 UI loads validated local settings and discovers HID interfaces matching the implemented controller identification logic.
2. The selected display is captured by the DXGI Desktop Duplication path using D3D11. Capture polling and LED output are separately paced; output favors the newest frame so stale frames do not accumulate in a queue.
3. Edge sampling and `LedMapper` map screen regions to physical LED positions, then `FrameProcessor` applies configured color adjustments and temporal smoothing.
4. `ScreenSyncFrameBuilder` serializes colors into the observed frame command, and `LatestFrameOutputPump` writes the latest data to the controller with bounded/reconnection handling.
5. Settings are validated and stored as JSON in the current user's LocalAppData directory. Startup registration, when enabled, uses the current-user Run key.

## Modules

- `Program.cs`: low-memory native window, controls, message loop, tray, display selection, and orchestration.
- `Capture/`: monitor enumeration, DXGI capture/downscaling, timing and selection policies.
- `Core/`: colors, layout, sampling, effects, smoothing, HDR transforms, output pacing.
- `Audio/`: local WASAPI loopback capture.
- `Controller/`: HID discovery, device information, frame serialization, reconnect and latest-frame output.
- `Settings/`: JSON validation, persistence, import/export, reset, and editor policies.
- `Windows/`: optional per-user startup registration.

## Boundaries and constraints

The UI is intentionally native Win32 rather than WPF to reduce framework overhead. DXGI capture uses Vortice.Direct3D11. HID I/O uses Windows APIs. No service, elevated helper, network client, screenshot store, or audio recording store is included. External package restore is a build-time operation only.
