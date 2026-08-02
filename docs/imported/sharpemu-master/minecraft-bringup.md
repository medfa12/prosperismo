# Minecraft bring-up

Last verified: 2026-08-01, master plus the adjacent-mapping read fix described
below.

## Current checkpoint

Minecraft PPSA17221 now reaches its title screen and visibly renders the moving 3D
panorama behind the UI. This is visually verified with
`PrintWindow(PW_RENDERFULLCONTENT)` in:

`artifacts/game-runs/minecraft/20260801-115002-layered-cube-alias-proof-2/checkpoint-title-3d.png`

Minecraft also reaches a local world. The guest logs both `Player connected` and
`Player Spawned: SharpEmu`, and the PrintWindow capture below visibly contains the
in-world camera, clouds, and terrain at 3.7 FPS:

`artifacts/game-runs/minecraft/20260801-121217-get-ingame-visual-proof/checkpoint-125s.png`

That capture also contains Minecraft's own `out of data storage space` modal. It
is not a GPU failure: it exposed a separate, SDK-confirmed save-data capacity bug
described below. The rebuilt `20260801-122538-post-fix-ingame-stability` run
visually verifies the fix: `checkpoint-100s.png` shows the in-world sky and
gameplay prompts without the modal, and `checkpoint-140s.png` still shows
in-world terrain with no modal, crash, or device loss. The later image's dark-red
cast is Minecraft's underwater damage/death presentation after Steve spawned in
water, not evidence of an emulator lighting defect.

The native-resolution post-detile capture
`artifacts/game-runs/minecraft-texture/20260801-150504-corpus-gate/checkpoint-detiled-world.png`
confirms that the horizontal texture-strip corruption is gone in the world
consumer. It does **not** close Minecraft rendering: sky, clouds, UI, and some
terrain texels are coherent, but large terrain/model regions remain black. That
remaining black material is a separate checkpoint downstream of the corrected
atlas LOAD and must not be folded into the settled tiling defect.

The next implementation checkpoint is host-image ownership, not another ISA
guess. A differential against KytyPS5 `a65d17a5` found two live SharpEmu
selection defects in the same alias-lifetime class as Kyty's `3965d41` and
`2890d5f` texture-cache work:

- sampled-image arbitration gave an older exact-format Vulkan image a fixed
  score advantage over a newer compatible-format writer at the same guest
  address;
- ordered VideoOut capture consulted only the active `_guestImages` entry and
  ignored retained variants and their `LastWriterSerial` values.

Sony SDK 10.00 `video_out.h` defines a registered display buffer by its data
address together with tiling, dimensions, pitch, pixel format, and optional DCC
state. That contract does not permit dictionary insertion order to choose an
older host lifetime. SharpEmu now preserves exact logical dimensions as the
first compatibility constraint, then selects the newest initialized compatible
writer. Ordered flip capture performs the same search across active and retained
variants, using the active entry only as a writer-order tie-breaker. Included
regressions pin both the stale-exact-format and stale-active-entry cases.

This ownership correction is code- and unit-test verified. The post-change
corpus-gate run
`artifacts/game-runs/minecraft-texture/20260801-153601-corpus-gate` reached
`Player Spawned` at 110.406 seconds without device loss. A PrintWindow
`PW_RENDERFULLCONTENT` capture at that checkpoint is retained as
`checkpoint-kyty-writer-order.png`: geometry and previously detiled terrain
textures remain coherent, but large material regions are still black. Therefore
the stale-variant defects are real general correctness bugs but are **not the
remaining Minecraft material root cause**. The selected producer or one of its
sampled inputs remains the next boundary to measure.

The same run's trace proves that the panorama cubemap is GPU-produced rather than a
CPU upload: six dispatches of `cs=0x16AE4C00` write slices 0 through 5 of one
1024x1024 backing at `0x3BDC0000`; `ps=0x16B37000` then describes that address as a
six-face cubemap, and `vk.texture_variant_hit` selects an initialized 1024x1024
Vulkan image for it. The guest-memory probe remains zero by design because these
pixels have not been read back from the host image.

