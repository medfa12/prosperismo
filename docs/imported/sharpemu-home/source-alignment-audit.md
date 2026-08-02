<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# SharpEmu alignment with the local PS5 evidence

Audited 2026-07-31 on `master`. This is the current-status
companion to the chronological title journals. Where an older document disagrees with this
audit, current source, included tests, and the newest measured title section
win.

## Verdict

SharpEmu is **contract-aligned on a useful, title-exercised subset**, not a
general PS5 implementation yet. It is close enough to execute real PS5
x86-64 code, load title modules, decode Astro's intro, submit real AGC work,
render nonblack guest surfaces, and present complete frames in another title.
It is not close enough to claim complete gfx1013, AGC, compression, kernel, or
firmware equivalence.

The intended machine is the **base PS5**, not Trinity/PS5 Pro. The GPU model is
gfx1013: GFX10.1/RDNA1 common features plus BVH ray-tracing instructions.
`sceAgcGetIsTrinityMode` returns false. Trinity-specific VRS, expanded scratch
limits, and metadata variants are therefore outside the current target. Base
PS5 scratch rings are part of the target: SDK 10.00 defines both graphics and
compute scratch-ring size registers with separate base/Trinity wave limits.

The normal boot is HLE. SharpEmu does not boot or automatically execute an
installed Sony firmware. Game imports resolve to C# `[SysAbiExport]`
implementations. Selected firmware modules can be used through the narrow,
opt-in `SHARPEMU_LLE_MODULES` path, but raw FreeBSD syscalls and device ioctls
cannot execute directly on Windows. Firmware is principally an oracle for HLE
contracts.

## Evidence order used

1. Sony SDK 10.00 headers, generated AGC source, samples, host tools, and
   static libraries in `games/prospero-sdk-10.00`.
2. Exact title binaries/captures and versioned Sony firmware binaries in
   `games/`.
3. AMD/LLVM's gfx1013 definitions and byte-exact MC fixtures.
4. The maintained and legacy payload SDKs, fail0verflow Prosperous, and the
   PS5 Linux patches as primary implementation evidence for the layers they
   actually exercise.
5. PAL, Mesa ACO, KytyPS5, ps5rs, and other concrete implementations as
   corroboration, never as a replacement for a conflicting Sony contract.

### Reference checkout snapshot

These revisions were actually present during the audit; a later checkout must
record its own revision before using a changed result:

| Tree | Revision |
|---|---|
| `inspiration/ps5-payload-sdk` | `a0d2bc60bdcc0a5ee9e790fa3b02fe5051a152d0` |
| `inspiration/ps5-payload-sdk-legacy` | `4bdc3fb919483a74199f09661692d9fb746e6b6b` |
| `inspiration/prosperous` | `663b2cd041fce6b2c48151082f85cbfdd12f4d5d` |
| `inspiration/ps5-linux-patches` | `daa2e496086ae0c9fe22205f703925a2e2596185` |
| `inspiration/ps5rs` | `3ab6a064ff833f4630d20c9d065b709f32961548` |
| `inspiration/KytyPS5` | `c71bb9fc9eca4aef169010208c9c07de0e805062` |
| `inspiration/pal` | `c5e800072a32f68b6ccc4422936d96167c6e0728` |
| `games/llvm-amdgpu` | `56a48d712006ff7d9a09eef6a86b78d4b2eec3aa` |
| `games/mesa-aco` | `209e68250df169301db18c4cce8f512d67f2faf4` |
| `inspiration/shadps5-rust` | `6d0289c5fcdede25754f808cba3edf233cbbfe5b` |

The audit indexed every documentation and reference tree and then read the
files relevant to an implemented contract. Binary trees were inspected via
their exports, headers, samples, symbols, existing differential scripts, and
targeted disassembly; “read the games directory” must not be interpreted as
treating every opaque byte as prose.

## Alignment matrix

