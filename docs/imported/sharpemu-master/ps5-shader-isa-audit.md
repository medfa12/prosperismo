# PS5 shader ISA audit

> **Historical audit, not current defect list (2026-07-31).** This document
> records the state when the SDK-12 capture was first compared with the
> translator. Promoted VOP3 ranges, wave-width mask writes, MIMG's eighth
> opcode bit, MTBUF formats, DPP bank masks, `s_barrier`, 64 KiB LDS, relative
> VGPR moves, typed stores, DS shuffles, DS append/consume, and BVH handling now
> have included tests. Current live gaps are measured by
> `scripts/isa_compliance.py`: 227 of 540 indexed Sony names are not decodable
> against 668 decoder keys, concentrated in f16/f64/16-bit VALU,
> relative-SGPR, FLAT/SCRATCH, GWS/GDS, and uncommon memory/image forms. Also,
> the local text capture is only about 47 of roughly 547 source pages and has
> no instruction bodies for vector-memory, LDS/GDS, image, FLAT, or export.
> Use Sony's SDK-10 `libSceShaderIsaP.dll` or LLVM gfx1013 encodings for those
> families; absence from the captured text is not evidence of hardware
> absence.

Sony's official **GPU Shader Core ISA Instruction Reference** and **GPU Shader Core ISA
Specification** (both SDK 12.000) are now available locally, and the translator has been audited
against them. This is the first time our shader work has had authoritative ground truth rather than
AMD's public RDNA2 documentation plus inference from other emulators.

Every finding below carries a doc citation and a `file:line`, and every one survived an adversarial
second pass. 33 findings were confirmed, 1 refuted.

## The corpus

The PDFs are page-per-file and text-extractable. They are extracted to:

```
games/gpu shit_forzen/_text/GPU_Shader_Core_ISA_Instruction_Reference_-_SDK_12.000.txt   4.5 MB, 3,686 pages
games/gpu shit_forzen/_text/GPU_Shader_Core_ISA_Specification_-_SDK_12.000.txt           1.6 MB,   579 pages
```

4,265 pages, 6.1 M characters, **4,281 instruction entries**. Each entry has the mnemonic, `Usage`
(syntax), `Encodings` (which encodings the instruction is legal in), `Operation Summary` (the actual
semantics, e.g. `sdst.s = (ssrc0.s + ssrc1.s); SCC = overflow`), `Restrictions`, `Implicit R/W` and
`Rate`. Page markers look like `===== 0113.pdf p72 =====` - cite those.

Search with `Select-String`; the files are large but line-oriented. Regenerate with
`%TEMP%\extract_isa.py` (pypdf) if the PDFs are ever re-dropped.

**What the doc does NOT contain:** numeric opcode values. The encoding bit-diagrams are images and
flatten to `-----dword0----------dword1-----` in extraction. Opcode numbers still come from
`games/rdna2§op§code.txt` (AMD public table) cross-checked against `inspiration/shadPS4`. The Sony
doc is authoritative for *semantics, encodings-legality and restrictions*; the numbering is
inference. Keep that distinction when citing.

**Correction to a common assumption:** the shader-compiler unit tests are **not** in a
`SharpEmu.ShaderCompiler.Tests` project. They live in `tests/SharpEmu.Libs.Tests/ShaderCompiler/`
(`Gen5IsaOpcodeCoverageTests.cs`, `Gen5WaveWidthTests.cs`, `Gen5SpirvSilentDefectTests.cs`,
`Gen5NggPrimitiveExportTests.cs`, `Gen5ShaderTranslatorTests.cs`). That project already assembles
GFX10 machine words and runs them decoder -> scalar evaluator -> SPIR-V, so every test below is a few
dozen lines in an existing file and needs no title run.

## 1. The VOP3 encoding space is three quarters undecoded

`Gen5ShaderTranslator.cs:1473` `DecodeVop3` reads a 10-bit opcode (`:1481`) and looks it up in a
54-entry table (`:1497-1556`) covering a 1024-entry space. The four documented sub-ranges are not
modelled at all:

| range | meaning | present today |
| --- | --- | --- |
| `0x000-0x0FF` | VOP3-encoded **VOPC** | **none** |
| `0x100-0x13F` | VOP3-encoded **VOP2** | 10 of 64 |
| `0x180-0x1FF` | VOP3-encoded **VOP1** | **none** |
| VOP3-only | `v_alignbit_b32` 0x14E, `v_perm_b32` 0x344, 64-bit shifts 0x2FF-0x301, the `v_div_scale`/`div_fmas`/`div_fixup` triple, `v_mad_i32_i24` 0x142 | missing |

