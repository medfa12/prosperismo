# Sony shell oracle and React Native implementation

This directory contains the evidence-backed shell contracts used by
Prosperismo. Firmware/oracle material is authoritative; imported historical
implementation work is retained separately for provenance. Read these
documents before changing shell geometry, focus, animation, Home/Game Hub
protocols, firmware-background replay, or system-asset handling.

Start with:

1. [React Native implementation](react-native-shell-migration.md)
2. [Current evidence ledger](ps5-ui-state-of-work.md)
3. [Evidence index](ps5-reverse-engineering-index.md)
4. [Known unknowns](ps5-unknowns.md)

## Prosperismo architecture

Prosperismo has two host UI routes in one React Native Windows application:

- **Launcher** is a behavioral port of the previous desktop launcher. It owns local game
  discovery, metadata, configuration, compatibility notes, patches, trophies,
  save-data actions, and native-emulator process supervision.
- **Big Picture** adapts the recovered Sony-like shell. It consumes the same
  game/session/settings services as the launcher; it does not maintain a
  second game database or fabricate guest state.

The native emulator remains C++20/Vulkan. A Windows native module exposes the
bounded host services needed by both JavaScript routes. Sony's actual retail
React Native bundles are oracle inputs, not distributable application code.
Public React Native does not provide the proprietary PUI/Vsh native modules;
Prosperismo must implement each used contract from evidence or eventually
execute the real guest module through emulation.

## Local oracle layout

The primary local evidence paths are now:

| Material | Primary location |
|---|---|
| SDK, firmware, symbols and reconstructed roots | `C:\prosperismo\ps5oracle\sony` |
| ISA and public driver references | `C:\prosperismo\ps5oracle\public-references` |
| Curated reference implementations | `C:\prosperismo\ps5oracle\reference-projects` |
| Shell rendering/execution captures | `C:\prosperismo\ps5oracle\evidence` |
| Legally obtained installed titles | `C:\prosperismo\games` |

Compatibility junctions preserve the old `C:\sharpemu\games`,
`C:\sharpemu\inspiration`, and evidence paths mentioned by historical notes.
New work should use the Prosperismo paths.

None of the oracle payload is tracked. Do not commit Sony SDKs, firmware,
decompiled bundles, symbols, fonts, icons, native assemblies, games, or
captures.

## Source hierarchy

When sources disagree, prefer:

1. Sony SDK 10.00 declarations, samples, host tools, firmware symbols and
   measured guest execution.
2. LLVM's gfx1013 target definitions and tests for uncovered ISA families.
3. Curated concrete implementations such as Fail0verflow Prosperous, PS5 Linux
   patches and maintained payload SDKs.
4. PAL, Mesa/ACO, and other implementations as implementation references,
   never as proof of Sony behavior.

Keep claims labelled **CONFIRMED**, **DIFFERENTIAL**, **ASSUMED**, or
**RETRACTED**. Preserve failed approaches when they eliminate a plausible
cause; do not turn a dead end into an undocumented deletion.
