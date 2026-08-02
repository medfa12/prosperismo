> **Session startup:** read `docs/evidence-source-ledger.md`, especially
> "Ground-truth coverage snapshot", before inferring that a Sony contract is
> unavailable or requesting another reference. Prefer the SDK source/oracles
> already on disk over memory or analogy.

## Treat cubemap faces as views of one layered allocation (2026-08-01)

Do not derive host image identity from the descriptor's currently selected slice.
Sony's SDK defines cubemaps as a single backing allocation with one 2D-array slice
per face. A compute shader may write each face through six ordinary 2D storage
views, then sample the same allocation through one cubemap view. The storage
instruction dimension and the backing image's layer count are therefore separate
facts.

Minecraft provided a byte-exact example. Sony's ISA disassembler identifies the
instructions in `cs=0x16AE4C00` as `image_load ... dim:k2d` and
`image_store ... dim:k2d`. Six dispatches nevertheless target one 1024x1024
`k2dArray` descriptor allocation, with DWORD4 selecting slices 0 through 5 as
`00000000`, `00010001`, `00020002`, `00030003`, `00040004`, and `00050005`.
The later pixel shader describes the same address as a six-face cubemap. This
matches SDK 10.00's `gnmp/texture.h`, `gnmp/constants.h`, and the light-probe
samples: render through per-face array views, sample through a cubemap view.

SharpEmu previously allocated a one-layer Vulkan image for each storage view, so
each face replaced face zero. The cube consumer then could not form a compatible
GPU alias and fell back to zero guest bytes. Retaining a layered, cube-compatible
image, binding each storage operation to its selected layer, preserving earlier
layers when the observed descriptor grows, and forming a six-layer cube view made
Minecraft's 3D title panorama visible. The verified capture is
`artifacts/game-runs/minecraft/20260801-115002-layered-cube-alias-proof-2/checkpoint-title-3d.png`.

## Attribute an unknown NID to its import library before writing HLE (2026-08-01)

An unresolved NID is not necessarily a missing operating-system contract. Prospero
dynamic symbol names carry `<nid>#<library-id>#<module-id>`, and the image's
`DT_SCE_IMPORT_LIB` / `DT_SCE_NEEDED_MODULE` entries map those encoded IDs to their
owners. Decode that attribution before searching firmware or adding an export.

Minecraft exposed the concrete failure mode. Nine unknown calls all carried `#A#B`;
its own dynamic metadata maps library `A` and module `B` to `libcohtml.Prospero`.
The title ships `libcohtml.Prospero.prx` beside `eboot.bin`, but SharpEmu searched only
`sce_module`-style subdirectories. Adding nine HLE stubs would have replaced shipped
Coherent Labs code with invented behavior. Loading only root PRXs named by the main
image's needed-module table supplied all nine exports, preserved their real ABI and
state, and advanced the boot with no unresolved function imports.

## Do not widen texture page protection into a generic GPU-buffer cache (2026-08-01)

Prospero unified VA does not imply that every shader buffer is exclusively
GPU-owned. A range presented as a read-only storage buffer for one draw can
overlap CPU continuation, allocator, or service state elsewhere in the title.
`GuestImageWriteTracker` is an ownership mechanism for specific host-image
cache entries, not a general memory-generation oracle.

Astro provided a causal negative control. Page-protecting every persistent
global-buffer binding and skipping `SequenceEqual` on an unchanged tracker
generation produced the same CLR fatal error twice before LOGO, at about 2.45
million imports, in `ExecuteGuestContinuationEntry`. Removing only that change
let the same boot pass 16 million imports and reach LOGO, TITLE, and worldmap.
A correct global-buffer fast path needs write generations from the VM/mapping
layer or an explicit non-overlapping ownership proof, not another address-range
assumption.

## Recalibrate sequence gates after removing GPU backlog (2026-08-01)

Guest work sequence is an ordering token, not a wall-clock milestone. A fix
that stops a shader from spinning can let the renderer consume the queue fast
enough that a later scene is reached with a *lower* sequence number than in a
slow control. Astro's clustered-light fix reduced `ps=0x5008F1400` from about
132 ms to 0.24 ms, and a diagnostic gate copied from the slow run (`1035400`)
never fired because the corrected run reached only about `996028`. Before a
multi-minute boot, verify the flag is wired, enable its cheap occurrence
counter, and derive a conservative gate from the most recent behaviorally
equivalent run. Silence above a stale gate is not evidence that the work did
not execute.

## Trigger paired captures from the discriminating content state (2026-07-31)

A fixed draw ordinal is not a stable rendering boundary. The same shader and
target can execute repeatedly while its source allocation moves through clear,
partially populated, and presentation-ready lifetimes. Pair the source and
destination on one submitted command, and trigger the measurement only after
the source satisfies the condition under test. Record the work sequence,
resource identity, format, metadata identity, writer identity, content hash,
and nonblack count on both sides.

Astro's final tonemap demonstrates the failure mode. Draw ordinal 400 of
`ps=0x500640D00` sampled a black `0x53C420000`, so its black output said
nothing about tonemap correctness. A source-triggered capture later observed
2,146,458 nonblack RGBA16F pixels in that exact source and, on the same
unmodified draw, 4,951,550 nonblack A2R10G10B10 pixels in target
`0x5093F0000`. A `PrintWindow(PW_RENDERFULLCONTENT)` capture from the same run
showed the real PlayStation Studios image, although it was much too dark.
The remaining defect is therefore luminance/color correctness, not a postprocess
chain that still reads uniformly black.

## Preserve compression identity across every view role (2026-07-31)

The pixel allocation, its metadata allocation, and its current expanded host
representation are separate facts. A resource changing from render target to
storage image to sampled texture does not erase the Sony descriptor's DCC
association. Carry that association through every role, while still rejecting
same-address host images belonging to a genuinely different metadata lifetime.

Astro exposed both ways to lose this distinction. First, SharpEmu treated a
mode-6 DCC-decompress draw as permission to clear the entire expanded Vulkan
image with the fast-clear value. Sony defines mode 6 as DCC decompression plus
FMASK decompression and fast-clear elimination; it materializes compressed or
untouched fast-cleared regions, not pixels written by a newer color draw. The
old whole-image shortcut changed writer serial 35948 from `ps=0x5006CB800` to
serial 35949 `metadata-fast-clear`, and the next `6CE200` draw inherited black.
An already-initialized GPU-authored host image must therefore be preserved;
constant metadata clears retain their independent, writer-ordered path.

Second, the storage resolver copied address, dimensions, format, and tile mode
from `Core::Texture` but dropped its DCC address. The later compressed sampled
view correctly refused the resulting metadata lifetime and fell back to a
zero guest-memory upload. Sony SDK 10.00's texture contract explicitly says
that a texture aliasing a DCC render target points at that target's DCC buffer.
Forwarding the descriptor metadata address fixed the role transition without
weakening cross-lifetime alias rejection. Runtime evidence then changed
`ps=0x50065D500` from `cached-upload/tile0/uninitialized/writer0` to the
initialized GPU color image with matching DCC `0x57054E000`.

This is also a diagnostic lesson: log both requested and selected metadata
identity, and gate address-wide writer telemetry by work sequence. Counts of
same-address bindings cannot distinguish the right lifetime from a black
fallback.

## Expand implied primitives after guest vertex shading (2026-07-31)

Sony SDK 10.00 defines `sce::Agc::UcPrimitiveType::kRectList` as value `7`
and defines its three input vertices as upper-left, upper-right, and lower-left.
The lower-right corner is a primitive-assembly result, not a fourth invocation
of the guest vertex shader. Mapping value 7 to a four-vertex Vulkan strip is
therefore wrong: the fourth vertex has no guest input and executes arbitrary
vertex-fetch/procedural logic.

On Vulkan, preserve the guest's three invocations and expand the rectangle
between vertex and fragment shading. SharpEmu's fixed geometry stage consumes
one triangle, forwards the three post-VS positions and varyings, synthesizes
the fourth as `upperRight + lowerLeft - upperLeft`, and emits a four-vertex
triangle strip. Include the geometry module digest in pipeline identity and
enable `VkPhysicalDeviceFeatures::geometryShader` only when the device reports
it. A byte-level test must pin the geometry execution model and modes, four
`OpEmitVertex` instructions, and one `OpEndPrimitive`; a real Vulkan pipeline
creation is the driver-acceptance checkpoint.

Astro is also the negative control. A naive fourth guest vertex made the title
fully black. The correct post-VS expansion passes offline validation on the
AMD V620 and reaches LOGO/TITLE without device loss, but retained PrintWindow
captures remain a black movie rectangle and then a guest-black title. Thus
primitive 7 was a confirmed general translation gap, not the first point at
which Astro's postprocess content becomes zero. Do not promote a structural
fix to a title root cause without a paired content differential.

## Replay exact guest binaries before spending another boot (2026-07-31)

When the title binary, runtime descriptors, and a vendor ISA oracle are
available, a shader boundary should become an offline differential before it
becomes another multi-minute boot. Recover the exact program by measured PC
signature, compare every instruction byte sequence with the platform
disassembler, recreate only state observed in retained logs, and run the
translated program on the target host GPU with a discriminating input. Keep
decode agreement, translation correctness, and live resource selection as
separate checkpoints.

Astro's `cs=0x500690F00` demonstrates the method. Its 1,112 instructions from
`eboot.bin` match Sony SDK 10.00's `libSceShaderIsaP.dll` exactly by size and
opcode. The exact translated program preserves nonblack RGBA16F input on the
AMD V620 at the live 1920x1080 input / 2432x1368 output dimensions for all
3,326,976 output pixels. That result cannot prove which live
Vulkan image a title command selects, but it eliminates the decoder, shader
lowering, and driver from that boundary without waiting through an intro. The
only justified follow-up boot is therefore a same-command source/output
capture, not another final-frame observation.

## A synchronous Vulkan drain must retire the emulator ledger too (2026-07-31)

`vkQueueWaitIdle` proves device completion; it does not update an emulator's
parallel submission/resource ledger. Diagnostic code that flushes the guest
batch, waits the Vulkan queue, and then leaves tracked fence entries pending
can make the next capacity check wait on work that has already finished. The
symptom is especially misleading when retained bytes are conservatively
counted per descriptor binding: one physical image bound many times appears
as a huge incoming workload.

Astro's paired composite probe reproduced the trap. It captured two
byte-identical nonblack images, then the next 48-sample `690F` dispatch was
estimated at approximately 984 MiB. Backpressure climbed to 512 while the
guest work sequence remained fixed, not because the title or shader stopped,
but because the raw queue-idle path had not collected SharpEmu's completed
submissions. Synchronous diagnostic drains must use the same fence-wait,
completion collection, command-buffer release, and retained-resource release
path as normal CPU visibility. A one-shot readback submitted outside that
ledger may still use a raw queue idle.

## Decode DS offsets by instruction family, not by field names (2026-07-31)

GFX10 encodes the first two bytes of every DS instruction in fields commonly
named `offset0` and `offset1`, but those names do not give them one universal
meaning. LLVM's authoritative `DSInstructions.td` maps ordinary DS operations'
single 16-bit byte offset as `offset0=offset{7:0}` and
`offset1=offset{15:8}`. DS2 operations instead use two independent offsets,
scaled by element size (or by the ST64 stride), while DS crossbar operations
reuse the bits as a pattern. A generic control record may retain the raw bytes;
each opcode family must reconstruct its own semantic operand.

Astro supplied the discriminating nonzero-high-byte case. Its clustered-list
builder initializes, atomically exchanges, and reads an LDS head at `0x510`.
Using only the low byte redirected all three operations to `0x10`, copied
`1.0f` (`0x3F800000`) into a global uint head table, and made the consumer walk
an impossible node index. Static producer/consumer data flow plus exact live
bytes identified the alias; the GFX10 table defined the fix. Regression tests
must cover a nonzero high byte and generated backend address, not merely assert
that the decoder retained two raw fields.

The clean runtime differential also preserves the negative result: repairing
the address did not make Astro's postprocess visible and did not materially
change the slow pixel shader's approximately 123--140 ms 1080p cost. Bank the
general ISA correction, retract the corrupted-head explanation, and keep the
remaining black boundary open.

## Track GPU provenance separately from format availability (2026-07-31)

An address can name authoritative GPU content even when no exact-format color
image is available. Do not answer both questions with one format-keyed map.
On Prospero this is essential for DB surfaces: Sony SDK 10.00's HTILE sample
shows that a compressed depth clear may leave raw backing Z bytes unchanged,
while a texture view with compression enabled still reads the cleared depth.

Astro exposed the host-side failure at `0x513560000`. SharpEmu retained both a
native D32 depth image and an R32 image uploaded from zero-valued guest backing
bytes. The depth view resolver found D32, but CB targets alone were registered
as GPU-produced, so the old CPU upload was allowed to supersede it by writer
serial. Registering both DB read/write aliases when the draw is queued makes
the sampled descriptor defer to the resident depth lifetime without claiming
that an exact R32 color image exists. Keep these facts distinct:

- GPU provenance answers whether guest-memory upload is stale by construction;
- exact-format availability answers whether a color image can be looked up or
  presented directly;
- writer order arbitrates only among producer-valid competing lifetimes;
- metadata state determines whether raw backing bytes have semantic pixels at
  all.

The 2026-07-31 address-scoped run proves the binding changed from stale R32 to
D32 and survived 480 seconds without device loss. Its paired readback also
proves the selected D32 input and 960x540 R32 output are uniformly `1.0`.
That closes the black full-to-half transfer, but it is neutral first-use depth,
not evidence that guest scene geometry populated DB.

## Separate a symbol carrier from an executable firmware body (2026-07-31)

ELF magic and a populated symbol table do not prove that an artifact contains
machine code. The sampled early-firmware DevKit/TestKit carriers under
`games/prospero-firmware-symbols` expose names, bindings, per-version addresses,
and `st_size`, but their `.text` sections are `SHT_NOBITS` with size `0x10`.
Use them to identify and bound a function; establish behavior from a clear
firmware body for the exact version and from the matching public SDK contract.

This join resolved Astro's previously unserved
`sceVideoOutSubmitChangeBufferAttribute2` import without guessing. Its early
carrier size changes from `0xb4` to `0xb6`; the exact clear body is `0xd0` bytes
in 4.03 and `0xaa` bytes in 9.00. Both later wrappers validate the attribute
pointer before the port handle, dispatch a four-argument callback, perform
bookkeeping only after a nonnegative result, and return that result. SDK 10.00
supplies the public `0x50`-byte attribute layout and ABI. The address, size, and
internal callback slots are version-specific and must not be transplanted.

The resulting HLE route is a surface-completeness fix, not an Astro black-frame
finding: retained Astro logs contain no call to this NID. Conversely, frequent
`sceVideoOutIsFlipPending` calls are not evidence of a bad boolean return. Sony
samples consume it as a pending-count API, which is what SharpEmu returns; the
large count in slow runs reflects polling while GPU work remains pending.

## Compare decoded operands, not only the mnemonic and size (2026-07-31)

An executable disassembler differential can report green while the emulator
still reads or writes the wrong register. Instruction name and byte length
validate only the outer decode. For every memory family, also compare the
direction-specific address, data, destination, immediate, and modifier fields.

The GFX10 FLAT encoding exposed the concrete failure. Sony SDK 10.00's PSSL
compiler emitted this real pixel-shader spill load:

```
scratch_load_dword v5, v2 offset:0x0004
bytes: 04 40 30 DC 02 00 7D 05
```

Sony's `libSceShaderIsaP.dll` and LLVM agree on the instruction. The comparison
found that SharpEmu correctly used the low byte of the second DWORD as
`vaddr`, but its shared GLOBAL IR took `vdata` from bits 8..15 as the load
destination. GFX10 loads put `vdst` in bits 24..31; bits 8..15 are the
store/atomic data field. The canonical GLOBAL load
`00 90 30 DC 03 00 7D 01` therefore targets `v1`; the old IR targeted `v0`.

The old code also sign-extended `word & 0x1fff`. That mask includes bit 12,
which is the `dlc` modifier, so a zero-offset GLOBAL load with `dlc` acquired
an invented -4096-byte offset. GFX10 GLOBAL and SCRATCH use signed 12-bit
immediates independently of `dlc`; the plain FLAT fixture uses an unsigned
11-bit immediate. Do not share a mask merely because the instructions share
an outer encoding.

The fix now carries separate `vdata` and `vdst` fields, excludes `dlc` from
the immediate, and supports the Sony-compiler-proven scratch dword
load/store family on Vulkan. Scratch is represented as per-invocation Private
storage sized from Sony's graphics/compute scratch-ring registers; a missing,
non-divisible, or unsupported scalar-address contract fails translation
loudly. Golden-byte tests cover the Sony spill words and LLVM's zero-offset
DLC vector.

The standing ShaderIsa oracle now compares Sony's structured operands and
options with SharpEmu's GLOBAL/SCRATCH controls, as well as name and size. Its
fixture loader also handles LLVM's common two-line form (assembly followed by
an encoding-only comment); the old loader silently skipped most such vectors.
All 48 Sony-accepted vectors in `flat-scratch-instructions.s` now pass with 48
operand checks. After adding the Sony/LLVM-agreed 32-bit integer GLOBAL atomic
family, the first 40 `flat-global.s` vectors pass 40/40. Across the first 100,
all 66 implemented forms match and the other 34 fail decode explicitly; those
are AMD wrap-clamp increment/decrement or 64-bit/x2 atomics whose exact
Windows/Vulkan contract has not been implemented. Do not turn a larger decode
count into a semantic claim by mapping wrap-clamp operations to Vulkan's plain
increment/decrement operations.
SDK 10.00's JSON omits negative FLAT-family immediates, so the tool counts that
blind spot explicitly and retains LLVM's exact bytes as the signed-immediate
evidence. A title occurrence remains a separate causal question: retained
Astro traces contain zero FLAT/SCRATCH instructions, so this was a confirmed
general defect, not the black-frame root cause.

## Differential every accepted surface mode over identical bytes (2026-07-31)

A correct footprint and one correct format do not validate a tile-mode family.
Run the vendor's complete detiler and the emulator's production detiler over
the same deterministic tiled bytes for every mode and element size the emulator
trusts by default. Compare the complete visible linear result, not a coordinate
sample or a reimplemented reference equation.

Sony SDK 10.00 makes this a bounded Windows-host check through
`libSceAgcGpuAddress.dll`. Its public `TileMode` enum exposes tiled modes 1, 5,
9, 17, 24, and 27; raw values 4 and 8 are reserved. SharpEmu previously trusted
4 and 8, refused public PRT mode 17, and silently disagreed with Sony for all
five mode-1 element sizes, all four valid mode-24 sizes, and mode-27 at four
bytes per element. Astro's eight-byte mode-27 surface still passed, which is
why the narrower check hid the general defect.

`tools/SharpEmu.Tools.AgcGpuAddressOracle` now calls Sony's
`computeSurfaceSummary`, `computeUntiledSizeForSurface`, and `detileSurface`
exports and compares them with `GnmTiling` over identical payloads. The fixed
implementation passes all 29 valid public 2D, single-mip, single-slice,
single-sample combinations at both 257x193 and Astro-scale 960x540 dimensions.
That is a bounded proof of those combinations only. Mips, arrays, 3D/volume,
MSAA, and DCC/CMASK/FMASK/HTILE metadata remain separate contracts.

Keep the SDK DLL external and supplied at runtime. The conformance tool may
recover and exercise the ABI, but SharpEmu must not redistribute Sony binaries.

## Require two independent size answers from an executable ISA oracle (2026-07-31)

A host DLL with exported names is not yet a trustworthy oracle: its ABI and
target selector must be proved with discriminating instructions. For Sony SDK
10.00's `libSceShaderIsaP.dll`, raw disassembly takes a terminated array of
16-byte `(uint64 id, uint64 value)` options; option id 1 selects generation.
Generation 2 is the lowest value that accepts Astro's exact five-dword
`image_bvh_intersect_ray`, while 0 and 1 reject it. Ordinary instructions that
all generations accept cannot establish the selector.

The size export is a separate guard. `sceShaderIsaGetInstructionSize` takes
the first instruction's raw 64-bit value, not a pointer to its bytes. Require
its return value, the JSON instruction size, and the fixture byte count to
agree. Zero-extend four-byte instructions before forming that value; otherwise
uninitialized upper bytes make the check nondeterministic.

Run LLVM's byte-exact gfx1013 fixtures, Sony's oracle, and the production Gen5
decoder over identical bytes. A Sony-vs-LLVM name disagreement rejects the
fixture for Sony-directed changes; it is not evidence that SharpEmu is wrong.
Only Sony and LLVM agreement against SharpEmu is a decoder defect. This gate
accepted all 24 GFX10 export vectors, rejected unsupported public `ds_*_src2`
vectors rather than importing them blindly, and proved MUBUF opcodes `0x71`
and `0x72` were missing `buffer_gl0_inv`/`buffer_gl1_inv` decodes. Both are now
lowered to a Vulkan device-scope acquire barrier because Vulkan exposes memory
visibility rather than separate GL0/GL1 cache operations.

## Attribute a device loss at the operating-system boundary (2026-07-31)

`vkQueueSubmit` reports the call that first observes a lost device; it does not
identify the command that caused an asynchronous Windows GPU reset. Before
blaming the named shader, correlate the timestamp with
`C:\Windows\LiveKernelReports\WATCHDOG` and Windows Error Reporting, then
place a queue sentinel between the last substantial work and the observer.

Astro's repeated logs named the bounded `6EAC00` finalizer, but four matching
WATCHDOG dumps classify the events as `LiveKernelEvent 141` video TDRs. The
same module and pipeline-cache state succeed in adjacent runs, while the last
substantial physical work is a 32-chunk `571000` dispatch. The boundary run
retired all 32 fences, then the following empty sentinel submit observed the
reset before a new `6EAC00` command was recorded. The oldest chunk's measured
queue interval was 2,533.300 ms and Windows wrote a fifth matching WATCHDOG
dump. This proves `6EAC00` was the observer and the continuously enqueued chunk
sequence was the TDR workload.

Separate command buffers are not necessarily separate WDDM scheduling
intervals when the application queues them back-to-back. For a Windows TDR
fix, wait for each non-final chunk fence before enqueueing the next; preserve
asynchronous submission on hosts without this constraint. An empty Vulkan
device-fault address after a WDDM reset is not evidence that no GPU work
faulted.

Validate the scheduling fix with the ordinary production path, not the
boundary probe that discovered it. Astro run `20260731-185733-corpus-gate`
used committed head `830d202`, reached LOGO, TITLE, and worldmap at
61.875/225.125/309.516 seconds, and completed the 480-second window without
device loss or a new WATCHDOG dump. Its manifest had capture disabled, so the
run validates execution progress and TDR removal only; it does not establish
that the final guest image is nonblack.

## Route the title's exact NID, not only the newest named API (2026-07-31)

Implementing a vendor operation under its current SDK name does not prove an
older title reaches it. Firmware can retain a pre-versioned NID whose friendly
name is absent from public catalogs. Compare complete bodies and call
arguments, then route every proved ABI alias to one semantic implementation.

Astro exposed this failure mode. Sony SDK 10.00 names
`sceAgcDcbContextStateOp` as `HabmgqPwPw0`, and SharpEmu implemented that NID.
Astro instead imports `qj7QZpgr9Uw`. Its complete PS5 4.03 body has the same
operation switch and packet values as the named PS5 9.00 export, while
SharpEmu reduced it to a one-DWORD no-op. A live trace subsequently proved
balanced push-clear/pop use through the older NID.

For an ABI fix, require all four checks:

1. symbolise each firmware body by `st_size`, never nearest export;
2. compare control flow, packet values, and argument domain;
3. inspect the title's actual import relocation and call-site arguments; and
4. retain a cheap path-specific runtime trace proving the rebuilt executable
   exercised the intended alias.

Static equivalence proves routing correctness, not title causality. Astro's
balanced context-state operations now execute, but paired PrintWindow captures
remain guest-black, so the fix must not be promoted to the black-frame root
cause.

## Bind a resource interval to the game lifetime before calling it a defect (2026-07-31)

A writerless black resource is not automatically a missing producer. First
place the exact GPU interval between retained game-state markers and prove its
output is expected to reach the displayed frame. Transition, preload, history
reset, and discarded-frame work may intentionally read an uninitialized
history and still be correct.

Astro provides the concrete correction. Its black `0x537060000` history and
`cs=0x50068FA00` dispatch occur after `Level has started: ps_logo` but before
`title_controller_ship` loads. The title's executable also declares dedicated
full- and low-resolution TaaLite history pairs and deliberately selects the
low pair for its 1920x1080 mode. The measured absence of a writer and Sony's
proof that the preceding mode-6 packet is only `kDccDecompress` remain valid;
the inference that this was a broken title-frame handoff does not.

Before assigning a producer/consumer defect, require four joins:

1. the last real writer and its exact resource identity;
2. the first consumer and its exact sampled identity;
3. the semantics and queue order of every transition between them; and
4. the game-level lifetime showing that this output must survive to a retained
   or displayed frame.

If the fourth join is absent, report the resource observation without turning
it into a renderer root cause. Do not fabricate a copy to initialize history,
and do not use document preload as proof that a level transition was requested.

## Verify the exact runtime artifact before every long boot (2026-07-31)

This rule is now enforced, not merely advisory. `corpus_gate.py` refuses to
boot when any selected Release executable/library payload is older than a
runtime source or project input, and it refuses to auto-build a missing binary
inside the timed boot path. `game-test.py` records a SHA-256 fingerprint over the complete
`SharpEmu*` runtime payload because the apphost executable can remain
byte-identical while a library DLL changes.

