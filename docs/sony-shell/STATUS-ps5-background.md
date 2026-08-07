<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 background: status

The single place to pick this up from. Method and rules:
[`METHODOLOGY-executing-sony-shaders.md`](METHODOLOGY-executing-sony-shaders.md).

**The rule that governs everything here:** a pass is done when the console's own
instruction stream runs and produces the pixels. Porting the maths, fitting
constants to a video, or re-deriving an algorithm all fail the bar, even when
the output looks right.

## Executing

| layer | programs | note |
|---|---|---|
| particle simulation | `particle_c` | 6000-record bank, authored pattern parameters |
| wave control points | `fw_flow_vl` + `fw_flow_h` | merged LS+HS, 16 points/patch, factors all `12.0` |
| wave surface | `fw_flow_dv` | **rasterises** - 645,939 of 921,600 pixels, on an unestablished patch tiling |
| particle draw | `particle_vv` + `particle_p` | eight groups, 1,820 particles |
| light shafts + compositing | `light_p` | takes the particle frame as `texP` |
| plate | `fw_background_p` | runs; see *Unresolved* for where it belongs |

`docs/sony-shell/particle-live-simulation.md`,
`particle-draw-executed.md`, `light-shaft-layer.md`,
`firstwave-plate-executed.md`.

Rendered output is gitignored under `out/`.

## Not executing

**The FirstWave chain** - `fw_flow_vl` -> `fw_flow_h` -> `fw_flow_dv` ->
`fw_oit_p` -> `fw_comp_oit_p` -> `fw_blurh_p`/`fw_blurv_p` -> `fw_fxaa_p`.

The first three stages now **run**. What each produces:

| stage | state |
|---|---|
| `fw_flow_vl` + `fw_flow_h` (merged LS+HS) | executes, any number of patches, 16/16 control points each - position plus a unit normal, 32 bytes per point |
| tessellation factors | six literal `12.0`, read back from the factor buffer |
| `fw_flow_dv` | executes, 700 instructions, distinct geometry per domain coordinate and per patch |
| rasterisation | **nothing lit** - see below |

### What the hardware registers settled

`fw_flow_h`'s own header carries the tessellation setup, so none of it is
inferred:

| register | value | meaning |
|---|---|---|
| `VGT_TF_PARAM` | `0x00040042` | `TESS_QUAD`, `PART_INTEGER`, `OUTPUT_TRIANGLE_CW` |
| `VGT_LS_HS_CONFIG` | `0x0004100F` | 16 input CP, 16 output CP, **15 patches per threadgroup** |
| `VGT_HOS_MAX/MIN_TESS_LEVEL` | `12.0` / `1.0` | matches the hull's own inline constant |
| `SPI_SHADER_PGM_RSRC2_HS` | `0x003C0084` | **2 user SGPRs**, LDS 61,440 bytes |

Two corrections came out of that last row. With two user SGPRs the merged wave's
system registers begin at **s2**, so the order is `s2` offchip offset, `s3`
merged wave info, `s4` factor offset, `s5` scratch - and `s3` being merged wave
info is independently confirmed, because that is the register the prologue
already turned into EXEC.

- **`s4` had been pinned to the workgroup id.** It is the tessellation factor
  byte offset. Group 0 works either way, which is why one patch always looked
  right and every other patch was empty.
- **The ring offset is not in the packed id.** The shader's own address
  arithmetic only carries a within-group index; the per-threadgroup base arrives
  in `s2`. Probing showed the packed id was already correct - `(cp << 8) | patch`,
  varying properly - while every group still wrote patch 0. Supplying `s2` as
  `group * 1024` separated them.

The offchip stride is **1024 bytes per patch**, of which the hull fills 512.

Apple caps threadgroup memory at 32,768 bytes against the console's 61,440, so
the fifteen patches of a console threadgroup are split one per group. With `s2`
carrying the base this is equivalent, and it is the only host-shaped
accommodation in the chain.

### Two patch indices, not one

The hull addresses the offchip ring with the patch index *within its
threadgroup* - the group's base arrives separately in `s2` - while the local
section fetches lattice entries by the patch's position in the whole draw.
Applying the global index to both double-counts it: patch 1 then writes at
1536, inside patch 1's slot but past the 512 bytes the hull fills, so it reads
back as empty.

This was verified rather than reasoned. Patching the hull's SPIR-V to publish
the byte address its lattice fetch resolves to shows workgroup *p* reading
entries `16p..16p+15` only once the two indices are separated; before that,
every workgroup read entries 0-15 and every patch in the ring held identical
control points.

The same probe retired an earlier assumption: sweeping every candidate VGPR for
the local section's fetch index changed nothing, because the index was never the
problem.

