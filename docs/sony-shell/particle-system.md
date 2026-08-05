<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background particle simulation — `particle_c` decoded

Recovered 2026-08-05 from the 12.40 oracle image. This is the mechanism note
for the background's particle layer.

> **The particle layer is a first-class element, not a garnish — but its
> on-screen prominence is not measured.** Two independent lines of *firmware*
> evidence support the first half:
>
> - **What the firmware spends.** `particle_c` is **5018 instructions**, the
>   largest program in the eboot; the entire FirstWave flow pipeline
>   ([`firstwave-decoded-passes.md`](firstwave-decoded-passes.md)) is
>   700 + 325 + 41. Five programs are dedicated to particles against nine to
>   the wave/OIT/blur chain.
> - **What the managed layer says.**
>   [`bglayer-background-spec.md`](bglayer-background-spec.md) §2 reaches "it
>   was particles from day one" from the asset inventory and 58 versions of
>   managed history: `ParticleBottom`/`ParticleSpread`/`NoParticle` exist in
>   launch firmware, `simulateParticles` is a live per-frame entry point with
>   20 dispatches, and nothing named for wave motion ever had an
>   implementation.
>
> **What is *not* established is the relative visual weight on screen.** An
> earlier revision of this note asserted that "the genuine reference capture
> shows a field of warm bokeh points plus a volumetric shaft; the wave mesh is
> a secondary, low-contrast layer behind them". **That claim is withdrawn.**
> The capture in question was `ps5oracle/shell_ui/live_background/default.mp4`,
> and it is **not a firmware asset** — see
> [`reference-video-grading.md`](reference-video-grading.md) §0, which is
> authoritative on that file. No genuine capture of the PS5 shell background
> exists in the local oracle, so neither "particles dominate" nor "the wave
> dominates" is a measured statement. Instruction budget is a proxy for
> engineering effort, not for pixels.

Two classes of evidence appear across these notes and they are kept apart:
**ISA-recovered** (disassembly of a program the console executes) and
**reference-measured** (pixel statistics of `default.mp4`, whose provenance
failed). Everything in *this* note is ISA-recovered.

Method and rules are the same as the FirstWave note: opcode censuses, literal
multiplicity, constant offsets. No firmware program bytes or disassembly
listings are copied into the repository.

> **The render half is [`particle-draw.md`](particle-draw.md).** `particle_vv`,
> `particle_p`, `large_particle_vv` and `large_particle_p` are decoded there:
> the billboard expansion, the procedural disc falloff, the defocus edge on the
> large discs, the seven-entry embedded palette, and the two-pass split that the
> `transPatternFlag` nibble routes. Several statements in this note are closed
> or corrected by it, and each is flagged inline below.

## Slice and provenance

| | |
|---|---|
| Image | 12.40 oracle `system_ex/app/NPXS40087/eboot.bin`, 21,695,212 B, `18c9320b…` |
| Entry | `particle_c`, file offset `0x11fa100` (from [`shader-entry-map-12.40.json`](shader-entry-map-12.40.json)) |
| Span to next entry (`particle_p` `0x1201500`) | 29,696 B |
| Slice cut at the first `s_endpgm` | **29,092 B**, SHA-256 `6c77e3476edc128dd00a91e52cf2b5b40f4c1f4fefc0ce7d80cd4a6bdd8e6384` |
| Decode | `llvm-mc --arch=amdgcn --mcpu=gfx1010 --disassemble`, **zero illegal instructions** |
| Instructions | **5018** — the single `s_endpgm` at byte `0x71a0` is the only exit; every early-out branch targets it |

The earlier `bglayer-*` notes quote `particle_c` at `0xd9f1b0` / 29,760 B in the
4.03 image. That is a different firmware and a different descriptor-declared
size (which rounds up to the next 256-byte entry boundary); the 12.40 program
body is 29,092 B of real code. Nothing below contradicts the 4.03 findings —
where the two overlap they agree byte-for-byte, and that is called out.

Confirmed absent, by census: no `exp`, no `image_*`, no `ds_*` (no LDS), no
atomics, no `global_`/`flat_`/`scratch_`. All memory traffic is `buffer_*` and
`s_load`/`s_buffer_load`.

## Opcode census

