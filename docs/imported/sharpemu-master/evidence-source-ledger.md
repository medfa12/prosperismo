<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Evidence source ledger

Last audited: 2026-08-02.

For the current repository-wide conclusion, see
`docs/source-alignment-audit.md`. This ledger remains the claim-by-claim
evidence record; chronological title journals remain historical unless their
newest section explicitly supersedes an older result.

This is the durable map of what the local reference trees can establish. It
exists to preserve confident findings and failed leads between sessions without
promoting a plausible implementation into a Sony contract.

## Evidence labels

- **AUTHORITATIVE** — a Sony SDK declaration/sample or an exact Sony
  firmware/title binary for the version and layer named.
- **PRIMARY IMPLEMENTATION EVIDENCE** — code or toolchain documentation from
  the engineers who implemented a working target: AMD/LLVM ROCm,
  fail0verflow's Prosperous, both generations of the payload SDK, and the PS5
  Linux patches. These are curated engineering artifacts from concrete,
  exercised systems—not generic community reports. This is strong evidence
  for the layer and revision implemented; it is not downgraded merely because
  it is outside Sony.
- **CONFIRMED** — directly measured in SharpEmu from an exact shader, command
  stream, resource capture, disassembly, or guest-pixel capture.
- **CORROBORATED** — independently implemented or documented by a concrete
  toolchain/project and consistent with authoritative or direct evidence.
- **REFUTED/DEAD END** — a hypothesis tested against discriminating evidence
  and found not to explain the measured failure.
- **ASSUMED** — not yet grounded. It must never silently become a default,
  fabricated return value, or compatibility fix.

Authority is claim-specific, not a reputation ladder. Sony's SDK defines AGC's
public contract; a title capture proves which path Astro used; firmware proves
the implementation in one firmware version; LLVM/ROCm defines the toolchain's
`gfx1013` instruction syntax; the payload SDKs prove the interfaces and build
surface used by real payloads; Prosperous proves what its working exploit,
kernel, physical-memory, PCIe, and IOMMU paths require; and the Linux patches
prove what their working PS5/BC-250 kernel and driver enablement requires. None
of those sources should be stretched beyond that layer, but none should be
discarded under a generic “community” label either.

## Local implementation references

| Tree | Audited revision | What it can establish | Limit |
|---|---|---|---|
| `inspiration/ps5-payload-sdk` | `a0d2bc60bdcc0a5ee9e790fa3b02fe5051a152d0` | **PRIMARY IMPLEMENTATION EVIDENCE:** maintained payload-development SDK, syscall/runtime code, NID tool, current stub-name census | Generated stubs contain names, not function contracts |
| `inspiration/ps5-payload-sdk-legacy` | `4bdc3fb919483a74199f09661692d9fb746e6b6b` | **PRIMARY IMPLEMENTATION EVIDENCE:** preserved predecessor SDK for payloads running through real PS5 ELF loaders; stable historical ABI/build comparison point | Superseded layout and smaller stub surface; prefer the maintained successor for current names |
| `inspiration/prosperous` | `663b2cd041fce6b2c48151082f85cbfdd12f4d5d` | **PRIMARY IMPLEMENTATION EVIDENCE:** fail0verflow's working exploit, kernel, physical-memory, PCIe, and IOMMU research | Hard-coded RVAs and internal kernel bits are version- and layer-specific |
| `inspiration/ps5-linux-patches` | `daa2e496086ae0c9fe22205f703925a2e2596185` | **PRIMARY IMPLEMENTATION EVIDENCE:** working Linux enablement for the PS5-derived BC-250 APU | Linux policy and BC-250 configuration do not define retail AGC behavior |

These are shallow clones at the revisions above. “Shallow” limits local history,
not the evidentiary value of the checked-out code.

### Concrete facts from those projects

- **PRIMARY IMPLEMENTATION EVIDENCE:** the maintained payload SDK contains
  `host/bin/prospero-nid.c`. It computes the known Prospero NID form from
  SHA-1 of the symbol name plus the fixed 16-byte salt and the PlayStation
  base64 alphabet. The old statement that this file is absent applied only to
  the preserved legacy checkout.
- **CONFIRMED:** maintained `sce_stubs/*.c` files are symbol declarations, not
  signatures or behavior. The local generator reads a separately supplied
  `aerolib.csv`; that database is absent from the checkout, so the generator is
  not a self-contained offline authority.
- **CORROBORATED:** the maintained stub tree has 8,289 declarations in 30
  files, a strict 2,435-symbol superset of the legacy tree's 5,854 declarations
  in 12 files. This is useful for name/surface discovery only.
