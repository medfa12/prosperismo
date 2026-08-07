<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background particle field, rendered

`particle_c` → `particle_vv` → `particle_p`, all three executed from the 12.40
eboot's own instruction stream on an Apple M4 through MoltenVK. The parameters
come from Sony's serialized pattern blobs. Nothing in the image is modelled.

```
python3 tools/export_particle_frames.py --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin \
  --out frames --selector 1 --start 0 --fps 30 --frames 450

export DYLD_LIBRARY_PATH=/opt/homebrew/lib
export VK_ICD_FILENAMES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin \
  --render-particles --blocks frames --out png --fps 30
```

## Which pattern is the background

The seven serialized blobs are not interchangeable. Their small-group state:

| selector | name | groups | particles | `particleOptions` |
|---|---|---|---|---|
| 0 | `coldboot` | 2, from t=6.5 | 2,000 | `0x1141` → `0x141` at t=6.6 |
| 1 | **`spread_expanded`** | **8, from t=0** | **1,820** | **`0x1131`, never cleared** |
| 2 | `spread_expanded_fadeout` | 8 | 1,820 | `0x1131` → `0x131` at t=0.2 |
| 4 | `bottom_fadeout` | 8 | 1,820 | `0x1131` → `0x131` at t=0.2 |

Bit `0x1000` is the spawn enable. **`spread_expanded` is the steady-state
background**: eight groups, spawn permanently on, so the field respawns
indefinitely. `coldboot` opens a three-frame spawn window at t=6.5 and then
lets the bank die; the `_fadeout` patterns close the window at t=0.2. A render
that wants the living background has to use selector 1.

## Program locations — a correction

An earlier revision of `FirstWaveProbe.Stages` put the draw-program names on
`0x11F0400` / `0x11F1500` / `0x11F2600` / `0x11F2E00`. That was a guess from
opcode profile and it was **wrong**. The real slices, from
[`particle-draw.md`](particle-draw.md):

| program | file offset | slice |
|---|---|---|
| `particle_p` | `0x1201500` | 1,564 B |
| `particle_vv` | `0x1201D00` | 1,276 B |
| `large_particle_p` | `0x1202400` | 1,508 B |
| `large_particle_vv` | `0x1202C00` | 1,120 B |

`particle-c-executed.md` repeated the wrong `large_particle_p` address; both are
now corrected.

## A real translator bug: SDWA ABS/NEG are float modifiers

`Gen5SpirvTranslator` applied SDWA's `src0_neg` as an **arithmetic** negate
(`0 - value`) on the integer source path. The hardware treats ABS/NEG as
floating-point modifiers — it clears or flips the sign bit of the selected
sub-dword, whatever opcode consumes it.

`particle_vv` uses exactly that: `v13 = -v13` at `+0x470`, an SDWA `v_mov_b32`
with `src0_neg`, which turns `-1.002·w + 0.1001` into the clip-space `z`.
Negating the bit pattern arithmetically put **every particle behind the near
plane**, and nothing rasterised. `particle-draw.md` had already flagged these
encodings as ones `llvm-mc` mis-decodes; the fix is in
`Gen5SpirvTranslator.Alu.cs`.

## Four host contracts the draw needs

1. **User data starts at `s8`, not `s0`.** `particle_vv` reads `s3` at
   `pc=0x0004` as the NGG *merged wave info* — vertex count in bits 7:0,
   primitive count in 15:8 — and turns it into EXEC with
   `s[126:127] = -1 >> (64 - count)`. `s[0:3]` is then overwritten at
   `pc=0x008C` with the record-buffer V# loaded from `ResourcesVsPs + 0x00`.
   The only real user data is the SRT V# at `s[8:11]`.
2. **Program-embedded tables must be served from the shader image.** Both
   stages reach data inside their own program through `s_getpc_b64`:
   `particle_vv`'s 48-byte billboard corner table at `+0x500` and
   `particle_p`'s 84-byte palette at `+0x630`. Their bindings resolve to
   addresses *inside* the program, so a host that only knows its own buffers
   uploads zeros — which collapses all six corners of every quad onto one point
   and rasterises nothing.
3. **One guest allocation is one GPU buffer.** The two stages address the record
   bank through separate descriptor slots. `particle_vv` latches `renLife` into
   the record and `particle_p` reads it back, so giving each stage its own copy
   loses the write.
4. **The `renLife` latch has to survive the frame.** `particle_p`'s life fade is
   `smoothstep(sat(2·curLife)) · smoothstep(sat(2·(renLife − curLife)))`.
   `particle_c` spawns with `renLife = -1`, `particle_vv` latches `curLife` into
   it for corner 0, and until that write is folded back into the bank the fade
   is exactly zero and every particle shades to **black** — geometry, shape and
   kill all correct, and nothing visible. Merge only the dwords the readback
   actually changed: each group writes its own record range, so copying a whole
   readback discards every other group's latch.

Also: bind the resource blocks with room to spare. `ResourcesVsPs` is `0x140`
bytes and `particle_vv` reads `cameraZ` at `+0x138`; a 256-byte binding returns
zero there, which makes `clip.w = -pos.z` — negative, so the whole field is
clipped.

## What the render is, and what it is not

The image is Sony's: the simulation, the billboard expansion, the disc profile,
the lighting accumulation and the palette are all executed firmware code, and
the tuning values are replayed from the firmware's own authored blob.

**One host-chosen input is not recovered.** `SRTVsPs + 0x14` selects which half
of the palette is used — entries 0–3 (the four bright face-button hues) when it
is non-zero, entries 4–6 (warm gold/amber/brown) when it is zero. This render
writes zero, so it takes the warm path. That is an assumption, not a recovered
fact, and the colour of the result depends on it.

The field is now **named**: the PSSL reflection table calls it
`colorPatternFlag`, sitting directly beside `transPatternFlag` — see
[`bglayer-reflection-contracts.md`](bglayer-reflection-contracts.md). That
identifies what it is without settling what the shell writes into it.

Still not rendered: the `large_particle_vv`/`_p` pair and its two GNF sprites.
They appear in `coldboot` (4 and 40 particles) but not in `spread_expanded`, so
the steady-state background is complete without them.
