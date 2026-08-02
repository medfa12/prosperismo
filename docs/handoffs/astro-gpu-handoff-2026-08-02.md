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
- **CONFIRMED -- programmable image performance modulation is accepted:**
  Astro submits Sony image descriptors with `PERF_MOD` set. The field is a
  cache/performance hint, not an alternate image layout, and rejecting it
  stopped the postprocess sequence before the next observable boundary. The
  descriptor path now accepts the field without changing texture semantics.
- **CONFIRMED -- gfx1013 `s_cmp_eq_u64`:** the scalar decoder and SPIR-V
  lowering implement the 64-bit equality operation used after the postprocess
  sequence. Focused true, false and overlapping-register cases pass.
- **CONFIRMED -- Sony mip-range normalization:** Astro's storage descriptor
  `055ea000,c4700000,010dc1df,91b66fac,00000000,00700050,00000000,00000000`
  requests base/last mip 6 while `MAX_MIP` describes a six-level allocation.
  SDK 10's `agc/core/texture.h` defines `MAX_MIP` as the allocation level count
  and gives the explicit mip range precedence; it does not require the range's
  last value to be less than `MAX_MIP`. The host allocation remains six levels
  and the Vulkan view is clamped to its last real level (base 5, count 1),
  matching the independently proven implementation. The focused exact
  descriptor test passes, and a real boot passed the former crash and sustained
  at least frame 361.
- **CONFIRMED -- never-mapped unmaps do not drain the GPU:** a curated
  reference change demonstrated that Astro's wandering boot stall came from
  synchronously draining the GPU for ranges that had never acquired GPU
  resources. Prosperismo had the same unconditional path. The first port
  (`bcc4ff1`) avoided `SendCommandSync` but still called a shared closure whose
  first operation was `FinishCurrent()`; that moved the drain to the guest
  thread and raced the GPU recorder, producing either `VK_ERROR_UNKNOWN` from
  `vkEndCommandBuffer` or `!m_recording`. `4da4524` is the complete fix: after
  proving no cache overlap it performs only page/range bookkeeping, with no
  scheduler or cache drain. The following targeted run passed the former
  one-second failure, reached frame 1076 and captured the exact ES/GS pair. It
  was stopped after the retained measurement rather than allowed to grow the
  trace further. This is a boot-reliability fix, not an explanation for zero
  RGB.
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
- **CONFIRMED -- the first measured full-resolution writer chain remains RGB
  zero through the last ordinary pixel draw:** in the retained
  `20260802-115409-sony-mip-view` run, the compute writers `0x500571000`,
  `0x50059CD00`, `0x5005CC100`, `0x5005FDB00` and both occurrences of pixel
  shader `0x50074F400` executed at frames 120, 240 and 360. Every readback had
  zero RGB pixels and alpha in all 2,073,600 pixels. The immediately following
  geometry draw at `gs=0x500705600` was skipped each time with the exact state
  `stages=0x2030`, `prim_group=3`, `vert_group=24`, `ngg=0x46`,
  `max_out=216`, `gs_max_vert=72`, `gs_out_prim=2`. This is the current first
  unexecuted writer boundary; it is not yet proof that the draw itself would
  produce nonblack colour.
- **CAPTURED -- the exact skipped merged ES/GS programs are retained:** run
  `20260802-125515-native-ge-target-state` captured 224 bytes at
  `es=0x500704F00`, 3,408 bytes at `gs=0x500705600`, and the complete launch
  state. The state confirms wave64, a three-primitive hardware group ceiling,
  an ESGS ring item size of four dwords, 216 maximum output vertices and 210
  maximum triangle-strip primitives. `GE_CNTL.VERT_GRP_SIZE=24` is GFX10.1's
  minimum clamp, not evidence of 24 live input vertices. Actual vertex and
  primitive counts are per-wave launch values. These bytes, not analogy with an
  older particle/native-primitive shader, are the next offline replay input.