- **PRIMARY IMPLEMENTATION EVIDENCE:** the Linux patch identifies GC IP
  `10.1.3` for the
  PS5-derived APU and configures it as an APU. Its fixed
  `wave_front_size = 32` is a Linux-driver configuration fact, not proof that
  every Sony shader stage is wave32.
- **PRIMARY IMPLEMENTATION EVIDENCE:** the same patch admits the device through
  AMD's `gfx_v10_0` path and records an empirically verified BC-250 result:
  clearing only `CC_GC_SHADER_ARRAY_CONFIG` changes CU enumeration, while
  dispatch to the harvested WGPs additionally requires
  `SPI_PG_ENABLE_STATIC_WGP_MASK`. This is direct hardware/driver evidence for
  that PS5-derived target, not a retail AGC command-stream contract.
- **PRIMARY IMPLEMENTATION EVIDENCE:** the Linux patch is broader than a GPU-ID
  note: it carries PS5 platform, PCI, storage, display, audio, firmware-loading,
  and `amdgpu` changes needed to boot a real kernel on the hardware. Individual
  hard-coded values still need their target and kernel revision attached.
- **PRIMARY IMPLEMENTATION EVIDENCE:** Prosperous contains executable exploit
  and kernel payload paths plus concrete physical-memory, PCIe configuration,
  SMN, and IOMMU access code. Its hard-coded addresses and private protection
  bits are measurements of that implemented target, not portable public SDK
  constants.
- **PRIMARY IMPLEMENTATION EVIDENCE:** AMD/LLVM's
  [`gfx1013` instruction page](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX1013.html)
  explicitly refers all other instructions to
  [`GFX10 RDNA1`](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX10.html)
  and lists the gfx1013-specific BVH intersection instructions. SharpEmu must
  therefore use the gfx1013/Sony hybrid table, not a generic RDNA1 or RDNA2
  table.

## Sony sources in `games/`

### Ground-truth coverage snapshot (2026-08-02)

Read this inventory before requesting or downloading another reference. It
measures evidence available locally, not emulator compatibility or implemented
coverage.

- **AUTHORITATIVE, COMPLETE:** the Sony SDK 12 Shader Core ISA Specification
  contains all 80 local page files.
- **AUTHORITATIVE, PARTIAL:** the accompanying Instruction Reference contains
  47 unique numbered pages out of roughly 547 (plus a table of contents and one
  duplicate page), or about 8.6%. It does not contain instruction bodies for
  vector memory, LDS/GDS, image, FLAT, or export. Do not infer absence from a
  failed grep of this capture.
- **AUTHORITATIVE EXECUTABLE ORACLE:** SDK 10.00 includes the working
  `libSceShaderIsaP.dll`, the working PSSL compiler, and 1,197 `.pssl` source
  files. `SharpEmu.Tools.ShaderIsaOracle` has recovered the raw-disassembly
  interface and can differentially check production decoding. LLVM gfx1013
  machine-code tests and the RDNA1 ISA guide cover encodings for families
  missing from the Sony page capture, without overruling Sony where Sony text
  exists.
- **AUTHORITATIVE, IMPLEMENTATION SOURCE PRESENT:** SDK 10.00 contains
  `sdk/target/include_common/agc_gpu_address.h` and 17 implementation/generated
  files (about 4.2 MB) under `sdk/target/src/agc_gpu_address`, in addition to
  release/debug host DLLs, import libraries, and target static libraries. This
  is Sony's actual tiling, swizzle, mip, DCC-key, HTILE, and CMASK address
  implementation. Do not guess this math or treat the DLL as an opaque last
  resort.
- **AUTHORITATIVE SAMPLE COVERAGE:** the SDK contains 68 AGC target headers,
  185 target `.a` libraries, 21 host DLLs, a 24-file metadata-compression suite,
  Sony's native-primitive/geometry/mesh samples, and the 230-file
  `tiled-deferred-tutorial`. These directly cover DCC, HTILE, CMASK, FMASK,
  mip construction, NGG, mesh, and the deferred-rendering pattern used by
  Astro.
- **AUTHORITATIVE, VERSION-BOUNDED:** local clear firmware trees cover 3.02,
  4.03, 9.00, and 12.40; `firmware_kernels` contains 14 clear kernel ELFs; and
  `prospero-firmware-symbols` contains 2,369 carriers across 13 early firmware
  versions. The external `system_system_ex_database.part` extraction supplies
  exact clear 10.00-era bodies. Join symbol names and `st_size` to the exact
  matching body; never carry RVAs, offsets, or behavior between versions.
