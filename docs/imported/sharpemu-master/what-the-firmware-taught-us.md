<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# What running Sony's own modules taught us about our HLE

Executing Sony's `libSceAgc`, `libSceAgcDriver` and `libSceGnmDriver` as native
code did not make a game render. Its value was different and larger: **Sony's
driver is an oracle that audits our kernel.** It checks return values, validates
arguments, and prints its own diagnostics when we lie to it. Our own titles
never complained, because a game does not audit the OS it runs on.

The extracted Prospero SDK 10.00, its host tools, and the available Sony/AMD
GPU Shader Core ISA pages from SDK 12.000 are GPU-side contract oracles. They resolve
questions that firmware frequency scans and AMD-family tables cannot:
`CxVgtShaderStagesEn` supplies Sony's actual wave and stage masks;
`CxCbControl::kDccDecompress` supplies mode-6 semantics; the metadata samples
show when raw backing bytes are not pixels; and the native primitive samples
define target 20 as three 10-bit subgroup-relative indices. The captured SDK 12
text additionally defines operand 125 as architectural `NULL`, but it is only
about 47 pages of a roughly 547-page reference. It contains no instruction
bodies for vector memory, LDS/GDS, image, FLAT, or export. Absence from that
capture is therefore not evidence that Prospero lacks an instruction.

The completed SDK 10.00 extraction supplies stronger executable checks.
`libSceShaderIsaP.dll` disassembles raw instructions when its generation option
is set; passing null options silently selects generation zero and can call
valid Prospero encodings invalid. The checked-in
`tools/SharpEmu.Tools.ShaderIsaOracle` recovers the disassembly, size, and
result-freeing ABIs and runs Sony, LLVM fixtures, and SharpEmu's production
decoder over identical bytes. Generation 2 is the lowest value accepting the
exact Astro BVH instruction, and the size API takes a raw 64-bit instruction
value rather than a pointer. The first proved local divergences were gfx1013
`buffer_gl0_inv` and `buffer_gl1_inv`; both now decode and lower to Vulkan
device-scope acquire visibility. `libSceAgcGpuAddress.dll` and its shipped
source define exact surface layouts. The checked-in
`tools/SharpEmu.Tools.AgcGpuAddressOracle` compares Sony's complete detile with
the production detiler for every default-trusted public 2D single-sample mode;
it caught failures that an exact Astro RGBA16F case did not. Use LLVM gfx1013
tablegen/MC fixtures and AMD's RDNA1 guide for a missing family, but verify disputed bytes against
Sony's executable oracle. Prefer the Sony contract for Sony-facing fields
when another implementation targets a different layer. Use firmware and title
captures to establish which path executes, then use the SDK to define what
that path means.

The word “community” is not a useful evidence class. AMD/LLVM's ROCm
documentation is primary toolchain documentation. Both payload SDK
generations, fail0verflow's Prosperous, and the PS5 Linux patches are
**primary implementation evidence**: curated, executable work from engineers
who built payload environments, kernel/exploit machinery, or a bootable Linux
hardware stack. Their scope is the exact layer, target, and revision they
implement; that scope is not a judgment that the work is weak. Exact
revisions, permitted inferences, confirmed facts, and refuted leads are kept
in `evidence-source-ledger.md`.

Every defect below was found that way, and **every one of them was wrong for
every title, not just under LLE.**

## New contracts extracted during the 2026-07-31 general-alignment pass

The SDK and firmware were used to replace several plausible host behaviors
with exact guest contracts:

- `CxCbControl` modes are compositional. Mode 5 decompresses FMASK and also
  eliminates fast clear; mode 6 adds DCC decompression to both effects. The
  CB_COLOR_INFO CMASK/FMASK controls describe prerequisites, not decoded
  pixels. On an expanded Vulkan image, elimination preserves pixels already
  written by a newer GPU draw instead of replacing the entire image with the
  fast-clear words. SharpEmu records those transitions without claiming to
  decode the metadata allocation.
- SDK `agc/gnmp/texture.h` makes a sampled/storage texture's DCC metadata
  address part of its render-target alias contract. Storage-image promotion
  must carry that address just as it carries format, dimensions, and tile
  mode. Dropping it made Astro's compressed sampled view reject the real GPU
  output and choose a zero guest-memory upload; forwarding it preserves the
  exact lifetime while different DCC allocations remain incompatible.
