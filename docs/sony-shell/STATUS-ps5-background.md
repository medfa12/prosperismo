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
| wave surface | `fw_flow_dv` | evaluates and draws; produces no pixels yet |
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

**How a patch maps to lattice entries is still unrecovered**, and it is host
draw state rather than shader code. Two readings have been tested against the
executing stages:

| reading | result |
|---|---|
| patch `p` takes entries `16p..16p+15` | patches 0 and 1 evaluate; patch 2 on is NaN, and the NaN follows the lattice entries rather than the grouping |
| a sliding 4x4 window over an 11-wide lattice | all 96 patches produce control points, but the domain then emits half zeroes and half NaN - no usable geometry |

Neither is asserted. The consecutive reading degenerates almost immediately and
the sliding window produces nothing the domain can evaluate, so the real mapping
is something else - most likely the index buffer the host builds from the seed
table, which the seed-block note already warns is *not* itself one patch.

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