- **MISSING:** the SDK tree contains no CHM/PDF prose manual or PDB, and the
  complete `games/` tree contains zero `.rgp`, `.razor`, `.pscapture`, `.pix3`,
  `.wpix`, `.renderdoc`, or `.rdc` GPU captures. RazorGPU and ShaderDebugger
  installers are present under `games/prospero-sdk-10.00-tools`, but installers
  are not execution evidence.

Planning judgement, explicitly not a conformance metric: the local trees cover
roughly 85% of the obtainable ground-truth *categories*. The limiting factor is
now converting existing material into automated contracts and emulator
behavior, not collecting more generic AMD/community documentation. If new
material can be supplied, prioritize: (1) a real Astro Razor capture at the
full-resolution-to-half-resolution boundary, (2) the missing Sony instruction
reference pages, then (3) SDK prose manuals/PDBs or named 10.x symbol carriers
that can be joined to the clear 10.x bodies already present externally.

| Tree | Provenance and proper use |
|---|---|
| `games/prospero-sdk-10.00` | **AUTHORITATIVE** Prospero Programmer Tool Runtime Library 10.00.00.40. Prefer its headers, register structures, address-library source/DLL, shader ISA DLL, compiler, and samples for public GPU contracts. The completed extraction recovered 971 previously missing files, including host binaries and target static libraries. |
| `games/prospero-firmware-symbols` | **AUTHORITATIVE for the carrier metadata present:** 2,369 early DevKit/TestKit ELF symbol carriers spanning firmware 0.83–1.14. They expose names, bindings, addresses, and `st_size`; sampled `.text` sections are `SHT_NOBITS`/`0x10`, so they do not contain function bodies and cannot establish behavior alone. Join them to a clear exact-version firmware module and SDK declaration. |
| `games/gpu shit_forzen/GPU Shader Core ISA * - SDK 12.000` | **AUTHORITATIVE for the pages present:** Sony/AMD GPU Shader Core ISA material from PS5 DevNet SDK 12.000. The local capture is only about 47 pages of a roughly 547-page instruction reference and lacks instruction bodies for vector memory, LDS/GDS, image, FLAT, and export. The adjacent `_text` extraction is a search index; absence there is not evidence of ISA absence. |
| `games/3.02` | **AUTHORITATIVE for the contained binaries.** Clear modules plus derived stub-call material. Keep binary evidence separate from generated analysis. |
| `games/PS5_4.03_reconstructed` | Mixed tree. Firmware binaries are exact evidence where present; the README records missing protected modules and reconstruction. Do not call the whole directory byte-faithful. |
| `games/PS5_9.00_decrypted` | Partition trees are recorded as byte-faithful; `_analysis` is derived. Keep those provenance classes separate. |
| `games/12.40 system dump` | Partition trees plus an `_anomalies` quarantine. The quarantined `FAKE00000` duplicate is not a real title identity. |
| `games/firmware_kernels` | Fourteen clear kernel ELFs, versions 11.00–13.42. Authoritative for the exact kernel binary being inspected, not for an older firmware's private layout. |

The local `libSceAgc`, `libSceAgcDriver`, `libSceGnmDriver`, `libSceVideoOut`,
and `libkernel` binaries differ in size and hash across 3.02, 4.03, 9.00, and
12.40. This proves they are versioned oracles. Symbolise every firmware
function by the symbol's `st_size`; never use the nearest preceding export and
never carry an RVA across versions without re-deriving it.

### External clear-firmware database (2026-07-31)

`C:\Users\sharpemu\Downloads\system_system_ex_database.part\system_system_ex_database`
is a read-only local evidence source, not a runtime dependency and not copied
into the repository. The indexed extraction contains 38,047 files totaling
41,396,433,367 bytes, with version families 1 through 13. Firmware family 10
contains 10.00, 10.01, 10.20, 10.40, and 10.60.

- **AUTHORITATIVE for each exact binary:** the relevant 10.00 `.sprx` files are
  clear FreeBSD x86-64 `SCE_DYNAMIC` ELFs, not opaque SELF containers. They
  contain executable bodies, dynamic symbols with exact `st_size`, relocations,
  strings, and dependency metadata. Sony SDK 10's `prospero-llvm-readelf.exe`
  reads them directly. `llvm-nm` saying "no symbols" is not evidence of no
  symbols; these images expose their NID-bearing dynamic table without ordinary
  section headers.
- **AUTHORITATIVE + CONFIRMED:** Astro's exact eboot has 1,734 unique NIDs;
  the current `inspiration/ps5rs/data/nids.csv` resolves 1,724 (99.4%). The
  eboot imports 11 resolved `sceVideoOut*`, 71 resolved `sceAgc*`, and 17
  resolved `sceAvPlayer*` names, and the corresponding 10.00 modules contain
  those implementations. SharpEmu registers these title-facing names, but its
  normal Windows boot remains HLE; this join identifies which firmware bodies
  must be used as behavioral oracles.