- Sony's shipped `libSceAgcGpuAddress` source and DLL compute surface and
  metadata layouts and can tile or detile metadata keys. They do **not**
  reconstruct DCC- or HTILE-compressed pixels, and there is no corresponding
  FMASK metadata decoder because FMASK is itself a tiled surface. Sony's
  Toolkit performs DCC, HTILE, and FMASK decompression with CB/DB fullscreen
  GPU operations. P/Invoking the address library is therefore an exact layout
  oracle, not a substitute for Vulkan image-state transitions and resolves.
- HTILE identity belongs to a depth surface lifetime. SDK sync/toolkit headers
  distinguish texture-compatible compressed depth (flush for texture) from
  incompatible compressed depth (explicit decompression and possible HTILE
  fixup). Reusing a host depth image now requires the same HTILE allocation;
  otherwise SharpEmu refuses to interpret compressed Z backing bytes as depth.
- Sony's ISA library names gfx1013 GLOBAL atomic INC/DEC at FLAT opcodes
  `0x3c/0x3d`, and pins the scalar bit-count/reverse/sign-extension family.
  The decoder follows those bytes. The Vulkan backend implements their
  dynamic unsigned DATA threshold with a compare-exchange retry loop rather
  than SPIR-V's operand-free increment/decrement operations.
- Sony's generation-2 ISA oracle also accepts `ds_write_addtid_b32` and
  `ds_read_addtid_b32` on Prospero, including Astro's exact `0xDAC00700` word,
  while LLVM deliberately rejects them for stock gfx1013. This is a concrete
  custom-target exception. Sony pins the bytes and operands; AMD's RDNA1
  specification pins the LDS address as `M0[15:0] + thread_id*4 + offset16`.
  SharpEmu implements that bounded LDS pair and leaves GDS variants rejected.
- Native primitive replay must be selected by submitted contracts, never a
  shader address. The direct-export point-to-triangle subset now derives its
  subgroup counts, output capacity, amplification and parameter layout from
  Sony register state plus decoded exports. Unsupported wave32, multi-wave,
  instanced, culling and emit/cut shapes still fail closed.
- SDK `sys/dirent.h` defines variable records, not fixed 512-byte entries;
  `sceKernelGetdents` now packs every complete aligned record and preserves the
  directory offset after a guest-memory fault. SDK `kernel/uuid.h`, the exact
  firmware wrapper, and the kernel UUID generator pin the UUID-v1 field layout
  and error behavior. Windows supplies only the kernel's random-node fallback,
  never a fabricated Prospero MAC address.
- The firmware HDR query has exact fallback float words when its display
  service lookup is unavailable. That is the appropriate Windows translation;
  an emulator performance profile is not a guest display capability.
- SDK 10.00 defines all five `ScePadVibrationMode` values. Complete 4.03 and
  9.00 `libScePad` bodies independently pin the validation order: module
  initialization, public mode range, then handle lookup. The Windows mapping
  now retains the guest's mode and returns the exact firmware errors instead
  of treating the call as an unconditional-success shell.
- The exact Astro final-tonemap pair is now an oracle-backed negative control.
  Sony's ISA library and SharpEmu agree on all 350 pixel and 69 vertex
  instruction names and sizes, and a source-triggered same-draw capture proves
  the unmodified shader turns 2,146,458 nonblack source pixels into 4,951,550
  nonblack target pixels. That closes outer decode and the binary-black
  postprocess claim, but not operand-level numerical accuracy: the visibly
  dark PlayStation Studios capture keeps color/exposure semantics open.
- A DCC address and active DCC compression are separate descriptor facts.
  Sony SDK 10's DCC sample copies a render target, disables only
  `DccCompression`, and uses that copy for raw backing access while the DCC
  base remains installed. SharpEmu now gates the active metadata lifetime on
  `CxRenderTarget::DccCompression::kEnable`. GNMP similarly says FMASK mode
  bits are ignored when FMASK compression is disabled; stale one-fragment or
  uncompressed bits no longer fabricate an invalid state.