Unmapped opcodes become `$"Vop3Raw{opcode:X3}"` (`:1555`), which **passes decode** and then fails at
`Gen5SpirvTranslator.Alu.cs:1188` `unsupported vector opcode`, aborting the whole shader
(`Gen5SpirvTranslator.cs:2282-2286` returns false out of the block loop). The Metal backend has the
identical hole at `Gen5MslTranslator.Alu.cs:374-375`. Because decode succeeds, this hole is
**invisible to opcode-coverage metrics**.

Why it matters: `===== 0113.pdf p73 =====` says the 32-bit VOPC form "requires sdst to be VCC,
vsrc1 to be a VGPR, and no input modifiers", and Specification `===== 25.pdf p3 =====` lists the
VOP3-only categories including "Operations that use a scalar destination (sdst) other than VCC". So
any compare into a non-VCC SGPR pair, any compare with an input modifier, and even
`v_and_b32 v0, s1, s2` are **forced** into VOP3 by the encoding rules. That is ordinary compiler
output for divergent control flow, not an exotic feature.

**Contract.** Resolve by range before consulting the VOP3-only table: `op < 0x100` -> VOPC `op`;
`0x100 <= op < 0x140` -> VOP2 `op-0x100` (excluding `v_madmk/v_madak/v_fmamk/v_fmaak`, which the doc
documents as "VOP2 literal constant" only); `0x180 <= op < 0x200` -> VOP1 `op-0x180`; else VOP3-only.
Each promoted range must reuse the existing `DecodeVopc`/`DecodeVop2`/`DecodeVop1` tables so there is
one source of truth per mnemonic. For promoted VOPC the destination must be a **scalar operand pair**
from the vdst byte - `Gen5ShaderTranslator.cs:2308` unconditionally emits
`Gen5Operand.Vector(word & 0xFF)` - and the emitter must write the mask there rather than to VCC
(`Alu.cs:1950-1954` hardcodes 106). An opcode with no table entry must produce a **named decode
error**, never a `Vop3Raw***` that dies later.

Also add `0x129`/`0x12A` to `IsVop3BOpcode` (`:1561`) and name `0x16D`/`0x177`.

## 2. Wave32 lane masks are written as 64 bits, clobbering `sdst+1`

`Gen5SpirvTranslator.cs:6606` `StoreWaveMask` -> `Alu.cs:3670-3681` `StoreS64`, which
unconditionally stores `register` and `register + 1` with **no `_waveLaneCount` test on the path**.
Call sites: `Alu.cs:1946` (v_cmpx), `:1954` (v_cmp), `:4084`/`:4088` (carry-out).

Specification `===== 38.pdf p9 =====`: the thread mask "is 32 bits in Wave32 mode and 64 bits in
Wave64 mode. In the latter case, sdst must be the first of a 2-GPR aligned pair." And
`===== 01.pdf p2 =====` makes wave32 the default for every stage except pixel - so this is the
normal case. GFX10 wave32 codegen allocates VCC_HI and odd SGPRs as ordinary temporaries precisely
because the mask does not occupy them.

Symptom: a live scalar - a descriptor dword, a loop bound, a buffer offset - silently zeroed by the
next compare. Presents as black or stretched geometry, a sampler reading descriptor 0, or a loop
exiting immediately, with **no diagnostic**. The same file already gets this right for the `*_b32`
saveexec form at `Alu.cs:2100`.

**Contract.** `StoreWaveMask` writes only the low dword when `_waveLaneCount == 32`, the pair only
when 64. Same for `StoreCarryOut`.

## 3. FLAT and SCRATCH segments do not decode at all

`Gen5ShaderTranslator.cs:1771` `DecodeFlat`, line 1781:
`name = segment == 0x2 ? opcode switch { ... } : string.Empty;` - segments 0 (FLAT) and 1 (SCRATCH)
always fall through to `unknown-flat segment=0x0` at `:1812`. Within GLOBAL only 24 of 57 opcodes
are named; every atomic except `0x32` add and `0x38` umax is missing. Related: `:2456` reads SADDR
without distinguishing NULL (125), so `global_load_dword v1, v[2:3], off` is treated as an SGPR-pair
base and hits `global-address-null` (`Gen5ShaderScalarEvaluator.cs:366`).

Specification `===== 25.pdf p5 =====`: "the SEG field is used to specify the type of instruction
(0=Flat, 1=Scratch, 2=Global)". `===== 25.pdf p9 =====`: "Setting the saddr operand to NULL disables
scalar addressing."

This ranks third because **scratch is where the compiler spills VGPRs**, so the failure rate scales
with shader complexity rather than with any opt-in feature. Long pixel shaders and large compute
kernels emit `scratch_store_dword`/`scratch_load_dword`, and each one drops the entire program.