- **Exact examples, symbolised by `st_size`:** 10.00
  `libSceAgcDriver.sprx` places `sceAgcDriverSubmitDcb` at vaddr `0x2970`,
  size 15; `sceAgcDriverAgrSubmitDcb` at `0x2980`, size 81; and
  `sceAgcDriverSubmitAcb` at `0x29E0`, size 67. `libSceAgc.sprx` places
  `sceAgcSuspendPoint` at `0x8000`, size 148 and `sceAgcDcbSetFlip` at
  `0x7390`, size 171. `libSceVideoOut.sprx` places
  `sceVideoOutRegisterBuffers2` at `0x11890`, size 242. `libSceAvPlayer.sprx`
  places `sceAvPlayerGetVideoDataEx` at `0x2F50`, size 420.
- **Version boundary:** equal file length does not mean equal implementation.
  Comparing 10.00 with 10.60 finds 79,383 differing bytes in `libSceAgc`, but
  only 31 in `libSceAgcDriver`, 30 in `libSceVideoOut`, and 38 in
  `libSceAvPlayer`. Those counts are facts about whole files, not proof that
  the latter function bodies are identical; disassemble the exact symbol range
  before carrying a contract across versions.
- **Dead end closed:** absence of a firmware body in the previously copied
  `games/` subset is no longer evidence that it is unavailable locally. Check
  this extraction before concluding that a Sony implementation cannot be
  inspected. Conversely, the presence of these bodies does not make broad LLE
  viable on Windows: raw FreeBSD syscalls and `/dev/gc` ioctls still require
  translated HLE, and ioctl `0xC0488131` remains outside the Astro render fix.

The 10.00 reference hashes used for the first joins are:

| Module | Bytes | SHA-256 |
|---|---:|---|
| `libSceAgc.sprx` | 366,512 | `10584957393E4BA441C8A5B259F00EE2BFB562C00D5A70BA3F9F1EC8AB3D1446` |
| `libSceAgcDriver.sprx` | 174,024 | `F3DDE6D5C92903DC5C778CAD8DAD92308A29FE9B60F1BCBEBCD755F7FBCEF98E` |
| `libSceVideoOut.sprx` | 244,452 | `8E7CFAD2FB4737636383FD87639066E6A50B1F9E5730D30346065E6A55615669` |
| `libSceAvPlayer.sprx` | 379,084 | `2CF5B55F92A3A13F7C4199904169B1A95B15389642B147F064680EB922154275` |

### Sony firmware symbols and VideoOut facts

- **AUTHORITATIVE + CONFIRMED, PARTIAL HLE FIX:** all 23 early
  `libSceVideoOut.prx` carriers name
  `sceVideoOutSubmitChangeBufferAttribute2`; SDK 10.00 defines its four-argument
  ABI and `0x50`-byte attribute, and exact clear 4.03/9.00 bodies agree on the
  outer validation and callback flow. SharpEmu now registers Astro's
  `HuViW4HnrOw` NID and implements the public null-option path. It does not
  emulate the private driver callback or the registered-allocation growth
  constraint. The same SDK layout exposed and fixed the shared v2 parser's
  discarded `aspectRatio` and `pitchInPixel` fields; it now reads offsets
  `+0x08` and `+0x14` instead of inventing `pitch = width`. Retained Astro
  traces contain no submit-change call, so closing that import surface gap does
  not explain the black postprocess chain; the corrected parser also serves
  `RegisterBuffers2` and is a separate general data-path fix.
- **REFUTED AS A VIDEOOUT DEFECT:** Astro's high-frequency
  `sceVideoOutIsFlipPending` trace is not proof that the API should return a
  boolean. Sony SDK samples treat the result as a pending count, including
  comparisons against the number of registered buffers; SharpEmu already
  returns that count. The observation is consistent with guest polling during
  a slow GPU backlog.

### Sony GPU facts relevant to Astro

- **AUTHORITATIVE:** SDK 12's shader-core specification defines scalar operand
  125 as `NULL`: reads yield zero and writes are ignored. It also states that
  non-pixel stages default to wave32 and pixel shaders default to wave64,
  subject to the program's explicit mode.
- **AUTHORITATIVE:** SDK 10.00 `libSceShaderIsaP.dll` decodes and sizes raw
  instructions, including families absent from the local SDK 12 text capture.
  Its generation option must be explicit: null options select generation zero
  and can report valid Prospero 64-bit compare and BVH encodings as invalid.
  The checked-in differential tool recovered the raw-disassembly options as a
  terminated array of `(uint64 id, uint64 value)` pairs, with id 1 selecting
  generation; generation 2 is the lowest value accepting Astro's exact BVH
  instruction. `sceShaderIsaGetInstructionSize` takes the first instruction's
  raw 64-bit value, not a pointer. Its result must agree with the size in the
  returned JSON before a vector is trusted.