- Sony's generation-2 ISA oracle and LLVM gfx1013 fixtures agree on the exact
  VOP2 slots for scalar-half add, sub, reverse-sub, multiply, min and max.
  These now lower through the existing exact software half widen/narrow path.
  Nonzero VOP3 `op_sel`, SDWA/DPP controls, and state-register half operands
  remain explicit rejects instead of guessed semantics.
- Prospero dynamic imports preserve attribution outside the NID token.
  Firmware `DT_SCE_NEEDED_MODULE` (`0x61000045`) and
  `DT_SCE_IMPORT_LIB` (`0x61000049`) pack a numeric ID, version fields, and a
  string-table offset; symbol suffixes encode those IDs with Sony's 64-symbol
  alphabet. The loader now retains both decoded lists on `SelfImage`, allowing
  callers to join `NID#libId#modId` to the exact imported names.
- Native-primitive launch counts are per wave, while connectivity indices are
  subgroup-relative. SDK 10's `native_prim.pssl` names
  `S_NGG_VERTEX_COUNT`/`S_NGG_PRIMITIVE_COUNT` as the current wavefront's
  inputs and computes a group thread ID from lane plus wave index. Repeating
  the subgroup count in every output wave and seeding `v5` from `lane & 63`
  duplicated work after lane 63. Mesh lowering now subtracts each wave's
  64-lane base before packing `s_gs_wave_id` and uses the group-local
  invocation for `S_NGG_VERTEX_INDEX`.

Two dead ends are equally important. The GDS append/consume range and clear
path agrees byte-for-byte with Sony's SDK and succeeds in retained Astro runs,
so the intermittent submit loss at `6EAC00` is not presently a GDS finding.
Matching Windows WATCHDOG dumps classify the recent incidents as video TDRs;
the targeted boundary run proved the Vulkan call naming `6EAC00` was only the
first observer. All 32 preceding `571000` chunks retired, then an empty
sentinel submit observed the reset before a new `6EAC00` command was recorded.
The Windows translation now inserts a host fence wait between compute chunks;
back-to-back Vulkan submissions alone did not create a WDDM scheduling
boundary. Plain run `20260731-185733-corpus-gate` validates that host
translation without diagnostic overrides: LOGO, TITLE, and worldmap were all
reached during the 480-second gate, with no Vulkan device loss and no new
WATCHDOG dump. A later source-triggered run closes the binary-black final-frame
finding and captures genuine but severely dark guest output. The remaining
rendering question is luminance/color correctness and later interactive-world
content, not whether the postprocess chain can carry nonblack pixels.
Likewise, seeing `s_trap` during translation proves only static presence in the
program image. Without same-PC runtime evidence, it must not be blamed for a
device loss.

## The bugs it found in our HLE

### 1. `sceKernelGetAppInfo` cleared 256 bytes into an 88 byte contract

EXTRACTED from `libkernel.sprx` vaddr 0x20280: the real implementation is a
single sysctl with `*oldlenp = 0x58`, so 88 bytes is the most it can ever write.
We cleared `0x100`.

Callers size their buffer for the real contract. `libSceAgcDriver` reserves
exactly 0x78 for it at `rbp-0xa8` and keeps its **stack canary at `rbp-0x30`**,
immediately after, so our over-clear landed on the canary and nothing else. The
guest died in `__stack_chk_fail` with no indication of who had written there.

Generalise the lesson, not the constant: **an over-clear is a write.** Any HLE
export that zeroes "enough" of an out-parameter is corrupting whatever the caller
put next to it. Recover the real length from the real implementation.

### 2. Module initializers ran in load order

`RunPreloadedModuleInitializers` walked modules in whatever order they loaded.
`libSceAgc`'s initializer asked `libSceAgcDriver` for its memory arena before
that driver had initialised, got `addr = 0, size = 0` **with a success return**,
printed its own complaint, and never re-asked. 255,000 dispatches later its bump
allocator returned NULL.

Hardware orders initializers by dependency. Now so do we, with a stable
topological sort. Any title with inter-module dependencies was exposed to this.

### 3. The device ioctl path never set the guest return register

