# Astro Bot: AGC / VideoOut contract conformance vs decrypted 4.03

> **Historical conformance snapshot.** Several registrations and semantics
> listed as missing below have since landed. Current source registers all 71
> catalogued Astro AGC imports, but registration is not proof of equivalent
> behavior. Use `docs/source-alignment-audit.md` for current coverage and keep
> this document for its versioned firmware-body evidence.

Read-only audit, 2026-07-27, against source snapshot `8b860da` (no source change since).
Ground truth is the cleartext firmware in `games/PS5_4.03_reconstructed`. The emulator was
not run for this audit, so every causal claim below is labelled UNVERIFIED and only the
byte-level differences are asserted as fact.

Update, 2026-07-31: item 2 is fixed on the current working line. Complete PS5
4.03 and 9.00 bodies prove `qj7QZpgr9Uw` is the legacy context-state contract;
it now routes to the same implementation as named NID `HabmgqPwPw0`. The
original snapshot description remains below as historical evidence.

Companion documents: `astro-bot-boot.md` (the runtime measurements and what has been
refuted), `nid-firmware-audit.md` (the generated NID/library census), `ps5-shader-isa-audit.md`.

## The headline

**Astro's problem is not an unresolved NID.** The measured `4221/4221` bound-stub result
stands; nothing in a full import census contradicts it. What the census did find is a set of
bound exports whose behaviour differs materially from the firmware they stand in for, and
several of those sit directly on command construction, synchronisation, queue submission and
flip ordering - which is exactly the region the black screen lives in.

Import census (SELF/ELF dynamic symbol tables and import relocations, resolved through the
physical `PT_LOAD` mappings):

| Image | Unique undefined NIDs | Import relocations | `SysAbiExport` attribute matches |
|---|---:|---:|---:|
| `eboot.bin` | 1,733 | 2,913 | 710 |
| `libSceNpCppWebApi.prx` | 95 | 98 | 91 |
| `libc.prx` | 117 | 118 | 111 |
| union | **1,822** | **3,129** | **792** |

The eboot has 1,732 relocation-backed NIDs; the extra undefined symbol is the TLS symbol
`vNe1w4diLCs#__tls_get_addr`, which is why `scripts/astro_import_routing.tsv` has 1,732 rows.
A missing `SysAbiExport` attribute is **not** a missing import - LLE and other resolver paths
serve those. The TSV's `UNSERVED` column is stale (e.g. it flags `sceAgcDriverAgrSubmitDcb`,
which exists at `AgcExports.cs:3335-3383`); use its NID/name/library columns as a catalog only
and ignore its route status.

Astro's graphics import surface is 82 exports: 11 `libSceVideoOut`, 6 `libSceAgcDriver`,
65 `libSceAgc`. There is no standalone `libSceGpuQueue*` library - the queue contract reaches
us through `libSceAgcDriver` submissions plus kernel event queues.

## Ranked conformance defects

"Verified" means the difference is directly visible in both current source and the cited
firmware instructions. Static xrefs prove that code *references* an import, not that the
reported run executed that branch. Firmware citations give module vaddr; for these decrypted
`.sprx`, **file offset = vaddr + 0x4000**. Eboot addresses need `+0x800000000` at runtime.

### 1. `sceAgcDcbSetFlip` (`YUeqkyT7mEQ`) - 2 static xrefs, `0x464DC2` / `0x4A5AAB`

We allocate exactly 6 DWORDs and write a private `IT_NOP`/`RFlip` marker
(`AgcExports.cs:3209-3234`, confirmed by direct read). Firmware asks the driver for the
packet size, gets **0x40 DWORDs** (`libSceAgcDriver.sprx` vaddr `0x6D80`), reserves that many,
and emits a substantial stream (driver `0x6DA0-0x6FE8`, including `0xC0064900` at `0x6F18`).
Firmware also validates buffer index in `[-2,15]` and can fail.

