<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background plate, rendered

`fw_background_p` executed from the 12.40 eboot, fed the FirstWave constant
buffer that the firmware's own builder at VA `0x000c5d00` produces.

```
python3 tools/export_firstwave_constants.py --out cb --palette 5 --frames 1
PIX_ENABLE=302 PIX_ADDR=302 FULLSCREEN_VS=fullscreen.vert.spv \
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin \
  --render-plate --constants cb/00000.bin --out-png plate.png
```

## It is not a flat fill

An earlier note in this repository called `fw_background_p` "a flat fill" and
showed a solid blue image as evidence. That was wrong, and it was wrong because
of two host mistakes, not because of the shader. Its 90 instructions build:

- a **view ray** from `worldProjectionMatrix`, taking `v_rcp_f32` of `m00`
  (`+0x40`) and `m11` (`+0x54`) to recover the FOV scale;
- a **radial term** — `v_rsq_f32` of a squared distance — lerping
  `BackgroundColour0` to `BackgroundColour1`;
- a **Gaussian glow**, `v_exp_f32` of `-14.4·r²`, tinted by
  `BackgroundLightColour` and steered by a `v_sin_f32`/`v_cos_f32` pair driven
  by `time · 0.0477465`, so the light source drifts;
- a **hash dither** (`v_mul_lo_u32` by `0x43EB` and `0x3D73`, then the
  `0x5208DD0D` fold) that breaks up the gradient's banding.

## The two host mistakes

1. **`screenDim` at `+0x190` is a pair of unsigned integers**, not floats. The
   shader reads it with `v_cvt_f32_u32`. Writing `1920.0f` there gives the
   shader `1.14e9`, and every reciprocal derived from it collapses.
2. **`worldProjectionMatrix` at `+0x40` must be present.** Left zero, `rcp(0)`
   is an infinity and the whole plate degenerates to one colour.

A third is host state rather than firmware data: the plate reads `FragCoord`
from `v2`/`v3`, so `SPI_PS_INPUT_ENA`/`ADDR` need `PERSP_CENTER` plus
`POS_X_FLOAT` and `POS_Y_FLOAT` — `0x302`. With `PERSP_CENTER` consuming `v0`
and `v1`, the position lands exactly where the shader looks for it.

With all three right the plate resolves 289 distinct colours at 1920×1080
instead of one.

## The palettes are Sony's

`docs/sony-shell/evidence/firstwave-host-constants-12.40.json` records six
palette records, each six vec4s, captured from the constructor at
`0x000c41e0`. Reset selects record **4**. Rendering each one:

| record | `BackgroundColour0` | `BackgroundColour1` | plate |
|---|---|---|---|
| 0–3 | `(-0.078, -0.078, 0.0)` | `(0.298, 0.620, 1.000)` | bright blue |
| 4 | `(-0.078, -0.078, -0.039)` | `(0.318, 0.627, 0.961)` | bright blue, the reset default |
| **5** | `(0.0, 0.0, 0.039)` | `(0.098, 0.235, 0.408)` | **dark navy** |

Record 5 is the dark room the login sequence sits in; record 4 is the home
screen's blue. The negative components in `BackgroundColour0` are in the file
and are kept — they are a signed-byte domain divided by 255, and they are what
makes the gradient reach past black at the bottom.

## What this does not include

The **light shafts** are not here. Per
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md) §*The rays are
per-pixel, in the OIT resolve*, they are a Schlick Fresnel term (`F0 = 1.0203e-4`,
IOR exactly 50/49) evaluated in `fw_oit_p` on the tessellated wave surface, then
spread by the radially-masked 13-tap Gaussian in `fw_blurh_p`/`fw_blurv_p`.

That needs the surface, and the surface needs the tessellation pipeline:
`fw_flow_vl` (local) tail-calls `fw_flow_h` (hull) — they are one merged
program in hardware — and `fw_flow_dv` is the domain stage.
`Gen5SpirvTranslator` currently emits Vertex, Pixel and Compute only, so the
two tessellation stages have to be added before Sony's surface can run. That is
work in our translator, not a gap in the recovered firmware data: the control
lattice (11×15), the boundary ring and the uniform 12×12 tessellation factors
are all already recovered.
