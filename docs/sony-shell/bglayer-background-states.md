<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background's state machine, from `BGLayer.dll.sprx`

Source: `ps5oracle/fwdb/<version>/BGLayer.dll.sprx`, the managed BGLayer
library. Its type metadata survives in the decrypted SPRX, so these are Sony's
own names and values — not inferred from behaviour. The dumps live in
`ps5oracle/shell_ui/bglayer_api/`.

Checked across **3.00, 12.40, 13.00 and 13.20**: identical, except that 3.00
has a single `Shutdown` tail where later firmware splits `ShutdownAnimation`
and `FadeOutShutdownAnimation`. A value stable over four firmware versions is
not a coincidence of one build.

## `GlobalBackgroundState`

| value | name |
|---:|---|
| 0 | `None` |
| 1 | `Black` |
| 2 | `ColdBootAnimation` / `BootAnimation` |
| 3 | `WarmBootAnimation` |
| 4 | `InitialBootAnimation` |
| 5 | `InitialSetup` |
| 6 | `InitialWelcomeScreenAnimation` |
| 7 | `InitialWelcomeScreenFadeOutAnimation` |
| **8** | **`Login`** |
| 9 | `ParticleBottom` |
| 10 | `ParticleSpread` |
| **11** | **`NoParticle`** |
| 12 | `Shutdown` |
| 13 | `ShutdownAnimation` |
| 14 | `FadeOutShutdownAnimation` |

**`NoParticle` is a first-class background state.** The particle field is not
merely faded out in settings — the shell switches to a state whose name says
the particles are absent. Whatever remains on screen there is, by elimination,
the plate and the wave. That settles from Sony's own naming what was until now
an observation about a capture.

`ParticleSpread` (10) and `ParticleBottom` (9) are the two steady-state home
screens, and they line up exactly with the serialized pattern blobs
`spread_expanded` and `bottom_*` recovered in
[`particle-live-simulation.md`](particle-live-simulation.md).

## `LightRenderModeIndex`

The particle mode, selected per state:

| value | name |
|---:|---|
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

`Bottom`, `Spread` and `ColdBoot` are the pattern blobs by name. `NoParticle`
and `None` are distinct values, so "no particle system" and "no background"
are different things.

## `WaveColourPreset` — the wave is its own layer

| value | name | | value | name |
|---:|---|---|---:|---|
| 0 | `InitialSetup` | | 11 | `Boot` |
| 1 | `MiniApp` | | 12 | `Store` |
| 2 | `SystemArea` | | 13 | `PsVideo` |
| 3 | `MusicUnlimited` | | 14–21 | `ThemeFlow0`…`ThemeFlow7` |
| 4 | `HomeScreen` | | | |
| 5 | `NoWave` | | | |
| 6 | `Black` | | | |
| 7 | `WhatsNew` | | | |
| 8 | `MusicUnlimitedSplash` | | | |
| **9** | **`Login`** | | | |
| 10 | `LoginNoUserLogined` | | | |

This is the decisive structural point: **the wave has its own colour preset,
indexed separately from the particle mode**, and it has its own visibility and
opacity controls — `ShowWave`, `MaskWave`, `WaveOpacity`, plus `WaveGpuTime`
and `WavePostGpuTime` counters. So the light shafts are an independently
controlled layer that persists across states where the particles do not, which
is exactly the behaviour a capture shows in settings.

`Login` (9) and `LoginNoUserLogined` (10) are separate presets, so the login
screen's wave colour is authored, not shared with `HomeScreen` (4).

## Where this leaves the render

The two layers this repository executes today —
[`firstwave-plate-executed.md`](firstwave-plate-executed.md) and
[`particle-draw-executed.md`](particle-draw-executed.md) — correspond to
`ParticleSpread`. The missing layer is the wave, and this file names its
controls: preset, opacity and mask.

**Not yet recovered:** the mapping from a `WaveColourPreset` value to concrete
colours. `FirstWave::Initialize` seeds six palette records and reset selects
record 4; there are 22 presets, so either a larger table exists or the presets
resolve through another indirection. Until that is read, rendering a specific
screen's wave colour is not possible from firmware data alone.