Before the required Release build, remove only compiled Release outputs:
`artifacts/bin/Release` and each `artifacts/obj/*/Release` directory. Preserve
`artifacts/game-runs`, image dumps, WAVs, and every other evidence directory.
Then run the solution-level no-incremental build and require zero warnings.
This prevents a locked or stale DLL from surviving into a run while avoiding
the much worse mistake of deleting retained measurements.

A passing focused test build does not prove that the executable selected by a
boot harness contains the change. Multi-target .NET trees can keep separate
framework-only and RID-specific outputs. SharpEmu's focused
`SharpEmu.Libs.Tests` build updated `artifacts/bin/Release/net10.0`, while
`corpus_gate.py` launched `artifacts/bin/Release/net10.0/win-x64/SharpEmu.exe`.
The resulting six-minute boot was valid, but it was a baseline run rather than
a test of the pending patch.

Before a long runtime probe:

1. read the harness manifest or source to identify the exact executable and
   dependency directory it will launch;
2. run the repository's required solution/RID build, not merely a focused
   test-project build;
3. check the timestamp and content hash of the modified dependency in the
   launch directory; and
4. include a cheap, change-specific trace that proves the new path executed.

Do not call a run positive or negative until that wiring check passes. Probe
exhaustion means only that the selected window produced no result. It is not a
stall, crash, or failed boot; wait for the real milestone or an independently
proved lack of forward progress.

For producer/consumer joins, store the producer's immutable per-work sequence.
A mutable renderer-global sequence can advance while an older queued resource
retires and falsely attach the producer to unrelated later work. Astro's live
join uses the sequence captured on the translated draw resources, requires a
strictly later consumer in the same process, and reports both sequences.

## Decode vendor register descriptors before indexing emulated state (2026-07-31)

A register descriptor stored in an indirect table is not necessarily a raw
hardware register offset. Preserve the encoded word until its list type and
selector are known, test any sentinel before normalization, and only then
index emulated register state.

Sony's `CxPsShaderUsage` is the concrete case. Firmware emits
`0x10000000 + slot` for its 32 pixel-input entries; draw translation consumes
physical context offsets `0x191 + slot`. Storing the encoded value directly
does not fail loudly—it merely makes every later lookup miss. Conversely,
changing an HLE producer to physical offsets hides the parser defect for HLE
tables while native title tables remain broken.

Verify both halves independently:

1. symbolise the exact firmware export by `st_size` and inspect the descriptor
   bytes it writes;
2. confirm the SDK structure's slot/count and value fields;
3. make the HLE producer emit the native representation;
4. decode at the indirect-packet consumer, where the register-list kind is
   known; and
5. test encoded slots, ordinary offsets, selector normalization, and the raw
   all-ones sentinel.

Do not infer a register's physical offset from its final draw-state lookup.
That is the consumer representation, not proof of the guest ABI.

## Replay the discriminating shader math before blaming a zero intermediate (2026-07-31)

A zero render target can be the correct output of an edge, visibility, mask,
or culling pass even when its source contains real color. Once exact source
pixels and the native shader are retained, execute the smallest
output-deciding predicate offline before adding another multi-minute boot
probe. Count the lanes that should write; do not substitute the source's
nonblack count for that result.

Astro's SMAA edge shader is the bounded example. Sony SDK 10.00's
`libSceShaderIsaP.dll` and SharpEmu agree on all 98 instructions, and the
paired primitive shader supplies correct fullscreen UV offsets. Its RGBA16F
source has 995,072 RGB-nonblack pixels, but the maximum left/up RGB difference
is `0.0491943` against a strict `0.1` threshold. No pixel can enter the edge
export path, so the all-zero R8G8 target is correct. Treating it as a broken
writer would move the investigation away from the required-color chain.

The rule is narrow: reproduce the exact samples, constants, comparisons, and
lane/export predicate. If the expected writer count is zero, mark the pass as
a proved dead end and move to a downstream stage that must carry color. If it
is nonzero, the same replay supplies a concrete expected result for the live
pair capture.

## Key long-running probes by work sequence, not occurrence (2026-07-31)

An occurrence number is local to one run. Startup timing, asynchronous shader
compilation, and workload scheduling can move the same occurrence between the
logo and title lifetimes. Astro demonstrated the failure directly: occurrence
130 of one compute shader was adjacent to the wanted title work in one run,
while occurrence 131 in another run happened before the logo. Both probes were
wired correctly; the selector was not stable.

For a multi-minute producer/consumer probe:

1. establish the producer's address and guest work sequence in a retained
   trace;
2. select the first dispatch whose program address and bound resource both
   match the intended consumer at or after that sequence;
3. claim the selection once atomically, because renderer work can execute on
   more than one host thread;
4. retire the selected readback before later work can reuse the host image or
   lose the Vulkan device; and
5. report an explicit `stage=none` with resource counts when no recognized
   host image is bound.

This does not make work sequence a semantic identity. The strongest join is
still the same live image/resource identity on the producer and consumer. Work
sequence is a stable temporal fence; shader address plus occurrence alone is
not.

## Preserve metadata semantics when the host stores expanded images (2026-07-30)

A hardware metadata write can change the value a shader reads without changing
one byte of the pixel allocation. If the host backend expands a compressed
guest surface into an ordinary image, it must mirror that metadata operation
in queue order. Reading or uploading only the data allocation silently turns
valid compressed content into stale host pixels.

Astro supplies the bounded example. Sony SDK 10.00 defines DCC byte `0x40` as
constant `RGBA={0,0,0,1}` and implements a full DCC clear by repeating that
byte through its uint4 compute-fill kernel. The live command fills 24 KiB at
the exact metadata address carried by the following texture descriptor, while
SharpEmu's expanded RGBA16F image remains byte zero. Those two representations
are not equivalent: the guest shader deliberately distinguishes alpha one
from alpha zero.

For a metadata-to-expanded-image bridge:

1. prove the metadata operation from the vendor implementation, including the
   exact code value and range;
2. retain both metadata identities: the current render-target metadata address
   with the host image and the sampled texture descriptor's metadata address
   across the backend seam; a reused pixel address can name different metadata
   allocations at different dimensions;
3. recognize the producer by complete semantics and runtime operands, never by
   a title or shader address;
4. apply the corresponding host-image operation at the same queue position,
   or at the first matching target bind when the expanded host image did not
   yet exist;
5. refuse partial ranges and codes whose pixel meaning is not proved; and
6. never replay a delayed clear after a newer image writer; track the writer
   frontier and refuse the late operation instead; and
7. verify the exact metadata-address join at runtime before calling it causal.

Recognizer tests must begin at the same representation runtime receives. For
an ISA-pattern recognizer, feed the captured machine words through the
production decoder and then recognize the resulting program. A hand-built IR
can prove the recognizer's internal logic while silently assuming the very
opcode, operand, or control decode that needs verification. Astro's DCC-fill
test uses the exact nine dwords from `cs=0xC08E6AA00` through
`Gen5ShaderTranslator.TryDecodeProgram`.

Do not infer current binding state from a deduplicated warning. A log keyed by
data-surface address can preserve the first metadata address it saw while
later bindings change. Such a line proves that compression exists, not that
its detail describes the selected draw.

Astro demonstrates the concrete alias: the same pixel address carries
`dword6/dword7=007B0000/00057052` at 960x540 (DCC
`0x570520000`) and `607B0000/00057052` at 1920x1080 (DCC
`0x570526000`). Preserve the raw descriptor and decode the low metadata byte;
pixel address alone cannot perform this join.

Queue order includes both metadata and pixel writers. In Astro's captured
interval, each constant DCC fill precedes 44 draws to the associated pixel
surface. A host backend that waits until sampling to expand the clear would
erase those draws. Materialize at the producer or before the first subsequent
target writer, and use a per-image writer serial to prove that a sample-time
fallback is still safe.

Do not record the right operation under the wrong render-pass load policy.
When a deferred metadata clear initializes a newly created expanded image,
that pending operation must participate in render-pass selection; otherwise an
initial clear load-op can overwrite it before the first fragment executes.
Likewise, close any batched render pass before recording the image transition,
transfer clear, or timestamp reset that establishes this boundary.

## A host initialization is not a guest writer (2026-07-30)

Resource-lifetime serials must describe guest-visible writes. Creating a host
image, choosing a neutral first-use clear, or repairing a stale descriptor is
emulator bookkeeping; it is not evidence that the guest produced that
content. Promoting such bookkeeping in alias arbitration can activate a
downstream path with fabricated state and make a plausible local fix look
causal.

Astro provides the concrete failure. Its first use of depth `0x513560000`
expands a stale 1x1 DB descriptor to 1920x1080 and initializes the new Vulkan
depth attachment to neutral 1.0. Making that implicit clear outrank an older
same-address color upload passed every offline test, but the next supported
boot device-lost in the downstream clustered-list/GDS chain. The patch was
reverted. The valid checkpoint is the guest command that produces the depth
or its metadata, followed by bounded producer outputs—not the host resource
that happens to be newer.

When two host representations alias one guest address:

1. identify which guest commands wrote each representation;
2. distinguish explicit guest clears/writes from host-only initialization;
3. capture the first consumer and its bounded outputs; and
4. reject a lifetime rule if it merely substitutes deterministic host state
   for an unmeasured guest state.

## Execute the vendor oracle before porting a family table (2026-07-30)

An incomplete document capture is not an ISA oracle. The local Sony shader
instruction reference contains about 47 of roughly 547 pages and has no
instruction bodies for vector memory, LDS/GDS, image, FLAT, or export.
Absence from that text proves only that the captured chapter is absent.

Prospero SDK 10.00 supplies two executable oracles for these gaps:

- `libSceShaderIsaP.dll` disassembles and sizes raw shader instructions. Its
  raw-disassembly options are a terminated sequence of 16-byte
  `(id, value)` pairs; option id 1 selects the generation. A null option list
  selects generation zero and incorrectly labels valid Prospero encodings such
  as the observed 64-bit compare and BVH instruction as invalid. Record the
  exact generation option with every comparison.
- `libSceAgcGpuAddress.dll` computes the surface summary and the byte offset of
  an exact texel. Compare the complete coordinate domain, not only the padded
  dimensions or allocation size.

The second rule found a real SharpEmu defect. For Astro's mode-27, 960x540,
8-byte RGBA16F surface, Sony and SharpEmu agreed on a 128x64 block, 1024x576
padded extent, and `0x480000`-byte allocation. Nevertheless, the old
within-block equation disagreed at 450,304 of 518,400 visible texels; the first
disagreement was `(8,0)`, Sony offset `0x2100` versus SharpEmu `0x2000`.
Replacing the exact eight-byte R_X equation reduced the Astro comparison to
zero mismatches, but a later complete-detile gate caught a remaining four-byte
R_X transcription error. Matching a footprint, or even one format in a mode
family, is therefore not evidence that every tiled surface is read correctly.

When an executable vendor oracle exists, run it and the emulator over identical
bytes or coordinates. A difference is an implementation defect; agreement is a
bounded proof. Use LLVM's gfx1013 tables and AMD's RDNA1 guide only for families
missing from the captured Sony text, and never let a generic family table
overrule an observed Sony result.

## Map the outer executable container, not only its embedded ELF (2026-07-30)

Astro's 1.007 executable is an fSELF containing an ELF. The embedded ELF
program header says where a segment belongs in its decoded image; it does not
give the segment's physical offset in the outer SELF file. Treating ELF
`p_offset=0x4000` as a physical file mapping made valid RVAs decode as
unrelated instructions and briefly caused a false retraction of a correctly
extracted state machine.

For a SELF/fSELF, bind static claims to all three coordinate systems:

1. parse the outer SELF segment entries and identify the entry backing each
   ELF program header;
2. record the physical SELF offset, ELF virtual address, and derived RVA
   mapping explicitly; and
3. verify decisive instructions and referenced strings at their physical
   offsets in the original hashed file.

For Astro 1.007 SHA-256
`3B5100797FE83663E18A650F82F901D066ACF1029AE80CFB1BE638FE0839DEBD`,
SELF entry 1 maps PHDR0 RVA zero to physical `0x3B1F0`, while entry 3 maps
PHDR1 VA `0x74F0000` to physical `0x7532DF0`. Those mappings recover the
expected classifier and `StartLevel` evidence. A disassembly is only as exact
as the container mapping used to select its bytes.

## Record the environment the emulator received, not only requested overrides (2026-07-30)

Retained-run provenance was incomplete. `game-test.py` copied the invoking
process environment into the emulator, then recorded only `--env` and
manifest overrides in `run.json`. An inherited shader marker, forced texture,
capture address, or control-flow experiment could therefore change a run
without appearing in its manifest. Run `20260730-122812-corpus-gate` is the
concrete warning: its log contains `vk.texture_force_white_binding`, while
its manifest lists no force-white control.

The harness now records `runtime.environment_effective`: the sorted, effective
`SHARPEMU_*` namespace after inherited values, explicit unsets, overrides, and
harness-derived `SHARPEMU_APP0_DIR`/FFmpeg values are applied. It deliberately
does not copy unrelated host variables. `environment_overrides` and
`environment_unset` remain separately recorded so the source of an intentional
change is visible.

For older runs without `environment_effective`, a log marker can prove that a
control was enabled, but silence cannot prove that every unlogged control was
disabled. Treat those runs as valid only for claims independent of the missing
environment provenance. Also compare the recorded Git head and dirty-file
list with the commit under evaluation: a retained run cannot validate a fix
that landed later, even if its capture happened on the same day.

## Name the predicate before changing a wait primitive (2026-07-30)

Astro's worldmap document loads but the level never starts. The retained
condition trace is not a deadlock: `OdxAsyncLoader` repeatedly exits and
re-enters the same wait while broadcasts monotonically advance the signal
epoch. The paired rwlock also grants and releases every measured blocked
writer. This does not make the Odx condition causal. The same loader waits
earlier and resumes when work is queued, so its final wait can be an idle work
queue downstream of a missing transition request. Changing lock fairness,
forcing a wake, or returning success cannot repair an unidentified guest
decision.

Condition diagnostics must include the stable guest thread identity and the
guest return RIP for each wait, signal, and broadcast. The return PC names the
code that examines the predicate after the import returns; the condition's
heap address does not, and it moves between runs. Disassemble that callsite
and first classify the predicate as work availability, completion, readiness,
or shutdown. Only then trace its producer—or leave an ordinary idle queue
alone and move upstream to the owner of the missing transition.

## A unique embedded shader body is not a runtime shader identity (2026-07-30)

An eboot-wide byte scan found exactly one 53-instruction pixel program with
image operations at PCs `0x20` and `0x28` plus an
`SBufferLoadDwordx16` at PC `0x68`. The archive records prove that it is a real
serialized pixel shader at file offset `0xE15C418`, paired with the export
shader immediately before it. Its executable-byte SHA-256 is
`04AACB30E561699B808045239FF620A208F151A1A7587ECD730F554345804868`.
Those facts do not prove that the program occupied runtime address
`0x50063F800`.

The retained runtime evidence rejects that mapping. Static resource discovery
for `0x50063F800` reports one image at PC `0x20`, no PC-`0x28` image, and no
SMEM sites. Its generated SPIR-V is 23,060 bytes in one run and 23,148 bytes
in another. Compiling the embedded candidate with its actual two images and
PC-`0x68` global produces 26,564 bytes; supplying the retained one-image shape
fails at the unresolved PC-`0x28` operation. Static instruction discovery
cannot erase a real SMEM instruction, and binding discovery logs every image
site. The bodies are therefore different.

This exposed a separate implementation defect: SharpEmu cached decoded programs
by `(code address, declared size)`, while `sceAgcCreateShader` can associate a
new header with a reused shader-heap address. That cache can combine current
header metadata and user registers with stale instructions. Commit `d359578`
adds header identity and a focused regression that reproduces the stale split.
Treat the defect as fixed generically, not proof that it occurred in the
retained Astro run until the live header and bytes are captured.

For runtime shader identity, require at least one of:

1. an exact live byte dump or hash with the declared size and header address;
2. a proven loader relocation from the serialized archive record; or
3. a complete instruction/resource listing that matches the retained runtime
   program.

Uniqueness inside an executable ranks a candidate; it does not relocate it.

## Validate the guest state before calling a black checkpoint defective (2026-07-30)

A byte-exact black image is a strong boundary only after proving that the guest
state requires visible content there. Astro also shows why a
semantic-sounding log line is not enough to classify that state. Its selected
49-writer interval is ordered after
`Level has started: title_controller_ship` and `Continue: worldmap`, but no
title-level end or ProductNext transition state is observed. A fresh save root
with no ordinary slot and no pad input produces the same `Continue` line. It
may name queued/preloaded content rather than an active transition.

The exact shaders still weaken any claim that every writer is required: one
family samples deliberately black atlas edges, and another consumes inactive
particle records and emits a degenerate dummy primitive. A target binding,
nonzero write mask, nonblack whole texture, or count of draws is not proof that
a fragment should write color. But the complete interval can no longer be
dismissed as an intentional transition merely from the `Continue` log order.

For transition/loading pipelines, add a state predicate to every image
checkpoint:

1. identify the active level/phase at the exact work sequence;
2. prove the content is required in that state, preferably with a healthy
   hardware/control capture;
3. only then bisect producer, representation, shader, and presentation edges.

If the required state never occurs, investigate that readiness predicate
separately. Never force a transition image nonblack to compensate for a level
that has not started, and never infer that a transition started from a preload
label alone.

## Preserve guest writer order across split host-image aliases (2026-07-30)

A guest allocation can legitimately appear as depth, sampled texture,
storage image, or color target over its lifetime. If the host implementation
represents those views with separate Vulkan images, initialization booleans
are insufficient: both images can be initialized while only one contains the
latest guest-visible value.

Astro supplied an exact example at `0x53A500000`. A compute dispatch wrote a
nonblack 960x540 R32 host image, while a distinct depth host image existed at
the same guest address. Texture resolution unconditionally preferred the
depth object, so a completed later producer was hidden by an older
representation. The correct discriminator is ordered writes, not aspect
priority, creation time, address alone, or a title-specific exception.

Track a monotonic writer identity on every host representation and advance it
only when an operation can actually modify that representation. In
particular, a bound render target with write mask zero and a bound depth target
with depth writes disabled are not writers. When sampling an aliased guest
address, select the newest initialized compatible representation. If a
consumer requires a representation different from the newest one, materialize
it explicitly rather than silently choosing stale data.

This rule complements, rather than replaces, metadata handling. Sony's DCC
decompress and depth-flush operations define when compressed data becomes
texture-readable. Once SharpEmu holds authoritative expanded Vulkan pixels,
however, a skipped metadata packet must not cause an older alias to win and
must not be treated as a source of invented nonblack color.

## Make retained GPU work a deep, serializable checkpoint (2026-07-30)

`VulkanOffscreenGuestDraw` is already close to an offline replay fixture. It
retains translated vertex and pixel SPIR-V, texture payloads, constant/global
buffers, vertex and index buffers, render state, target/depth descriptors, and
NGG compute-replay data. For a title with a multi-minute boot such as Astro,
serializing that state once can turn dozens of shader, sampler, and raster
experiments into seconds-long offline tests.

The queued record is not yet a complete checkpoint. `ToArray()` copies the
lists but not the byte arrays held by their elements; several of those arrays
are pooled and are returned after execution. Target and depth records describe
attachments but do not snapshot the host-image contents present before the
draw. A draw may also depend on an earlier queued compute dispatch, image
metadata transition, or a different guest-address lifetime. Replaying only the
draw can therefore pass while the live failure remains an ordering, alias, or
residency bug.

A durable replay package must deep-copy and hash:

1. every shader and buffer byte range, recording logical length separately
   from pooled capacity;
2. every texture subresource and its raw guest descriptor, sampler, binding
   shader address, and binding PC;
3. color and depth attachment contents immediately before execution, plus
   their size, format, tile mode, metadata disposition, and writer identity;
4. render, draw, index, instance, subgroup, and NGG replay state; and
5. the ordered predecessor operations needed to establish GPU-written
   buffers or aliased images.

Use single-draw replay for shader/input/raster questions. Use a short ordered
sequence replay when the question concerns clears, resolves, compute
producers, metadata, or aliasing. The emulator remains necessary to capture
live order and contents; after capture, offline replay should be the default
place to bisect the checkpoint. A replay result is evidence only when its
manifest proves that the relevant pre-draw state was preserved.

## Bisect GPU pipelines with executable checkpoints before booting (2026-07-30)

A final black present contains too many edges to diagnose efficiently. Build
an ordered graph of concrete resources and shader operations, mark every edge
as proven, refuted, or unresolved, and place the next probe at the first
unresolved edge. Prefer offline evidence for static facts:

1. decode the exact shader and reconstruct its branches and def-use chain;
2. decode the exact constant buffers and register state;
3. compare the lowering with the platform ISA or an independent implementation;
4. distinguish address reuse by size, format, phase, and writer lifetime; and
5. run the emulator only for facts that require live per-invocation values,
   queue ordering, alias residency, or host-image contents.

Astro demonstrates the payoff. Offline reconstruction showed that its
PC `0x11A8..0x1278` smoothstep block is skipped by a scalar branch. A prior
marker-free capture at PC `0x1278` had therefore measured no value, not a zero
value. A same-PC marker at PC `0x1174` then proved that the preceding
depth/range fade executes and preserves nonzero material data. Separately,
matching only guest address `0x53AA00000` had conflated Sony
`kDccDecompress` on a 1920x1080 `ps_logo` lifetime with the later failing
960x540 `worldmap` lifetime. Size and phase refuted DCC as the cause without
another boot.

Every internal value capture must carry an execution marker at the same PC.
The checkpoint has three possible outcomes:

- marker absent: the PC was not reached; reconstruct or measure control flow;
- marker present, value zero: the first-zero boundary is at or before this PC;
- marker present, value nonzero: advance to the next consumer.

Use a known control run to estimate the trigger window, then terminate the
exact title process as soon as the checkpoint fires. This converts a slow boot
into one new bit of causal information instead of another ambiguous final
frame.

## Prove that an architectural gap is live before calling it causal (2026-07-30)

SharpEmu silently drops pixel exports other than configured color targets, so
Sony's MRTZ depth export is genuinely unsupported. That fact alone did not
explain Astro's clear full-resolution depth attachment. Exact dumps for all
eight retained pixel-shader families using the attachment show only MRT0/MRT1
color exports, and the traced Sony DB shader-control state accompanies
disabled depth writes.

Separate two claims: “the emulator cannot implement this legal operation” and
“this title executes that operation at the failing boundary.” The first is a
general bug; the second requires a live instruction plus matching state. Keep
the general bug queued, but do not spend the title's causal-fix budget on it
until both are present.

## Byte-count closeness can rank a suspect, but cannot classify pixels (2026-07-30)

Astro's ordered D32 snapshot contains 4,148,035 nonzero bytes. An entirely
clear 1920x1080 float-depth image filled with `1.0` (`00 00 80 3F`) would
contain 4,147,200 nonzero bytes. The difference is only 835 bytes, which makes
missing or under-covered depth rasterization a strong upstream suspect.

That arithmetic is not a pixel count. One changed float can add or remove
several nonzero bytes, and equal byte counts can describe different values.
Use byte-count proximity to choose the next probe, then classify the actual
float values or the writer's output. For Astro, the next proof is the depth
rasterization into `0x513560000` or the sampled/reconstructed depth values in
the first half-resolution material draw—not a claim that exactly 835 bytes or
pixels contain geometry.

## An ordered image pair closes one edge, not every aliased lifetime (2026-07-30)

Astro's hierarchical-depth probe now has a same-command result. In
`20260730-041226-corpus-gate`, occurrence 1719 of `cs=0x5006C6A00`
records:

```text
input-depth addr=0x513560000 size=1920x1080 format=D32Sfloat
nonzero_bytes=4148035/8294400 hash=0x7FFD6D0B0E3B8543

output-storage addr=0x53A500000 size=960x540 format=R32Sfloat
nonzero_bytes=1036909/2073600 nonblack_pixels=518400/518400
hash=0x92AA4A36482903FF
```

The output head contains repeated `0000803F`, or float `1.0`. The input copy,
dispatch, and output copy are in one command buffer, so later queue work cannot
explain the result. The first full-to-half HiZ reducer executes and does not
turn a live depth image into zero.

Occurrence 1720 is a different edge: 60x34 groups consume
`0x53A500000` and write the 480x270 level `0x53AC40000`. Do not pair its
shader address with the occurrence-1719 resource assumptions. More broadly,
the address `0x53A500000` has also appeared under other formats and lifetimes.
This measurement proves the R32F hierarchical-depth lifetime only; it does not
prove that an aliased color/G-buffer lifetime contains pixels.

The result also sharpens the DCC fork. Sony's Prospero samples say
`kDccDecompress` preserves logical color while changing representation and
implicitly eliminating fast clear/FMASK state. For SharpEmu's authoritative,
expanded host image, preserve is therefore correct; treating stale compressed
guest bytes as ordinary pixels is not. A later black half-resolution color
surface must be traced to its actual writer or host-image alias/residency
transition, not blamed on this working depth reducer.

## Match guest wave width to the Vulkan subgroup contract (2026-07-30)