Every exit from `KernelIoctlCore` goes through `Ok`/`Fault`, which write `RAX`.
The `/dev/gc` path returned without touching it, so the guest read a stale
register: `call ioctl; test eax, eax; jne` treated a **successful** ioctl as
failed. The reply was right and the return path was wrong, which is why
implementing the ioctl did not help on its own.

**Check what the guest reads, not what we return.** A C# return value is not the
ABI.

### 4. The title loaded inside Sony's system address window

`sceAgcInit` classifies its own caller's return address: `retaddr < 0x800000000
|| retaddr >= 0xC00000000` means "the title". EXTRACTED, and corroborated by
`libkernel.sprx` file offset 0x1CD5E running the identical two-bound idiom to
pick an mmap hint.

We loaded `eboot.bin` at `0x800000000`, dead centre of the system window, so
every guest call looked like a system call. Our own `Ps4MainImageBase` was
already outside it, as hardware expects. Moved to `0xC00000000`.

Related trap found while moving it: five of the eight sites keyed on that base
used it as an **exclusive upper bound**, and four of those are the IL2CPP object,
class, string and field recognisers, meaning "above 4 GB and below the image".
Moving the base downward would have widened them by 16 GB and silently
reclassified every pointer the backend inspects. `Ps5ManagedHeapEnd` is now its
own constant. See `GuestImageLayout`.

### 5. `sceAudioOut2PortGetState` accepted the invalid-handle sentinel

The gate read `portHandle <= uint.MaxValue`, but
`SCE_AUDIO_OUT2_PORT_HANDLE_INVALID` is `0xFFFFFFFFFFFFFFFF`, wider than that.
The one handle that is invalid by definition was the one handle always accepted,
answered with success and a **fabricated connected-and-ready port state**.

## The bug it found in our gate

`corpus_gate.py` passed `--no-screenshot` on every run, so the corpus gate had
been blind for its entire existence. It stays off for now, but for a stated
reason rather than an oversight: the harness's capture is GDI
`GetWindowRect` + `CopyFromScreen`, which cannot read a DXGI flip-model window
and raises "The handle is invalid", killing the run before it writes a manifest.
`PrintWindow` with `PW_RENDERFULLCONTENT` does work and is the fix.

## Method lessons, paid for twice each

**A share is not a measurement.** The AudioOut2 retry was called the blocker at
99.3% of the log; the healthy control is 97.4%, and the stuck run spins it 1.7x
faster. Then `internal_expf` was called the divergence at 66.6% versus 36.7%;
rate-normalised it had risen 1.32x while everything else fell to 0.33x, and the
healthy run reaches 83-86% expf **while progressing**. Normalise by time, and bin
by phase before comparing runs of different lengths.

**Symbolise by size, not by nearest preceding export.** A fault was reported
inside `sceAgcGetDefaultCxStateFlat` because that was the nearest exported
symbol; the function has `st_size = 0x71` and the real code was an unexported
helper 0xBF further on. Check `st_size` and confirm containment.

**Read your own instrumentation before believing it.** A debug HUD on screen was
read as the game's own output, and taken as proof the guest render path worked.
It was `PerfOverlay.cs`, ours, on by default. The guest was drawing nothing.

**Refuse loudly rather than answer plausibly.** Six `/dev/gc` ioctls are still
refused with ENOTTY, logged once each and enumerable. That is why every failure
so far has been locatable: the driver stops at a named request instead of
proceeding on a lie and dying somewhere unrelated. `0xC010813B` is the clearest
case, and it also marks the real boundary of this approach: the driver prefills
its 16-byte buffer with `0xFF` before the call precisely so it can tell a kernel
that wrote nothing from one that wrote zeroes. Those sixteen bytes are hardware
state we cannot derive.

## What this means for the HLE effort

LLE needs us to emulate the **Oberon GPU's kernel interface** (doorbell apertures
at `0xFE0200000`, ring descriptors, trap handler resources, wave traps) rather
than the graphics API above it. That is a strictly larger surface than HLE, and
at least one point on it is currently unfabricable.

So HLE remains the shorter road to a rendered frame. The firmware's contribution
is that our kernel is now measurably more honest underneath it, and that we have
a working technique: **when something is unexplained, run Sony's own module and
let it audit us.**
