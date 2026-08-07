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

### The second vertex stream shapes the surface

The local section fetches two vertex streams at the **same index** - probing the
resolved address shows byte `index * 16` for both. That stream is not cosmetic:
replacing it with a constant direction moves the control point **positions**, not
just the normals. With real data the height varies per control point; with a
constant it collapses to one value per row.

So whatever feeds it is part of the surface's shape, and getting it wrong
invalidates the geometry rather than just the shading.

### The ring is not that stream; the lattice is

Binding the 13-pair ring there forces a stand-in, because 26 entries bound a
wider draw and the surface fills with NaN past vertex 25. That stand-in is gone.

The ring cannot be a per-vertex attribute of this mesh on its own terms: it is 13
angular control points - ten spans closed with the cubic degree's three - while
the lattice is `165 = 3 x 5 x 11`. **13 does not divide 165.** The two are not
samples of one grid; the ring is construction data for a ten-fold periodic
angular sweep.

Reading the second stream from the **lattice's own 165 entries** gives a complete
mesh with **no NaN at all** - 128 of 128 control points finite over eight patches
- using only seed-block bytes at their natural length, nothing repeated and
nothing invented. That is now the default.

**What is verified and what is chosen.** Verified: both streams are fetched at
one index; the stream affects position; the lattice length removes every NaN.
Chosen: that the host points both streams at one buffer. A fetch shader reading
one buffer twice is unusual, and the alternative - one interleaved buffer of
32-byte vertices, position and direction - is not ruled out. The patch tiling
remains unestablished separately.

`0xc2a30` is not the mesh producer. Its stack frame and cross-product tail
(`vshufps 0xc9` / `0x49`, with degenerate fallbacks) make it
`evaluatePointAndNormal(u, v, knots, 15, 11, lattice, out)` - a **single** point
per call, called four times at the seed site. That is bounds or corner probing.

### fw_oit_p executes, and its user data is recovered from its own registers

`fw_oit_p` decodes to **524 instructions**, matching
`firstwave-12.40-stage-contracts.md`, and compiles to 181 KB of SPIR-V.

Its twelve user SGPRs were read off the program's own memory operations rather
than assumed:

| operation | through | meaning |
|---|---|---|
| `buffer_atomic_add` at `0x928` | `s[4:7]` | the node allocation counter |
| `buffer_store_dword` at `0xB2C`, `0xB34`, `0xC70`, `0xC78`; `buffer_load_dword` at `0xB64..0xB7C` | `s[0:3]` | the per-pixel node list |
| every `s_buffer_load` | `s8` | the FirstWave constant buffer |

The constants had been bound at `s0`, which is why the node descriptor resolved
to a zero-length buffer. With the layout corrected all three resolve.

**The two OIT buffers are scratch, and allocating them is not fabrication.**
Every byte in them is written by this shader and read back by `fw_comp_oit_p`;
only the extent is a host decision, in the same category as the render target's
size. Nothing about their contents is invented.

### Why it still produces no pixels

It discards every fragment, and the discard is exact. At `0x...` the program
compares against the literal `0x3B03126F` - **0.002** - and keeps the fragment
only when the computed alpha exceeds it. Instrumenting the alpha chain shows:

```
v5 = 1.0        v5 *= v18 (1.0)        v5 *= v11 (~4.5e-06)        v5 += v16*v11 (0)
```

so the alpha arrives around **1e-6**, three orders below the threshold, and
`v11` - an interpolant from the domain - is what collapses it. The interpolants
either side are live (`v6 = 0.841`, `v7 = 0.135`), so the domain is feeding real
data; one channel is near zero.

Advancing `time` at `+0x184` from 0 to 30 does not lift it, so this is not the
ten-second entrance envelope. The likely cause is upstream: the domain's exports
depend on the vertex stream arrangement, which is still a host choice rather
than a recovered one.

### What the domain actually exports

Instrumenting the five parameter outputs *after* they are written (they follow
the position store, so probing before it reads all zeros) gives real data:

| location | instance 0 | reading |
|---|---|---|
| 0 | `(0.863, -0.276, -0.422, 1)` | unit normal - length 1 |
| 1 | `(2681.4, -129.7, -2448.6, 1)` | world position |
| 2 | `(1, 0, 0, 1)` | `(v13, 0, 0, 1)` - the shader builds those zeros itself |
| 3 | `(0.862, -0.284, -0.420, 1)` | neighbouring normal |
| 4 | `(0.861, -0.274, -0.429, 1)` | neighbouring normal |

So four of the five carry proper geometry. Location 2 is **not** a collapsed
channel: the domain constructs it as `(v13, 0, 0, 1)` with literal zeros, by
design.

`fw_oit_p`'s `v11` - the factor that drives its alpha to 1e-6 - is fed by
**Location 2, component 0**, which the domain sets to `v13`. Instance 0 exports
`1.0` there; instance 5 exports NaN.

### The fragment interface is correct; the collapse is inside fw_oit_p

Reading `fw_oit_p`'s inputs at entry shows it receives exactly what the domain
exports:

```
Location 0   0.2136  -0.3815   0.8980   1     unit normal
Location 1   -682.9  -249.1   -1380.5   1     world position
Location 2      1       0        0      1     (v13, 0, 0, 1)
Location 3   0.2063  -0.3742   0.9028   1     neighbouring normal
Location 4   0.2258  -0.3815   0.8950   1     neighbouring normal
```

So the vertex-to-fragment interface is **not** permuted, and `v11` starts life as
`Location 2.x = 1.0`. The shader's own arithmetic then drives it to `4.5e-06`
before the `0.002` test. The discard is internal to `fw_oit_p` operating on
inputs that are correct.

`SPI_PS_INPUT_CNTL_n` is absent from the shader's header - the driver builds that
mapping from reflection at draw time - so a permuted interface was a live
possibility, and this rules it out.

Four things were tested against the discard and none revive it:

| tried | result |
|---|---|
| `time` at `+0x184`, 0 to 30 | no change - not the entrance envelope |
| `BlurParameters` at `+0x170`, all zero in the captured frame, forced to 1.0 | no change |
| wave lane count 32 and 64 | no change |
| ring stride 512 matched to the domain's step | no change |

### The discard decoded: a radial edge fade

Bisecting `v11` across its five writes before the alpha test gives
`1 -> 1 -> 1 -> 0.05 -> 9.3e-06`, and the two writes that collapse it decode
exactly:

```
v11 = v11 * v11 - 0.95                     (literal 0xBF733333-ish, measured -0.95)
v11 = clamp(-20 * v11 + 1, 0, 1)           (literals 0xC1A00000 = -20, 0x3F800000 = 1)
```

So the surface's alpha is

```
alpha = clamp(1 - 20 * (r*r - 0.95), 0, 1)
```

which is **zero exactly when `r` reaches 1.0** and full for `r` below about
0.9999. `r` is `Location 2.x`, which the domain builds as `v13`, and `v13`'s
final write is `v13 += v15 * v5` - linear in a coordinate.

This is a **radial edge fade**: the wave is a disc that fades out at its rim.
That fits the boundary ring being a ten-fold periodic circle of unit directions:
the angular direction closes on itself and the radial direction fades.

Probing `v13` per vertex confirms it varies - vertices 0, 1, 7 and 100 give
`0`, vertices 431 and 863 give `1.0` - so the geometry does carry a radial
coordinate, and fragments near the centre should survive with `alpha = 1`.

They do not: the OIT counter stays at zero for every fragment at every patch
count and resolution tried. So the radius reaching the fragment stage is pinned
at the rim even though the domain computes both extremes. That is the specific
question to answer next, and it is much narrower than "the shading is wrong".

### The domain's NaN is a stride mismatch, not bad geometry

The hull's ring is **fully finite at 96 patches** - 1,536 of 1,536 control points,
no NaN at any patch count tested. The NaN appears only in the domain, which steps
its ring slot by **512** bytes while the hull's patches are spaced **1024**
apart, so odd instances read the half the hull never fills.

Matching the two strides does not make `fw_oit_p` capture, so the mismatch is
real but is not the whole story. The counter stays at zero either way.

This was found by bisecting EXEC - instrumenting all 27 writes to it showed the
fragment alive through three and dead at the fourth.

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