Astro exposed a performance defect that looked like a bad producer. The V620
reports a default subgroup size of 64, but also reports subgroup-size control
from 32 through 64 and permits a required size for compute. Prospero compute
dispatch state marks the affected shaders as wave32. Translating those shaders
for host wave64 forced SharpEmu's two-part wave emulation through every
EXEC/ballot/shuffle-heavy control-flow path.

Commit `2e9c52d` now enables Vulkan 1.3 subgroup-size control, translates a
guest wave32 compute shader against an effective 32-lane host subgroup, carries
that contract through the compiled shader and dispatch records, includes it in
the pipeline cache key, and attaches
`VkPipelineShaderStageRequiredSubgroupSizeCreateInfo` with size 32. Guest
wave64 and hosts that cannot require compute subgroup 32 retain the previous
default-width path.

The discriminating run is
`artifacts/game-runs/astro/20260730-040135-corpus-gate/attempt-01.log`.
Before this change, the title's 8,160-group compute chain compiled
`cs=0x50057B800`, retired a submission after about 7.4 seconds, and the next
`cs=0x5005A8100` submission reported Vulkan device loss. With the required
wave32 subgroup, the same title path continued through:

```text
PLAY: [2:51] StartLevel title
GAME: Level has started: title_controller_ship
LevelDocument Loaded: worldmap [worldmap]
```

No device-loss line fired before the run was stopped after worldmap load. This
proves removal of the observed watchdog boundary, not an exact speedup: this
run did not enable per-dispatch GPU timestamps, so it does not supply a new
retirement duration for either shader.

Do not promote progress to rendered output. A same-run PrintWindow capture
with `PW_RENDERFULLCONTENT` is retained as
`printwindow-worldmap.png`. Its nominal
`nonblack_pixels=125076/1367100` comes entirely from window chrome and
SharpEmu's performance HUD; the guest region remains visibly black. The
subgroup correction fixes a real general compute defect and makes the render
boundary measurable, but it does not fix Astro's empty half-resolution
postprocess input.

The scalar warnings in `0x50057B800` and `0x5005A8100` are a separate
classification issue. Sony's SDK defines their `srt=2` metadata as a direct
two-dword Shader Resource Table entry, and the live SRT entries are genuinely
zero. Their downstream BVH resources are optional/null. Do not invent a
pointer or recover constants from a neighbouring binding to silence those
warnings. Fix diagnostic hygiene separately: a zero base remains null even
with a positive immediate offset, and a failed or zero-length read must not
become a synthetic Vulkan binding.

## Pair an internal value probe with proof that the probed PC executed (2026-07-30)

An all-zero forced export does not prove that the captured registers were
zero. Astro's first `ps=0x5002AFC00` probe copied four values after PC
`0x0D10` and exported them, but the target remained black. Repeating the probe
with an unconditional red marker written at the same PC also produced no red:
the selected occurrence's preceding `v_cmpx` made EXEC empty and
`s_cbranch_execz` bypassed the entire relative-VGPR block. Its scalar selector
would have entered the block. The first run measured no value at all.

For a control-flow-local shader probe, export both:

1. the architectural values under test; and
2. a same-PC execution marker that cannot be confused with those values.

Interpret a missing marker as "PC not reached by an output-producing
invocation," not as a zero register result. Select another occurrence or move
the probe to a proven dominating boundary.

When a checkpoint lands on a texture sample, split address/layout questions
from live-content questions. Use the platform address library to compare exact
mip offsets and tiled element offsets offline before changing a detiler. Then
run a named differential at the same draw: force the selected texture to a
known color and export both coordinates and the sampled value. A forced-color
success implicates guest content or mip choice; a failure implicates binding,
coordinates, upload, or sample lowering. Label the substitution explicitly;
it is a discriminator, not evidence that the fabricated value is correct.

Select that differential by shader address and binding PC, not by the guest
texture address alone. Resource addresses are transient and can name different
draws or lifetimes. Likewise, do not byte-fill a block-compressed payload and
describe the result as a texel color: BC payload bytes are encoded blocks, not
RGBA values. Substitute a known uncompressed image of the same logical shape,
or encode valid blocks, and record the substitution in the checkpoint.

Trace constant or repeated interpolants back into the exact guest shader before
calling them replay corruption. Astro's retained NGG output reported
`PARAM1=(5,5,0,0)`. The exact export shader shows an inline integer `5`
converted to float and exported to both X and Y, with zero in Z and W. The
constant was guest-authored. A suspicious value is a lead until its producer
has been reconstructed.

Apply the same control discipline to run duration. Astro's healthy control
does not start `title` until about 2:57 and spends a long interval decoding the
first-boot MP4. A one-minute run at `ps_logo` is not stuck merely because the
log is repetitive. Compare the current phase and elapsed time with the healthy
control before terminating it; once the control window is exceeded without
new evidence, stop the exact process tree rather than waiting blindly.

Process ownership is part of that evidence. `game-test.py` formerly ran
`taskkill /F /IM SharpEmu.exe` before every attempt and retry. A second
workflow could therefore terminate a healthy boot it did not launch; the
victim manifest recorded only an unexplained `0xFFFFFFFF` process exit, with
no emulator shutdown, fatal, device-loss, Windows crash event, or dump.
Automatic cleanup now targets the launched root PID and its descendants with
`taskkill /T /PID`. The image-name-wide operation remains available only
through the explicit `game-test.py kill` command. Do not classify a silent
process disappearance as a guest regression until cross-workflow process
ownership has been ruled out.

## Preserve numeric values and guest write masks across host shortcuts (2026-07-29)

Astro exposed two ways a diagnostic or optimization can manufacture a false
color boundary.

First, a fixed-function replacement must preserve all guest state that affects
the result. Sony's fast-clear helper writes only R/G after mode-2 clear
elimination. Replacing that draw with `vkCmdClearColorImage` cleared RGBA and
destroyed the materialized B/A clear values. A fullscreen triangle is not
automatically equivalent to a whole-image clear. Before selecting the host
shortcut, require:

1. every bound target has a full RGBA write mask;
2. blending and depth rejection are disabled;
3. culling cannot reject the primitive;
4. viewport and scissor cover the complete target;
5. the decision participates in pipeline/cache identity.

Second, floating-point image evidence must be classified numerically.
Byte-wise nonzero detection counts IEEE `-0` as colour because its sign byte
is set. Astro's RGBA16F dump consequently reported 32 nonblack pixels even
though every decoded RGB component was `+0` or `-0`. For half and single
precision, ignore the sign bit when testing zero; continue to count nonzero
subnormals, infinities, and NaNs as data. Keep alpha excluded exactly as for
integer RGBA formats. Pin both opaque black and negative-zero black with
tests, and inspect the raw dump whenever a tiny nonblack count controls the
investigation.

Finally, bind measurements to the actual runtime program and occurrence.
Stale shader addresses from a later worldmap path did not name the title
writers in a new AGC trace. The live title sequence was
`ps=0x5006CB800 -> cs=0x500690F00 -> ps=0x500650600`.
Guest-memory descriptor bytes, a first empty occurrence, and a deferred
readback cannot substitute for a same-command live host-image capture.

## Architectural state outranks an auxiliary shadow across EXEC changes (2026-07-29)

Astro's half-resolution material shader exposed a general SIMD translation
risk. `VCvtPkrtzF16F32` wrote both the architectural VGPR and a convenient
`vec2` packed-half shadow. Compressed `EXP` then found the nearest static pack
instruction and read the shadow. That is not equivalent to reading the VGPR:
the pack is EXEC-guarded, and a later `s_mov_b64 exec, s[44:45]` can reactivate
a lane whose pack write was suppressed. For that lane the old architectural
VGPR bits are the value the hardware exports, while the shadow still contains
its initializer.

The architectural fix is still required, but later runtime probes showed that
this was not the cause of Astro's broad black target: pack-time EXEC was live
and all four pre-pack material components were already zero. Do not choose a
register value from the nearest textual writer when writes are
lane-conditional. At the consuming instruction:

1. read the architectural register file;
2. reinterpret or unpack those bits according to the consumer encoding;
3. let the modelled EXEC state decide which earlier writes reached that file.

A helper representation may cache a value only if every architectural write,
including masked writes, aliases, save/restore sequences, and reactivated
lanes, keeps it coherent. Otherwise it is a diagnostic convenience, not
architectural state. The regression test must follow the emitted module's
def-use graph to the output; merely finding both variables or both loads in
SPIR-V does not prove which one feeds the export.

## Attribute an empty datum, then classify whether it should be nonempty (2026-07-29)

An exact zero can be real guest state. Astro's native-primitive shader loaded
zero from one structured record, and a fence-retirement trace identified the
only writable overlapping binding as particle compute
`cs=0x555F4F500`. The same 64-byte record was zero after every successful
producer fence, which eliminated residency, host coherency, and producer-to-
consumer ordering at that address.

That still did not make the producer wrong. The shader is a particle emitter,
and an inactive particle record may correctly be zero. The downstream shader
explicitly emits a dummy one-primitive allocation when no lane survives.
Treating that fallback as missing scene geometry would conflate an optional
draw with the independently measured full-resolution-to-half-resolution color
boundary.

Use two labels:

1. **attributed empty datum**: the writer and ordered post-write value are
   measured;
2. **incorrect empty datum**: a healthy control, guest invariant, or direct
   source/destination pair proves the datum should be nonzero.

Do not advance from the first label to the second by intuition. Likewise,
`smem_zero_filled=0` only eliminates the evaluator's unbound-load fallback; it
does not prove the compute shader's inputs represent an active object. When a
telemetry tool lacks a runtime sink for the shader stage, state the cap result
as unmeasured rather than interpreting the host-side “probe armed” line as a
runtime count.

## Check every host limit before choosing the host primitive (2026-07-29)

Astro's native primitive shader is a useful example of turning an architecture
gap into a bounded implementation instead of filling unknown registers with
plausible values.

Start with the exact shader and submitted state. In `es=0x50011FC00`, the
program itself proves that `s3[27:24]` is the wave index, `s3[15:8]` is the
input primitive count, and `v5` is the input vertex index. It constructs a
192-lane subgroup ID, performs its own input bound, allocates eight triangles
and ten vertices per survivor, and exports Sony's packed target-20 triangle
indices. Combined with the measured registers, that gives 19 inputs, at most
190 vertices and 152 triangles per subgroup without reconstructing the rest of
the native-GS launch ABI.

Only then compare host mechanisms, and check the complete limit set. A first
pass observed that 190 vertices, 152 primitives, and 192 invocations each fit
the V620's advertised mesh limits. It missed output storage. POS0 plus eleven
PARAM `vec4`s are 48 scalar components per vertex. With the reported
256-component per-vertex granularity, Vulkan charges
`256 * 48 * 4 = 49,152` bytes, which exceeds the V620's 32 KiB
`maxMeshOutputMemorySize`. The corpus run selected the direct mesh path and then
blocked at graphics pipeline creation. Counts fitting individually did not make
the module valid.

That result changes the host primitive without changing the recovered guest
contract. Compute capture followed by indexed-indirect replay uses seven
192-invocation workgroups to capture POS/PARAM exports, target-20 triangle
connectivity, and `GS_ALLOC_REQ`; runtime `m0` counts drive one indirect draw
per guest subgroup. This keeps the wide guest interface in ordinary
storage/vertex buffers instead of mesh output memory.

Probe the earliest host-visible boundary after a successful fence. The
2026-07-29 replay run is the useful example: a black presentation did not
distinguish compute, indirect replay, rasterization, or postprocess. Reading
the four replay buffers after fence success did. Allocation and indirect
records were populated, while all 64,512 vertex dwords were zero and the only
nonzero indices came from host-added subgroup rebasing. That converts “the
replay is black” into the narrower finding “the guest native-primitive
translation computes empty POS/PARAM and local connectivity.” Never label a
buffer read as retired when it came from a generic destruction path; creation
and submit failures also destroy resources before a fence can prove execution.

Keep implementation state separate from rendering evidence. Focused tests, a
passing suite, and populated indirect buffers prove transport, not the pixels.
The correct status remains **verified execution, empty guest shader output**
until a nonblack guest capture succeeds.

Continue upstream one value at a time. For Astro, decoded control flow showed
that the apparent one-triangle result was an explicit empty-output fallback.
A fenced lane probe at the compare's feeding `buffer_load_dword` then recorded
the descriptor, scalar index, lane vertex index, effective byte address, and
loaded value. All 19 lanes read zero from one exact structured-record range,
while other bytes in the same bound buffer were nonzero. This is stronger than
either “the buffer is black” or “the descriptor exists”: it names the first
empty guest datum and leaves only its producer/residency and its scalar index
as competing causes. Do not force the fallback count or export values; doing so
would erase the boundary the measurement just recovered.

## Sony SDK ground truth beats a plausible AMD table (2026-07-29)

The Astro half-resolution investigation produced four reusable rules.

**Decode the platform contract, not the marketing generation.** Prospero SDK
10's `CxVgtShaderStagesEn` contradicts the GFX10.3 table previously copied into
the project. Astro's runtime word `0x00002030` means an enabled wave64 geometry
stage under Sony's definition. Passing no width had made SharpEmu silently run
the shader as one lane; assuming the usual non-pixel wave32 default would also
have been wrong. Commit `6b89866` now carries the decoded Sony width through
the backend and cache key. When a public Sony field is absent, keep the value
unknown rather than filling it from a neighbouring AMD architecture.

**A compression operation is representation-dependent.** Sony defines
`CB_COLOR_CONTROL.MODE=0x60` as DCC decompression plus implicit fast-clear
elimination and FMASK decompression. Its samples prove that this is required
when compressed guest bytes/metadata are authoritative. SharpEmu, however,
keeps GPU-rendered targets as expanded Vulkan images. At the measured
`0x53AA00000` boundary the exact initialized GPU-authored image survives, so
preserving it is correct and decoding stale guest bytes would be a regression.
The same opcode may therefore require:

1. preserve, for an initialized expanded host image;
2. decode/materialize, for guest-memory-backed DCC;
3. a loud unsupported result when neither representation is available.

The opcode name alone cannot decide which branch the current resource needs.

**Removing one known defect does not validate the frame.** After exact wave64
was enabled, the one-lane warning disappeared and the title still produced
`nonblack_pixels=0/2025600`, RGB all zero and alpha all 255. That negative
result is useful: it leaves the independent native-primitive defect exposed.
Sony's samples define target 20 as three 10-bit connectivity indices; Astro's
shader exports it from `v40`, amplifies 19 inputs to as many as 190 outputs,
and changes points to triangles. SharpEmu drops target 20 and submits the input
points. Until a mesh-style/compute-prepass backend exists, the renderer must
report this as unsupported and must not describe the plain-vertex result as a
valid pass-through rendering.

**Classify the surface before calling zero a color boundary.** Runtime format,
opcode flow, DB state, and Sony's sample all agree that
`cs=0x5006C6A00` performs a hierarchical-depth max reduction from
`0x513560000` (1920x1080 R32F depth) to `0x53A500000` (960x540 R32F).
The same numeric address is reused elsewhere as a color target with a
different format and tile mode; those lifetimes are not interchangeable.

The next discriminating depth measurement is not another corpus count.
GPU-stage depth-aspect and R32 snapshots are needed immediately before and
after the selected `cs=0x5006C6A00` occurrence, in the same ordered command
stream. The current compute-image probe records the input copy, dispatch, and
output copy in one command buffer, so the snapshots are ordered around that
dispatch. The earlier all-zero and all-one captures selected different boot
occurrences; they are not competing measurements of one dispatch. Use the
occurrence/work-sequence logger to select the title/worldmap occurrence before
interpreting another pair. Guest-byte probes still cannot settle a live
Vulkan-image question.

**Separate a verified seam from a verified rendering path.** Commit `0c885d5`
restores and tests the old NGG POS/PARAM compute-capture compiler variant, and a
V620 runtime probe confirms that `VK_EXT_mesh_shader` can be enabled without
losing Astro's LOGO, TITLE, or worldmap-loaded milestones. Neither fact proves
that Astro's amplified geometry renders: the capture compiler intentionally
does not interpret target 20, and the host seam has no AGC caller yet. Record
such work as foundation until a trace proves selection, output counts and
connectivity are guest-derived, and a captured guest image changes.

## THE BLACK FRAME: THE INSTRUMENT WAS BLIND, AND COULD HAVE LIED (2026-07-29)

Two findings that matter more than the frame itself, plus a correction to my own
framing of it.

**1. "275 draws per frame" is not 275 draws.** `PerfOverlay.RecordDraw()` is
incremented at the ENTRY of three different executors, before any validation and
before any Vulkan call: `ExecuteComputeDispatch` (:11046, a compute dispatch),
`ExecuteOffscreenDraw` (:11937), and `ExecuteOffscreenColorClear` (:12632, a
colour clear). Each then early-returns on skip lists and format gates. So the
number counts guest work items DEQUEUED, mixing dispatches and clears, and counts
them before every early return. I quoted it repeatedly as evidence the guest
submits a full scene. It is not that. Fourth time this week a count was treated
as a finding.

**2. The content instrument was structurally unable to measure the presented
surface, and could have reported a false success.** Both defects are in
`VulkanVideoPresenter.cs`:

- `GetReadbackBytesPerPixel` had no entry for `R8G8B8A8Srgb`, `B8G8R8A8Unorm`,
  `B8G8R8A8Srgb`, `R16G16Unorm` or `R16G16B16A16Unorm`. Our own log prints the
  swapchain as `B8G8R8A8Unorm`, and `TryDecodeRenderTargetFormat` maps the PS5
  display-buffer encoding (data_format 10, number_type 9) to `R8G8B8A8Srgb`. With
  bpp 0 the readback prints `readback=unsupported` and returns.
- `CountNonblackPixels` masks alpha for `R16G16B16A16*` but fell through to a
  default that INCLUDES alpha for `R32G32B32A32*`. An opaque-black HDR float
  target would report `nonblack_pixels = width * height`: a false "we have
  picture" from the single number this whole investigation rests on. The adjacent
  16-bit case getting it right is the differential proof that this is an
  omission, not a design choice.

**3. Correction to my own reasoning.** I claimed alpha=255 with RGB=0 argues
against "nothing rendered" and for something that wrote alpha and zeroed colour.
**That is not safe as stated:** `vkCmdBlitImage` substitutes 1.0 for a missing
alpha component when the source format has none, and the presented source's
format is not printed anywhere today. The inference only holds if the source
carries alpha, which is unmeasured.

**What IS established.** The black is inherited, not created by the copy or by a
later overwrite: the `vk.swapchain_image` line is emitted only from a path set
inside `RecordGuestImageBlit`, so a blit provably happened; that path is a single
`CmdBlitImage` over the full extent with no pipeline, no shader, no blend and no
write mask; and the readback is recorded before the overlay blit, so the dump is
pure guest content. The presenter's own swapchain mask is hard-wired RGBA and
guest colour attachments are `StoreOp.Store` unconditionally. Copy, swapchain,
mask and store-op are all exonerated with file:line.

