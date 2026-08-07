<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Sony's particle simulation, running

`particle_c` — the 5,018-instruction compute shader behind the PS5 background —
now spawns and integrates the console's own particles on an Apple M4, from a
zeroed record bank, driven by the parameters Sony authored in the firmware.

```
export DYLD_LIBRARY_PATH=/opt/homebrew/lib
export VK_ICD_FILENAMES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json
RESOURCES_BIN=<block>.bin SIM_TIME=6.5 PROPERTY_OUT=props.bin \
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin --compile-particle
```

```
resources: particleOptions=0x1141 numParticles=1600 maxParticleId=6000
           offsetParticle=1400 stride=1 life=2..4
dispatch : OK
  buffer[2]  408,000 bytes   98,737 changed (24.2%)
             1,600 of 6,000 records have a non-zero curLife
```

1,600 is `numParticles`; records 1400–2999 is `offsetParticle` through
`offsetParticle + numParticles`. The output honours every authored bound:

| field | observed range | authored parameter |
|---|---|---|
| `pos.x` | `-0.002 … 1.997` | `particleSpawnRangeMin.x = -0.0`, `Max.x = 2` |
| `pos.y` | `-1.500 … 0.949` | `Min.y = -1.5`, `Max.y = 0.95` |
| `pos.z` | `8.001 … 9.001` | `Max.z = 8`, `Min.z = 9` |
| `curLife` | `2.002 … 3.998` | `particleMinLife = 2`, `particleMaxLife = 4` |

`fore` and `right` come out as unit vectors. Nothing in that table was fed to
the shader as an expected value; the ranges are what its own spawn path
produced from the authored block.

## `BackgroundLayer::ResourcesCs`

Names from the reflection string table at `0x1126160`; offsets and widths from
the shader's own `s_load` instructions. Neither is a guess, and they agree.

| offset | width | member |
|---|---|---|
| `0x00` | V# | `particleIds1` |
| `0x10` | V# | `particleProperties` |
| `0x20` | u32 | `particleOptions` |
| `0x24` | u32 | `randSeed` |
| `0x28` | u32 | `numParticles` |
| `0x2C` | u32 | `maxParticleId` |
| `0x30` | u32 | `offsetParticle` |
| `0x34` | u32 | `indexStridePerParticle` |
| `0x38` | f32 | `particleMinLife` |
| `0x3C` | f32 | `particleMaxLife` |
| `0x40` | f32 | `blurRadiusPowerFactor` |
| `0x44` | f32 | `blurRadiusClearEdgeThreshold` |
| `0x48` | f32×3 | `particleSpawnRangeMax` |
| `0x54` | f32×3 | `particleSpawnRangeMin` |
| `0x60` | f32 | `particleMaxAcceleration1` |
| `0x64` | f32 | `particleMaxRotationSpeed` |
| `0x68` | f32×3 | `particleCurlSizeP` |
| `0x74` | f32 | `particleCurlSpeedP` |
| `0x78` | f32 | `particleCurlTimeRateP` |
| `0x7C` | f32 | `particleCurlSpeedInit` |
| `0x88` | u32 | `numRendezVousPoints` |
| `0x8C` | 0x24 each | `particleRendezVousPoints[]` (indexed: `soffset` is a register) |

Only `0x00` and `0x10` are descriptors. Writing a V# at `0x38`/`0x48`/`0x98`,
as an earlier revision of the probe did, corrupts the lifetimes and the spawn
box.

`BackgroundLayer::SRTCs`, the buffer in `s[0:3]`:

| offset | member |
|---|---|
| `0x00` | `ResourcesCs*` (8 bytes) |
| `0x08` | `time` |
| `0x0C` | `timeStep` |
| `0x10` | `timeRateForLifeCountDown` |
| `0x14` | `isPreSimulation` |
| `0x18` | `transPatternFlag` (bits 7:4 previous pattern, 3:0 current) |

## `BackgroundLayer::ParticleProperty` — 17 floats, 68 bytes

The MUBUF immediates give the offsets; the reflection gives the names; the two
account for `0x44` bytes exactly with no padding.

| offset | member |
|---|---|
| `0x00` | `pos` (f32×3) |
| `0x0C` | `blurBoundary` |
| `0x10` | `vel` (f32×3) |
| `0x1C` | `fore` (f32×3) |
| `0x28` | `transPatternFlag` |
| `0x2C` | `right` (f32×3) |
| `0x38` | `curLife` |
| `0x3C` | `maxLife` |
| `0x40` | `renLife` |

## The record bank: 6000 records, zeroed, with a shuffled ID list

The allocator sits at `0xE02AB`–`0xE056D` in the 12.40 eboot.

- `0xE02AB` requests `0x1770 * 0x44` and calls `memset(0)`. **A fresh particle
  bank is entirely zero** — every particle is dead and the shader's own spawn
  path creates them. This is the single fact that had been missing: earlier
  runs seeded the bank with uniform random floats, which is a placeholder, not
  firmware behaviour.