- **PRIMARY IMPLEMENTATION EVIDENCE:** local LLVM gfx1013 tablegen plus its
  byte-exact MC fixtures covers the missing vector-memory, LDS/GDS, image,
  FLAT, and export families. AMD's RDNA1 reference is the gfx10.1 baseline.
  These fill a capture gap; they do not overrule a Sony oracle result.
- **CONFIRMED GENERAL DEFECT, FIXED:** LLVM's GFX10 fixtures and Sony's oracle
  agree that MUBUF opcodes `0x71` and `0x72` are `buffer_gl0_inv` and
  `buffer_gl1_inv`. SharpEmu previously emitted raw unknown names. They now
  decode exactly and lower to a Vulkan device-scope acquire barrier for
  Uniform/Storage memory; Vulkan exposes visibility semantics, not separate
  GL0/GL1 cache invalidation instructions.
- **AUTHORITATIVE + PRIMARY IMPLEMENTATION EVIDENCE, FIXED:** Sony SDK 10.00's PSSL compiler emits
  `scratch_load_dword v5, v2 offset:0x0004` as
  `04 40 30 DC 02 00 7D 05`, and `libSceShaderIsaP.dll` accepts it under the
  proved Prospero generation. LLVM's GFX10 fixtures independently agree that
  FLAT-family load destination is bits 24..31 of the second DWORD, while bits
  8..15 are store/atomic data. SharpEmu now keeps `vdata` and `vdst` separate,
  masks the signed 12-bit immediate independently of bit-12 `dlc`, and decodes
  the bounded vector scratch dword load/store family. Vulkan provides
  per-invocation Private scratch sized from Sony's graphics/compute ring-size
  registers; missing or incompatible allocation and scalar-address mode fail
  loudly. Exact operand and SPIR-V tests preserve the Sony/LLVM-agreed
  zero-offset DLC vector `00 90 30 DC 03 00 7D 01`. Retained Astro traces
  contain zero occurrences, so this fix is not evidence for Astro causality.
- **AUTHORITATIVE EXECUTABLE DIFFERENTIAL, BOUNDED:** the ShaderIsa oracle now
  parses Sony's structured operand/option JSON and LLVM's common two-line
  fixture form. All 48 Sony-accepted vectors in
  `flat-scratch-instructions.s` agree on name, size, operands and cache
  modifiers; the supported vector byte/short/D16 accesses lower to bounded
  per-invocation Vulkan storage. The Sony/LLVM-agreed 32-bit integer GLOBAL
  swap, compare-swap, add/subtract, signed/unsigned min/max, and bitwise atomic
  forms now lower through Vulkan atomics. Compare-swap preserves the hardware
  data-pair order: the low VGPR is the new value and the high VGPR is the
  comparator; GLC returns the old value through the separately encoded vdst.
  The first 40 `flat-global.s` vectors now match 40/40. Across the first 100,
  all 66 implemented forms match and the other 34 are honest decode failures
  for wrap-clamp increment/decrement or 64-bit/x2 atomics. Those remain
  unsupported because Vulkan's plain increment/decrement and 32-bit lowering
  are not exact substitutes. SDK 10.00 omits negative FLAT-family immediates
  from its JSON, so those remain byte-exact LLVM evidence and are counted as
  unreported rather than falsely certified by Sony output.
- **AUTHORITATIVE:** SDK 10.00 `libSceAgcGpuAddress.dll` reports the exact
  surface summary and tiled element offset. For Astro's mode-27 960x540
  RGBA16F surface, the previous SharpEmu equation disagreed at 450,304 of
  518,400 visible texels even though total allocation size matched.
- **AUTHORITATIVE, COMPLETE FOR A BOUNDED MATRIX:** SDK 10.00's public
  `AgcGpuAddress::TileMode` exposes tiled modes 1, 5, 9, 17, 24, and 27; raw
  values 4 and 8 are reserved. A direct `detileSurface` differential initially
  found 13 failures: the two reserved modes were trusted, public mode 17 was
  refused, and production bytes differed for every tested mode-1 and mode-24
  element size plus mode-27 at four bytes per element. After correction, all
  29 valid public 2D/single-mip/single-slice/single-sample combinations match
  Sony byte-for-byte at 257x193 and 960x540.