| Area | Current status | Evidence |
|---|---|---|
| GPU identity | **Aligned** | LLVM defines gfx1013 as GFX10.1 common plus `FeatureBVHRayTracingInsts`; SharpEmu decodes Astro's BVH instruction and does not use a generic RDNA2 table. |
| Base/Pro mode | **Aligned for base PS5** | `sceAgcGetIsTrinityMode` returns false. Trinity-only contracts are not emulated. |
| GPU memory protection bits | **Aligned** | Sony `sys/dmem.h` uses GPU read/write `0x10/0x20`; SharpEmu uses the same. Prosperous's `0x40/0x80` values belong to a different internal layer and are not substitutes. |
| Native CPU execution | **Substantial, partial** | Same-ISA x86-64 execution, guest ABI/TLS, mapped address space, import traps, and Windows fault handling work. AMD-only SSE4a fixups exist. General FreeBSD syscall execution and complete guest exception semantics do not. |
| Loader/import routing | **Substantial, partial** | SELF/ELF mapping, relocations, TLS, adjacent title modules, NID dispatch, and a narrow LLE route work. `DT_SCE_NEEDED_MODULE` and `DT_SCE_IMPORT_LIB` are now decoded into their packed ID/version/name records and retained on `SelfImage`, so the `#libId#modId` suffix no longer loses its dynamic-table attribution. Broad firmware equivalence is not implied. |
| Public HLE surface | **Far from complete** | The firmware census currently measures roughly 15.9% covered under its broad SDK-directory definition and re-derives 1,056 constant-success/no-work exports. This is not a compatibility percentage, but it prevents an equivalence claim. |
| Kernel memory/synchronization | **Large title-driven subset, partial** | Direct-memory maps, guest protection flags, pthreads, mutexes, rwlocks, condition variables, semaphores, event flags/queues, and `_umtx_op` families have firmware-derived contracts and tests. Windows remains the host, so raw FreeBSD syscall/device behavior is translated rather than inherited; uncommon operations and complete exception/signal semantics remain open. |
| Astro raw syscall surface | **Mostly served, bounded** | The syscall audit passes 41/41 invariants. Astro has 45 HLE-served syscall-backed imports and five unserved APR pathname-resolution functions. `/dev/gc` ioctl `0xC0488131` remains an intentionally rejected LLE expansion. |
| AGC exports | **Title-complete by registration, semantically partial** | Current source registers 125 AGC-library exports; 118 overlap the 354-export game-facing 4.03 firmware union. Every one of Astro's 71 catalogued AGC imports now has a repo registration, but stale routing TSV rows still say six are unserved. Registration does not prove firmware-equivalent behavior. |
| PM4/queue ingestion | **Large title-driven subset** | Real context/shader/uconfig packets, draws, dispatches, DMA/writes, waits, releases, flips, and context-state push/clear/pop are parsed. Queue callback/error propagation and many uncommon packets remain simplified. |
| Shader decoder | **Substantial, incomplete** | The 2026-07-31 `scripts/isa_compliance.py` run measures 721 decoder keys and 209 of Sony's 540 indexed entries not decodable by name. This is a name-level census, not a semantic score. The missing set is dominated by remaining f16/f64/16-bit VALU, relative scalar access, plain FLAT, scalar scratch, GWS/GDS, and uncommon memory/image forms. The complete GFX10 vector SCRATCH load/store fixture set (48 distinct accepted vectors) and the first 40 GLOBAL fixture vectors agree with Sony on name, size, operands, and modifiers. The standing oracle performs operand-aware GLOBAL/SCRATCH checks instead of relying on mnemonic counts. |
| Shader semantics | **Improved, not complete** | Promoted VOP3 ranges, wave-width mask writes, MIMG's eighth opcode bit, MTBUF format, DPP bank masks, `s_barrier` semantics, 64 KiB LDS, relative VGPR moves, typed stores, DS shuffles, DS append/consume, BVH decoding, gfx1013 GL0/GL1 invalidations, corrected GLOBAL destinations/modifiers, the 32-bit integer GLOBAL atomic family, scalar bit-count/reverse/sign-extend operations, Vulkan per-lane scratch dword/byte/short/D16 accesses, and scalar-half add/sub/subrev/mul/min/max now have included tests. Sony's executable ISA oracle pins the six f16 VOP2 encodings as well as GLOBAL atomic INC/DEC at opcodes `0x3c/0x3d`; f16 lowering widens and narrows through the exact existing software path and fails closed on unmodelled `op_sel`/SDWA/DPP controls. The oracle also accepts Prospero's `ds_write_addtid_b32`/`ds_read_addtid_b32` target exception even though LLVM rejects the pair for stock gfx1013. 64-bit/x2 and floating atomics, indirect shader PC calls, plain FLAT, scalar-address scratch execution, much of the remaining 16/64-bit arithmetic, full export variants, several GDS/GWS operations, and Metal scratch/expanded-GLOBAL lowering remain open. |
| Wave/subgroup model | **Bounded, partial** | Graphics stages use real wave metadata; compute wave32 can request a 32-lane Vulkan subgroup; wave64 paths are gated or bridged and unsafe combinations fail closed. This is not a complete cross-vendor model for every wave-sensitive instruction. |
| NGG | **Three bounded contracts; not a general mesh backend** | The exact `0x5002aa400` gate is gone. Indexed, instanced, single-wave wave64 triangle-list passthrough replay is selected from Sony stage registers, program shape, exports, and draw state. Direct-export point-to-triangle amplification derives subgroup input counts, output budget, amplification, parameter stride, and aligned target-20 slots from submitted registers and decoded program shape instead of Astro constants. Its mesh launch now packs Sony's per-wave vertex/primitive counts into `s_gs_wave_id` and seeds `S_NGG_VERTEX_INDEX` from the group-local invocation, so waves after lane 63 neither repeat wave zero's counts nor duplicate `v5`. The supported amplified subset remains auto-indexed points, no GS instancing/user VGPRs, one allocation and no emit/cut/export loop. Wave32, indexed amplification, tessellation, general topology conversion/culling and a general mesh/primitive backend remain open. |
| Tiling | **Aligned for public 2D single-sample modes** | The standing AgcGpuAddress oracle compares Sony's complete detile with production over identical bytes. All 29 valid mode/element-size combinations for public modes 1/5/9/17/24/27 pass at 257x193 and 960x540; reserved modes 4/8 are refused. Mips, arrays, volume, MSAA, and metadata layouts remain outside this proof. |
| Resource identity/aliasing | **Materially improved, partial** | GPU provenance is independent of consumer format; tile mode and DCC metadata participate in identity; newest-writer arbitration, BGRA view compatibility, storage aliases, and constrained variant propagation have included tests. Storage promotion now preserves Sony's texture-descriptor DCC address instead of silently moving an expanded GPU output into metadata lifetime zero; Astro's next sampled draw selects that exact GPU image. Arbitrary overlapping mappings and all cross-representation materializations are not solved. |
| DCC/HTILE/FMASK/CMASK | **Identity and transition contracts; pixel materialization incomplete** | Sony constant DCC metadata codes and queue order can materialize a proved constant-clear Vulkan image. Modes 2/5/6 expand to their complete eliminate-fast-clear/FMASK/DCC effects; CB_COLOR_INFO CMASK/FMASK requirements are decoded. Active DCC identity now requires `CxRenderTarget::DccCompression::kEnable`: Sony's sample retains the DCC base while disabling compression to write raw backing, so a nonzero base alone is not an active compressed lifetime. FMASK now models compressed, uncompressed, one-fragment, disabled, and invalid states; SDK GNMP says stale mode bits are ignored when FMASK compression is disabled. A metadata elimination preserves newer GPU-authored pixels, while compressed reads still require exact metadata identity. HTILE enablement, texture compatibility, metadata identity, and flush-vs-decompress participate in depth reuse. Sony's address DLL supplies layouts, not decompression. General nonconstant metadata materialization, HTILE fixup/resummarization, MSAA FMASK storage, and cross-view synchronization remain open. |
| VideoOut | **Functional, partial** | Buffer registration, flips, vblank/event ordering, GPU-image submission, and host presentation work. All 11 of Astro's routed VideoOut imports are now registered; exact 4.03/9.00 bodies plus SDK 10 ground the public null-option path for `SubmitChangeBufferAttribute2`, but retained Astro traces contain no call. SDK 10's v2 layout also corrected the shared parser's aspect and pitch fields (`+0x08`/`+0x14`) instead of inventing `pitch = width`. The private driver callback, registered-allocation growth constraint, and additional non-Astro exports remain incomplete. Astro's extra `RegisterBuffers2` qwords were measured zero, refuting them as its scanout cause; `IsFlipPending` correctly returns the pending count used by Sony samples. |
| AvPlayer/media | **Functional for Astro MP4** | Default decode/probe is in-process FFmpeg through native libraries; an external executable is only an explicit `SHARPEMU_FFMPEG_PATH` override. Astro's 3840x2160 intro produces video and PCM. Bink2 has a separate in-process bridge for the static-title path used by Demon's Souls. |
| Audio | **Real data path, incomplete ecosystem** | AJM ATRAC9 uses LibAtrac9; AudioOut2 mixes nonzero PCM and can reopen a Windows endpoint; the user heard Astro's intro. SDK-defined AudioOut2 mono-object passthrough values now route left and right independently; firmware-only values 3/4 remain accepted without invented semantics. Selector audio is measured 37–43 dB below the intro, so no fabricated boost is justified. NGS2/AJM and service breadth remain incomplete. |
| Input | **Functional core, semantically partial** | Pad/mouse open and read paths feed real host state and are exercised every frame by Superliminal. `scePadSetVibrationMode` now follows SDK 10's five-value domain and the exact 4.03/9.00 firmware validation/error order while mapping compatible rumble to Windows. Controller information, triggers, light bar, motion, specialty devices, and privileged variants still include simplified or success-only behavior; basic polling does not establish DualSense equivalence. |
| Save data/filesystem | **Functional host translation, partial** | Host-backed save slots implement and test initialization, mount/unmount, metadata, delete, save-data memory, synchronization events, and path containment. `sceKernelGetdents` now emits the SDK's variable-length, four-byte-aligned records and advances the open-directory offset only after all guest writes succeed. This remains a Windows representation, not Sony storage/crypto/quota equivalence; dialogs, PlayGo/APR, and uncommon filesystem surfaces remain incomplete. |
| Network/NP | **Offline scaffolding, not service-equivalent** | Socket/TLS/HTTP and signed-out NP paths cover title startup cases. `sceNetAccept` now transactionally publishes the peer sockaddr/length and disposes the accepted host socket on guest-memory fault. The census still contains large `libSceSsl`, `libSceNet`, and NP success-shell populations; there is no claim of PSN service equivalence. |
| System UI/services | **Compatibility subset only** | User/SystemService, IME, common dialogs, trophies, sharing, and companion surfaces are sufficient for selected boot flows but include many no-work shells. HDR tone-map luminance now uses the exact successful firmware fallback words instead of a host performance profile, and UUID creation follows the SDK's DCE layout plus the firmware UUID-v1 algorithm with a Windows-appropriate random-node fallback. SharpEmu does not emulate the PS5 shell or privileged system applications as part of a normal game boot. |
| Multi-title evidence | **Positive but narrow** | Superliminal sustains a correct title screen at 32 FPS with audio and input polling; its later PSN managed initialization is a separate blocker. Astro has real nonblack intermediate surfaces, working intro audio/video, and a retained PrintWindow capture of genuine but severely dark PlayStation Studios guest output. There is still no retained nonblack interactive worldmap capture. |

