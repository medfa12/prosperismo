# GPU surface gap — RDNA2/AGC to any GPU (goal axis 2)

> **Stale generated snapshot (2026-07-31).** Do not use the counts or emitter
> classifications below as current findings. `scripts/gpu_surface_gap.py`
> currently fails its stale source anchors and its text classifier misses
> specialized production branches: it labels DS append/consume, typed MTBUF
> stores, and BVH rejected even though those paths and included tests exist.
> Current source registers 125 AGC exports; 118 overlap the 354-export
> game-facing firmware union, and all 71 catalogued Astro AGC imports have a
> registration. Registration still does not prove semantic equivalence. See
> `docs/source-alignment-audit.md` for the current status.

**Scope.** Where "the RDNA2 code the Prospero OS outputs is adapted to any GPU (NVIDIA is fine)"
actually stands in this tree, and what concretely remains.

**Reproduce every number here with one command:**

```
python3 scripts/gpu_surface_gap.py
```

It writes `docs/gpu-surface-gap.data.md` (the raw measured tables) and prints the same to stdout.
It also re-locates every `file:line` cited below by exact-substring search and **fails loudly**
(exit 1) if an anchor no longer matches — that is the signal that this document's line references
have gone stale. Numbers are tagged **EXTRACTED** (read from ground truth), **DIFFERENTIAL**
(computed by diffing two ground-truth sources) or **ASSUMED**.

