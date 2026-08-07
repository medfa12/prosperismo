<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# `particle_c`: located, decoded, and its constants read out of the code

**This supersedes the "constants not recovered" status in
[`particle-sim-12.40-map.md`](particle-sim-12.40-map.md).** The particle
simulation's tuning values were never findable on the CPU side because they are
not there: they are **literal operands inside the shader's own instruction
stream**. Decoding the shader hands them over exactly.

Reproduce:

```
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin --firstwave
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin --dump-stage particle_c
```

## Where the shaders are

The FirstWave stage offsets came from
[`firstwave-12.40-shader-contracts.json`](firstwave-12.40-shader-contracts.json).
The **particle** programs were not in that file. They were located by scanning
the 12.40 eboot for the `s_inst_prefetch` prologue (`0xBFA00003`) and profiling
each hit's opcode mix — 19 programs sit in `0x11F0000–0x1200000`, ten of them
already named.

| offset | bytes | instr | MIMG | MUBUF | identification |
|---|---|---|---|---|---|
| `0x11FA100` | `0x71A4` | 5,018 | 0 | 26 | **`particle_c`** |
| `0x11F2E00` | `0x510` | 232 | 2 | 0 | **`large_particle_p`** (two `image_sample`) |
| `0x11F0400` | `0xE90` | 673 | 3 | 0 | particle draw |
| `0x11F1500` | `0xED0` | 687 | 4 | 0 | particle draw |
| `0x11F2600` | `0x5DC` | 262 | 4 | 0 | particle draw |

`0x11FA100` is the only program in the region with buffer traffic and **no**
texture reads — a simulation, not a draw. Its second instruction pair is
`s_load_dwordx4 ..., 0x28`, and `ResourcesCs` stores the particle count at
`+0x28`. Identification is structural; nothing here rests on proximity.

The four draw programs match `particle-draw.md`'s "all four programs gate on the
same nibble", and only one of them has exactly two `image_sample`, which is what
that document says `large_particle_p` has.

## The translator accepts them

14 of 15 stages decode through `Gen5ShaderTranslator`, including the 5,018-instruction
compute shader. Instruction counts for the ten named stages match the contract
JSON exactly (90, 41, 700, 524, 180, 40, 122, 122, 463), which independently
confirms the slices.

**`fw_flow_vl` is the one failure.** Its contract length `0x72C` contains no
`s_endpgm`, and decoding to the next program's boundary desyncs on
`unknown-vop2 op=0x00`. Unresolved; it is the wave surface's vertex-fetch stage
and does not block the particle path.

## The particle record is 68 bytes

Read off the shader's own `buffer_load`/`buffer_store` offsets, not assumed:
`0x00`, `0x0C`, `0x10`, `0x20`, `0x28`, `0x30`, `0x38`, `0x40`, with `dwordx3`
and `dwordx4` widths. Last dword begins at `0x40`, so the stride is **68 bytes**
— matching the figure in `particle-system.md`.

## The `ResourcesCs` layout

Offsets the shader loads from its resource pointer:

```
0x00 0x10 0x20 0x24 0x28 0x38 0x48 0x58 0x68 0x70 0x78 0x88 0x8C 0x94 0x98 0xA8
```

and, from the constant buffer in `s[0:3]`: `0x00 0x08 0x10 0x14 0x18`.

## The constants

Ordered by first use, which makes the algorithm readable.

**Per-particle PRNG — Park–Miller minimal standard.**

| pc | value | role |
|---|---|---|
| `0x02F4` | `16807` | Lehmer multiplier |
| `0x0314` | `2147483647` | modulus `2^31 − 1` |
| `0x0330` | `0x10624DD3` | magic reciprocal for `/1000` |
| `0x0360` | `1000` | divisor |

**Noise — Ashima/Gustavson 4D simplex, verbatim.**

| value | identity |
|---|---|
| `0.309017003` | `F4 = (√5 − 1)/4` |
| `0.138196602` | `G4 = (5 − √5)/20` |
| `0.276393205`, `0.414589792` | `2·G4`, `3·G4` |
| `289`, `-0.00346020772` | `mod289` and `1/289` |
| `34` | `permute(x) = mod289((x*34 + 1)*x)` |
| `1.79284286`, `-0.853734732` | `taylorInvSqrt(r) = 1.79284291400159 − 0.85373472095314·r` |
| `7`, `0.142857149`, `49`, `0.0204081628` | gradient construction: `7`, `1/7`, `49`, `1/49` |
| `-0.44721359` | `−1/√5` |

This is the published constant set of the standard WebGL simplex
implementation, not a resemblance to it.

**Two decorrelated fields, offset by 20.** The whole spatial constant block
appears twice, at `pc≈0x08B4` and again at `pc≈0x4D34`, with every positional
constant shifted by exactly `+20`:

| first field | second field |
|---|---|
| `-9519` | `-9499` |
| `9051` | `9071` |
| `-1239.09998` | `-1219.09998` |
| `-123` | `-103` |
| `123.400002` | `143.399994` |
| `129845.602` | `129865.602` |