- **SONY-ORACLE DECODED -- the exact live replay contract is bounded:** the ES
  has 11 live instructions through `s_setpc_b64`; it writes one dword per lane
  to LDS with `ds_write_b32 v5, v8`, using SGPR3's wave index and the four-dword
  ESGS item size. The GS is valid through `s_endpgm` at `+0xB74` and has no
  `GS_EMIT` or `GS_CUT`. Wave zero packs dynamic vertex/primitive counts into
  `m0` and sends `GS_ALLOC_REQ`; the tail exports target-20 connectivity from
  `v12`, followed by POS0, PARAM0 and PARAM1. Bytes after the terminators are
  padding/data and are not opcodes. Prosperismo currently treats `s_sendmsg` as
  a no-op, drops target 20, has no geometry stage, and has no paired ES-to-LDS
  GS launch. Sony's SGPR3 ABI supplies workgroup size, wave index, GS wave ID,
  primitive count and vertex count; the exact draw packet must still provide
  topology, index count, instance count and therefore the actual per-wave
  counts. No additional live ALU opcode hole is proven at this boundary.
- **PACKET-MEASURED -- both observed launch shapes are required full-resolution
  writers:** the pair is submitted as a point-list auto draw with one vertex and
  one instance, and as a point-list indexed draw with one 16-bit index and 512
  explicit instances. Both use `ps=0x500707600`, MRT mask `0x7`, and first write
  the DCC-enabled `0x514080000` target before the same state appears at
  `0x5168C0000`. `GE_CNTL=0x00003003` enables multiple instances per wave;
  `VGT_GS_ONCHIP_CNTL=0x00C01818` supplies 24/3/3 capacity fields. The direct
  auto-draw packet path had also been discarding persistent `IT_NUM_INSTANCES`;
  `cd84068` now preserves that state. A correct replay must support both packet
  shapes and derive live per-wave counts rather than substituting the capacity
  fields.
- **OFFLINE BLOCKER MEASURED -- the replay needs 12 KiB of Workgroup LDS:** the
  exact GS uses static byte offsets through at least `0x2C48`, and its Sony
  `GS_RSRC2.LDS_SIZE` field is 24, or `0xC00` dwords. This checkpoint removes
  the former 1,024-dword compute hard-code: the register-derived allocation now
  reaches IR, SPIR-V Workgroup array sizing, LDS bounds and the shader cache key.
- **CLOSED -- compute-write to vertex/index/indirect-read visibility:** the
  generic shader-write barrier previously stopped at VertexInput; a compute
  pass that authors the indirect argument words of the following draw was not
  ordered against it. `MakeShaderWriteDependency` now carries
  `IndirectCommandRead` and the destination stage mask carries `DrawIndirect`
  (exposed as `ShaderWriteDestinationStages()`); both halves are pinned in
  `shader_cfg_tests`. This is a correctness contract for the coming replay's
  indexed-indirect draw, not itself a rendering fix.
- **CONTRACT EXTENDED -- live point-list subgroup schedules are derivable
  offline:** `TryPlanPointListGsSubgroups` derives per-subgroup live
  vertex/primitive counts and the owning wave count from the exact draw shape
  and the register-derived capacity limits, honoring GE_CNTL's
  multiple-instances-per-wave bit. Both measured Astro shapes are pinned in
  `native_primitive_replay_tests`: the auto 1x1 draw plans one subgroup with
  one live primitive, and the indexed 1x512 draw plans 171 subgroups of three
  primitives with a two-primitive tail. Unmeasured splits of a single instance
  across subgroups remain fail-closed. The counts compose directly with the
  SGPR3 `TryPackGsWaveLaunch` ABI; the ES/GS SPIR-V translation, LDS ring
  handoff, export capture and the indirect replay itself remain unimplemented.
- **VISUALLY NEGATIVE -- the corrected-drain run still has no recognizable
  guest scene:** `PrintWindow(PW_RENDERFULLCONTENT)` at frame 590 is retained as
  `checkpoint-frame-current.png`. Excluding the Windows title bar, all 943,488
  client pixels are grayscale with channel values only 0 through 13 and zero
  chromatic pixels. The faint patterned background is not title/world-map
  rendering; the missing ES/GS writer remains live.
- **CLASSIFIED, ROOT CAUSE OPEN -- frame-361 host termination:** Windows
  recorded `0xc0000409` with exception data `7`. Microsoft SDK `winnt.h`
  defines value 7 as `FAST_FAIL_FATAL_APP_EXIT`, not a stack-cookie failure.
  The log ends mid-command without a Prosperismo fatal report and WER retained
  no dump, so the initiating `abort`/`std::terminate` path is not identified.
  Do not conflate this explicit host termination with the skipped geometry
  writer or the previously fixed unmap stall.