This is deliberate: our DCB parser is a **semantic** parser, not a command processor, and it
recognises the private marker at `AgcExports.cs:4251-4266`. So the fix is not "emit firmware
bytes" - emitting them without a byte-accurate parser would delete our own flip path. Builder
and parser have to move together, or not at all.

### 2. `qj7QZpgr9Uw` - fixed legacy context-state route; 10 static xrefs (`0x48CC71`, `0x48CEEE`, `0x48D29C`, `0x48D542`, `0x48D9C7`, `0x48DBCE`, `0x48DF80`, `0x48E228`, `0x48E5E1`, `0x48E64A`)

The historical snapshot allocated one DWORD containing `0x80000000`, a no-op.
Firmware (`libSceAgc.sprx` vaddr `0x3320-0x35FD`) branches on its mode argument
over values `0..3`, allocates several packet sizes and emits active packets
with headers `0xC0012800` (`0x33DC`/`0x34B6`), `0xC0001200` (`0x3533`) and
conditionally `0xC0039F00` (`0x35C5`). PS5 9.00's named
`sceAgcDcbContextStateOp` body emits the same packet contract. Current source
routes both NIDs to the semantic implementation. The 2026-07-31 live run
recorded 6,532 push-clear and 6,532 pop calls, but its PrintWindow captures
remained guest-black; the conformance fix is verified without assigning it
black-frame causality.

### 3. `sceAgcDcbStallCommandBufferParser` (`u2T2DiA5hRI`) - 4 xrefs

We substitute a two-DWORD `IT_NOP`/`RZero` on the stated grounds that direct execution has no
independent command processor to stall (`AgcExports.cs:2466`ff). Firmware emits exactly two
DWORDs whose low DWORD is `0xC0004200` (`libSceAgc.sprx` `0x62B0-0x6330`, store at
`0x631B-0x6323`). The opcode `0x42` is deliberately not named here.

### 4. `sceAgcSuspendPoint` (`h9z6+0hEydk`) - 5 xrefs

We trace, read Unity diagnostic globals, set `RAX=0` and return success
(`AgcExports.cs:3624`ff), never calling the driver helper we already registered at
`AgcDriverRenderStateExports.cs:138-156`. Firmware (`libSceAgc.sprx` `0x8540-0x85B2`) returns
`0x8A6C002F` when disabled, otherwise copies 13 bytes into a descriptor and calls
`sceAgcDriverSuspendPointSubmit` (`QcmHLO2n7mk`). The driver (`0x1BD0`) reads descriptor `+0`,
`+8`, `+0xC`, does `lock xadd` on a sequence, fans out callbacks and returns the backend result.
A frame/suspend boundary is reported complete with no submission, no sequencing and no way to
propagate a backend failure.

### 5. `sceAgcInit` (`23LRUSvYu1M`) - 1 xref at `0x6F3B3BD`

We validate a non-null pointer and an accepted selector, then return success **without writing
the pointed-to DWORD** (`AgcExports.cs:695-706`, confirmed by direct read). Firmware
(`libSceAgc.sprx` `0x8500-0x8530` into worker `0x7720`) tests the retained output pointer and
writes a DWORD at `[r14]` (`0x7898-0x78A0`), and the worker allocates process/queue state with
error paths including `0x8A6C0004`. Astro loads `esi=13` and passes a writable `rdi`. This is
the cheapest item on the list: the guest asks for an output we never write.

### 6-8. Draw builders

- `sceAgcDcbDrawIndexAuto` (3 xrefs): we accept only modifier `0x40000000`, allocate 7 DWORDs,
  emit a private NOP marker (`AgcExports.cs:2093-2121`). Firmware allocates **3** and writes
  `0xC0012D00`, the vertex count and a derived initiator (`0x45D0-0x4685`, stores `0x4666-0x4673`).
