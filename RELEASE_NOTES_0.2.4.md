# LumaSync 0.2.4

## What's new

- Pauses LED frame output while a Windows-key shortcut is active, preventing shortcut-related keyboard reports from colliding with controller frames. Output resumes after the key is released and a short settling interval passes.
- Updates Windows shutdown handling to stop capture and send the controller's all-black LED frame before shutdown proceeds. Capture resumes if Windows cancels shutdown.
- Renames the debug counter from lock-key events to keyboard events to reflect the expanded output gate.
