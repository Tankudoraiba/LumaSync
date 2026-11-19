# LumaSync 0.2.5

## What's fixed

- Sends the controller's dedicated turn-off command four times when the lighting worker exits, matching DX Light's shutdown behavior. This replaces the single black screen-sync frame, which could leave the LEDs on when Windows shuts down.