Minecraft also loads and starts all five directly required guest modules:

- `libc.prx`
- `libfmod.prx`
- `libRenoirCore.PS5.prx`
- `libcohtml.Prospero.prx`
- `libSceNpCppWebApi.prx`

The earlier 90-second run at
`artifacts/game-runs/minecraft/20260801-093459-http2-host-transport/attempt-01.log`
established module/service survival but did not capture a frame. It is retained as
the control for those earlier CPU-side fixes, not as rendering evidence.

## Settled root causes

### Render-target LOAD imported Sony-tiled memory as linear scanlines

Minecraft's 2048x1024 Flipbook atlas is a color target with CB tile mode 27.
Sony SDK 10.00 names that layout `CxRenderTarget::TileMode::kRenderTarget` and
documents it as the layout for large 2D render targets; it is not row-major.
The SDK's `AgcGpuAddress` tables provide the exact 64-KiB RB+ R_X address
equation already used by `GnmTiling` for mode 27.

`ProvideRenderTargetInitialData` bypassed that implementation. On first use it
read exactly `width * height * bytes-per-pixel` from guest memory and handed the
bytes directly to `vkCmdCopyBufferToImage`. Minecraft's later Flipbook draws
correctly updated small atlas regions, but they were drawn over this incorrectly
linearized LOAD content. Sampling the atlas therefore produced the black,
horizontally striped terrain while the sky, clouds, and UI remained correct.

The paired native-resolution captures make the failure measurable:

- Before the fix,
  `20260801-144433-corpus-gate` captured target `0x532B0000` as a dense field of
  horizontal stripes.
- After the fix,
  `20260801-145309-corpus-gate` captured target `0x61FE0000` as a coherent packed
  Minecraft atlas. The runtime reported `tile=27`, `decoded=1`, logical and
  physical sizes of 8,388,608 bytes, and no DCC metadata.
- `20260801-150504-corpus-gate/checkpoint-detiled-native.png` then captured the
  native loading screen with coherent UI/atlas content, and
  `checkpoint-detiled-world.png` verified the same correction in the world
  consumer. The latter still contains a distinct large-black-material defect.

Both images contained roughly 1.31 million nonblack pixels and 257 sampled
colors. Counts alone therefore could not distinguish the broken layout from the
correct one; the spatial image capture was load-bearing evidence.

The render-target seed path now reads the complete tiled footprint and applies
the Sony-derived address equation before uploading. It refuses unknown tiled
layouts rather than presenting tiled bytes as plausible scanlines, and it does
not decode a DCC-backed seed as raw color. The regression constructs a mode-27
surface from Sony's reference equation and requires the render-target LOAD path
to recover every row-major RGBA8 texel; separate cases require unknown modes and
DCC-backed data to be rejected.

`SHARPEMU_RENDER_SCALE=0.5` had hidden this defect: the logical 8-MiB seed no
longer matched the 1024x512 physical host image, so the bad initial upload was
skipped and only the later coherent sparse atlas updates remained. A scaled
capture was therefore a useful differential, not proof that native tiling was
correct.

### `sceSaveDataGetMountInfo` returned used blocks as free blocks

Prospero SDK 10.00 `save_data_defs.h` defines a save-data block as 65,536 bytes
and `SceSaveDataMountInfo` as `{ blocks, freeBlocks, reserved[32] }`. Sony's
`api_save_data_basic` sample supplies the allocation in `SceSaveDataMount3.blocks`
and prints both returned fields independently.

SharpEmu retained neither the mount's allocation nor that meaning. It always
reported 32,768 total 32-KiB blocks and wrote the number of *used* blocks into
the `freeBlocks` field. Minecraft mounts each world with 480 Prospero blocks.
The measured 1,359,222-byte world occupies 21 of those blocks, so the correct
answer is 459 free blocks; master instead told the title that roughly 21 were
free. Minecraft consequently spawned the player, showed real 3D, then raised its
storage-full modal.

