<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# FirstWave decoded passes — what makes the ripples and the rays

Recovered 2026-08-05. This extends
[`firstwave-12.40-stage-contracts.md`](firstwave-12.40-stage-contracts.md),
which pins *boundaries and bindings*, with the **mechanism** of the two
visible effects. It records opcode censuses, constant offsets and structural
conclusions — no firmware program bytes or disassembly listings are copied
into the repository.

## Recovered background pipeline — the map

Where each piece lives, what is settled about it, and which note owns it. Every
row is **ISA-recovered** from the 12.40 oracle eboot unless the class column
says otherwise.

| # | Piece | Programs | Mechanism (settled) | Owning note |
|---|---|---|---|---|
| 1 | **Plate** | `fw_background_p` | full-screen gradient pass; already translated and shipping in `FirstWaveBackground.{h,cpp}` | [`firstwave-12.40-stage-contracts.md`](firstwave-12.40-stage-contracts.md) |
| 2 | **Surface + tessellation** | `fw_flow_vl` → `fw_flow_h` → `fw_flow_dv` | 4×4 bicubic control lattice displaced by **one** 3D simplex evaluation, squared (one-sided), under a cubic 10-second entrance envelope `0.4·e³ + 0.16`; hull writes six tessellation factors, **every one literally `12.0`** → uniform 12×12 | this note, §§*ripples*, *control points*, *12x12* |
| 3 | **Particles — simulation** | `particle_c` (5018 instr.) | one particle per invocation, 64/group; **three-octave curl noise over 4D simplex** with analytic derivatives, lacunarity 2, per-particle gain `[0.5,1.0)`; explicit-Euler step; rate-limited velocity and billboard rotation; 68-byte record, 36-byte attractor record | [`particle-system.md`](particle-system.md) |
| 4 | **Particles — draw** | `particle_vv/p`, `large_particle_vv/p` | two looks from one buffer, routed by a 4-bit `transPatternFlag` nibble: additive procedural points (flat top → Hermite shoulder → hard cut at 0.99, 7-entry embedded palette, **alpha 0**) and premultiplied textured discs with a **computed defocus width** | [`particle-draw.md`](particle-draw.md) |
| 5 | **OIT** | `fw_oit_p`, `fw_comp_oit_p` | per-pixel bounded linked list; gather → **insertion sort on depth held entirely in registers** (`v_movreld/movrels`) → blend → head reset. No LDS, no atomics | this note, §*OIT resolve* |
| 6 | **Fresnel glint** | inside `fw_oit_p` | Schlick, exponent **5**, `F0 = 1.0203e-4`; `√F0 = 1/99` exactly ⇒ **IOR = 50/49** — an authored value, not a fit | this note, §*Schlick Fresnel* |
| 7 | **Blur** | `fw_blurh_p`, `fw_blurv_p` (+`fw_blur_vv`) | separable **13-tap Gaussian**, weights sum to 1.0, fitted **σ = 3.8462 texels** at every tap, offsets exactly `±k/3840`; **radially masked** so lanes outside the mask take one unblurred sample | this note, §*13-tap Gaussian* |
| 8 | **Resolve** | `fw_fxaa_p` | antialias; not decoded | — |
| 9 | **Utility ends** | `fw_clear_vv/p`, `fw_basic_vv/p` | oversized full-screen-triangle constant clear; plain textured blit. `fw_basic_*` is **not** the wave mesh | this note, §*four newly-named stages* |

**What drives it all:** `time` at constant-buffer offset `+0x184`, read by
`fw_flow_vl` and `fw_oit_p`; `waveOpacity` at `+0x188`; the colour/light block
at `+0x110`–`+0x150`.

### What the reference video says actually dominates

**Nothing, authoritatively.** The only candidate capture in the local oracle,
`ps5oracle/shell_ui/live_background/default.mp4`, **failed provenance** — it
carries a `clipchamp.com` encoder tag, ships a non-silent AAC track, does not
loop, and neither its filename nor any `.mp4` literal appears anywhere in the
12.40 shell eboot or `rnps.img`. Full evidence:
[`reference-video-grading.md`](reference-video-grading.md) §0, which is
**authoritative for that file and for every number measured from it**.

Stated precisely, so the two classes never blur together:

| Question | Answer | Class |
|---|---|---|
| Is the mesh tessellated 12×12? | **Yes**, six factors, all `12.0` | ISA-recovered — **stands** |
| Is the displacement bicubic + 3D simplex driven by `time`? | **Yes** | ISA-recovered — **stands** |
| Does the shipped shell *look* particle-dominated? | **Not established** | no genuine capture exists |
| Does `default.mp4` contain a wave surface? | **No**, at any detectable amplitude (four independent tests) | reference-measured, **provenance failed** — carries no weight |
| How prominent is the wave on a real console? | **Not recovered.** `waveOpacity` (`+0x188`) is host-written and has never been read | open |

The strongest honest statement about relative weight is an **effort** argument,
not a pixel one. Counted exactly, from the manifest and from
[`particle-draw.md`](particle-draw.md):

| Group | Programs | Instructions |
|---|---:|---:|
| particles (`particle_*`, `large_particle_*`) | 5 | **6,001** (5018 + 211 + 298 + 200 + 274) |
| wave / OIT / blur / FXAA (`fw_flow_*`, `fw_oit_p`, `fw_comp_oit_p`, `fw_blur*`, `fw_fxaa_p`) | 9 | **2,517** (700 + 41 + 325 + 524 + 180 + 122 + 122 + 40 + 463) |
| plate (`fw_background_p`) | 1 | 90 |

