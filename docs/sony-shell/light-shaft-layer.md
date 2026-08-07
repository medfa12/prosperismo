<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# `light_p` — the light-shaft layer

The layer behind the particles is **`light_p`**, at file offset `0x11F9700`,
sitting directly after `fw_background_p` in the shader descriptor table. It had
been in this repository's scans since the beginning as an unnamed program with
six `image_sample`; the descriptor table
([`shader-hardware-registers.md`](shader-hardware-registers.md)) names it, and
its PSSL reflection blob at `0x1125D88` gives its complete input list.

## Its inputs, from reflection

**Textures:** `texFloor`, `texVolume`, `texP`

**`VolatileCB`:** `time`, `opacity`, `intensity`, `particleAlpha`

**`ColorCb`:** `lightCol`, `lightColOnFloor`, `light2Col`, `light2ColOnFloor`,
`pointLightCol`, `pointLightAmbCol`, `themedColor`, `gamma`, `gintensity`,
`noise`

**Input:** `input.Uv` (TEXCOORD) → **Output:** `main.Color`

That is a two-light rig — `lightCol`/`light2Col` with separate
`…OnFloor` variants for where each light lands — plus a point light with its own
ambient, a volumetric texture, and a floor texture. `themedColor` is the same
value BGLayer exposes as `SetThemedLightColor`, which ties this shader to the
managed API in [`bglayer-background-states.md`](bglayer-background-states.md).

Its registers: `SPI_PS_INPUT_ENA/ADDR = 0x2`, `SPI_PS_IN_CONTROL.NUM_INTERP = 1`
— one interpolant, the UV. So it is a **screen-space pass**, not geometry. The
matching vertex program is `rect_uv_vv` (`0x11EEE00`), whose reflection is
`constants{reserve, pos}` → `main.TexCoord`, `main.Position`.

## `light_p` is a compositor, not only a shaft

`texP` and `particleAlpha` are the tell: the shader samples a **particle
target** and has a dedicated alpha for it. So `light_p` does not merely draw
shafts over the scene — it combines the particle layer with the lighting.

This changes the shape of the remaining work. The pipeline is not
"plate → particles → shafts on top". It is closer to:

```
plate                       fw_background_p
particles  -> texP          particle_c / particle_vv / particle_p
?          -> texVolume
?          -> texFloor
composite                   rect_uv_vv + light_p
```

The two layers this repository already executes are inputs to `light_p`, not
siblings of it.

## Where `texVolume` and `texFloor` come from — solved

They are **Sony's own GNF textures, embedded in the eboot**, not files.

`createIesTex` at `0xE9670` builds both. Its two calls are literal:

```
lea rdx, [rip+…]  -> 0x1006AE0     mov ecx, 0x4100    lea rsi, [rbx + 0x80]   ; texFloor
lea rdx, [rip+…]  -> 0x10029E0     mov ecx, 0x4100    lea rsi, [rbx + 0x88]   ; texVolume
```

Both blobs begin with the magic `GNF `, carry a `0xF8` header and `0x4000`
bytes of payload, and share an identical texture descriptor:

```
T# = 00000000 C010000C 801FC01F 90500204 00000000 00700000 00000000 00004000
```

They are genuinely different images — 16,059 of 16,640 bytes differ. The
descriptor words are recorded verbatim rather than decoded into width and
height, which would be inference.

**IES** is the photometric light-profile format (IES LM-63), so the name says
what these are: light distribution profiles. That is the shape of the shaft.

This is why every asset-path search came up empty. The eboot's asset string
table lists exactly two BGLayer textures, both large-particle sprites, and both
filesystem dumps agree — because these two are not files. Looking for them as
files was the wrong question, the same mistake as searching for the shader
descriptor table's pointers as values.

## The layer's constant buffers, recovered from the builder

The draw path at `0xEA390`–`0xEA55B` builds both buffers field by field.

**`ColorCb`** — the object field on the left, the constant-buffer offset on the
right, in the reflection's declaration order:

| CB offset | source | member |
|---|---|---|
| `0x00` | obj `+0xF0` | `lightCol` |
| `0x10` | obj `+0x100` | `lightColOnFloor` |
| `0x20` | obj `+0x110` | `light2Col` |
| `0x30` | obj `+0x120` | `light2ColOnFloor` |
| `0x40` | obj `+0x130` | `pointLightCol` |
| `0x50` | obj `+0x140` | `pointLightAmbCol` |
| `0x60` | obj `+0x170` | `themedColor` |
| `0x70` | obj `+0x160` | `gamma`, `gintensity` |
| `0x78` | obj `+0x168` | `noise` |

