<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Methodology: rendering the PS5 shell by executing Sony's own programs

This is the working method behind
[`particle-live-simulation.md`](particle-live-simulation.md) and
[`particle-draw-executed.md`](particle-draw-executed.md), which together
produced the first real frames of the PS5 animated background on macOS. It is
written as a procedure so the remaining passes can be done the same way.

**The rule: no reconstruction.** A pass is done when the console's own
instruction stream runs and produces the pixels. Porting the maths, fitting
constants to a video, or re-deriving an algorithm from its published origin all
fail the bar — even when the output looks right.

## 1. Read the writers, not the data

Scanning firmware for a constant *as a value* failed five times across
sessions. Disassembling the **function that writes** it, and resolving
RIP-relative operands, produced a result every time.

```python
import capstone
buf = open('ps5oracle/fwdb/12.40/NPXS40087-eboot.bin', 'rb').read()
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for ins in md.disasm(buf[ADDR:ADDR + 0x300], ADDR):     # VA = file_offset - 0x4000
    if 'rip + ' in ins.op_str:
        d = int(ins.op_str.split('rip + ')[1].split(']')[0], 16)
        target = ins.address + ins.size + d              # <- the constant
```

Three things this found: the render resolution, the billboard quad table, and
the particle-bank allocator with its PRNG seed.

## 2. Constants live in three places, and only one is the CPU

- **Inline in the shader's instruction stream.** The `particle_c` tuning values
  were never findable CPU-side because they are literal operands. `--dump-stage`
  prints them.
- **In authored, serialized blobs.** The per-pattern parameters — counts,
  lifetimes, spawn boxes, curl values, rendezvous points — are keyframed data
  loaded by a parser, not code. Find the loader, not the values.
- **In CPU code.** Only the structural things: allocation sizes, strides, seeds.

If a value is not in the code, look for the loader that reads it.

## 3. Recover struct layouts from two independent directions

Never name a field by proximity. Do it twice and require agreement:

- **From the shader**, by decoding its own `s_load` / `buffer_load` offsets and
  widths. This gives exact offsets with no names.
- **From reflection**, by finding the PSSL name table (for `BackgroundLayer` it
  is at `0x1126160` in 12.40). This gives exact names in declaration order with
  no offsets.

Zip them. For `ParticleProperty` the two accounted for `0x44` bytes with no
padding and no leftovers, which is itself the proof. Where a third source
exists — the donor decoder's field map — check it agrees offset-for-offset
before trusting any of them.

Widths matter: an `s_load_dwordx4` is not necessarily a descriptor. Treating
`ResourcesCs + 0x28` as a buffer V# instead of four scalars silently retired
every dispatch.

## 4. Execute, then instrument, then bisect

The pipeline is: eboot → `Gen5ShaderTranslator` decode → `Gen5SpirvTranslator`
→ Vulkan on MoltenVK. When it produces nothing, do **not** reason about the
maths. Get a number out of the GPU.

Ladder that worked, cheapest first:

1. **Substitute a trivial stage.** A constant-colour fragment shader, or a
   fullscreen-triangle vertex shader, separates "my Vulkan pass is wrong" from
   "the guest program is wrong". (`DEBUG_VS`, `DEBUG_FS`.)
2. **Watch the guest's own side effects.** `particle_vv` stores `renLife` back
   to the record; a zero-byte diff on that buffer proved the program was dying
   before it got there. (`TRACE_VS`, `TRACE_SIM`.)
3. **Change topology.** Rendering points instead of triangles distinguishes "no
   position written" from "degenerate triangles".
4. **Patch the SPIR-V.** `spirv-dis`, edit, `spirv-as`. Replacing the position
   with a constant proved the export block was reached; then writing the four
   clip components into a storage buffer gave the actual numbers, which named
   the bug in one step. (`tools/probe_clip.sh`.)

Every failure in this work was a *host contract* error or a *translator* bug,
never a misunderstanding of Sony's algorithm. Instrument accordingly.

## 5. Host contracts that are easy to get wrong

Each of these produced a plausible-looking wrong result rather than an error:

| Symptom | Cause |
|---|---|
| Every thread masked, zero bytes written | `dispatchThreadLimit` push constant undeclared |
| All records collapse onto record 0 | buffer V# written with `stride = 0` (`idxen` MUBUF multiplies by it) |
| Every workgroup writes records 0–63 | `ComputeSystemRegisters` null, so the workgroup-id SGPR is a static zero |
| Constant buffer's bytes land in the resource block | bindings identified by size instead of `BaseAddress` |
| A resource field reads as zero | binding truncated below the offset the shader reads (`cameraZ` at `+0x138` in a 256-byte binding) |
| All six quad corners coincide | a table embedded in the program image, reached by `s_getpc_b64`, uploaded as zeros |
| Geometry perfect, every pixel black | two stages given separate copies of one guest allocation, losing a cross-stage write |

## 6. When the translator is wrong, fix the translator

`Gen5SpirvTranslator` applied SDWA `ABS`/`NEG` as an arithmetic negate on the
integer source path. They are floating-point modifiers: the hardware flips the
sign bit of the selected sub-dword whatever opcode consumes it. `particle_vv`
uses an SDWA `v_mov_b32` with `src0_neg` to build its clip-space `z`, so the
whole field landed behind the near plane.

Two signals that it is the translator and not the setup: the ISA notes already
flagged those encodings as ones `llvm-mc` mis-decodes, and the instrumented
clip values were *coherent but sign-flipped*. Working around it in the host
would have been reconstruction.

## 7. Reference material has ranks

- **Firmware bytes** — the only thing that settles a question.
- **Reflection tables and serialized blobs** in the firmware — data, not code,
  but still Sony's.
- **Prior decode notes** (`docs/sony-shell/*.md`, the donor decoder in
  `ps5oracle/`) — high value, and they carry their own uncertainty labels.
  Re-verify against 12.40 before relying on them; both the pattern-blob format
  and the `ResourcesCs` field map were confirmed that way.
- **Video captures** — *only* for knowing what success looks like. They never
  supply a number.
  [`reference-video-grading.md`](reference-video-grading.md) §0 establishes that
  `ps5oracle/shell_ui/live_background/default.mp4` is not even a firmware asset.

## 8. State what is assumed, in the same breath as the result

The particle render is Sony's code and Sony's parameters, with **one** exception:
`SRTVsPs + 0x14` selects which half of the palette is used, it is host-written,
and it has never been read out of the firmware. The render writes zero, which
takes the warm path. That belongs next to the picture, not in a footnote.