- **AUTHORITATIVE, BOUNDED MULTI-MIP CHECK:** the same SDK DLL's surface summary
  reports Minecraft's 2048x1024, four-level, mode-27 atlas as 0xAA0000 bytes
  with mip offsets `0x2A0000`, `0xA0000`, `0x20000`, and `0`; production matches
  every offset and size. A 256x128, nine-level case also matches the SDK's first
  tail level and every mip-tail coordinate. Coverage still does not include
  arrays, volume layouts, MSAA, or compression metadata.
- **AUTHORITATIVE:** SDK 10.00 `agc/registerstructs.h` defines
  `CxCbControl::Mode::kDccDecompress = 0x60` and says it decompresses DCC while
  implicitly eliminating fast clear and decompressing FMASK.
- **AUTHORITATIVE:** the tiled-deferred sample creates DCC-enabled G-buffers,
  clears each DCC target with `Toolkit::clearRenderTargetCs(..., 0)`, fills the
  G-buffers, and performs the required depth transition. A raw zero backing
  store is not a general substitute for compressed-surface metadata state.
- **AUTHORITATIVE:** `sys/dmem.h` defines public
  `SCE_KERNEL_PROT_GPU_READ = 0x10` and `SCE_KERNEL_PROT_GPU_WRITE = 0x20`.
- **CONFIRMED:** Astro's full-resolution HDR scene target `0x514080000`
  contained 869,977 nonblack pixels. The discriminating failure is downstream:
  the 960×540 inputs consumed by clustered lighting were zero.
- **CONFIRMED:** final tonemap shader `0x500640D00` had complete scalar
  constants (`smem_zero_filled=0`) and inherited black; it was not the first
  black-producing stage.
- **CONFIRMED:** all 13 retained material draws using
  ES `0x5002AA400` and PS `0x5002AFC00` selected one of three captured BC7
  sources at PC `0x01D0`. The ES exported `(5,5)` and sampler mode 2 clamps to
  edge; an offline decode found the bottom-right texel exactly `(0,0,0,255)`
  in all three sources even though each texture contains hundreds of thousands
  of nonblack texels. Forcing only that exact binding white proved the path
  could write, but did not make the original shaders wrong.
- **CONFIRMED:** the active boundary remains the 49-writer interval for the
  960×540 target `0x53AA00000`, followed by PS `0x50063F800`, which samples it
  black and replaces the full-resolution scene RGB.
- **CONFIRMED:** submitted DCB markers name that fullscreen draw
  `/Main_0_0_0/JsParticleHalfResolution_0`. It is an optional particle overlay,
  not an unidentified transition copy.
- **CONFIRMED, exact live shader:** `ps=0x50063F800` saves EXEC, enters WQM,
  compares RGB with zero but alpha with `1.0`, and executes its sole
  `done vm` MRT0 export for the measured byte-zero `(0,0,0,0)` input. Sony SDK
  10.00's `libSceShaderIsaP.dll` decodes exact live PC-`0x2c` bytes
  `F906047CF2808606` as `v_cmp_eq_f32 s[0:1], 1.0, v3`; LLVM independently
  assigns inline encoding `242` to FP32 `1.0`.
- **CONFIRMED GENERAL DEFECT, NOT CAUSAL FOR THIS DRAW:** RDNA1 ISA section
  11.2.1 says EXEC predicates every export; LLVM gives EXP implicit use
  `[EXEC]`. Commit `c0c2b08` accumulates per-lane mapped color-export coverage
  and uses it for the color epilogue. The exact Astro occurrence executes EXP,
  so this correction does not explain its black replacement.
- **LIVE-EXERCISED, visual result NEGATIVE:** run
  `20260730-224023-corpus-gate` compiled the corrected shader and reached its
  exact marked draw. Immediate `PrintWindow(PW_RENDERFULLCONTENT)` capture
  succeeded, but all 1,015,250 pixels below the emulator HUD had RGB exactly
  zero. The export fix is therefore insufficient for visible output. This
  window capture does not say whether `0x514080000` was preserved locally;
  that exact pre/post target readback remains the next discriminator.

- **CONFIRMED OFFLINE:** Astro's exact 2,858-instruction amplified native-GS
  program is embedded in `eboot.bin` at file offset `0xE2FDEAC`. Its `v39`
  output index equals physical local invocation `(waveIndex<<6)+lane`; active
  primitive and vertex lanes are dense prefixes. Its allocation request is
  owned only by wave 0, exactly matching SharpEmu's local-invocation-zero
  indirect writer.
- **CONFIRMED:** retained replay allocation `0x1001` is the guest shader's
  deliberate one-vertex/one-primitive degenerate fallback after zero
  structured-input survivors. The measured PC-`0x0058` record was left zero
  by particle compute `cs=0x555F4F500`; that optional-empty particle path is
  not proof that a base postprocess surface should contain color.