The mount record now retains the requested allocation, usage is rounded in Sony's
64-KiB units, and `freeBlocks` is the saturating difference between allocation
and usage. Compatibility mounts with no explicit allocation use the SDK maximum
of 16,384 blocks. The regression test mounts 48 blocks, writes 65,537 bytes, and
requires `{ blocks=48, freeBlocks=46 }`.

The post-fix visual run remains in-world for at least 40 seconds between its two
retained captures and never displays the former capacity modal. This closes the
save-data failure independently of the unit test.

### Cubemap faces were collapsed into one host-image layer

Sony SDK 10.00 is explicit that a cubemap uses one 2D-array slice per face.
`sdk/target/include/agc/gnmp/texture.h` defines cube and cube-array slice ranges in
multiples of six; `gnmp/constants.h` describes faces as slices of a 2D texture
array; and the image-based-lighting samples render through face-specific array
views before sampling the same backing as a cubemap.

Minecraft follows that contract exactly. Sony's `libSceShaderIsaP.dll` identifies
the relevant instructions in `cs=0x16AE4C00` as non-arrayed `dim:k2d` image loads
and stores. The storage descriptor, however, is type 13 (`k2dArray`) and its raw
DWORD4 advances through:

- `00000000`: base/last slice 0
- `00010001`: base/last slice 1
- `00020002`: base/last slice 2
- `00030003`: base/last slice 3
- `00040004`: base/last slice 4
- `00050005`: base/last slice 5

The six source images are nonblack and all six stores target the same allocation.
The consumer descriptor is type 11 (`kCubemap`), depth 6, at that same address.

SharpEmu previously represented each storage view as a one-layer image. Reuse at
the same address overwrote face zero, and the later cubemap could not use the GPU
producer, so it sampled zeroed guest RAM. The fix separates backing allocation
shape from view shape: the host image retains all observed layers and is created
cube-compatible; each DIM2D storage operation binds one selected array layer; a
larger layered variant preserves already-produced layers; and the sampler creates
a six-layer cube view of that same GPU-authored image. The first run with this fix
made the 3D title panorama visible.

### The nine apparent missing NIDs were game-module exports

The calls first appeared in
`artifacts/game-runs/minecraft/20260801-092336-distinct-savedata-mounts/attempt-01.log`:

`PdOmR8dy1fM`, `ONEnhMinebs`, `vlmAvJqR7mQ`, `9U0bvCIzgIo`, `X3FzDPzpD2Y`,
`XUe8OAKB-vQ`, `AqBgviIjLjw`, `3R8wAeW86wM`, and `LWe+u8SHKgU`.

All nine dynamic symbols are encoded as `<nid>#A#B`. The eboot's authoritative
dynamic import tables decode `A` as the library `libcohtml.Prospero` and `B` as the
needed module `libcohtml.Prospero`. They are absent from the firmware and public NID
catalogs because they are not Sony OS exports. Minecraft supplies the provider as
`libcohtml.Prospero.prx` in the app root.

SharpEmu previously enumerated `sce_module`, `sce_modules`, `Media/Modules`, and
`Media/Plugins`, but not direct dependencies beside `eboot.bin`. The fix enumerates
root PRXs only when their normalized file name appears in the main image's
`DT_SCE_NEEDED_MODULE` set. It deliberately does not start unrelated root plugins,
such as `MediaDecoders.Prospero.prx`. Module names may contain dots, so normalization
removes only `.prx` or `.sprx`; `Path.GetFileNameWithoutExtension` would incorrectly
turn `libcohtml.Prospero` into `libcohtml`.

The first run with this fix loaded `libcohtml.Prospero.prx` with 704 runtime symbols,
loaded all five modules without failure, and eliminated the nine unresolved calls.

### The next crash was the Windows HTTP/2 transport bridge