That is a 2.4:1 instruction budget in the particles' favour, and
[`bglayer-background-spec.md`](bglayer-background-spec.md) §2 independently
concludes "it was particles from day one" from the asset inventory and 58
versions of managed history. Treat *"the wave is a main visual element"* and
*"the particles are the background"* both as **assumptions**.

## Toolchain: the ISA is decodable without Sony's SDK

The stage-contracts note documents verification through SDK 10
`libSceShaderIsaP.dll`, which is Windows-only. That is not the only route.
**Homebrew LLVM's `amdgcn` backend decodes these programs exactly**, so the
work can be done on any host with LLVM:

```
python3 -c "d=open('slice.bin','rb').read(); print(' '.join(f'0x{b:02x}' for b in d))" > slice.hex
llvm-mc --arch=amdgcn --mcpu=gfx1010 --disassemble < slice.hex
```

`gfx1010` (RDNA) is the correct `--mcpu`. Evidence that it is correct rather
than merely tolerated:

- **Zero illegal instructions** across all ten stages.
- Instruction counts match the manifest exactly for eight of ten stages
  (`fw_background_p` 90, `fw_blur_vv` 40, `fw_blurh_p` 122, `fw_blurv_p` 122,
  `fw_comp_oit_p` 180, `fw_flow_dv` 700, `fw_flow_h` 41, `fw_flow_vl` 325).
  `fw_fxaa_p` and `fw_oit_p` come out 10 and 2 lower by naive line counting;
  that is a counting artifact, not a decode failure, since neither slice
  produces an illegal instruction.
- Every slice's SHA-256 matches the manifest before disassembly.
- Decoded constant offsets land on the documented shared ABI
  (`0x40` world-projection, `0x180` opacity, `0x190` screen dimensions).

Slices are cut from the local 12.40 `NPXS40087/eboot.bin` at the manifest's
`file_offset`/`code_length`. Note the firmware database's 12.40 eboot is a
*different file* (21,695,140 bytes) from the oracle copy the manifest pins
(21,695,212 bytes, `18c9320b…`); the offsets belong to the oracle copy.

## The ripples are a tessellated bicubic patch, not a sine wave

`fw_flow_dv` — the domain stage, 700 instructions — contains **no
transcendentals at all**: zero `v_sin_f32`, `v_cos_f32`, `v_exp_f32` or
`v_log_f32`. Its opcode census is almost entirely fused multiply-add:

| Opcode | Count |
|---|---:|
| `v_mac_f32` | 223 |
| `v_mad_f32` | 179 |
| `v_mul_f32` (+`_sdwa`) | 164 (+28) |
| `v_madmk_f32` / `v_fmaak_f32` | 16 / 8 |
| `buffer_load_dwordx4` | 16 |
| `v_rsq_f32` | 12 |
| `exp` (export) | 7 |