| Opcode | Count | | Opcode | Count |
|---|---:|---|---|---:|
| `v_mul_f32` (+`_sdwa`) | 1248 (+9) | | `v_ceil_f32` | **216** |
| `v_add_f32` (+`_sdwa`) | 690 (+117) | | `v_floor_f32` | **171** |
| `v_mac_f32` (+`_e64`) | 564 (+5) | | `v_cndmask_b32_sdwa` | 167 |
| `v_madmk_f32` | 441 | | `v_fract_f32` | **135** |
| `v_mad_f32` | 298 | | `v_max_f32` | **45** |
| `v_sub_f32` (+`_sdwa`) | 276 (+6) | | `v_rsq_f32` | 13 |
| `v_madak_f32` | 47 | | `v_exp_f32` / `v_log_f32` | 5 / 3 (+2 sdwa) |
| `v_min_f32` | 36 | | `v_ldexp_f32` | 15 |
| `v_mul_lo_u32` / `v_mul_hi_u32` | 37 / 11 | | `v_mad_u64_u32` | 12 |
| `buffer_load_*` | 13 | | `buffer_store_*` | 17 |

The `ceil`/`floor`/`fract` cluster is the tell. It is not integration
arithmetic — it is **simplex-noise lattice arithmetic**, and its counts are
exact multiples of nine (216 = 9×24, 171 = 9×19, 135 = 9×15, 45 = 9×5).

## It is one particle per invocation, 64 per workgroup

The first two instructions are `v_lshl_add_u32 v1, s4, 6, v0` — linear
invocation `i = (workgroup_id << 6) + lane`, so **64 threads per group** — and
then the record index

```
idx = Resources[0x30] + Resources[0x34] * i
```

guarded by two unsigned compares that kill the wave unless `i < Resources[0x28]`
**and** `idx < Resources[0x2c]`. `Resources[0x30]`/`[0x34]` are a base and a
stride into a shared particle array, which is exactly the shape needed for
`simulateParticles`' documented "20 dispatches per frame (2 instances × 10
systems)" — each system is a base/stride window on one buffer. There is no LDS
and there are no atomics, so the simulation is embarrassingly parallel: one
invocation owns one record for the whole program.

## The particle record

Every `buffer_*` on the particle descriptor uses `idxen` with the index above.
The touched byte offsets, gathered from all 30 accesses:

| Offset | Size | Accesses in `particle_c` | Matches `ParticleProperty` |
|---|---|---|---|
| `0x00` | float3 | `dwordx3` load (alive path) and store (writeback); `dwordx3` store of `(1e5,1e5,1e5)` on kill | `pos` |
| `0x0c` | float | single `dword` store at spawn | `blurBoundary` |
| `0x10` | float3 | `dwordx4` store at spawn (with `0x1c`), `dwordx4` store at writeback, `dwordx3` load | `vel` |
| `0x1c` | float3 (`0x1c,0x20,0x24`) | `dwordx4`@`0x10` + `dwordx4`@`0x20` at spawn; `dwordx4`@`0x10` + `dwordx2`@`0x20` at writeback; `dwordx3` load @`0x1c` | `fore` |
| `0x28` | u32 | written as part of `dwordx4`@`0x20` at spawn; read as `dword` at entry | `transPatternFlag` |
| `0x2c` | float3 | `dwordx4`@`0x20` + `dwordx4`@`0x30` at spawn; `dwordx3` load and store at writeback | `right` |
| `0x38` | float | five `dword` loads and six `dword` stores; `dwordx2` store on kill | `curLife` |
| `0x3c` | float | `dwordx4`@`0x30` at spawn; zeroed with `0x38` on kill | `maxLife` |
| `0x40` | float | one `dword` store of `-1.0` at spawn; never read here | `renLife` |