- `sceAgcDcbDrawIndexOffset` (2 xrefs): our 5 DWORDs put the original count in both DWORD 1 and
  DWORD 3 and reduce flags to `flags & 0xE0000001` (`AgcExports.cs:3130-3157`). Firmware
  normalises a zero count to 1 for DWORD 1, keeps the original in DWORD 3, and derives DWORD 4
  from flag bits plus a global (`0x4770-0x483A`).
- `sceAgcDcbDrawIndexIndirect` (3 xrefs): we write `{header, dataOffset, 0, 0, raw modifier}`
  (`AgcExports.cs:2124-2148`). Firmware bit-extracts from the modifier at `0x4D81` onward and
  packs derived 64-bit fields plus a separate initiator.

Packet lengths are often right; the control fields are not.

### 9. `sceVideoOutRegisterBuffers2` (`rKBUtgRrtbk`) - 2 xrefs

**This is the one that connects to the black screen.** We step by `0x20` per entry but read only
qwords `+0x00` and `+0x08`, discard the second, and register only the first address
(`VideoOutExports.cs:1413-1422`, confirmed by direct read). Firmware's bookkeeping worker
(`libSceVideoOut.sprx` `0x17F79-0x18025`) iterates with the same `0x20` stride and reads qwords
`+0x00`, `+0x10` **and** `+0x08` (`0x17FC0-0x17FEB`). The wrapper also forces its return to 0
(`0xF7C7`); we return `setIndex` (`VideoOutExports.cs:1425`), dormant here because both call
sites pass `setIndex=0`.

What the omitted qwords *mean* is UNVERIFIED - do not call them DCC or right-eye fields on this
evidence. But see `astro-bot-boot.md`: the title never writes the two addresses we scan out, and
every render target it does write is somewhere else entirely. A per-entry dump is now wired in
(`videoout.register_buffer_entry`, all four qwords) and is the next experiment to run.

Related model gaps in the same function: compressed and uncompressed categories are presented
identically by choice (`:1377-1380`), slots force `AddressRight=0` (`:1764-1769`), and Attribute2
pitch is synthesised as width (`:1804-1823`).

### 10. Queue submission - `UglJIZjGssM` (4 xrefs), `AhGvpITrf4M` (1), `gSRnr79F8tQ` (5)

All three read only descriptor `+0`/`+8`, assign emulator queue state and **always return 0**
after enqueue (`AgcExports.cs:3267-3313,3335-3383,3423-3478`); AGR states outright that callback
fan-out is not modelled and ACB ignores byte `+0xC`. The firmware driver funnels SubmitDcb
(`0x27E0`), AGR (`0x27F0-0x2837`, returns `0x8A6D0003` with no context) and ACB (`0x2840-0x288C`)
into a common worker at `0x1890` that reads `+0/+8/+0xC`, stamps a sequence, calls registered
callbacks and returns callback/backend status. Forcing success here can hide a failed setup and
erases the ordering that the `dcb.graphics` device loss would have been visible in.

### 11-14. Lower priority

- `sceAgcGetRegisterDefaults2` / `...Internal`: we accept selectors 7, 8, 10, 13 and return one
  fixed allocation (`AgcExports.cs:12883-12943`). Firmware (`0x87F0`, `0x88B0`) jump-tables
  selectors `0..8`; anything above 8 takes hardware-dependent branches. Astro asks for 13, which
  is off the end of the 4.03 table. Version skew is plausible; returning one synthetic blob for
  every accepted version is not firmware behaviour either way.
- `sceVideoOutColorSettingsSetGamma_` writes 4 bytes and ignores size
  (`VideoOutExports.cs:452-476`); firmware accepts size `0xC` or `0x10` and writes the same float
  at offsets 0, 4 and 8 (`0x4C80-0x4CF9`). `sceVideoOutAdjustColor_` reads 4 bytes
  (`:479-506`); firmware validates size, copies 12 bytes and sets a fourth DWORD to `-1` for
  `0xC`, or copies all 16 for `0x10`, then touches device state (`0x4D00-0x4E46`). Astro passes
  size `0x10` to both.