After guest-module binding, the boot reached `sceHttp2SendRequestAsync` (NID
`A+NVAFu4eCg`). SharpEmu constructed an HTTP/2 request and called synchronous
`HttpClient.Send`. .NET's `SocketsHttpHandler` does not support synchronous HTTP/2 and
threw `NotSupportedException` before completing the guest call.

The host transport now calls `SendAsync(...).GetAwaiter().GetResult()` at the
synchronous HLE boundary. An async-only `HttpMessageHandler` regression test proves
that this path no longer relies on the unsupported synchronous handler method. The
next 90-second boot did not reproduce the dispatch error.

## Earlier settled root cause

Minecraft concurrently mounts `BedrockUserSettingsStorage` and
`BedrockLevelInfoCache`. Returning `/savedata0` for every successful mount overwrote
the first live mapping and led to a deliberate `std::out_of_range` termination while
building `/BedrockLevelInfoCache`. SDK 10 defines 16 simultaneous save-data mounts and
a busy error for a duplicate live slot. Allocating distinct `/savedata0` through
`/savedata15` mount points removed that crash and exposed the module-discovery defect
above.

## Renderer differential: packed attributes and sampler state

The maintained KytyPS5 checkout was updated through `bc436548` and used as a
differential, not as an ABI authority. Its packed `10:10:10:2` integer-format
change exposed a real gap in SharpEmu's AGC metadata bridge. The raw SRD and
Vulkan paths already handled the format, but `AgcVertexMetadata.MapAttribFormat`
silently fell back to float4 for the scaled and integer variants.

Sony SDK 10.00 closes the contract directly:

- `include_common/agc/core/vertexattribute.h` assigns 211, 215, 219, and 223
  to `k10_10_10_2UScaled`, `SScaled`, `UInt`, and `SInt`.
- `include_common/agc/core/buffer.h` and `format.cpp` identify the corresponding
  packed typed-buffer formats; `agc/gnmp/extras/dataformats.h` exposes
  `kDataFormatR10G10B10A2Uint`.

The metadata bridge now preserves all four numeric types. This is a general
correctness fix. No retained Minecraft draw proves that one of these four
formats caused its remaining dark material, so it is not labeled the visual
root cause.

The same differential found that SharpEmu decoded Sony's anisotropic sampler
filters as ordinary nearest/linear filters and ignored word 0 bit 15. SDK 10.00
`include_common/agc/core/sampler.h` defines word 0 bits 9..11 as the maximum
anisotropy ratio, bit 15 as `TextureCoordinates::kUnnormalized`, and word 2
filter values 2/3 as anisotropic point/bilinear. The renderer now enables host
anisotropy when supported, clamps the requested ratio to the Vulkan device
limit, and maps unnormalized descriptors onto Vulkan's legal clamp/no-mip/
no-bias/no-compare/no-anisotropy subset.

This sampler fix is title-active. The retained world log
`artifacts/game-runs/minecraft-texture/20260801-142338-corpus-gate/attempt-01.log`
contains sampler `00000612,00FFF000,06F00000,40000000`: Sony's fields decode
to anisotropic bilinear with an 8:1 maximum ratio. None of the retained
Minecraft samplers sets the unnormalized-coordinate bit. Correct anisotropic
filtering can improve Minecraft's texture quality, but it does not by itself
explain a solid-black surface; that attribution still requires a paired
producer/consumer capture.

## Renderer differential: vertex ranges across adjacent mappings

KytyPS5 `cc76827` exposed a second general Minecraft-relevant contract rather
than a title workaround. A vertex-buffer descriptor may begin in one committed
guest mapping and end in the immediately adjacent mapping. Kyty now acquires the
complete chain of touching committed ranges and stops only at an actual hole.

SharpEmu's host-backed `PhysicalVirtualMemory` already implemented that flat-VA
rule for `TryWrite`, but `TryRead` required the entire request to fit one internal
`MemoryRegion`. `Gen5ShaderScalarEvaluator.TryReadGlobalMemory` hid the rejection
by repeatedly halving the request until a prefix succeeded. A large vertex stream
crossing a bookkeeping boundary could therefore reach Vulkan with a silently
truncated suffix even though every byte was valid in the guest address space.

