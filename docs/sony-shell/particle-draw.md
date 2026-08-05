<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background particle draw — `particle_vv/p` and `large_particle_vv/p` decoded

Recovered 2026-08-05 from the 12.40 oracle image. This is the **render** half of
the background particle system; the **simulation** half is
[`particle-system.md`](particle-system.md) (`particle_c`). The split is
deliberate: `particle_c` computes no colour and no geometry, and these four
programs read no simulation state they do not already find in the record.

Read the two together. `particle-system.md` establishes the `ParticleProperty`
record (`pos@0x00, blurBoundary@0x0c, vel@0x10, fore@0x1c, transPatternFlag@0x28,
right@0x2c, curLife@0x38, maxLife@0x3c, renLife@0x40`, stride `0x44`); every
field name used below is that note's, and the read side confirms all of them.

Same rules as the FirstWave notes: constants, offsets, structure and behaviour
only. No firmware program bytes or disassembly listings are copied into the
repository.

## Verdict up front

**Two different particle looks are drawn by two different pairs of programs from
the same record buffer**, separated by a 4-bit tag:

| | `particle_vv` + `particle_p` | `large_particle_vv` + `large_particle_p` |
|---|---|---|
| Shape | **procedural disc**, no texture at all | **procedural disc**, edge width driven by a focus term |
| Colour | one of **seven** embedded palette entries, picked per particle | two sampled background textures, cross-faded, then HSV-tinted |
| Lighting | per-light diffuse + Phong specular + ambient, accumulated to **one scalar** | none |
| Edge | `smoothstep(...)` raised to the power `1 + gradient` — sharp | `smoothstep(...)` over a width that ranges from `1e-4` to ~`1.0` — soft |
| Output | RGB, **alpha = 0** → purely additive | premultiplied RGB **and** alpha |
| Billboard basis | the record's `fore`/`right` pair, screen-size-clamped | a camera basis built from constants, or the identity |

**The split is stated by the ISA, not inferred from any image**: `particle_p`
contains **zero `image_*` instructions** and `large_particle_p` contains exactly
two `image_sample`, and all four programs gate on the same nibble. The
"out-of-focus" character of the large discs is likewise an ISA finding — a
computed defocus width in `large_particle_p` (§*The defocus edge*), not a blur
pass and not an inference from a picture.

> **Note on wording.** An earlier revision of this note motivated the split by
> saying it "maps onto the reference capture's small sharp points and large soft
> out-of-focus discs". That motivation is **withdrawn**: the capture meant was
> `ps5oracle/shell_ui/live_background/default.mp4`, which
> [`reference-video-grading.md`](reference-video-grading.md) §0 establishes is
> **not a firmware asset**. Nothing in this note depends on it — every claim
> below is read out of the eboot. Where `default.mp4` measurements are
> mentioned at all, they are labelled *reference-measured (provenance failed)*
> and carry no evidential weight.

## Slice and provenance

| Program | File offset | Slice to first `s_endpgm` | SHA-256 (slice) | Instructions |
|---|---|---:|---|---:|
| `particle_p` | `0x1201500` | 1564 B | `0a656272…` | **298** |
| `particle_vv` | `0x1201d00` | 1276 B | `bdde07f4…` | **211** |
| `large_particle_p` | `0x1202400` | 1508 B | `c7a25fce…` | **274** |
| `large_particle_vv` | `0x1202c00` | 1120 B | `6d5c6f23…` | **200** |

Image: 12.40 oracle `system_ex/app/NPXS40087/eboot.bin`, 21,695,212 B,
`18c9320b…`; entries from
[`shader-entry-map-12.40.json`](shader-entry-map-12.40.json); decode
`llvm-mc --arch=amdgcn --mcpu=gfx1010 --disassemble`.

### A decoder caveat that changes the answer, not just the count

`llvm-mc` at `gfx1010` **refuses two encodings in `particle_vv` and one in
`large_particle_vv`** and silently resynchronises four bytes later. Both are
`v_mov_b32` in SDWA form with the `src0_neg` modifier set — an eight-byte
register negate. Decoded by hand from the SDWA word, they are:

| Program | Byte offset in slice | Instruction |
|---|---|---|
| `particle_vv` | `+0x318` | `v8 = -v0` |
| `particle_vv` | `+0x470` | `v13 = -v13` |
| `large_particle_vv` | `+0x3d0` | `v12 = -v13` |