`themedColor` at obj `+0x170` is confirmed twice over:
`SetThemedLightColorNative` (native fn `0xD3110`, reached through `0xE9CC0`)
writes a `float4` to exactly that offset and sets a dirty flag at `+0xDD`, which
this builder tests before rebuilding.

**`VolatileCB`:**

| CB offset | member | source |
|---|---|---|
| `0x00` | `time` | obj `+0xCC` |
| `0x04` | `opacity` | obj `+0x58` |
| `0x08` | `intensity` | eased curve of obj `+0xD0`, scaled by obj `+0xD4` |
| `0x0C` | `particleAlpha` | obj `+0xD8`, **or zero when obj `+0xB0` is null** |

The easing is explicit in the instructions: with `t = obj+0xD0`, the shader
receives `0.5 · (2t)²` for `t < 0.5` and `0.5 · (2 − (2 − 2t)²)` for `t ≥ 0.5`,
then multiplied by obj `+0xD4`. A quadratic ease-in-out, read rather than fitted.

## Texture bindings, and why the particles vanish in settings

Three binds, through the same helper at `0xC222A0(context, slot, texture)`:

| slot | source | texture |
|---|---|---|
| 0 | obj `+0x80` | `texFloor` |
| 1 | obj `+0x88` | `texVolume` |
| 2 | obj `+0xB0`, **falling back to obj `+0x80`** | `texP` |

Samplers follow at `0xC21DA0`, slot 0 from obj `+0x98` and slot 1 from obj
`+0xA0`.

Obj `+0xB0` is the **particle render target**, and it is the same field that
gates `particleAlpha`. When it is null the shader gets `particleAlpha = 0` and
slot 2 is bound to `texFloor` as a harmless stand-in.

That is the mechanism behind `GlobalBackgroundState.NoParticle`
([`bglayer-background-states.md`](bglayer-background-states.md)): with no
particle target the light layer still draws, with its particle contribution
scaled to zero. The observation that the particles disappear in settings while
the shafts remain is now explained by the code, not by watching a capture.

## The colour presets: table found, contents not

The crossfade at `0xEA0C2` resolves the preset:

```
lea    rax, [rip+…]  -> 0x137CFC0     ; table base
movsxd rcx, dword ptr [rbx + 0xE0]    ; preset index, initialised to -1
shl    rcx, 7                         ; 128 bytes per record
add    rax, rcx
```

So the **`WaveColourPreset` table is at `0x137CFC0`, 128 bytes per record**,
indexed by the object field at `+0xE0`. Each record is a `ColorCb` — `0x7C`
bytes — padded to `0x80`. The draw path then crossfades the live colours at obj
`+0xF0`…`+0x170` toward that record.

**Its contents are not in the file** — it is runtime-initialised memory, seeded
by code, the same arrangement as `FirstWave::Initialize`'s palette records.
Reading the static bytes yields values with absurd exponents; those are not
colours.

**The seeder is at `0xEA786`**: a straight-line block of vector stores, one
record at a time. `tools/dump_wave_colour_presets.py` replays it — walk the
instructions, track which RIP-relative constant each vector register last
loaded, and apply every store into the table. Nothing is fitted; every float is
a constant the seeder itself names.

Twenty of the twenty-two records come out. Three checks say the replay is right:

- `gamma` is `0.45454` — **1/2.2** — in every record but three, and those three
  (`MiniApp` `0.607`, `SystemArea`/`MusicUnlimited` `0.560`) are deliberate
  variations, not noise.
- `themedColor` is `(1, 1, 1, 1)` everywhere: the identity default that
  `SetThemedLightColor` overwrites.
- `gintensity` forms a clean per-screen ladder — `1.0`, `0.52`, `0.32`, `0.27` —
  and `Login` (`0.52`) and `LoginNoUserLogined` (`0.32`) differ **only** in that
  one value, which is exactly what two variants of one screen should look like.

| preset | `lightCol` | `light2ColOnFloor` | `pointLightCol` | `gintensity` |
|---|---|---|---|---|
| `HomeScreen` | `0.072, 0.070, 0.073` | `2.0, 1.0, 1.3` | `0.063, 0.063, 0.060` | `1.0` |
| `Login` | `0.020, 0.020, 0.200` | `5.0, 5.0, 5.0` | `0.20, 0.20, 0.40` | `0.52` |
| `LoginNoUserLogined` | `0.020, 0.020, 0.200` | `5.0, 5.0, 5.0` | `0.20, 0.20, 0.40` | `0.32` |
| `Boot` | `0.020, 0.020, 0.200` | `5.0, 5.0, 5.0` | `0.20, 0.20, 0.40` | `0.27` |
| `Store` | `0.065, 0.065, 0.070` | `1.75, 1.75, 1.75` | `0.0, 0.0, 0.01` | `1.0` |
| `ThemeFlow2` | `0.100, 0.175, 0.300` | `3.0, 7.15, 9.0` | `0.15, 0.35, 1.10` | `1.0` |