Ground truth used: the decrypted 4.03 firmware (`games/PS5_4.03_reconstructed`, cleartext ELF
export tables), `scripts/astro_import_routing.tsv` (a shipped title's 1732-row import surface),
and this repository's own source. `inspiration/` was not used as authority anywhere below.

---

## 0. What the pipeline actually is today

Read the code, not the design docs. The real path is:

```
guest calls libSceAgc HLE          AgcExports.cs (112 [SysAbiExport] entries)
  -> writes PM4 packets into the guest's own DCB/ACB command buffer
guest calls sceAgcDriverSubmitDcb  AgcExports.cs (submit)
  -> ParseSubmittedDcb walks PM4 type-3 packets            AgcExports.cs:3328
     - SET_CONTEXT_REG / SET_SH_REG / SET_UCONFIG_REG      -> shadow register dictionaries
     - DRAW_* / DISPATCH_*                                 -> TryTranslateGuestDraw / ObserveComputeDispatch
     - WRITE_DATA / DMA_DATA / RELEASE_MEM / WAIT_REG_MEM  -> guest-memory + sync side effects
  -> at a draw packet (AgcExports.cs:5821):
     - read SPI_SHADER_PGM_LO/HI_ES (0xC8) and _PS (0x8)   AgcExports.cs:5828-5836
     - Gen5ShaderTranslator.TryCreateState  : fetch + decode the RDNA2 program
     - Gen5ShaderScalarEvaluator.TryEvaluate: CPU-side abstract interpretation of the SGPR
                                              code, following SRT/EUD pointer chains into
                                              guest memory to recover concrete descriptors
     - GuestGpu.Current.TryCompileVertexShader / TryCompilePixelShader
       -> Gen5SpirvTranslator -> SPIR-V 1.5 (SpirvModuleBuilder.cs:320)
     - snapshot vertex/index/uniform bytes out of guest memory
     - GuestGpu.Current.SubmitOffscreenTranslatedDraw -> VulkanVideoPresenter
```

Facts worth stating plainly:

- **Vulkan is the default backend everywhere** (`GuestGpu.cs:22-46`); `SHARPEMU_GPU_BACKEND=metal`
  opts into a *second, independent* MSL translator (`SharpEmu.ShaderCompiler.Metal`, 4.8k lines)
  that is macOS-only. Two translators means every ISA fix has to be made twice.
- **Output is SPIR-V 1.5** (`SpirvModuleBuilder.cs:320`), i.e. Vulkan 1.2 core.
- **No vendor extension is required.** Device creation asks for `VK_KHR_swapchain` plus *optional*
  `VK_KHR_maintenance8` and `VK_EXT_robustness2`, each degrading to a warning when absent
  (`VulkanVideoPresenter.cs:4222-4258`). Nothing AMD-specific. This part of the goal is met.
- **This has run on real NVIDIA hardware.** The project's own record documents the presenter
  verified on a GCP Tesla T4 and the title-boot loop running there. That is stronger evidence of
  "works on NVIDIA" than anything I can establish statically.
- **The resource model is CPU-side static recovery, not hardware-style runtime descriptors.**
  `Gen5ShaderScalarEvaluator` interprets the shader's scalar code on the CPU at draw time to work
  out which buffers/images the shader will read, then binds those as real Vulkan descriptors. This
  is the single most consequential architectural choice in the pipeline and the source of most of
  §3.

---

## 1. What works

### Command surface (measured)

| quantity | value | provenance |
|---|---:|---|
| `libSceAgc.sprx` firmware exports | 219 | EXTRACTED |
| `libSceAgcDriver.sprx` firmware exports | 135 | EXTRACTED |
| game-facing AGC surface (union of the two) | 354 | EXTRACTED |
| AGC NIDs registered in this repo | 117 | EXTRACTED |
| of those, firmware-confirmed | 110 | DIFFERENTIAL |
| firmware AGC exports with no implementation | 245 | DIFFERENTIAL |
| **AGC NIDs the shipped title actually imports** | **71** | EXTRACTED |
| of those, unimplemented | **6** | DIFFERENTIAL |
| AGC NIDs we implement that the title never calls | 48 | DIFFERENTIAL |

**The 245-entry "gap" is almost entirely irrelevant and saying so is the useful result.** A real
title touches 71 AGC entry points, and 65 of them are implemented. Chasing the other 245 would be
motion, not progress. The six that matter are listed in §2.

At the PM4 level the parser consumes the genuine AMD IT_ opcodes (`SET_CONTEXT_REG` 0x69,
`SET_SH_REG` 0x76, `SET_UCONFIG_REG` 0x79, `DRAW_INDEX_2` 0x27, `DRAW_INDEX_AUTO` 0x2D,
`DRAW_INDEX_OFFSET_2` 0x35, `DRAW_INDEX_MULTI_AUTO` 0x30, `DRAW_*_INDIRECT` 0x24/0x25,
`DISPATCH_DIRECT/INDIRECT` 0x15/0x16, `WRITE_DATA` 0x37, `DMA_DATA` 0x50, `RELEASE_MEM` 0x49,
`WAIT_REG_MEM` 0x3C, `ACQUIRE_MEM` 0x58, `EVENT_WRITE` 0x46, `SET_PREDICATION` 0x20)
alongside Sony's NOP-tagged pseudo-packets (`AgcExports.cs:37-81`). So a title that inlines real
AGC packet-building instead of calling the .prx still parses.

### Shader ISA (measured)

516 distinct RDNA2 opcode names are decoded (`Gen5ShaderTranslator.cs`, 18 encodings). Per-encoding
SPIR-V coverage, where *rejected* = falls to a `default:` that sets an error and drops the draw:

| encoding | decoded | emitted | ignored (silent) | rejected (explicit) |
|---|---:|---:|---:|---:|
| Vop1 | 35 | 32 | 0 | 3 |
| Vop2 | 42 | 39 | 0 | 3 |
| Vop3 | 61 | 57 | 0 | 4 |
| Vop3p | 8 | 8 | 0 | 0 |
| Vopc | 65 | 61 | 0 | 4 |
| Sop1 | 35 | 33 | 0 | 2 |
| Sop2 | 51 | 48 | 0 | 3 |
| Sopc | 18 | 16 | 0 | 2 |
| Sopk | 19 | 15 | 0 | 4 |
| Sopp | 35 | 9 | **26** | 0 |
| Smem / Smrd | 10 / 10 | 10 / 10 | 0 | 0 |
| Mubuf | 43 | 43 | 0 | 0 |
| Mtbuf | 8 | 4 | 0 | 4 |
| Flat (global) | 24 | 24 | 0 | 0 |
| Mimg | 33 | 33 | 0 | 0 |
| Ds (LDS) | 39 | 36 | 0 | 3 |
| Vintrp | 3 | 2 | 1 | 0 |

Genuinely strong areas, all DIFFERENTIAL from the table above:

- **Memory**: MUBUF 43/43, MIMG 33/33, global 24/24, SMEM 10/10. Typed buffer format conversion,
  sub-dword and D16 loads/stores, buffer/image atomics, `image_get_resinfo`, gather4 with depth
  compare — all present.
- **Packed f16 (VOP3P) 8/8**, with an explicit, documented refusal to use `PackHalf2x16`
  (implementation-defined subnormal/rounding) in favour of exact integer sequences
  (`Gen5SpirvTranslator.Alu.cs:1040-1053`). That is exactly the right instinct for cross-vendor
  determinism.
- **Control flow** is a structured PC-dispatch loop over basic blocks
  (`Gen5SpirvTranslator.cs:1606-1706`) rather than an attempt to recover reducible CFGs — portable
  and total. Invalid branch targets are explicit errors, not guesses.
- **EXEC mask** is modelled as a per-lane boolean plus a 64-bit wave mask, with a real wave64
  bridge for compute (`Gen5SpirvTranslator.cs:861-900, 5295-5325`).
- **Tiling**: Sony's public tiled modes 1/5/9/17/24/27 have exact 2D
  single-sample equations; Sony-reserved raw modes 4/8 are not trusted. The
  standing SDK 10.00 AgcGpuAddress tool compares complete Sony and production
  detiles over identical bytes and passes all 29 valid mode/element-size
  combinations at 257x193 and 960x540. Its bounded multi-mip mode also matches
  Sony's complete mip offsets/sizes for Minecraft's 2048x1024 four-level atlas,
  plus the first-tail level and all tail coordinates for a 256x128 nine-level
  case. The gate does not yet cover arrays, volumes, MSAA, or metadata layouts
  (`GnmTiling.cs`, `GnmTilingDetileTests.cs`).
- **Failure is loud where it is explicit.** A translation error prints one
  `[COMPAT][SHADER] ps=… es=… error=…` per distinct (pixel shader, error) even with all tracing
  off (`AgcExports.cs:10705-10711`). That is a genuinely good design decision.

---

## 2. What is missing (explicit, will not silently corrupt)

These fail loudly. They cost you draws, not correctness-you-cannot-see.

**AGC entry points the shipped title calls and we do not implement** (DIFFERENTIAL; verified absent
from `src/` by NID grep):

| NID | name | why it matters |
|---|---|---|
| `1rZSWUv1IRc` | `sceAgcDcbCopyData` | builds `COPY_DATA`; register/counter readback into memory |
| `qzMN2XKGA4k` | `sceAgcAcbCopyData` | same, async-compute queue |
| `Abendgtz+3o` | `sceAgcCbDispatchGetSize` | **size query** — the title sizes its command buffer from this |
| `bxGoVxpdSPQ` | `sceAgcCbSetShRegisterRangeDirectGetSize` | **size query**, same risk |
| `T6xuVw0KUJo` | `sceAgcDebugRaiseException` | debug-only |
| `AhGvpITrf4M` | `sceAgcDriverAgrSubmitDcb` | alternate submit entry |

The two `*GetSize` functions are the sharp ones: an unresolved size query returns whatever the
unresolved-import path returns, and the guest then allocates a command buffer from it.

**Shader ISA — 32 decoded opcodes with no emitter.** Ranked by how likely a shipping shader is to
contain them:

1. `SWaitcntVmcnt`, `SWaitcntVscnt`, `SWaitcntExpcnt`, `SWaitcntLgkmcnt` (SOPK). These are the
   GFX10 *split* waitcnt forms. LLVM emits `s_waitcnt_vscnt null, 0x0` routinely on GFX10 for
   store ordering. The plain SOPP `s_waitcnt` is already ignored as a no-op
   (`Gen5SpirvTranslator.cs:1761`) — these four should be too, but instead they reach
   `TryEmitScalarAlu`'s `default:` and **kill the whole draw**. This is the cheapest high-value fix
   in the entire document: four names added to the ignore list at `Gen5SpirvTranslator.cs:1758`.
2. `DsPermuteB32`, `DsBpermuteB32`, `DsSwizzleB32` — cross-lane shuffles. Extremely common in
   compute (reductions, wave-level scans). Map to `OpGroupNonUniformShuffle` / `ShuffleXor`.
3. `SSetpcB64`, `SSwappcB64` — shader subroutine call/return. Any shader compiled with real
   function calls (large uber-shaders, ray-tracing-style callables) is untranslatable today.
4. `VSadU8`, `VSadU16`, `VSadU32`, `VSadHiU8` — sum-of-absolute-differences; video/motion and some
   post-process passes.
5. `TBufferStoreFormatX/Xy/Xyz/Xyzw` (MTBUF stores). `TBufferLoadFormat*` works; the store side is
   simply not in the accepted-prefix list at `Gen5SpirvTranslator.cs:2436-2439`.
6. `VMovrelsB32`, `VMovreldB32`, `VMovrelsdB32` — indexed VGPR access (dynamically indexed local
   arrays).
7. `VCmpxFI32`, `VCmpxFU32`, `VCmpxTI32`, `VCmpxTU32` — the always-false/always-true VCMPX integer
   forms. The float forms are handled (`Gen5SpirvTranslator.Alu.cs:1560-1567`); the integer
   VCMPX spellings were left out of the same lists. Two-line fix.
8. `SAshrI64`, `SMulHiI32`, `SAbsdiffI32`, `SBitcmp0B64`, `SBitcmp1B64`, `VCvtPkI16I32`,
   `VCvtPkU16U32`, `VDot2cF32F16`.

**Whole pipeline stages that do not exist:**

- **Tessellation.** `sceAgcCreatePrimState` returns `ORBIS_GEN2_ERROR_INVALID_ARGUMENT` the moment
  a hull shader is passed (`AgcExports.cs:842`). LS/HS program registers are tracked
  (`AgcExports.cs:88-92`) but never compiled. Explicit refusal — good — but a title that
  tessellates cannot render.
- **Legacy GS + VS copy-shader path.** Only ES (`SPI_SHADER_PGM_LO_ES`) and PS are read at draw
  time (`AgcExports.cs:5828-5836`). `SPI_SHADER_PGM_LO_GS`/`_VS` are tracked and ignored.
- **GDS.** Explicit error, `Gen5SpirvTranslator.cs:1855`.
- **Render-target compression metadata** (DCC / HTILE / FMASK / CMASK). No decoder exists. A title
  that leaves DCC enabled on a texture it later samples will read compressed bytes.

---

## 3. What is silently wrong — the important section

Ordered by how much damage a wrong result does before anyone notices. Every line number here is
re-verified by `scripts/gpu_surface_gap.py` on each run.

### 3.1 NGG geometry amplification is dropped with no error and no log

**This is the top item.** See §4 for the full treatment. Summary: `s_sendmsg` — which is how an NGG
primitive shader issues `GS_ALLOC_REQ`, `GS_EMIT` and `GS_CUT` — is in the unconditional
accept-and-emit-nothing list at `Gen5SpirvTranslator.cs:1758-1772`, and the NGG export target 20
(PRIM, primitive connectivity) falls through the `else { return true; }` at
`Gen5SpirvTranslator.cs:4426-4431`. An amplifying shader therefore emits exactly **one** vertex per
invocation — whichever one happened to be in the export registers last — and its connectivity is
taken from the draw's index buffer instead of from the shader. No trace, no warning, no counter.

### 3.2 SMEM binding recovery zero-fills on failure, with no trace at all

`Gen5SpirvTranslator.cs:2204-2221`. When `TryResolveDominatingBufferBinding` cannot match a scalar
load to a recovered descriptor, **every scalar destination is set to `UInt(0)` and the function
returns `true`**:

```csharp
if (!TryResolveDominatingBufferBinding(instruction.Pc, scalarAddress, registerCount: …, out var bindingIndex))
{
    foreach (var destination in instruction.Destinations)
        if (destination.Kind == Gen5OperandKind.ScalarRegister)
            StoreS(destination.Value, UInt(0));      // line 2216
    return true;
}
```

The sibling handlers `TryEmitGlobalMemory` (`:2261`) and `TryEmitBufferMemory` (`:2380`) both
*error* in the same situation. Only the scalar path degrades, and unlike the CPU-side evaluator
fallbacks (which do print `[LOADER][WARN] agc.scalar_pointer_fallback`,
`Gen5ShaderScalarEvaluator.cs:2258-2300`) this one prints nothing. Constants read as zero →
black materials, identity matrices, zero light counts, and a frame that renders "successfully".

**Minimum fix:** emit a one-shot `[COMPAT][SHADER] smem_zero_fill pc=… s{n}` from that branch. Do
not change the behaviour first — make it visible first, then count how often it fires on a real
title.

### 3.3 Untiled upload: unsupported swizzle modes are uploaded as raw tiled bytes

`AgcExports.cs:9054`:

```csharp
var rgba = TryDetileTextureSource(…) ?? source.AsSpan(0, checked((int)sourceByteCount)).ToArray();
```

`TryDetileTextureSource` returns `null` whenever `GnmTiling.NeedsDetile` is false, and
`NeedsDetile` is false for every swizzle mode outside `{1,4,5,8,9,24,27}` unless `SHARPEMU_DETILE=1`
is set (`GnmTiling.cs:143-152`). The tiled bytes are then handed to Vulkan as if linear, with
`IsFallback: false` and no trace. The same `?? raw` fallback exists for storage-image seeding at
`AgcExports.cs:8838-8847`. Result: scrambled textures, indistinguishable from a shader bug.

GFX10 has many more modes than the seven trusted ones — the `_X`/`_T` pipe-XOR variants,
`SW_256B_*`, the `_D` display modes. Which ones a given title uses cannot be determined statically
from this repo.

### 3.4 Unknown guest image format silently becomes `R8G8B8A8Unorm`

`VulkanVideoPresenter.cs:10092`. An unrecognised `(dataFormat, numberType)` pair falls to
`_ => Format.R8G8B8A8Unorm`. If the real surface is 16-bit or block-compressed, the bytes are
reinterpreted at the wrong bytes-per-pixel — garbage, no log. Compare with `:1827`, `:2088`,
`:2124`, `:10132`, which all default to `Format.Undefined` (a value the caller can test). The
sampled-texture path is the outlier.

### 3.5 Unknown primitive topology silently becomes `TriangleList`

`VulkanVideoPresenter.cs:9529-9538` maps only `{1,2,3,5,6,0x11}`. Everything else — quad list (19),
quad strip (20), polygon (21), the four adjacency topologies (9-12), patch (8) — becomes
`TriangleList`. Vertices are consumed in the wrong groupings and the mesh is silently wrong.

Also note `RECT_LIST` (0x11) is mapped to `TriangleStrip` with a forced vertex count of 4
(`:9697-9700`). AMD's RECT_LIST defines a rectangle from **3** vertices with the fourth derived;
reading a 4th vertex from the buffer is a hack that happens to work for full-screen quads and is
wrong for a general rect list.

### 3.6 Blend state silently defaults

`VulkanVideoPresenter.cs:9727` (`_ => BlendFactor.One`) and `:9738` (`_ => BlendOp.Add`).
Blend factors 11/12 (`BOTH_SRC_ALPHA` / `BOTH_INV_SRC_ALPHA`) fall through to `One`. Lower impact
than the above but the same shape: an unrecognised guest value becomes a plausible wrong value.

### 3.7 Host subgroup size is queried, logged, and then ignored

`VulkanVideoPresenter.cs:4088-4139` reads `PhysicalDeviceSubgroupProperties` and
`PhysicalDeviceSubgroupSizeControlProperties` and prints them. Nothing consumes them.
Meanwhile the translator hard-codes 32 (`Gen5SpirvTranslator.cs:19 RdnaWaveLaneCount = 32`) and
masks lane ids with `& 31` (`:5186-5192`), and `VK_EXT_subgroup_size_control` is **not** in the
enabled device-extension list (`:4236-4258`), so no `requiredSubgroupSize` is pinned on any
pipeline.

- On **NVIDIA** the subgroup size is 32, so this is accidentally correct — which is exactly why it
  will stay unnoticed.
- On **Intel** (SIMD 8/16/32, driver-chosen per shader) and **AMD** (wave32 or wave64) the
  cross-lane emulation silently operates on the wrong lane grouping. `s_cbranch_execz`,
  `v_readlane`, ballots and saveexec sequences produce wrong results with no diagnostic.

The mission names Intel as a target. This is the concrete reason it is not one yet.

### 3.8 Wave64 is only emulated for compute, and only at exactly 64 threads

`Gen5SpirvTranslator.cs:357-361`:

```csharp
_waveLaneCount = waveLaneCount == 64 ? 64u : 32u;
_emulateWave64 = stage == Gen5SpirvStage.Compute
    && _waveLaneCount == 64
    && (ulong)localSizeX * localSizeY * localSizeZ == 64;
```

Two consequences:

- A wave64 compute shader whose workgroup is **not exactly 64 invocations** (256 is a very common
  choice) gets `_emulateWave64 == false` while still declaring `_waveLaneCount == 64`. Its
  cross-lane operations degrade to host-subgroup behaviour without any warning.
- **Graphics stages never receive a wave size at all.** `TryCompileVertexShader` /
  `TryCompilePixelShader` have no `waveLaneCount` parameter (`IGuestGpuBackend.cs:33-57`); only
  `TryCompileComputeShader` does (`:59-69`), fed from `DISPATCH_INITIATOR` bit 15
  (`AgcExports.cs:9608`). RDNA2 runs pixel shaders in wave64 in plenty of configurations. Any
  wave64 VS/PS is translated as wave32.

### 3.9 LDS in graphics stages becomes per-invocation Private memory

`Gen5SpirvTranslator.cs:903-936`. Compute gets a real `Workgroup` array; vertex and pixel stages
get a `Private` array because SPIR-V forbids `Workgroup` there. The comment is honest about the
assumption ("or as NGG staging whose cross-lane reads don't feed this stage's exports"), but the
assumption is not checked and the failure is silent. NGG merged ES/GS shaders stage data through
LDS *by construction*; a real cross-lane LDS read in a graphics stage reads this invocation's own
scratch instead.

### 3.10 Vertex-stage exports other than POS0 and PARAM are dropped

`Gen5SpirvTranslator.cs:4414-4432`. Only target 12 (POS0) becomes `gl_Position`; targets 13-15
(POS1-POS3) hit `if (export.Target != 12) return true;` and vanish. Those carry **clip/cull
distances, point size, `gl_Layer` and `gl_ViewportIndex`**. Layered rendering (cubemap faces,
shadow-cascade array slices, VR multiview) and user clip planes therefore do nothing, silently.

### 3.11 SOPP: 26 opcodes accepted and dropped without a word

`Gen5SpirvTranslator.cs:1826-1832` accepts the entire `Sopp`/`Smrd`/`Smem` encoding class with a
bare `return true;`. Most of the 26 are genuinely irrelevant (`s_sleep`, `s_setprio`,
`s_icache_inv`, perf counters). Two are not:

- **`SDenormMode` / `SRoundMode`** (`s_denorm_mode`, `s_round_mode`) set the FP denormal-flush and
  rounding mode for the code that follows. Dropping them means the translated shader runs in
  whatever mode the host default is. This is a real numerical-divergence source, and denormal
  handling differs between vendors.
- **`STrap`** (`s_trap`) is a guest trap the shader can take.

### 3.12 Indirect draw arguments are resolved on the CPU at PM4-parse time

`AgcExports.cs:5805-5815` reads the draw count out of `state.IndirectArgsAddress` from **guest CPU
memory** while walking the command buffer. Vertex, index and uniform data are likewise snapshotted
from guest memory. There is a barrier-driven writeback of GPU-dirtied buffers at `ACQUIRE_MEM`
(`AgcExports.cs:4189-4225`), so a well-formed guest that fences between a producing dispatch and a
consuming draw will be correct. A guest that relies on GPU-side ordering without that fence, or on
an indirect arg written later in the same submission, gets stale values with no diagnostic. I could
not determine statically whether the shipped title does this.

### 3.13 Debug scaffolding wired to one title's shader addresses

Ten sites (11 source lines) in `Gen5SpirvTranslator*.cs` test `_state.Program.Address == 0x0000000500781200ul` or
`0x0000000500780000ul` (`:394`, `:478`, `:579`, `:1259`, `:4391`, `:4449`, `:4773`, `:4849`,
`:5058`, and `Alu.cs:1648`). **All are gated behind `SHARPEMU_*` environment variables and none are
active by default**, so this is not a correctness bug. It is flagged because `docs/mission.md`'s
falsifiable test is "no title-specific flag, patch, or assert-skip" — these are exactly that shape,
and they will rot as soon as those shader addresses change.

### 3.14 Dead code that looks like working capability

`scripts/gpu_surface_gap.py` §4 verifies these are referenced *only* by their own tests:

- `NggShaderStages`, `NggSendMessage`, `NggEsGeometryClassification`
  (`src/SharpEmu.Libs/Agc/NggPrimitiveShader.cs`, 207 lines, 37 assertions in
  `NggPrimitiveShaderTests.cs`) — a complete, correct NGG classifier that **nothing in the draw
  path calls**.
- `Gfx10Detiler` (`src/SharpEmu.Libs/Agc/Gfx10Detiler.cs`, 479 lines) — superseded by `GnmTiling`
  and never called from production.

Related: `scratchpad-ngg-amplify-plan.md` at the repo root describes an NGG amplification design
against symbols that **do not exist in this tree** — `BuildNggCaptureDrawResources`,
`RecordNggCaptureDispatch`, `NoteNggAmplifyPending`, `TryEmitComputeCaptureExport`,
`NggEsAmplifying`, `ComputeCaptureSpirv` all return zero hits across `src/`, despite the document
asserting several are "confirmed present at `VulkanVideoPresenter.cs:5108`" (a line inside an
unrelated flip-capture routine). `TryAttachNggComputeCapture` *does* exist
(`AgcExports.cs:7397`) but is only a vertex-record-count helper — it attaches nothing and
dispatches nothing. Treat that plan as an unimplemented proposal, not a description of the tree.

---

## 4. NGG — the make-or-break risk, concretely

### Does the pipeline handle NGG today?

**It handles the pass-through case by accident, and it silently mistranslates the amplifying case.**

There is no NGG detection at runtime at all. The draw path never decodes `VGT_SHADER_STAGES_EN`,
never checks `PRIMGEN_EN` or `PRIMGEN_PASSTHRU_EN`, and never scans the program for `s_sendmsg`.
The code that would do all three exists and is fully unit-tested — and is unreachable (§3.14).

What actually happens on an NGG draw:

1. `TryTranslateGuestDraw` reads `SPI_SHADER_PGM_LO/HI_ES` (0xC8/0xC9) — the merged ES/GS
   primitive shader — and treats it as a plain vertex program
   (`AgcExports.cs:5828-5836`), with NGG's user-data base at s8
   (`AgcExports.cs:153 NggUserDataScalarRegisterBase = 8`).
2. It is compiled with `Gen5SpirvStage.Vertex`: one host invocation per guest vertex or index.
3. `s_sendmsg` is accepted and emits nothing (`Gen5SpirvTranslator.cs:1767`). The in-source comment
   —"exports are translated directly, so the message is moot" — is true for `GS_ALLOC_REQ` and
   false for `GS_EMIT`/`GS_CUT`.
4. `exp` to target 12 stores to `gl_Position`; targets 32-63 to varyings; **target 20 (PRIM) is
   dropped** (`:4426-4431`).
5. `VGT_SHADER_STAGES_EN` is copied into the guest's CX register block by
   `sceAgcCreatePrimState` (`AgcExports.cs:854`), so the value is *available* — it is simply never
   read back.

For a pass-through NGG shader (1 input vertex → 1 output vertex, `GS_ALLOC_REQ` only) this is
correct: a vertex shader is exactly the right lowering, and connectivity legitimately comes from
the draw's topology and index buffer.

For an amplifying shader the SPIR-V is well-formed and the draw renders — with the wrong geometry.
Each invocation's repeated `exp` sequences all `Store` into the same output variables, so only the
**last** emitted vertex survives, and the primitive count is whatever the draw packet said. There is
no counter, no trace, no `[COMPAT]` line. **This is the single most dangerous behaviour in the GPU
path**, because a title using NGG amplification for foliage, particles, hair, or decal expansion
will render a plausible-looking but wrong frame and every automated check will pass.

### What a correct implementation requires

Order of work, with the specific unknowns named:

1. **Wire up detection first (cheap, high information).** Read `VGT_SHADER_STAGES_EN` (context
   register 0x2D5) out of `state.CxRegisters` in `TryTranslateGuestDraw`, decode it with the
   existing `NggShaderStages.Decode`, scan the decoded ES program's SOPP words with
   `NggSendMessage.Decode` + `NggEsGeometryClassification.FromSendMessages`, and emit one trace
   line per distinct ES shader address:
   `agc.ngg es=0x… primgen=… passthru=… alloc_req=N emit=N cut=N`.
   This is ~30 lines against code that already exists and is tested, and it converts "we don't know
   if NGG amplification is our problem" into a measurement. **Do this before designing anything.**
2. **Fail loudly on amplifying draws** until a backend exists: set the translation error to
   `ngg-amplifying-unsupported emit=N cut=N` so the existing one-shot `[COMPAT][SHADER]` reporting
   fires and the draw is dropped rather than drawn wrong. Dropping is strictly better than
   silently-wrong: a missing object is visible, a wrong object is not.
3. **Then build a backend.** Two viable shapes, and the choice depends entirely on what step 1
   measures:
   - **Compute prepass + indirect indexed draw** (what `scratchpad-ngg-amplify-plan.md` proposes).
     Run the merged ES/GS as a compute kernel, one invocation per *input primitive*; lower
     `GS_EMIT` to an `atomicAdd` into a device-local vertex SSBO plus strip→list index emission,
     `GS_CUT` to a strip-window reset; drive the draw with `vkCmdDrawIndexedIndirect` off the
     atomic counter. Needs no device feature beyond compute + `atomicAdd` + indirect draw, so it
     works on NVIDIA, Intel, AMD and MoltenVK alike. The hard part is real and unavoidable:
     `GS_EMIT` snapshots the *current* export register values, so `exp` results must be buffered
     into SPIR-V temporaries and flushed at each `GS_EMIT` rather than written through — the export
     lowering at `Gen5SpirvTranslator.cs:4408-4470` has to be restructured for that.
   - **`VK_EXT_mesh_shader`** — the natural mapping (task+mesh ≈ ES+GS), available on all current
     NVIDIA and Intel desktop drivers. It needs a second SPIR-V execution model
     (`SetMeshOutputsEXT`, per-workgroup output arrays) and is unavailable on MoltenVK, so it
     cannot be the only path if macOS is to keep working.
   - Native Vulkan geometry shaders are not an option: there is no `ExecutionModel = Geometry`
     backend here, and MoltenVK has no geometry-shader support.
4. **Output sizing** is the remaining open problem in either shape. `VGT_GS_MAX_VERT_OUT` (0x2CE)
   and `GE_MAX_OUTPUT_PER_SUBGROUP` bound it, and neither register is read anywhere in this tree
   today (`AgcPrimaryRegisterDefaults.cs:58` only carries a default value). Worst-case allocation
   is `inputPrimCount × GS_MAX_VERT_OUT` clamped by the subgroup cap.

### Is there a correct-but-slow bypass?

Not today. The compute-prepass design *is* the correct-but-slow bypass, and it is unbuilt. The
cheapest thing that improves the situation this week is step 1 + step 2: detect, report, and drop.

### On external references

`inspiration/KytyPS5` offers nothing to copy here — it is documented in this repo's own notes as
aborting on any real amplification, no-op'ing the GS messages and dropping the PRIM export. It is a
*negative* confirmation that the classification boundary (passthrough vs amplifying) is the right
one, and nothing more. There is no oracle; the only ground truth available is the firmware's own
shader binaries and the RDNA2 ISA document.

---

## 5. Ranked: what must be built for a real title to render correctly on NVIDIA

Ranked by (damage if wrong) × (likelihood a shipping title hits it) ÷ (cost).

| # | Work | Why | Cost |
|---|---|---|---|
| 1 | **NGG detect + report + fail-loud** (§4 steps 1-2) | Converts the worst silent corruption into a measurement. Uses code that already exists and is tested. | ~1 day |
| 2 | **Add the four `s_waitcnt_*` SOPK forms to the no-op list** (`Gen5SpirvTranslator.cs:1758`) | LLVM emits these routinely on GFX10; today each one kills the entire draw. | ~1 hour |
| 3 | **Trace the SMEM zero-fill** (`Gen5SpirvTranslator.cs:2216`) | The known silent-corruption instance, currently invisible. Measure before changing. | ~1 hour |
| 4 | **Trace every silent default**: unknown image format (`:10092`), unknown topology (`:9538`), raw-tiled upload (`AgcExports.cs:9054`), blend factor/op (`:9727`,`:9738`) | Same principle. Five one-shot warnings turn five invisible failure modes into a work list ranked by real frequency. | ~half a day |
| 5 | **`ds_permute` / `ds_bpermute` / `ds_swizzle`** → `OpGroupNonUniformShuffle`/`ShuffleXor` | Cross-lane shuffles are everywhere in compute; explicit failure today. | ~1 day |
| 6 | **NGG amplification backend** (compute prepass + `vkCmdDrawIndexedIndirect`) | The actual capability gap. Only start after #1 says a real title needs it. | weeks |
| 7 | **Pin the subgroup size**: enable `VK_EXT_subgroup_size_control`, request `requiredSubgroupSize = 32`, and refuse-with-message if the device cannot provide it | Makes the existing wave32 assumption *checked* instead of accidental. Prerequisite for Intel. | ~1 day |
| 8 | **Plumb wave size into graphics stages** and lift the `localSize == 64` restriction on wave64 compute emulation | `IGuestGpuBackend.cs:33-57` needs the parameter that `:59-69` already has. | ~2 days |
| 9 | **POS1-POS3 exports**: clip/cull distance, point size, `gl_Layer`, `gl_ViewportIndex` (`Gen5SpirvTranslator.cs:4414`) | Layered rendering (shadow cascades, cubemaps) silently does nothing today. | ~2 days |
| 10 | **`sceAgcCbDispatchGetSize` + `sceAgcCbSetShRegisterRangeDirectGetSize`** | Two of the six AGC entry points the shipped title actually calls; both feed guest buffer sizing. | ~half a day |
| 11 | **`sceAgcDcbCopyData` / `sceAgcAcbCopyData`** (`COPY_DATA` packet, build + parse) | GPU→memory readback the title calls. | ~1 day |
| 12 | **`s_denorm_mode` / `s_round_mode`** → SPIR-V `FPDenormMode`/`FPRoundingMode` execution modes | Numerical divergence, vendor-dependent. Low frequency, subtle when it bites. | ~2 days |
| 13 | **Remaining ISA gaps**: MTBUF stores, `s_setpc`/`s_swappc`, `v_sad_*`, `v_movrel*`, VCMPX integer F/T forms | Each is an explicit draw drop. Cheap individually. | ~1 day each |
| 14 | **Additional GFX10 swizzle modes** beyond the trusted seven | Needed only once #4 shows how often the raw-upload fallback fires. | data-driven |
| 15 | **DCC / HTILE / FMASK decode** | Nothing exists. Scope unknown until #4 measures. | large |
| 16 | **Tessellation (LS+HS)** | Genuinely absent, explicitly refused. Only worth it if a target title needs it. | large |

---

## 6. What I could not determine statically

Stated plainly, per the anti-hallucination protocol:

- **Whether the shipped title actually uses NGG amplification.** `scripts/astro_import_routing.tsv`
  proves it calls `sceAgcCreatePrimState` (`D9sr1xGUriE`), which implies an ES/GS primitive shader,
  but pass-through versus amplifying is a property of the shader *binary*, which lives in the game
  dump on the VM and is not in this checkout. §4 step 1 is precisely the experiment that answers
  this. **Absence of evidence here is not evidence of absence.**
- **Whether the title uses tessellation.** Same reason. It would show up immediately as an
  `ORBIS_GEN2_ERROR_INVALID_ARGUMENT` from `sceAgcCreatePrimState` (`AgcExports.cs:842`) in a boot
  log — I have no boot log.
- **Which GFX10 swizzle modes real assets use**, and therefore how often §3.3's raw-upload fallback
  actually fires. Requires a boot with a counter on that branch.
- **Whether the CPU-side indirect-arg resolution (§3.12) breaks any real GPU-driven pass.** Depends
  on whether the guest fences with `ACQUIRE_MEM` between producer and consumer; needs a trace.
- **How often the SMEM zero-fill (§3.2) fires.** No instrumentation exists, which is why it is
  item #3 on the list.
- **Real-hardware behaviour on Intel.** The wave-size assumption (§3.7) is a static reading of the
  code. I have not run it on Intel and this repo contains no Intel run record.

I searched: all of `src/` (C# sources and tests), `docs/`, `scripts/astro_import_routing.tsv`, the
firmware modules `libSceAgc.sprx` / `libSceAgcDriver.sprx` / `libSceAgcVsh.sprx` in
`games/PS5_4.03_reconstructed`, and the repo-root scratchpad. I did not open `inspiration/` as
authority, and I did not have access to the game dump or to any boot log.
