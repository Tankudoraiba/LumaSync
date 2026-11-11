# USB controller protocol

## Supported hardware

The current device support is for a 54-LED USB HID controller with these reported identifiers:

- VID:PID `1A86:FE07`
- Device identifier `000603`
- Firmware `1.8.5`

Other controllers and firmware versions are not claimed as compatible.

## Lighting frames

LumaSync sends `SC` lighting frames through the HID output interface. A frame has a seven-byte command envelope and a five-byte slot for each LED. Each slot carries the LED index and its RGB values. The 54-LED frame is split into 64-byte HID payloads with a leading zero report ID.

Device information is read during connection so the app can confirm the reported LED count and firmware before sending lighting frames. The application does not contain firmware-writing commands.

## Update rates

Screen capture and lighting output can be configured for 30, 60, or 75 Hz, subject to display and controller performance. The 75 Hz option matches the configured 75 Hz monitor setup.
