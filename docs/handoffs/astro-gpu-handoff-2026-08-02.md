# Astro GPU alignment handoff

Date: 2026-08-02

Status: active Prosperismo bring-up checkpoint. Astro now passes
`TileBasedLighting`, submits repeated flips and sustains the first postprocess
frame sequence, but no nonblack in-game frame has been captured. Do not infer
render correctness from frame or flip counts.

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
- **CONFIRMED -- GPU-dynamic raw SMEM address binding:** the first precise
  failure was `cs=0x500571000` at PC `0x1f6c`. PC `0x1f60` loads a pointer into
  `s8:s9`; PC `0x1f6c` dereferences `s8 + 24`. CPU-folding that second load read
  stale CPU memory at address `0x18`. Runtime address provenance now anchors the
  live 48-bit SGPR address to a real guest allocation and rebases it in SPIR-V.
  Grouped `s_load_dwordxN` emission preserves the original base when destination
  SGPRs overlap it. A focused GPU test changes the address on the GPU and reads
  four dwords from the selected allocation. Astro passes this shader with no
  fabricated zero load and grows from 17 to 30 compiled shaders.
- **CONFIRMED -- Sony ES base companion register:** the next packet wrote SH
  offset `0xCA` (`SPI_SHADER_PGM_RSRC1_ES`) with `0x03000002`. Sony's
  `sce::Agc::ShShaderBaseEs` NATVIS contract treats its third register as a
  fixed `kDefault` word; only LO/HI form the executable ES address. Prosperismo
  now explicitly accepts that word for direct and indirect packets. The next
  Astro run passes the former fatal and submits repeated flips.
- **CONFIRMED -- rotating dynamic-address permutations removed:** the sustained
  run repeatedly retranslated the dispatcher-fallback compute
  programs at `0x500571000`, `0x50059cd00`, `0x5005cc100` and `0x5005fdb00`.
  Their emitted modules are roughly 270k--285k SPIR-V words. The new dynamic
  address binding specializes the allocation base, and Astro rotates the
  backing allocation across frames. Existing cache telemetry proved each miss
  was `address resource 1 no longer matches specialization`. The binding base is
  now supplied as two runtime shader-data dwords per address resource and is no
  longer part of the SPIR-V specialization. Across the rotating allocations all
  four programs and their Vulkan pipelines are reused with zero mismatch. A
  clean Release run reaches frame 189 at about 1.87 FPS, versus about 0.033 FPS
  before the fix. This is a performance result, not proof of a valid guest
  image.
- **CONFIRMED -- first observed half-resolution writer executes:** marker
  `resize_normal`, `cs=0x5006F7700`, dispatches `120x68x1`. It samples the
  full-resolution images at `0x510D10000` and `0x5104A0000`, then writes
  960x540 storage images at `0x539910000` and `0x539B90000`. The immediately
  following `ScreenSpaceShadow` shader `cs=0x500700A00` samples
  `0x539910000`. Therefore this half-resolution surface is not waiting for a
  skipped CB metadata draw: its compute producer is present. Pixel content at
  the producer boundary has not yet been read back in Prosperismo.
- **CONFIRMED -- the current super-resolution pass inherits black RGB:** in
  `20260802-104324-superres-bound-images`, the exact Vulkan sampled binding of
  `cs=0x500690F00` at `0x53AA00000` had 2,081,320 nonzero *bytes* and hash
  `0xA2FFED664DD95323`; its exact storage binding at `0x53B9F0000` began zero.
  Channel-aware native-image readback in
  `20260802-104933-superres-source-footprint` resolved the ambiguity: the
  1920x1080 RGBA16F source had **zero RGB-nonzero pixels** and nonzero alpha in
  all 2,073,600 pixels. The buffer-offset shader-data dword was zero, so the
  complete 32-byte constant buffer was addressed at its bound base.
- **CONFIRMED -- `0x500690F00` is not the current first black producer:** a
  diagnostic-only differential replaced its 48 `IMAGE_LOAD` results with
  `float4(0.5, 0.5, 0.5, 1.0)` while retaining the original 1,112-instruction
  ALU, dispatch, coordinates and four stores. The output became fully
  populated (`23,288,832` nonzero bytes) in
  `20260802-104759-superres-constant-load`. The unmodified output is
  black-with-alpha. Together with the exact bound-image readback, this proves
  that the shader's native loads correctly inherit a black RGB source; it does
  not support an NSA-coordinate, storage-write, EXEC-coverage or ALU failure.
  The substitution was removed and is not a rendering fix.