## Refuted leads and dead ends

Record a dead end only with the measurement that killed it.

- **REFUTED:** the unique eboot pixel shader at file offset `0xE15C418` is
  runtime `ps=0x50063F800`. The archive proves it is a real two-image,
  one-SMEM pixel shader, but retained runtime static discovery has one image
  and zero SMEM sites. Their current generated SPIR-V sizes also differ
  materially. No loader relocation or live-byte hash connects them.
- **CONFIRMED implementation defect; fixed by `d359578`; Astro occurrence
  UNMEASURED:** decoded shader programs were cached only by code address and
  declared byte length. A new AGC header at the same-sized reused shader-heap
  address could therefore receive stale instructions while using current
  header metadata and user registers. The focused regression reproduces that
  stale resource-site split and proves the header-aware cache identity.
- **REFUTED:** “nothing renders.” The full-resolution HDR scene target is
  nonblack. The failure is specifically in the downstream half-resolution
  chain.
- **REFUTED for the observed 960×540 lifetime:** “dropped mode-6
  `kDccDecompress` draws are the first loss.” Sony confirms what mode 6 means,
  but the captured target stays zero across its actual writer interval; the
  expected nonzero producer still has to be identified.
- **REFUTED:** “the observed `0x5002AFC00` material draws should be nonblack.”
  Exact export, sampler, and BC7-edge reconstruction show black is the
  shader-consistent result for all 13 draws in the measured interval.
- **REFUTED as an Astro cause:** the MRTZ architecture defect is real, but the
  retained Astro shader families measured so far do not make it the first
  black-producing stage.
- **REFUTED for the exact amplified shader:** capture records must be
  redirected from physical invocation ID to a different compact `v39` index,
  or indirect counts are read from the wrong wave. Exact title instructions
  prove `v39` equals physical invocation, while only wave 0 executes
  `GS_ALLOC_REQ`.
- **CORRECTED:** the 49 all-black writers do not prove that worldmap scene
  geometry was submitted and lost. They occur after
  `Level has started: title_controller_ship` and `Continue: worldmap`, but
  before `LevelDocument Loaded: worldmap`. A fresh root with no ordinary save
  slot and no pad input produces the same ordering, while no title-level end
  or ProductNext state-8/state-9 execution is observed. Calling the interval a
  measured transition was therefore also unsupported. It is a live title
  renderer boundary in which a black half-resolution image replaces a
  nonblack full-resolution image; individual optional-empty writers remain
  non-findings until tied to required title content.
- **REFUTED:** `JsParticleHalfResolution_0` suppresses its MRT export for the
  measured all-byte-zero source. It suppresses transparent black
  `(0,0,0,1)`; its alpha comparison is `1.0`, so `(0,0,0,0)` executes the
  export. The exact target-only pair records 995,072 RGB-nonblack pixels before
  the draw and zero afterward while alpha survives the RGB-only write mask.
- **REFUTED:** Prosperous's internal `VM_PROT_GPU_R = 0x40` and
  `VM_PROT_GPU_W = 0x80` should replace SharpEmu's public HLE protection bits.
  Sony SDK 10.00 defines the public dmem/mman GPU bits as `0x10/0x20`, matching
  SharpEmu. Prosperous is naming a different internal kernel layer or version.
- **REFUTED:** the Linux patch's global `wave_front_size = 32` can override
  Sony stage metadata. It describes that Linux driver configuration; Sony's
  stage flags and exact title shader metadata decide the guest contract.
- **DEAD END for HLE bring-up:** implementing `/dev/gc` ring-submission ioctl
  `0xC0488131` through LLE. It expands the required surface to the kernel GPU
  interface and is strictly larger than the working HLE route.
- **DEAD END:** treating a nonblack `PerfOverlay` HUD as guest output. Guest
  pixels must be captured with `PrintWindow` and `PW_RENDERFULLCONTENT`.
- **DEAD END:** inferring work absence from absent ERROR-level diagnostics such
  as `gpu_ledger_retired` or `work=offscreen`. Their absence means no reported
  anomaly, not no GPU work.

## Current Astro frontier

**2026-07-31 correction:** the historical `0x50068FA00` black surface is
pre-title transition work: the complete trace places it after
`Level has started: ps_logo` and before the title level loads. It is not a
proved missing worldmap producer. The current long-run GPU failure is instead
an asynchronous Vulkan device loss observed while submitting
`cs=0x500529400` with `131072x1x1` groups, after a preceding compute retirement
took about 6.76 seconds. The named dispatch is not yet proved causal. Preserve
the checkpoint as last-retired/last-submitted attribution plus a retained
guest-state-valid frame after an actual title selection.