Sony SDK 10.00's APR contract independently supports the address-space model:
`ampr/apr_command_buffer.h` states that commands enclosed by a map sequence may
access addresses inside, outside, or crossing the map command's address range.
The map is not an artificial access boundary; mapped pages and any real gaps are.

`PhysicalVirtualMemory.TryRead` now walks adjacent regions under its exclusive
path, using the same contiguity rule as writes. It stages cross-region data so a
later gap or unreadable page cannot modify only the caller's prefix before the
operation reports failure. Included regressions require an eight-byte read split
across two adjacent mappings to succeed byte-exactly and require the same request
across a one-page hole to fail without changing the destination.

This is a verified VM/GPU-input correctness defect and a plausible explanation
for missing or streaked large Minecraft meshes. It is **not yet promoted as the
remaining visual root cause**: no retained draw trace recorded a vertex descriptor
that crossed such a boundary. The next visual run must compare the native world
capture against the current post-detile checkpoint and retain the actual vertex
range if the material changes.

That visual check is now complete and rejects this defect as the remaining
Minecraft cause. Corpus-gate run
`artifacts/game-runs/minecraft-texture/20260801-173132-corpus-gate` reached
`Player Spawned` at 98.031 seconds with no device loss. Its native-resolution
PrintWindow captures retain both boundaries: `checkpoint-60s.png` is a coherent,
textured loading screen, while `checkpoint-world.png` still shows large black
world regions and repeated or misplaced block textures. The adjacent-mapping
read fix therefore remains a general correctness import, but the next terrain
checkpoint must be later in vertex/fetch addressing, indexing, or draw assembly.

## Renderer root cause: instance fetch and mip-chain authority

The later vertex-metadata differential exposed one dropped Sony field.
`include_common/agc/core/vertexattribute.h` defines `m_fetchIndex` as selecting
the vertex index or instance index used to fetch an attribute. SharpEmu decoded
that bit into `MetadataVertexResource.PerInstance`, but `ApplyMetadataFormat`
discarded it before building `Gen5VertexInputBinding`. Vulkan therefore advanced
every metadata-described input per vertex. The bridge now preserves the field;
the existing presenter maps it to `VK_VERTEX_INPUT_RATE_INSTANCE`.

This was visually active. Corpus-gate run
`artifacts/game-runs/minecraft-texture/20260801-174107-corpus-gate` reached
`Player Spawned` without device loss, and `checkpoint-instance-rate.png` changed
the earlier exploded wall of duplicated geometry into spatially coherent trees,
terrain, and entities. Large black surfaces and repeated atlas strips remained,
so instance rate was a real geometry fix but not the texture root cause.

The remaining terrain corruption was a resource-authority error. Sony SDK 10.00
`include_common/agc/core/texture.h` defines `m_maxMip`/`setNumMipLevels()` as the
total number of mip levels present in the allocation, independently of the
currently addressable `m_baseMipLevel`/`m_lastMipLevel` view. Minecraft's
2048x1024 terrain atlas is a four-level texture (`MAX_MIP=3`) composed in guest
memory. A small number of animated-tile draws also bind the same base address as
a color target.

SharpEmu's submit-thread availability answers carried only the guest address and
format. Once any draw registered that base as GPU-produced, the texture path
skipped the guest-memory read even though the retained render-target image had
one mip. The presenter then treated mip-count equality as only a ranking hint,
accepted the one-mip image as an alias, and discarded any decoded atlas payload.
The result was the attachment's mostly-clear content plus a few animated tiles:
large black materials and repeated/misplaced strips instead of the CPU-composed
atlas.

Two sides of the contract are now enforced:

- an ordinary 2D mip chain cannot use an address-only image-availability answer;
  it falls through to the guest-memory detile/upload path;
- sampled Vulkan aliases require the host guest-image resource mip count to equal
  the Sony descriptor's total resource mip count.