This is not cosmetic. Without them the byte cursor drifts, every subsequent
branch target lands mid-instruction, and — worse — the two vertex programs read
as exporting the *wrong* clip-space `z` and the wrong `param5.x`. With them,
byte totals close exactly (1276/1276 and 1120/1120), every branch target lands
on an instruction boundary, and the depth export becomes a coherent projection.
**Instruction counts are 211 and 200, not the 209 and 199 a naive `llvm-mc` line
count gives**; the missing two and one are exactly these.

The counts for the two pixel programs (298, 274) need no correction.

### The programs do not end at the first `s_endpgm`

All four continue past it:

| Program | First `s_endpgm` | Real code end | What follows |
|---|---|---|---|
| `particle_p` | `+0x618` | `+0x62c` | discard epilogue, then an **84-byte palette at `+0x630`** |
| `large_particle_p` | `+0x5e0` | `+0x5f4` | discard epilogue, then `s_code_end` padding |
| `particle_vv` | `+0x4f8` | `+0x4fc` | a **48-byte corner table at `+0x500`** |
| `large_particle_vv` | `+0x45c` | `+0x460` | padding, then a **48-byte corner table at `+0x470`** |

The pixel epilogue is three instructions — `exec = 0`, a null `exp mrt0 … done
vm`, `s_endpgm` — and is the branch target of every kill test. Both pixel
programs *discard*; neither writes a killed fragment.

## Billboard expansion: six vertices, one inline quad

Both vertex programs are NGG primitive shaders (`s_sendmsg
sendmsg(MSG_GS_ALLOC_REQ)` then `exp prim`). The expansion is identical in both:

```
particle = tid / 6              // v_mul_hi_u32 by 0xaaaaaaab, then >> 2
corner   = tid - 6 * particle
```

The corner offsets come from a table **embedded in the program image**, read
through a V# built by `s_getpc_b64` + a PC-relative add, with
`num_records = 48` and `dword3 = 0x10005004` (an 8-byte stride). Both tables
were extracted and are **byte-identical**:

| corner | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| offset | `(-1,-1)` | `(1,-1)` | `(-1,1)` | `(1,-1)` | `(-1,1)` | `(1,1)` |

Two triangles over the `[-1,1]` square, sharing the `(1,-1)–(-1,1)` diagonal.
This reproduces the 4.03 table quoted in
[`bglayer-background-spec.md`](bglayer-background-spec.md) §1a exactly.

Both pixel programs kill at radius **0.99** in that same corner space, so the
drawn disc is inscribed in the quad and the quad's own corners (radius √2) are
always discarded. About 23% of the rasterised area is thrown away — the price of
drawing a circle with a quad.

Unused registers in both vertex programs are pre-filled with `0x49742400` =
**`1e6`**, the off-screen sentinel the simulation note already flagged.

## Culling: one nibble decides which pair draws the particle

All four programs perform the same test:

```
kill unless (particle.transPatternFlag & 0xf) == (SRTVsPs[0x10] & 0xf)
```

`SRTVsPs[0x10]` is `transPatternFlag` per
[`bglayer-background-spec.md`](bglayer-background-spec.md) §1b, and
`particle-system.md` establishes that `particle_c` migrates a particle's nibble
from the current pattern id to the next one at respawn. **The same buffer feeds
both looks**; the nibble is what routes a particle to the small-point pass or
the large-disc pass, and the migration is what crossfades between them.

`particle_vv` and `particle_p` additionally bounds-check twice against counts in
the resource block; `large_particle_*` checks only the nibble.

## `particle_vv` — the small points

### Resource block

`SRTVsPs[0x00]` is a pointer; everything below is an offset into the block it
points at. **These offsets are not the ones `particle_c` uses** — the compute
stage's block puts the auxiliary buffer at `+0x00` and the record buffer at
`+0x10`, and this one is the other way round. They are two different structs
reached through two different SRTs; nothing here contradicts the compute note.

