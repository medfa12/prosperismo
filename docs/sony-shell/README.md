# Sony shell evidence and migration

This directory preserves the evidence-backed PS5 shell work imported from the
SharpEmu `codex/ps5-shell-integration` handoff at commits `687729af` and
`d651a3b0`. It records recovered contracts, measurements, confidence levels,
known unknowns, and dead ends. Read these documents before changing shell
geometry, focus, animation, Home/Game Hub protocols, firmware-background
replay, or system-asset handling.

Start with:

1. [Current state](ps5-ui-state-of-work.md)
2. [Evidence index](ps5-reverse-engineering-index.md)
3. [Known unknowns](ps5-unknowns.md)
4. [Original migration handoff](kytyps5-shell-migration-handoff.md)

## Prosperismo architecture

Prosperismo has two host UI routes in one React Native Windows application:

- **Launcher** is a behavioral port of Kyty's Qt launcher. It owns local game
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
4. PAL, Mesa/ACO, Kyty and other implementations as implementation references,
   never as proof of Sony behavior.

Keep claims labelled **CONFIRMED**, **DIFFERENTIAL**, **ASSUMED**, or
**RETRACTED**. Preserve failed approaches when they eliminate a plausible
cause; do not turn a dead end into an undocumented deletion.