- `0xE032B` and `0xE044A` each allocate `0x1770 * 4` and run an **inside-out
  Fisher–Yates** shuffle of the IDs `0..5999`.
- The draws come from the renderer-global **xorshift128+** at
  `0x1275288`/`0x1275290`, seeded `0x112210F47DE98115` / `0x7B`. The sum is
  taken 32-bit — `lea eax, [rsi + rcx]` — before the modulo. The state carries
  across both buffers, so the two permutations differ.
- The three descriptors live at object `+0x724` (properties), `+0x734` (ids1),
  `+0x744` (ids2). `particleIds1` is the **first** permutation.

`BuildParticleIds` in `FirstWaveProbe.cs` reproduces this.

## The authored constants live in pattern blobs, not in code

Seven serialized pattern blobs are embedded in the eboot. The loader at
`0xE14F0` rejects a selector above 6, indexes a name table, and passes a blob
table, a length table and a count of 7 to the parser at `0xE52F0`.

| table | 12.40 |
|---|---|
| name pointers | `0x1275210` |
| blob pointers | `0x1275250` |
| byte lengths | `0xFF18A0` |

| sel | name | blob | bytes |
|---|---|---|---|
| 0 | `coldboot` | `0xFF18E0` | `0x1FAA` |
| 1 | `spread_expanded` | `0xFF3890` | `0x1DF5` |
| 2 | `spread_expanded_fadeout` | `0xFF5690` | `0x276C` |
| 3 | `bottom_camCal` | `0xFF7E00` | `0x2856` |
| 4 | `bottom_fadeout` | `0xFFA660` | `0x2707` |
| 5 | `initboot_to_spread_no_movie` | `0xFFCD70` | `0x2960` |
| 6 | `initboot_to_bottom_no_movie` | `0xFFF6D0` | `0x3208` |

The serialization format is unchanged from 4.03: the decoder in
`ps5oracle/sharpemu/scripts/ps5_particle_patterns.py` walks all seven 12.40
blobs and lands **exactly** on each declared length, with no slack. Its
`OBJECT_OFFSETS` / compute-block field map also agrees offset-for-offset with
the `ResourcesCs` table above, which was derived here independently from the
shader. Two derivations, one answer.

`coldboot` at `t = 6.5` produces, among others:

```
small_compute[1]  particleOptions=0x1141 numParticles=1600 maxParticleId=6000
                  offsetParticle=1400 indexStridePerParticle=1
                  particleMinLife=2 particleMaxLife=4
                  blurRadiusPowerFactor=0.5 blurRadiusClearEdgeThreshold=0.95
                  particleSpawnRangeMax=(2, 0.95, 8)
                  particleSpawnRangeMin=(-0.0, -1.5, 9)
                  particleMaxAcceleration1=8.5 particleMaxRotationSpeed=4
                  particleCurlSizeP=(0.2, 0.2, 0.2)
                  particleCurlSpeedP=6.2 particleCurlTimeRateP=0.01
                  rendezVous[0].center=(0.5, -0.95, 0)
                  rendezVous[0].weight=(0, 0.7071, 0.7071) endDist=4
```

`maxParticleId = 6000` and `indexStridePerParticle = 1` corroborate the
allocator above from a completely separate source.

## Three host bugs, each of which silently produced a plausible-looking run

1. **No `stride` in the buffer V#.** Every particle access is an `idxen` MUBUF,
   so the hardware multiplies the index by the descriptor's stride. A stride of
   zero collapses all 6000 records onto record 0 — which is exactly the "~37 of
   68 bytes changed" result that had been mistaken for a shader problem. With a
   non-zero stride, `num_records` counts elements, not bytes.
2. **`ComputeSystemRegisters` left null.** `pc=0x0004` is
   `v1 = (s4 << 6) + v0`; `s4` is the first system SGPR after the four
   user-data words, i.e. the workgroup id. Leaving it unmapped pins it to a
   static zero, so all 94 workgroups write records 0–63. Pass
   `new Gen5ComputeSystemRegisters(4, null, null, null)`.
3. **`dispatchThreadLimit` push constant undeclared** (previously recorded).
   The translator injects an `OpTypeStruct %v3uint`; Vulkan dispatches whole
   workgroups, so the host must supply the exclusive bound. Undeclared → zero →
   every thread masked.

## What is not done

The particles are **simulated, not drawn**. No pixel has come out of them yet.
The draw path is `large_particle_p` (`0x11F2E00`, two `image_sample`) and the
three other particle pixel programs, with the billboard quad table at `0xE2700`
and the `Particle0/1.gnf` sprites — see
[`particle-draw.md`](particle-draw.md).

Also still open: `fw_flow_vl` does not decode, which blocks the wave surface;
and the render target should be **3840×2160**, not the 1080p the plate probe
currently uses.
