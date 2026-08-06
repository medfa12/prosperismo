<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Particle simulation: the 12.40 address map

[`bglayer-background-spec.md`](bglayer-background-spec.md) §3 identifies the one
block still missing from the animated background — the `particle_c` simulation
parameters — and gives its location in **4.03**. That firmware is not on disk.
This re-derives the same map in **12.40**, which is.

Every address below is a virtual address in
`ps5oracle/fwdb/12.40/NPXS40087-eboot.bin` (`VA = file_offset - 0x4000`).

## The map

| Item | 12.40 | 4.03 (per spec) |
|---|---|---|
| `simulateParticles` entry | **`0xE24F0`** | `0x96640` |
| Its assert sites | `0xE26C4`, `0xE26D9` | `0x9680d`, `0x96829` |
| Assert source lines | `0x2BE` (702), `0x2C1` (705) | `0x2c4`, `0x2c7` |
| `"simulateParticles"` string | `0xF8F39E` | `0xb2fb5e` |
| Driver that walks the systems | **`0xE2700`** | `0x96860` |
| `ResourcesCs` copy | `0xE2595`–`0xE25FB` | `0x966e5`–`0x96746` |
| Eight-system pointer array | `ctx + inst*320 + `**`0x1A0`** | `+0x198` |
| Two singleton systems | `ctx + inst*80 + `**`0x5E0`** / **`0x5E8`** | `+0x5d8` / `+0x5e0` |

The two structures differ by a uniform **+8 bytes** at 12.40, consistent with one
pointer being added ahead of them. Everything else matches the spec exactly,
including the eight `vmovups ymm` pairs covering `+0x00..+0xF8` and the loop
bounds (`r13` from `0x34` to `0x3C` = 8 iterations, `r14 = ctx + r15*320`,
`r12 = r15*80`).

## One thing the spec did not state

`simulateParticles` does `mov rbx, rsi` at `0xE2527`, and the `ymm` copy then
reads `[rbx + 0x00 .. 0xF8]`. So **`ResourcesCs` sits at offset 0 of the particle
system object** — the system pointer *is* the block. The systems are therefore
whatever the pointer arrays hold, and the constants are written by whatever
allocates them.

The guard at `0xE2516` is `cmp dword ptr [rsi + 0x28], 0`, so the particle count
lives at `+0x28` **within** `ResourcesCs`, and a zero-count system is skipped
before any dispatch.

## Registration sites located

Two stores install the singleton systems:

```
0xF8F24   mov qword ptr [r12 + 0x5e0], rbx
0xF9DC6   mov qword ptr [rbx + 0x5e8], rdi
```

These are the entry points for walking up to the constructor.

## Still not recovered

**The constants themselves.** The constructor that fills a `ResourcesCs` has not
been found. Two approaches were tried and failed, recorded so they are not
repeated:

- Scanning the text for clusters dense in RIP-relative `vmovss` (1,866 across
  569 buckets). The densest clusters near the BGLayer code — `0xFBC00`,
  `0x121400`, `0x124000`, `0x13E800` — write only a handful of floats each
  (values 1.0, 0.6, 0.454545), none into a `0xF8` layout. The constructor is
  not among them.
- Disassembling backward from `0xF8F24`. The function-start heuristic
  (`ret` followed by padding) does not resolve there, and a fixed
  `0xF8A00`–`0xF8F60` window contains one float store. The allocation is
  further up, likely behind a vtable dispatch.

The next step is to follow `r12` at `0xF8F24` and `rbx` at `0xF9DC6` back to
their definitions rather than scanning by pattern.

## Ruled out: the `+0x5E0` store at `0xF8F24` is not the particle system

The registration sites named above were followed and **do not lead to the
particle constants**. Recorded so the path is not walked again.

`0xF8F24` stores into `[r12 + 0x5e0]` after allocating `0x28` bytes — 40, not
the `0xF8` a `ResourcesCs` needs. That was the first sign the identification was
wrong. Following it further:

```
0xF8EF0   mov  edi, 0x28        ; allocate 40 bytes
0xF8EF5   call 0xCFDD80         ; operator new
0xF8EFD   call 0x150EA0         ; -> returns global [0x1380CC0]
0xF8F05   call 0x150EC0         ; -> returns global [0x1380CC8]
0xF8F1F   call 0x14E390         ; construct
0xF8F24   mov  [r12 + 0x5e0], rbx
```

`0x14E390` is a **span constructor**, not a particle constructor: it zeroes
`[rdi..rdi+0x18]` then writes `[rdi+0x18] = start` and `[rdi+0x20] = start+size`.
A begin/end pair.

The two globals it spans are written by `0x14FF10`, which reads `0x1380CB0` /
`0x1380CB8`, calls `0xCFF680` and `0xCFF690` for a base and a size, then passes
them to `0xD00700` alongside the constants `0x2000000` (32 MB) and `0x3FFFFFF`
(64 MB). That is **direct-memory pool setup for the GPU allocator**, and the
object at `+0x5E0` is an allocator handle over that pool.

### Why the identification was wrong

The driver at `0xE2700` reads its singletons from `ctx + inst*80 + 0x5E0`, where
`ctx` is its own first argument. The store at `0xF8F24` uses `r12`, which is a
**different object** that merely has a field at the same offset. The two were
matched on the constant `0x5e0` alone.

### The correct next step

Do not search by offset constant. Start from the driver's `ctx` — the value in
`rdi` at `0xE2700`, reached from its caller at `0xE3177` — and find where *that*
object's slots at `+0x1A0..+0x1D8` (the eight-system array) are populated. The
clears at `0xEFEE3` and `0xF6550` write zero into a `+0x1a0` field and are
candidates for being in the owning class, but this has not been confirmed
either, and confirming it means checking the base register, not the offset.