The retained current run artifact is
`artifacts/astro-runs/20260802-125515-native-ge-target-state`; failed diagnostic
launches and older large probe directories were deliberately pruned after their
measurements were distilled into this handoff. The focused selector
`shader_recompiler_compute_tests.exe --vop3-u64-compare-only` passes the compare,
borrow, trap, BVH-miss, 64-bit-FF1 and dynamic-SMEM GPU semantic tests. The unfiltered
suite currently stops earlier in the pre-existing `ImageTransitionState`
depth/stencil mip-copy test; it is not claimed green.

## Established boundary

- Historical SharpEmu captures contain nonblack full-resolution scene targets
  and one source-triggered final-tonemap control. At work sequence 992427 the
  unmodified `ps=0x500640D00` read 2,146,458 nonblack RGBA16F source pixels and
  wrote 4,951,550 nonblack A2R10G10B10 pixels; `PrintWindow` showed the dark
  blue-strip PlayStation Studios image. All 350 PS and 69 VS instructions agreed
  with Sony's ISA oracle by mnemonic and size. This proves that the inherited
  pipeline can carry real RGB through the final tonemap, but it does not establish
  the content of Prosperismo's current allocation lifetime. In the current
  frame-3 lifetime, `0x514080000` and the following `0x53AA00000` RGBA16F
  image are byte-identical and have zero nonblack RGB pixels; their nonzero-byte
  counts are alpha. Never use a byte count as a pixel finding.
- The downstream 960x540 G-buffer/postprocess inputs examined at the clustered
  lighting boundary were all zero and were DCC-flagged.
- In the measured current black lifetime, the final tone-map pixel shader had
  complete scalar constants (`smem_zero_filled=0`) and inherited black input. It
  was not the first stage where pixels became zero. Do not generalize that
  occurrence into a claim that the shader or final chain is intrinsically black.
- Sony's `agc-registerstructs.natvis` identifies `CB_COLOR_CONTROL` mode 6 as
  `kDccDecompress`. This remains a general correctness contract, but the
  current `0x500690F00` boundary does not implicate it: the exact expanded host
  image selected for sampling is genuinely black in RGB.
- The historical `ps=0x5008F1400` slowdown is closed. Ordinary LDS accesses had
  a separate memory domain from DS atomics, and inactive lanes overwrote lane
  zero's `0xFFFFFFFF` sentinel at byte `0x510`. Sharing the Workgroup atomic
  domain and suppressing inactive stores changed the historical mean from
  132.039 ms to 0.238 ms (about 560x). Prosperismo must still measure its own
  shader timing, but the producer/list root cause is no longer open; `3249e35`
  ports the shared LDS domain.

### Preserved contracts and retired leads

- Sony modes 5 and 6 are compositional metadata operations. Mode 5 performs
  FMASK decompression plus fast-clear elimination; mode 6 adds DCC decompression.
  An initialized newer GPU-authored expanded image must be preserved. Recognized
  guest constant-metadata state is materialized in writer order; unknown or
  partial compressed state remains fail-closed. `libSceAgcGpuAddress` is an exact
  layout oracle, not a DCC/HTILE pixel decoder.
- Pixel address, DCC/HTILE allocation, host representation and guest writer
  serial are separate identity dimensions. Sampled, storage and render roles keep
  the exact metadata lifetime. If multiple compatible color/storage/depth host
  objects alias one guest range, select the newest initialized guest writer;
  host-object initialization is not itself a guest write.
- The presentation queue must retain a completed head while bounding the newer
  incomplete tail. Dropping the oldest completed entry can freeze a stale frame
  even while guest rendering advances; this remains an inherited audit item, not
  a proven cause in the current run.
- Native launch counts are per wave, physical local IDs include the wave index,
  only one wave owns `GS_ALLOC_REQ`, and target 20 packs three 10-bit
  subgroup-relative indices. A host path is valid only after checking the complete
  output-memory limit, not just vertex/primitive counts. `GE_CNTL`'s 24-vertex
  group field is a hardware minimum clamp and must not be used as the live count.
- Do not revive the old unbound-SMEM/forced-exposure tonemap theory, blame
  `0x500690F00` decode/NSA/store/ALU, infer pixels from nonzero bytes, or treat
  `LevelDocument Loaded: worldmap` as visible/interactable worldmap. Those leads
  are contradicted by later paired captures or measure only preload.