## Current Astro finding

> **Current source-triggered result.** Run `20260731-210748-corpus-gate`
> sampled 2,146,458 nonblack RGBA16F pixels at `0x53C420000` and wrote
> 4,951,550 nonblack A2R10G10B10 pixels at `0x5093F0000` on the same
> unmodified `ps=0x500640D00` draw. PrintWindow captured the real PlayStation
> Studios image, but it is severely too dark. The fixed-ordinal black capture
> below occurred while the source was black and is historical. The binary
> postprocess-black boundary is closed; luminance/color correctness and later
> interactive-world content remain open.

The first measured postprocess resource-selection losses are now closed. A
whole-image fast-clear shortcut incorrectly erased the initialized
`0x53AA00000` Vulkan image during Sony mode-6 DCC decompression. After that was
made writer-order aware, a paired probe proved `cs=0x50068FA00` changed its
2432x1368 RGBA16F output from 1,333,889 to 2,146,458 nonblack pixels. The next
draw still read black because storage-image promotion discarded the Sony
texture descriptor's DCC address and exact-lifetime aliasing correctly fell
back to guest memory. Preserving that address changes `ps=0x50065D500` to the
initialized GPU color image with requested/resolved DCC `0x57054E000`.
Corpus run `20260731-195610` reached LOGO, TITLE, and worldmap without device
loss and observed that selection repeatedly. No retained current PrintWindow
capture or paired `65D500` output yet proves a nonblack final frame.