**4. The strongest existing account is two-thirds unfixed.**
`docs/astrobot-bringup.md:241-264` records a firmware-cross-validated root cause:
the tonemap pixel shader's constant buffers read zero, from three coupled causes.
Checked against master: GFX10 scalar operand 125 decoding as architectural NULL
**is fixed**; `SHARPEMU_RECOVER_UNBOUND_SMEM` and `SHARPEMU_ASTRO_TONEMAP_FIX`
have **zero hits under src/** and exist only in docs; the 1x1 auto-exposure
writeback has only the `SHARPEMU_FORCE_EXPOSURE` stopgap, unset in every measured
run. Both live causes are in `Gen5ShaderScalarEvaluator.cs`.

**Next concrete action:** the instrument fixes are real and are NOT landed. The
two workflows produced incompatible copies of `VulkanVideoPresenter.cs` and
hand-merging them on a thin context would have risked exactly the unverified
output this framework exists to prevent, so master was left at its green commit.
Recover them from `.claude/worktrees/wf_2c3d2ffe-855-1`, land the format table and
the alpha mask first since they are self-contained, then re-run the frame dump:
until the readback can measure the presented surface, no statement about where
the black begins can be closed by measurement.
## THE LLE PATH BREAKS AT ONE UNIMPLEMENTED IOCTL: 0xC0488131 (2026-07-29)

Complete causal chain for "submits guest images and presents nothing", and it is
a single cause with three visible consequences.

**The break.** Under LLE the firmware submits its command buffer through
`/dev/gc` ioctl **`0xC0488131`** (INOUT, 72 bytes, group 0x81, num 0x31). We
refuse it. Log line 2374, then 19 `ioctl` failures at `libSceAgcDriver+0x9FD8`,
after which the engine stops asking.

**Why that removes everything.** Every entry point to our PM4 parser
`ParseSubmittedDcb` is an HLE export (`AgcExports.cs:3607`, `:3848`, `:6341`,
`:13857`). Under LLE all of them are bound to firmware bodies, so **our parser
never sees a single dword of the guest command stream**. That is also why the
control never emits `vk.submit_guest_image` and the stuck run never emits
`vk.flip_wait_order`: they are two different flip routes. The control's DCB
reaches our parser, hits our private `IT_NOP/RFlip` sub-packet and takes the
ordered path. The LLE one bypasses our GPU entirely.

**`sceVideoOutSubmitEopFlip` IS called and DOES succeed**, proven three ways:
firmware disassembly (`sceAgcDriverSetFlip` 0x6e21 `call 0xa880` with
`r8 = rbp-0x48`), PLT/JMPREL resolution (thunk 0xa880 pushes index 0x1e,
JMPREL[30] = `j8xl+92A0q4`), and differentially (the control produces zero
`vk.submit_guest_image`, so the game never calls SubmitFlip itself). Its negative
return would hit a firmware printf that demonstrably reaches our log, and no such
line appears.

**Where the frame actually dies.** `TrySubmitGuestImage` succeeds because
`_availableGuestImages` is populated by `sceVideoOutRegisterBuffers`, which is
registration, not content. The presentation is queued, dequeued, and then dropped
at `VulkanVideoPresenter.cs:14299-14318` because `presentedGuestImage is null`:
`_guestImages` is only ever populated by GPU work that, under LLE, never happens.
**And the drop was silent**, its diagnostic gated behind a
default-off trace flag. That silence is exactly why this looked like "never
flipped" rather than "flipped into nothing". Now reported unconditionally
(`0 warnings, 1978 tests`).

**The ASSUMED token is exonerated, with ground truth.** Disassembling
0x6e5d-0x6f90: the driver tests `(token & 0x0BFFC000) == 0x08000000`, and our
token always has bit 27 clear so the branch is never taken, which is the default
the code comment claimed. Crucially **both arms rejoin the same straight-line
tail** before the `IT_RELEASE_MEM` write at 0x6f18, so the token selects five bits
of an earlier packet and cannot suppress the flip. Now pinned by a test against
the firmware bytes rather than left as a comment.

**The label write is the same cause again.** `ApplySubmittedReleaseMem`
(`AgcExports.cs:4113`) is correct and would handle the exact 0xC0064900 packet the
firmware emits. It is simply unreachable, because it lives inside
`ParseSubmittedDcb`. So the flip label stays zero and any poll on it waits
forever.

**This is the LLE ceiling, stated precisely.** Implementing `0xC0488131` means
emulating the Oberon ring-submission interface: taking a raw guest DCB from an
ioctl payload and executing it. That is the "hardware contract, not API contract"
boundary `docs/what-the-firmware-taught-us.md` describes, and it is a strictly
larger surface than HLE.

**So the goal change was right.** The non-black frame is reachable on the HLE
path, which presents; the LLE path cannot present at all without a ring
submission implementation. LLE keeps its value as an auditor.
## THE CHAIN CLOSES: NOTHING IS BLOCKED, NOTHING PRESENTS (2026-07-29)

The allocation stop is explained, my trylock lead was wrong, and the cause is in
the presentation path.

**My "scePthreadMutexTrylock 5 -> 35" was a sampling artifact.** Whole-run it is
**5 in both**, and binned by 10M dispatches the two runs are statistically
identical. The unsampled evidence inverts the claim outright: trylock returning
`ORBIS_GEN2_ERROR_BUSY` is **control 26, stuck 0**. The stuck run never observes a
contended trylock. There is no spinner, no contended mutex, no lock held by a
blocked thread. Contention exists only in the HEALTHY run, where it is normal.

**Verdict: never asked, not blocked.** Three independent strands:

1. **The thread roster stops dead.** Both runs are identical thread-for-thread
   through creation index 56. The control then creates 11 more Havok workers,
   `ProductNextLoad_ATQT` (literally the load thread) and `LevelTask_ATQT`. The
   stuck run creates nothing, ever, across 243,000 more lines.
2. **Signal epochs are frozen.** Same cond, same mutex, same thread role:
   WebApiJobWorker epoch 6 -> 622 in the control against **8, frozen**;
   Physics/SoundUpdate/Sndz 1096 -> 1238 against **10**. And the warning is
   emitted once per wait call, so a repeat proves the previous wait RETURNED.
   In the stuck run every wait appears exactly once, between lines 4652 and 5931,
   and never again. Those threads are still inside their FIRST wait.
3. **No thread is near the allocator.** `mspace.stats` shows a healthy allocator
   in both runs with peak advancing identically. Every stalled thread is a
   consumer parked on its own work queue. The allocator is idle and correct.
   Nothing ever posts the work.

**And the chain terminates here: the stuck run never presents a guest frame.**

| | stuck | control |
|---|---|---|
| `presented splash` | L2411 | L1754 |
| `presented first frame` | **never** | L3548 |
| `presented guest frame` | **never** | L3549 |
| `vk.flip_wait_order` | **never** | L3830 |
| `vk.submit_guest_image 3840x2160` | L3305, L3328 | never |

The control presents its first guest frame at L3548 and the second Havok batch
and `Armadillo Load` follow immediately after. **The stuck run submits guest
images and then presents nothing**, and the engine, waiting on presentation to
continue, never posts the next load. Workers idle, allocation stops, and the
whole downstream picture follows from one missing flip.

Note the stuck run is the only one that produces `vk.submit_guest_image` at all,
which is why this was mistaken earlier for progress the HLE path never made. It
is the opposite: a submit with no corresponding present.

**Also worth recording as a method result:** the pthread layer was audited and is
correct. Three separate items have now ended by ELIMINATING a candidate rather
than fixing one, and each elimination was worth more than a speculative fix.

**Next concrete action:** find why a submitted guest image never becomes a
present under LLE. `vk.submit_guest_image` fires and `vk.flip_wait_order` never
does, so the break is between submission and flip ordering. Our own
`sceVideoOutSubmitEopFlip` (implemented this session from the firmware call site)
is on that path and is the first thing to check: it was never exercised before
today because the LLE chain could not reach a flip.
## THE DEVICE IOCTL NEVER SET THE GUEST RETURN REGISTER (2026-07-29)

`0 warnings, 1966 tests`. **`checkSuspend failure` is gone: 5 -> 0.**

Every exit from `KernelIoctlCore` goes through `Ok` or `Fault`, which write RAX.
The device-node path added for /dev/gc did `return deviceResult` and never touched
it, so the guest read a stale register. `libSceAgcDriver`'s suspend-point wrapper
does `call ioctl; test eax, eax; jne` at vaddr 0x9CCF, so a leftover non-zero RAX
made a **successful** ioctl look like a failure. The wrapper then returned its
boolean as 0 and `sce_agc_internal_suspend_point_submit_final` printed
`checkSuspend failure = 0x00000000` five times a boot.

That is why answering request 0xC0108139 did not help on its own: the reply was
right and the return path was wrong. Success now returns through `Ok`; failure
sets RAX to -1 and keeps the encoded errno as the export result, which is the
ioctl convention the guest expects.

**This is a general defect, not an Agc one.** Every /dev/gc ioctl we answer was
returning a stale RAX, so any firmware caller that branched on the result was
being told the opposite of the truth. It went unnoticed because until the
suspend point there was no caller that both succeeded and checked.

**Still no milestone.** 246,860 log lines, `GAME: Resident Load end 0.04s`, no
LOGO. Removing this failure did not change what the engine reaches, so the
suspend point was not the thing gating progress either. That is now three
candidate blockers eliminated by measurement rather than argument: the audio
retry, the expf share, and the suspend-point check.

**Next concrete action:** the remaining measured difference from the HLE control
is that allocation stops entirely after ~10M dispatches (`sceLibcMspaceMalloc`
11 -> 0, `free` 5 -> 0, `strchr` 16 -> 0) while compute continues and
`scePthreadMutexTrylock` goes 5 -> 35. Nothing has yet connected that to a cause.
Attack it directly: find which thread is spinning on trylock and what lock it
wants, using the per-thread state dump the runtime already emits, and establish
whether the allocator itself is blocked or simply never asked.
## THE LATCH CLEARED, THE IOCTL IS ANSWERED, AND checkSuspend STILL FAILS (2026-07-29)

`0 warnings, 1963 tests`. Two solid results and one open question.

**1. The init latch cleared.** Confirmed three ways in the stuck log: zero
occurrences of `0x8A6C002F` (the value `sceAgcSuspendPoint` returns when the
latch is set, loaded at libSceAgc 0x8557); the gated path provably executed,
since `0x8563` falls through only on latch == 0 into `call 0x12060` which resolves
through JMPREL entry 26 to `sceAgcDriverSuspendPointSubmit`, and the driver
messages printed from inside that call exist; and `[AgcDriver] Info - Initialized`
is present with no libSceAgc error of any kind. **The image base move (8afa378)
did what it was traced to do.**

**2. `checkSuspend` was a refused ioctl, and it is now answered.**
`sce_agc_internal_suspend_point_submit_final` (libSceAgcDriver 0x1A00) calls the
wrapper at 0x9C90, which issues request **0xC0108139** and returns a boolean
initialised to 0 at 0x9CB9 and set to 1 only at 0x9CDE. The caller compares
against literal 1 at 0x1ACE. So the printed `0x00000000` IS the failing value, not
a formatted reply field. Attribution was 1:1: exactly five refusals of
0xC0108139 at rebased return address 0x9CCF, exactly five `checkSuspend failure`
lines, zero of either in the HLE control.

The reply was derivable only as "unobserved", and was answered only that far.
Neither output slot is read on Astro's path: the u32 at +0x00 is stored through a
pointer `sceAgcSuspendPoint` sets to null (0x8573 `xor esi, esi`), and the u64 at
+0x08 lands in driver .bss 0x1A898 which the whole text segment only ever writes,
never loads. The device therefore writes **zero bytes** and returns success. A
test pins that with a poison pattern the driver never uses, so "we wrote nothing"
stays distinguishable from "we wrote zeroes".

**3. Open: the failure survived the fix.** Post-fix boot: 0xC0108139 no longer
appears in the unimplemented list, so the handler is live, yet
`checkSuspend failure` still prints exactly five times. The wrapper is returning 0
for a reason other than the ioctl being refused. Next step is to establish what
our ioctl path actually returns in guest `eax` at 0x9CCF: the wrapper branches on
`test eax, eax` immediately after the call, so an ORBIS-style success code that is
not numerically zero would fail that test while looking like success to us.

**Also honest: the allocation stop is NOT connected to this.** All five failures
land at import indices 255k to 1.69M, an order of magnitude before the
post-10M allocation census. A tempting corroboration was checked and rejected:
both runs emit exactly 64 `mspace` lines, which is a sampling cap, not a
cessation, and reading the last one as a timeline would have been wrong.

Still refused, deliberately: 0xC010813B, 0x40048135, 0xC00C8110, 0xC0848119 and
one more. 0xC010813B stays refused on strong evidence, its 0xFF prefill proves the
driver reads real hardware state.
## CORRECTION AGAIN: expf IS THE ENGINE RUNNING, AND THE ENGINE STOPPED LOADING (2026-07-29)

The previous entry named `internal_expf` as the divergence because it was 66.6%
of the stuck run against 36.7% of the healthy one. **Wrong, and wrong the same
way twice: a share read without normalising the denominator.**

Rate-normalised from `run.json` elapsed times:

| | control (reaches worldmap) | stuck (LLE) | ratio |
|---|---:|---:|---:|
| dispatches/s | 137,486 | 140,472 | 1.02x |
| expf/s | 95,724 | 126,517 | **1.32x** |
| non-expf/s | 41,762 | 13,964 | **0.33x** |

expf rose 32%. The SHARE moved because everything else fell to a third. And the
36.7% control figure was an averaging artifact: binned by dispatch index the
healthy run runs 7.8% expf before LOGO and **83-86% after worldmap**, higher
than the stuck run's headline. A high expf share is the signature of Astro's
engine RUNNING, not of it spinning.

`internal_expf` was also checked against the firmware and **ours is correct**:
EXTRACTED from `libSceLibcInternal.sprx` vaddr 0x4CD80, 528 bytes, FreeBSD msun
`__ieee754_expf` with a Sony fast path. Argument xmm0 low dword, return xmm0 low
dword, rax untouched, `expf(1.0f) == 0x402DF854` bit-for-bit, and every edge case
agrees. One gap recorded as debt: the firmware calls a range-error helper at
0x16AE0 on overflow and underflow and we do not set errno; no evidence Astro
reads it, so nothing was fabricated. The NID `8zsu04XNsZ4` appears once in the
eboot and in NONE of the natively-executed firmware modules, so LLE introduced no
new caller: same caller, same code, different throughput.

**The real differential, post-10M dispatches:**

| export | control | stuck |
|---|---:|---:|
| `sceLibcMspaceMalloc` | 11 | **0** |
| `sceLibcMspaceFree` | 5 | **0** |
| `libc:strchr` | 16 | **0** |
| `scePthreadMutexTrylock` | 5 | **35** |
| `internal_memcpy_s` | 43 | 68 |

**Allocation stopped completely.** Zero samples across 159M dispatches bounds
malloc below 0.06% against a healthy 0.76%, at least 12x down and consistent with
zero. The engine did not stop computing; it stopped LOADING. Trylock traffic
seven-folded, which is what a thread retrying a resource it never gets looks
like.

**Two diagnostics unique to the stuck run:**

- `[AgcDriver] Error - sce_agc_internal_suspend_point_submit_final(): checkSuspend
  failure = 0x00000000`, five times, zero in the control. This is Sony's own
  driver reporting a failed suspend-point submit, and the suspend point is one of
  the paths gated by the libSceAgc init latch traced earlier.
- `vk.submit_guest_image 3840x2160`, twice, **zero in the control**. The LLE run
  submitted guest images the HLE run never did.

Honest counter-evidence, not suppressed: the control emits 464
`pthread_cond_wait stalled` warnings and still reaches worldmap, so the stuck
run's 20 blocked waits are not by themselves a blocker signal.

**Next concrete action:** chase `checkSuspend failure = 0x00000000`. It is the
only diagnostic unique to the stuck run, it comes from Sony's own code, and it
sits on the suspend-point path the init latch gates. Find it in
`libSceAgcDriver.sprx`, establish what it checked and what we returned, exactly as
the /dev/gc and canary traces were done.

**Method note, twice burned:** a share is not a measurement. Normalise by time or
by a denominator you have checked is stable, and bin by phase before comparing
two runs of different lengths.
## CORRECTION: AUDIO IS NOT THE BLOCKER, AND I MADE THE ERROR I QUOTED (2026-07-29)

The previous entry called the AudioOut2 retry the LLE blocker on the strength of
it being 99.3% of the log. **That is wrong, and it is precisely the mistake the
standing rule names: a count is not a finding until you know the healthy count.**
I cited that rule in the same entry and then did not apply it.

Measured against the HLE control `20260728-171313-corpus-gate`, which reaches
LOGO, TITLE and worldmap:

| | HLE control (reaches worldmap) | LLE run (reaches nothing) |
|---|---:|---:|
| log lines | 145,962 | 608,368 |
| `8XTArSPyWHk` = 0x80268009 | 142,114 | 604,176 |
| share of log | **97.36%** | 99.31% |
| failures per second | 293.8 | **502.2** |
| `JK2wamZPzwM` PortCreate failures | **0** | **0** |

The healthy boot is already 97.4% this same line. The arguments are byte
identical across both runs, down to the same 16 alternating `rcx` values and the
same module-relative return `0xEB217D`. And the stuck run spins the loop **1.7x
faster** than the healthy one, which is the opposite of being blocked by it.

**Port creation never fails, in either path.** Zero `JK2wamZPzwM` warnings, and
`DirectExecutionBackend.Imports.cs:1439-1497` logs every negative export result
unconditionally with only eight sampled-down pairs, none of them AudioOut2, so
that absence is proof rather than silence. `sceAudioOut2ContextPush` is dispatched
and never returns `0x80268001`, so a context exists and the render path is pushing
frames. The retries are `PortSetAttributes` on the invalid-handle sentinel from
slots the engine never asked us to create, refused correctly.

The 2026-07-27 comment at `AudioOut2Exports.cs:572-599` describing this as a
failed-PortCreate retry loop is stale on that point; a dated correction sits above
it and the still-live `SHARPEMU_DISABLE_SNDZ` documentation was left intact.

**One real defect found and fixed** (`0 warnings, 1953 tests`): the invalid-handle
gate in `AudioOut2PortGetState` read `portHandle <= uint.MaxValue`, but the
sentinel is `0xFFFFFFFFFFFFFFFF`, wider than that. The single handle that is
invalid by definition was the one handle always accepted, answered with success
and a fabricated connected-and-ready port state. That is the fabricated-device
hazard seen from the query side. The fix refuses only the sentinel; a test pins
the wide-handle leniency two upstream tests depend on, so the narrowness is
deliberate rather than accidental.

**The real difference between the two boots, disclosed and not chased:**

| export | HLE control | LLE run |
|---|---:|---:|
| `libSceLibcInternal:internal_expf` | 36.7% | **66.6%** |
| `sceLibcMspaceMalloc` | 23.1% | 10.5% |

`internal_expf` is two thirds of all import traffic in the stuck run, roughly
112M calls of ~169M dispatches, against one third in the healthy one, while
allocation traffic halves. **That is the behavioural difference between a boot
that reaches worldmap and one that reaches nothing**, and it is the next thing to
investigate.
## THE LLE BLOCKER IS NOW A SPIN, AND IT IS THE AUDIO PORT (2026-07-28)

With the image base moved (`8afa378`) Astro's engine runs under Sony's firmware
graphics stack. It then stops advancing, and a budget experiment says so
unambiguously.

| budget | log lines | AudioOut2 retries | share | engine output |
|---:|---:|---:|---:|---|
| 480s | 250,016 | 246,887 | 98.7% | `GAME: Resident Load end 0.10s` |
| 1200s | 608,303 | 604,177 | **99.3%** | `GAME: Resident Load end 0.03s` |

**2.4x the time bought 2.4x the retries and not one additional engine line.** That
rules out "it just needs longer". The engine is spinning, not progressing, and
`sceAudioOut2PortSetAttributes` (`8XTArSPyWHk`) failing with `0x80268009` on
handle `0xFFFFFFFFFFFFFFFF` is essentially the entire run.

**This changes the standing reading of that pattern.** `AudioOut2Exports.cs:584`
records, from Ghidra work, that the engine "leaves failed slots at -1 and already
calls PortSetAttributes(-1) harmlessly", and under HLE that was true: it was
noise at 74.6% of a log that still reached LOGO, TITLE and worldmap. Under LLE it
is 99.3% of everything and the title reaches nothing. The pattern did not change;
what it costs did. A count is not a finding until the healthy count is known, and
now both counts are known.

**Next concrete action:** find why the port handle is -1, i.e. why port creation
fails, rather than treating the retry as noise. `AudioOut2Exports.cs:572-599`
already documents the engine side in detail: `SceSndzAudioOutMain` (eboot
0x800EB31B0) creates its first port through a wrapper at 0x800EB3E10 whose result
is the only init return it checks (test/jns at 0x800EB32BC); on failure it logs,
usleeps a second and retries forever, never reaching the render loop at
0x800EB3800. That is exactly the spin now observed. Establish which call fails
and why, with the same disassembly discipline that produced today's six root
causes. Note `SHARPEMU_DISABLE_SNDZ` exists and parks that thread deliberately;
it is NOT the fix and setting it stalls the title elsewhere.

Corpus budget restored to 480s so baselines stay comparable.
## THE MOVE LANDED: 3,830 -> 250,016 LOG LINES, AND THE ENGINE IS RUNNING (2026-07-28)

`8afa378`. `Ps5MainImageBase` moved from `0x800000000` to `0xC00000000`, above
Sony's system window, with `Ps5MainImageWindowEnd` 0xC10000000,
`Ps5ModuleSpaceEnd` 0xD00000000 and `Ps5ModuleSearchStart` 0xC04000000.
`Ps5ManagedHeapEnd` deliberately stayed at 0x800000000. 0 warnings, **1948
tests**.

**Measured effect, LLE path, same 480s budget:**

| | before | after |
|---|---:|---:|
| log lines | 3,830 | **250,016** |
| import dispatches | 255,331 | **69,932,369** |
| `Cannot initialize the Gpu` | fired | **gone** |
| access violation | `0xC0000005` | **none** |
| guest engine output | none | `GAME: Resident Load end 0.10s` |

**Astro Bot's own engine is now running under Sony's firmware graphics stack.**
Not crashing, not refusing: executing 69.9 million import dispatches and printing
its own progress. Every prior LLE boot died in the loader or in a firmware null.

The chain of causes closed exactly as traced: the title loaded inside the window
`sceAgcInit` uses to identify system callers, so it skipped publishing the AGC
context and left the latch at libSceAgc vaddr 0x30011 set, which made the
accessor at 0x8640 return NULL to everything downstream. Moving the image out of
that window makes the firmware classify the game as the game.

**It still reaches no corpus milestone in 480s**, and the tail says why is worth
checking rather than assuming: the last lines are `sceAudioOut2PortSetAttributes`
(`8XTArSPyWHk`) failing with `0x80268009` on handle -1, the same known pattern
that accounted for 74.6% of an earlier HLE log. At 69.9M dispatches that spam is
now a plausible throughput problem rather than a cosmetic one.

**Next concrete action:** raise the corpus budget for a diagnostic LLE run and see
whether milestones arrive with more time, and separately measure what fraction of
those 69.9M dispatches are the AudioOut2 retry. A count is not a finding until the
healthy count is known: establish how many of those calls a working boot makes
before treating the spam as the blocker.
## THE MOVE IS PREPARED BUT NOT LANDED, AND HERE IS EXACTLY WHAT IT COSTS (2026-07-28)

Landed: `GuestImageLayout` (`a118b86`) and the managed-heap ceiling decoupled from
the image base (`ce4e6d1`). 0 warnings, **1948 tests**, all green, base unmoved.

The heap decoupling is the load-bearing preparation. `IsPlausibleManagedHeapPointer`
used to read `address < Ps5MainImageBase`, which was true only by coincidence: the
IL2CPP object, class, string and field recognisers mean "above 4 GB, below where the
heap stops", and the image happened to start there. Moving the base without this
would have widened that predicate by 16 GB and silently reclassified every pointer
the backend inspects, with no diagnostic. It is now `Ps5ManagedHeapEnd`, same value,
independent constant.

**I attempted the move and reverted it deliberately.** With
`Ps5MainImageBase = 0xC00000000`, `Ps5MainImageWindowEnd = 0xC10000000`,
`Ps5ModuleSpaceEnd = 0xD00000000` and `Ps5ModuleSearchStart = 0xC04000000`, the
build is clean and **exactly 10 tests fail: the differential rows in
`GuestImageRangeTests`**, which recompute the ORIGINAL literal expressions inline
and assert agreement. That is the tests working as designed, not a defect. They
must be updated in the same commit as the move, and doing that carelessly would
destroy the one property that makes them worth having.

**Everything the move needs is now known:**

- New base `0xC00000000`. The firmware test is `retaddr >= 0xC00000000` implies
  title, so this is the first address that classifies correctly.
- Collisions checked: PRT area starts at `0x1000000000`, so
  `Ps5ModuleSpaceEnd = 0xD00000000` leaves 12 GB of clearance. libkernel's own
  hints (`0x880000000`, `0x200000000`) are outside the new range. Astro's eboot is
  `0x0FAE09A8`, so the 256 MB window still covers it.
- `Ps5ManagedHeapEnd` must NOT move; it stays `0x800000000`.
- Test file `tests/SharpEmu.Libs.Tests/Loader/GuestImageRangeTests.cs` needs
  `LiteralMainImageBase` (line 17), `LiteralMainImageWindowEnd` (19),
  `LiteralModuleSpaceEnd` (20), the pin at line 32, and the `InlineData` boundary
  rows updated. The managed-heap rows must be left alone.
- `Ps5ModuleSearchStart` in `SelfLoader.cs:78` is base + 0x4000000 and must move
  with the base.

**Why it is not landed:** finishing it means editing boundary assertions I could
not then re-verify carefully, and a half-checked edit to the predicates that
classify every guest pointer is exactly the unverifiable output this framework
exists to prevent. The refactor that makes the move safe is committed and green;
the move itself is a contained next step with no unknowns left in it.

**After the move, boot and check one thing:** libSceAgc's latch at vaddr 0x30011.
If it clears, `sceAgcInit` published the context and the accessor at 0x8640 stops
returning NULL, which unblocks `sceAgcSuspendPoint` and its three siblings too.
## THE EIGHT SITES ARE FOUR PREDICATES, AND FIVE USE THE BASE AS A CEILING (2026-07-28)

Step one landed: `GuestImageLayout` in `SharpEmu.Core` names the windows the CPU
backend and loader were matching by bare literal. 0 warnings, **1948 tests**, 41
of them new. The base did NOT move, by design, so the move can be bisected apart
from the refactor.

**A correction to the brief that wrote this item.** I described eight sites as
guest-pointer heuristics keyed on the image base. They are **four distinct
predicates**, and five of the eight use `0x800000000` as an exclusive UPPER
bound:

| predicate | window | used by |
|---|---|---|
| main-image window | `[0x800000000, 0x810000000)` | stack-return credit, ref-scan extent |
| module code space | `[0x800000000, 0x900000000)` | RIP sampler, matches `Ps5ModuleSearchEnd` |
| plausible image ptr | `[0x800000000, 0x1000000000)` | unvalidated qword classify; upper bound is the PRT area start |
| **managed heap** | `[0x100000000, 0x800000000)` | **IL2CPP object, class, string and field recognisers** |

Two sites that both read `guestCodeStart` turned out to be different predicates
(`Diagnostics.cs:275` bounds at 0x900000000, `:295` at 0x810000000) and were kept
distinct rather than collapsed.

**This changes the move.** The IL2CPP recognisers say "above 4 GB and BELOW the
main image", so relocating the title **downward** would put the image inside the
managed-heap window and silently break every class-init and string recogniser,
with no diagnostic. Any new base must be **above** `0x100000000` reasoning, i.e.
the move has to go up, past `0xC00000000`, and the heap predicate's ceiling has to
move with it or become an explicit constant that no longer means "the base".

The new tests are differential, not restatements: each theory row recomputes the
original literal expression inline and asserts the two agree, at base-1, base,
base+1, upper-1, upper, upper+1. Plus disjointness (heap and image can never both
be true) and nesting (main window subset of module space subset of image space).

**Step two did NOT land.** The move agent worked in the main tree rather than its
worktree, and produced `tests/.../Loader/Ps5ImageBaseTests.cs` written against the
pre-unification layout, referencing `SelfLoader.Ps5MainImageBase` after it had
moved to `GuestImageLayout`. It is preserved at
`%TEMP%\Ps5ImageBaseTests.cs.partB` and was not landed. `Ps5MainImageBase` is
still `0x800000000`.

**Next concrete action:** perform the move, upward and out of
`[0x800000000, 0xC00000000)`, updating the managed-heap ceiling in the same edit,
with a collision check against the import stub region, the firmware LLE module
load addresses, the guest heap, the PRT area at `0x1000000000`, and libkernel's
own `0x880000000` and `0x200000000` hints. Then boot and confirm the latch at
libSceAgc `0x30011` clears.
## THE BASE SHOULD GO TO 0x900000000, AND THE MOVE IS NOT A ONE-LINE CHANGE (2026-07-28)

Chosen and NOT applied, with the reason stated rather than the move attempted.

**The base: `0x900000000`.** Derived by elimination over every claim the tree makes
on the guest address map, not by preference:

| region | claimed by | evidence |
|---|---|---|
| `[0x100000000, ~0x520000000)` | guest mmap search | `KernelMemoryCompatExports.cs:163` `DefaultMapSearchBase = 0x100000000` on Windows, growing up; `:54` `DirectMemorySizeBytes` 16 GiB |
| `[0x800000000, 0x840000000)` | Sony's system-caller window | decoded from libSceAgc 0x8500 and libkernel 0x1CD5E |
| `[0x804000000, 0x900000000)` | LLE module placement | `SelfLoader.cs:78-79` |
| `[0x1000000000, 0xED00000000)` | PRT area | `KernelRuntimeCompatExports.cs:103-104` |
| `0x600000000000` | guest allocation arena | `PhysicalVirtualMemory.cs:29`, `KernelMemoryCompatExports.cs:154` |
| `0x700000000000` | import stubs | `SelfLoader.cs:24`, `DirectExecutionBackend.cs:334` |
| `~0x6FFFAC000000`, `0x7FFDxxxxxxxx` | guest stacks, dispatcher stubs | `Imports.cs:222`, `CpuDispatcher.cs:32-35` |

The only unclaimed gap in the low canonical half is `[0x900000000, 0x1000000000)`,
28 GiB, bounded below by `Ps5ModuleSearchEnd` and above by `PrtAreaStartAddress`.
Astro's image is `0x0FAE09A8`; at 0x900000000 the headroom to the PRT area is
`0x6F051F658`, about 27.75 GiB, so a title would have to carry an executable image
a hundred times Astro's before it mattered. Moving DOWN instead is not available:
below 0x800000000 is the mmap search region, and an image there would collide
non-deterministically depending on how much the title mapped first.

Two things corroborate the choice rather than merely permitting it. libkernel hands
a system caller the mmap hint `0x880000000`, which today lands inside our own LLE
module placement range; moving the title out of the window switches the hint to
`0x200000000`, which is in the mmap region where it belongs. And the eboot is
`ET_SCE_DYNEXEC` (`e_type = 0xFE10`) with `p_vaddr` starting at 0, so the base is
entirely our choice and no file dictates it.

**Why it was not applied here.** Not a collision. The blocker is that the move
silently disarms roughly fifteen behavioural workarounds keyed to absolute
addresses derived from the current base, enumerated in the correction below. None
of them fails loudly; they simply stop matching. `Diagnostics.cs:275` is the
sharpest case: it hard-codes `rip >= 0x900000000` as the guest-code ceiling, so
moving the base to 0x900000000 without touching it makes the RIP sampler reject
the entire relocated image. Doing all fifteen conversions blind, in the same item,
without the ability to boot, is how a green suite gets reported for a tree that
cannot run.

**And the pin the move was supposed to trip did not exist.** `SelfLoaderTests.cs`
and `GuestTlsImageTests.cs` pin `Ps5MainImageBase`, but they live in
`src/SharpEmu.Tests`, which is not a member of `SharpEmu.slnx` and no longer
compiles against the tree (`Gen5ShaderMetadata`, `VulkanGuestRenderTarget` and
`IGuestAddressSpace.TryBackFixedRange` are all gone). Last touched at `ecddc7d`.
The move would have passed a full green suite without a single assertion noticing.
Replaced by `tests/SharpEmu.Libs.Tests/Loader/Ps5ImageBaseTests.cs`, which reaches
the real constant through `InternalsVisibleTo` instead of retyping it and decodes
the window out of the module on every run.

**The remaining order of work:** base-relativise the fifteen sites, then flip the
constant, then boot. Two commits, and the second one is the one to bisect.

## THE SYSTEM WINDOW IS 1 GiB, NOT 4: THE UPPER BOUND WAS RETYPED, NOT DECODED (2026-07-28)

The two entries below say the system-caller window is `[0x800000000, 0xC00000000)`.
It is `[0x800000000, 0x840000000)`. Corrected here and in place below; the
0xC00000000 figure was never in either module.

libSceAgc.sprx vaddr 0x8500 (file 0xC500), bytes read out of the file:

```
48 8B 04 24                     mov    rax, [rsp]            ; the caller's return address
48 BA FF FF FF FF 07 00 00 00   movabs rdx, 0x7FFFFFFFF
48 89 C1 / 48 C1 E9 23          mov    rcx, rax / shr rcx, 0x23
0F 94 C1                        sete   cl                    ; retaddr <  0x800000000
48 81 C2 01 00 00 40            add    rdx, 0x40000001       ; rdx = 0x840000000
48 39 D0 / 0F 93 C0             cmp    rax, rdx / setae al   ; retaddr >= 0x840000000
08 C8 / 0F B6 D0                or     al, cl / movzx edx, al
E9 EF F1 FF FF                  jmp    0x7720
```

0x7FFFFFFFF + 0x40000001 = 0x840000000. The bound is never a literal in the
instruction stream: it is built by an add, which is why retyping it went unnoticed.
`cmp rax, 0xC00000000` cannot even be encoded, since `cmp r64, imm` takes a
sign-extended imm32 only. An unencodable instruction in a disassembly listing is
the tell that the line was written by hand.

libkernel.sprx file offset 0x1CD5E agrees, and it is the same pair of bounds:

```
48 8B 45 08                     mov    rax, [rbp+8]
48 BA FF FF FF FF 07 00 00 00   movabs rdx, 0x7FFFFFFFF
49 BF 00 00 00 80 08 00 00 00   movabs r15, 0x880000000      ; hint for a SYSTEM caller
48 81 C2 01 00 00 40            add    rdx, 0x40000001       ; rdx = 0x840000000
48 89 C1 / 48 C1 E9 23          mov    rcx, rax / shr rcx, 0x23
48 39 D0                        cmp    rax, rdx
48 BA 00 00 00 00 02 00 00 00   movabs rdx, 0x200000000      ; hint for a TITLE caller
4C 0F 43 FA                     cmovae r15, rdx              ; retaddr >= 0x840000000
48 85 C9 / 4C 0F 44 FA          test rcx, rcx / cmovz r15, rdx ; retaddr < 0x800000000
```

Why it matters, and it is not cosmetic in either direction. The prescribed action
was "move the base out of `[0x800000000, 0xC00000000)`". On the real bound
`[0x840000000, 0xC00000000)` is 3 GiB of perfectly legal target that the wrong
figure excludes, and worse, a reader who trusts it would reject 0x900000000 as
"still inside the window" when 0x900000000 is in fact already outside and
classifies correctly.

`tests/SharpEmu.Libs.Tests/Agc/AgcDefaultStateTests.cs:808` already asserted
`0x840000000 == 0x7FFFFFFFF + module.ReadUInt32(0x851B)` and was green in the
same tree as the wrong doc. A passing test contradicted a committed claim for two
commits and nothing surfaced it, because nothing compares prose to assertions.

## THE IMAGE BASE IS LOAD-BEARING IN EIGHT PLACES (2026-07-28, checked before touching it)

**Superseded on the same day: it is load-bearing in far more than eight, and the
eight are not all the same kind of thing. See the correction entry appended to
this one below.**

Before moving `Ps5MainImageBase`, searched for who assumes it. It is not one
constant, it is a de facto ABI:

```
SelfLoader.cs:76                          private const ulong Ps5MainImageBase = 0x800000000UL
SelfLoader.cs:2046                        return isNextGen ? Ps5MainImageBase : Ps4MainImageBase
DirectExecutionBackend.Diagnostics.cs:254 const ulong guestCodeStart = 0x800000000UL
DirectExecutionBackend.Exceptions.cs:1014 const ulong scanBase = 0x800000000UL
DirectExecutionBackend.Imports.cs:828     candidate < 0x800000000 || candidate >= 0x1000000000
DirectExecutionBackend.Imports.cs:862     savedRbx < 0x800000000
DirectExecutionBackend.Imports.cs:913,916 candidate >= 0x800000000, klass >= 0x800000000
DirectExecutionBackend.Imports.cs:964,1002 candidate >= 0x800000000
```

**CORRECTION, same day, after reading all eight sites instead of grepping them.**
The census above is wrong twice over, and both errors point the same way: the
refactor it prescribes would have been a defect.

*The eight are two different predicates, not one.* Only `Imports.cs:828` uses the
base as a LOWER bound, `candidate < 0x800000000 || candidate >= 0x1000000000`,
which is an image/code test. The other five Imports sites use it as an UPPER
bound on the guest heap: `savedRbx >= 0x100000000 && savedRbx < 0x800000000` at
861, and the same shape at 912-916, 963-964 and 1001-1002. Those coincide today
only because the image happens to sit directly above the mmap search region
(`KernelMemoryCompatExports.cs:163`, `DefaultMapSearchBase = 0x100000000` on
Windows, growing upward). Repointing all six at one `GuestImageBase` inverts five
of them: move the base DOWN and `[0x100000000, base)` is empty, so every IL2CPP
object, string and class recogniser goes permanently silent with no error. That
is the fail-open the standing rules forbid. The right decomposition is three
named ranges, not one constant: the guest image, the guest heap, and Sony's
`[0x800000000, 0x840000000)` system-caller window, which is not ours and must
never be expressed in terms of our base.

*Eight is a floor, and the ones it missed are the dangerous ones.* The grep
covered `SharpEmu.Core` and matched only the bare literal. It missed every
absolute address DERIVED from the base, and those are behavioural, not
diagnostic, so they do not fail loudly when the base moves: they silently stop
matching. `DirectExecutionBackend.cs:7540-7557` rewrites a live return address on
the guest stack keyed on `0x800001C61 -> 0x800001E2B`; `Imports.cs:2979,2983`
gate a patch on `returnRip == 0x800EA01A6`; `DirectExecutionBackend.cs:6178,6183,
6188,7455` read `0x80E7E3A88`, `0x80E7E3A90`, `0x80E754C70`; `Imports.cs:751` uses
`0x801A73110`; `Exceptions.cs:1345-1356` holds eight absolute dump targets and a
`0x8028F6100` window; `SelfLoader.cs:83-84,828-829` hold `0x807BA25B0` and
`0x8030FC300`. `AgcExports.cs:3703` holds `0x801A8B4F8` and lives in
`SharpEmu.Libs`, which the grep never searched. Every one of those is
title-image-relative and every one is inside Astro's `0x0FAE09A8` image.

*And two magnitude bounds will not track the base either.*
`Diagnostics.cs:255` declares `guestCodeEnd = 0x810000000` and uses it at 295,
but the RIP filter at 275 is `rip < guestCodeStart || rip >= 0x900000000UL`, a
fourth bound in the same function that contains no `800000000` for a grep to
find. `Exceptions.cs:1014-1015` hard-codes a 256 MiB image span; Astro's image is
`0x0FAE09A8`, which is 99.6% of it, so a title 0.4% larger is already truncated
by that scan today, before anything moves.

**Session summary, firmware path, all measured today:**

| step | evidence |
|---|---|
| firmware imports 21 unresolved -> 0 | Agc 44/0, AgcDriver 51/0, GnmDriver 38/0 |
| Sony code executing in-game | `[LLE] summary: lle=93 hle=1774` |
| /dev/gc created | `opened device node '/dev/gc' fd=1073741824` |
| trap handler resources | 15 fields EXTRACTED, complaint gone |
| canary smash fixed | GetAppInfo cleared 256 into an 88 byte contract |
| six gnm ioctls answered | ring, doorbell, setup, all from call sites |
| initializers dependency-ordered | dmem complaint 1 -> 0 |
| log depth | 305 -> 1,286 -> 2,431 -> 3,830 |
| guest exit | 0x8A6DFFFF -> 0xAA3C8DC0 -> 0xAB553D40 -> 0 |

Astro still reaches no milestone under LLE, and that is the honest headline: the
HLE path still gets furthest (LOGO, TITLE, worldmap loaded, device loss gone at
the default valve since `9d6c157`).
## ROOT CAUSE: WE LOAD THE TITLE AT THE SYSTEM IMAGE BASE (2026-07-28)

`sceAgcInit` (`23LRUSvYu1M` at libSceAgc 0x8500, not 0x84C0) decides whether to
publish the global AGC context by classifying **its own caller's return address**:

```
0x8500  mov    rax, qword ptr [rsp]     ; the caller's return address
0x8511  shr    rcx, 0x23                ; >> 35
0x8515  sete   cl                       ; retaddr <  0x800000000
0x8518  add    rdx, 0x40000001          ; rdx = 0x7FFFFFFFF + 0x40000001 = 0x840000000
0x851F  cmp    rax, rdx
0x8522  setae  al                       ; retaddr >= 0x840000000
0x8527  or     al, cl                   ; "caller is the TITLE"
0x852C  jmp    0x7720
```

That flag reaches `[rbp-0xBC]`, and at **0x79E3** a zero takes `je 0x78C5`, which
returns **SUCCESS with the latch at 0x30011 left set**. Fifteen exits from
`sceAgcInit` were enumerated over a recursive-descent CFG (827 instructions, 793
of 827 blocks reach 0x7D43); this is the one that fires.

**`src/SharpEmu.Core/Loader/SelfLoader.cs:76`: `Ps5MainImageBase = 0x800000000`.**
Boot log line 84: `Registered module handle=1 name=eboot.bin
base=0x0000000800000000 size=0x000000000FAE09A8`. The entire 262 MB title image
sits inside `[0x800000000, 0x840000000)`, so every call from the game into
libSceAgc looks like a system-library call and the firmware correctly refuses to
let a system module hijack the process-global context.

**The window's meaning is EXTRACTED and corroborated by a second module.**
`libkernel.sprx` file offset 0x1CD5E runs the identical idiom on a return address
to pick an mmap hint: callers inside `[0x800000000, 0x840000000)` get
0x880000000, callers outside get 0x200000000. Two unrelated Sony modules agree
that range is system code and anything outside it is the title. Our own
`Ps4MainImageBase = 0x400000` at line 77 is outside it, which is what hardware
expects.

**Both previously-named suspects are KILLED.** `sceKernelGetAppInfo` and
`sceKernelTitleWorkaroundIsEnabled` gate nothing: at 0x777F a negative GetAppInfo
just zeroes r12d and falls through, and the workaround selectors 0x52/0x53 feed a
value stored to 0x328B8 that is not the byte tested at 0x79E3. The instinct to
flip a selector answer would have been wrong, and the reviewer lens that forbids
flipping values to make progress is the reason it was not tried.

**Next concrete action, and it needs its own item because the blast radius is
every title:** move the PS5 main image base out of `[0x800000000, 0x840000000)`.
This is a one-constant change in `SelfLoader.cs` that relocates every guest
image, so it must be gated on the full corpus, not just Astro. Check first
whether anything in the tree assumes that base: search for the literal, for
hard-coded address ranges in the CPU backend, and for any diagnostic that
classifies guest pointers by magnitude. Only then boot.
## THE LATCH: libSceAgc NEVER MARKS ITSELF INITIALISED (2026-07-28)

Traced, no code changed, and that is the right outcome: the chain terminates in a
firmware global whose writer we can name but whose gate we cannot yet evaluate.

**Symbolisation verified this time.** `qj7QZpgr9Uw` has `st_value = 0x3320`,
`st_size = 0x2DE`, a real `push rbp` prologue at 0x3320 and its `ret` at 0x35FD
exactly at `st_value + st_size - 1`. RIP vaddr 0x354E is genuinely inside it.

**The faulting instruction and the null:**

```
0x3546  call 0x8640                        ; returns RAX
0x354B  mov  r15, qword ptr [rax]          ; AV, tolerated by our VEH
0x354E  mov  r12d, dword ptr [rax + 0x18]  ; FAULT, target 0x18
```

RAX is the return of the local accessor at 0x8640, whose entire body is:

```
0x8640  cmp byte ptr [0x30011], 0
0x8647  je  0x864c
0x8649  xor eax, eax                       ; returns NULL
...     otherwise index a table at 0x328C0, stride 80
```

**So the module carries an "am I initialised" latch at vaddr 0x30011, and its
value in the file is 1**, i.e. NOT initialised (PH7 file offset 0x34011, bytes
`00 01 00 00 | ff ff ff ff`, with the index dword at 0x30014 set to -1). It is a
deliberate latch, not zeroed bss.

**Exactly one writer exists in the whole 0x121E2-byte text**, found by a
RIP-relative xref sweep: every other reference is a `cmp`.

```
0x7D39  mov dword ptr [0x30014], r12d   ; publish the index
0x7D43  mov byte ptr [0x30011], 0       ; clear the latch
```

That writer lives inside `sceAgcInit`'s body at 0x7720. **We know that body runs**:
four consecutive import return addresses in the log land on four consecutive call
sites inside it (0x7756 mutex lock, 0x776F getpid, 0x777D GetAppInfo, 0x7799 and
0x77C0 `sceKernelTitleWorkaroundIsEnabled` with esi 0x52 then 0x53). So
`sceAgcInit` is entered and reaches at least 0x77C0, and then never reaches
0x7D43.

**And the same latch gates everything else**, which is why nothing else recovered
on its own: `sceAgcSuspendPoint` (0x8540) loads error `0x8A6C002F` and skips the
driver submit entirely when the latch is set; same shape at 0x85DC, 0x8970,
0x89A0.

**The branch after 0x77C0 is decided by OUR answers.** Both
`sceKernelGetAppInfo` (`KernelExtraCompatExports.cs:688`, zeroes 0x58, returns 0)
and `sceKernelTitleWorkaroundIsEnabled` (`:721`, writes 0, returns 0) hand back
zero, so `cmp dword [rbp-0xB0], 1` at 0x77C4 fails. That is the class the last
root cause came from too: one of our exports returning zero on a call firmware
made back into us.

**Next concrete action:** disassemble 0x7720 from 0x77C0 forward to 0x7D43 and
identify every branch that can skip the latch clear, then evaluate each against
what we return. `sceKernelTitleWorkaroundIsEnabled` is queried twice with
distinct selectors 0x52 and 0x53 and we answer 0 to both without knowing what
either means; that is enumerable debt and the first thing to price.
## DEPENDENCY-ORDERED INITIALIZERS: THE DMEM COMPLAINT IS GONE (2026-07-28)

`0 warnings, 1897 tests`. Module initializers now run in dependency order
(`ModuleInitializerOrder.Plan`, a stable topological sort; unrelated modules keep
their relative order, cycles are reported by name and broken deterministically,
imports served by HLE create no edge, no module is named in the code).

The boot log proves the sort engaged and the defect is fixed:

```
[RUNTIME] Module initializer order (dependency-sorted from load order):
          libc.prx, libSceNpCppWebApi.prx after libc.prx,
          libSceAgcDriver.sprx after libc.prx, libSceAgc...
[AgcDriver] Info - Initialized, submit.mode= 1.
```

**"Unable to get Dmem from AgcDriver" now appears ZERO times**, against once per
boot before. libSceAgcDriver initialises before libSceAgc, so the arena is real
and the bump allocator at libSceAgc 0xC080 no longer returns NULL.

| | before | after |
|---|---:|---:|
| log lines | 2,310 | **3,830** |
| dmem complaint | 1 | **0** |
| faulting function | libSceAgc 0x7720 helper (NULL arena) | `qj7QZpgr9Uw+0x22E` |

**The fault moved, as predicted, and is again inside firmware.** New crash at
`qj7QZpgr9Uw+0x22E`, RAX = 0 with RBX and RCX holding plausible pointers
(0x30412D620, 0x502703FE4). Still `0xC0000005`. A different function, 66% deeper
into the boot.

**Do not symbolise this by nearest-preceding export.** That mistake cost a hop
last time: `sceAgcGetDefaultCxStateFlat` has `st_size = 0x71` and the real code
was an unexported helper 0xBF later. Check `st_size` before trusting the name.

**Next concrete action:** resolve `qj7QZpgr9Uw`, confirm the RIP lies inside it
by size, and trace what is NULL in RAX exactly as the last trace did: find the
producer, then find who was supposed to have populated it. The same three
candidate classes apply, and the last one was the winner: a global initialised by
a call we never made, a driver value zeroed because we refused something, or one
of OUR exports returning zero on a call firmware made back into us.

**Still open:** six `/dev/gc` requests remain refused, `0xC010813B` on strong
evidence (the driver prefills 16 bytes with 0xFF, so it reads real hardware
state). `accept`/`recvfrom` overrun the caller-declared sockaddr, reproduction
recorded, fix not written.
## ROOT CAUSE: WE RUN libSceAgc'S INITIALIZER BEFORE ITS DEPENDENCY (2026-07-28)

The null is ours, and the guest announced it 255,000 dispatches before the crash.

**Correction to the previous entry.** `AAeX-U5-P3M` (`sceAgcGetDefaultCxStateFlat`)
is at libSceAgc vaddr 0x75F0 with `st_size = 0x71`, so it ends at 0x7661 and RIP
0x785F is NOT inside it; it was merely the nearest preceding exported symbol. The
fault is in an unexported local helper based at 0x7720, tail-called from
`sceAgcInit` (`kW3GLb7QfPg`, 0x84C0) via `jmp 0x7720` at 0x84EE. Symbolising by
nearest-preceding-export is unsound and misled the last entry.

**The chain, every hop cited:**

```
libSceAgc 0x7849  mov edi,8 / mov esi,8 / call 0xC080   ; RDI=8, RSI decremented to 7
libSceAgc 0xC080  bump allocator over {cursor @0x329A8, end @0x329B0}
                  free = end - cursor = 0 - 0 = 0  ->  xor eax,eax  ->  returns NULL
libSceAgc 0x785F  mov dword ptr [rax], 0             ; FAULT, RAX = 0
```

Captured RAX/RBX/RDX = 0, RDI = 8, RSI = 7 match that allocator instruction for
instruction. The arena globals are written only by libSceAgc's own module
initializer at 0xC0F0, which asks
`sceAgcDriverGetReservedDmemForAgc` (`Um-jkyDy9rI`, libSceAgcDriver 0x8A0) for
the region. That function reads `g_dmem_table` at driver vaddr 0x18020 and, when
it is null, **writes addr = 0, size = 0 and returns SUCCESS** (0x8DB-0x8E6).
`g_dmem_table` is populated only by the driver's own initializer.

**And the log says exactly this**, run `20260728-195956-corpus-gate`:

```
1105  ExecuteEntry starting at 0x0000027828350010     <- libSceAgc initializer
1106  Unable to get Dmem from AgcDriver               <- libSceAgc's own diagnostic
1108  Starting module libSceAgcDriver.sprx            <- the dependency, AFTER
1270  [AgcDriver] Info - Initialized, submit.mode= 1. <- driver init SUCCEEDED
```

libSceAgc's initializer runs **before** libSceAgcDriver's, gets a zero arena,
prints its complaint, and never re-asks. The driver then initialises perfectly
160 lines later. 255,331 dispatches after that, `sceAgcInit` allocates 8 bytes
from the empty arena and stores through NULL.

**Ours, at `src/SharpEmu.Core/Runtime/SharpEmuRuntime.cs:499`.**
`RunPreloadedModuleInitializers` iterates `loadedModuleImages` in load order with
no dependency sort, even though libSceAgc has a bound import from
libSceAgcDriver. On hardware the loader orders initializers by dependency.

**Refused-ioctl hypothesis: KILLED**, three ways. No refused ioctl occurs on the
supplier chain (its only ioctl is `0xC004812E`, which we answer). The driver's
init succeeded, so `g_dmem_table` was populated, just too late. And all six
refusals happen after the damage, five of them on fd `0x40000001`, a different
descriptor from libSceGnmDriver's own open.

**Next concrete action:** order preloaded module initializers by import
dependency, not load order. A module whose imports bind to another loaded module
must run after it; cycles must be reported rather than silently ordered. Then
re-run and expect the fault to move, since a correct arena only removes this
crash.

**Method note worth keeping:** the guest printed "Unable to get Dmem from
AgcDriver" at line 1106 and nobody read it until the crash was traced backwards
by hand. It is the cheapest oracle in the log and it named the failure before the
symptom existed.
## THE FAULT IS NAMED: sceAgcGetDefaultCxStateFlat DEREFERENCES NULL (2026-07-28)

Captured the access violation rather than implementing blind, as the previous
entry required. The backend named it:

```
Code: 0xC0000005     RIP: 0x000002782835785F
RIP symbol: AAeX-U5-P3M#E#A+0x26F   (fn base 0x000002782835 75F0)
RAX 0  RBX 0  RDX 0   RDI 8  RSI 7  R8 0x0B800000  R9 0x87400000
```

`libSceAgc` was mapped at `0x27828350000`, so the faulting instruction is at
module vaddr **0x785F**, inside `AAeX-U5-P3M` = **`sceAgcGetDefaultCxStateFlat`**
(ps5rs nids.csv). Three registers are zero and the function is Sony's own
default-context-state builder, so this is a null dereference inside firmware
code, not in ours.

That is a much better position than it sounds. The fault is in a NAMED firmware
function at a known offset in a module we hold as a cleartext ELF, so the
pointer it dereferences can be traced back to whoever was supposed to supply it,
by disassembling 0x75F0..0x785F. No guessing required.

**The leading suspect is one of the six ioctls we still refuse.** Each refusal
leaves the driver believing it holds a resource it does not, and the log shows
`sceAgcDriverNotifyDefaultStates` and `sceAgcGetRegisterDefaults2Internal` bound
nearby. If the defaults table is established through a request we answer with
ENOTTY, this null is the downstream consequence, arriving 255,331 import
dispatches later. That is exactly the distance a plausible-but-wrong success
would have added, and the reason the refusals are logged.

**Next concrete action:** disassemble `sceAgcGetDefaultCxStateFlat` from 0x75F0
and identify the pointer that is null at 0x785F, then trace where it should have
been set. Only then decide which of the six refused requests to recover next.
Do not recover them in numeric order on the assumption that the fault implies
them; the branch has already been burned once by a statically predicted request
order that the boot refuted.
## SIX GNM IOCTLS ANSWERED; THE DRIVER NOW RUNS DEEP AND FAULTS (2026-07-28)

`0 warnings, 1875 tests`. Five more requests recovered from their call sites and
answered, all EXTRACTED field by field:

| request | fn | what it is |
|---|---|---|
| `0xC0408121` x4 | 0x8550 / 0x8790 / 0x89d0 | map a GNM ring, get a doorbell |
| `0xC0088101` | 0x9a90 | 8-byte selector, +0x04 pinned to literal 1 |
| `0xC0108102` | 0x8330 | 16 bytes carrying a guest pointer to a PM4 packet |
| `0x80048134` | 0xa190 | one dword, esi=0 on the init path |
| `0x80048126` | 0x9d10 | one dword, esi=1 |

The 64-byte `0xC0408121` payload was fully recovered, 11 fields, and **nothing
inside it is read back**: every instruction after the call in all three issuing
functions was scanned. So answering it invents nothing. Its buffer at
`[rbp-0x70]` also ends flush against the canary at `[rbp-0x30]`, the same
geometry that just cost us a day, so the handler writes nothing back and a test
guards the byte past the declared size.

**Stopped, on evidence, at `0xC010813B`.** The driver prefills its 16-byte buffer
with `0xFF` (`vpcmpeqd xmm0,xmm0,xmm0` at 0x084f7, stored 0x0850f) before the
call, then copies all 16 bytes out to its caller. That prefill IS the
measurement: the driver deliberately poisons the buffer so a kernel that writes
nothing is distinguishable from one that writes zeroes. Sixteen bytes of real
hardware state, none of it derivable from anything we hold. Refused, and the
reason is recorded rather than papered over.

**Measured effect.** The boot now reaches six distinct unimplemented requests,
each hit exactly once: `0xC0848119`, `0xC008811B`, `0xC010810B`, `0xC010813B`,
`0x40048135` and one more. Execution goes far deeper than before, 255,331 import
dispatches, and the guest entry no longer returns an error at all
(`Guest returned: 0`). The process then dies with `0xC0000005`, an access
violation, rather than a clean guest-side refusal.

That is a change of failure class. Every previous stop was the firmware politely
declining; this is native Sony code running long enough to touch memory we have
not set up. The six named requests are the obvious suspects, since each one we
refuse leaves the driver holding a resource it thinks exists.

**Next concrete action:** the access violation has a faulting address and a call
site; capture them (the backend already reports RIP for CPU traps) before
implementing anything further, because the fault may name the missing resource
directly and save recovering six more request contracts blind.

**Known, unlanded defect:** `accept` and `recvfrom` write more sockaddr than the
caller declared. Reproduce with a 0xA5 guard immediately past the declared
length; `KernelSocketCompatExports.cs:282` already clamps, so the overrun is
elsewhere on those paths.
## THE CANARY WAS OURS: WE CLEARED 256 BYTES INTO AN 88 BYTE CONTRACT (2026-07-28)

Hypothesis confirmed from the callee, not the call site. Sony's own
`sceKernelGetAppInfo` in `libkernel.sprx` (vaddr 0x20280, size 0x83) is a single
sysctl that sets `*oldlenp = 0x58` at 0x202b7, so **88 bytes is the most it can
ever write**. Ours cleared `0x100` = 256.

`libSceAgcDriver` 0x0a3e0 reserves exactly 0x78 for that buffer at `rbp-0xa8`
and keeps its stack canary at `rbp-0x30`, immediately after it. Our over-clear
landed on the canary and nothing else, so the guest died in `__stack_chk_fail`
at 0x0a4a6 with no indication of who had written there. One constant, EXTRACTED
and cited in the code.

**Measured effect of the one-line fix:**

| | before | after |
|---|---|---|
| log lines | 1,286 | **2,431** |
| `__stack_chk_fail` | fires | **gone** |
| blocked at | canary, unattributable | `/dev/gc` ioctl `0xC0408121` |

The run nearly doubled and the failure is once again a named, located request:
INOUT, 64 bytes, group 0x81, num 0x21. That is the loud-refusal design still
paying out.

**Four walls cleared on this path today**, each one measured: unresolved firmware
imports 21 -> 0; `/dev/gc` absent -> opened; trap handler resources refused ->
registered; canary smashed -> intact.

**A defect found and deliberately NOT landed.** The reviewers extended the guard
pattern beyond `GetAppInfo` and found the same class of bug in the socket layer:
`accept` and `recvfrom` write more sockaddr than the caller declared, across
declared lengths 0, 4 and 8. Seven tests fail against current `master`. Those
tests are NOT in the tree, because the fix for them was never produced and
landing red tests helps nobody, but the finding is real and reproducible: rebuild
`GuestBufferBoundsTests` with a 0xA5 guard immediately past the caller-declared
sockaddr length. `KernelSocketCompatExports.cs:282` already clamps with
`Math.Min(addrlen, 16)`, so the overrun is elsewhere on those two paths and
needs its own item.

**Next concrete action:** recover `0xC0408121` from its call site in
`libSceAgcDriver.sprx` the way `0x80788123` was recovered, and keep walking the
request sequence. 46 codes remain ENOTTY, logged and enumerable.
## TRAP HANDLER RESOURCES REGISTERED; A STACK CANARY IS THE NEXT WALL (2026-07-28)

`1d1f0c9`-era work landed as 0 warnings, 1855 tests. The firmware driver now
opens `/dev/gc`, and its first request `0x80788123` is answered rather than
refused: **"Can't set trap handler resources" is gone from the boot** and no
unimplemented-ioctl line fires. Every one of the 120-byte payload's 15 fields was
EXTRACTED from the driver's own allocation names, `SceGnmTrapCode`,
`SceGnmTrapData`, `SceGnmCwsr`, `SceGnmDdid`, mapped by nine
`sceKernelMapNamedSystemFlexibleMemory` calls at 0x07d4f-0x07e28.

Guest exit moved `0xAA3C8DC0` -> `0xAB553D40`. The remaining failure:

```
[LOADER][DEV] opened device node '/dev/gc' fd=1073741824
[LOADER][ERROR] __stack_chk_fail#1: rip=... rdi=0x0000000040000000
Import#604 (Ou3iL1abvng) rsi=0x0000000080788123 rdx=0x00007FFFF07FFE68
```

**The driver's stack canary is dying, and it was dying before this change too**,
so answering the ioctl did not cause it and did not cure it. It was visible in
the previous run and recorded then as unexplained.

**The geometry names a suspect.** The payload lives at `[rbp-0xa8]` and is 120
bytes, so it ends exactly at `[rbp-0x30]`, which is where the function's canary
sits (`cmp rax, [rbp-0x30]; jne 0x0a682` at 0x0a4a6). Anything writing past 120
bytes at that address lands on the canary and nowhere else. The same stack buffer
is `sceKernelGetAppInfo`'s output earlier in the function, at 0x0a417. So the
first thing to check is whether OUR `sceKernelGetAppInfo` writes more bytes than
the guest sized its buffer for. That is a bounded, testable question and it does
not require a boot to answer.

**Refused on principle, and correctly.** Five further requests had their byte
shape fully recovered (`0x80048134`, `0x80048126`, `0x80048127`, `0x80088136`,
`0x80048138`) but not their meaning: the driver's symbol table stops at 0x7440,
so the issuing functions are internal, unnamed, and near no string. Shape is not
meaning, and answering them with 0 would assert we did something we cannot name.
They stay ENOTTY, logged and enumerable. 46 codes remain in that state.

**Still live, unrelated, and larger:** `KernelIoctlCore` returns success for every
ioctl on every non-device descriptor after zeroing 16 bytes of the argument.
## /dev/gc OPENS, AND THE DRIVER NAMES ITS NEXT REQUEST (2026-07-28)

`f4bedc5`, 0 warnings, 1843 tests. The device exists and the firmware driver got
past `Cannot initialize the Gpu`:

```
[LOADER][DEV] opened device node '/dev/gc' fd=1073741824
[LOADER][DEV] /dev/gc ioctl unimplemented request=0x80788123 dir=IN size=120 group=0x81 num=0x23
[LOADER][WARN] Import#603 result: -1 (PfccT7qURYE)
Can't set trap handler resources
[LOADER][ERROR] __stack_chk_fail#1
Guest returned: -1438872128   (0xAA3C8DC0)
```

Guest exit code moved from `0x8A6DFFFF` to `0xAA3C8DC0`, and the failing point
moved from "cannot open the device" to "the device answered but refused request
0x80788123". That is the loud-refusal design working: the driver printed its own
name for the request, **"Can't set trap handler resources"**, which is a contract
label we did not have to reverse for ourselves.

**A prediction was wrong and the run corrected it.** The implementation
recovered 49 ioctl codes by disassembly and implemented `0xC004812E`, expecting
it to be first because it follows the open at `0x7967`. The real first request
is `0x80788123` (IN, 120 bytes, num 0x23), issued from a different path. The 48
refusals were the right call: had they returned success, the driver would have
proceeded on a lie and failed somewhere unrelated.

**A blanket lie was found and is still live.** `KernelIoctlCore` zeroes 16 bytes
of the argument and returns success for EVERY ioctl on EVERY descriptor. The new
device hook diverts device descriptors only, so every other title still gets
"success" for ioctls nobody implemented. That is a separate, larger item and it
is exactly the plausible-stub class this project exists to remove.

**Next concrete action:** implement `0x80788123`, a 120-byte IN structure the
driver calls "trap handler resources". Recover its layout from the call site in
`libSceAgcDriver.sprx` the same way the open chain was recovered, then re-run.
The `__stack_chk_fail` immediately after is likely a consequence of the failed
path rather than a second bug, but it must be confirmed rather than assumed.
## THE FIRMWARE DRIVER WANTS /dev/gc (2026-07-28, it said so itself)

Traced `0x8A6DFFFF` to its cause in four log lines. Sony's own driver diagnoses
the failure for us:

```
[LOADER][IO-FAIL] resolve guest='/dev/gc' host='' reason=path-unmapped
[LOADER][IO-FAIL] open    guest='/dev/gc' host='' reason=path-unmapped
[LOADER][WARN] Import#586 result: -1 (wuCroIGjt2g)
[AgcDriver] Error - Cannot initialize the Gpu (0)
[LOADER][INFO] Guest returned: -1972502529
```

`libSceAgcDriver` opens `/dev/gc`, the Prospero graphics-core device node, as the
first thing it does. We mount /app0, the firmware filesystems, temp0, download0,
hostapp and devlog, but we have **no device nodes at all**, so the open returns
-1 and the driver aborts before touching the GPU. That is why our two new
VideoOut exports were called zero times: execution never reached a flip.

This is the real LLE boundary, and it is exactly the shape the FreeBSD constraint
predicted: firmware drivers do not call a library to reach the GPU, they open a
kernel device and drive it with ioctls. We implement the kernel, so the device
has to be ours.

**Next concrete action:** implement `/dev/gc` as a device node, not a file. Open
must return a real descriptor that subsequent operations recognise, and the
ioctls `libSceAgcDriver` issues after opening must be answered. Recover the
ioctl surface the same way the last two exports were recovered, by
disassembling: the driver's init path is reachable from the `open` call site,
and every `ioctl` request code it passes is a constant in that code. Do not
guess request numbers, and do not return success for an ioctl that was not
implemented, since the driver checks and will proceed on a lie. Note that the
kernel ELFs added today under `games/firmware_kernels/` (14 cleartext kernels,
11.00 through 13.42) contain the other side of this interface and are the
ground truth for what /dev/gc accepts.
## SONY'S AGC CODE RAN INSIDE THE GAME: 93 NIDS BOUND (2026-07-28)

`8d395ab` implemented the last two blocking exports and the firmware chain
loaded. Run `20260728-174859-corpus-gate`,
`SHARPEMU_LLE_MODULES=libSceAgc,libSceAgcDriver,libSceGnmDriver`:

```
[LLE] summary: lle=93 hle=1774
```

**Ninety-three of the title's imports now execute Sony's own firmware code.**
Every previous attempt died in the loader with "has unresolved imports" and exit
code 3, having executed nothing. This one bound the whole set and ran.

The two exports were recovered by disassembling the firmware, not guessed.
`libSceAgcDriver.sprx` JMPREL[30] `j8xl+92A0q4` and JMPREL[31] `7VSZJxxcTL8`
resolve through `DT_SCE_IMPORT_LIB` to `libSceVideoOut`, and both are called
from exactly one function, `sceAgcDriverSetFlip` at 0x6da0 (calls at 0x6e21 and
0x6e58).

- `sceVideoOutSubmitEopFlip` is byte-for-byte `sceVideoOutSubmitFlip` plus a
  null-checked `uint64_t*` out-token in r8, whose low 28 bits the caller emits as
  INT_CTXID of a PM4 `IT_RELEASE_MEM` packet. Arguments EXTRACTED from the call
  site; the 28-bit token ENCODING is ASSUMED and enumerable as the single
  constant `EopFlipTokenMask`.
- `sceVideoOutSysGetBus` returns `dword [port+0x138]`, a field written exactly
  once in the whole module, from `sceVideoOutOpen`'s `busType` argument. Fully
  EXTRACTED, no debt.

**Where it now fails.** The guest entry point returns `0x8A6DFFFF` and the
process exits code 4. That is a guest-side failure after execution began, not a
loader refusal, and it is a different and later failure than anything seen
before. The title reaches no milestone, so LOGO, TITLE and WORLDMAP all regress
against the HLE baseline; that is expected while the chain is half native and
must not be recorded as a baseline.

Our two new VideoOut exports were never called in this run (0 occurrences), so
the failure happens before the first flip. The `[LLE]` binding log plus the
firmware-hit lines confirm all three modules mapped from the 4.03 tree.

**Next concrete action:** find what returns `0x8A6DFFFF`. It is a guest return
value, so trace which of the 93 LLE-bound calls precedes it. `SHARPEMU_LOG_ALL_IMPORTS`
plus the `[LLE]` binding table narrows it to a named Sony function; the answer is
either a contract we get wrong in a callee those modules call back into, or an
argument we hand them.
## CORRECTION: THAT HUD IS OURS, AND THE GUEST DRAWS NOTHING (2026-07-28)

The previous entry read the on-screen overlay as the game's own and concluded
"the render path works end to end". **That is wrong.** The overlay is SharpEmu's
`src/SharpEmu.Libs/VideoOut/PerfOverlay.cs:177-183`, which formats those exact
five lines, rasterises them with an embedded 5x7 font into a 376x176 BGRA panel,
and has the presenter blit it into the swapchain at a 12 pixel margin
(`VulkanVideoPresenter.cs:5176-5178`). It is ON by default unless
`SHARPEMU_OVERLAY=0`, and F1 toggles it.

The red rectangle is ours too. `DrawFrameGraph` (`PerfOverlay.cs:193-244`) draws
a 128-sample frame-time bar graph and colours a bar red above 34 ms. At 1519.9 ms
every bar is clamped to full height and red, so the graph reads as one solid
block. The two thin lines are its 16.7 ms and 33.3 ms reference marks.

**So the guest contributes ZERO visible pixels.** Everything on screen is host
instrumentation. That agrees with the readback, `nonblack_pixels=0/2025600`, and
it removes the "overlay renders, scene does not" lead entirely: there was never a
guest-drawn overlay to contrast against.

**What survives, correctly attributed.** These are OUR counters measuring guest
submissions, not the engine's self-report, which makes them evidence about our
pipeline rather than about what the game believes:

- `DRAWS 275/F` at `31/S`: the guest really is submitting 275 draws per frame.
- `1519.9 MS` per frame, `FPS 0.1`, `FLIP 0.1`.
- `CPU 2%`: we are not CPU bound.
- `Q 501+1`: 501 pending work items plus 1 in-flight submission, the first
  direct measure of how far the queue has fallen behind.

**Method note.** The screenshot was still worth every minute: it produced the
frame-time and queue-depth numbers, exposed that the corpus gate had been passing
`--no-screenshot` since it existed, and it took a human glance to catch that the
HUD was ours. An agent reading its own instrumentation as the guest's output is
exactly the confusion that "absence is not evidence" does not cover, and worth
remembering as its own failure mode.
## WE LOOKED AT THE SCREEN AND THE GAME IS ALIVE (2026-07-28)

First visual verification of an Astro Bot boot. Run
`20260728-172421-manual-screenshot`, captured at engine time 00:05:24, right
around the worldmap load. The game's OWN debug HUD is on screen and legible:

```
FPS 0.1   FLIP 0.1   1519.9 MS
DRAWS 31/S  275/F   Q 501+1
ALLOC 72.2 MB/S  GC 25/1/0
MEM 5861M  BUF 192M  CPU 2%
TIME 00:05:24
```

**The engine is not stalled.** It submits **275 draws per frame**, allocates
72 MB/s, runs its GC, and advances its own clock. It is emitting a debug overlay
that renders correctly: text, a red rectangle, two rule lines. So the render
path works end to end. What it does not do is put any game imagery on screen.

**1519.9 ms per frame at 2% CPU.** Not CPU bound, and the earlier reading that
this is our shader dispatcher spinning is consistent with it. `Q 501+1` is a
501-deep submission backlog, which is the first direct sign of how far behind
the GPU queue has fallen.

Six captures between engine time 02:49 and 07:19 are pixel-identical except for
the HUD counters. No splash, no logo, no title, no menu, no worldmap imagery,
at any point, including well past `LevelDocument Loaded: worldmap`.

**Correction to an earlier entry.** "The one presented frame is fully black" was
measured off a guest-image readback and is still true of that surface, but it
gave the wrong impression: the WINDOW is not blank. It carries a stable,
correctly-rendered overlay. Scene geometry produces nothing; overlay geometry
produces pixels. That distinction is the sharpest lead we have and no log metric
could have surfaced it.

**Tooling defect, now fixed.** `scripts/corpus_gate.py` passed `--no-screenshot`
to every run, so the corpus gate has been blind for its entire existence. Only
`--no-require-screenshot` is kept, so a capture failure still cannot fail a gate
whose scoring is log-only. Separately, the harness's own GDI capture
(`GetWindowRect` + `CopyFromScreen`) fails with "The handle is invalid" against
this window, which is the usual DXGI flip-model versus desktop-capture problem;
`PrintWindow` with `PW_RENDERFULLCONTENT` works and is what produced these
images.

**Next concrete action:** the overlay renders and the scene does not, so compare
them. Find what the overlay's draws have that the scene's 275 draws lack: bound
render target, viewport, EXEC mask at export, or a colour write mask. The
shader-address dump and the new subgroup lowering both apply, and the question
is now narrow enough to answer from one frame's draw list.
## THE DEVICE LOSS IS GONE AT THE DEFAULT VALVE (2026-07-28, measured)

`9d6c157`, 0 warnings, 1813 tests. Run `20260728-161155-corpus-gate`, valve at
its default 100,000, no caps, no flags.

```
LOGO t+..  TITLE t+..  WORLDMAP reached
[corpus-gate] astro: not reached: DEVICELOST
```

First run all day to reach worldmap without the GPU being reset.

**Absence proven, not assumed.** The pixel wave really engaged: the
"modelled as ONE lane" warning now fires for **Vertex only** (4 programs, guest
wave 32 against a 64-lane host, correctly refused) and **zero times for Pixel**,
against 2 in the previous run. The run also goes 15% further, 165,573 log lines
against 143,795. Note the honest gap: `gpu_ledger_retired`, `work=offscreen` and
every mention of `0x5008F1400` drop to zero, but all three are ERROR-level lines
emitted only on anomaly, so their absence is consistent with "nothing went
wrong" and does not by itself prove the draw executed.

**What the workflow found that was not briefed**, each a real defect:

- `BooleanToWaveMask` took ballot component 0 only. At 64 lanes that gives guest
  lanes 32-63 a permanently clear EXEC bit, so half the fragments never shade
  and `s_cbranch_execz` retires a half-unrun wave.
- `v_readfirstlane` elected `FindILsb(ballot[0])`. A wave active only in lanes
  32-63 fed `FindILsb(0)`, undefined in GLSL.std.450, into a broadcast Id.
- `DeclareStageInterface` declared `LocalInvocationIndex`, a compute-only
  builtin, for any wave64 module. Invalid SPIR-V in a fragment shader,
  previously unreachable only because the graphics wave was refused.
- `EmitPermlane16` hard-coded `& 31`.

**Wave width is now EXTRACTED, from the primary source**, not inferred:
`games/gpu shit_forzen/_text/GPU_Shader_Core_ISA_Specification_-_SDK_12.000.txt:68-73`
states wave32 is the default for all stages except pixel, and wave64 is the
default for pixel shaders.

**The capability gate is real.** `VulkanVideoPresenter.LoadComputeDeviceLimits`
now publishes `supportedStages` and `supportedOperations` rather than printing
and discarding them, and the translator gates on the stage bit plus VOTE and
BALLOT, with "never queried" worded distinctly from "queried and absent".

**Still open, and untouched by this.** Worldmap loads and never starts, and the
single presented frame is still fully black. Those were always separate from the
crash; the crash merely ended the run first. Two items of known debt:
helper-invocation semantics are UNVERIFIED (a helper lane sets its EXEC bit from
instruction zero, so readfirstlane can elect a helper and broadcast its value to
real fragments), and vertex shaders remain one-lane on this host.
## THE WAVE FIX IS CORRECT AND REFUSED ITSELF: THIS HOST IS WAVE64 (2026-07-28)

Landed the graphics subgroup wave (`907c837`, 0 warnings, 1799 tests) and booted
at the DEFAULT valve. The device loss still fired on the same shader. It did not
engage, and the reason is in the log:

```
Vulkan subgroup default=64 stages=ShaderStageAllGraphics, ComputeBit, ...
[SPIRV][WARN] host subgroup size is 64 but the translator models a 32-lane RDNA wave
[SPIRV][WARN] program=0x0000000500670F00 stage=Pixel is emitted without a subgroup
              invocation id, so its wave is modelled as ONE lane
```

The implementation gates the graphics wave on `hostSubgroupSize == 32`, as a
proxy for device support and to avoid leaving lanes 32-63 with a zero lane bit.
**This host reports 64.** So the wave was refused and the pixel stage is still
one lane. The guard behaved correctly; the premise was wrong.

**Two facts this run establishes that were previously unknown:**

1. `supportedStages` includes `ShaderStageAllGraphics`. The device-capability
   risk that drove the conservative outcome does not apply on this machine:
   fragment vote and ballot are legal here. That does not make them legal
   everywhere, so the capability still needs a real query rather than a proxy.
2. The guest's pixel wave is very likely 64, matching the host. The EXTRACTED
   citation at `StoreWaveMask` makes wave32 the default for every stage EXCEPT
   pixel, and Astro's `s_mov_b64 s[40:41], exec` is a 64-bit mask. So host and
   guest agree at 64 and the natural mapping is one host lane per guest lane,
   with no bridge at all.

**Independent corroboration of the root cause.** At 1080p the shader costs
mean=814.2 ms (n=24, min 547.7, max 6598.4) against mean=786.3 ms at 4K. Four
times fewer fragments, the same cost. A fragment-bound shader gets cheaper; a
shader whose cost is dispatcher iterations does not. The clamp was not free
elsewhere, but for this shader it changed nothing, which is what the
spinning-dispatcher model predicts.

**Next concrete action:** model the graphics wave at 64 lanes when the host
subgroup size is 64, one host lane per guest lane, and admit the wave whenever
host size equals the modelled guest width rather than requiring 32. Publish
`supportedStages` and `supportedOperations` from
`VulkanVideoPresenter.LoadComputeDeviceLimits` (they are read and printed at
`VulkanVideoPresenter.cs:4270-4320` but only `subgroupSize` is published at
`:4286`/`:661`) so the translator can gate on the real capability instead of a
proxy. Acceptance is unchanged: the device loss must disappear at the DEFAULT
100,000 valve.
## THE ONE PRESENTED FRAME IS MATHEMATICALLY BLACK (2026-07-28, read back)

Captured the single presented guest frame with
`SHARPEMU_TRACE_GUEST_IMAGES=present` and `SHARPEMU_GUEST_IMAGE_DUMP_DIR`:

```
vk.swapchain_image size=1920x1055 format=B8G8R8A8Unorm
  nonzero_bytes=2025600/8102400  nonblack_pixels=0/2025600
  hash=0x7AEE9C1FB9A0F725
```

Not dark, not dim. Zero non-black pixels out of 2,025,600. Exactly one quarter
of the bytes are nonzero, which is the alpha channel sitting at 255 for every
pixel, so RGB is uniformly zero and the frame is fully opaque. The compositor
presents it happily; nothing downstream can tell it apart from a real frame.
Raw BGRA preserved at `artifacts/framedump-134922/`.

That is the whole picture for an entire boot: one opaque black frame, presented
once, early, just after the splash. It is the same in every run that gets that
far, with the dispatcher valve at its default or capped at 256.

**Corpus titles are now clamped to 1080p.** `SHARPEMU_MAX_WIDTH=1920` and
`SHARPEMU_MAX_HEIGHT=1080` added to both manifest entries. The guest had been
allocating a 3840x2160 display buffer and we were downscaling it into a
1920x1055 swapchain, so the title was rendering four times the pixels that could
ever be displayed. Per `EmulationCostProfile`, clamping the display buffer makes
the title itself take the cheaper path.

Note for anyone comparing numbers across this boundary: every timing recorded
earlier today, including the 786 ms mean on `0x5008F1400`, was measured at 4K.
Post-clamp timings are not comparable to them, and the baseline was recorded at
4K too.
## ROOT CAUSE CONFIRMED: PIXEL SHADERS MODEL A ONE-LANE WAVE (2026-07-28)

The convergence hypothesis is confirmed, in code, with a test that walks the
emitted module rather than prose.

`Gen5SpirvTranslator.UsesSubgroupOperations()` is
`_stage == Compute && ObservesWaveWidth()`. **It is false for every vertex and
pixel shader by construction.** Everything follows from that one predicate:

| construct | pixel-stage lowering | consequence |
|---|---|---|
| `EXEC` | a private `OpTypeBool`, initialised true | one lane, not a mask |
| `SubgroupAny(x)` | returns `x` unchanged | `s_cbranch_execz` has no vote |
| `v_readfirstlane_b32` | copies this invocation's own VGPR | every lane elects itself |
| `CurrentLaneBit()` | constant `1` | mask arithmetic is meaningless |
| `GuestWaveLane()` | constant `0` | every invocation believes it is lane 0 |

Declared capabilities for a pixel module are `Shader`, `Int64`, `ImageQuery`.
No `GroupNonUniform*` at all. Asserted by
`ExecMaskLoweringTests.PixelStageHasNoSubgroupVoteSoExeczIsPerInvocation`, which
compiles the readfirstlane shape and counts **zero** `OpGroupNonUniformAny`.

So Astro's 0x1208 loop is semantically inverted. On hardware one wave walks the
distinct resource indices across its lanes and retires the matching lanes each
pass, so the trip count is the number of distinct indices. Here every invocation
runs a private copy whose mask only ever describes itself, nothing retires
wave-wide, and the only thing that stops it is the 100,000-step valve. That
matches every measured symptom: 786 ms mean, `No fault detected`, and
`SHARPEMU_SHADER_MAX_STEPS=256` removing the device loss outright.

Second defect found in passing: `StoreWaveMask` in wave32 writes s126 and leaves
**s127 stale** (`Gen5SpirvTranslator.cs:6855-6865`), so the guest's
`s_mov_b64 s[40:41], exec` at 0x1214 captures a garbage high half. Harmless only
while `CurrentLaneBit()` is 1; it becomes a real bug the moment a genuine wave
mask exists, which is exactly what the fix introduces.

Also landed: `OpName` for `exec`/`vcc`/`scc`. Before this they were anonymous
Private variables, so "does v_cmpx write EXEC" could not be answered by walking
the module at all.

**Next concrete action:** extend subgroup lowering to the pixel stage. Declare
`GroupNonUniform`, `GroupNonUniformVote`, `GroupNonUniformBallot` and
`GroupNonUniformShuffle`, wire `SubgroupInvocationId` for graphics stages, and
make `SubgroupAny`, `CurrentLaneBit`, `GuestWaveLane` and
`v_readfirstlane_b32` real. Fix the stale s127 in the same change, since a real
mask makes it load-bearing. Verify by dumping steps for 0x5008F1400 and by the
corpus gate: the device loss must disappear at the DEFAULT 100,000 valve, not a
lowered one.
## ASTRO'S KILLER SHADER IS A BINDLESS READFIRSTLANE LOOP (2026-07-28, dumped)

Merged the listing instrument and pointed it at the shader that ends every run.
`SHARPEMU_DUMP_SHADER_ADDR=0x5008F1400`, run `20260728-134922-corpus-gate`.

```
[SHADERDUMP] summary shader=0x00000005008F1400 instructions=2292 backwardBranches=4
```

2,292 instructions, 41 bound textures. Branch mix: 18 `s_cbranch_scc0`,
**17 `s_cbranch_execz`**, 10 `s_branch`, 7 `s_cbranch_vccz`, 5 `s_cbranch_scc1`,
1 `s_cbranch_vccnz`.

All four backward branches are UNCONDITIONAL `s_branch` with no guarding
compare, which is why the earlier "wrong trip count" framing was wrong. The
loop at `0x1208` is the standard GCN readfirstlane idiom for divergent resource
indices:

```
0x1208  v_cmpx_ne_u32              EXEC = lanes still needing work
0x120C  s_cbranch_execz            exit when none remain
0x1210  v_readfirstlane_b32 s106, v1
0x1214  s_mov_b64 s[40:41], exec   save
0x1218  v_cmpx_eq_u32 s106, v1     EXEC = lanes matching the first lane
0x121C  s_cbranch_execz
  ...
0x13B4  s_mov_b64 exec, s[40:41]   restore
0x13B8  s_branch -436              unconditional back-edge
```

**The loop has no trip count at all.** It terminates only because `v_cmpx`
narrows EXEC and `s_cbranch_execz` observes it reach zero. Termination is a
property of our EXEC and wave modelling, not of any bound in a buffer. That
retires the "read the loop bound from the descriptor" line of enquiry: there is
no bound to read.

`s126`/`s127` is the EXEC pair in the GFX10 SGPR encoding, and the listing
prints it as a plain `s126`, so check first whether the SPIR-V path treats a
write to 126 as a write to EXEC. `Gen5ShaderScalarEvaluator.cs` clearly does
(`:169`, `:1503`, `:1778`, `:1833`, `:1895`, `:1944`), but that is the
compile-time scalar evaluator, not the runtime lowering in
`Gen5SpirvTranslator`.

**Hypothesis, UNVERIFIED, stated so it can be killed:** a SPIR-V invocation is
one lane, so a per-invocation PC dispatcher emulating a 64-bit EXEC mask cannot
converge this loop the way hardware does. Each invocation would walk the unique
values itself rather than the wave retiring them together, turning one wave-wide
convergence into up to 64 iterations per invocation, nested inside the outer
loops. That is the shape of a 100,000-step dispatcher run.

**Next concrete action:** instrument the dispatcher to report actual steps per
draw for this shader (the valve at `Gen5SpirvTranslator.cs:335` already counts
them; it just never reports). If steps land near 100,000, confirm whether
`v_cmpx` and `s_cbranch_execz` are lowered against a real per-invocation EXEC
and whether subgroup ops are available to model the wave properly.
## THE LLE FRONTIER IS NOW TWO NAMED VIDEOOUT FUNCTIONS (2026-07-28)

Implementing one kernel export collapsed the chain. `sceKernelSetProcessProperty`
(NID `-W4xI5aVI8w`) is exported only by libkernel, which carries 352 syscall byte
sites and can never run here, so it had to be ours. Added as a success stub,
tagged ASSUMED, matching the same NID in `inspiration/acelogic-sharpemu`
`KernelSystemServiceCompatExports.cs:36`. It counts its calls and announces once
that it has no extracted contract, so no run can quietly depend on it.

Effect, run `20260728-132535-corpus-gate`:

```
[LLE] libSceAgc imports:         bound=44 unresolved=0
[LLE] libSceAgcDriver imports:   bound=51 unresolved=0
[LLE] libSceGnmDriver imports:   bound=38 unresolved=0
```

Sony's entire graphics command stack now resolves completely. That is the whole
Agc chain: 21 unresolved at the start of the day, 0 now.

What remains is not Agc at all. `libSceAgcDriver` needs two exports that live in
VideoOut and are absent from `src/`:

| NID | name |
|---|---|
| `7VSZJxxcTL8` | `sceVideoOutSysGetBus` |
| `j8xl+92A0q4` | `sceVideoOutSubmitEopFlip` |

Loading `libSceVideoOut.sprx` to supply them does not work: it drags in 26
unresolved imports of its own, so the cheap direction is closed. And these two
must NOT be stubbed. `sceVideoOutSubmitEopFlip` is the flip submission itself,
the boundary where GPU work becomes a displayed frame; a success stub there
would return OK and present nothing, which is precisely the plausible-lie
failure this project exists to prevent.

**Correction to a method used earlier today:** the raw byte scan for "which
module exports this NID" over-approximates badly. It named `libSceGnmDriver` as a
provider of `j8xl+92A0q4` when that module merely imports it too. Loading it
proved that. Read the export table, do not grep the file.

**Next concrete action:** implement `sceVideoOutSysGetBus` and
`sceVideoOutSubmitEopFlip` against our existing `VulkanVideoPresenter`, deriving
the flip contract from how `libSceAgcDriver` calls it rather than guessing.
Until both exist, no LLE run has executed a single instruction of Sony's Agc
inside a game, and every Astro Bot measurement on this branch remains an HLE
measurement.
## ROOT CAUSE: THE 786ms IS OUR SPIR-V DISPATCHER SPINNING, NOT THE GAME'S LOOP (2026-07-28)

A workflow reviewer reading `Gen5SpirvTranslator.cs:326-340` proposed that the
cost is our own PC-dispatcher loop running to its safety valve, and named a
cheap discriminating test. The valve is already env-configurable, so the test
needed no code at all: `SHARPEMU_SHADER_MAX_STEPS` defaults to `100_000`, and
its own comment says it exists so "a mistranslated shader whose loop-exit
condition is wrong" terminates instead of wedging the GPU.

Booting Astro Bot with `SHARPEMU_SHADER_MAX_STEPS=256`, run
`20260728-123827-corpus-gate`, changing nothing else:

| | default (100000) | capped (256) |
|---|---|---|
| LOGO / TITLE / WORLDMAP | reached | reached |
| `Vulkan device lost` | **fires** | **0 occurrences** |
| log lines | 103,500 | **158,865** |

The device loss is gone and the run continues 58% further. Nothing else was
touched, so the cap alone accounts for it. That confirms the dispatcher was
running to its valve: the half-second floor and the 6.8s tail were our
translation looping, not a guest trip count.

**This is not a fix and must not be recorded as one.** A lower cap makes the
shader terminate early, so its output is simply wrong; the comment at `:326`
says exactly that. What the experiment buys is the location of the defect. It is
in how we lower Gen5 control flow into a SPIR-V PC dispatcher, not in a scalar
bound read from a buffer, and not in Sony's code.

**Absence checked, per the standing rule.** `gpu_ledger_retired` and
`work=offscreen` both drop to zero in the capped run, but both are error-context
lines, so zero is consistent with "no errors" rather than "no rendering". The
positive counter, presented guest frames, is 1 in both runs, so rendering did
not visibly advance either way. The honest claim is narrow: the crash is gone,
the run goes further, the picture does not improve.

**Still open, and now clearly separate:** the worldmap loads its document and
never starts. `GAME: Level has started` prints for `ps_logo` and
`title_controller_ship`, never for `worldmap`, with or without the cap. The
device loss was a second, independent bug that happened to end the run first.

**Next concrete action:** the new `[SHADERDUMP]` instrument from workflow
`wf_e266cbfb-fc4` is unmerged in `.claude/worktrees/wf_e266cbfb-fc4-1`. Merge
it, dump `0x5008F1400`, and read how many backward branches its dispatcher
carries. Then decide whether the lowering should emit structured SPIR-V loops
for reducible regions instead of dispatching every basic block through one
switch.
## THE AGC CHAIN CLOSES ON ONE MISSING KERNEL FUNCTION (2026-07-28, measured)

Iterated the LLE dependency closure by boot, not by reasoning. Each step is a
real run under `SHARPEMU_LLE_MODULES`.

| modules LLE'd | unresolved |
|---|---|
| `libSceAgc` | 21 |
| `+ libSceAgcDriver` | 3 |
| `+ libSceGnmDriver, libSceVideoOut` | 1 |
| `+ libSceComposite` | 1 |

Every module in that chain is admitted native: `libSceAgc` exec_bytes=74210,
`libSceAgcDriver` 43538, `libSceGnmDriver` 32434, `libSceVideoOut` 108386,
`libSceComposite` 41570, **all with syscall_byte_sites=0**. `libSceAgc` reports
`bound=44 unresolved=0`: Sony's graphics library is fully satisfied.
`libSceComposite` resolved out of the 3.02 dump, which is the multi-version
firmware search doing exactly what it was built for.

The one remaining import is `-W4xI5aVI8w`, and the byte scan finds it exported
by `libkernel.sprx`, `libkernel_sys.sprx` and `libkernel_web.sprx`. Those carry
352 syscall byte sites and can never execute on Windows, so it cannot be
satisfied by loading more firmware. `inspiration/ps5rs/data/nids.csv:85040`
names it:

> **`sceKernelSetProcessProperty`**

It does not exist anywhere in `src/`.

**Conclusion.** The entire Prospero graphics stack can run as Sony's own native
code on this host. The gate is a single unimplemented kernel function. That is
the whole distance between our reimplementation of libSceAgc and Sony's.

**Next concrete action:** implement `sceKernelSetProcessProperty`. Recover its
contract the way the framework requires rather than guessing: it is a leaf-ish
kernel export, so the firmware differential oracle applies directly. Take the
4.03 body, check it against the syscall admission, and if it is clean, drive it
with the oracle to extract the contract instead of inventing one. Then re-run
the chain above and see how far Astro Bot gets with Sony's Agc in place of ours.
## LLE EXPERIMENT: SONY'S libSceAgc LOADS, THEN STARVES ON 21 MISSING NIDS (2026-07-28)

Ran the experiment rather than reasoning about it. `SHARPEMU_LLE_MODULES=libSceAgc`,
Astro Bot, run `20260728-121008-corpus-gate`.

**It loaded.** `[LLE] admission libSceAgc: exec_bytes=74210 syscall_byte_sites=0
admitted=True`, mapped from
`games/PS5_4.03_reconstructed/filesystems/system/common/lib/libSceAgc.sprx`.
Same-ISA native execution of Sony's graphics library is not the obstacle.

**Then it refused to run**, correctly and loudly:

```
[LLE][ERROR] libSceAgc has 21 unresolved strong import(s)
InvalidOperationException: Firmware LLE module libSceAgc has unresolved imports
```

The 21, none of which appear anywhere in `src/`:

```
+b34-CLWc0s  -vc-xL+G8u0  0MtUJ3BpGhE  2PrsbRYyZi4  AU87qNukGi4
HMnVBVUyajk  LEnn-4ARRJM  UM8rn9hRWrY  UM9b9NunSrE  Um-jkyDy9rI
VhLnEiTuuWo  WNyjOWq8-Vk  aCfbPzyjU90  gyVTZWyySpM  i6bfTi13ApA
k8rLr8nq-hE  oFb2hMcoJa4  u8BkdHb1+Po  xDPdCurOujQ  yuO+lNrj+Do
zmw2uVSEj94
```

**What this settles.** Firmware LLE is mechanically sound and blocked on a
dependency frontier, not on the CPU, the ISA, or the loader. Sony's own library
states our HLE's gaps as a list of 21 names, which is a far better work queue
than the 814-entry LIE census because these are the exact imports a real Sony
module needs in order to function. The run reached no milestone at all, which is
the honest cost of failing closed: the alternative would have been to leave the
stubs as traps and let the title die somewhere unrelated and unexplained.

Note the corpus gate reported `IMPROVEMENT astro: anti-milestone DEVICELOST no
longer fires` beside three regressions. It did not fire because the boot never
got far enough to draw. That is exactly the absence-is-not-evidence trap, and it
is worth keeping as the standing example of why an anti-milestone must be read
next to the log length.

**What it does NOT settle.** Nothing here touches Astro's menu. That run dies
because one pixel shader costs 786 ms a draw, and that shader is compiled by us.

**Next concrete action:** resolve those 21 NIDs to names against the 154,458-entry
Aerolib catalog and the 39,158 symbol names in `games/3.02`, then rank them by
how many other firmware modules also import them. That ranking is the real
build order for the HLE.
## ASTRO'S DEVICE LOSS IS A SHADER HANG, NOT A FAULT (2026-07-28, measured)

From `artifacts/game-runs/astro/20260728-094757-corpus-gate`, the four
submissions before the loss retired in 0.469, 0.240, 0.239 and 0.211 ms, one
draw each. The next one, `timeline=41453 guest_submission=3787
seq=[1307546..1307547] draws=1`, took **6845.906 ms** and then the device went
away. That is a TDR: Windows reset the GPU because a single draw never
finished.

`VK_EXT_device_fault` reports `origin=exception result=Success addresses=0/0
vendor_infos=0/0 description='No fault detected'`. So it is **not** a page
fault, not an out-of-bounds descriptor, not a memory violation. Nothing was
violated; the work simply never terminated.

That relocates the whole investigation. It is not the asset path, not the
lock, not GPU cost, not the fonts, and not a driver fault. One draw hangs, and
a draw hangs because a shader does not terminate. The suspect is named in
every device-loss line and has been all along: `ps=0x00000005008F1400`, mrt=2,
textures=41, vertices=4164.

**Next concrete action:** dump that pixel shader's Gen5 bytecode and its
translated SPIR-V, and look for a loop whose exit condition our translation
drops or inverts. The scalar evaluator is the highest-risk area: it has taken
5 point fixes in one day and is the file the fix-storm detector already flags.
A wrapped or sign-extended loop bound there produces exactly this, a shader
that is correct-looking and never exits.
## MOUNTS, ATRAC9, AND TWO GATES THAT WERE LYING (2026-07-28)

**Landed** (`e522596`, `f4cb2fa`, `761788f`, `d17110e`; premerge green on all
five steps: build 0 warnings, 1747+137 tests, corpus, oracle, fix-storm).

- **ATRAC9 decodes.** AJM parsed the config and wrote zeroes. Real files now
  decode: `sfx_coldboot` 332,000 bytes in, 423,728 samples over 1,660 frames,
  peak 2,678; `bgm_home` 8,919,200 in, 11,415,957 samples over 44,596 frames,
  peak 1,560. The worker also found AJM had **two owners with separate instance
  dictionaries**, so an instance created through the registered
  `sceAjmInitialize` could never be resolved by `sceAjmBatchStartBuffer`. The
  decode path was unreachable from the real export registry regardless of what
  it decoded.
- **Firmware filesystems are now mounted.** We mounted `/app0` and nothing else,
  so `/etc/localtime`, `/usr/share/zoneinfo/localtime` and `/SymbolMap` were
  `path-unmapped`. `system`, `system_ex` and `preinst` map onto the decrypted
  4.03 tree, longest name first.
- **The firmware oracle was permanently red** on `strlen`, which lives in a
  module with 4 syscall byte sites and can never be differentially executed.
  NOT_GATEABLE and INCONCLUSIVE no longer block; DIVERGENCE, harness fault and
  **coverage loss** do, against a committed floor of 14 scored cases. Verified
  both directions.
- **Superliminal's corpus baseline was scoring failures as progress.** LEVEL0
  and LEVEL1 matched only the loader's own IO-FAIL lines: Unity probes for
  `.resG`/`.res` sidecars that ship in no build of the game. The title has never
  opened a single Media file. Baseline re-recorded at SPLASH + GUESTFRAME.

**Negative result, measured:** Astro's eight missing typefaces are NOT the
blocker. A run with all eight substituted from firmware reached identical
milestones to one without: LOGO t+76s, TITLE t+168s, WORLDMAP t+246s, device
loss t+269s. The substitution is opt-in behind `SHARPEMU_FONT_SUBSTITUTE=1` and
marked TODO(urgent); it is the wrong typeface and must not be on by default.
Eight fonts including `SIE-ShinGoPr6N-Heavy` exist in no tree we hold.

**Next concrete action.** Astro still dies at the worldmap with
`Vulkan device lost` on an offscreen pass (`ps=0x5008F1400`, mrt=2,
textures=41, vertices=4164). Ruled out so far: rwlock starvation (traced),
assets (`.odx` sweep is normal probing), GPU cost (`gpu_render_ms` flat),
fonts (above). Not yet examined: the PS5 delivers assets straight into
GPU-visible memory, and the firmware modules behind that are present and
mineable, `libSceAgcDriver`, `AgcCompositor`, `libSceZlib`. Start by listing
which of their exports Astro imports and which of those are in the 814-LIE
census bucket.
## REFUTED: THE RWLOCK IS NOT ASTRO'S BLOCKER (2026-07-28, traced)

A worker added a real per-rwlock diagnostic and traced `pthread_rwlock#1C`
through a live worldmap boot. The starvation reading is false:

- **No read lock ever blocked `OdxAsyncLoader`**: every relevant line showed
  `readers_total=0` and `read_holders=[]`. Neither reader starvation nor the
  survey's `CompatWriterTotalCount` latch is occurring.
- The transient holder at each block was `ProductNextLoad_ATQT`, one compat
  writer. It released, the scheduler granted the lock to `OdxAsyncLoader`, and
  `OdxAsyncLoader` released it.
- After worldmap: **36 writer blocks, 36 wake-acquires, 36 releases, no unmatched
  wait**, and `OdxAsyncLoader` went on to perform **1,251 further compat-writer
  acquire/release pairs**.

The loader is not starved; 36 blocks against 1,251 completed pairs is about 3%
and is ordinary contention. No lock semantics were changed, which was the right
call: altering grant or ownership to fix an unobserved lost wake would be
speculative and could corrupt mutual exclusion.

**The error was mine and it repeats a rule in `/goal`: a count is not a finding
until you know the healthy count.** Thirty blocks looked like starvation only
because nobody had measured the healthy block rate.

Kept: `SHARPEMU_LOG_PTHREAD_RWLOCK_FILTER`
(`KernelPthreadExtendedCompatExports.cs:41`) takes a guest address, a wake key
like `#1C`, or `*`, and prints acquire, release, block, wake request,
rejection and grant with thread names, `ReaderTotalCount`, the named writer,
`WaitingWriters` and named holders. Formatter at `:1963-2056`.

**Worldmap still never prints `StartLevel`. The cause is open. Do not re-chase
the rwlock.**
## THIRD FIRMWARE TREE FOUND: games/3.02 (2026-07-28)

Only 4.03 and 9.00 were ever surveyed. `games/3.02` is not in a versioned
layout and was missed: 842 files, 513.6 MB, with 527 `.sprx`, 275 `.c`,
25 `.elf`, 15 `.self`, and subdirs `known_pairs/` and `Stub call library/`.

**`Stub call library/` carries 39,158 exported symbol names** across 275
libraries, generated by Sony's genstub.py as jump thunks. Of those, **16,652 are
mangled C++** (`_Z...`) and **10,952 are `sce*`**. We register 4,255 exports
in total.

Two uses, both boot-free and both larger than anything currently queued:

1. **Signatures for free.** Mangled C++ names decode to full parameter and
   return types. `conformance-framework.md` planned to build that table by
   walking 906 SDK headers with libclang; this is broader, version-matched to
   3.02, and needs no headers.
2. **Name the unknown NIDs.** NIDs are computable from symbol names, so 39,158
   names can be hashed and matched against unresolved imports and against the
   203 ABSENT census exports we register but 4.03 does not export.

`known_pairs/` holds `.elf` and `.self` of the same binary
(`decid_update`, `first_img_writer`), which is a decrypted/encrypted pair and
the direct way to learn the SELF container layout rather than inferring it.

Unchecked: whether 3.02's `.sprx` are cleartext, how its module set differs
from 4.03 and 9.00, and whether the stubs carry NIDs directly (a grep for
`sprx_dlsym("` found none, so NIDs likely must be computed).
## ASTRO MENU: THE LOADER IS STARVED ON AN RWLOCK (2026-07-28, measured)

Run `20260728-071927-mutexowner`. Worldmap document loads at line 686140, then:

- `Guest thread 'OdxAsyncLoader' state=Blocked reason=pthread_rwlock_wrlock` x30,
  each followed by `Pumping ... reason=wake resume=0x0000000800010E31`, the same
  resume address every time.
- `guest_threads.wake key=pthread_rwlock#1C count=34`.
- The main thread runs 492 `pthread_cond_broadcast` / `wait-exit` pairs with
  `waiters=0` and a climbing epoch. That is it waiting for the loader.

**The worldmap menu never starts because its .odx asset loader cannot acquire a
write lock.** Not missing assets: the earlier `.odx` miss counts are normal
probing, since the dump ships 586 `.odxb` and only 2 `.odx`.

**This is ours to fix.** `src/SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs`:
`PthreadRwlockState` at line 120 carries `ReaderTotalCount`, `WriterThreadId`
and `WaitingWriters`; `scePthreadRwlockWrlock` is at line 1172. Check whether a
continuous stream of readers starves a queued writer. FreeBSD blocks new readers once
a writer is waiting, so the question is whether the read path honours `WaitingWriters`.
`fix_storm.py` already flags the sibling `KernelPthreadCompatExports.cs` with 5
commits, so under P6 this warrants a contract review rather than a sixth point fix.

Guest addresses move between runs (the cond was `0x31560B0F0` once and
`0x31560B670` the next). Match on behaviour, never on the address.
## SUPERLIMINAL GATE: THE cctor-THREW READING IS WRONG (2026-07-28, measured)

Probe run `20260728-065020-psnprobe` on master, `SHARPEMU_PS5MANAGER_PROBE=observe`:
`CurrentState=1`, `PsnInitialized=0`, `PsnInitResult=0`; subsystem singletons
NULL with `b12F=1, dE0=0, runs_init=1`.

Decode `runs_init` before using it. Per `Ps5ManagerStateProbe.cs:63-72` the
IL2CPP guard is `test byte [klass+0x12F],2` / `cmp dword [klass+0xE0],0` and the
initializer runs only when the bit is set and the word is zero. That is our state,
so **those classes are ready to initialize and have not been touched**. They did not
initialize and fail.

So "Main::Initialize throws at the first null subsystem" is wrong: that path ends in
`ud2` and the title runs forever instead. It never got there. The NULL singletons
are a symptom of the coroutine being suspended earlier.

Corroboration: 45 guest threads scheduled, all Unity workers, **zero PSN or NP
threads**. PSNCore.prx loads with 2,435 imports and **0 unresolved**, 0 NOT_FOUND in
the run, and PrxInitialize succeeds. The plugin is healthy and is simply never told
to start its subsystems.

Rejected on evidence: one thread parks forever on `cond=0x100000BA0`
(`signal_epoch=0`) and had a queued `type=0x1E` (SIGUSR1) exception earlier. The
GC-signal-never-delivered deadlock that suggests **is already fixed** in
`KernelPthreadCompatExports.cs:1927-1952`, modelled on Kyty, and the stall reports
`pending_exception=False`. Do not re-implement it.

**Next:** find what `<Initialize>d__84::MoveNext` awaits between PrxInitialize
succeeding and the nine subsystem starts. Reading its state field per frame would say
whether it polls or parks. Awake only starts it when state==0, so there is no retry,
which is why the title screen is permanent.
## SUPERLIMINAL: KYTY IS AT PARITY WITH US, NOT AHEAD (2026-07-28)

Ran the Kyty 0.2.2 build on Superliminal for 130 s, 2,911,266 printf lines.
Two assumptions died.

**Kyty does not implement PSN, it stubs it.** It loads the game's own
`PSNCore.prx` and patches the `PSNCommon_v1` imports to stubs, and logs zero
`sceNp*` symbols all run. So the long-planned "diff Kyty's PSN init against our
census" has nothing to diff, and PSN is not what holds this title for us either.

**Kyty reached level0 and level1 and never level2** in that run, which is exactly
where we stop. We are at parity. The older note that Kyty saw level2 once in
three runs makes that run the anomaly.

So the question is not which HLE call is missing. It is what condition or input
the game waits for at level1, and Kyty cannot answer it because it stops in the
same place. Note a naive `SHARPEMU_PAD_AUTO_PRESS=1` was already measured
delivering real Cross and Options presses without advancing the title.
## ASTRO IS STUCK, AND THE GPU WORK IS A SYMPTOM (2026-07-28)

Read the engine output before optimising. Astro loads the worldmap
LevelDocument at log line 74398 and then never emits `StartLevel` or
`Level has started` for it, unlike ps_logo and title which both do. The
remaining 9,008 lines of an 83,406-line log are 78 failed stats on worldmap
`.anim` assets plus `pthread_cond_wait`. The 525 ms pass drawn 104 times is
the loading screen it renders while blocked; the device loss follows from that.

The dump has `data/` and 55,756 `.anim` files, the exact
`worldmap/cinematics/world1_unlock/anim` directory exists, and the `_1`
suffix convention is real (841 such files). But the requested GUIDs are absent
in every form. One request is malformed: `stat guest='/app0/.anim'`, an empty
filename.

**We do NOT derive these paths, checked.** There is no `.anim` anywhere in
`src/**`, and the IO-FAIL line (`KernelFileTraceLog.cs:68`) prints the guest's
path verbatim. So the GAME built `/app0/.anim` itself, which means it read an
empty string from data we handed it. That is the plausible-stub failure class:
a success-returning stub writes zeros, the guest builds a name out of them, and
the name is garbage. The 814-entry LIE bucket from the stub census is the
population to search.

**That boot is done** (`20260728-005358-filetrace`) and it narrowed things twice.

`SHARPEMU_LOG_FILE=1` does **not** enable success tracing: 783 IO-FAIL lines and
exactly one other `guest=` line. Do not spend another boot on that flag; find or
add the real switch for `KernelFileTraceLog.Verbose`.

More useful: **the game performs no file I/O at all between
`LevelDocument Loaded: worldmap` (line 70311) and the first `.anim` failure
(line 71524)**, only two mutex locks. The bad names are not read from a file at
that moment, they are derived from the worldmap document already parsed in
memory. So the defect is in what we returned while that document loaded, or the
assets really are absent.

Failure population, 783 stats: `.odx` 335, `.odxb` 125, no extension 96,
`.anim` 92, `.xml` 88, `.json` 38, with 765 under `app0/data/prein`. Only two
have the empty-name shape, so the empty name is rare and the bulk is a broad
probe.

**CORRECTED, same session: much of this IS benign probing.** The biggest bucket,
335 missing `.odx`, is the game sweeping for a source form the dump does not
ship: `nx_ui_worldmap_bot_icon.odx`, then `~~0`, `~~3`, `~~4`, `~~5`. The
directory holds 586 `.odxb` files and the entire dump contains **2** `.odx`
files. Missing `.odx` is the normal state.

**So the per-level counts below are not evidence of a bug** and the inference
drawn from them was wrong; worldmap may just have more assets to probe. What
still stands is the behaviour, not the counts: worldmap's LevelDocument loads,
`StartLevel` never fires, no file I/O happens in the window, and the guest sits
in `pthread_cond_wait`. **Find what it waits on. Do not count failed stats.**

**It is a spin, not a deadlock (run `20260728-010908-conds`).**
`SHARPEMU_LOG_PTHREAD_CONDS=1` works. After worldmap loads, one condition
variable takes ~1,664 operations: `cond=0x31560B0F0`, `mutex=0x3142F4DA0`,
broadcast with **`waiters=0`** every time and a monotonically climbing epoch
(`0x547`, `0x548`, `0x549`...), interleaved with `pthread_cond_wait-exit`. The
engine broadcasts into an empty wait set and immediately re-waits. That is a
poll loop on a predicate that never becomes true, the same shape as the
Superliminal PreloadManager stall, and not a lost wakeup.

**Next: find the predicate.** Dump the guest word the loop tests between
`wait-exit` and the next wait; `SHARPEMU_LOG_SEMA_DEREF` is the precedent for
printing guest memory beside a trace. Then identify which HLE call feeds it.

Analysis trap worth stating: do not classify these lines by substring, because
`waiters=0` contains "wait" and broadcasts get miscounted as waits. Match the
event name before the colon.

Tooling, checked so the next session does not repeat it:
`SHARPEMU_LOG_THREAD_STATE_MS=20000` produced **zero** output despite existing in
the source, and `SHARPEMU_LOG_FILE=1` logs only failures. **Start with
`SHARPEMU_LOG_PTHREAD_CONDS`**, which names the condition variable a thread
waits on. Others available: `SHARPEMU_SAMPLE_STALLED_THREADS`,
`SHARPEMU_LOG_GUEST_THREAD_SNAPSHOTS`, `SHARPEMU_LOG_GUEST_THREADS`,
`SHARPEMU_LOG_PTHREADS`, `SHARPEMU_LOG_PTHREAD_MUTEX_FILTER=<addr>`. The
Superliminal precedent is the template: `SHARPEMU_LOG_SEMA=1` pinned that stall
to one semaphore, named who signalled it and from where, and proved the waiter
was never woken again.

**(superseded, wrong inference)** Distinct failing paths per level:
**worldmap 143**, title_controller_ship 11 (and that level starts fine), each
`ui_*` 4. Worldmap misses 13 times more than a level that works, and **all 143
of its directories exist on disk with files in them**. So the dump carries
worldmap's content structure and the game is asking for specific filenames
inside populated directories and missing. That points at the game deriving
wrong names from data we supplied, not at absent content. Chase the derivation.
# Methodology execution tracker

The live work order. Sessions resume from here (`/goal`); update statuses in
place. Statuses: TODO / BUILDING / DONE / BLOCKED.

Derived from `emulator-methodology.md` section 5 and `conformance-framework.md`
Phase 0, then revised 2026-07-27 after reading the Bun Zig-to-Rust rewrite.

## The correction that reorders everything

Bun's rewrite went fast because the gate existed before the work: a TypeScript
suite with a million assertions, authored independently of the code being
generated, which the generator could not rationalize away. We have no such
thing. Our 1,678 C# tests are written by the same agents that write the code,
encoding the same assumptions, which is exactly how invented struct blobs
shipped green. **Passing them proves self-consistency, not conformance.**

There is no Sony test suite and nobody will hand us one. So the gate has to be
manufactured, and manufacturing it IS the critical path. Candidate oracles,
ranked by independence from us:

| # | Oracle | Independent because | Needs |
|---|---|---|---|
| 1 | Real-hardware payload matrix | Sony's kernel authors the answers, we only author the questions | console payload execution (unconfirmed) |
| 2 | Self-differential vs 565 cleartext 4.03 modules | the real implementation, on disk | nothing we lack |
| 3 | Sony ISA pseudocode as an executable reference | Sony wrote the semantics | nothing we lack |
| 4 | Guest engine asserts + milestone corpus | Team Asobi wrote the assertion | nothing we lack |

Oracles 2 and 3 need nothing we do not already have on disk, so they are Track
0. Oracle 1 is promoted the moment console access is confirmed. Oracle 4 is the
merge-time regression detector, not a conformance gate.

## Standing loop rules (baked into every loop prompt)

The unit of work is Bun's topology: **1 implementer, 2 adversarial reviewers in
split contexts seeing only the diff and told to assume it is wrong, 1 fixer.**
Our hand-rolled n=1 of this on the isa-vop3 branch caught 3 real MAJORs that
compiled and looked plausible.

- Fail closed. Never substitute zero or identity for an unknown value.
- A paragraph-long comment justifying a workaround means the code is wrong
  (Bun's rule, verbatim). Fix the code, not the comment.
- Fixes stay generic; the game name lives in the commit message.
- Every non-obvious value carries provenance: EXTRACTED / DIFFERENTIAL /
  ASSUMED.
- SPDX headers, no em dashes, no AI traces.
- **Meta-rule: when a loop produces garbage, fix the loop, not the output.**
  Edit prompts, not generated code.

## NEXT ITEM, fully specified and boot-free to start

Astro's device loss is caused by one offscreen pass costing 525 ms per draw,
104 draws, which pins a virtualised GPU until the host resets it. Vulkan
timestamps (`SHARPEMU_LOG_GPU_TIMESTAMP=1`, landed `438bfc7`) split it:
render 260 ms, **post 264 ms**, and the post phase contains only a
`ColorAttachmentOptimal -> ShaderReadOnlyOptimal` barrier on the two colour
targets (`VulkanVideoPresenter.cs:12177-12226`). That is a full-surface DCC
decompress charged once per draw, with a matching re-compress on the next
render, which is why the two halves are nearly equal and why the cost does not
move with vertex count.

**DONE (`4720dbd`) and it was not enough.** `gpu_post_ms` 264.4 -> 0.00 and
`gpu_total_ms` 525.4 -> 260.95, but `submit_to_observed_ms` stayed at 525.6,
**unchanged**. The reason is now measured: `host_execute_ms` is **530 ms per
draw**. The host offscreen-execute path, not the GPU, sets the duration of this
pass. Before the fix the GPU and host costs were coincidentally equal and
overlapped, which is why wall clock tracked GPU time and made "this is real GPU
execution" look established. It was not.

**NEXT: profile the host path.** The pass is 1920x1080, `mrt=2`, 41 textures.
Suspects are per-draw descriptor-set rebuilds for those 41 textures, pipeline
re-creation or re-hashing, texture re-upload, or a linear scan over guest images
per binding. `host_execute_ms` is the number to move; if it falls, wall clock
falls and the GPU stops being pinned.

Original direction, now completed: keep the targets in `ColorAttachmentOptimal`
across consecutive draws and transition lazily. Eliminated already, do not re-chase: the
scalar-read theory (fixed, correct, changed nothing), a driver memory fault
(`No fault detected`), attribution by adjacency, and host submit overhead
(`submit_to_observed_ms` tracks `gpu_total_ms`).

## RESOLVED: Astro boots past its logo movie again

2026-07-27. `artifacts/bin` held a hand-placed, untracked `ffmpeg.exe` and
`ffprobe.exe`. They were deleted during a mixed-output investigation and nothing
in the repo replaces them: the fetched ffmpeg package ships shared libraries
only, and the publish targets copy to `artifacts/publish`, not `artifacts/bin`.

Without them `sceAvPlayerAddSource` fails, the title asserts at
`VideoPlayerOrbis.cpp:278`, and the `ps_logo` level never ends, so TITLE and
WORLDMAP are unreachable. **Every boot-based measurement is blocked**, including
the whole device-loss investigation and the corpus gate.

Restore by putting both executables in
`artifacts\bin\Release\net10.0\win-x64\` or setting `SHARPEMU_FFMPEG_PATH`.
Proper fix afterwards: `FfmpegNativeBinkFrameSource` already uses the ffmpeg DLLs
in-process via FFmpeg.AutoGen, so the AvPlayer path should drop its
external-process dependency and this cannot recur.

**Do not bulk-delete `artifacts/`.** It is gitignored and holds tooling nothing
rebuilds.

## Where we stand against the definition of done

`/goal` section 6 lists six conditions. As of 2026-07-27, none are fully met.

| # | Condition | State |
|---|---|---|
| 1 | `premerge.py` green on master | **NO, and it now fails for a better reason.** Build is 0-warning and the suite is 1860 green, but the newly gating oracle fails on 9 firmware divergences, and the corpus step cannot run at all until ffmpeg is restored |
| 2 | Two independent oracles operational **and gating merges** | **HALF, and the half that exists now bites.** T0.6 is wired into `premerge.py` as a gating step (`43b631e`) and **fails master on 9 real divergences**. T0.5 still has a prototype only |
| 3 | Q1, Q2, Q4 drained | **NO.** Q2 wave 1 done; Q1 re-aimed off f16; Q4 not started |
| 4 | Zero `SHARPEMU_ASTRO_*` flags | **NO.** Not started |
| 5 | Both Track 2 investigations resolved | **NO.** Not started |
| 6 | Corpus baseline advanced and reproducible | **NO.** Wave 1 boot in progress; baseline still the 2026-07-27 recording |

The nearest condition is 2: wiring `scripts/fw_oracle_gate.py` into `premerge.py`
turns a working oracle into a gating one. The blocker is that case files are
hand-authored from disassembly, so a case generator is the real prerequisite.

## A warning the oracle audit raised about our own census

The T0.6 harness audit found that **a dispatch which never reached the firmware
was indistinguishable from one that ran and returned zero**, and it fixed that.
The same confusion is the exact shape of `stub_census.py`'s **VERIFIED NO-OP**
bucket: "firmware body provably does nothing and returns the constant we
return". If any of those verdicts came from a body that never executed rather
than one that executed and did nothing, the bucket is overstated and we have
been counting hallucinated stubs as verified. Re-derive that bucket through the
oracle before trusting it, and do not gate on no-new-LIE until it is re-derived.

## Track 0 - manufacture the oracles (critical path)

### T0.1 Milestone corpus gate - DONE 2026-07-27
2026-07-27: built and baselined from real boots at `ddaa2db`. Astro reaches
LOGO/TITLE/WORLDMAP then hits the DEVICELOST anti-milestone at t+298s (device
losses are NOT at zero on current master; the baseline records that reality).
Superliminal reaches LEVEL0/SPLASH/GUESTFRAME/LEVEL1, no device loss.
`scripts/corpus_gate.py --rescore <run-dir>` re-scores an existing log without
booting; scoring is log-content only because the emulator can exit 0 on a lost
Vulkan device. Regression and anti-milestone paths verified: exit 1 on a
truncated log and on an injected device-lost line.
Regression detector, not a conformance gate; scoped accordingly. Boots every
corpus title headless, scores guest-observable log milestones, fails on any
previously-reached milestone now missed. `scripts/corpus_gate.py`,
`corpus/manifest.json`, `corpus/baseline.json` recorded from real runs.
Guest-observable milestones are implementation-independent, which is the one
property of Bun's suite we can copy today.

### T0.2 Pre-merge gate wrapper - DONE 2026-07-27
`scripts/premerge.py`: 0-warning Release build, tests, corpus gate, optional
stub-census report (`--with-census`, non-gating for now), plus a non-gating
fix-storm report. An excluded-from-CI framework is theater. Verified live
2026-07-27 at `ddaa2db` with `--skip-corpus`: build PASS (0 warnings), test
PASS (1815/1815), fix-storm report printed, exit 0. The corpus step calls
`corpus_gate.py` in boot mode (about 12 minutes for both titles); that exact
invocation path is the one part not yet exercised end-to-end, but the gate's
boot mode is the path that recorded the baseline, so the seam is proven on
both sides. Census gating (no-new-LIE) is the remaining P2 follow-up.

### T0.3 Get the suite green and non-flaky - TODO, and bigger than recorded
2026-07-27: **the zero-warning policy has been vacuous.** `dotnet build` is
incremental, so unchanged projects never recompile and their analyzer warnings
are never emitted. Measured on one tree minutes apart: cold build 15 warnings,
immediate rebuild 0. `premerge.py` now passes `--no-incremental` (`796da76`),
which makes master's 15 pre-existing warnings visible for the first time:
`DirectExecutionBackend.cs` and `Ngs2Exports.cs` 10 lines each, then
`SaveDataExports.cs`, `AjmExports.cs`, `MessengerCompatExports.cs`,
`AgcExports.cs`. Codes are CS0649, CS0169, CS0414, CA2014, CS8602. Clearing
these is the real content of this item. Do not weaken the gate to make it pass.

2026-07-27 measurement at `ddaa2db`: full Release suite is green, 1815/1815
across all five test projects; the 3 previously-known failures are gone.
Remaining scope is the flake sources, not red tests.
Hygiene, explicitly demoted from "the gate": the equeue delete bug, the
allocator over-allocation leak, the timing flakes. Necessary so a red gate means something; not sufficient to be a
conformance oracle.

### T0.4 ISA contract table - DONE 2026-07-27, `908c2cd`
542 instruction names from the table of contents; **275 carry real bodies, 267
exist only as contents entries** because those chapters were never captured in
our PDF set. That is a source gap, not a parse failure. Over the 275: summaries
and descriptions 100%, restrictions 99.6%, encoding family 99.3%, but full
`Operation Details` pseudocode only 39 rows, because most instructions state
their semantics in the one-line summary instead.

**Opcode numbers are definitively not in the source.** Zero occurrences of the
word "opcode" in 4.57 MB, and the encoding diagrams are images that flatten to
`-----dword0-----`. Numbering stays inference from AMD's table and is
deliberately absent from the contract table.

The PDFs are not one instruction per file: they are duplicate captures of the
same chapters, 4,281 entry instances collapsing to ~275 unique, with content
displaced into page-bottom float regions so a naive read attributes a summary to
the wrong instruction. `docs/isa-contract-table.md` documents seven hazard
classes and the four near-miss mechanisms that were caught in validation.

**The table itself is gitignored.** It reproduces Sony SDK documentation
verbatim and this repository is public; the parser is tracked, the extraction is
regenerated locally.

### T0.5 ISA pseudocode as an executable reference - VIABLE, re-aimed
Verdict 2026-07-27: **build it, but not starting at f16.** A prototype grammar
parses 221 of 275 semantic rows verbatim, and a stdlib prototype interpreter
reading cells live from the table ran real instructions (`s_add_u32`,
`s_addc_u32`, `s_and_b32`, `v_bfe_u32`, `v_add_f32` over seven operand vectors,
plus harvested `s_cmp_<compareOp>_i32` variants). Production ceiling is roughly
250 of 275 rows expanding to ~429 concrete mnemonics, plus ~57 hand-written
intrinsics and a small wave-state model.

**This overturns the stated first slice.** `prospero-isa-gaps.md` ranks f16 VALU
(43 instructions) highest, but the f16 chapters were never captured, so there
are no f16 semantics to compile. First slice is SALU integer with SCC plus the
177 expanded compare mnemonics with EXEC masking. f16 needs a different source
before it can be either implemented against contract or differentially tested.

### T0.6 Self-differential firmware oracle - USABLE 2026-07-27, not wired in
`SharpEmu --fw-oracle --cases=<file.json>` maps a cleartext 4.03 module through
the ordinary loader, executes Sony's body under the native backend, runs our HLE
export against byte-identical guest state, and compares RAX plus every arena
byte. Game-free. Full write-up and limits in `docs/self-differential-oracle.md`.

**Measured on 4.03 `libSceAgc.sprx`, 17 cases over 5 case files.**
`sceAgcGetDataPacketPayloadAddress` 5/5 MATCH, the positive control that says the
harness works. `sceAgcDcbDrawIndexAuto` 5 DIVERGENCE: we emit a 7-dword private
`IT_NOP` where Sony emits a 3-dword `IT_DRAW_INDEX_AUTO`, we reject `modifier==0`
that Sony accepts, and we never dword-align the DCB cursor (new bug).
`sceAgcDriverValidateDcbRange` 4/4 DIVERGENCE: Sony dereferences rsi/rdx as
structures, so the "begin/end gpu-va" comment at `AgcExports.cs:3504` is wrong.
`libSceLibcInternal` is refused whole: 4 syscall byte sites, no guest syscall
handler exists. **None of these HLE bugs are fixed; `AgcExports.cs` is untouched.**

Adversarial audit found four defects in the harness itself, all fixed and each
one demonstrated firing: it verified 24 of 44 body bytes (a tamper past the
prefix produced four false MATCHes); a dispatch that never reached the firmware
was indistinguishable from one returning zero, aimed straight at the VERIFIED
NO-OP census bucket; case 17 killed the run and discarded the 16 verdicts before
it; and the containment canary was checked on one side only.

**READ A DIVERGENCE AS A QUESTION, NOT A CONVICTION.** Checked 2026-07-27: the
`sceAgcDcbDrawIndexAuto` "we emit `IT_NOP` where Sony emits `IT_DRAW_INDEX_AUTO`"
verdict is **not a dropped draw**. Our own DCB parser recognises the private
`ItNop + RDrawIndexAuto` pair at `AgcExports.cs:4247-4260` and calls
`TryTranslateGuestDraw`, the same internal-protocol trick as the private `RStall`
0x1D. Reading that verdict as "draws never happen" was one inference away and
would have been wrong. What the divergence does cost is real but narrower: guest
code that re-reads its own DCB sees a NOP, a hardware-captured command buffer
will not match, and we spend 7 dwords where Sony spends 3, which compounds with
the cursor-alignment bug. The other two findings on that function, rejecting
`modifier==0` that Sony accepts and never dword-aligning the cursor, are
unambiguous bugs. Before acting on any oracle verdict, check whether our
architecture consumes the difference elsewhere.

**Remaining before this is a merge gate.** No generator, so every case file is
hand-authored from a disassembly; six integer arguments only, no float or SSE
path; module globals outside the observation window; `premerge.py` does not call
`scripts/fw_oracle_gate.py`. Cost is not the obstacle: 3.9 s fixed plus 0.42 s
per case, about 26 minutes for 4,108 NIDs at 5 cases across 16 cores.

### T0.7 Hardware payload matrix - BLOCKED on one fact
`inspiration/ps5-payload` is cloned. If console payload execution is available,
this is the highest-value item in the project and it reorders the plan. Awaiting
confirmation; everything else proceeds regardless.

## Track 1 wave 1 - DONE 2026-07-27, merged at `4ca38f7`

Four codex lanes (`gpt-5.6-sol`, xhigh) on Q2 audit findings, disjoint file
ownership, worktree-isolated. **Zero merge conflicts.** Suite 1815 -> 1858, all
green, and the wave introduced no new warnings. Q2 needed no new oracle: every
finding already carried a Sony citation and a `file:line`, and the shader tests
need no game run, so it was drainable before T0.4 finished.

What landed: MIMG's eighth opcode bit; `ds_append`/`ds_ordered_count` no longer
aliasing the permute pair; MTBUF instruction format; wave32 lane masks sized by
wave width; NaN pinned through min/max/med3/clamp; the mad family unfused with
`NoContraction` and the legacy family forcing `0.0 * x == 0.0`; barriers
covering uniform and image memory; LDS at the full 64 KiB with range checks
returning zero on invalid read; NGG connectivity proven before a shader is
called pass-through; Metal drops made loud.

**The protocol worked without supervision.** Every `ASSUMED` tag was verified by
hand afterwards and each one was accurate: `0x3E`, `0x3F` and `0xB2` are
corroborated in reference sources, `0xB3` genuinely is not, and the worker
tagged exactly that one. Where Sony's documents establish a field width but not
its numeric values, the decoder lane implemented the width and **refused the
mnemonics by name** rather than importing them from AMD's table. Two lanes
independently derived the same DPP bank formula from `38.pdf p6`.

Cross-lane work was reported rather than grabbed: the decoder change invalidated
`Gen5WaveWidthTests`, which sat outside its ownership, so it said so and left it.
The integrator fixed it on the integration branch, which is where it belonged.

Method notes for the next wave:
- Gate against the worktree's **true base commit**, never `master`; master
  advances underneath and the diff will blame workers for the integrator's own
  commits.
- Worker briefs must name the branch exactly (`isa/ngg-classifier`, not
  `isa/ngg`), or the merge silently skips a lane.
- Junction `games` and `inspiration` in, but **unlink them with `cmd /c rmdir`
  before deleting a worktree**; a recursive delete can follow the reparse point
  into the real dumps.
- Metal is out of scope from wave 2 onward: the target is Windows and Vulkan.

## Track 1 - drain the queues with the loop

Only after T0.1 plus the relevant oracle exists. Bun did 3 files before 1,448:
**run 3 items through the full loop first**, and only scale if the trial holds.
Rank corpus-first, instructions actually observed in Astro/Superliminal shaders
before the long tail, so loop output stays immediately testable against boots.

- **Q1 ISA gaps:** ~240 instructions, each shipping its own ground-truth
  contract from T0.4. Highest value per `prospero-isa-gaps.md`: f16 VALU (43),
  `s_getreg`/`s_setreg`, `s_movrels*` relative addressing, FLAT/SCRATCH.
- **Q2 ISA audit findings:** ~30 remaining, each already carrying a doc
  citation and file:line.
- **Q3 NID stubs:** 4,108, against 565 cleartext modules; the nid-swarm skill
  already encodes the contract gate. Drain via the codex-workers pattern.

Worktree isolation is mandatory for loop shards. Two dotnet builds on one tree
corrupt each other; we learned that hazard on our own shared tree.

## Track 2 - serial investigations (spend the capability)

Not loop-shaped. These are sequential evidence chains where each measurement
changes the next question, so they stay hand-driven while the loops build
capability in the background.

- **Astro:** the four indirect-dispatch argument producers never run
  (`astro-bot-boot.md`, last three sections). Disassemble the four compute
  shaders; find the producer. Verify GPU writeback reaches presented guest
  images before assuming any fix would be visible.
- **Superliminal:** where does `Unity.PSN.PS5.Main::Initialize` block inside
  `<Initialize>d__84::MoveNext`? Kyty boots this title; diff its PSN-init call
  sequence against our import census.

## Deferred from the earlier ordering

- **Stub-debt lifecycle (P2):** static census exists; the retirement mechanism
  (no-new-LIE gate, then a date after which unverified success-stubs on
  corpus-touched surfaces fail loudly) lands with T0.2/T0.6.
- **Fix-storm trigger (P6):** DONE 2026-07-27. `scripts/fix_storm.py` slides a
  21-day window per src/ file over the full commit graph, excluding pure
  renames, generated files, and `[docs]` commits; threshold 5 flags exactly
  the three known storms at current HEAD (DirectExecutionBackend.cs 7,
  AgcExports.cs 6, KernelPthreadCompatExports.cs 5) and nothing else.
  Report-only (`--json` for tooling); premerge runs it non-gating so storms
  are visible at merge time. Caveat: repo history spans only 2 days so the
  window cutoff is untested against multi-week history; rename chains with
  modifications count per-name.
- **File-I/O instrumentation (P4):** known-dark subsystem, close it as a
  permanent env-gated facility before the next Astro push.
- **Flag retirement (P3, P11):** each surviving `SHARPEMU_ASTRO_*` flag gets
  the 9e7df6ae treatment: general mechanism lands, flag deleted, commit cites
  the flag it kills.