The sixteen `buffer_load_dwordx4` reads use a **32-byte stride at offsets
`0x000`–`0x1E0`** (0, 32, 64, … 480 — the scheduler issues them out of order,
but the set is exact and matches the manifest's "16 four-dword reads cover
offsets `0x0..0x1E0` in `0x20` steps"). Sixteen control points on a regular
grid, consumed by a pure multiply-accumulate chain, is a **4x4 bicubic patch
evaluation** in two parameters.

> **Correction (2026-08-05).** An earlier revision of this section named the
> basis as **Bernstein**. That was inferred from the 16-control-point layout,
> not read from the constants, and the constants contradict it: `fw_flow_dv`
> contains **no 3.0 and no 6.0** anywhere. What it does carry is `+/-1.5` (x8),
> `+/-4.5` (x4), `-2.5` (x4) and `-5.0` (x3), used as `1.5*a - b`,
> `-5.0*a + b` and `4.5*a + b` over control-point values. A cubic Bezier in
> Bernstein form cannot avoid a 3; a cubic B-spline needs 3, 6 and 4. So the
> patch is bicubic, but **the basis is not identified** - it is some cardinal
> or factored form whose matrix has not been solved. Do not implement it as
> Bernstein on the strength of this document. The twelve `v_rsq_f32` are
the tangent/normal normalizations that such a surface needs, and the seven
exports are `prim`, `pos0` and `param0..param4`, matching the manifest.

Each control point occupies 32 bytes but only its leading `float4` is read, so
the patch record carries additional per-point data the domain stage ignores.

**Where the motion comes from.** `fw_flow_dv` never reads `time`. The only
stage in the flow pipeline that does is `fw_flow_vl`, which issues a single
`s_buffer_load_dword` at **`0x184`** and writes two 128-bit LDS records;
`fw_flow_h` then assembles the patch from four LDS pairs. So the wave is
animated by **moving the control points per frame in the vertex stage**, and
the domain stage is a pure, time-independent surface evaluator.

## The control points are displaced by 3D simplex noise

`fw_flow_vl` (325 instructions) is identifiable **exactly**. Its opcode census
has no transcendentals either, but is dominated by lattice arithmetic —
23 `v_floor_f32`, 15 `v_ceil_f32`, 10 `v_cndmask_b32`, 7 `v_cmp_ge_f32`,
7 `v_max_f32`, over an 84/46/42/35 `mul`/`add`/`madmk`/`mac` body, with one
`v_rsq_f32` and the two `ds_write_b128` records.

Its float literals are the published constants of **Ashima Arts / Stefan
Gustavson 3D simplex noise** (`webgl-noise`, MIT licence) — every one of them,
with nothing left over:

| Literal | Value | Role in `snoise(vec3)` |
|---|---:|---|
| `0x43908000` | `289.0` | `mod289` |
| `0x42080000` | `34.0` | `permute(x) = mod289(((x*34.0)+1.0)*x)` |
| `0x3fe57be0` | `1.79284286` | `taylorInvSqrt` term A (`1.79284291400159`) |
| `0xbf5a8e5c` | `-0.853734732` | `taylorInvSqrt` term B (`0.85373472095314`), negated |
| `0x3f19999a` | `0.6` | corner falloff `max(0.6 - dot(x,x), 0)` — the **3D** variant (2D uses `0.5`) |
| `0x3e124925` | `0.142857149` | `n_ = 1/7` |
| `0x3eaaaaab` | `0.333333343` | skew factor `F3 = 1/3` |
| `0x3e2aaaab` | `0.166666672` | unskew factor `G3 = 1/6` |

The `floor`/`ceil`/`cndmask`/`cmp_ge` cluster is the simplex cell and
corner-ordering logic, and `v_max_f32` is the falloff clamp.

### It is exactly one noise evaluation

The literal *multiplicities* settle this. `snoise(vec3)` evaluates four simplex
corners and calls `permute` in three nested stages, each on a 4-vector; the
compiler scalarises those into four instructions per stage. Predicted counts
for a single evaluation, against what the program actually contains:

| Constant | Predicted (1 evaluation) | Observed |
|---|---:|---:|
| `permute` `*34` | 3 stages x 4 corners = 12 | **12** |
| `mod289` (`289.0` / `1/289`) | 12 in permutes + 3 for `i.xyz` = 15 | **15 / 15** |
| `1/49`, `-49`, `-7`, `1/7` | 1 per corner = 4 | **4 each** |
| `0.6` falloff | 1 per corner = 4 | **4** |
| `taylorInvSqrt` A | 1 per corner = 4 | **4** |
| `42.0` output scale | once, at the end | **1** |

Every count matches a single evaluation exactly, and the lone `42.0` is
decisive: a second octave would need a second final scale. **`fw_flow_vl`
performs exactly one 3D simplex noise evaluation per control point** — not an
fBm stack. (`taylorInvSqrt` B and `ns.x` appear once rather than four times;
the compiler materialised them in a scalar register and broadcast.)

After that classification only **six** literals in the whole program are not
canonical simplex constants: `2000.0` (x3), and one use each of `0.4`, `0.2`,
`2/9`, `0.16` and `-0.1`. Reading their use sites assigns all of them.

### The time envelope: the wave settles over ten seconds

The program's opening instructions build a decaying envelope from `time`
(`vcc_hi`, loaded from `+0x184`):

```
e   = clamp(1 - 0.1 * time, 0, 1)     // reaches 0 at t = 10s
e3  = e * e * e                        // cubed
amp = 0.4 * e3 + 0.16
```

So the displacement amplitude starts at `0.4 + 0.16 = 0.56` and decays
**cubically** to a steady state of `0.16` after ten seconds. That is an
entrance settle: the surface is markedly more agitated when HOME appears and
calms into its resting motion. `-0.1` is the envelope rate, `0.4` its
coefficient and `0.16` the steady-state amplitude.

`0.2` is a separate term, added to a noise input coordinate as
`coord = coord + 0.2 * time` — a constant drift of the sampling point through
the noise field, which is what keeps the resting surface moving after the
entrance envelope has decayed to zero. `2/3`, `1/3` and `2/9` appear alongside
it in the coordinate construction (the fused simplex skew).

### The output: squared noise along a normalized direction

The closing sequence is:

```
n = 42.0 * noiseSum      // canonical snoise output scale
n = n * n                // squared -> always non-negative
pos.xyz += n * dir.xyz   // dir is normalized via v_rsq_f32 from a second
                         //   vertex stream fetched with buffer_load_format_xyz
pos.xyz *= 2000.0
ds_write_b128            // control point handed to fw_flow_h
```

Two consequences worth implementing correctly. The noise is **squared**, so
the displacement is one-sided — the surface only ever bulges along `+dir`,
never both ways, which is why the wave reads as swells rather than symmetric
ripples. And the displacement is applied along a **per-vertex normalized
direction** supplied by a second vertex buffer, not along a fixed world axis.
`2000.0` is the final world-space scale applied to all three components before
the control point is written to LDS.

Caveat: registers are reused aggressively across the program's 325
instructions, so the *pairing* of the envelope term with the specific
displacement multiply is not asserted — only that the envelope is computed as
written and that the closing sequence has the shape above.

This settles the ripple mechanism end to end:

> **The PS5 background wave is a tessellated bicubic patch whose control
> lattice is displaced by 3D simplex noise, with `time` (constant `+0x184`)
> as the third noise axis.**

That is why the motion reads as organic and non-repeating rather than as
regular sinusoidal swells — and it is consistent with this shell lineage's
earlier generation, where `wave_bg_p` embedded Ken Perlin's canonical 256-entry
permutation table (see [`bglayer-shaders.md`](bglayer-shaders.md) §2.3).

**Implementation note.** `webgl-noise` is public, MIT-licensed code by Ashima
Arts and Stefan Gustavson. Prosperismo may implement simplex noise from that
public source (with its licence honoured) — that is not a translation of Sony
code. What the firmware contributes here is only the *fact* that this
algorithm, with these standard constants, drives the control lattice.

The model is implemented and tested in
`frontend/ProsperismoLauncher/src/bigPicture/shellWaveField.ts`: 3D simplex
noise, the cubic Bernstein basis, 4x4 patch evaluation, and the control
lattice at a given `time`. Frequency, amplitude and time scale are explicit
options rather than invented constants, because the firmware's scaling
literals are not yet assigned.

Two porting hazards worth recording, both hit during that implementation:

- The permutation chain takes a **distinct offset per axis**
  (`i1.z/i2.z` for z, `i1.y/i2.y` for y, `i1.x/i2.x` for x). Collapsing them
  to a single per-corner scalar selects wrong gradients and widens the output
  range well past `[-1, 1]`.
- The GPU form multiplies by reciprocals (`p * (1/49)`, `j * (1/7)`,
  `x * (1/289)`). Mirroring that in IEEE double **breaks**: `196 * (1/49)`
  evaluates to `3.9999999999999996`, so `floor` returns 3, `j` escapes its
  `0..48` range, and `taylorInvSqrt` — a Taylor series valid only near
  `|p| = 1` — is evaluated far outside its domain and returns a negative
  scale. Use exact division on the CPU. Both faults are silent: the field
  still looks like noise and remains continuous and deterministic, so only a
  range assertion catches them.

This is the single most important correction to a naive implementation: a
per-vertex or per-pixel `sin(x + time)` displacement is the wrong model. The
console evaluates a smooth bicubic surface whose control lattice is animated.

## The rays are per-pixel, in the OIT resolve

The lighting that reads as rays/shafts is **not** in the geometry. It is in
`fw_oit_p` (522–524 instructions), whose census contains exactly the
transcendentals the mesh lacks:

| Opcode | Count |
|---|---:|
| `v_sqrt_f32` | 10 |
| `v_cos_f32` | 6 |
| `v_rsq_f32` | 6 |
| `v_exp_f32` | 4 |
| `v_rcp_f32` | 4 |
| `v_sin_f32` | 3 |
| `v_log_f32` | 3 |

The `v_log_f32`/`v_exp_f32` pairs are `pow()` — a specular term. The
`sin`/`cos` group together with the `time` read is the moving shimmer along
the light. Its constant loads resolve the shared ABI precisely:

| Load | Offset | Covers |
|---|---|---|
| `s_buffer_load_dwordx16` | `0x110` | `BackgroundColour0`, `BackgroundColour1`, **`BackgroundLightColour`** (`0x130`), reflection (`0x140`) |
| `s_buffer_load_dwordx8` | `0x150` | environment and edge colours |
| `s_buffer_load_dwordx4` | `0x170` | `BlurParameters` |
| `s_buffer_load_dword` | `0x184` / `0x188` / `0x18C` | `time`, `waveOpacity`, `oitSliceOffset` |
| `s_buffer_load_dwordx4` | `0x190` | `screenDim` |

So one pixel program consumes the entire colour/light block in a single
16-dword fetch, applies a time-varying specular, and writes into the OIT node
buffer; `fw_comp_oit_p` resolves the node/head buffers, and the separable
`fw_blurh_p`/`fw_blurv_p` pair (identical 122-instruction programs differing
only in axis, 14 `image_sample` each) spreads that highlight into the visible
glow before `fw_fxaa_p`.

## The OIT resolve is a per-pixel list, sorted in registers

`fw_comp_oit_p` (180 instructions) is a textbook bounded per-pixel
linked-list resolve, and its structure is legible from the control flow.

The giveaway is six `v_movreld_b32` and five `v_movrels_b32` — **indexed
register moves**, which exist to index a small fixed-size array held entirely
in registers. Two arrays are used in parallel, based at `v4` and `v5`.

The program runs in three phases, each a backward `s_branch`:

1. **Gather.** Guarded by `v_cmpx_lt_u32` against a fragment count, it walks
   the node buffer (`s[0:3]`, indexed loads) from the per-pixel head
   (`s[4:7]`) and writes each fragment into the register arrays with
   `v_movreld_b32 v4, colour` / `v_movreld_b32 v5, depth`.
2. **Sort.** It reads pairs back with `v_movrels_b32`, compares with
   `v_cmpx_gt_f32 depthA, depthB`, and writes the swapped pair back through
   `v_movreld_b32` — an **insertion sort keyed on depth**, entirely in
   registers, with no LDS and no atomics.
3. **Resolve.** It blends the sorted fragments (19 `v_mul_f32`, 11
   `v_mad_f32`, 4 `v_rcp_f32`), then `buffer_store_dword` back into `s[4:7]`
   to reset the head for the next frame, and exports compressed `mrt0`.

Its constant reads are `0x40` (world-projection, `dwordx8`), `0x130`
(`BackgroundLightColour`), `0x180` (`opacity`), `0x184` (`time`) and `0x18c`
(`oitSliceOffset`, `dwordx4`).

**Not recovered:** the array's capacity (how many fragments per pixel survive
before the list is truncated) and the exact blend equation, including whether
the walk is front-to-back or back-to-front. Both need register-allocation
analysis this pass did not do.

