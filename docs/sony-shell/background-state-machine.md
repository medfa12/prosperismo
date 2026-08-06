<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background state machine, decoded

The enums driving the animated background are now recovered **with their
numeric values**, from the CLI metadata in the decrypted `BGLayer.dll.sprx`.
Names alone were not actionable — the shell selects a light mode by number, so
`LightRenderModeIndex` was useless as a list of labels.

Tool: [`tools/shell-recovery/cli_enums.py`](../../tools/shell-recovery/cli_enums.py).
Full dumps at `ps5oracle/shell_ui/bglayer_api/enums_<version>.txt` (gitignored).
12.40 carries **87 enums**; 3.00 has 41.

## GlobalBackgroundState

The states the background reacts to, exactly as observable on the console:

| Value | Name |
|---|---|
| 0 | `None` |
| 1 | `Black` |
| 2 | `ColdBootAnimation` / `BootAnimation` |
| 3 | `WarmBootAnimation` |
| 4 | `InitialBootAnimation` |
| 5 | `InitialSetup` |
| 6 | `InitialWelcomeScreenAnimation` |
| 7 | `InitialWelcomeScreenFadeOutAnimation` |
| 8 | `Login` |
| 9 | **`ParticleBottom`** |
| 10 | **`ParticleSpread`** |
| 11 | `NoParticle` |
| 12 | `Shutdown` |
| 13 | `ShutdownAnimation` |
| 14 | `FadeOutShutdownAnimation` |

`ParticleBottom` (9) and `ParticleSpread` (10) are the ambient states — the
ordinary home-screen background, and what
`ps5oracle/shell_ui/live_background/default.mp4` captures.

3.00 lacks `ShutdownAnimation`; it was inserted at 13 and shifted
`FadeOutShutdownAnimation` to 14. 3.00 also carries an `OnBoardingReboot` member
that later firmware drops.

## LightRenderModeIndex

Numbered from 65, with a gap between 72 and 78:

| Value | Name |
|---|---|
| 65 | `NoParticle` |
| 66 | `InitialWelcomeNoParticle` |
| 67 | `Bottom` |
| 68 | `Spread` |
| 69 | `ColdBoot` |
| 70 | `WarmBoot` |
| 71 | `InitialBoot` |
| 72 | `Shutdown` |
| 78 | `Black` |
| 79 | `None` |

Values 73–77 are absent from the metadata. Whether they are unused or defined
elsewhere is not established.

## Transitions

**`BackgroundTransitionType`** — `Invalid` (-1), `LaunchingGame` (0), `Hide` /
`HideGameSplash` (1), `LaunchingGameBC` (2), `SystemDefault` (5),
`CustomImageRipple` / `CustomImage` (6), `CustomImageSlideInLeft` (7),
`CustomImageSlideInRight` (8), `CustomImageFade` (9), `CustomImageRippleBack`
(10), `FadeToBlack` (11). Note 3 and 4 are unassigned.

**`BackgroundTransitionDegree`** — `CrossFade` (0), `Subtle` (1), `Normal` (2),
`Strong` (3).

**`BackgroundTransitionFlag`** — a bitfield: `EndTransition` (1),
`RequestOldImage` (2), `RequestNextImage` (4), `RequestNextOverlayImage` (8),
`CanceledTransition` (16), `Cancelable` (32), `RequestFallbackImage` (64),
`BasematAnimationInProgress` (128).

**`WaveColourPreset`** — `InitialSetup` (0), `MiniApp` (1), `SystemArea` (2),
`MusicUnlimited` (3), `HomeScreen` (4).

**`LightParticleFlag`** — `None` (0), `IsReady` (1), `PauseParticle` (2).

These confirm the values that
[`bglayer-managed-contract.md`](bglayer-managed-contract.md) recorded from a
different route, and extend them.

## Correction: the scene geometry is loaded from files, not built in code

[`background-is-a-3d-scene.md`](background-is-a-3d-scene.md) reasoned that
because `CreateBasicModel` and `CreateLightShaftModel` sit beside the scene
builder, the room and shaft were likely **constructed in code** — which would
have made the geometry recoverable by reading the builder, and reduced the
outstanding asset requirement to four textures.

`HmuModelResult` contradicts that:

| Value | Name |
|---|---|
| -102 | `SequenceNotFound` |
| -101 | `FileLoadFailed` |
| -100 | `FileNotFound` |
| 0 | `Ok` |

A model API does not have `FileNotFound` and `FileLoadFailed` error codes unless
models come from files, and `SequenceNotFound` says the same for sequences. The
`LoadFreeForm` symbol noted as an open question in that document is the loader.

This is a material setback for the port: the room geometry and the sequence
timelines are **asset data that no dump on hand contains**, not code that can be
read out of the eboot. The `Create*Model` names are constructors over loaded
data, not generators.

## Corroboration: the post-process chain

`HmuModelAntiAliasingType` — `Default` (0), `FXAA_SSAAx4` (1), `FXAA` (2),
`Copy` (3) — matches one-for-one the four `.ags` shader assets found in the
eboot's string table (`effect_post_process_fxaa_ssaa_x4_p`,
`effect_post_process_fxaa_p`, `effect_post_process_copy_p`,
`effect_post_process_vv`). Two independent routes agreeing on the same set is
worth more than either alone.
