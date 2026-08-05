<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PoC: PS5 background shaders execute on macOS

**Result: proven.** A shader taken out of a genuine PS5 firmware eboot is
decoded, translated to SPIR-V, and executed on this Mac's GPU. No
reimplementation is involved — the code that runs is the console's own.

Run it:

```
DYLD_LIBRARY_PATH=/opt/homebrew/lib \
VK_ICD_FILENAMES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json \
dotnet run --project frontend/ProsperismoShell/Prosperismo.Shell.BackgroundPoc -c Release -- \
  --eboot ps5oracle/fwdb/3.00/NPXS40087-eboot.bin \
  --offset C751A0 --length 1C90 --out poc-out --frames 12
```

Output:

```
translate: OK - 208,312 bytes of SPIR-V
Vulkan device: Apple M4
Vulkan subgroup size: 32
driver accepted the recovered firmware graphics pipeline
render   : OK - 12 frame(s) at 960x540
distinct : 12 unique frame(s)  (ANIMATED)
```

## What the chain is

1. **Slice** an AMDGPU shader ELF out of `NPXS40087/eboot.bin`.
2. **Decode** PS5 GCN with `Gen5ShaderTranslator`, checking the ripple ABI
   (`cbuffer[40,160]`, textures at `s0`/`s8`).
3. **Emit** SPIR-V.
4. **Execute** through Vulkan on **MoltenVK**, on an Apple M4.

Every step is real firmware data. The SPIR-V is generated from Sony's
instruction stream, not authored.

## Two things this needed

**The baked offset is per-firmware.** `Ps5NativeRippleCompiler` hardcoded
`0xD8A070`/`0x1BB8`, which is the donor's 4.03 build. Other versions place the
shader elsewhere, so `TryCompile` now takes an explicit slice, and the PoC has
a `--scan` mode that walks every AMDGPU ELF in an eboot and reports which ones
the translator accepts. In the 3.00 eboot (137 shader ELFs) exactly **three**
match the ripple ABI: `0xC751A0`, `0xC78BB0`, `0xC79E30`.

**macOS needs portability enumeration.** MoltenVK is not a conformant Vulkan
driver; it advertises itself as a portability implementation. Without
`VK_KHR_portability_enumeration` and
`InstanceCreateFlags.EnumeratePortabilityBitKhr`, `vkCreateInstance` fails with
`ErrorIncompatibleDriver` even though MoltenVK is installed. That flag is now
set on macOS in `Ps5ParticleVulkanBackend`.

## What is NOT proven

The rendered frames **animate but are flat** — a uniform level that changes per
frame, not the console's wave. That is expected and worth stating plainly:

- A `--sweep` over the 40-byte constant buffer shows only **slot 2** changes
  the image. The other nine slots do nothing observable.
- The shader's real uniform values are **not recovered**. This is the same gap
  [`bglayer-shaders.md`](bglayer-shaders.md) §2.4 records: `uCoff0`, `uCoff1`,
  `uLightColor`, `uLightPos` and `uCenterPos` are filled by native code that is
  not present in the dump, and no literal block matching a filled-in
  `UniformData` exists in the image.
- The 160-byte second constant buffer and the source/target textures are also
  fed placeholder data here.

So the PoC establishes **the pipeline**, not the picture. Feeding it correct
uniforms is the remaining work, and that is a recovery problem, not a macOS
one.

## Why this matters

Before this, the recovered background maths lived in reimplementations —
TypeScript, then C++ — which can only ever be as right as the recovery.
This shows the console's actual shader binary can be run on an Apple GPU, so
the background can eventually be produced by *executing Sony's code* rather
than approximating it. The remaining unknowns are constant values, which are
findable, rather than platform capability, which is now settled.