## The mesh is tessellated a uniform 12x12

`fw_flow_h`, the hull stage, is only 41 instructions and decodes completely.
It does two jobs.

**It moves the control points from LDS into the patch buffer.** Lane setup
takes the thread's packed id and splits it: bits 0..7 are the control-point
index, bits 8..12 the patch id. The LDS address is
`controlPoint * 512 + patch * 32`. It then reads **eight dwords** with four
`ds_read2_b32` pairs — exactly the two 128-bit records `fw_flow_vl` wrote at
LDS offsets `0` and `0x10` — and stores them as two `buffer_store_dwordx4`
into the descriptor loaded from root offset `+0x30`. That is the same
control-point buffer `fw_flow_dv` reads its sixteen strided `float4`s from, so
the LDS hand-off is closed end to end: **vl writes -> h copies -> dv
evaluates**.

**It writes the tessellation factors, and they are constant.** Guarded to the
first patch lane (`v_cmpx_gt_u32 1, patchId`), it materialises `12.0f` via
`v_cvt_f32_i32` and stores six floats per patch — one
`buffer_store_dwordx4` plus one `buffer_store_dwordx2` at `+16`, contiguous at
a **24-byte stride** — into the descriptor from root offset `+0x20`. Six
floats at 24 bytes is exactly the quad-domain factor record: four outer edge
factors plus two inner factors.