**Contract.** Name all three segments from one shared opcode table. SCRATCH lowers to a per-lane
private array. FLAT may lower as GLOBAL only where the address is provably in the global aperture and
must otherwise be refused **by name**. `SADDR == 125` means no scalar base: address is the VGPR pair
plus the sign-extended 13-bit immediate, and until a flat heap mapping exists that case needs its own
greppable diagnostic rather than the misleading `global-address-null`. No path may bind a descriptor
at a guessed address - a wrong descriptor is worse than an honest reject.

## Tier 2

4. **GS system SGPRs never seeded** (`Gen5ShaderScalarEvaluator.cs:152-160`,
   `Gen5SpirvTranslator.cs:2049-2088`). s0-s7 are uninitialised, so s3 (`s_gs_wave_id`, carrying
   `gs_vert_count`/`gs_prim_count`) reads 0 and every NGG program computes `gs_vert_count = 0`. It
   only survives today because the one idiom Sony's compiler happens to emit masks the shift to
   `[5:0]` and lands on all-ones EXEC **by luck** (`Alu.cs:2883-2888`). `s_bfm_b64 exec, count, 0`
   gives EXEC=0; `v_cmp_gt_u32 exec, count, laneid` gives EXEC=0; an early-out returns before any
   export. A compiler-version coin flip that produces a black screen.
5. **No 16-bit VALU at all** - 46 VOP1, 16 VOP2, 125 VOPC, ~90 VOP3 slots. Hard failure on the first
   `v_add_f16`. Follow item 1; same tables.
6. **MTBUF FORMAT never decoded** (`Gen5ShaderTranslator.cs:1686`, `Gen5ShaderIr.cs:191`). The
   idiomatic case - `V#.format = 0` because the format lives on the instruction - reads every
   component as 0, collapsing vertex positions to the origin. Verify the bit position (25:19)
   against a captured instruction first; the encoding diagrams did not survive extraction.
7. **MIMG opcode read as 7 bits, is 8** (`:1868`) - op[7] lives in word0 bit 0 on GFX10. Every `_G16`
   gradient sample aliases onto a different instruction and reads packed f16 gradients as f32: wrong
   LOD, wrong mip, no error.
8. **DPP `bank_mask` uses `lane%4` instead of `(lane/4)%4`** (`Alu.cs:3538`,
   `Gen5MslTranslator.Alu.cs:526`) - both backends. Invisible at the default 0xF; transposes the
   write-enable pattern for every wave reduction and prefix scan.
9. **`s_barrier` emitted with WorkgroupMemory-only semantics** (`Gen5SpirvTranslator.cs:2461`,
   `:6580`) - 0x108 should be 0x948, so buffer and image writes are not made visible across the
   workgroup. One constant; Metal is already correct.
10. **LDS wraps instead of range-checking, and compute LDS capped at 32 KB against 64 KB hardware**
    (`:91`, `:3046`).
11. **DS `0x3E`/`0x3F` decoded as `ds_permute`/`ds_bpermute`, but are `ds_append`/`ds_ordered_count`**
    - two-way silent corruption. Note the wrong numbers are **baked into the test suite**
    (`Gen5IsaOpcodeCoverageTests.cs:275`, `:293`), which currently certifies the bug; fix both in one
    commit.
12. **Subgroup size queried but never pinned** (`VulkanVideoPresenter.cs:4157-4213`, `:7275-7288`) -
    no `VkPipelineShaderStageRequiredSubgroupSizeCreateInfo`. On AMD (host subgroup 64) two guest
    waves alias onto one and half the dispatch reads `IsCurrentLaneSet` as false.

## Tier 3: the numerics batch

Findings 7-12/19/21/33, all effort S, all in `Gen5SpirvTranslator.Alu.cs` + `Gen5MslTranslator.Alu.cs`.
In five of them the correct behaviour **already exists elsewhere in the same file**
(`EmitClampToUnitInterval` `Alu.cs:1404`, `EmitPackedF16MinMax` `:1506`, the scalar `s_bfe` clamp
`:2450`), so the fix is mostly deleting a divergence. Highest value three:

- **VOP3 CLAMP** emitted as GLSL `FClamp`, so a NaN is not flushed to +0 as DX10_CLAMP requires
  (`Alu.cs:4312`).
- **`FMin`/`FMax` -> `NMin`/`NMax`** (`Alu.cs:371-375`): SPIR-V leaves the NaN result undefined where
  the ISA pins it.
- **`v_med3_f32` must return vsrc2 when any source is NaN** (`Alu.cs:820-835`).

Together those are the difference between a NaN dying at the saturate idiom the way hardware kills it
and a NaN reaching the render target as a black or garbage tile.

