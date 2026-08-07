<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The draw-side SRT contracts, from the PSSL reflection table

The 12.40 eboot carries a PSSL reflection string table for every
`BackgroundLayer` shader. It sits at `0x1126160`–`0x1128100` and lists each
struct's members **in declaration order**. Zipped against the offsets the
shaders themselves load, it names fields that were previously blanks.

Method and its limits are in
[`METHODOLOGY-executing-sony-shaders.md`](METHODOLOGY-executing-sony-shaders.md)
§3: the reflection gives names with no offsets, the shader gives offsets with no
names, and only where the two account for a struct completely is the mapping
settled.

## `BackgroundLayer::SRTVsPs` — and the end of one assumption

| offset | member |
|---|---|
| `0x00` | `R` — the `ResourcesVsPs` pointer |
| `0x08` | `time` |
| `0x0C` | `timeStep` |
| `0x10` | `transPatternFlag` |
| **`0x14`** | **`colorPatternFlag`** |

[`particle-draw-executed.md`](particle-draw-executed.md) closed with one input
labelled as an assumption: `SRTVsPs + 0x14`, which selects which half of
`particle_p`'s seven-entry palette is used — the four bright face-button hues
when non-zero, the warm gold/amber/brown set when zero.
[`particle-draw.md`](particle-draw.md) recorded it as host-written and never
read out of the firmware.

**It is `colorPatternFlag`** — a per-draw colour selector sitting directly
beside `transPatternFlag`, which is the pattern index. So it is a first-class
authored input of the same family, not an incidental host value.

That is the name, not the value. Which value the shell writes for a given
`GlobalBackgroundState` is still unrecovered, and the render in
`particle-draw-executed.md` still writes zero by assumption. What has changed is
that the question is now well posed: find the writer of `SRTVsPs + 0x14`, not
"find whatever this dword is".

## `BackgroundLayer::ResourcesVsPs` — the small-particle draw block

Names from reflection; offsets confirmed against `particle_vv`/`particle_p`
scalar loads.

| offset | member |
|---|---|
| `0x00` | `particleProperties` (V#) |
| `0x10` | `particleIds` (V#) |
| `0x20` | `numParticles` |
| `0x24` | `offsetParticle` |
| `0x28` | `indexStridePerParticle` |
| `0x2C` | `maxParticleId` |
| `0x30` | `blendBlur` |
| `0x50` | `distStartBlurAgain` |
| `0x54` | `distEndBlurAgain` |
| `0x58` | `randSeed` |
| `0x5C` | `unblurMinSize` |
| `0x60` | `unblurMaxSize` |
| `0x64` | `blurMaxSize` |
| `0x68` | `blendDistDarken` |
| `0x88` | `intensityDistDarken` |
| `0x8C` | `numLights` |
| `0x90` | `lights` |
| `0x138` | `cameraZ` |
| `0x13C` | `groupId` |

This confirms the binding used by the shipped render: `particleProperties` at
`+0x00` and `particleIds` at `+0x10`. Note the order is **the opposite** of
`ResourcesCs`, where the ID buffer is first — the two blocks are different
structs reached through different SRTs, and cross-indexing them is a mistake
[`particle-draw.md`](particle-draw.md) already warns about.

## `BackgroundLayer::SRTLargeParticleVsPs` / `ResourcesLargeParticleVsPs`

The large soft discs — the out-of-focus bokeh visible in the login sequence —
are a separate contract, and this is its first complete listing.

`SRTLargeParticleVsPs`: `R`, `time`, `timeStep`, `transPatternFlag`. **No
`colorPatternFlag`** — the large particles are textured, so they have no
palette to select.

`ResourcesLargeParticleVsPs`, in declaration order:

`particleProperties`, `particleIds`, **`backgroundTex`**, **`backgroundTex2`**,
`textureOptions`, `particleColorInHsv`, `useCamera`, `camera`, `randSeed`,
`numParticles`, `offsetParticle`, `indexStridePerParticle`, `maxParticleId`,
`blendEdgeBlur`, `edgeBlurMaxSize`, `transparency`, `parMinSize`, `parMaxSize`.

`camera` is a `BackgroundLayer::CameraProperty`: `fovY`, `aspect`, `near`,
`far`.

Offsets confirmed from `large_particle_p`'s own loads: it reads **`0x20` as
`s_load_dwordx16`** — 64 bytes, which is exactly two 32-byte texture
descriptors, so `backgroundTex` is at `0x20` and `backgroundTex2` at `0x40`.
Then `textureOptions` at `0x60`, `particleColorInHsv` at `0x64` (three floats,
read as a `dwordx2` plus a `dword`), `useCamera` at `0x70`, and `camera` at
`0x74`, which `large_particle_vv` reads as `dwordx2` at `0x74` plus a `dword`
at `0x78`. `large_particle_vv`'s `s_load_dwordx8` at `0x84` then covers
`randSeed` through `maxParticleId`.

The two textures are the assets already on disk:
`Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and `…Particle1.gnf`, 167,936 bytes
each. `particle-draw.md` records the pixel program as cross-fading two sampled
textures and then HSV-tinting — `particleColorInHsv` is that tint, named.

## What this unblocks and what it does not

Unblocked: the large-particle pair now has a complete, named input list, so
wiring it needs only image and sampler bindings in the runner — the same
capability the blur and FXAA passes need.

Not unblocked: the value of `colorPatternFlag`, and the mapping from
`WaveColourPreset` to concrete colours
([`bglayer-background-states.md`](bglayer-background-states.md)). Both are host
state written per screen, and both need the writer found rather than another
table read.