### The blocker

Nothing rasterises. The projection at constant-buffer `+0x80` is

```
1.71677        0        0        0
      0  1.71677        0        0
      0        0 -1.02703       -1
      0        0  519.381  900.450
```

so `w = 900.45 - z` and the camera sits at world `(0, 0, 900.45)` - which the
buffer states outright at `+0x100`. The near plane is `z = 505.7`. Control
points run to `z = 1950`, behind the eye, and to `x = 2685` where the frustum
half-width at that depth is 524.

### The boundary ring is a closed periodic B-spline, and it re-scopes the knot tables

The 13-pair ring decodes exactly. Every entry is a **unit** direction -
`r = 1.0000` for all 26 - at an exact multiple of 36 degrees:

```
36, 72, 108, 144, 180, -144, -108, -72, -36, 0,   then 36, 72, 108 again
```

Ten distinct angles around the full circle, then three wrapped repeats, each
angle appearing twice with `z = -1` and `z = 0`. Ten spans with the cubic
degree's three control points duplicated to close the loop is a **closed
periodic cubic B-spline**: the surface is radially symmetric with ten-fold
angular periodicity.

**This corrects an earlier claim in this file.** The knot tables at `0x00FF1740`
and `0x00FF16F0` - 8 spans over 11 control points, 12 spans over 15 - were
written up here as giving the GPU's patch tiling, `8 x 12 = 96`. They do not.
They describe the **host-side** `11 x 15` lattice that `0xc2a30` evaluates; that
routine opens with a knot-span binary search and is handed `11`, `15` and both
tables directly. The angular direction on the GPU has ten spans, not eight or
twelve, so the two cannot be the same grid.

The `96` figure produced a picture because 96 patches of *something* were drawn,
but the tiling it rested on was not established. It is withdrawn.

### The second vertex stream shapes the surface, so the stand-in invalidates the picture

The local section fetches two vertex streams at the **same index** - probing the
resolved address shows byte `index * 16` for both. The second was being fed the
26-entry ring, tiled with `i % 26` to clear the bound.

That tiling is not cosmetic. Replacing the ring with a constant direction and
re-reading the control points changes the **positions**, not just the normals:

| stream | first control points (x, y, z) |
|---|---|
| real ring | `(2452.2, 731.9, 1544.9)  (2937.7, 801.0, -0.0)  (2521.2, 777.0, -1450.0)` |
| constant | `(2523.4, 783.6, 1332.4)  (2913.7, 783.6, -29.6)  (2523.4, 783.6, -1466.6)` |

With the real ring the height varies per control point; with a constant it
collapses to one value per row. The stream is part of the surface's shape.

**So the rendered image is not Sony's geometry.** It is Sony's shaders run over a
second stream that is 26 real entries repeated seven times. The pixel count is
real and the stages executing is real; the shape is not trustworthy, and the
picture should not be read as the PS5's wave.

### Why the ring is not that stream

The ring is 13 angular control points - ten spans closed with the cubic degree's
three - while the lattice is 165 = 3 x 5 x 11. **13 does not divide 165**, so the
two are not samples of one mesh. The ring is construction data for the angular
sweep; the per-vertex second stream is something the host computes, and it is
neither of the two tables the seed block provides.

`0xc2a30` is not the producer either. Its stack frame and its cross-product tail
(`vshufps 0xc9` / `0x49`, with degenerate fallbacks) make it
`evaluatePointAndNormal(u, v, knots, 15, 11, lattice, out)` - a **single** point
per call, and it is called four times at the seed site. That is bounds or corner
probing, not mesh generation.

**Open, and now precisely stated:** the local section needs a second per-vertex
stream the same length as the lattice. Neither seed table is it, and the host
routine that would build it has not been found. Until it is, the wave's shape
cannot be claimed as recovered.

### The surface rasterises, on data that is not Sony's

645,939 pixels of 921,600, from `fw_flow_vl` + `fw_flow_h` + `fw_flow_dv`
executing. Both the patch tiling and the second vertex stream underneath it are
unestablished - see above - so this demonstrates the stages run end to end and
nothing about the shape. The geometry is the console's; the colour is a
placeholder fragment shader, because `fw_oit_p` is not wired yet.

What had been blocking it was **not** the tiling and not the addressing.

The local section fetches the boundary ring with the same vertex index as the
lattice, and the seed block's ring is **26 entries** where the lattice is 165.
Every vertex past index 25 read out of range, returned zero, and turned into NaN
the moment the hull normalised it. The signature was exact: with eight patches,
26 control points were finite and 102 were NaN.

