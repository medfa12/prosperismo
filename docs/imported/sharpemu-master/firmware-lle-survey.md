<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Can Sony's own modules run under Windows?

Measured 2026-07-28 across `games/PS5_4.03_reconstructed`,
`games/PS5_9.00_decrypted` and `games/3.02`.

## The test

Prospero is x86-64 and so is the host, so a firmware module body is already
native code. Exactly one instruction breaks that equivalence: `syscall`. In
user mode on Windows it traps into the Windows kernel with whatever service
number sits in RAX, so a FreeBSD number is not merely wrong, it is
unpredictable. A module carrying one cannot be executed as-is.

The scan counts the `0F 05` byte pair across `PF_X` `PT_LOAD` segments. It
over-approximates, so a measured zero is a sound proof of absence.

## Result

```
modules scanned      : 1737
  not ELF (encrypted):   34
  ZERO syscall sites : 1207
  has syscall sites  :  496
```

70% are syscall-free, and the offenders are concentrated where you would
expect. `libkernel_sys.sprx` has 455, `libkernel.sprx` 352, `libkernel_web.sprx`
342. That is the layer we already implement ourselves, so the modules that
cannot run native are precisely the ones we were never going to run native.

The libraries that matter for the current work are clean:

| module | bytes | verdict |
|---|---:|---|
| `libSceAgc` | 274,256 | RUNNABLE |
| `libSceAgcDriver` | 138,496 | RUNNABLE |
| `libSceGnmDriver` | 114,800 | RUNNABLE |
| `libSceVideoOut` | 206,960 | RUNNABLE |
| `libSceAjm` | 72,072 | RUNNABLE |
| `libSceUlt` | 259,608 | RUNNABLE |
| `libSceAudioOut` | 430,336 | 2 sites |
| `libSceFios2` | 513,112 | 3 sites |
| `libSceSysmodule` | 219,024 | 1 site |

The near-misses carry one to three sites, few enough to trap individually
rather than abandon the module.

## This is not a prediction, it already happens

The firmware differential oracle executes real 4.03 `libSceAgc` bodies in
process on the direct-execution backend and compares them against our HLE:
14 scored cases, 0 divergences. Sony's graphics code has been running under
Windows on this machine for days. What does not exist is the loader work to
make a *title* reach it instead of our C# exports.

## What it does not buy

Astro Bot never requests a firmware path: booting it with all three dumps
mounted and searched produced **zero** firmware hits, and the identical
milestones and identical device loss. The filesystem half of "supply the
firmware" is inert for that title, because it is self-contained under /app0
and its two real module bodies, `libc.prx` and `libSceNpCppWebApi.prx`, ship
inside the game dump.

Nor would LLE touch the current blocker. The run ends because one draw takes
6,845 ms and Windows resets the GPU, and that shader was translated by us, not
by Sony. Replacing our `libSceAgc` with theirs changes who *builds* the command
buffer, not who compiles the shader in it.

## Honest read

LLE for the graphics and audio libraries is viable, evidenced, and a large
piece of loader work whose payoff is correctness across many titles rather
than progress on this one. It is a real option, not a shortcut.