Highest byte touched is `0x43`. **Stride is 0x44 = 68 bytes** — not readable
from the code (it lives in the V# descriptor), but 68 is the tight lower bound
and it matches the `ParticleProperty` layout already recovered in
[`bglayer-background-spec.md`](bglayer-background-spec.md) §1b field for field.
That note flagged the layout as "provisional, re-derive before shipping". **It
is now re-derived, independently, from the 12.40 image, and it is correct.**

`particle_vv` corroborates from the read side: it loads `0x00` (`dwordx3`),
`0x1c` (`dwordx3`), `0x2c` (`dwordx3`), `0x28`, `0x38` and `0x40` from the same
descriptor. `particle_p` loads `0x0c`, `0x28`, `0x38` and `0x40`.
`large_particle_vv` loads `0x00` and `0x28`; `large_particle_p` loads `0x28`
and `0x38`/`0x3c` together — the large discs fade on `curLife`/`maxLife`, the
small points on `curLife`/`renLife`. See
[`particle-draw.md`](particle-draw.md).

`blurBoundary@0x0c` gets its meaning on the read side: `particle_p` uses it as
the radius of the disc's **flat, unblurred top**, before the Hermite shoulder
that runs out to the 0.99 kill radius. The recovered name is exactly right.

### `fore` and `right` are an orthonormal billboard frame

At spawn the shader draws six `[-1,1)` uniforms from the RNG (below), builds
`U = normalize(r0,r1,r2)`, then `V = normalize(cross(U, (r3,r4,r5)))`. `U` goes
to `0x1c`, `V` to `0x2c`. Every frame both are rotated (see *Orientation*) and
`V` is re-derived as a normalized cross product against the updated `U`, so the
pair stays orthonormal for the lifetime of the particle. `particle_vv` reads
`pos`, `fore` and `right` and emits **6 vertices per particle** (`v5 - 6*i`
selects a corner, and an 8-byte-stride, 48-byte inline table supplies the six
`float2` corners) — the frame is the billboard basis for a quad.

## The motion field: 3-octave curl noise over 4D simplex noise

Every float literal in the program, with its role. Nothing is left over except
the six spawn/offset constants listed afterwards.

| Literal | Value | Count | Role |
|---|---:|---:|---|
| `0x43908000` | `289.0` | 216 | `mod289` |
| `0xbb62c4a7` | `-0.00346021` | 216 | `-1/289`, the `mod289` divide |
| `0x42080000` | `34.0` | 180 | `permute(x) = mod289(((x*34.0)+1.0)*x)` |
| `0x3e124925` | `0.14285715` | 180 | `1/7` — `grad4` `ip.z`, used once as `ip` and three times as the `*ip.z` scale |
| `0x40e00000` | `7.0` | 135 | `grad4` `floor(fract(j*ip)*7.0)` |
| `0x3fc00000` | `1.5` | 90 | `grad4` `p.w = 1.5 - dot(abs(p.xyz), 1)` |
| `0x3e0d8369` | `0.13819660` | 72 | **`G4 = (5-√5)/20`** |
| `0x42440000` | `49.0` | 17 | canonical `snoise(vec4)` output scale |
| `0x3e8d8369` | `0.27639320` | 36 | `2·G4` |
| `0x3ed4451d` | `0.41458979` | 36 | `3·G4` |
| `0xbee4f92e` | `-0.44721359` | 36 | `-1 + 4·G4` |
| `0x3e9e377a` | `0.30901700` | 12 | **`F4 = (√5-1)/4`** |
| `0x3b5ee95c` | `0.00340136` | 45 | `1/294` — `grad4` `ip.x` |
| `0x3ca72f05` | `0.02040816` | 45 | `1/49` — `grad4` `ip.y` |
| `0x3fe57be0` | `1.79284286` | 45 | `taylorInvSqrt` term A |
| `0xbf5a8e5c` | `-0.85373473` | 3 | `taylorInvSqrt` term B, negated (materialised once per site in an SGPR) |
| `0xc0c00000` | `-6.0` | 45 | **analytic-derivative term** `d(m³)/dx = 3m²·(-2x)` |
| `0.5` (inline) | — | 45 | corner falloff `max(0.5 - dot(x,x), 0)` |

These are the published constants of **Ashima Arts / Stefan Gustavson 4D
simplex noise** (`webgl-noise`, MIT), the same family already identified in
`fw_flow_vl` — but the **4D** variant (`F4`/`G4`, `1/294`, `1/49`, output scale
`49.0`), not the 3D one (`F3 = 1/3`, `G3 = 1/6`, scale `42.0`).

**One deviation, recorded because it matters for a port:** the corner falloff
radius here is **`0.5`**, not the `0.6` in Ashima's published `noise4D.glsl`.
It appears as an inline `v_sub_f32 v, 0.5, dot` immediately before each of the
45 `v_max_f32 v, 0, v` — 45 = 9 × 5 corners, exactly. `0.6` (`0x3f19999a`)
does not occur anywhere in the program. Everything else about the evaluation is
canonical, including the `49.0`.

### There are exactly nine static evaluations, and the counts prove it

`snoise(vec4)` evaluates five simplex corners, calls `mod289` on `i` plus two
`permute` chains (a scalar 4-stage chain and a vector 4-stage chain), and calls
`grad4` once per corner. Predicted per-evaluation counts against observed
totals divided by nine:

| Constant | Predicted per evaluation | Observed / 9 |
|---|---:|---:|
| `mod289` lanes (`289.0`, `-1/289`) | 4 (`i`) + 4 (`j0`) + 16 (`j1`) = 24 | **24 / 24** |
| `permute` `*34.0` | 4 + 16 = 20 | **20** |
| `floor` | 4 (`i`) + 3×5 (`grad4`) = 19 | **19** |
| `fract` | 3 × 5 corners = 15 | **15** |
| `1/7` | 4 × 5 = 20 | **20** |
| `7.0` | 3 × 5 = 15 | **15** |
| `1/294`, `1/49` | 1 × 5 = 5 | **5 each** |
| `taylorInvSqrt` A | 4 (vec4 `norm`) + 1 (scalar for `p4`) = 5 | **5** |
| falloff `max(…,0)` | 3 + 2 = 5 | **5** |
| `-6.0` derivative | 1 per corner = 5 | **5** |
| `G4` | 4 (`x1`) + 4 (`dot(i0,C.xxxx)` scalarised) = 8 | **8** |
| `2·G4`, `3·G4`, `-1+4·G4` | 4 each | **4 each** |

Every count lands. **Nine 4D simplex evaluations exist in the program.** The
one predicted count that does not land is `1.5`, observed at 10 per evaluation
against a predicted 5 — the `grad4` `p.w` constant is materialised twice per
call by this compiler. That is a codegen artifact, not a second use site; it is
recorded rather than explained away.

### The nine are three sites of three, and each site is a curl

`F4` appears only **12** times, i.e. four per site, not four per evaluation.
`dot(v, vec4(F4))` is factored as `F4·(x+y+z) + F4·w`, and the `F4·w` half is
loop-invariant (the fourth coordinate is time) and hoisted — so a site shows one
hoisted `v_mul` plus three `v_madmk`, one per evaluation. **Three sites, three
evaluations each.**

Each site ends by scaling six accumulators by `49.0` and then subtracting them
in three pairs:

```
out.x = A1 - A0
out.y = A2 - A3
out.z = A4 - A5
```

Six accumulators, three pairwise differences, three noise fields, and a `-6.0`
analytic-derivative term per corner. That is **curl of a vector potential**:
each of the three noise fields is one component of Ψ, each contributes the two
partial derivatives the curl needs (not three), and the three differences are
`∂Ψz/∂y − ∂Ψy/∂z`, etc. All three sites have this identical shape.

The three potential components are decorrelated by constant offsets applied to
the sample point before evaluation. Measured, per site:

| Site | Ψ₀ offset | Ψ₁ offset | Ψ₂ offset |
|---|---|---|---|
| spawn warp | `(0, 0, 0)` | `(123.4, 129845.6, -1239.1)` | `(-9519, 9051, -123)` |
| velocity | same set | same set | same set |
| orientation | `(20, 20, 20)` | `(143.4, 129865.6, -1219.1)` | `(-9499, 9071, -103)` |

The orientation site's offsets are the spawn site's **plus exactly 20.0 in
every component**, and its input point is `20.0 × p`. So the tumble field is the
same curl field, sampled at 20× the spatial frequency with a uniform `+20`
decorrelation shift — not an independently authored field.

### Exactly three octaves, lacunarity 2, per-particle gain

Each site's noise block is a real loop — a backward `s_cbranch_scc1` — closed by

```
s_add_i32  sN, sN, 1
s_cmp_lt_i32 sN, 3
```

at all three sites. **The octave count is 3, hardcoded.** Inside the body:

- frequency is `v_ldexp_f32 v, 1.0, i` — `2^i`, so **lacunarity is exactly 2.0**;
- amplitude is `v_exp_f32(i × v_log_f32(g))` — `g^i`, gain to the power of the
  octave index;
- and `g` is **per particle**: `g = 0.5 + 0.5 · (slot / Resources[0x2c])`,
  where `slot` is the u32 this invocation reads from the auxiliary buffer and
  `Resources[0x2c]` is the particle capacity. So `g ∈ [0.5, 1.0)` — particles
  low in the buffer get a smooth, low-persistence drift and particles high in
  it get an increasingly rough one. This is what gives the field per-particle
  variety without per-particle parameters.

Dynamic cost per live particle per frame: two sites execute (the spawn warp only
fires on respawn), each 3 components × 3 octaves = **18 `snoise(vec4)`
evaluations per particle per frame**, plus 9 more on the frame a particle
respawns.

## Randomness: Park–Miller, reseeded per frame

The spawn path runs the **minimal-standard Lehmer generator**
`seed = 16807 · seed mod (2³¹−1)` — literals `0x41a7` (16807) and the
`0x80000001`/`>>30`/`0x7fffffff` sequence that performs the mod-`2³¹−1` fold —
and derives uniforms as `r = seed mod 1000` via the `0x10624dd3` magic divide
plus `>>6`, giving

```
uniform01  = r * 0.001                 (v_mad_f32 with 0x3a83126f)
uniformPM1 = r * 0.002 - 1.0           (v_mad_f32 with 0x3b03126f)
```

This matches the RNG already noted for 4.03. One nuance recorded in
[`particle-draw.md`](particle-draw.md), which finds the same generator in both
vertex programs: the multiply is `v_mul_lo_u32`, so the product is **truncated
to 32 bits before** the mod-`2³¹−1` fold. A single step from an arbitrary seed
is indistinguishable from Park–Miller, but a port that *chains* the generator
with a 64-bit product will diverge.

New here is the **seed**:

```
seed0 = aux[i] + Resources[0x2c] * (Resources[0x24] + round(time / timeStep))
```

`round(time / timeStep)` is a frame counter, so the spawn stream is re-rolled
every frame rather than being a fixed per-particle stream. `aux[i]` is a u32
read from a second buffer descriptor (`Resources[0x00]`) indexed by the *raw*
invocation index, and the same value normalised by the capacity is the fBm gain
above.

## The per-frame state machine

The program is one compiled state machine over a phase value. Structure, from
the resolved branch targets:

1. **Instance gate.** Read `transPatternFlag` at `0x28`; keep the lane if its
   low nibble equals `SRT[0x18] & 0xf`, or if `SRT[0x18] & 0xf` equals
   `(SRT[0x18] >> 4) & 0xf`. On the spawn path the nibble is rewritten to
   `(SRT[0x18] >> 4) & 0xf`. `SRT[0x18]` therefore packs a *current* and a
   *next* pattern id, and particles migrate from one to the other one respawn at
   a time — the mechanism behind the two-instance crossfade inferred in
   `bglayer-background-spec.md`.

2. **Life countdown**, on `curLife` (`0x38`), with a two-rate schedule split at
   `0.5`:
   - above `0.5`: `curLife -= SRT[0x10] * timeStep`;
   - at or below `0.5`: `curLife -= timeStep` (real seconds);
   - clamped so a step that crosses `0.5` lands on `0.5`;
   - a separate scalar-gated path subtracts `Resources[0x38]` (`particleMinLife`)
     outright;
   - and if bit 10 of `Resources[0x20]` is set, `curLife` is forced to `-1.0`.

   `curLife <= 0` selects the respawn path. The `0.5` boundary reads as a
   pre-spawn queue delay above it and the live lifetime below it, which is
   consistent with `curLife`/`maxLife` being seeded from
   `[particleMinLife, particleMaxLife)` — but that reading is an inference from
   the shape, not something the program states.

3. **Kill.** One scalar-uniform branch writes `pos = (100000, 100000, 100000)`,
   `vel = 0`, `fore.x = 0`, and zeroes `curLife`/`maxLife`, then jumps to the
   update. `1e5` is the park position, appearing exactly three times — one per
   component. (`particle_vv` uses a different sentinel, `1e6`, to fold its
   vertices.)

4. **Respawn** — the whole first noise site plus the RNG:
   ```
   home    = spawnMin + uniform01 * (spawnMax - spawnMin)   // per axis
   pos     = home + curlAmp.xyz * curl3(freq.xyz * home, t) // 3 octaves
   vel     = 0
   fore    = normalize(rand3PM1)
   right   = normalize(cross(fore, rand3PM1))
   curLife = maxLife = uniform(Resources[0x38], Resources[0x3c])
   renLife = -1.0
   blurBoundary = 0.2 + 0.8 * f(idx / capacity)^2
   transPatternFlag = (transPatternFlag & ~0xf) | nextPattern
   ```

5. **Update**, for live and freshly spawned particles alike (position comes from
   the `0x00` load for the former and from the respawn block for the latter —
   register allocation proves they converge on the same value).

### `blurBoundary`: the size curve, and two folded constants

With `u = idx / capacity`, pivot `P = Resources[0x44]` and exponent
`E = Resources[0x40]`:

```
t = (u > P) ? P + (1-P) * pow((u-P)/(1-P), E)
            : P - P * pow(1 - u/P, E)
blurBoundary = 0.2 + 0.8 * t*t
```

Continuous at `u = P` (both branches give `P`), mapping `[0,1] → [0,1]`, then
squared and remapped. **`0.2` and `0.8` are literals in this program**, each
occurring exactly once. If the source form is
`lerp(unblurMinSize, unblurMaxSize, t²)`, then `unblurMinSize = 0.2` and
`unblurMaxSize = 1.0` are constant-folded into this build. That is a partial
answer to open item **M5** in `bglayer-background-spec.md`, which listed both as
unrecovered runtime parameters. Stated conservatively: **the resulting
`blurBoundary` range is exactly `[0.2, 1.0]`.** `Resources[0x40]` driving the
exponent is consistent with the recovered name `blurRadiusPowerFactor`.

Note this makes `blurBoundary` a function of the particle's **index in the
buffer**, not of the RNG. The 4.03 note attributed the size to `r/1000` from the
Park–Miller stream; in the 12.40 program the interpolant is the pow-remapped
normalised index. Where the two disagree, the measured 12.40 dataflow wins for
12.40.

**`blurBoundary` is not the sprite's size.** Calling this "the size curve" is
loose: [`particle-draw.md`](particle-draw.md) shows there are **two independent
size quantities**, and this is only one of them. `blurBoundary` is consumed by
`particle_p` as the *radius of the disc's flat unblurred top* in corner space;
the sprite's actual *quad scale* comes from a separate Park–Miller lottery in
`particle_vv` over `Resources[0x5c..0x60]`, seeded from the same auxiliary u32.
Neither supersedes the other, and a port that folds them into one parameter will
get both the silhouette and the falloff wrong. `particle-draw.md` is
authoritative on how each is used at draw time.

## Integration

The update is three stages, in this order.

**1. Target velocity = attractor sum + curl.**

```
target = Σ_k blend_k(pos) + Resources[0x74] * curl3(freq.xyz * pos, t)
```

**2. Velocity, rate-limited.** With `Δ = target - vel` and
`a = timeStep * Resources[0x60]`:

```
vel = (|Δ|² >= a²) ? vel + normalize(Δ) * a : target
```

`Resources[0x60]` is therefore a **maximum acceleration** — consistent with the
recovered name `particleMaxAcceleration1`. There is no drag term, no gravity
constant, and no bounds test in the whole program: particles do not bounce or
wrap. They are recycled only by the lifetime countdown, and a killed particle is
parked at `1e5` rather than clamped.

**3. Position, explicit Euler.** The last three arithmetic instructions before
the writeback are `v_mac_f32 pos.{x,y,z}, timeStep, vel.{x,y,z}`:

```
pos += vel * timeStep
```

That is it — a single first-order step, no Verlet, no substepping, no
predictor-corrector.

### Orientation

The third noise site produces a tumble vector `w = curl3(20·pos + 20, t)`.
Applied identically to `fore` and then to `right`, with
`s = timeStep * Resources[0x64]`:

```
n = normalize(a + Resources[0x74] * w)
if dot(n, a) < 0: n = -n              // axis is undirected; keep the hemisphere
d = n - normalize(a)
if |d|² > s²: n = normalize(normalize(a) + normalize(d) * s)
```

then `right` is re-derived as `normalize(cross(fore_new, right_new))` so the
frame stays orthonormal. `Resources[0x64]` is a **maximum angular step per
second** — consistent with the recovered name `particleMaxRotationSpeed`. Two
`1e-12` guards (`0x2b8cbccc`) protect the normalizations.

## The attractor set is 36 bytes per record, not 32

The attractor loop is the only data-dependent loop in the program:

```
for (k = 0; k < Resources[0x88]; ++k)   // count is runtime data
```

with `s_mul_i32 offset, k, 36` and a base of `Resources[0x8c]`. Fields, from the
four indexed `s_load`s:

| Record offset | Size | Use | `ParticleBlendParam` |
|---|---|---|---|
| `+0x00` | float3 | centre; `d = centre - pos` | `center` |
| `+0x0c` | float3 | per-axis weight on `d` | `weight` |
| `+0x18` | float | inner radius of the falloff | `beginDist` |
| `+0x1c` | float | outer radius of the falloff | `endDist` |
| `+0x20` | float | scalar strength on the whole term | **not in the 32-byte layout** |

The maths, exactly:

```
len = |d|
if (len >= 1e-6):                        // 0x358637bd
    u = clamp((len - beginDist) / (endDist - beginDist), 0, 1)
    f = u*u*(3 - 2*u)                     // smoothstep; the -2/+3 pair is explicit
    accum += weight * d * f * strength / len
```

**Correction to [`bglayer-background-spec.md`](bglayer-background-spec.md)
§1b**, which records `ParticleBlendParam` as 32 bytes ending at `endDist@0x1c`.
The 12.40 program indexes it with a stride of **36** (`s_mul_i32 …, 36`, twice,
in the same loop) and reads a fifth float at `+0x20`. The earlier entry is not
wrong about the four fields it names — it is short by one. The stride literal is
the decisive evidence: a 32-byte stride would have been an `s_lshl_b32 …, 5`.

Note the falloff is `smoothstep(beginDist, endDist, len)`, i.e. **zero at the
centre and full at the rim**. Whether that reads as attraction or repulsion
depends on the sign of `strength` and `weight`, which are runtime data.

## Constant-buffer layouts

### The compute SRT (`s[0:3]`, the buffer the task brief calls "small")

| Offset | Type | Use |
|---|---|---|
| `0x00` | pointer (2 dwords) | address of the `Resources` block; every `s_load` in the program is relative to it |
| `0x08` | f32 | **`time`** — the fourth noise coordinate, and `round(time/timeStep)` is the RNG frame seed |
| `0x0c` | f32 | **`timeStep`** — `pos += vel*dt`, `dt*maxAccel`, `dt*maxRotSpeed`, `dt*lifeRate` |
| `0x10` | f32 | rate multiplying `timeStep` for the `curLife` countdown above `0.5` |
| `0x14` | u32 | tested `== 0` only, in the life-schedule gate. Purpose **not determined** |
| `0x18` | u32 | packed nibbles: `[3:0]` current pattern id, `[7:4]` next pattern id |

`time@0x08` / `timeStep@0x0c` agree exactly with the `SRTVsPs` layout already
recovered for the vertex and pixel stages. The compute SRT is a distinct
struct — its `0x10` is a float rate, whereas `SRTVsPs[0x10]` is
`transPatternFlag`.

### The `Resources` block (indirect, via the pointer at SRT `0x00`)

Every scalar offset the program touches:

| Offset | Size | Use |
|---|---|---|
| `0x00` | V# (4 dwords) | auxiliary u32-per-slot buffer (RNG seed, fBm gain) |
| `0x10` | V# (4 dwords) | the particle record buffer |
| `0x20` | u32 | flag word. Bits tested: `& 0x40f == 0`, bit 10, `& 0x1000`, `& 0x300 == 0x100`. The last of these skips the velocity **and** orientation noise sites |
| `0x24` | u32 | added to the frame counter in the RNG seed |
| `0x28`,`0x2c`,`0x30`,`0x34` | u32 ×4 | invocation limit, capacity, base index, index stride |
| `0x38`,`0x3c` | f32 ×2 | `curLife`/`maxLife` spawn range — `particleMinLife`, `particleMaxLife` |
| `0x40` | f32 | size-curve exponent — `blurRadiusPowerFactor` |
| `0x44` | f32 | size-curve pivot |
| `0x48`..`0x53` | float3 | spawn box corner A |
| `0x54`..`0x5f` | float3 | spawn box corner B (the `min` end: it is the additive base) |
| `0x60` | f32 | maximum acceleration — `particleMaxAcceleration1` |
| `0x64` | f32 | maximum angular speed — `particleMaxRotationSpeed` |
| `0x68`,`0x6c`,`0x70` | float3 | curl-noise spatial frequency |
| `0x74` | f32 | curl strength (velocity **and** orientation sites) |
| `0x78` | f32 | curl time scale |
| `0x7c`,`0x80`,`0x84` | float3 | per-axis curl amplitude for the spawn-position warp |
| `0x88` | u32 | attractor count |
| `0x8c` + `36k` | 36 B | attractor records |

The four `particleCurl*` names recovered from 4.03 reflection tables map onto
`0x68`–`0x70` (frequency), `0x74` (strength), `0x78` (time scale) and
`0x7c`–`0x84` (spawn amplitude), but the name-to-offset assignment is an
inference from role, not something the 12.40 image states.

## `renLife`: the render-side latch

`particle_c` writes `renLife = -1.0` at spawn and never reads it.
`particle_vv` closes the loop: for **corner 0 only**, it loads `renLife`, and if
`renLife < 0` it copies `curLife` into it and stores it back. `particle_p` then
reads `blurBoundary`, `curLife` and `renLife` together. So `renLife` is the
countdown value captured on the first frame a particle is actually rasterised,
and `curLife / renLife` is a normalised age available to the pixel shader that
is immune to the particle having been spawned mid-frame or off-screen. The
`-1.0` is the "not yet latched" sentinel, and the compute shader is the only
thing that ever re-arms it.

## What this does not establish

- **Every runtime float value.** Item **M5** of
  [`bglayer-background-spec.md`](bglayer-background-spec.md) stands: the
  `Resources` block is written by host code from `.rodata`, and none of its
  values are literals in the shader. This note recovers the *shape* and the
  *arithmetic*, plus the two folded size constants (`0.2`, `0.8`). It does not
  recover `particleMinLife`, `particleMaxLife`, the spawn box, the curl
  frequency or strength, the accelerations, the attractor set, or the counts.
- **The attractor count.** `Resources[0x88]` is runtime data.
- **`SRT[0x14]`**, and the individual meanings of the bit tests on
  `Resources[0x20]` beyond the one that gates the two noise sites.
- **Whether the `0.5` split in `curLife` is a spawn queue.** The two-rate
  schedule and the clamp are measured; the interpretation is not.
- **The `1.5` count** in `grad4` (10 per evaluation against a predicted 5).
- **`light_p`.** `0x11f9700`, 2072 B, 338 instructions, **6 `image_sample`**,
  constants including `-π` and `π/2`. Sliced and censused only; **not
  decoded**. Calling it "the volumetric light shaft" is a **guess from the
  name and the census** — no ISA evidence assigns it that role, and the shaft
  seen in `default.mp4` cannot support the inference because that clip is not
  a firmware asset ([`reference-video-grading.md`](reference-video-grading.md)
  §0). It is the obvious next target because it is the largest undecoded
  program adjacent to the particle set, and for no other established reason.
- ~~**Colour.**~~ **Closed by [`particle-draw.md`](particle-draw.md).**
  `particle_c` computes no colour of any kind — that part stands. But the
  12.40 `particle_p` image carries an **84-byte, seven-entry** palette, not the
  three colours `bglayer-background-spec.md` §1a records from 4.03 (those three
  are entries 4–6, unchanged). The role assignment §1c flagged as an invention
  risk is settled: there is **one colour per particle**, selected by
  `id % 4` or `id % 3` on the same auxiliary u32 that seeds the size lottery and
  the fBm gain, and then multiplied by a single scalar. None of the entries is a
  "core", "rim" or "tint".
- **Cross-version stability.** Everything above is measured on 12.40. Where it
  overlaps 4.03 findings the two agree (record layout, RNG, 6 vertices, the
  `renLife` latch, `t*t*(3-2t)` blends, `time`/`timeStep` SRT offsets); the two
  places they differ (`ParticleBlendParam` stride, the `blurBoundary`
  interpolant) are called out above and were not re-checked against 4.03.

## Port notes

The **draw** side is already ported, to
`windows/Prosperismo/FirstWaveParticle.{h,cpp}` with
`FirstWaveParticleHostTest.cpp` — see [`particle-draw.md`](particle-draw.md)
§*Host port* for exactly what is and is not in it. **The simulation below is
not ported**; no `FirstWaveParticleSim` exists yet. If it is reimplemented
alongside `FirstWaveSurface` / `FirstWaveBlur` / `FirstWaveParticle`, the pieces
that must be right, in order of visual weight:

1. **Curl noise, not plain noise.** The velocity field is divergence-free by
   construction. Substituting three independent noise channels for the curl
   gives sources and sinks, and particles will visibly pile up and thin out.
2. **Three octaves, lacunarity 2, per-particle gain in `[0.5, 1.0)`.** The gain
   varying by buffer slot is what stops the field looking like one coherent
   flow.
3. **The rate limits, not the field, set the feel.** Velocity approaches the
   target at `maxAccel · dt` and the billboard frame rotates at
   `maxRotSpeed · dt`. Both are hard clamps on a normalized step, not lerps.
4. **Explicit Euler, one step.** Do not "improve" it.
5. **The frame is orthonormal every frame**, re-derived by cross product. Skipping
   that lets the quads shear.
6. **Corner falloff radius `0.5`**, if you paste a stock `snoise4D`.