Every one of those six values is **12.0**. The tessellation is therefore a
**uniform 12x12 subdivision of each 4x4 bicubic patch**, fixed — not adaptive,
not distance-based, not driven by any constant-buffer parameter. Six further
`buffer_store_dword` of the same `12.0` go to `0x8000 - 96 * (index + 1)` in
the control-point descriptor, a second factor region addressed from the end of
the buffer backwards.

That is the mesh wiring in full: a 12x12 tessellated bicubic patch whose
control lattice is simplex-noise displaced.

## The glint is Schlick Fresnel with an exact 50/49 IOR

`fw_oit_p`'s lighting contains three identical `sqrt -> log2 -> exp2`
sequences. That is `pow()`, and the shape of each one identifies it exactly:

```
t = sqrt(slope * x + addend)
t = 1 - t
t = exp2(5 * log2(|t|))        // v_fmac_f32 t, 4.0, t  =>  t = 5t
F = (1 - F0) * t + F0
```

The `1 - x` base raised to the **fifth power**, then blended as
`F0 + (1 - F0) * (...)`, is **Schlick's Fresnel approximation** — the exponent
5 is its signature. The decoded literals confirm it rather than merely fit it:

| Literal | Value | Role |
|---|---|---|
| `0x38d5f91b` | `0.0001020303098` | `F0` |
| `0x3f7ff950` | `0.9998979568` | `1 - F0` |
| `0xbf85471e` | `-1.041232824` | base slope |
| `0xbd28e3c0` | `-0.04123282433` | base addend |

Three independent checks:

- `F0 + (1 - F0) = 0.9999999871` — an exact partition, as Schlick requires.
- `sqrt(F0) = 0.01010101` = exactly **1/99**.
- Therefore `F0 = ((n-1)/(n+1))^2` gives an index of refraction of exactly
  **50/49 = 1.020408**. That is an authored value, not a fitted one.

Note also `slope = -(1 + |addend|)`, so the two base literals are one
parameter, not two.

A Fresnel term is precisely what makes a wave surface glint along grazing
edges rather than lighting uniformly — this is the term the blur above then
spreads into visible rays. The IOR is very low (water is ~1.333, `F0 ~ 0.02`),
so the effect is a restrained rim rather than a mirror.

**Not recovered:** what feeds `x` into the base — the geometric term the
Fresnel is evaluated against — and how the three instances differ (three
colour channels, three lights, or three surface layers). The `sin`/`cos`
cluster later in the program (6 `cos`, 3 `sin`) is a separate time-driven
oscillation that has not been decoded.

## The blur is an exact 13-tap Gaussian, radially masked

`fw_blurh_p` and `fw_blurv_p` are the same 122-instruction program differing
only in axis, and both decode completely.

**The kernel.** Twelve `v_madmk_f32` instructions build tap coordinates as
`uv + width * K`, and the twelve `K` literals are exactly `±k / 3840` for
`k = 1..6` — one texel of the **native 4K width**, which is the same `3840`
already recorded as `kNativeWidth` in `FirstWaveFirmware1240Constants.h`.
With the centre sample that is a **13-tap symmetric kernel**. The weights are:

| Tap | Weight |
|---|---|
| centre | `0.11399816721677780` |
| ±1 | `0.11020942032337189` |
| ±2 | `0.09958209842443466` |
| ±3 | `0.08409796655178070` |
| ±4 | `0.06637910753488541` |
| ±5 | `0.04896875098347664` |
| ±6 | `0.03376356884837151` |

Two independent checks confirm the decode. They **sum to 1.0**
(`0.9999999925`), so the kernel is normalized; and solving
`w_k / w_0 = exp(-k^2 / 2*sigma^2)` gives **sigma = 3.8462 texels at every
single tap**, agreeing to four decimal places. A misread weight would break
both properties.

**The radial mask.** The prologue does not blur uniformly. It computes

