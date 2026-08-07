<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Handoff: render the background by executing Sony's shaders

**Goal:** render the PS5 background by *executing* the firmware's own shaders on
macOS. Not reimplementing, not approximating, not measuring from video.

**Target sequence:** `GlobalBackgroundState.Login = 8` (the login sequence).
**Target resolution: 3840×2160**, not 1080p — see below.

## The method that works

Read the **functions that write** the constants, with capstone, resolving
RIP-relative operands. Do not scan for values.

Scanning for constants as data failed five times across earlier sessions.
Disassembling writers produced a real result three times in a row within
minutes. Start here every time.

```python
import capstone, struct
buf = open('ps5oracle/fwdb/12.40/NPXS40087-eboot.bin','rb').read()
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for ins in md.disasm(buf[ADDR:ADDR+0x300], ADDR):
    if 'rip + ' in ins.op_str:
        d = int(ins.op_str.split('rip + ')[1].split(']')[0], 16)
        target = ins.address + ins.size + d          # <- the constant
```

The tuning constants that were never findable CPU-side are **literal operands
inside the shaders**. `--dump-stage` prints them.

## What works end to end

eboot → `Gen5ShaderTranslator` decode → SPIR-V → execute on Apple M4 via
MoltenVK. Proven for **both** a compute and a pixel shader.

```
export DYLD_LIBRARY_PATH=/opt/homebrew/lib
export VK_ICD_FILENAMES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json
P=frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc
E=ps5oracle/fwdb/12.40/NPXS40087-eboot.bin

dotnet run --project $P -c Release -- --eboot $E --firstwave          # 14/15 decode
dotnet run --project $P -c Release -- --eboot $E --dump-stage particle_c
dotnet run --project $P -c Release -- --eboot $E --compile-particle   # dispatches
FULLSCREEN_VS=<scratch>/fullscreen.vert.spv \
dotnet run --project $P -c Release -- --eboot $E --render-plate --out-png out.png
```

New code: `FirstWaveProbe.cs` (decode/dump/compile/render modes),
`ParticleComputeRunner.cs` (compute dispatch **and** a fragment render pass —
the existing `Ps5ParticleVulkanSession` is graphics-only and
`RenderOpaqueFrames` hardcodes the ripple ABI, so neither could carry these).

### Two host traps that cost hours

1. **`dispatchThreadLimit` push constant** (`OpTypeStruct %v3uint`, 12 bytes).
   The translator injects it; Vulkan dispatches whole workgroups so the host
   must supply the exclusive bound. Undeclared → zero → **every thread masked,
   silently**. Declare the range and `vkCmdPushConstants` before dispatch.
2. **Identify buffers by `Gen5GlobalMemoryBinding.BaseAddress`**, never by size.
   Two blocks are both 256 bytes; matching by size put the constant buffer's
   bytes where the dispatch bounds belonged.

## Shader locations (12.40 eboot)

Found by scanning for the `s_inst_prefetch` prologue `0xBFA00003` and profiling
opcode mix. 19 programs in `0x11F0000–0x1200000`.

| offset | len | instr | what |
|---|---|---|---|
| `0x11FA100` | `0x71A4` | 5,018 | **`particle_c`** — sim; no MIMG, 26 MUBUF |
| `0x11F2E00` | `0x510` | 232 | **`large_particle_p`** — 2 `image_sample` |
| `0x11F0400/1500/2600` | | 673/687/262 | other particle draws |
| `0x11F9300` | `0x230` | 90 | `fw_background_p` — flat fill plate |
| `0x11F6900` | `0x72C` | — | **`fw_flow_vl` — DOES NOT DECODE** |

`fw_flow_vl` has no `s_endpgm` inside its contract length and desyncs on
`unknown-vop2 op=0x00` past it. Unresolved; blocks the wave surface.

## Recovered facts

- **Render resolution 3840×2160** — from `(3839.5, 2159.5, 0.5, 0.5)` at
  `0x00EFEE50` (`2×1920−0.5`, `2×1080−0.5`). Independently corroborated by the
  blur tap offsets `±k/3840`.
- **Particle record is 68 bytes**; dword 0's low nibble is a **category tag**
  gated against `cbuffer[+0x18]` at `pc=0x00B8`.
- **`ResourcesCs + 0x28` is `{count, size, base, stride}`**, not a descriptor.
- **`particle_c` constants** (literals in the stream): Park–Miller PRNG
  (`16807`, `2^31−1`), the complete Ashima 4D simplex set (`F4=0.309017`,
  `G4=0.1381966`, `mod289`, `34`, `taylorInvSqrt`), and that whole block a
  **second time with every spatial constant +20** — two decorrelated fields
  forming the curl vector field.
- **Billboard quad at `0xE2700`** — 32-byte stride table, `xmm0 = −xmm3` via
  `vxorps` with `0x80000000`:

  | offset | value |
  |---|---|
  | `+0x10` | `{−v, +v, (0,1,−1,−1)}` |
  | `+0x30` | `{−v, −v, (0,1, 1,−1)}` |
  | `+0x50` | `{+v, −v, (0,1, 1, 1)}` |
  | `+0x70` | `{+v, +v, (0,1,−1, 1)}` |
  | `+0x90` | repeats `+0x10` |

  Four corners closing a quad — matches `particle-draw.md`'s independent
  "six vertices, one inline quad". `v` is the half-size (`wide`).

## Corrections this session forced

- `0xE2700` is the **draw**/billboard path, not the simulation driver as
  earlier notes claim.
- `particle-system.md` says per-particle gain is `[0.5,1.0)`. The literals are
  `0.2`/`0.8` → **`[0.2,1.0)`**.
- `particle_c` is a *simulation* and renders nothing. Chasing it for an image
  was wrong; the pixel shaders make pixels.

> **Status: the particle field renders.** See
> [`particle-draw-executed.md`](particle-draw-executed.md) and
> [`METHODOLOGY-executing-sony-shaders.md`](METHODOLOGY-executing-sony-shaders.md).
> What remains is the layer *behind* the particles — the FirstWave light rays.

## Next step

The simulation is **done** — see
[`particle-live-simulation.md`](particle-live-simulation.md). `particle_c`
spawns and integrates 1,600 particles from a zeroed bank using `coldboot`'s
authored parameters, and every spawn bound in the output matches the block.

What remains is the **draw**: `large_particle_p` (`0x11F2E00`), the billboard
quad table at `0xE2700`, and the `Particle0/1.gnf` sprites, through the
fragment path in `ParticleComputeRunner`. Then re-render at 3840×2160.

## Do not repeat

- Do not render `fw_background_p` alone and present it as the background. It is
  a flat fill. Sony's own design story: the background is "particles of light".
- Do not name a recovered constant by proximity.
- Do not measure from `default.mp4`.