Windows' retained kernel evidence now classifies the four recent failures that
the Vulkan log first observed at `6EAC00`: each corresponding WATCHDOG dump is
a `LiveKernelEvent 141` video TDR involving `SharpEmu.exe`. An adjacent TITLE
success and two failures used the same executable fingerprint and initial
pipeline-cache bytes, and the same `6EAC00` module has both succeeded and
failed. Therefore the latest log names the observer of a WDDM engine reset,
not a proved causal shader. The last substantial physical-queue work before
the observation is the 32-chunk `571000` dispatch. The new opt-in TDR boundary
probe drains prior work, retires an empty sentinel, and fingerprints every
target binding. Run `20260731-185113` settled the boundary: 193 earlier
`6EAC00` packets passed the sentinel, then a drain retired all 32 chunks of
`571000`; the following empty sentinel submit observed device loss before any
new `6EAC00` command was recorded. Windows simultaneously wrote
`WATCHDOG-20260731-1853.dmp`. The oldest chunk had occupied the queue interval
for 2,533.300 ms. Windows now waits for each non-final chunk fence before
enqueueing the next chunk, creating the scheduling boundary that separate
back-to-back Vulkan submissions did not provide. The fix is build- and
test-clean and now has a plain post-fix Astro validation:
`20260731-185733-corpus-gate` used committed head `830d202` and the normal
corpus environment, reached LOGO at 61.875 seconds, TITLE at 225.125 seconds,
and worldmap document load at 309.516 seconds, then completed the 480-second
observation without device loss or a new Windows WATCHDOG dump.