Also here: the **MAD-vs-FMA split**. `v_mad_f32`/`v_mac_f32`/`v_madmk`/`v_madak` are lowered as fused
FMA, but the ISA defines them as unfused mul-then-add (Sony states MAD rounds the product before the
add - that is what distinguishes it from `v_fma_f32`). 1 ULP, but do it in the same pass and use
`EmitPreciseFloat` (`Alu.cs:1476`) so the driver cannot re-fuse. The PS5-only **legacy** family
(`v_mul_legacy_f32`/`v_mac_legacy_f32`/`v_mad_legacy_f32`, 0x107/0x106/0x140) must lower as
`p = (src0 == 0 || src1 == 0) ? 0 : src0*src1` then a separate `FAdd` - `===== 0111.pdf p42 =====`
requires `0.0 * x == 0.0` even for x = NaN or Inf, which an FMA cannot express. RDNA2's
`FMA_LEGACY` replacements do not exist on PS5.

## NGG: reassessed, and demoted

The project notes have long recorded NGG as "the shared graphics blocker for both Astro Bot and
Superliminal". **That ranking is wrong and should be corrected.**

Stripping out finding 4 (GS SGPR seeding, promoted to tier 2 above - it is a self-contained
black-screen bug that does not need the mesh path), what remains of NGG sits around eighth to tenth:
below the decoder work, below the wave32 mask, roughly level with subgroup-size pinning. The reason:
the index-buffer fallback is **not currently blocking anything** - it silently produces
plausible-but-wrong geometry for culling shaders while asserting in its own warning text that the
program is pass-through. The decoder holes above drop *entire draws*, on shaders that have nothing to
do with NGG, in far more titles.

**Pull forward now (effort S, changes nothing structural):** tighten the classifier.
`NggPrimitiveShader.cs:478` defines `IsPassThrough` as
`PrimitiveExports <= 1 && Position0Exports <= 1 && !ExportInLoop`, which a standard culling primitive
shader satisfies while having connectivity that is emphatically **not** the draw's index buffer
(`===== 67.pdf p2 =====`: a culling shader exports exactly one primitive per invocation after
narrowing EXEC and requesting a right-sized allocation). Add the `ConnectivityForwarded` predicate the
file's own offline census already describes at `:429-439` - target-20 export has `en == 0x1` and
sources v0, nothing writes v0 before that PC, and M0 before `GS_ALLOC_REQ` is the unmodified
`s_gs_wave_id` field extract - and route everything else to a loud refusal. That converts a silent
wrong-geometry path into a visible one in about a day, and it is the precondition for knowing how
many titles actually need the real path.

**The real path is a project, not a fix:** `VK_EXT_mesh_shader` as a new backend target
(`SetMeshOutputsEXT` for `GS_ALLOC_REQ`, `gl_PrimitiveTriangleIndicesEXT` for the target-20 dword's
three 9-bit offset fields, `gl_MeshPrimitivesEXT[i].gl_Layer/gl_ViewportIndex/gl_PrimitiveID` for the
Y dword), plus AGC plumbing to route NGG draws to a mesh pipeline, plus GS VGPR v0-v4 synthesis, plus
a compute-prepass fallback for devices and for Metal. **A geometry shader cannot express the model** -
NGG is subgroup-cooperative, not per-input-primitive, and cannot express vertex compaction at all.
Three to six weeks of focused work.

Separable prerequisite (3-4 days): the Metal backend silently drops every position export except POS0
and drops the NGG prim export **with no diagnostic at all** (`Gen5MslTranslator.Pixel.cs:746-761`).
Factor `DecodePositionSlots`/`TryPlanPositionOutputs` out of `Gen5SpirvTranslator.cs:1571-1714` into
the shared compiler so both backends consume one planner.

**Blocked on ground truth:** the position misc-vector Z/W mapping. Sony puts `{viewport index, render
target index}` in Z and object ID / right-eye X in W, contradicting the PS4/GCN layout we ported from
shadPS4 - but the exact bit split inside Z is not in this document and must come from libSceAgc or a
firmware NGG shader. Until then the honest move is to refuse the export with a specific message when
`PA_CL_VS_OUT_CNTL.USE_VTX_VIEWPORT_INDX` is set, exactly as `TryPlanPositionOutputs` already refuses
EdgeFlag - rather than routing object ID into `gl_ViewportIndex` and declaring
`CapabilityShaderViewportIndex` for a shader that never wanted it.

> **Opcode provenance warning (added 2026-07-27).** Findings in this audit that rest on AMD's public
> RDNA 2 guide (via `games/rdna2*op*code(...).txt`) are not authoritative for Prospero. Sony's own
> GPU Shader Core ISA documents are in `games/gpu shit_forzen/_text/` and disagree in both
> directions - Prospero keeps `v_mac_f32`/`v_mad_f32`-family instructions that RDNA2 dropped, and has
> no trace of the `v_dot*` family that RDNA2 has. See `prospero-isa-source.md` before acting on any
> opcode claim here.
