# Security and privacy review

## Scope

This review covers the application source and project configuration in this repository. It is a source-level review, not a formal audit or proof of runtime behavior.

## Confirmed design findings

- The application code performs local DXGI display capture, local Windows audio loopback, HID communication, JSON settings, and optional per-user startup registration.
- The source contains no analytics, telemetry, cloud client, automatic update client, or application-level HTTP/DNS request code.
- Screen and audio processing occur locally. The application does not persist captured frames or loopback audio.
- Settings are stored under `%LOCALAPPDATA%\LumaSync`; import/export is initiated by the user.
- Optional startup registration uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. It is disabled unless selected in the app and requires no elevation.
- No Windows service, driver installation, firmware-writing path, or elevation request is implemented.
- No persistent diagnostic log writer is included; status and diagnostics are displayed in the running process.

## Risks and limits

- Settings are plain JSON in the current user's profile and are not intended for secrets.
- DXGI capture and WASAPI loopback can access desktop imagery and audio while their respective lighting modes are active. Processing is local.
- HID access is controller-specific; unsupported devices must not be treated as compatible.
- NuGet restore contacts configured package sources during development/build. Normal runtime has no intended network requirement.
- This source review does not prove absence of network behavior in every runtime environment. Release validation should monitor outbound connections in a clean Windows environment while exercising capture, audio, and USB modes.

## Severity and confidence

| Finding | Severity | Confidence |
|---|---|---|
| No intentional telemetry or remote service in application source | Low concern | High for inspected source; runtime not exhaustively monitored |
| Optional current-user startup registration | Low | High; explicit setting and HKCU Run code |
| Local unencrypted settings file | Low | High |
| Screen/audio capture privacy surface | Moderate, expected for feature | High |
| Third-party DXGI wrapper dependency | Low to moderate supply-chain consideration | High that package is referenced; verify package provenance when releasing |

## Review locations

Review `Windows/WindowsStartupRegistration.cs`, `Settings/AppSettings.cs`, and the capture/audio modules. For release validation, monitor outbound connections while exercising local features; no screen content or executable samples need to be uploaded to third parties.
