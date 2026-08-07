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

## The blocking unknown: where `texVolume` and `texFloor` come from

They are **not shipped assets.** The complete set of asset paths in the 12.40
eboot contains exactly two BGLayer textures —
`Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and `…Particle1.gnf` — and both are the
large-particle sprites. There is no `texVolume` or `texFloor` file, in the
eboot's string table or anywhere in the 12.40 or 3.20 filesystem dumps.

So they are produced at runtime. **Which pass produces them is not recovered**,
and this note does not guess. Recording the candidates and what would settle
each:

- The FirstWave chain (`fw_flow_vl`/`_h`/`_dv` → `fw_oit_p` → `fw_comp_oit_p` →
  `fw_blurh_p`/`fw_blurv_p` → `fw_fxaa_p`) renders *something* into *some*
  target, and [`firstwave-decoded-passes.md`](firstwave-decoded-passes.md)
  describes its output as a Fresnel glint that the radially-masked blur "spreads
  into visible rays". A blurred glint is a plausible `texVolume`. **Not proven:**
  no code path has been traced from the FirstWave output to `light_p`'s texture
  binding.
- `fbm_tex_p` (`0x11EFA00`) is a procedural fBm texture generator taking
  `origin`, `progress`, `ratio`. Plausible for `noise`, which `light_p` names as
  a `ColorCb` scalar rather than a texture — so probably **not** the source of
  either texture.

Settling this needs the code that binds `light_p`'s texture descriptors —
the same search that is still open for `colorPatternFlag`'s value and the
`WaveColourPreset` colour mapping.

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