The diagnosis was separated from the data by feeding a lattice whose entries
repeat the first sixteen. The NaN count did not move - 26 finite either way - so
the fault was the index bound, not the values. Widening the ring's record count
removes the NaN entirely and the surface appears.

### fw_oit_p executes

`fw_oit_p` decodes to **524 instructions**, matching
`firstwave-12.40-stage-contracts.md` exactly, evaluates, and compiles to 181 KB
of SPIR-V as the surface's fragment stage with its header's own
`SPI_PS_INPUT_ENA/ADDR = 0x302`.

It produces no pixels yet, and the reason is visible in its bindings: it fetches
a descriptor from **`descriptorBlock + 0x00`** - its OIT node buffer - which
nothing populates, so the fetch resolves to a zero-length buffer. It is a node
*capture* stage; `fw_comp_oit_p` is what resolves the list into colour. Both the
node buffer's shape and the resolve pass are still to come.

Note the stage table's addresses are **file offsets**, not virtual addresses -
the local and hull slices already use them that way. Adding `0x4000` decodes
garbage.

**This leaves one host stream unrecovered.** The ring is tiled with `i % 26` to
get past the bound, which is a stand-in, not Sony's data. Either the host
expands the 13 pairs into a per-lattice-point stream, or its vertex descriptor
maps many vertices onto few records some other way. The 26-entry seed table is
real; how it reaches 165 vertices is not yet known, and the pixels above are
honest about the geometry and not about that stream.

### Tooling added

`tools/patch_domain_probe.py` publishes a domain stage's clip output and its
export gate into a scratch region of the patch ring. Note the module declares a
**fixed three-element** buffer array, so a probe cannot append a fourth binding;
it writes past the data instead.

**`coldboot`'s large particles** — `large_particle_vv`/`_p`. Contract fully
recovered. The sprites are BC7 480×270 with nine mips and `SW_256B_S` tiling,
whose geometry differs from the light textures because the element is a 16-byte
block rather than a texel. **Not needed for a steady-state background**: of the
seven pattern blobs only `coldboot` ever activates a large group.

## Unresolved firmware data

| item | what is known |
|---|---|
| `opacity`, light object `+0x58` | The draw path reads it into `VolatileCB+0x04`. No writer found. A `0.9999` candidate at `0xB7B86` was checked and belongs to a different object. Assumed 1.0. |
| `colorPatternFlag`, `SRTVsPs+0x14` | Named by reflection; selects which half of `particle_p`'s seven-entry palette is used. Host-written, value unread. Assumed 0, which takes the warm path. |
| `ThemeFlow6`/`ThemeFlow7` presets | Not written by the seeder at `0xEA786`. Widening the window fills them with another function's stores — `gamma = 200.0` where every genuine record carries 1/2.2 — so they are left blank. |
| Composition order | Whether the FirstWave chain composites with `light_p` or is an alternative background. `fw_background_p` under `light_p` looks worse in either order, and the wave has its own gating (`ShowWave`, `WaveOpacity`, `MaskWave`, a `NoWave` preset). Not asserted either way. |

## Reference material, ranked

Firmware bytes settle questions. Reflection tables, serialized blobs and the
per-shader register table are Sony's data and rank next. Prior decode notes in
this directory are high value but carry their own uncertainty labels; re-verify
against 12.40 before relying on them. Video captures are **only** for knowing
what success looks like — they never supply a number, and
`reference-video-grading.md` §0 establishes that the one in the oracle is not
even a firmware asset.

## Two failure modes this work kept hitting

**Searching for pointers as values.** The shader descriptor table and every
`R_X86_64_RELATIVE` slot read as zero in the file. Sessions were spent scanning
for addresses that were never stored. Resolve the RELA table instead —
`tools/dump_shader_registers.py` shows the pattern.

**Widening a window until the answer appears.** Both the `ThemeFlow6` presets
and several offset hunts produced plausible wrong numbers when the search range
grew. A blank is honest; a plausible wrong number poisons every note that cites
it. When a value appears only after widening, check it against something
independent before writing it down.

## Tools

| tool | what it does |
|---|---|
| `dump_shader_registers.py` | per-shader hardware registers, via the RELA-resolved descriptor table |
| `dump_wave_colour_presets.py` | `WaveColourPreset` colours, by replaying the seeder |
| `export_particle_frames.py` | authored `ResourcesCs`/`ResourcesVsPs` blocks per frame |
| `export_firstwave_constants.py` | the 412-byte FirstWave constant buffer |
| `probe_clip.sh` + `patch_clip_probe.py` | dump a vertex stage's clip output by patching the SPIR-V |
| `patch_domain_probe.py` | the same for a domain stage, including its export gate |
