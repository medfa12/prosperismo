# Astro GPU alignment handoff

Date: 2026-08-02

Status: active Prosperismo bring-up checkpoint. Astro boots into its first GPU
frame and reaches `TileBasedLighting`, but shader translation still terminates
before a presentable game frame. No nonblack final frame is claimed.

## Current Prosperismo checkpoint

The inherited SharpEmu surface evidence below remains useful background, but it
is not yet a measurement of Prosperismo's renderer. Prosperismo had earlier,
independent blockers before it could reach that boundary:

- **CONFIRMED — AGC async DMA ABI:** `GraphicsAcbDmaData` incorrectly used the
  synchronous draw-command-buffer signature. Sony's SDK archive symbol records
  `AsyncCommandBuffer::dmaData(AsyncDmaDataDst, CachePolicy, uint64,
  AsyncDmaDataSrc, CachePolicy, uint64, uint32,
  DmaDataWaitForPreviousDmas, DmaDataWriteConfirm)` with no CP-engine or
  block-engine parameters. Correcting that ABI moved Astro from
  `Classification_Async` into `Classification_0`, `DeferredLighting_0` and
  `TileBasedLighting`.
- **CONFIRMED — gfx1013 `v_cmp_gt_u64`:** Sony's ISA library disassembles raw
  `6a 00 e4 d4 6a 00 01 00` as `v_cmp_gt_u64 vcc_lo, vcc, 0`. The decoder and
  SPIR-V emitter now implement unsigned 64-bit greater-than using high/low
  32-bit words. The exact instruction passes a Vulkan compute regression.
- **CONFIRMED — gfx1013 reverse subtract with borrow:** Sony disassembles
  `80 30 00 54` as `v_subrev_co_ci_u32 v0, vcc_lo, 0, v24, vcc_lo`, and the
  corresponding VOP3B bytes as the same operation with an arbitrary scalar
  borrow mask. Both encodings now lower as `src1 - src0 - borrow_in`, produce
  the architectural borrow mask, and pass true/false/underflow GPU tests.
- **CONFIRMED -- gfx1013 trap policy:** PC `0x1f80`, raw `01 00 92 bf`, is
  Sony `_SCE_BREAK()` / LLVM `s_trap 1`. On the Windows/Vulkan path it now
  terminates the invocation explicitly; it is neither ignored nor allowed to
  fall through. A focused Vulkan regression pins the CFG behavior.
- **CONFIRMED -- gfx1013 BVH decode and explicit capability fallback:** Sony's
  ISA library identifies Astro's exact five-word MIMG at PC `0x2190` as
  `image_bvh_intersect_ray ... dmask:0xf ... r128 nsa`. Prosperismo decodes the
  11-address, four-result gfx1013 form. Vulkan has no direct raw-Prospero-BVH
  descriptor equivalent, so the current capability policy writes four
  `UINT_MAX` results. This is not claimed as ray-tracing implementation: Astro
  itself initializes and tests all four results against `-1` as its miss/no-child
  sentinel, so the policy deliberately disables the RT effect through a guest
  path rather than inventing a hit.
- **CONFIRMED -- gfx1013 `s_ff1_i32_b64`:** Sony disassembles raw `6a 14 eb be`
  as `s_ff1_i32_b64 vcc_hi, vcc`. The 64-bit least-significant-set-bit operation
  now handles low-half, high-half, zero and Astro's overlapping source/destination
  form in a focused Vulkan regression.
- **CURRENT BLOCKER -- GPU-dynamic raw SMEM address binding:** decode, CFG and IR
  now complete for `cs=0x500571000`, but resource tracking rejects the
  `s_load_dwordx4 s20, s8, null` at PC `0x20b8`: the traversal loop constructs
  `s8:s9` from a GPU-dynamic node value, so descriptor source 29 contains an
  unknown low dword. Prosperismo can materialize CPU-resolvable SRT addresses
  and fixed address snapshots, but cannot yet bind/dereference a raw scalar
  guest address selected by the running shader. SharpEmu is a useful proven
  differential here: its scalar evaluator records a real guest-memory binding
  for dynamic SMEM when it can recover one and only zero-fills when it cannot.
  Port the binding/base-bias contract; do not make Prosperismo accept the shader
  by fabricating zero memory.

