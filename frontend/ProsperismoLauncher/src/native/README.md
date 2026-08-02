# Prosperismo Windows host boundary

The TypeScript launcher deliberately owns library traversal, metadata parsing,
settings inheritance, routing, and emulator argument construction. A small RNW
native module named `ProsperismoHost` must provide only operating-system work:

- `listDirectory(path)` returns `{name, path, kind, symbolicLink}` entries.
- `readTextFile(path)` reads UTF-8 `param.json`.
- `canonicalizePath(path)` returns the resolved absolute path.
- `chooseGameDirectories()` opens a multi-folder picker.
- `loadLauncherSettings()` and `saveLauncherSettings(json)` use the per-user
  application-data location (not the old Qt system-scope INI location).
- `findEmulator()` searches next to the launcher and one directory above for
  `kyty_emulator.exe`. The filename is a compatibility boundary; all visible
  product branding remains **Prosperismo**.
- `fileExists(path)` supports optional `_Patches/<TITLE_ID>.json` discovery.
- `launch(executable, args, workingDirectory)` creates a detached console
  process and passes each argument without shell re-parsing.

RNW 0.84 native generation succeeded, but this machine has neither Visual
Studio nor MSBuild, so the module implementation and native build cannot be
validated here. Until the adapter is registered, the UI remains navigable and
reports the unavailable host operation instead of fabricating filesystem data.
