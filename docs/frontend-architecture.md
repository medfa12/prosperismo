# Prosperismo frontend architecture

The frontend is React Native Windows with TypeScript. It intentionally replaces
Kyty's Qt deployment while leaving the native C++ emulator, Vulkan renderer,
SDL input/audio and FFmpeg media path independent.

## Routes

- `launcher`: desktop game table and all operational features of the imported
  Qt launcher.
- `home`: Big Picture route adapted from the recovered PS5 Home work.

Both routes use a single state store and a single native service boundary.
Changing routes must not rescan titles, duplicate settings, or terminate an
active emulation session.

## Native boundary

The Windows module owns filesystem traversal, process creation, safe save-data
operations, and native dialogs. Its launch request maps exactly to
`prosperismo_emulator`'s public command line; no shell-only emulator configuration is
permitted.

High-rate game frames are not transported through React Native. The native
emulator owns its Vulkan presentation window. The frontend receives only
session lifecycle and bounded telemetry events.

## Sony boundary

The host UI may reproduce contracts and measured behavior documented under
`docs/sony-shell`. It must not embed or redistribute the local Sony bundles or
assets under `ps5oracle`. Preview readiness, placeholder notifications, or
invented Game Hub payloads must remain explicitly diagnostic and disabled in
production.
