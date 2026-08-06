<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Reading the background scene out of the eboot

The 12.40 shell eboot is a **plain ELF64/x86-64 image**. No key material, no
console and no firmware dump beyond what is already on disk is needed to read
the scene renderer — this document records how, and what the first pass found.

Tool: [`tools/shell-recovery/eboot_xref.py`](../../tools/shell-recovery/eboot_xref.py)

## Image layout (12.40 `NPXS40087/eboot.bin`)

| Segment | File offset | Virtual address | Size | Perm |
|---|---|---|---|---|
| text | `0x00004000` | `0x00000000` | `0x00D08DBC` | `--X` |
| rodata | `0x00D10000` | `0x00D0C000` | `0x00429580` | `R--` |
| data | `0x0113C000` | `0x01138000` | `0x0009B4A0` | `RW-` |
| data | `0x011D8000` | `0x011D4000` | `0x001A3768` | `RW-` |
| CLI metadata | `0x0137DD50` | `0x02D05D50` | `0x00132CF8` | `---` |

So for the first two segments `VA = file_offset - 0x4000`.

## Finding code by the strings it uses

x86-64 reaches rodata through RIP-relative `lea`, so a reference to a string at
virtual address `V` is `lea reg, [rip+disp32]` with
`V == next_instruction_address + disp32`. Scanning for that encoding across the
text segment costs one pass and needs no correct instruction boundaries:
**70,760 LEAs resolving to 25,637 distinct targets**.

Confirmed cross-references:

| String | Referenced from |
|---|---|
| `effect_light_shaft` | `0x1E0D14`, `0x1E19BE` |
| `FwLsShader` | `0xCB1A5`, `0xCB208` |
| `IBLGlobalSpace` | `0x1E9BEC`, `0x1E9F87`, `0x1EBD40`, `0x1ED019` |
| `TextureIBLDiffuse` | `0x1A3D68` |
| `LightController` | `0x1C9A08` |
| `SamplerColorRamp` | `0x1ED549` |

**Names ending in `Native` have no such reference** — `CreateLightShaftModel`,
`CreateBasicModel`, `DrawModelNative`, `StartModelSequenceNative`. They resolve
into the `---` segment at `0x2D05D50`, which is embedded CLI metadata. These are
therefore **managed method names bound at runtime by the Mono host**, not
entries in a static table, so they must be found through the binding
registration rather than by cross-reference.

## The light shaft is a lazily-constructed singleton

`0x1E1990` is a textbook function-local static:

```
0x1E1990  movzx eax, byte [rip+...]   ; guard byte at 0x1383B10
0x1E1999  je    0x1E19A3              ; already built -> return
0x1E199B  lea   rax, [rip+...]        ; the object at 0x1383B08
0x1E19A2  ret
0x1E19A7  lea   rdi, guard
0x1E19AE  call  0xCFDF90              ; __cxa_guard_acquire
0x1E19B7  lea   rdi, object
0x1E19BE  lea   rsi, "effect_light_shaft"
0x1E19C5  call  0x16C980              ; construct
0x1E19D1  call  0xCFDFA0              ; __cxa_guard_release
```

Its constructor is `0x1E0CE0`: it installs a vtable (`0x1143050`), names the
effect `effect_light_shaft`, then calls `0x1E0E90` with the name — the effect
lookup/bind. The light shaft is one named effect object, created once and
reused, which matches it being a persistent model in the scene rather than a
per-frame post pass.

## Sampler bindings

`0x1ED505`–`0x1ED56D` assembles a descriptor array of three samplers, each as a
`{pointer, size}` pair, then registers the group through `0x181B70`:

```
SamplerColor        (resolved via 0x179790)
SamplerDiffuse      (resolved via 0x1CEB70)
SamplerColorRamp    (resolved via 0x1CEFD0)
```

`SamplerColorRamp` is the entry that consumes `shutdown_ramp.gnf`, and
`SamplerDiffuse` the one that consumes `diffuse_default.gnf` — which confirms
those two missing textures are **material inputs**, not geometry.

## FirstWave shader modules

`FwLsShader` and `FwHsShader` are not shader *names* — they are source module
names passed to a `FW_ASSERT` macro whose format string sits at `0xF41F6E`:

```
%s:%d FW_ASSERT failed
```

The referencing sites pass line numbers `0xAB` (171) and `0xB2` (178). Nearby
strings `FirstWave::Finalize()` and `[BGLog] %s : GPU load %u usec` confirm the
`FirstWave` namespace. So `Fw*Shader` are compilation units of the FirstWave
renderer, and naming a shader after them would be exactly the
proximity-guessing this project has already corrected once.

## Render vocabulary recovered near the effect

Sampler and state names clustered around `effect_light_shaft`:

`Sampler`, `lightController`, `AmbientColor`, `kDepthCompareGreaterEqual`,
`kAnisotropyRatio2`, `kBlendMultiplierOne`, `kFilterModeAnisoBilinear`,
`kWrapModeClampBorder`, `kStencilOpOnes`, `ZeroAlpha`, `Barrier::kToRwTexture`,
`Xf::BeginningRender`, `SetupMeshSizeBuffer`, `validateViewportSize`.

Pixel/vertex programs named in the same region: `basic_p`, `particle_p`,
`large_particle_vv`, `average_gauss_nxn_p`, `vr2_dist_stereo_p`,
`vr2_mirror_stereo_p`, `caesar_playarea_ui_p`, and the asset
`shaders/effects/effect_post_process_copy_p.ags`.

Also present: `LoadFreeForm`, `Loading`, `STPTRI`, `asset not found `. `STPTRI`
is likely a primitive/topology tag but is **not** identified here.

## Not yet established

The geometry itself. `CreateLightShaftModel` and `CreateBasicModel` are managed
entry points, so the next step is locating the Mono internal-call registration
that binds them to native code, and reading the model construction from there.
Whether `LoadFreeForm` loads geometry from an asset or builds it procedurally is
still open — it is the question that decides whether the room can be rebuilt
from code alone.