| Offset | Type | Use (measured) |
|---|---|---|
| `+0x00` | V# | the `ParticleProperty` record buffer |
| `+0x10` | V# | the auxiliary u32-per-slot buffer (the size seed) |
| `+0x20` | u32 | invocation limit; the wave dies unless `tid < 6 * this` |
| `+0x24` | u32 | base record index |
| `+0x28` | u32 | record index stride |
| `+0x2c` | u32 | record capacity; the wave dies unless `index < this` |
| `+0x30`..`+0x38` | float3 | origin of the gradient axis |
| `+0x3c`..`+0x44` | float3 | the gradient axis itself |
| `+0x48`, `+0x4c` | f32 ×2 | gradient band A: `lo`, `hi` |
| `+0x50`, `+0x54` | f32 ×2 | gradient band B: `hi`, `lo` (note the order) |
| `+0x5c`, `+0x60` | f32 ×2 | size lottery range |
| `+0x64` | f32 | the minimum-screen-size target |
| `+0x138` | f32 | eye `z` |

### The gradient scalar, and the two smoothstep pairs

The programs project the particle onto an axis:

```
eye  = (-pos.x, pos.y, Res[0x138] - pos.z)
h    = dot(Res[0x3c..0x44], eye - Res[0x30..0x38])
```

The leading minus on `pos.x` is real — the x axis is mirrored between world and
eye space, and it is confirmed independently by `particle_p`, which receives
`-pos.x` in `param5.x` and applies the identical form with its own constants.

From `h`, four clamped ramps are formed and summed in **two pairs**:

```
a = saturate((h - Res[0x48]) / (0.01 * (Res[0x4c] - Res[0x48])))
b = saturate((h - Res[0x48]) /         (Res[0x4c] - Res[0x48]) )
c = saturate((h - Res[0x54]) /         (Res[0x50] - Res[0x54]) )
d = saturate((h - Res[0x54]) / (0.01 * (Res[0x50] - Res[0x54])))

wide   = smoothstep(b) + smoothstep(c)      // exported as param0.w
narrow = smoothstep(a) + smoothstep(d)      // 1% of the band width
```

`smoothstep` here and everywhere in these four programs is the open-coded
`t*t*(3-2t)` with a clamped argument — the `-2.0`/`3.0` pair (`v_madmk_f32` with
`0x40400000`) is explicit at every site.

`wide` is the single most load-bearing value in the pass: it drives the sprite
size floor, the quad's depth extent, the pixel shader's core radius blend, and
the pixel shader's falloff exponent. **Its range is `[0, 2]`, not `[0, 1]`** —
it is a sum of two smoothsteps, and nothing clamps the sum. Whether the two
bands are arranged so that only one is ever active is a property of the runtime
constants and is **not determined**.

### Size

```
seed   = aux[index]                              // the u32 particle_c also uses for fBm gain
r      = ((16807 * seed) mod 2^31-1) mod 1000
size   = Res[0x5c] + 0.001 * (r * (Res[0x60] - Res[0x5c]))
```

Literals `0x41a7`, the `0x80000001`/`>>30`/`0x7fffffff` fold and the
`0x10624dd3`/`>>6` magic divide — the same generator `particle_c` uses, and the
same one the 4.03 note records. **One correction of emphasis:** the multiply is
`v_mul_lo_u32`, which truncates to 32 bits *before* the fold, so this is not
textbook Park–Miller; it reduces the truncated product. For a single step from
an arbitrary seed the distinction is invisible, but a port that chains the
generator will diverge if it uses a 64-bit product.

**This is a second, independent size source.** `particle_c` computes
`blurBoundary` from the particle's buffer index (`particle-system.md`), and it
is used as a *radius* by the pixel shader; this lottery is used as the *quad
scale* by the vertex shader. They are not the same quantity and neither
supersedes the other.

### Quad axes and the minimum-screen-size clamp

```
A = size * fore                              // record +0x1c
U = size * lerp(right.xy, (fore.y, -fore.x), narrow)
```

As `narrow` goes 0 → 1 the second axis rotates off `right` and onto `fore`
rotated a quarter turn, i.e. the quad becomes a square in screen space.

Then each axis is floored:

```
floor  = lerp(size, Res[0x64], wide)
scaleA = max(floor, |A.xy| + 1e-6) / (|A.xy| + 1e-6)
scaleU = max(floor, |U|    + 1e-6) / (|U|    + 1e-6)

offset.xy = corner.x * A.xy * scaleA + corner.y * U * scaleU
offset.z  = size * (1 - wide) * (corner.x * fore.z + corner.y * right.z)
```

Note the asymmetry: the xy extent gets the size floor, the z extent gets
`(1 - wide)` instead. Both are measured; only the first has an obvious purpose
(keeping distant particles from disappearing below a pixel).

