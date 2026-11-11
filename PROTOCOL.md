# Controller protocol notes

## Confirmed device under test

- USB VID:PID: `1A86:FE07`
- Device-reported identifier: `000603`
- Firmware string: `1.8.5`
- Transport: HID, as observed by the implementation and host device enumeration
- LED count: 54 for the tested setup

These observations describe one connected device and must not be generalized to every controller sold with a similar product.

## Frame output

The implementation uses the `SC` frame format in `ScreenSyncFrameBuilder` and the corresponding HID report path. Each LED color is serialized in the observed per-LED five-byte slot; the current 54-color frame buffer reserves a seven-byte command prefix and `54 * 5` bytes for color slots. Unused slots remain zero-filled. The source and tests are authoritative for exact byte layout and validation; do not infer support for firmware update or unrelated command codes from this lighting-only implementation.

The `DeviceInfoProtocol` parser handles the device-information response currently used by the app. Packet parsing validates expected lengths/identifiers before accepting the response. No firmware update command is implemented.

## Tested behavior

The actual 54-LED unit was used for controlled color operations and short 3-second output runs at 30, 60, and 75 updates per second. The recorded average write duration was about 10 ms. That test indicates the short runs were accepted; it does not establish a safe sustained maximum or guaranteed display-to-light latency. See `TESTING.md`.

## Unknowns

- Compatibility with other VID/PID pairs, hardware revisions, and firmware versions.
- Long-duration reliability at 75 Hz, particularly while a game is under load.
- Controller-side buffering and precise optical end-to-end latency.
- Whether all color and brightness controls have identical firmware semantics on other revisions.

Do not add undocumented commands without controlled evidence. The project deliberately contains no firmware-writing functionality.