```
d     = length(uv - BlurParameters.xy)
t     = saturate(8.0 * max(0, d - BlurParameters.z))
width = BlurParameters.w * (1 - t)
```

and then branches: lanes where `0.384 > width` take a **single unblurred
sample** and skip the 13-tap loop entirely. So the blur is at full width
inside `BlurParameters.z`, decays to nothing over an eighth of a UV unit
beyond it, and is skipped outright further out. That mask is what localises
the glow into rays around a point rather than smearing the whole frame.

This is implemented in `windows/Prosperismo/FirstWaveBlur.{h,cpp}` with
`FirstWaveBlurHostTest.cpp` asserting normalization, symmetry, outward
monotonicity, the fitted sigma, the exact `k/3840` offsets and their linear
scaling with width, the radial mask's plateau/decay/symmetry, and the
threshold behaviour. It builds warning-free and passes on macOS/clang.

## The native port, verified without Windows

The recovered surface model is implemented natively in
`frontend/ProsperismoLauncher/windows/Prosperismo/FirstWaveSurface.{h,cpp}`,
alongside the existing `FirstWaveBackground.{h,cpp}` plate. It carries **no
platform headers**, so the maths is buildable and testable on any host — the
precompiled-header include is guarded with `#ifdef _WIN32`, which MSVC still
honours because it always defines that macro.

`FirstWaveSurfaceHostTest.cpp` is a standalone program (deliberately *not* in
`Prosperismo.vcxproj`) that asserts the recovered properties and prints probe
values:

```
clang++ -std=c++20 -O2 -Wall -Wextra -I windows/Prosperismo \
    windows/Prosperismo/FirstWaveSurfaceHostTest.cpp \
    windows/Prosperismo/FirstWaveSurface.cpp -o /tmp/fwsurface && /tmp/fwsurface
```

It builds warning-free under `-Wall -Wextra` on macOS/clang and passes: the
envelope's value at t=0/5/10/30 and its monotonic cubic decay, noise
determinism and bounds over 4000 samples (including that the field actually
varies — a constant field would mean a bad decode), one-sidedness over 500
displacement samples, drift still animating a settled surface at t=20 vs
t=26, Bernstein partition-of-unity and endpoint interpolation, bicubic
constant/corner/convex-hull behaviour, and the null/bad-count contracts.

**Cross-validation.** The same probes were evaluated through the TypeScript
reference (`src/bigPicture/shellWaveField.ts`) and compared against the C++:
the worst absolute difference across the probe set is **1.33e-06**, which is
the expected `float` vs `double` gap — the native port and the JS reference
are the same function. The C++ uses `float` deliberately, matching the
firmware's precision.

## A local paired render

The recovered model was rendered offline and compared against the donor
shell's own wave scene, both on the same machine.

- **Recovered model**: one simplex evaluation per sample, squared (one-sided),
  scaled by the entrance envelope, shaded with a gradient-derived specular.
  Produces an organic, non-repeating swell field with light glints.
  *(Corrected 2026-08-05: this bullet previously ended "…that characterises
  the console background". That was an aesthetic assertion with nothing behind
  it — no capture of the console background existed then, and none exists now.
  See [`reference-video-grading.md`](reference-video-grading.md) §0.)*
- **Donor `wave-background` scene** (`tools/shell-shot --scene
  wave-background`): a **flat vertical gradient with no wave whatsoever**.

This is a concrete result, not an aesthetic judgement. Sampling the donor's
own output, `theme-one-background` has a per-channel standard deviation of
**0.02** (essentially a constant fill) and `wave-background` **40.6**, which is
the smooth top-to-bottom ramp — neither carries surface detail. The donor
never implemented the FirstWave mesh/OIT/blur passes; that is exactly the
"recovery boundary" its own notes describe, and it is why the donor shell is a
layout reference rather than a background reference.

**What this validates and what it does not.** It validates that the recovered
maths produces the right *class* of image and that the donor cannot serve as
the background oracle. It does **not** validate pixel fidelity against Sony:
no genuine PS5 background capture exists in the local oracle, so there is
nothing to diff against. Colour, contrast, spatial frequency and the
world-space mapping in the render above are presentation choices of the test
harness, not recovered values.

### Update 2026-08-05 — a candidate reference was tested and rejected

`ps5oracle/shell_ui/live_background/default.mp4` (1.08 GB, 1920×1080, 30 fps,
428 s) is catalogued in `ps5oracle-README.md` as *"Sony's animated home
background"* and was graded as a reference. **It is not a firmware asset.** Its
container carries the encoder tag `https://clipchamp.com` and a comment
advertising that editor; it ships a non-silent 192 kbit/s AAC track; it is
H.264 Constrained Baseline with no colour metadata; and it does not loop. The
strings `live_background` and `default.mp4` appear **zero** times in the 12.40
shell eboot and zero times in `rnps.img`, and the eboot contains no `.mp4`
literal at all — the shell does not play a video background. Full evidence in
[`reference-video-grading.md`](reference-video-grading.md) §0.

**The sentence above therefore stands unamended:** no genuine PS5 background
capture exists in the local oracle. The catalogue line in `ps5oracle-README.md`
is the claim that is wrong.

That clip contains **no wave or ripple surface at any detectable amplitude**
(four independent tests, `reference-video-grading.md` §7). This does **not**
contradict anything in this document. The 12×12 tessellation factors and the
bicubic/simplex displacement mechanism are read out of a program the console
executes; the clip is an export the console never loads. **Where the two
disagree, the ISA evidence wins**, and here they do not really disagree — a
file with no connection to the shell is simply silent on the subject.