The two scale factors are exported as `param2.zw` and the pixel shader uses them
to shrink the disc back along whichever axis was stretched, so the *drawn* point
stays circular while the *quad* is enlarged. That is the whole minimum-size
mechanism, and it is the reason the small points stay crisp instead of aliasing.

### Projection

`particle_vv` has no matrix. The projection is four folded literals:

| Literal | Value | Role |
|---|---:|---|
| `0x3f2dd2d0` | `0.67899799` | `clip.x = this * (offset.x - pos.x)` |
| `0x3f9a827b` | `1.20710695` | `clip.y = this * (pos.y + offset.y)` |
| `0xbf80419a` → negated | `1.00200200` | `clip.z = A * clip.w - B` |
| `0x3dcd013b` → negated | `0.10010000` | ” |

with `clip.w = (Res[0x138] - pos.z) + offset.z`.

Two things fall out and both check numerically:

- `1.20710695 = ½·cot(22.5°)` to 1.4e-7 relative, and
  `0.67899799 / 1.20710695 = 0.5625 = 9/16` exactly. So this is a **45° vertical
  field of view at 16:9, carrying an extra factor of ½** on both axes.
  `large_particle_vv` builds the same quantity at runtime from a fov in degrees
  and applies the same ½ with a `div:2` output modifier, so the halving is a
  house convention in this codebase, not a fitting artefact.
- Solving `A = f/(f-n)`, `B = fn/(f-n)` gives **near ≈ 0.0999, far = 50.0**
  exactly, i.e. a forward-Z `[0,1]` depth mapping. Not reverse-Z.

### Exports

| Export | Contents |
|---|---|
| `pos0` | clip position as above |
| `param0` | `pos.xyz`, **`wide`** |
| `param1` | corner world position (`pos + offset`), `1.0` |
| `param2` | `corner.xy`, `scaleA`, `scaleU` |
| `param3` | `normalize(cross(right, fore))`, `1.0` |
| `param4` | raw `tid`, record index (both integer, flat) |
| `param5` | `-pos.x`, `pos.y`, `Res[0x138] - pos.z`, `h` |

`param3` is a genuine normal built from the record's orthonormal frame, and it
is consumed by the lighting loop below.

### The `renLife` latch, written from the vertex shader

For **corner 0 only**, and only where `renLife < 0`, `particle_vv` copies
`curLife` into `renLife` and stores it back to the record. This is the closing
half of the mechanism `particle-system.md` describes from the compute side;
recorded here because it means the *vertex* stage performs a buffer **store**,
which is unusual enough to be worth flagging for a port.

## `particle_p` — the procedural point

**Zero `image_*` instructions.** Everything below is arithmetic.

### The shape

```
r     = length(corner.xy)                                  // param2.xy
kill  if r >= 0.99

dir   = (r < 1e-6) ? (0,0) : corner.xy / r
aniso = 1 / sqrt((|scaleA| * dir.x)^2 + (|scaleU| * dir.y)^2 + 1e-6)
inner = lerp(aniso, blurBoundary, smoothstep(saturate(10 * wide)))

t     = saturate((r - 0.99) / (min(0.98, inner) - 0.99))
shape = pow(smoothstep(t), 1 + wide)                       // v_log_f32 / v_exp_f32
```

The denominator is always negative, so `t` is 1 across the core and falls to 0
at `r = 0.99`. The profile is therefore a **flat top out to `inner`, then a
Hermite shoulder, then a hard cut** — not a Gaussian and not a plain
`1 - r²`.

`blurBoundary` arriving here as the radius of the flat top is a satisfying
closure: `particle-system.md` derives it in `particle_c` as a size curve over
the buffer index with range `[0.2, 1.0]`, and the name says "the boundary of the
unblurred region". The ISA agrees with the name.

The `1 + wide` exponent is the sharpener: because `smoothstep(t) ∈ [0,1]`,
raising it to a power ≥ 1 only ever darkens the shoulder, leaving the flat top
at 1 and the rim at 0. With `wide` reaching 2 the shoulder is cubed.

### Life fade

```
fade = smoothstep(saturate(2 * curLife)) * smoothstep(saturate(2 * (renLife - curLife)))
```

Fade in over the first half unit of `curLife`, fade out over the last half unit
before the latch. The factor of two is a literal `2.0` at both sites.

### Lighting

A real loop over `Resources[0x8c]` lights of **56 bytes** each based at
`Resources[0x90]`, gated on a per-light `type == 1`:

> **These are pixel-stage offsets and do not index the compute block.**
> `particle-system.md` records `Resources[0x88]` as the *attractor count* and
> `Resources[0x8c]` as the base of 36-byte *attractor* records. Those are the
> same numbers in a **different struct**, reached through a different SRT — the
> same caveat already given above for the vertex block. A count-and-array pair
> landing at `0x88`/`0x8c` in one block and `0x8c`/`0x90` in another is a
> coincidence of layout, not a shared binding, and the two must never be
> cross-indexed.

| Light field | Type | Use |
|---|---|---|
| `+0x00` | float3 | position |
| `+0x0c` | f32 | blend between the two distance measures below |
| `+0x10` | float3 | axis direction |
| `+0x1c` | u32 | type; the loop body is skipped unless this is `1` |
| `+0x20` | f32 | diffuse weight |
| `+0x24`, `+0x28` | f32 ×2 | falloff `hi`, `lo` |
| `+0x2c` | f32 | ambient weight (**not** multiplied by the falloff) |
| `+0x30` | f32 | specular weight |
| `+0x34` | f32 | specular exponent, applied with `v_log_f32`/`v_exp_f32` |

The shading point is `lerp(cornerWorldPos, particlePos, smoothstep(saturate(10 *
wide)))` — the same blend weight as the core radius. The eye is the literal
`(0, 0, -3)`; `-3.0` appears inline, and this is a *different* eye from the
vertex shader's `Res[0x138]`, which is recorded as measured and **not**
reconciled.

Per light:

```
L      = normalize(lightPos - shadingPoint)
ndl    = dot(normal, L)                              // param3.xyz
R      = reflect(normalize(P - lightPos), normal)
cosSpc = dot(R, eye - P) / (|R| * |eye - P|)

cd     = dot(axis, L)
dPerp  = |lightPos - shadingPoint| * sqrt(1 - cd*cd)
u      = saturate(((1 - k) * dPerp - k * cd - lo) / (hi - lo))    // k = light +0x0c
F      = min(1, smoothstep(u))

diffuse  += diffW * |ndl|      / (1 + wide)^2 * F
specular += specW * |cosSpc|^e / (1 + wide)^2 * F
ambient  += ambW               / (1 + wide)^2
light     = diffuse + ambient + specular         // a single scalar, no per-light colour
```

`(1 - k) * dPerp - k * cd` mixes a **distance** with a **cosine**, which is
dimensionally inconsistent. It is what the instructions say (`v_mad_f32` of
`1 - light[0x0c]` against the perpendicular distance, plus `-light[0x0c]` times
the raw dot product). Whether that is intentional or a bug in Sony's source is
**not determined**; it is recorded literally rather than "corrected".

### Colour: seven entries, not three

An **84-byte table (7 × float3) at `+0x630`** in the program image, reached by
two `s_getpc_b64` sites that resolve to the *same* address. A pixel constant at
`SRTVsPs[0x14]` selects the half:

| Index | Value | 8-bit | Selected when |
|---:|---|---|---|
| 0 | `0.913725, 0.329412, 0.435294` | `(233, 84, 111)` | `SRT[0x14] != 0`, `id % 4 == 0` |
| 1 | `0.788235, 0.498039, 0.701961` | `(201, 127, 179)` | `id % 4 == 1` |
| 2 | `0.000000, 0.627451, 0.533333` | `(0, 160, 136)` | `id % 4 == 2` |
| 3 | `0.345098, 0.501961, 0.756863` | `(88, 128, 193)` | `id % 4 == 3` |
| 4 | `0.693767, 0.459286, 0.204934` | — | `SRT[0x14] == 0`, `id % 3 == 0` |
| 5 | `0.420054, 0.187302, 0.075132` | — | `id % 3 == 1` |
| 6 | `0.501961, 0.329412, 0.211765` | `(128, 84, 54)` | `id % 3 == 2` |

`id` is the per-particle u32 from the auxiliary buffer — the same value that
seeds the size lottery and the fBm gain.

Entries 0–3 are exact 8-bit values and read as the four PlayStation face-button
hues; entries 4–6 are the warm gold/amber/brown set. The *selection arithmetic*
is measured; calling 0–3 "the symbol colours" is an **inference from the
values**.