- Sony ShaderIsa raw options are terminated `(id,value)` pairs; id 1 with value
  2 selects the lowest generation accepting Astro's gfx1013 BVH form.
  `GetInstructionSize` takes the raw zero-extended instruction value, not a byte
  pointer. Firmware symbol-carrier ELFs with `SHT_NOBITS` text establish names and
  `st_size`, not executable behavior.

## Next falsifiable checkpoint

Update 2026-08-02 (later session): items 3-5 below are DONE and verified live.
The merged `0x500704F00`/`0x500705600` pair now replays as a 256-invocation
compute pass writing positions, two parameter sets and target-20 connectivity
into a persistent replay buffer, followed by a flat triangle-list draw whose
synthetic vertex shader fetches from it (commits `80bcea5`, `134c788`; the
compute-write barrier already reached vertex/index/indirect reads since
`4da4524`). Both measured launch shapes execute in a live Astro boot with zero
Vulkan errors: auto 1x1 (1 subgroup) and indexed 1x512 (171 subgroups x 3
primitives). Unsupported shapes remain fail-closed. The synthetic vertex half
must be compiled with the graphics lane-mask mode, not native-wave: the
measured host only offers required-subgroup-size control for compute.
The presented frame is still black, indistinguishable from the pre-replay
baseline, so the remaining work is items 1 and 6: writer-by-writer readback of
`0x514080000` for nonblack RGB.

A comparison clone of the Stepz97/ps5emu fork (which shows glitchy ASTRO BOT
frames on macOS) confirmed its `ShouldSkipGeShader` is byte-identical to ours
minus the replay: it also skips these classic-GS draws. Its visible frames come
from presentation-path work (linear framebuffer presentation, queue waits in
graphicsRun, scan-out readback/dump, CP-DMA drain no-ops, GPU-written SRT
pull-back), which is the right audit list if `0x514080000` turns out to hold
colour that never reaches the swapchain.

Move upstream from the current `0x514080000` black-RGB source, and keep early
blank transition frames separate from the later title/world-map lifetime:

1. read back the exact `0x514080000` host image after each writer, with RGB and
   alpha counted separately, until the first writer that should produce title
   or world-map colour is identified;
2. retain the guest marker/state and shader addresses for that occurrence so a
   deliberately blank early frame is not mistaken for the persistent failure;
3. ~~carry the exact 12-KiB LDS allocation through compute compilation and
   bounds, then add the compute-write to vertex/index/indirect-read barrier
   required by the following indexed-indirect draw~~ -- both halves are done:
   the LDS allocation reaches IR/SPIR-V/cache-key, and the shader-write
   barrier now reaches DrawIndirect/IndirectCommandRead;
4. replay the captured `0x500704F00` / `0x500705600` pair as a merged ES/GS
   compute pass. The recompiler half of this is now DONE: the host merges the
   pair byte-wise (`TryMergeEsGsForReplay`, terminal ES `s_setpc_b64 s[6:7]`
   patched to `s_nop`), and the recompiler compiles the merged program as
   Compute in geometry-replay mode (`ShaderComputeInputInfo::geometry_replay`).
   In that mode POS/PARAM/PRIM exports become storage-buffer writes into a
   per-subgroup replay block (layout `IR::GeometryReplayLayout`: pos[216]vec4 |
   params[n][216]vec4 | prim[210] | counts[2], 8-dword header), wave-zero
   `GS_ALLOC_REQ` stores the m0 vertex/primitive counts, and the emitter seeds
   v0 = tid*4 (ESGS ring offset), v8 = global instance index and the packed
   SGPR3 wave-launch word. The captured pair recompiles offline to validating
   SPIR-V (`shader_cfg_tests --geometry-replay-only`, artifact-gated).
   Remaining host half: allocate/fill the replay SSBO (prim slots pre-filled
   `0x80000000`), dispatch subgroup-count workgroups at the measured launch
   shapes (auto 1x1 and indexed 1x512), barrier, then a synthetic passthrough
   draw expanding target-20 connectivity;
5. keep unsupported launch shapes fail-closed. A register classifier that
   recognizes the state is useful, but it is not a render fix without the
   ES/GS ring, output allocation, vertex/primitive export and indirect replay;
6. only after a writer produces nonblack RGB, follow its exact host-image
  identity and writer serial through the observed lifetime. Do not assume one
  fixed route: preserved runs contain both `6CB800 -> 690F -> 650600` and
  `6CB800 -> 6CE200 -> 68FA` lifetimes.

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