Sampling one noise domain at two large offsets is the standard way to obtain
independent components for a vector field. `20` itself is a literal at
`pc=0x4D34`.

**Other literals:** `100000` (`0x0240`), `0.001`/`0.002` (`0x03AC`/`0x03F8`),
`0.8`/`0.2` (`0x0668`/`0x06AC`), `1.5`, `3`, `-6`, `1e-6`, `0x1000` (4096),
`0x300` (768), `0xF0` (240), `0x40F` (1039).

### A correction this forces

`particle-system.md` states the per-particle gain is drawn from `[0.5, 1.0)`.
The literals are `0.2` and `0.8`, which give `0.2 + 0.8·r` → **`[0.2, 1.0)`**.
The earlier figure was inferred; this one is read from the code.

## It compiles and dispatches

`--compile-particle` takes it the rest of the way:

```
decode : OK - 5,018 instructions
evaluate: OK - 4 buffer binding(s), 0 image binding(s)
spirv   : OK - 1,478,652 bytes
device  : Apple M4
dispatch: OK
```

Sony's particle simulation is a valid SPIR-V compute shader that this Mac's
Vulkan driver accepts and runs. `278,528 = 68 × 4096` — the evaluator's buffer
size agrees with the record stride derived independently from the store offsets.

## The two dispatch gates

Read out of the prologue, and worth recording because both silently retire the
whole dispatch when their inputs are wrong.

**Bounds, `pc=0x0004`–`0x0044`.** The block at `ResourcesCs + 0x28` is *not* a
buffer descriptor — it is four scalars, and they work in **index** space, not
bytes:

| offset | name |
|---|---|
| `+0x28` | `numParticles`; lane retires when `threadId >= numParticles` |
| `+0x2C` | `maxParticleId`; lane retires when the record index reaches it |
| `+0x30` | `offsetParticle` |
| `+0x34` | `indexStridePerParticle` |

```
v1  = globalThreadId
v0  = indexStridePerParticle * v1
v15 = offsetParticle + v0
exec = ~((numParticles <= v1) | (maxParticleId <= v15))
s_cbranch_execz            ; pc=0x0044
```

**Pattern, `pc=0x0064`–`0x00BC`.** `SRTCs + 0x18` is `transPatternFlag`: bits
7:4 the previous pattern index, bits 3:0 the current one. `pc=0x005C` loads
`particleProperties[v15]` at **record offset `0x28`** — the record's own
`transPatternFlag` — and `v_cmpx_eq_u32` at `pc=0x00B8` retires every lane
whose nibble differs. One dispatch processes one pattern.

**Correction.** An earlier revision of this file called record dword 0 a
"category tag". It is `pos.x`. The nibble compared at `pc=0x00B8` comes from
record offset `0x28`, which the MUBUF instruction supplies as an immediate;
reading only the decoder's register operands hid it.

## The dispatch writes

`particle_c` now executes on an Apple M4 and **mutates the particle buffer**.

The blocker was a host bug, and a non-obvious one: the translator injects a
`dispatchThreadLimit` **push constant** (`OpTypeStruct %v3uint`, 12 bytes).
Vulkan dispatches whole workgroups, so the command path has to supply the exact
exclusive thread bound. The runner declared no push-constant range, leaving it
zero, and every thread in the dispatch was masked off. Declaring the range and
issuing `vkCmdPushConstants` with `(count, 1, 1)` before `vkCmdDispatch` turned
0 changed bytes into non-zero output.

Ruled out along the way, each by test rather than by argument:

| suspect | result |
|---|---|
| runner correctness | control shader, same layout, wrote 16,313 bytes |
| descriptor layout | SPIR-V declares one `guestBuffers` array, set 0 binding 0 |
| wave32 vs wave64 | both compile (1.48 MB / 1.61 MB), neither was the cause |
| category tag | swept 0,1,2,3,5,8 — no effect on write volume |
| user-SGPR count | `uint[4]` vs `uint[32]` produced byte-identical SPIR-V |

## The simulation runs

`particle_c` now spawns and integrates Sony's own particles on an Apple M4,
from a zeroed bank, driven by the console's authored parameters. See
[`particle-live-simulation.md`](particle-live-simulation.md) for the recovered
`ResourcesCs`/`ParticleProperty` layouts, the record bank allocator, the
pattern-blob constants, and the two host bugs that had to be fixed first.

## What is still not proven

- The **binding** of the shader's inline literals to named parameters. The
  values are certain and their pc order constrains the algorithm, but calling a
  specific float `particleMinLife` from its position in the instruction stream
  would be naming by proximity. The names in
  [`particle-live-simulation.md`](particle-live-simulation.md) come from the
  reflection string table and the load offsets instead, which is a different
  and sound derivation.
- The particles are simulated but **not drawn**. No pixel has been produced
  from them.
- `fw_flow_vl`'s decode gap.