**Which path the shipped shell takes is not recovered.** `SRTVsPs[0x14]` is
host-written and its value has never been read. An earlier revision claimed
"the reference capture's warm gold is the `SRT[0x14] == 0` path" — that is
**withdrawn**, because the capture was `default.mp4`, which is not a firmware
asset ([`reference-video-grading.md`](reference-video-grading.md) §0). For the
record: that clip's sprites measure hue ≈ 34° at saturation 0.28–0.63
(§3.3 there), which is compatible with entries 4–6 and not with 0–3 — but it
is a measurement of a non-firmware clip and settles nothing about the console.

One override, measured: when `Resources[0x13c] == 6` **and** the warm index
lands on entry 6, the table read is replaced by the inline constant
`(0.420054, 0.254900, 0.142295)`. That is a fourth warm tone, close to entry 5
in red but lighter in green and blue.

**Two corrections to earlier notes.**

1. [`bglayer-background-spec.md`](bglayer-background-spec.md) §1a records the
   4.03 table as "12 floats, last 3 are zero padding", i.e. three colours. The
   12.40 table is **84 bytes and seven colours**; the three 4.03 values are
   entries 4–6 here, unchanged to the last digit. Both are right about their own
   firmware. A port targeting 12.40 needs all seven.
2. §1c of the same note lists "role assignments for the three `particle_p`
   embedded colours" as its one outstanding invention risk, warning against
   calling one the core, one the rim and one the tint. **They are none of those.**
   There is exactly one colour per particle, selected by a modulus on the
   particle's seed and then multiplied by a single scalar. That open item is
   closed.

### Output

```
brightness = lerp(1, Res[0x88], smoothstep(saturate((h2 - Res[0x80]) / (Res[0x84] - Res[0x80]))))
rgb        = palette * (shape * brightness * lifeFade * light)
alpha      = 0
```

where `h2` is a second gradient projection, identical in form to the vertex
shader's `h` but reading `Resources[0x68..0x7c]` instead of `[0x30..0x44]`, and
using `param5.xyz` as the point.

The export is `exp mrt0 … done compr vm`, and the second packed pair is
`(blue, 0)` — a literal zero, not a computed alpha. **The small points are a
pure additive pass.**

## `large_particle_vv` — the big discs

Same six-vertex expansion, same corner table, same nibble cull, same size
lottery (with `Resources[0xa8]` added to the seed first, and the range at
`Resources[0xe4..0xe8]`).

The difference is the basis. `Resources[0x70] == 1` selects a 3D path:

```
F = normalize(Res[0x90..0x98])
R = normalize(cross(Res[0x9c..0xa4], F))
U = cross(F, R)
p = (pos - Res[0x84..0x8c])
P = p.x * R + p.y * U + p.z * F
```

Otherwise `R = (1,0,0)`, `U = (0,1,0)` and `P = pos` — the particle positions are
already screen coordinates.

```
corner3 = size * (corner.x * R + corner.y * U)
C       = P + corner3
```

Projection, in the `Res[0x70] == 1` path and only when `Res[0x7c] < Res[0x80]`
(near < far):

```
sy       = ½ · cot(radians(Res[0x74] / 2))     // Res[0x74] is a fov in DEGREES
clip.x   = C.x * sy / Res[0x78]                // Res[0x78] is the aspect ratio
clip.y   = C.y * sy
clip.z   = C.z * (n + f)/(f - n)  -  n·f/(f - n)
clip.w   = C.z
```

The degrees→revolutions constant is `0x3ab60b61 = 1/720`, and AMD's
`v_sin_f32`/`v_cos_f32` take revolutions, so the angle really is `fov/2` in
radians. The `½` is a `div:2` output modifier — the same halving `particle_vv`
has baked into its literals.

The depth row is *not* the textbook D3D one: it is the standard mapping plus a
bias of `n/(f-n)`, so NDC z runs `[n/(f-n), f/(f-n)]` rather than `[0,1]`. For
`n ≪ f` that is within `n/f` of correct. Recorded as measured; whether it is
deliberate is **not determined**.

Otherwise `clip = (C.x / Res[0x78], C.y, C.z, 1)` — a straight aspect-corrected
pass-through.

| Export | Contents |
|---|---|
| `pos0` | as above |
| `param0` | `pos.x / aspect`, `pos.y`, `P.z`, `1.0` |
| `param1` | the same clip position as `pos0` |
| `param2` | `corner.xy`, `0`, `0` |
| `param3` | `size`, `0` |
| `param4` | record index (flat) |