That run had capture disabled, so it is a control-flow/TDR result rather than
a pixel result. There is still no retained nonblack worldmap capture, and the
live `68FA` postprocess input/output differential remains the next falsifiable
rendering checkpoint.

The immediately preceding clean pre-fix run
`artifacts/game-runs/astro/20260731-183337-corpus-gate` used committed head
`84a049a`, binary SHA-256
`B97F0CE822DDF4E7AD17DF9566DD10411942ADEFF1B742749C097C447BAF9B6F`,
and reached `Level has started: ps_logo` at 173.000 seconds. It did not reach
TITLE or worldmap: `vkQueueSubmit` for `cs=0x5006EAC00` returned
`ErrorDeviceLost` at 193.062 seconds, after the ledger reported
`submit_timeline == completed_timeline == 53559`, zero in-flight submissions,
and a signaled retirement for `cs=0x500571000`. The device-fault extension
reported `Success` with no fault address. The preceding clean run at
`20260731-180513` failed at the same module with the same empty-ledger shape.
This is a precise validation blocker, not a pixel result for `68FA` and not
evidence that the run was merely slow. The latest run did not reach
`cs=0x555F41F00`, so it does not dynamically validate the new add-TID lowering.

The accompanying bounded GDS audit found no correction to make. Sony SDK 10
and SharpEmu agree on `(byteBase << 16) | byteRange`, byte-addressed 32-bit
counters, the ordered DMA clear, one atomic reservation per active wave, and
broadcasting the old counter. Astro's `m0=0x0c600020` plus offsets
`16/20/24/28` addresses `0xc70/0xc74/0xc78/0xc7c`, and retained runs show the
matching clears. The same 54-instruction, backward-branch-free `6EAC00` module
has retired successfully in 0.309 ms in another retained run. The surviving
differential is runtime descriptor/data state or Windows driver/TDR behavior,
not a proved GDS range/reset defect. A compile-time `s_trap pc=0x1f80`
diagnostic in `571000` also does not prove that a runtime invocation reaches
the trap.