`Login`'s dim blue with a bright neutral floor pool is the dark room the login
sequence sits in; `HomeScreen`'s near-neutral key with a warm `(2.0, 1.0, 1.3)`
floor is the home screen. `noise` is `0.008` in every record.

**`ThemeFlow6` and `ThemeFlow7` are not written by this block** and are reported
blank. Widening the disassembly window makes them appear to fill — with values
another function stores into the same range, `gamma = 200.0` where every genuine
record carries 1/2.2. The narrow window is deliberate: a blank is honest, a
plausible wrong number is not.

## What a complete `spread_expanded` background contains

Sampling all seven pattern blobs across their timelines settles which draw
programs a given background actually needs:

| pattern | small groups | large groups |
|---|---|---|
| `coldboot` | 0, 1 | **0, 1** |
| `spread_expanded` | 0–7 | none |
| `spread_expanded_fadeout` | 0–7 | none |
| `bottom_camCal` | 0–7 | none |
| `bottom_fadeout` | 0–7 | none |
| `initboot_to_spread_no_movie` | 0–7 | none |
| `initboot_to_bottom_no_movie` | 0–7 | none |

**`coldboot` is the only pattern that uses large particles.** Six of the seven
never activate a `large_compute` group at any time in their timeline, so
`large_particle_vv`/`_p` and the two GNF sprites are a boot-animation element,
not part of a steady-state background.

That means for `spread_expanded` — the living home-screen background — the
complete draw set is the eight small groups plus `light_p`, which is what
this repository executes. The large pair is still needed for `coldboot`, and
its sprites are BC7 480×270 with nine mips and the same `SW_256B_S` tiling,
which is a different untiling geometry from the 8bpp case because the element
is a 16-byte block rather than a texel.

An earlier revision of this note listed the large particles as a missing layer
of every background. That was wrong: they are missing from one pattern out of
seven.

## The colour crossfade, and what actually advances

`0xE9C10` is the preset setter — `(object, screen, variant, immediate)`. It
computes the record index as **`variant + screen * 4`**, which is why the table
reads as groups of four: `gintensity` runs `1.0`, `0.52`, `0.32`, `0.27` across
each group, so the four entries of a screen are dimming variants of one look.

Two paths out of it:

- `immediate` clear → the record is copied straight into the live colours at obj
  `+0xF0` and the duration at `+0xE8` is set to **-1**. A snap.
- `immediate` set, and obj `+0xD0`, `+0xD4`, `+0x58` all below `0.01` — the
  light effectively off — → the elapsed and duration at `+0xE4`/`+0xE8` are
  cleared and nothing else happens.

The tick at `0xE9CE0` runs the crossfade only when the duration is **positive**:

```
elapsed += dt
t       = elapsed / duration
eased   = 1 - (1 - t)^4            // quartic ease-out
```

and it blends by an incremental factor `((1-t0)^4 - (1-t1)^4) / (1-t0)^4`, so
repeated per-frame lerps reproduce that curve exactly rather than compounding.
It snaps to the target once `elapsed >= duration - 1` or the eased value passes
`0.9999`.

**Nothing in the light class ever sets a positive duration.** Every store to
`+0xE8` inside it writes `-1`. So the light does not advance on its own — it
changes when the shell's state machine asks for a preset with a duration. What
*does* advance by itself is the particle field, through the authored timeline in
the pattern blob.

`time` at obj `+0xCC` accumulates `dt * 0.001` — so the update takes
milliseconds — and wraps at `3600`, one hour.

## The full shader inventory

Resolving the descriptor table gives **138 shaders** with exact code offsets,
listed by `tools/dump_shader_registers.py`. The ones that bear on the
background, beyond those already documented:

| shader | file offset | note |
|---|---|---|
| `light_p` | `0x11F9700` | this layer |
| `rect_uv_vv` | `0x11EEE00` | its vertex stage |
| `fbm_tex_p` | `0x11EFA00` | procedural fBm texture |
| `average_gauss_nxn_p` | `0x11F3500` | separable Gaussian |
| `average_gauss_vh_p` | `0x11F3A00` | `Sampler0`, `s_Texture`, `scale`, `iterate` |
| `average_pixels_p` | `0x11F3F00` | downsample |
| `bloom_downscale_c` | `0x1244E00` | compute: `src`, `dstTex`, `uv_scale`, `intensity`, `threshold` |
| `bloom_upscale_c` | `0x1245100` | compute |

That `light_p` was sitting unexamined in every scan this repository has run,
one entry after `fw_background_p`, is worth recording: the descriptor table
existed the whole time and was invisible because its pointers are relocations
rather than values.