- **CONFIRMED -- presentation is stale while guest work advances:** in
  `20260802-102045-present-hashes`, present samples 0, 60, 120, 180 and 240
  were byte-identical (`0x95D803D3F5D100F6`) while the guest loaded later UI
  and world-map assets. The visible dark PlayStation Studios image is retained
  presentation content, not proof that the current AGC scene target is
  nonblack.

Validated run artifacts are under `artifacts/astro-runs/20260802-081824`
(pre-fix compare), `20260802-082506` (next VOP2 blocker),
`20260802-082830` (next VOP3B blocker), `20260802-082946` (trap),
`20260802-084504` (BVH), `20260802-085544` (`s_ff1_i32_b64`) and
`20260802-090417` (dynamic-SMEM boundary), `20260802-094750` (dynamic SMEM
fixed; next `RSRC1_ES` boundary), and `20260802-095253` (repeated frames after
the Sony ES companion-register fix), `20260802-095659-cache-proof` (exact
specialization-mismatch proof), `20260802-100034-runtime-address-base`
(cross-frame shader reuse), and `20260802-100534-release` (clean Release
performance and producer boundary), `20260802-102045-present-hashes` (stale
present proof), `20260802-104324-superres-bound-images` (exact compute
bindings), `20260802-104759-superres-constant-load` (diagnostic load
differential), and `20260802-104933-superres-source-footprint` (channel-aware
native readback). The focused selector
`shader_recompiler_compute_tests.exe --vop3-u64-compare-only` passes the compare,
borrow, trap, BVH-miss, 64-bit-FF1 and dynamic-SMEM GPU semantic tests. The unfiltered
suite currently stops earlier in the pre-existing `ImageTransitionState`
depth/stencil mip-copy test; it is not claimed green.

## Established boundary

- Historical SharpEmu captures contain nonblack full-resolution scene targets,
  but they are controls from different runs and allocation lifetimes. They do
  not establish the content of Prosperismo's current frame. In the current
  frame-3 lifetime, `0x514080000` and the following `0x53AA00000` RGBA16F
  image are byte-identical and have zero nonblack RGB pixels; their nonzero-byte
  counts are alpha. Never use a byte count as a pixel finding.
- The downstream 960x540 G-buffer/postprocess inputs examined at the clustered
  lighting boundary were all zero and were DCC-flagged.
- The final tone-map pixel shader had complete scalar constants
  (`smem_zero_filled=0`) and inherited black input. It was not the first stage
  where pixels became zero.
- Sony's `agc-registerstructs.natvis` identifies `CB_COLOR_CONTROL` mode 6 as
  `kDccDecompress`. This remains a general correctness contract, but the
  current `0x500690F00` boundary does not implicate it: the exact expanded host
  image selected for sampling is genuinely black in RGB.
- The historical slow `ps=0x5008F1400` loop walked a GPU-built linked list whose
  producer had previously been absent. Performance must be remeasured after the
  producer path is present before changing control-flow semantics.

## Next falsifiable checkpoint

Move upstream from the current `0x514080000` black-RGB source, and keep early
blank transition frames separate from the later title/world-map lifetime:

1. read back the exact `0x514080000` host image after each writer, with RGB and
   alpha counted separately, until the first writer that should produce title
   or world-map colour is identified;
2. retain the guest marker/state and shader addresses for that occurrence so a
   deliberately blank early frame is not mistaken for the persistent failure;
3. audit the observed rect-list draw that targets `0x514080000` but is skipped
   because the current vertex replay reports no parameter exports while its
   pixel shader requires two inputs. This is an **OPEN lead**, not yet a root
   cause: the submitted state includes a separate native primitive program at
   `gs=0x500705600`, so the ordinary-ES fallback may be discarding the stage
   that owns those exports;
4. compare that native-primitive contract with Sony's
   `agc_basic_geometry_shader/native_prim.pssl` and the preserved, tested NGG
   replay implementation before admitting or replacing the draw;
5. only after a writer produces nonblack RGB, follow its exact host-image
   identity through `6CB800 -> 690F -> 650600` and presentation.

This distinguishes the currently live root-cause classes:

- a required full-resolution producer/native-primitive draw is skipped or
  replayed without its output contract;
- the producer executes but writes black because one of its inputs or shader
  semantics is wrong; or
- a later writer/alias transition replaces valid colour before the observed
  `0x514080000` sample.

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

- Do not carry a nonblack result across runs or allocation lifetimes. The
  current native `0x514080000` image is black in RGB even though historical
  controls at the same guest address were nonblack.
- Do not infer colour from nonzero bytes in RGBA targets; alpha alone produced
  the misleading 2,081,320-byte count in the current run.
- Do not continue changing `0x500690F00`: exact bindings, constants, synthetic
  nonblack replay, native fetch behavior, ALU, coverage and stores are now
  separated, and its current source is genuinely black RGB.
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