Validated run artifacts are under `artifacts/astro-runs/20260802-081824`
(pre-fix compare), `20260802-082506` (next VOP2 blocker),
`20260802-082830` (next VOP3B blocker), `20260802-082946` (trap),
`20260802-084504` (BVH), `20260802-085544` (`s_ff1_i32_b64`) and
`20260802-090417` (current dynamic-SMEM boundary). The last run records executable
SHA-256 `22AA47588AB5071F78826D8BDC75C4D487C7D1E4C759142D72C44EEDC11342BA`
and embedded Git identity `6cc5c3a`. The focused selector
`shader_recompiler_compute_tests.exe --vop3-u64-compare-only` passes the compare,
borrow, trap, BVH-miss and 64-bit-FF1 GPU semantic tests. The unfiltered
suite currently stops earlier in the pre-existing `ImageTransitionState`
depth/stencil mip-copy test; it is not claimed green.

## Established boundary

- SharpEmu captured the full-resolution HDR scene target at guest address
  `0x514080000` with 869,977 nonblack pixels. The scene does render.
- The downstream 960x540 G-buffer/postprocess inputs examined at the clustered
  lighting boundary were all zero and were DCC-flagged.
- The final tone-map pixel shader had complete scalar constants
  (`smem_zero_filled=0`) and inherited black input. It was not the first stage
  where pixels became zero.
- Sony's `agc-registerstructs.natvis` identifies `CB_COLOR_CONTROL` mode 6 as
  `kDccDecompress`. Dropping that operation is therefore a live correctness
  suspect, not an unknown register interpretation.
- The historical slow `ps=0x5008F1400` loop walked a GPU-built linked list whose
  producer had previously been absent. Performance must be remeasured after the
  producer path is present before changing control-flow semantics.

## Next falsifiable checkpoint

First make `cs=0x500571000` materialize without fabricated SMEM:

1. recover a real guest allocation/binding base for the GPU-dynamic raw-SMEM
   access at PC `0x20b8`, following the proven binding-plus-byte-bias shape;
2. bind a range that covers the traversal's runtime addresses and preserve the
   raw 48-bit guest address calculation in SPIR-V;
3. add a focused test where the SGPR address changes on the GPU but remains
   inside the captured guest allocation;
4. confirm the shader compiles and dispatches with no unresolved or zero-filled
   scalar loads.

Then capture one paired producer boundary where a known-nonblack full-resolution
source feeds the first required half-resolution target:

1. record the source and destination guest descriptors, including tile mode,
   DCC address/lifetime, pitch, mip and format;
2. prove whether the writer draw/dispatch executes;
3. inspect the destination host image immediately after the writer;
4. inspect the same resource at its first sampled alias;
5. compare address/swizzle math with Sony's `libSceAgcGpuAddress.dll` and the
   metadata transition with the SDK compression samples.

This distinguishes the two remaining root-cause classes:

- the DCC decompress/fast-clear metadata operation is dropped or implemented
  incorrectly; or
- render-target residency/alias identity selects a fresh or incompatible host
  image when the written surface is sampled.

Do this offline from retained draw state where possible, then perform one
targeted boot only after the probe wiring and exact executable are verified.

## Ground-truth order

1. Sony Prospero SDK 10 headers, samples, NATVIS register definitions and host
   oracle DLLs under `ps5oracle/sony/prospero-sdk-10.00`.
2. Byte-exact firmware behavior and symbols under `ps5oracle/sony`.
3. LLVM's gfx1013 definition/MC tests and AMD's RDNA 1 ISA baseline for families
   missing from the partial Sony ISA capture.
4. PAL, Mesa ACO, preserved emulator implementations and other curated projects
   as differentials, never as authority over conflicting Sony evidence.

The partial Sony ISA capture has no instruction bodies for vector memory,
LDS/GDS, image, FLAT or export. A failed grep of that capture is not evidence of
absence. Prospero gfx1013 is GFX10.1 plus BVH ray-tracing instructions; it is not
a stock RDNA2 decode table.

## Avoided dead ends

- Do not re-litigate whether Astro renders any scene pixels; the full-resolution
  capture already answers that.
- Do not blame the final tone mapper without finding an earlier nonblack input.
- Do not use a draw count, trace share or absence of ERROR-level telemetry as a
  finding.
- Do not chase LLE ioctl `0xC0488131`; it is a strictly larger implementation
  surface than the HLE path.
- Do not use title-specific fabricated resource values to force progress.

## Relevant preserved material

- `docs/imported/sharpemu-home/astro-bot-boot.md`
- `docs/imported/sharpemu-home/astrobot-bringup.md`
- `docs/imported/sharpemu-home/astro-agc-conformance.md`
- `docs/imported/sharpemu-home/gpu-surface-gap.md`
- `docs/imported/sharpemu-home/methodology-execution.md`
- `docs/imported/sharpemu-home/what-the-firmware-taught-us.md`
- `ps5oracle/public-references/PUBLIC-ISA-REFERENCES-README.md`
- `ps5oracle/sony/prospero-sdk-10.00/HOST-TOOLS-README.md`