The newest retained checkpoint is
`artifacts/game-runs/astro/20260731-160130-corpus-gate`. It corrects the
consumer selected by the preceding investigation. At work sequence `1003279`,
`ps=0x5006CB800` wrote the proven-nonblack 1920x1080 tile-27 image at
`0x53AA00000` with writer serial `35837`. The next consumer of that exact
identity was `ps=0x5006CE200` at work sequence `1003308`, not `cs=0x500690F00`.
All nine observed `6CE200` image operations at PCs `0xF8..0x150` selected
writer serial `35837`. Retained AGC register traces independently identify
this TAA-lite draw's MRT0 as `0x537060000` (2432x1368 RGBA16F, tile 27, DCC
metadata `0x56E834000`). `cs=0x50068FA00` then runs at sequence `1003314` and
samples that TAA-lite surface in the established resource layout. This makes
`6CB800 -> 6CE200 -> 0x537060000 -> 68FA` the current measured route.

The follow-up `artifacts/game-runs/astro/20260731-162942-corpus-gate` resolves
that target. At `6CE200` occurrence 261/work `987555`, the source has
1,333,889/2,073,600 nonblack RGB pixels and SHA-256
`8B301C0EF384488AD6DAB801067655F55FBDDA63CE4FEDDA31AD81B1B4791DB5`.
The 2432x1368 `0x537060000` MRT has 2,129,899/3,326,976 nonblack RGB pixels,
SHA-256
`8C791FDB84E15171F524BE354EA2FA9DFD5E3CCBE80753C9E2C24570B1433423`,
and half-float alpha 1.0 in every pixel. `6CE200` therefore preserves and
upscales real color. There is no `690F` dispatch after the corresponding
nonblack producer in this lifetime; the first-loss boundary moves to the
immediately following `68FA` sampled input and storage output.

In `artifacts/game-runs/astro/20260731-153952-corpus-gate`, the
`0x5006CB800` producer's 1920x1080 RGBA16F source at `0x514080000` measured
1,333,889 nonblack pixels. Its target at `0x53AA00000` was byte-identical:
both 16,588,800-byte captures have SHA-256
`8B301C0EF384488AD6DAB801067655F55FBDDA63CE4FEDDA31AD81B1B4791DB5`.
The source-to-target producer therefore preserves real scene data in that
lifetime.

The exact Astro `0x500690F00` shader was decoded from `eboot.bin` at file
offset `0xE1B73E0`, length `0x1FEC`. Sony's ISA oracle and SharpEmu agree on
all 1,112 instructions. Running the production SPIR-V offline on the same AMD
V620 with the captured live `0x53AA00000` bytes produces
2,152,646/3,326,976 nonblack output pixels. This is a discriminating
elimination: the exact game bytes, decoder, scalar evaluator, SPIR-V, and host
driver can produce nonblack output. The remaining failure class is the live
resource/submission/binding/synchronization path.