The second corpus-gate run
`artifacts/game-runs/minecraft-texture/20260801-175746-corpus-gate` reached
`Player Spawned` at 92.797 seconds with no device loss. Its native PrintWindow
`checkpoint-150s.png` is the decisive post-fix frame: the underwater world has
coherent per-texel water and stone detail, with none of the prior atlas-strip
repetition or large black material. The blue cast is the game's underwater
state. `checkpoint-180s.png` is black including the guest HUD and is therefore a
transition/death frame, not contrary terrain evidence. The first run's lone
black capture is rejected for the same reason.

That two-sided guard fixed the gross corruption, but it did not yet preserve the
complete Sony resource. A second, bounded SDK differential now covers the actual
Minecraft allocation. SDK 10.00 `libSceAgcGpuAddress.dll` reports the 2048x1024,
four-level, mode-27 atlas as a 0xAA0000-byte allocation with mip offsets
`0x2A0000`, `0xA0000`, `0x20000`, and `0`. The production equation matches those
offsets and sizes exactly. A separate 256x128, nine-level oracle case also
matches Sony's first-tail level and every packed tail coordinate; this corrected
the inherited even/odd tail-axis assumption.

The texture bridge now carries every resource mip into one Vulkan image, creates
the sampled view over the descriptor's requested mip range, and retains the
physical mip-0 address separately from the allocation base. When an animated
tile draw produces a newer one-level image, only that GPU-written mip 0 is copied
over the CPU-composed chain; the lower mips remain authoritative guest data.

Final visual acceptance is now **MEASURED**. Corpus-gate run
`artifacts/game-runs/minecraft-texture/20260801-204511-corpus-gate` reached
`Opening level` and `Player Spawned` without device loss. Its native PrintWindow
capture `checkpoint-postspawn-20s.png` shows coherent per-texel dirt, grass,
leaves, sky, clouds, and HUD. The earlier large black materials and repeated or
shredded atlas strips are absent.

The paired queue trace identifies the exact sampled resource rather than
inferring it from the frame: address `0x5DEE0000`, 2048x1024, format 10, tile
mode 27, four resource mips, and a 0xAA0000-byte decoded payload. The measured
guest write generation was zero. One payload was published and every subsequent
observed bind reused it. The retained implementation therefore shares one
immutable decoded array for an exact descriptor identity and write generation;
a new generation cannot reuse stale texels. Weak cache values avoid making this
a second permanent texture cache after queued draws retire.

The same run also corrects an earlier performance attribution. Process private
memory had already risen from about 8.2 GiB to 15.4 GiB at spawn before the first
full-mip event, then reached 21.8 GiB while the trace still showed one mip key,
one 10.625 MiB publication, and reuse thereafter. The remaining broad queue
growth is real but is **not** retained copies of this atlas and is not evidence
against the texture result. It remains a separate performance investigation.

Raeen was used as a Minecraft-specific differential after this capture. Its
successful Prospero mode-27 equation is byte-identical to SharpEmu's current
Sony-oracle equation, and its mip-chain/live-target authority rule is already
the same rule above. Its rotating texture hash audit compensates for a sparse
hash cache; SharpEmu instead has page write-protection and generation tracking,
so importing the scan would not close a demonstrated gap.

## Dead ends and non-findings

- Implementing the nine CoHTML NIDs as HLE is incorrect; the title ships their real
  implementation and ABI.
- The many failed probes for optional resource-pack archive/directory variants are not
  individually evidence of missing game data or a filesystem bug. The title continues
  probing other variants after each miss.
- Worker `pthread_cond_wait` warnings are not the current stop. The main process kept
  executing hundreds of millions of imports, and no unresolved-at-exit evidence was
  recorded.
- The relevant `image_store` instruction is DIM2D, not DIM2DArray. Changing this
  shader's SPIR-V coordinate to `ivec3` would contradict Sony's disassembly. Its
  array layer comes from the descriptor view, not the instruction coordinate.