## `large_particle_p` — the defocus disc

Two `image_sample`, both 2D, both `dmask 0x7` (RGB only), both with an immediate
sampler descriptor; the two T#s are a `s_load_dwordx16` from `Resources[0x20]`,
i.e. **two 8-dword image descriptors back to back** at `+0x20` and `+0x40`.
That reproduces `bglayer-background-spec.md` §1b's
`ResourcesLargeParticleVsPs` entry ("the two texture descriptors at `+0x20` and
`+0x40`") exactly. The pass runs in whole-quad mode (`s_wqm_b64`) because of
them.

### The defocus edge — the heart of the "out of focus" look

```
r      = length(corner.xy)
kill  if r >= 0.99

d      = (corner - centre) in param space, x rescaled by the aspect at Res[0x78]
rim    = centre + size * normalize(d)         // the disc rim in this fragment's direction
fdist  = length(rim - Res[0xbc..0xc0])        // distance to a 2D focus point
focus  = smoothstep(saturate((fdist - Res[0xd4]) / (Res[0xd8] - Res[0xd4])))

width  = focus * (min(0.9998, Res[0xdc]) - 0.9999) - 1.0001659e-4
edge   = smoothstep(saturate((r - 1) / width))
```

`width` is negative by construction and its magnitude is the softness:

- **in focus** (`focus = 0`): `width = -1.0002e-4`. The ramp occupies one
  ten-thousandth of the radius — a hard-edged disc.
- **fully defocused** (`focus = 1`, `Res[0xdc]` small): `width → -1.0`. The ramp
  occupies the whole radius — a Hermite bokeh disc, bright in the middle,
  vanishing at the rim.

That single expression is the entire small-sharp / large-soft distinction, and
it is **per fragment**, evaluated at the rim point in the fragment's own radial
direction, so one disc can be sharper on one side than the other.

The literals: `0x3f7ff2e5 = 0.9998`, `0x3f7ff972 = 0.9999`,
`0xb8d1c000 = -1.0001659e-4`.

### Depth and life

```
depth = min(1, smoothstep(saturate((P.z - Res[0x7c]) / (0.05 * (Res[0x80] - Res[0x7c]))))
              + (Res[0x70] != 0 ? 1 : 0))
life  = smoothstep(saturate(max(0, curLife))) * smoothstep(saturate(maxLife - curLife))
```

`depth` is a near-plane fade over the first 5% of the depth range, **disabled
outright** when `Res[0x70]` is non-zero — which is the same flag that selects the
3D basis in the vertex program, so the screen-space mode has no depth fade. That
is the only soft-particle-like term in either pass: **there is no depth-buffer
read, no scene-depth compare, and no soft-particle intersection fade anywhere in
these four programs.**

Note this pair uses `curLife`/`maxLife`, whereas `particle_p` uses
`curLife`/`renLife`. Measured, and different.

### Colour

```
uv    = (param1.x / 60 + 0.5,  0.5 - param1.y / 100)
tex   = lerp(sample(T0, uv), sample(T1, uv), blend)     // skipped, tex = white, if !(Res[0x60] & 1)
blend = ((Res[0x60] >> 1) & 0x3ff) / 1023
tint  = hsv2rgb(Res[0x64], Res[0x68], Res[0x6c])        // hue in RADIANS
c     = tex * tint

hsv   = rgb2hsv(c)
alpha = Res[0xe0] * edge * life * depth
out   = (hsv2rgb(hsv.h, hsv.s, hsv.v * alpha), alpha)
```

The two textures are almost certainly the `Sce.Vsh.ShellUI.BGLayer.Particle0/1.gnf`
pair already inventoried in `bglayer-background-spec.md`, and the 10-bit blend
weight is the crossfade between them. That identification is an **inference**;
the descriptors are opaque in the program.

HSV is open-coded twice, forward and inverse, with hue in radians: the wrap
constant is `2π` (`0x40c90fdb`), the sextant scale is `6/2π = 0.9549296`
(`0x3f747645`), the hue step is `π/3` (`0x3f860a92`) and the sextant thresholds
are `1.9999`, `3.9999`, `4.9999`. It is textbook HSV; the only thing worth
recording is the radian convention and the fact that the round trip exists at
all — the shader converts to HSV **solely to scale `value` by alpha**, i.e. to
darken without desaturating.

The final export carries a real alpha, so this pass is **premultiplied**, not
additive.

The UV mapping is the one loose end. `param1.xy` is a *clip* position, and
dividing it by 60 and 100 addresses only a `±0.02` neighbourhood of the texture
centre. Either the textures are small ramps for which that is still a useful
gradient, or the intended input was a world position. **Not determined**; the
constants are `1/60` (`0x3c888889`) and `1/100` (`0xbc23d70a`, negated) with a
`0.5` bias, and they are recorded as read.

## What this does not establish

- **Every runtime constant.** The resource blocks are host-written; none of the
  gradient bands, light sets, focus radii, texture bindings, fov, near/far or
  opacity values are literals in these programs. Shape and arithmetic are
  recovered; the numbers that drive them are not.
- **The relationship between the two vertex-side resource blocks.**
  `particle_vv` reads `0x20`–`0x88` plus `0x138`/`0x13c`; `large_particle_vv`
  reads `0x60`–`0xe8`. Whether that is one struct with two sub-blocks or two
  structs is not determined.
- **The two eyes.** `particle_vv` places the eye at `Resources[0x138]` on z;
  `particle_p` uses a literal `(0, 0, -3)` for its specular view vector. Both
  are measured; they are not reconciled here.
- **Whether `wide` can exceed 1.** It is a sum of two smoothsteps and nothing
  clamps it. The consequences (a cubic falloff shoulder, a size floor
  overshooting `Resources[0x64]`) are real if it does.
- **The dimensional mix in the light falloff** (`(1-k)·distance − k·cosine`).
- **The `large_particle_p` texture UV scale**, above.
- **Blend state.** The alpha values imply additive for the points and
  premultiplied for the discs, but the actual blend registers live in the
  command buffer, not the shader. Not read.
- **Cross-version stability.** Everything is measured on 12.40. The one place it
  overlaps a 4.03 finding — the embedded colour table — it *differs* (seven
  entries vs three), and that is called out above.

## Host port

The self-contained arithmetic is ported to
`frontend/ProsperismoLauncher/windows/Prosperismo/FirstWaveParticle.{h,cpp}`,
following the `FirstWaveSurface` / `FirstWaveBlur` pattern (no platform headers,
`pch.h` behind `#ifdef _WIN32`), with checks in `FirstWaveParticleHostTest.cpp`:

```
clang++ -std=c++20 -O2 -Wall -Wextra -I windows/Prosperismo \
    windows/Prosperismo/FirstWaveParticleHostTest.cpp \
    windows/Prosperismo/FirstWaveParticle.cpp -o /tmp/fwparticle && /tmp/fwparticle
```

What is ported: the corner table and vertex→corner mapping, the size lottery
(with the ISA's own magic-number reductions, checked against plain modulo), the
minimum-screen-size clamp, the projection constants and the runtime `½·cot`
form, the `particle_p` anisotropic radius / core radius / power falloff / life
fade / palette selection, the `large_particle_p` defocus width and edge profile,
its life fade and alpha, the texture flag decode, and the HSV pair.

What is **not** ported, deliberately: the lighting loop (its light records are
runtime data and its falloff has the unresolved dimensional mix), the gradient
projections (runtime axes), and anything touching textures.

The host checks include a negative control: perturbing `kPointCutoffRadius` from
`0.99` to `0.95` makes the suite fail, so the assertions are load-bearing.

## Port notes, in order of visual weight

1. **Two passes, not one.** Additive sharp points and premultiplied soft discs,
   routed by a 4-bit tag on a shared buffer. Drawing one pass with a size
   parameter will not reproduce it.
2. **The falloff is a flat top plus a Hermite shoulder**, cut hard at 0.99, with
   an exponent that varies per particle. Not a Gaussian, not `1 - r²`, and not a
   texture.
3. **The soft discs' softness is a computed width**, from a 2D distance to a
   focus point, evaluated per fragment on the rim. A fixed soft edge loses the
   effect entirely.
4. **The minimum-size clamp is two separate axis scales, undone in the pixel
   shader.** Skipping the undo turns the small points into ellipses whenever the
   clamp engages.
5. **Colour is one palette entry per particle**, chosen by a modulus on the same
   seed the simulation uses — not a gradient and not a per-fragment mix.
6. **Alpha zero for the points.** They rely on the framebuffer's additive blend;
   giving them an alpha will change the look wherever they overlap.