The shader resource census exposed one concrete live-path defect. Its 52 image
bindings are 48 copies of the same sampled `0x53AA00000` descriptor and four
copies of the same storage `0x53B9F0000` descriptor. SharpEmu independently
read, detiled, allocated, and queue-charged every occurrence, reporting about
944 MiB of texture payload for one dispatch against a 256 MiB queue budget.
Production now snapshots each distinct descriptor/view/storage identity once,
shares that immutable array across binding PCs while preserving each binding's
sampler and write semantics, and charges shared arrays once by reference. This
is a general queue/backpressure fix; the fresh boot reached LOGO, TITLE, and
worldmap document load without device loss but did not restore a retained
nonblack final frame. Offline evaluation of the exact eboot state confirms
that production's snapshot identity has exactly two keys for this shader (48
source views and four storage views), not twelve mip/view variants.

The fresh run's first queue-backpressure line reported 194 MiB of texture
arrays for an incoming compute dispatch, but the old diagnostic printed only
the CLR work type. New telemetry identifies the repeat as `cs=0x500525200`,
with 23 bindings and unique payloads dominated by 144 MiB at `0x528C30000`
and 50 MiB at `0x556970000`. It is not `690F` evidence. Queue telemetry now
names the compute shader, binding count, and each unique address/array length.

The paired probe also had a false-selection mode. After one positive producer,
the same guest address can be rebound as a 2432x1368 tile-27 variant by
`0x50068FA00` before another `690F` occurrence. Work-sequence ordering alone
could therefore capture a different lifetime. The trigger is now pinned to
address, logical extent, guest format, tile mode, and writer serial. The old
`requested tile27, resolved tile0` line was itself a telemetry defect: it read
tile mode from a binding wrapper; the selected `GuestImageResource` was tile
27. Writer and reader traces now report the authoritative selected image.

The next valid runtime checkpoint is the identity-pinned `68FA` pair with
sampled `0x537060000` and writable `0x53AA00000` in the same current-title
lifetime. Do not infer its output from queue depth, shader occurrence, an
address-only match, or an older black-input lifetime.

Two attempts to chain that compute probe after the proven `6CE200` writer
failed before the trigger: runs `20260731-163746-corpus-gate` and
`20260731-164015-corpus-gate` both reported `ErrorDeviceLost` while submitting
`cs=0x5006EAC00` shortly after LOGO (submission/work 708/228995 and
771/252076). The repeated failure is a precise blocker for those attempts, but
it does not classify `68FA` input or output. Two preceding targeted boots on
the same executable reached TITLE and worldmap without device loss, so treat
the difference as live until its enabling environment/state is isolated.

“Nothing renders” is false. The full-resolution HDR scene and later SMAA
composite are nonblack, and the composite preserves the scene byte-for-byte.
The SDK-grounded DCC constant-clear fix prevents the measured
`JsParticleHalfResolution_0` pass from erasing that scene in its observed
lifetime.

The earlier `0x50068FA00`/black-history investigation is not a proved current
root cause. The complete trace orders it after `Level has started: ps_logo`
and before the title level loads, so it is pre-title transition work. In the
current no-input corpus route, `LevelDocument Loaded: worldmap` is a preload;
the guest never requests `Level has started: worldmap`. A black transition
surface is not evidence that a worldmap producer was dropped.

An earlier long run had a separate unresolved GPU failure: Vulkan reported
device loss while submitting `cs=0x500529400`, `131072x1x1`, after a preceding
compute retirement took about 6.76 seconds. Vulkan loss is asynchronous, so
the named submission is not yet proven causal. The next valid GPU checkpoint
is a retained, guest-state-valid frame after an actual title selection/worldmap
start, plus device-fault attribution to the last retired/submitted work—not
another forced black-surface value.

## Newly confirmed shader-encoding contradiction

Sony SDK 10.00's PSSL compiler produced real `scratch_store_dword` and
`scratch_load_dword` instructions from a dynamic-indexed local array in a
Prospero pixel shader. One exact load is:

```
scratch_load_dword v5, v2 offset:0x0004
04 40 30 DC 02 00 7D 05
```

Sony's raw ISA DLL accepts that byte sequence as `scratch_load_dword`; LLVM's
GFX10 definitions independently assign the same segment and fields. SharpEmu
previously rejected it as `unknown-flat segment=0x1`.