- Zero texels in the guest-memory probe do not mean the cubemap producer is empty.
  The six source probes are nonblack, and the corrected consumer selects the
  initialized GPU-resident image. A readback probe and a provenance trace answer
  different questions.
- A successful scripted timeout proves survival and continued work, not rendering.
  The retained PrintWindow capture is the visual checkpoint.
- The HUD-over-black capture at
  `20260801-120348-get-ingame-auto-cross-2/checkpoint-world-90s.png` was a
  transition, not a demonstrated terrain-rendering endpoint. A later run on the
  same GPU code visibly rendered an in-world sky and terrain. Do not revive that
  black capture as a producer failure without a paired producer/consumer trace.
- Sony-oracle disassembly of Minecraft's shipped RenderChunk prepass,
  ForwardPBR, and deferred-shading AMDGPU ELFs found no missing instruction
  family in their 131 normalized mnemonics. None uses the non-gather dynamic
  sample-offset family implicated by the host's missing `VK_KHR_maintenance8`
  warning. That warning is not the terrain cause for this shader corpus.
- The Flipbook vertex input was byte-exact in both native and half-scale runs:
  half-float positions, UNORM16 texture coordinates, AGC semantic metadata, and
  the 54-vertex indexed draw all agreed. The pixel shader's `s_wqm_b64`/EXEC
  control flow and the later render-target/texture alias were also downstream of
  the first corrupt checkpoint. Do not revive them as the terrain root cause
  without evidence that the corrected atlas itself has regressed.
- Do not classify the dark-red `checkpoint-140s.png` frame as broken lighting.
  Steve is underwater and dying; the tint is guest gameplay state.
- KytyPS5 `42f634a` fixes embedded-fetch shaders that add a vertex offset with
  `V_SAD_U32`. No captured Minecraft vertex shader uses that instruction.
  Minecraft does contain overlapping loads such as
  `buffer_load_format_xyzw v[0:3], v0, ...`, but SharpEmu's SPIR-V lowering
  captures the byte-address SSA value before any destination store and its
  formatted path calculates all canonical components before writing VGPRs.
  Copying Kyty's grouped-emission patch would therefore not change this title's
  generated program.
- KytyPS5 `212282d` exposed a real general SharpEmu gap for compressed exports
  into `R16G16B16A16_UINT/SINT`: the packed 16-bit integer fields were being
  interpreted as half floats and numerically converted. Sony SDK 10.00 ships
  both integer target formats and integer clear pixel shaders. The translator
  now extracts the two packed fields with signed/unsigned bitfield operations.
  All captured Minecraft MRT outputs are floating-point, so this correctness
  import is deliberately not claimed as the remaining terrain fix.
- Raeen `96da89e` adds a rotating exact-hash audit because its sparse texture
  cache probe can miss CPU atlas writes between sampled windows. SharpEmu does
  not share that approximation: `GuestImageWriteTracker` write-protects every
  tracked source page and invalidates on the first guest CPU write. Porting the
  rotating scan would add recurring multi-megabyte reads without closing a
  demonstrated SharpEmu gap.
- Raeen `72cde40` is independent corroboration rather than a port candidate: its
  mode-27 2-byte-per-element swizzle regression explicitly imports SharpEmu's
  `RbPlus64KRenderX2Bpp` mask derivation and passes byte-exactly. It strengthens
  the existing tiling evidence but adds no missing implementation here.

## Verification

- Clean Release build: 0 warnings, 0 errors.
- Focused cubemap/NGG/libc/save-data tests: 41 passed, 0 failed.
- Full solution tests: 2,641 passed, 0 failed.
- Packed-attribute and sampler regressions: 12 passed, 0 failed.
- Root-module regression tests cover both direct-needed discovery and exclusion of an
  unrelated app-root PRX.
- HTTP/2 regression test covers an async-only host transport.
- Cubemap regressions cover sampled cube descriptors, per-face storage slice
  selection, backing-layer compatibility, and cube-view compatibility.