What the exercise did produce is a set of **measured** presentation values to
replace the harness guesses named above, tabulated in
[`reference-video-grading.md`](reference-video-grading.md) §8. Those are
measurements of that clip, not recovered firmware constants, and they inherit
its unestablished provenance. They are an improvement on invented numbers and
nothing more.

## What this settles, and what it does not

Settled: the ripple is a bicubic patch whose control lattice is displaced by
3D simplex noise driven by `time`; the ray/glow is a per-pixel specular with a
time term, blurred separably. A renderer can be built to that shape today.

Not settled, and worth stating plainly because the 2026-08-05 grading exercise
sharpened it: **how prominent the wave actually is in the shipped ambient
state.** The mechanism is recovered; the amplitude is not. `waveOpacity` at
constant-buffer offset `0x188` is written by native code at runtime and its
value has never been read. A mesh that is mechanically present but driven at
low opacity is fully consistent with everything in this document, and nothing
recovered so far distinguishes that case from a prominent one. Treat
"the wave is a main visual element" as an assumption, not a finding.

## Naming every entry: the descriptor array

The manifest covers ten stages. The eboot actually carries far more, and they
can be named from firmware data rather than inferred from position.

Each shader has a descriptor record in a table around `0x13C0000`–`0x13D0000`
(file offsets):

| Field | Offset in record | Contents |
|---|---|---|
| name pointer | `+0x00` | vaddr of a NUL-terminated ASCII name |
| code pointer | `+0x30` | vaddr of the program's first instruction |

Records are nominally `0x120` bytes apart, but the spacing is **not** uniform —
walking on a fixed stride finds only a fraction of them. Scan the region at
8-byte alignment and accept any record whose `+0x00` resolves to a plausible
identifier and whose `+0x30` lands in the code region.

Address translation is uniform across the four `PT_LOAD` segments:

```
file_offset = vaddr + 0x4000
```

This yields **138 named entries**, and it reproduces **all ten** manifest
stages at exactly their published offsets — the cross-check that makes the
method trustworthy. The result is committed as
[`shader-entry-map-12.40.json`](shader-entry-map-12.40.json).

**Do not filter on the prologue.** Most programs open with
`s_inst_prefetch 0x3`, and an earlier pass used that as the entry marker. It
silently drops entries: `fw_basic_vv` opens with `s_inst_prefetch 0x1` and
`fw_clear_p` opens with an `s_load` and no prefetch at all. Filtering on `0x3`
found 105 of 138 and missed precisely the four FirstWave stages that were the
point of the exercise.

### The complete FirstWave set

All fourteen `fw_*` programs, contiguous and in address order:

| Offset | Name | In manifest |
|---|---|---|
| `0x11F4200` | `fw_clear_p` | no |
| `0x11F4300` | `fw_clear_vv` | no |
| `0x11F4500` | `fw_basic_p` | no |
| `0x11F4600` | `fw_basic_vv` | no |
| `0x11F4800` | `fw_blurh_p` | yes |
| `0x11F4D00` | `fw_blurv_p` | yes |
| `0x11F5200` | `fw_blur_vv` | yes |
| `0x11F5500` | `fw_flow_dv` | yes |
| `0x11F6600` | `fw_flow_h` | yes |
| `0x11F6900` | `fw_flow_vl` | yes |
| `0x11F7200` | `fw_oit_p` | yes |
| `0x11F8100` | `fw_comp_oit_p` | yes |
| `0x11F8700` | `fw_fxaa_p` | yes |
| `0x11F9300` | `fw_background_p` | yes |

The four additions sit immediately *below* the manifest's lowest entry, which
is why a scan anchored on `fw_blurh_p` never reached them.

The particle family is likewise named and absent from the manifest:
`particle_c` at `0x11FA100` (the 29,092-byte compute simulation — 5,018
instructions, no exports, confirming the behavioural classification made
before the table was found), `particle_p` `0x1201500`, `particle_vv`
`0x1201D00`, `large_particle_p` `0x1202400`, `large_particle_vv` `0x1202C00`,
plus the `shutdown_*` and `caesar_playarea_*` sets.

## Contracts for the four newly-named FirstWave stages

These four are small enough to characterise completely. Together they are the
pipeline's utility ends: clear the target, and blit a texture.

**`fw_clear_vv` + `fw_clear_p` — full-screen constant-colour clear.**

`fw_clear_vv` (23 instructions) is an NGG primitive shader. It issues
`MSG_GS_ALLOC_REQ`, exports `prim`, then builds a position from the low two
bits of the vertex index: each bit is converted to float and passed through
`v_mad_f32 x, 4.0, bit, -1.0`, giving coordinates in `{-1, 3}`. That is the
standard **oversized full-screen triangle** — vertices `(-1,-1)`, `(3,-1)`,
`(-1,3)` — with `z = 0`, `w = 1`. It reads no buffers at all.

`fw_clear_p` (6 instructions) reads **one 4-dword constant at buffer offset 0**
through `s[0:3]`, packs it with two `v_cvt_pkrtz_f16_f32` and exports
compressed `mrt0`. No texture, no interpolation. The clear colour is that
constant.

