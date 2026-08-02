# Launcher and shell architecture

## Ownership

The frontend is split at an intentionally narrow operating-system boundary:

```text
React Native / TypeScript
  routes + focus state + library model + param.json + settings + argv
                              |
                              v
ProsperismoHost native module
  directory/file picker + filesystem + settings file + process launch
                              |
                              v
kyty_emulator.exe compatibility executable
```

The executable filename is retained only for compatibility with the existing
native build output. It is not a visible product name.

The Desktop route is a direct responsibility port, not a visual copy of the Qt
widgets. The Big Picture route owns the console-like Home and emulator Settings
surfaces. F10 is reserved for direct Home/Settings switching once native key
routing is connected; it must not summon Desktop settings.

## Legacy launcher parity contract

The TypeScript core independently implements the behavior recovered from:

- `src/launcher/src/configurationListWidget.cpp`
- `src/launcher/include/configuration.h`
- `src/launcher/src/mainDialog.cpp`
- `src/launcher/src/patchesDialog.cpp`

Important compatibility details are locked by Jest tests:

1. Each configured root begins scanning at its child directories.
2. Traversal is breadth-first, ignores symbolic-link directories, and stops
   descending when a directory contains `eboot.bin`.
3. Canonical paths are deduplicated case-insensitively on Windows.
4. Metadata comes from `sce_sys/param.json`; localized title, version, and
   required-firmware fallbacks match the legacy launcher.
5. Emulator arguments retain their exact ordering and lowercase boolean text.
6. `_Patches/<UPPERCASE_TITLE_ID>.json` is appended only when present.

## Recovered shell provenance

The Big Picture boundary is based on documented facts and cleanly re-authored
state/layout code from the authoritative migration handoff:

- `C:/sharpemu-integration/docs/kytyps5-shell-migration-handoff.md`
- `C:/sharpemu-integration/docs/ps5-rn-layout.md`
- `C:/sharpemu-integration/docs/ps5-home-structure.md`
- `C:/sharpemu-integration/docs/ps5-focus-highlight.md`
- `C:/sharpemu-integration/docs/ps5-settings-integration.md`

The current shell preserves the closed 56 x 56 circular system-button geometry
and separates Home from emulator Settings. More complete focus graph, motion,
hub protocol, and renderer work should be ported from the behavior/tests named
in the handoff, without importing Avalonia control structure.

Local readable Sony RN bundles under `ps5oracle/sony/useful rnps` are
ground-truth research material. They and all proprietary assets remain outside
the frontend package and Git history.

SharpEmu migration sources are GPL-2.0-or-later; retain their attribution when
porting documented behavior or source-derived tests. React Native Windows is
MIT licensed. Confirm whole-project licensing before redistribution.
