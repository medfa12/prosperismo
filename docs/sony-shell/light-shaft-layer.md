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

**Its contents are not in the file.** Reading 22 records at that address yields
values with absurd exponents — it is runtime-initialised memory, seeded by code,
the same arrangement as `FirstWave::Initialize`'s six palette records. Those
bytes are **not** colours and are not reproduced here.

The seeder has not been found. A sweep for writers into the table's range
returns a large number of hits across an adjacent general data region, and
narrowing that was not completed. This is the one remaining data gap in the
light layer, and it is what a specific screen's wave colour depends on.

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