The same comparison found a less honest defect in the supported GLOBAL path.
For `global_load_dword`, GFX10 encodes `vaddr` in bits 0..7 of the second
DWORD, store/atomic `vdata` in bits 8..15, `saddr` in bits 16..22, and load
`vdst` in bits 24..31. The old translator used bits 8..15 for both store data
and load destination. It also sign-extended `word & 0x1fff`, so the independent
`dlc` modifier at bit 12 became part of the address. The LLVM/Sony-agreed bytes
`00 90 30 DC 03 00 7D 01` name a zero-offset DLC load into `v1`; old IR instead
named `v0` and offset -4096.

This is now **CONFIRMED GENERAL DEFECT, FIXED; ASTRO CAUSALITY REFUTED**. The
shared field extraction is corrected and exact Sony/LLVM operand fixtures cover
it. Vulkan models the bounded dword scratch family as per-lane Private storage,
sized from Sony's graphics or compute ring register. The retained Astro
instruction census reports zero FLAT/SCRATCH occurrences, so do not claim the
fix as a route to Astro's black frame without a new title occurrence.

## Known stale artifacts

- `docs/ps5-shader-isa-audit.md` is a historical audit: many of its highest
  findings have since been fixed. Its FLAT/SCRATCH and remaining width/ISA
  gaps are still live.
- `docs/gpu-surface-gap.md` and its generated data are stale. The generator's
  static emitter classifier misses specialized branches (for example it calls
  DS append/consume, typed stores, and BVH rejected despite production paths
  and included tests) and its source anchors now fail.
- `scripts/astro_import_routing.tsv` still labels six AGC imports unserved even
  though current source registers all six.
- `docs/capability-survey.md` describes an older commit and an obsolete Astro
  rwlock theory.
- `docs/ffmpeg-bink2.md` originally described the old external-process
  AvPlayer default. The current default is in-process native FFmpeg; the
  external process remains an explicit diagnostic/override path.
- `scripts/verify_os_surface_claims.py` is not self-contained from the current
  checkout because it expects generated `scripts/nid_gap.tsv`.

## Highest-value alignment work

1. Extend the now operand-aware `tools/SharpEmu.Tools.ShaderIsaOracle` beyond
   GLOBAL/SCRATCH controls to buffer, DS, image, and export controls. Turn the
   1,197 SDK PSSL files into broader compiler-generated gfx1013 vectors.
2. Expand the standing AgcGpuAddress gate beyond its completed public 2D
   single-mip/single-slice/single-sample matrix to mip chains, arrays, volume,
   MSAA, and metadata layouts before accepting those paths by default.
3. Continue FLAT and the missing f16/relative-SGPR families from Sony's
   executable oracle plus LLVM gfx1013 fixtures, prioritized by title shader
   occurrence rather than raw counts. The first 40 GLOBAL vectors now pass;
   among the first 100, all 66 implemented forms agree and the 34 remaining
   vectors are explicit wrap-clamp increment/decrement or 64-bit/x2 gaps. Keep
   those and other unsupported address spaces fail-closed until their exact
   Windows/Vulkan memory model is explicit.
4. Generalize compressed logical-surface state: DCC transitions first, then
   FMASK/CMASK/HTILE and cross-view materialization. Keep data/metadata
   identity and queue order in one model.
5. Expand the firmware differential oracle beyond its current 14 scored cases,
   prioritizing success shells actually reached by title import census.
6. Repair or replace the stale GPU-surface generator before using its emitter
   totals as findings.

## Validation snapshot

The last complete solution validation on this head was a zero-warning Release
build with 2,554 tests passing. The firmware oracle currently has five case
files and 14 scored cases with zero divergences; that is positive but far too
small to generalize. This refresh re-derived 41/41 syscall-surface invariants,
the 1,056 success-shell census, the 715/540/215 decoder-name counts, and the
AGC export/import joins. Sony's complete detiler again matched all 29 accepted
public 2D single-sample combinations at both 257x193 and 960x540. No new
emulator boot was performed for this source-alignment audit.
