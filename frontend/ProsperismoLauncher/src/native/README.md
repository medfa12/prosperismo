# Prosperismo Windows host boundary

The TypeScript launcher deliberately owns library traversal, metadata parsing,
settings inheritance, routing, and emulator argument construction. A small RNW
native module named `ProsperismoHost` provides only operating-system work:

- `listDirectory(path)` returns `{name, path, kind, symbolicLink}` entries.
- `readTextFile(path)` reads UTF-8 `param.json`.
- `readBinaryFile(path)` reads trophy packages without text transcoding.
- `writeTextFile(path, contents)` atomically materializes edited patch plans.
- `canonicalizePath(path)` returns the resolved absolute path.
- `chooseGameDirectories()` opens a multi-folder picker.
- `loadLauncherSettings()` and `saveLauncherSettings(json)` use the per-user
  application-data location (not the old Qt system-scope INI location).
- `findEmulator()` searches packaged and development locations for
  `prosperismo_emulator.exe`.
- `fileExists(path)` supports optional `_Patches/<TITLE_ID>.json` discovery.
- `openPath(path)` delegates to the registered Windows shell handler.
- `removeDirectories(paths, titleId, confirmed)` deletes only confirmed,
  non-reparse `_SaveData/<TITLE_ID>` directories and reports individual failures.
- `launch(executable, args, workingDirectory)` creates a detached console
  process, passes each argument without shell re-parsing, and reports running,
  exited, or failed lifecycle events to the launcher.

The adapter is implemented in `windows/Prosperismo/ProsperismoHost.*` using the
attributed C++ module API shared by RNW 0.83 and 0.84. Filesystem strings cross
the boundary as strictly validated UTF-8. Settings are replaced atomically in
`%LOCALAPPDATA%/Prosperismo`, and process launch uses `CreateProcessW` with one
quoted argument at a time rather than shell parsing.

Startup phases and failures are durably appended to
`%LOCALAPPDATA%/Prosperismo/launcher-startup.log`. For bounded native-module
isolation only, `PROSPERISMO_DISABLE_HOST=1` starts the same launcher without
registering `ProsperismoHost`; it does not alter the JavaScript application.
`PROSPERISMO_PROBE_WINAPPSDK=1` performs only the dispatcher, compositor, and
AppWindow activation sequence, logs the failing step, and exits without loading
React Native. It is intended for installation/runtime diagnosis.