The CPU-side blocker is narrower than "worldmap hangs": the document loads,
but the guest never requests `StartLevel: worldmap`. The active
`OdxAsyncLoader` condition is not a proven gate. That worker takes the same
long waits earlier in boot and resumes when work arrives, so its final wait may
be an ordinary empty queue downstream of the missing transition request. What
is refuted is rwlock starvation and a thread trapped inside the condition
wait—not the unknown level-readiness logic. The next condition trace must
carry the guest return RIP and thread identity to classify the callsite before
changing synchronization semantics or fabricating readiness.

Do not re-litigate whether Astro renders anything, whether the final tonemap
has constants, or whether the 13 observed `0x5002AFC00` draws should sample
color. The paired `es=0x5000F6700` program was incorrectly sent through the
plain-vertex fallback despite matching the measured amplified native-GS
contract of `es=0x50011FC00`. A strict contract-based replay correction and
the same-address writer-order correction are both offline-verified but
**UNMEASURED LIVE**.

The six `0x5000FD100` writers bind nonblack BC7 data, but that fact alone is
not a finding. Run `20260730-184647-corpus-gate` closes the four connected
checkpoints after the generalized replay fix: PC `0x0058` structured input is
zero, the allocation is the guest's deliberate `0x1001` one-vertex/
one-primitive fallback, retired vertex and index payloads are zero, and raster
coverage has no nondegenerate triangle. The same result repeats for all six
retirements. These draws are **CONFIRMED optional-empty**; forcing particle
data would fabricate guest state.

The 24 `0x500006E00` writers are also closed offline by the complete
`20260730-190148-corpus-gate` interval. Every draw binds guest texture
`0x507405100`, a 1x1 zero texel, at both material sample PCs `0x01B4` and
`0x01D0`. Ten pair with `es=0x5002AA400`, ten with `0x5000F6700`, and four
with `0x50011FC00`. They are not an omitted base-color family. With the 13
black-edge `0x5002AFC00` draws and the optional-empty particle families, all
49 writers have a shader-consistent zero result. The guest's
`ps=0x50063F800` shader treats `(0,0,0,1)` as its no-op sentinel, but the
measured surface is `(0,0,0,0)`, so it exports black. The ordered target-only
readback is now complete: `0x514080000` falls from 995,072 RGB-nonblack pixels
to zero at the marked draw. The remaining checkpoint is upstream control and
resource state, not another attempt to fabricate a half-resolution producer.

The visual acceptance checkpoint must additionally be guest-state valid.
Prefer the first frame after `Level has started: worldmap`. If that predicate
still never fires, treat the level-start stall as an independent CPU/asset
readiness problem; do not use a loading-transition image to demand a GPU
color producer.

## Sony metadata and view identity

- **AUTHORITATIVE CONTRACT, SDK 10:** `CxCbControl` field value 6 is
  `kDccDecompress`. It operates in place and includes fast-clear elimination
  and FMask decompression.
- **AUTHORITATIVE CONTRACT:** DCC metadata keys can represent entire constant
  RGBA blocks. Zero expanded color bytes are therefore not evidence that a
  compressed logical surface is black.
- **AUTHORITATIVE CONTRACT AND SAMPLE:** render target, texture, smaller
  render-target, and translated depth/color views may share the exact data,
  CMask, FMask, and DCC addresses. Texture-compatible DCC is sampled after the
  required synchronization; incompatible views are explicitly materialized
  without changing logical surface identity.
- **IMPLEMENTATION CONSEQUENCE:** separate Vulkan images are an emulator
  detail. Selecting a stale host object by descriptor kind violates Sony's
  allocation model. Commit `a5f0a3c` fixes the measured newest-writer
  selection defect; explicit cross-representation materialization remains a
  separate general gap.
- **CORROBORATION:** PAL's GFX10 barrier and color-target code independently
  models DCC decompression as an ordered metadata BLT associated with the same
  pixel/metadata allocation. PAL corroborates Sony here; it is not the
  authority for Sony-facing field values.
- **DEAD END FOR THIS QUESTION:** the maintained and legacy payload SDKs and
  fail0verflow Prosperous contain no DCC/HTILE surface-transition contract.
  That negative scope result does not reduce their value as primary
  implementation evidence for the layers they do implement.

Retained draws are almost replay fixtures, but their pooled byte arrays must be
deep-copied and a useful replay must preserve pre-draw attachment contents,
alias/metadata identity, and enough predecessor operations to reproduce
compute and transition dependencies. Use a single-draw replay for
shader/descriptor questions and a short serial sequence for residency,
metadata, aliasing, or producer-consumer questions.