- `sceVideoOutClose` removes the handle and returns 0 unconditionally
  (`VideoOutExports.cs:349-362`); firmware validates, checks resource state, waits and tears down
  (`0x10400-0x107B0`). Shutdown behaviour, not first-frame.
- `sce::Np::CppWebApi::Common::initialize` (`UYPxv8MIzGo`) is a declared stub returning 0
  (`NpCppWebApiExports.cs:9-22`). Astro ships the provider itself: `libSceNpCppWebApi.prx` vaddr
  `0x37B0` validates the `LibContext` path, calls worker `0x4780` with the output context and
  init params, propagates status and stores library state globally. A real defect, but the title
  already reaches worldmap, so it is not the graphics blocker.

### Unranked: success-returning exports with no 4.03 provider found

Contracts UNVERIFIED, so these are not ranked above firmware-proven findings, but none should
stay a silent success once its ABI is recovered:

| Export | Behaviour | xrefs |
|---|---|---:|
| `-KRzWekV120` `sceAgcDriverUnknown_KRzWekV120` | logs, returns success, no output or state (`AgcExports.cs:3592-3606`) | 1 |
| `dolOmWH+huQ` `sceAgcDriverValidateDcbRange` | writes 24 zero bytes, returns success (`:3481-3508`) | 1 |
| `fd5Bp5tGTgo` `sceAgcDriverSubmitDcbRange` | zeroes 24 output bytes, treats empty ranges as success, returns success regardless (`:3511-3561`) | 2 |

## Things that are already right - do not chase these

1. `sceVideoOutSetBufferAttribute2` matches byte layout: we clear `0x50` bytes and write tiling
   `+4`, zero `+8`, width/height `+0xC`/`+0x10`, option `+0x18`, pixel format `+0x20`, clear
   colour `+0x28`, DCC control `+0x30` (`VideoOutExports.cs:1268-1306`); firmware does the same
   at `0x30C0-0x30FD`.
2. `sceAgcDriverRegisterOwner` and `sceAgcDriverRegisterResource` correctly return the retail
   constant `0x8A6C9018` (`AgcExports.cs:13688-13698,3583-3590`); firmware `0x67A0`/`0x67B0` are
   constant-return thunks.
3. `sceAgcDebugRaiseException` returns `0x8A6D0003`, matching the driver chain
   (`AgcExports.cs:3386-3420`).

## Suggested order, if this list gets worked

1. Run the `videoout.register_buffer_entry` dump. It is one run and it either explains the black
   screen outright or removes finding 9 from the board. Do this before anything else here.
2. `sceAgcInit`: write the output DWORD and create the minimum state later calls consume. Keep
   selector-13 behaviour explicitly versioned rather than treating the 4.03 table as proof of a
   newer SDK layout.
3. Firmware-byte unit tests for `qj7QZpgr9Uw`, the stall packet, the three draw builders and the
   0x40-DWORD flip. **Change builder and parser as a pair** - byte-accurate emission without a
   byte-accurate parser loses the private markers our flip path depends on.
4. Route `sceAgcSuspendPoint` through the driver submission contract: descriptor `+0xC`, sequence
   advance, callbacks, real return.
5. Stop forcing success after parse/enqueue in the three Submit paths; preserve `+0xC` and
   propagate backend errors. This is a prerequisite for trusting any future device-loss
   attribution.
6. Only then revisit the `0x5008F0900` shader. Nothing here proves that shader is correct, and
   nothing here proves it is guilty.

## Limits

No runtime counts, screenshots or Vulkan validation back this audit. Static xrefs are not
execution proof. Firmware 4.03 is authoritative for the cited bodies, but Astro ships a newer SDK
and versioned structures or selectors can legitimately differ - every place that matters is
marked UNVERIFIED above. The audit exhaustively covers the 82 graphics imports; it does not claim
semantic equivalence for the other 1,740.
