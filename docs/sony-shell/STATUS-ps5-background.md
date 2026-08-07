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
| particle draw | `particle_vv` + `particle_p` | eight groups, 1,820 particles |
| light shafts + compositing | `light_p` | takes the particle frame as `texP` |
| plate | `fw_background_p` | runs; see *Unresolved* for where it belongs |

`docs/sony-shell/particle-live-simulation.md`,
`particle-draw-executed.md`, `light-shaft-layer.md`,
`firstwave-plate-executed.md`.

Rendered output is gitignored under `out/`.

## Not executing

**The FirstWave chain** — `fw_flow_vl` → `fw_flow_h` → `fw_flow_dv` →
`fw_oit_p` → `fw_comp_oit_p` → `fw_blurh_p`/`fw_blurv_p` → `fw_fxaa_p`.

Everything it needs from the firmware is recovered: the merged local+hull
program decodes (366 instructions), the tessellation clamps are 12.0/1.0 from
two independent sources, the 11×15 control lattice and 13-pair boundary ring
have exact IEEE-754 words, and every stage's hardware registers are readable.

What it needs is **host capability**, and this is a build rather than a hunt:

1. Tessellation control and evaluation stages in `Gen5SpirvTranslator`, which
   emits Vertex, Pixel and Compute only.
2. Local/hull VGPR seeding — the local section reads `v2` as the vertex index
   and `v3` as the LDS slot (`cpIndex·16 + patchId`), which is a different
   contract from the compute path's local invocation id.
3. The fixed-function tessellator, quad domain, uniform 12×12.
4. OIT: `buffer_atomic_add` and a per-pixel linked list, then the resolve.
5. Multi-pass render targets for the blur and FXAA passes.

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