**`fw_basic_vv` + `fw_basic_p` — textured blit.**

`fw_basic_vv` (22 instructions) is also an NGG primitive shader. It loads a
root table with `s_load_dwordx8 s[0:7], s[8:9]`, yielding **two vertex buffer
descriptors** — `s[0:3]` and `s[4:7]`. From the first it fetches
`buffer_load_format_xyzw` (position); from the second
`buffer_load_format_xy` (texture coordinate). It exports `pos0` and `param0`.

`fw_basic_p` (14 instructions) interpolates `attr0.xy`, runs
`image_sample` with `dmask:0xf` on image `s[0:7]` and sampler `s[8:11]`, packs
to f16 and exports compressed `mrt0`. It enters whole-quad mode
(`s_wqm_b64`) so the sample gets correct derivatives, and restores `exec`
afterwards. There is no lighting, no constant buffer, and no blend maths — it
is a plain textured draw.

## Contracts for the particle family

None of these appear in the manifest either. All decode with zero illegal
instructions.

> **The whole particle family is now decoded — simulation in
> [`particle-system.md`](particle-system.md), draw in
> [`particle-draw.md`](particle-draw.md).** Those two notes are authoritative
> for everything particle-related; the table below is a summary and defers to
> them on any disagreement.
>
> `particle_c` drives particles with **three-octave curl noise built on
> Ashima/Gustavson *4D* simplex noise with analytic derivatives** (`F4`/`G4`,
> `1/294`, `1/49`, output scale `49.0` — the 4D siblings of the 3D constants
> tabulated for `fw_flow_vl` above, with the corner falloff radius at `0.5`
> rather than `0.6`), integrates with a single explicit-Euler step, and
> rate-limits both velocity and billboard rotation. The 68-byte
> `ParticleProperty` record and the 36-byte attractor record are pinned there.
> The four draw programs split into an additive procedural point pass and a
> premultiplied textured defocus-disc pass, routed by a 4-bit tag.
>
> **On relative prominence.** `particle_c` alone is 5018 instructions against
> 700 + 325 + 41 for the whole flow pipeline decoded above, and
> [`bglayer-background-spec.md`](bglayer-background-spec.md) §2 independently
> concludes "it was particles from day one" from asset and managed-history
> evidence. That is a statement about **engineering effort**, which is
> ISA/asset-recovered. It is **not** a measurement of on-screen weight: no
> genuine capture of the console background exists in the local oracle, and the
> clip once used for that argument has failed provenance
> ([`reference-video-grading.md`](reference-video-grading.md) §0). Do not read
> either layer's dominance as settled.

| Stage | Instructions | Constant offsets | Exports | Notes |
|---|---:|---|---|---|
| `particle_c` | 5018 | `0x0`, `0x8`, `0x10`, `0x14`, `0x18` | — | the simulation; no exports, no image, and — notably — **no LDS and no atomics** |
| `particle_vv` | **211** | `0x0`, `0x10` | `prim`, `pos0`, `param0..param5` | six parameter records |
| `particle_p` | 298 | `0x0`, `0x10`, `0x14` | `mrt0` | no image sample — procedural |
| `large_particle_vv` | **200** | `0x0`, `0x10` | `prim`, `pos0`, `param0..param4` | five parameter records |
| `large_particle_p` | 274 | `0x0`, `0x10` | `mrt0` | **2 `image_` ops** — the large variant is textured |

**Corrected 2026-08-05.** The two vertex counts were previously given as 209
and 199. `llvm-mc` at `gfx1010` **refuses three eight-byte VOP1-SDWA
`v_mov_b32` negate encodings** (two in `particle_vv`, one in
`large_particle_vv`) and silently resynchronises four bytes later, so a naive
line count is short and every downstream byte offset drifts. With those three
hand-decoded the slice byte totals close exactly and the true counts are **211
and 200**. Details, including the affected clip-space `z` and `param5.x`, are
in [`particle-draw.md`](particle-draw.md) §*A decoder caveat that changes the
answer, not just the count* — which is authoritative for these five programs.
This is the one known case where the "zero illegal instructions" check above is
**not** sufficient on its own: llvm-mc reported no error while dropping
instructions.

Two structural points. `particle_c` uses neither LDS nor atomics, so the
simulation is embarrassingly parallel per particle — each invocation reads and
writes its own record, with no cross-particle reduction. And the small and
large particles differ in kind, not just scale: the small one is fully
procedural in the pixel shader, while the large one samples a texture.

Note these constant offsets are a **different, smaller buffer** than the
shared FirstWave block documented in
[`firstwave-12.40-stage-contracts.md`](firstwave-12.40-stage-contracts.md)
(whose members run to `0x194`). The particle stages bind their own layout;
do not index them against the FirstWave ABI.

Not settled: the exact Bernstein weighting order in `fw_flow_dv`; the precise
register pairing between the time envelope and the displacement multiply in
`fw_flow_vl` (see the caveat above);
and the runtime float values of the colour/light block, including the
`BlurParameters` quad that positions and sizes the glow mask (its *shape* is
recovered; its per-frame values are set by native code). Entry identification
and the contracts for the four new FirstWave stages and the particle family
are no longer open (see the two sections above); what those sections do *not*
provide is the manifest's per-instruction verification — no SHA-pinned slice,
opcode-by-opcode check, or scalar-load table has been added for them, so they
are characterisations, not byte-exact contracts.
