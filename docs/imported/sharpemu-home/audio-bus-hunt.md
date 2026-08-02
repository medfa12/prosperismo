# The +0x2660 audio-bus hunt — firmware side

**Lane:** `descriptor` (analysis only; no source edited).
**Date:** 2026-07-25.
**Question:** what is supposed to fill `SoundManager singleton+0x2660`, the 0x28-stride
vector of named descriptors that the bus builder at guest `0x800DC0500` consumes and that
is always NULL (see `docs/astrobot-bringup.md` "The audio wall, traced end to end")?

**Answer up front:** *no firmware API can fill it.* Part 2 below is a **negative, and it is
solid**: across all 552 cleartext 4.03 modules there is no audio API that hands a caller a
list of named records, and Astro Bot's entire audio import surface (47 entry points) is
plain C, handle-based, and returns no strings at all. Part 1 followed the
`PortSetAttributes` descriptor to its terminus — it is the PCM mix path and dies there —
but it did produce a complete, previously unmapped firmware contract for
`sceAudioOut2PortSetAttributes`, which we currently model as 100% inert across ~21,000
calls per boot.

The one genuinely new structural result is in §3: the `+0x2660` element's string layout is
an **exact** match for the Dinkumware `std::string` that the PS5 SDK's own STL uses, which
means the record is built by C++ code compiled with the SDK toolchain — the game — and was
never going to arrive from a system module. That converts "probably game-side config" from
a guess into a measured inference, and it makes the file/asset trace the right next move.

---

## 0. Method and the exact scans behind every count

Ground truth: `games/PS5_4.03_reconstructed/filesystems/{system,system_ex,preinst}`.
Parsing mirrors `scripts/fw_exports.py` (PT_DYNAMIC, `DT_SCE_SYMTABSZ` 0x6100003F bound,
`NID#lib#mod` symbol names). Scratch tooling (throwaway, in the session scratchpad):
a module loader, a recursive-descent single-function disassembler, an `E8`/`E9` rel32
xref sweep over executable `PT_LOAD`s, and a transitive call-tree + reachable-string
collector.

Counts used below, each with its scan:

| Count | Scan |
|---|---|
| **552** modules parsed | every `*.sprx`/`*.elf`/`*.prx` under the three roots that parses as a cleartext ELF with PT_DYNAMIC |
| **295,447** export rows | one row per (module, exported NID) over those 552 modules |
| **47,402** rows name-resolved | `nid_of()` over the 154,458 names in `scripts/ps5_names.txt`, plus `scripts/nid_names.tsv` |
| **184 / 47 / 31 / 41 / 39 / 49 / 21** exports | libSceAudioOut / libSceNgs2 / libSceAudio3d / libSceAudioPropagation / libSceAudioSystem / libSceAudioIn / libSceSysmodule |
| **8** callers of `0x16b00` | rel32 sweep of libSceAudioOut `ph00` (`off=0x4000 va=0x0 filesz=0x452F2`, X) |
| **33** functions / **0** strings in the descriptor tree | transitive call tree from `0x25ab0` + `0x25f10`, collecting every rip-relative target that decodes as a ≥4-char printable NUL-terminated string |

`libSceAudioOut.sprx` `ph00`: `p_offset=0x4000 p_vaddr=0x0 p_filesz=0x452F2`, so
`.text file offset = vaddr + 0x4000`, as the lane brief states. All vaddrs below are
module-relative.

---

## 1. PART 1 — the `PortSetAttributes` descriptor, followed to its terminus

### 1.1 The lane brief's facts reproduce exactly

**EXTRACTED.** `sceAudioOut2PortSetAttributes` = `8XTArSPyWHk` @ `libSceAudioOut+0x411E0`,
size 3474. `sceAudioOut2LoPortGetState` = `xaZ3K60Wwz0` @ `+0x16B00`, size 671. The rel32
sweep finds exactly **8** direct calls to `0x16B00`, at the eight addresses named in the
brief. Their enclosing exports:

| Call site | Enclosing export |
|---|---|
| `0x02703` | `sceAudioOutGetPortState` (`+0x2630`, lib `libSceAudioOut`) |
| `0x27EBA`, `0x27EF4`, `0x27F2E`, `0x27F68` | `sceAudioOut2ContextAdvance` (`+0x27890`) |
| `0x41AF8` | `sceAudioOut2PortSetAttributes` (`+0x411E0`) |
| `0x42013`, `0x42157` | `sceAudioOut2PortGetState` (`+0x41F80`) |

### 1.2 Where the `rbp-0xb0` struct goes

**EXTRACTED.**

```
0x41AEE  lea  r14, [rbp - 0xb0]
0x41AF5  mov  rsi, r14
0x41AF8  call 0x16b00            ; sceAudioOut2LoPortGetState(loPortHandle, &state)
...
0x41EA4  lea  r9,  [rbp - 0xb0]
0x41EB2  call 0x25ab0
...
0x41EDA  lea  r9,  [rbp - 0xb0]
0x41EE8  call 0x25f10
```

`0x25AB0` and `0x25F10` each have exactly **one** caller (both inside `PortSetAttributes`)
and are near-identical twins. Each zeroes a 0x38-byte stack descriptor and stores the
state pointer into it:

```
0x25AEF  vmovups ymmword ptr [rsp + 0x38], ymm0     ; zero
0x25AF5  vmovups ymmword ptr [rsp + 0x20], ymm0     ; zero  -> descriptor base = rsp+0x20
0x25AFB  mov     qword ptr [rsp + 0x38], r9         ; descriptor +0x18 = &loPortState
0x25B0E  mov     dword ptr [rsp + 0x20], 2          ; descriptor +0x00 = kind
```

`0x25F10` is byte-for-byte the same shape with `mov dword [rsp+0x20], 1` at `0x25F6D` —
the two calls are *kind 2* then *kind 1* of the same operation.

`lea r10,[rsp+0x20]` is pushed as the 8th argument to `0x2B140` (`0x25B66`) or `0x2AD20`
(`0x25B85`), selected by `0x33BA0(ctx, portId)`. Those fill four small parallel arrays at
`[rsp+0x90/0x98/0xa0/0xb0]` with **1** entry (`r15=1`, `0x25B71`) or **2** (`r15=2`,
`0x25B90`), each entry `{u32, u32, void*, void*}`. The loop at `0x25D0C..0x25DA1` feeds
each entry to `0x2B420` with float-buffer arithmetic
(`imul edx,ebx; shl rdx,2; add rdx,[rsp+0x78]` and
`lea rsi,[rcx+rcx+2]; imul rsi,rbx; add rsi,[r12+8]` — 2 or 3 bytes/sample × frames).
`0x2B420` walks a per-channel array with **stride 0x40** starting at `r9+0x14` and
`r9+0x214` (`0x2B470..0x2B481`, `0x2B498`) — i.e. the shared-memory port record's channel
table. `0x2B140` reads per-port gain floats at `+0x1F0/+0x1F4/+0x1F8/+0x200`
(`0x2B2A5..0x2B2C3`).

Terminus: a spinlock on `globalCtx[+0x58]` (`0x25DCE` `lock cmpxchg`) and a single
`mov byte [rax], 1` dirty flag (`0x25DE3`) in the `0x358`-stride per-context array based at
`0x9D008`.

**The tree is closed and contains no names.** Transitive call tree from `0x25AB0` +
`0x25F10` = **33 functions**, all leaves being PLT thunks (`memset`, `memcpy_s`,
`scePthreadRwlock*`, `usleep`, `__stack_chk_fail`) or small helpers, and **0 reachable
string constants**. Nothing on this path produces, stores, or returns a list, a name, or a
C++ container.

**Verdict on Part 1: dead end for `+0x2660`, and it is a clean dead end.** The descriptor
is a per-call PCM mix-request control block. It does not survive the call.

### 1.3 What the LoPort state actually contains (refines a "ruled out" item)

The standing problem lists as ruled out: *"the 0x40-byte port-state struct is NOT it:
+0x10..+0x3F is uninitialised stack residue in the firmware too (one 0x40-byte memcpy from
a frame only built to +0x0F)."* **I reproduced this independently and can now name the
exact routine.**

**EXTRACTED.** `sceAudioOut2LoPortGetState` copies out 0x40 bytes at `0x16D16..0x16D30`:

```
0x16D16  mov     word    [r15],        ax        ; <- [rbp-0x70]
0x16D1A  mov     byte    [r15 + 2],    cl        ; <- [rbp-0x6e]
0x16D30  vmovups xmmword [r15 + 3],    xmm0      ; <- [rbp-0x6d], 16 bytes
0x16D1E  mov     qword   [r15 + 0x13], rdx       ; <- [rbp-0x5d]
0x16D22  mov     dword   [r15 + 0x1b], esi       ; <- [rbp-0x55]
0x16D26  mov     byte    [r15 + 0x1f], dil       ; <- [rbp-0x51]
0x16D2A  vmovups ymmword [r15 + 0x20], ymm1      ; <- [rbp-0x50], 32 bytes
```

The source window is `[rbp-0x70 .. rbp-0x31]` = 0x40 bytes. Its only producer is
`0x16B8F call 0x1EDB0` with `rcx = &[rbp-0x70]`, and `0x1EDB0` writes **only** `rcx[0x00]`
through `rcx[0x0F]`:

```
0x1EFEB  mov word  [r13 + 4],   di
0x1F003  mov word  [r13],       di
0x1F010  mov byte  [r13 + 2],   al
0x1F01D  mov word  [r13 + 6],   ax
0x1F026  mov dword [r13 + 8],   ecx
0x1F02A  mov byte  [r13 + 3],   0
0x1F02F  mov dword [r13 + 0xc], 0
```

`sceAudioOut2LoPortGetState` itself writes only `[rbp-0x70]`, `[rbp-0x6e]`, `[rbp-0x6c]`
and bits of `[rbp-0x68]`. **`+0x10..+0x3F` of the 0x40-byte output is genuinely
uninitialised caller stack in the firmware.** Consumers must therefore only read
`+0x00..+0x0F`. Prior finding: confirmed, and now anchored at `libSceAudioOut+0x1EDB0`.

### 1.4 `sceAudioOut2PortGetState` returns no name and no list

**EXTRACTED.** `sceAudioOut2PortGetState` (`+0x41F80`, 808 bytes) writes to the caller's
`out` buffer (`r14`) in exactly three places:

- `0x42013 call 0x16B00` — the 0x40-byte copy above (only `+0x00..+0x0F` meaningful);
- `0x4220C mov word [r14], ax` — recomputes the low u16 flag word;
- `0x4224C/0x42250/0x42255` — the "no device" fallback: `word [r14]`, `byte [r14+2]=0`,
  `and byte [r14+8], 0xFE`.

There is no name, no pointer to a name, and no array. The SetAttributes → GetState loop is
closed from the game's point of view.

### 1.5 New, usable firmware contract: the full `PortSetAttributes` attribute map

This is the useful by-product of Part 1. We treat `sceAudioOut2PortSetAttributes` as inert;
the firmware does not.

**EXTRACTED.** The attribute record is **0x18 bytes**:

- validation loop `0x412C0`: `cmp dword [r11+rcx],0` / `add rcx,0x18` / bound
  `rax = numAttrs*0x18` (built at `0x412AC lea rax,[rax*8]` + `0x412B4 lea rax,[rax+rax*2]`);
- dispatch loop `0x4152B`: `lea r13,[r14+r14*2]` then `0x41507 lea rsi,[r11+r13*8]`
  = `&attrs[i]` at stride 0x18;
- fields observed read: **`+0x00` = u32 attribute id**, **`+0x08` = value pointer**
  (`0x15F56` and `0x15E50` respectively). `+0x10` exists by stride; I did not observe a
  read of it, so its meaning is **ASSUMED** (SDK docs call it the value size).

`0x41510 call 0x15C90` forwards **one attribute at a time** to
`sceAudioOut2LoPortSetAttributes` (`yiOhxHhSzC0`, `+0x15C90` → `+0x15CA0`), which runs a
validation switch (jump table at `0x54794`, 0x37 entries) and then an apply switch (jump
table at `0x54870`, 0x36 entries) writing into the per-port record `rbx`.

Decoded apply map (**EXTRACTED**, from the `0x54870` table plus each handler block):

| Attr id | Effect on the per-port record |
|---|---|
| `0x00` | volume/mix matrix apply via `0x50C0`; sets `rec+0x1E = 1` |
| `0x01` | f32 → `rec+0x470`; `rec+0x75E = 1` |
| `0x02` | u32 → `rec+0x460` |
| `0x03` | `{u64 @+0, u32 @+8}` → `rec+0x464`; `rec+0x75E = 1` |
| `0x04` | f32 → `rec+0x474`; `rec+0x75E = 1` |
| `0x05` | u32 → `rec+0x478` (3 and 4 special-cased); `rec+0x1F = 1` on change |
| `0x06` | `rec+0x1F = 1` (no payload) |
| `0x07` | accepted, ignored |
| `0x08` | u32 → `rec+0x4A8`; `rec+0x1F = 1` on change |
| `0x09` | bool → `rec+0x75C` |
| `0x0A` | f32 → `rec+0x524`, gated on a global > `0x02FFFFFF` and port type `0x102`/`0x104` |
| **`0x0B`** | **`strncpy_s(&rec+0x52C, 0x10, value, 0x0F)` — a 16-byte PORT NAME** (`0x165DB..0x165F9`) |
| `0x0C`–`0x20` | rejected, `0x80268001` |
| `0x21` | u32 → `rec+0x520` |
| `0x22`–`0x2F` | rejected, `0x80268001` |
| `0x30` | u32 → `rec+0x4AC` |
| `0x31` | u16 → `rec+0x518` |
| `0x32` | u32 → `rec+0x4B0` |
| **`0x33`** | **`memcpy(&rec+0x4F0, value, 0x28)`** (`0x1664D`, via the `0x4930` memcpy shim) |
| `0x34` | u32 clamped 0..4 → `rec+0x4B4`; `rec+0x75F = 1` |
| `0x35` | `memcpy(&rec+0x4D0, value, 0x20)` (`0x16328`) |

19 accepted ids; ids `0x0C..0x20` and `0x22..0x2F` are hard-rejected with `0x80268001`.

The port record's name field is initialised to the literal **`"NONAME"`** at
`0x13F15 call strncpy_s(&rec+0x52C, 0x10, "NONAME", 8)` inside the port-record constructor
`0x13CD0`. The literal is at `+0x51986`.

Note the coincidence and do not over-read it: attribute `0x33` carries a **0x28-byte**
payload and attribute `0x0B` carries a **0x10-byte** name — the same two magic numbers as
the `+0x2660` element size and its SSO threshold. That is suggestive of a shared "named
descriptor" idiom in Sony's audio SDK, but it is **not** evidence that `+0x2660` is fed
from here: this is a *setter*, the name is never read back (§1.4), and the whole path is
game → firmware, not firmware → game. Tagged **ASSUMED / suggestive only**.

**Actionable for the emulator (not done in this lane — analysis only):** we should log the
attribute ids Astro actually passes across those ~21,000 calls. If the title ever sends
`0x0B` or `0x33`, we learn the game's own name for its port and the shape of its 0x28-byte
descriptor, straight from the guest, without a game dump. That is one probe site and no
behaviour change.

---

## 2. PART 2 — the firmware-wide hunt for a named-record producer

### 2.1 There is no audio "bus" in the firmware

**EXTRACTED.** Every one of the 47,402 name-resolved exports whose name contains `Bus`:

```
libAacs               AacsBusDecrypt
libSceAudioOut        sceAudioOutGetSimulatedBusUsableStatusByBusType
libSceJsc/NKWebKit    _ZN3JSC4Heap19isCurrentThreadBusyEv        (substring "Busy")
libSceMbus            sceMbusEventBusStatusChange{Sub,Unsub}scribe,
                      sceMbusGetDeviceInfoByBusId_,
                      sceMbusGetSimulatedBusUsableStatusByBusType{,2}
libSceSystemService   sceShellCoreUtilTestBusTransferSpeed
libSceUsbd            sceUsbdGetBusNumber
libSceVideoOut        sceVideoOutGetPortStatusInfoByBusSpecifier_,
                      sceVideoOutGetVideoOutModeByBusSpecifier_,
                      sceVideoOutSysGetBus, sceVideoOutSysGetVideoOutModeByBusSpecifier
```

Every "Bus" in PS5 firmware is **Mbus** (the hardware media/device bus), the USB bus, or
`Busy`. Raw-string scans of the audio modules agree: `libSceAudioOut` mentions only
`sceAudioOut2IpmiMbus*`; `libSceNgs2`, `libSceAudio3d` and `libSceAudioPropagation` mention
"bus" **zero** times; `libSceAudioSystem` (which lives in `system/priv/lib` — privileged,
IPMI-server side, not linkable by a game) has an internal `BusParam`/`shmAinBusBuffer`
concept that is never exported to a title.

### 2.2 No audio export returns a named-record list

**EXTRACTED.** Audio-family exports whose name contains `Name|Enum|List|Label|Title`:
`sceNgs2SystemEnumHandles`, `sceNgs2SystemEnumRackHandles`, `sceNgs2ModuleEnumConfigs`,
`sceNgs2ModuleArrayEnumItems`, `sceNgs2ModuleQueueEnumItems`, `sceNgs2GeomCalcListener`,
`sceNgs2GeomResetListenerParam`, and `sceMediaFwAudioTrack{GetLabel,ListGetLength,ListGetTrack}`.

- `sceNgs2SystemEnumHandles` (`libSceNgs2+0xF360`) writes
  `qword [r15 + idx*8]` (`0xF3F0`) — an array of **8-byte handles**, no names.
- `libSceAudio3d` has no enumeration at all;
  `sceAudio3dPortGetAttributesSupported` returns u32 attribute ids.
- `sceMediaFwAudioTrackGetLabel` is the only genuinely name-bearing list API in the whole
  firmware audio surface — and it is in `libSceMediaFrameworkInterface`, which **Astro Bot
  does not import** (see §2.3).

### 2.3 The game's audio surface makes the question moot

**EXTRACTED** from `scripts/astro_import_routing.tsv` (1732 imports = 690 HLE + 930 LLE +
112 UNSERVED). Astro Bot's *entire* audio import surface is **47 entry points**:

- **libSceAudioOut — 15**, all `sceAudioOut2*`: `Initialize`, `UserCreate/Destroy`,
  `ContextQueryMemory/Create/Destroy/Push/Advance/GetQueueLevel/ResetParam`,
  `PortCreate/Destroy/SetAttributes/GetState`, `GetSpeakerInfo`.
- **libSceAudioPropagation — 21**, all handle+struct C calls.
- **libSceAudioIn — 6** (5 named + one unresolved NID `X+4jdIS75P0`, which is a real
  `libSceAudioIn` export at `+0x26F0`, size 216).
- **libSceAcm — 5**.
- **libSceNgs2 — 0. libSceAudio3d — 0. libSceMediaFrameworkInterface — 0.**

The title does its own mixing in software and pushes PCM through
`sceAudioOut2ContextPush`. **Not one of those 47 entry points can return a list of names.**

Reachable-string scan over all 15 `sceAudioOut2*` imports (transitive call tree, every
rip-relative ≥4-char printable literal) yields only internal trace tags
(`"sceAudioOut2LoPortDestroy"`, `"sceAudioOut2ShmAllocate"`, `"shm.cc"`, `"object.cc"`),
IPMI/shm kernel-object names (`"SceAudioSystemIpcServer"`, `"/SceAuOut2ChPShm"`,
`"SceAuOut2SysMbusLock%d"`), and `snprintf` templates used to *build* those object names
(`"%sP%xC%xP%x"` at `0x14003`, formatted into a 0x20-byte stack buffer with
`"/SceAuOut2ChPShm"`). **None of them is written to a caller buffer.** The single
name-shaped literal, `"NONAME"`, is the port record's private default (§1.5).

`sceAudioOut2GetSpeakerInfo` (`+0x203F0`): `memset(out, 0, 0x50)` then three `vmovups`
copying a 0x50-byte numeric blob from `+0x9C24C`. No strings, no list.

### 2.4 The audio modules are not even C++ at the boundary

**EXTRACTED.** C++-mangled export counts, for every module Astro imports from:

```
libSceAudioOut 184 exports / 0 mangled     libSceAudioIn 49 / 0
libSceAudioPropagation 41 / 0              libSceAcm 24 / 0
libSceAvPlayer 56 / 0                      libSceVoiceChat 30 / 0
libkernel 1240 / 0                         libSceSysmodule 21 / 0
```

The only C++-API modules the title touches at all are `libSceJson` (96/96 mangled),
`libSceNpManager` (103/507), `libSceCommonDialog` (15/29), `libSceHttp2` (1/56) and
`libSceNpEntitlementAccess` (1/26). **No audio module exports a single C++ symbol**, so no
audio module can hand a game a `std::vector`, a `std::string`, or any object with a
constructor.

**PART 2 verdict: negative, and firmly. There is no firmware API — in the audio family or
anywhere in the title's 1732-import surface — that returns an array of 0x28-byte records
embedding a `std::string`. `+0x2660` cannot be filled by a `libSce*` call.**

---

## 3. What the `+0x2660` record actually is (new)

The reported layout is: 0x28-byte element, string at `+0x08`, buffer `+0x08`, size `+0x18`,
capacity `+0x20`, SSO threshold `0x10`. That is **not** libc++ — and identifying whose STL
it *is* settles the producer question.

**EXTRACTED.** `libSceLibcInternal.sprx` exports exactly three STL throw-helpers:

```
_ZSt11_Xbad_allocv
_ZSt14_Xlength_errorPKc
_ZSt14_Xout_of_rangePKc
```

`_Xbad_alloc` / `_Xlength_error` / `_Xout_of_range` are **Dinkumware/Microsoft STL**
internals (libc++ uses `std::__1::__throw_length_error`). Corroborated by the diagnostic
literals in `libSceLibcInternal.sprx`:

```
"string too long"   "vector<T> too long"   "deque<T> too long"   "invalid string position"
```

and by an actual call site — `libSceAudioPropagation+0x7185`:
`lea rdi,[rip+0xaa04]  ; "deque<T> too long"` / `0x718C call _ZSt14_Xlength_errorPKc`.
`libSceAudioPropagation` imports `_Znwm`, `_ZSt14_Xlength_errorPKc` and `_ZSt11_Xbad_allocv`
from `libSceLibcInternal`.

**DIFFERENTIAL.** Dinkumware's `std::string` is
`{ union { char _Buf[16]; char* _Ptr; }; size_type _Mysize; size_type _Myres; }` = **0x20
bytes**, SSO threshold 16. Place one at record offset `+0x08`:

| Field | Predicted | Observed at `+0x2660` |
|---|---|---|
| buffer / pointer | `+0x08` | `+0x08` ✓ |
| size | `+0x18` | `+0x18` ✓ |
| capacity | `+0x20` | `+0x20` ✓ |
| record size | `0x08 + 0x20 = 0x28` | `0x28` ✓ |
| SSO threshold | `0x10` | `0x10` ✓ |

Five for five. **The `+0x2660` element is `struct { <8-byte head>; std::string name; }` built
with the PS5 SDK's own STL — i.e. by C++ code compiled into the title.**

**Ruled out on the way (EXTRACTED):** it is *not* a `sce::Json` container either.
`sce::Json::String` is an **8-byte pimpl**, not 0x20:

```
libSceJson2+0xA1F0  c_str()    : mov rax,[rdi]; test rax,rax; je ...; mov rax,[rax]; ret
libSceJson2+0xA180  size()     : mov rax,[rdi]; ...; mov eax,[rax + 8];  ret
libSceJson2+0xAF80  capacity() : mov rax,[rdi]; ...; mov eax,[rax + 0xC]; ret
libSceJson2+0x9FD0  ctor       : mov qword [rdi],0; mov edi,0x18; call operator new
```

so `{ptr; size@+8; cap@+0xC}` in a heap block of 0x18 bytes, behind one pointer. A vector
of `sce::Json::String`-bearing records would be 0x10-stride, not 0x28. **Json objects are
not what sits at `+0x2660`** — though JSON remains a plausible *source* of the text, copied
into SDK `std::string`s by game code.

---

## 4. PART 3 — the plain statement, and what to do next

**Plainly: neither part found the producer, and Part 2 establishes that it cannot be a
firmware API.** No `libSce*` module returns named-record lists; the title's whole audio
surface is 47 C entry points that return integers, flags and PCM; and the `+0x2660` element
is an SDK-STL `std::string` record, which only game-side C++ can construct. `+0x2660` is
filled from **game-internal configuration** — most plausibly deserialised from an asset the
title loads — and the way to settle it is the asset/file trace, exactly as the lane
anticipated.

### 4.1 Leads that stay dead — do not re-run

- **The libSceJson2 iterator gap.** Still dead. `docs/astrobot-bringup.md:295` killed it with
  a runtime hit count: **0 of the 15 NIDs is ever called.** I re-confirmed the static half
  only (all 15 NIDs return 0 hits under `src/`, and `src/SharpEmu.Libs/Json/` implements no
  `Array::begin/end`, no iterator type, no container storage) — that changes nothing. The
  measurement stands and outranks the static argument. Do not implement those 15.
- **libSceAmpr as an *audio* API.** Unchanged and correct. §4.2 makes a different claim
  about the same module (its *file-read* primitives), not this one.
- **Any Ngs2 / Audio3d route.** The title imports neither. Zero NIDs.

### 4.2 The one new file-shaped lead — hit-count it before touching code

Astro's asset I/O runs through **APR** (`sceKernelAprResolveFilepaths*` → ids →
`sceAmprApr*CommandBuffer*` → DMA read), and in our tree a specific slice of that path is
unserved. All twelve NIDs below return **0 hits** under `src/` (verified by literal grep),
and `src/SharpEmu.Libs/Ampr/AmprExports.cs` registers only the plain
`sceAmprAprCommandBufferReadFile` / `sceAmprMeasureCommandSizeReadFile` forms:

| NID | Symbol |
|---|---|
| `mZSbNJVJpV8` | `sceAmprAprCommandBufferReadFileGather` |
| `Jg-AgkdJHkk` | `sceAmprAprCommandBufferReadFileScatter` |
| `BVmR1H8l+XI` | `sceAmprAprCommandBufferReadFileGatherScatter` |
| `YPxkUDhgoNI` | `sceAmprAprCommandBufferResetGatherScatterState` |
| `Eul7AGEpjLo` | `sceAmprAprCommandBufferMapBegin` |
| `bFEs0Gs6D2A` | `sceAmprAprCommandBufferMapDirectBegin` |
| `X169CE6G3Y4` | `sceAmprAprCommandBufferMapEnd` |
| `QzB4O+bJQyA` | `sceKernelAprResolveFilepathsToIdsAndFileSizesForEach` |
| `eYAh2vlCY-U` | `sceKernelAprResolveFilepathsToIdsForEach` |
| `i3HWvW35jao` | `sceKernelAprResolveFilepathsWithPrefixToIds` |
| `VB-BtuIW8Xc` | `sceKernelAprResolveFilepathsWithPrefixToIdsForEach` |
| `C+Khtbbx2g8` | `sceKernelAprResolveFilepathsWithPrefixToIdsAndFileSizesForEach` |

The shape fits: the *plain* `ReadFile` path is served (which is why textures load and the
renderer presents), while the **gather/scatter and direct-map batch forms** — the ones an
engine uses to pull many small records or a packed config table in one command — are not.
A config that never arrives leaves every container built from it empty.

**This is a hypothesis, and the repo's own methodology rule (`docs/astrobot-bringup.md`
"Methodology", item 2) applies: a static who-calls-this argument is not sufficient.**
The runtime hit count is free — an unresolved import already logs
`[LOADER][WARN] Import#N unresolved: nid=…` per call
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs:605`). So:

1. Boot once, `grep` the log for those twelve NIDs, and count. **If all twelve are zero,
   this lead dies exactly like the Json one — record it and move on.**
2. Only if non-zero: implement the served-form equivalents and re-check whether `+0x2660`
   ever becomes non-NULL (the 200 ms change-sampler already exists).

### 4.3 The other two experiments, ranked

1. **File/dir trace.** `SHARPEMU_LOG_IO=1` now covers the open path
   (`src/SharpEmu.Libs/Kernel/KernelFileTraceLog.cs:28` documents the earlier blind spot),
   and `sceKernelGetdents` is implemented and traced
   (`KernelMemoryCompatExports.cs:7701`). Note that **`sceKernelGetdents` is the only
   name-list source anywhere in the title's 1732 imports** — the full scan for
   `Name|Enum|GetList|ListGet|Dirent|Getdents|ReadDir` over the routing table returns just
   `sceKernelGetdents`, `sceUserServiceGetUserName`, `sceKernelMapNamedDirectMemory`,
   `sceKernelSetVirtualRangeName`, `sceKernelClearVirtualRangeName`. If the engine
   enumerates a directory to discover its buses, `getdents` is the call, and a boot log will
   show it plus which path it asked for and whether that path exists on the host mount.
2. **Attribute-id census on `sceAudioOut2PortSetAttributes`.** One probe site, no behaviour
   change: record the `attrs[i].id` values (record stride 0x18, id at `+0x00`) across the
   ~21,000 calls. If the title sends id `0x0B` we get its own port name; if it sends `0x33`
   we get a 0x28-byte descriptor from the guest. Either is direct evidence about the shape
   the engine thinks in, obtainable without a game dump.

---

## 5. Everything Part 1 established that is worth keeping

Independent of the bus question, these are new firmware facts with citations, ready to be
turned into contract tests by whoever owns the audio HLE:

- `sceAudioOut2PortSetAttributes` attribute record is **0x18 bytes**, `{u32 id @+0x00;
  const void* value @+0x08; …}`, dispatched **one at a time** to
  `sceAudioOut2LoPortSetAttributes` at `libSceAudioOut+0x41510`.
- The accepted attribute-id set is `{0x00–0x0B, 0x21, 0x30–0x35}` (19 ids); `0x0C–0x20` and
  `0x22–0x2F` are rejected with `0x80268001`. Full field map in §1.5.
- Attribute `0x0B` sets a **16-byte port name** at record `+0x52C`, default `"NONAME"`
  (`libSceAudioOut+0x13F15`).
- `sceAudioOut2LoPortGetState` writes **0x40 bytes** of which only `+0x00..+0x0F` is
  initialised; `+0x10..+0x3F` is caller stack residue on real firmware
  (`+0x1EDB0` writes `rcx[0x00..0x0F]` only; copy-out at `+0x16D16..0x16D30`).
- `sceAudioOut2PortGetState` reaches the caller's buffer only through that copy plus a
  low-u16 flag rewrite and a no-device fallback — **it never returns a name**.
- `sceAudioOut2GetSpeakerInfo` writes exactly **0x50 bytes** memcpy'd from `+0x9C24C`.
- The PS5 SDK C++ standard library is **Dinkumware**, not libc++
  (`_ZSt14_Xlength_errorPKc` / `_ZSt11_Xbad_allocv` / `_ZSt14_Xout_of_rangePKc` in
  `libSceLibcInternal`, plus `"string too long"` / `"vector<T> too long"` /
  `"deque<T> too long"` / `"invalid string position"`). Any guest `std::string` is 0x20
  bytes, `{buf[16] | ptr; size; cap}`, SSO threshold 16.
- `sce::Json::String` is an 8-byte pimpl over `{char* ptr; u32 size@+8; u32 cap@+0xC}`
  (`libSceJson2+0xA1F0/0xA180/0xAF80/0x9FD0`), so JSON containers cannot be the `+0x2660`
  storage.

---

# Engine side: two singleton slots, and the tick path reads the one nobody fills

**Date:** 2026-07-25, added by the orchestrator after the firmware-side analysis above.
**Source:** Astro Bot's own `eboot.bin`, extracted surgically over SSH. SELF mapping is
`file_off = 0x3b1f0 + (guest - 0x800000000)`, so guest `0x800DC0400` sits at file offset
`0xDFB6F0`. 2304 bytes were pulled and disassembled locally; the 240 MB binary was never moved.

This section **combines with §2's negative** above. That section proved no firmware API can fill
`+0x2660`, and that the element's string layout is the SDK's Dinkumware `std::string` — i.e. the
records are built by *game* C++. This section shows where that build is gated.

## `0x800DC0500` takes a mode argument in `esi`

```
0x800DC0523  cmp byte [rdi+0x2900], 0 / jne 0x800DC1315   ; the idempotence latch
0x800DC053D  test esi, esi / je 0x800DC0648                ; esi == 0 -> per-tick path
```

The per-tick caller (`0x800F3E4D2`) passes **`esi = 0`**. So `0x800DC0545..0x800DC0647` — an assert
helper, a `0x100`-byte allocation, and a large structure init — is the **`esi != 0`** path and
never executes on the tick.

## The two paths use different globals

| Slot | Gate byte | Object pointer | Accessed by |
|---|---|---|---|
| **A** | `0x80E7F5570` | `0x80E7F5578` | `esi != 0`. **Writes** the object at `0x800DC062B` (`mov qword [rip+0xda34f46], rax`), then `0x800DC0632` writes `byte [0x80E7F5580] = 0`. |
| **B** | `0x80E7F5558` | `0x80E7F5560` | `esi == 0` per-tick. **Read only.** `0x800DC0648` and `0x800DC0657` each `movzx eax, byte [rip+…]` / `test al,al` / `je` → `0x800DC1406` / `0x800DC146F`. Then `0x800DC065E lea r12,[rip+…]` / `0x800DC066D mov rdi,[r12]`. |

**Nothing in this routine writes slot B's gate at `0x80E7F5558`.** The per-tick path's precondition
is produced somewhere else entirely. That is the long-standing "the producer never runs" symptom,
now located one level further out than before: it is not that the build loop iterates zero times,
it is that the code containing the loop is **branched around** before reaching it.

Past the gates the tick path is virtual dispatch on `[rbx+8]` — `call [rax+0x430]`, `[rax+0x20]`,
`[rax+0x10]` — each followed by a sign test and an error call to `0x800DBE430`.

A third flag group is **written** in `0x800DC0A00..0x800DC0C90`, the region containing the sole
`&defaultBusses` append at `0x800DC0B20`: `0x800DC0AAC` and `0x800DC0C4C` set
`byte [0x80E7E3A98] = 1`; `0x800DC0ADB` and `0x800DC0C7B` store `r15` into `0x80E7E3A90`.

## The next question, stated precisely

**Who writes `0x80E7F5558`?** That byte gates the entire per-tick bus build. Settling it means
scanning `eboot.bin` for writers of that address — a `mov byte [rip+disp], 1` whose target
resolves there. The binary is on the VM at
`C:\Users\astro\Downloads\ASTRO.BOT-PPSA21564-USA-Game-v01.007.000-PS5\PPSA21564-app\eboot.bin`
(251,850,759 bytes) and regions can be pulled by file offset over SSH without moving the whole
file — that is how everything here was obtained.

Secondary: does anything ever call `0x800DC0500` with a **non-zero** `esi`? If not, slot A's
constructor is dead in our runtime, and that is a second candidate explanation which the same scan
would settle.

---

# Round 3: the loop is real, and seven hypotheses are dead

**Date:** 2026-07-25, from Astro Bot's `eboot.bin` now held locally at
`games/ASTRO.BOT-PPSA21564-USA-Game-v01.007.000-PS5/someFilesnotall/eboot.bin`
(251,850,759 bytes, SELF magic `4F153D1D`). All scans below are single local capstone passes.

## The loop, decoded in full

Linear sweep from the function entry (mid-function addresses do NOT decode — `0x800DC0A60`
starts on `07`, invalid in 64-bit; always sweep from `0x800DC0500`):

```
0x800DC0B02  mov rax, [rbx+0x2668]     ; end
0x800DC0B09  mov r14, [rbx+0x2660]     ; begin
0x800DC0B17  cmp r14, rax
0x800DC0B1A  je  0x800DC0CA2           ; begin == end -> ZERO ITERATIONS
0x800DC0B20  lea rax, [rbx+0x2728]     ; &defaultBusses
0x800DC0B40  add r14, 0x28             ; stride 0x28
0x800DC0B47  cmp r14, [rbp-0x160]
0x800DC0B4E  je  0x800DC0CA2
```

So the long-standing description is CORRECT: `+0x2660`/`+0x2668` are `begin`/`end` of a
0x28-stride vector on the owner, and `+0x2728` is `&defaultBusses`. The loop is entered every
tick and exits immediately because the vector is empty.

## Everything now measured, not inferred

| Link | Result | How |
|---|---|---|
| Gate `0x80E754C68` | **SET** (`01`) | runtime probe |
| Owner `0x80E754C70` | **valid** (`0x3009A22C0`) | runtime probe |
| Builder `0x800DC0500` | **called every tick** | only call site `0x800F3E4D2` |
| magic-static guard `0x80E7F5558` | **set** (`word=0x1`) | `SHARPEMU_LOG_GUARDS=1` |
| `esi != 0` constructor branch | **UNREACHABLE** | 1 `E8` call (`xor esi,esi`), 0 `lea`, 0 raw ptr |
| vtable | `{dtor, deleting-dtor}` only | `0x800DC04D0` = dtor + `operator delete` |
| vector passed to a growth helper | **NO** | 0 × `lea r64,[reg+0x2658]` (the `begin-8` idiom) |
| `[reg+0x2660]` REX.W stores | 2, both **unrelated** | `0x803C4E8B0`, `0x8048151E9` — stack temporaries in other functions that collide on the displacement |
| firmware API returning named records | **none** | 552 modules |
| config file read | **never** | 16 paths touched all boot, none under `/app0/data` |

## The surviving lead

`0x80E754C70` has **20 distinct writers**, and most store the same `rax` into TWO adjacent
globals in one breath (`mov [rip+X], rax` immediately followed by `mov [rip+Y], rax`):

```
0x800DBC6EA  0x800DBC758  0x800DBCE8F  0x800DBD106  0x800DD0A7D  0x800DD0C2E
0x800DD6817  0x800DD6885  0x800DD74E6  0x800F3488F  0x800F405E6  0x800F5C916
0x800F6AF9E  0x800FD68FD  0x800FD6D04  0x800FD781E  0x800FD7BB6  0x800FD9F28
0x800FFD8AE  0x800FFDBD2
```

Twenty writers is not a singleton constructed once — it is a **"current/active" pointer that gets
reassigned**. That admits an explanation nothing else has: the builder reads whatever instance is
current at tick time, and the instance whose `+0x2660` actually gets populated may be a
**different one**. The runtime probe confirms the pointer is valid and stable across the samples
taken, so the next step is to check whether it is the *same* value the populating code writes.

**Do not** search for writes to `+0x2660` again — that has now been done at REX.W width, at
`lea`-address width, and via the `begin-8` idiom, and all three are negative.

---

# RESOLVED (2026-07-25)

The assert is **not a bug to fix**. It is non-fatal by design, and the emulator was already
reproducing retail control flow once the trap is not taken. What follows is the ground truth.

## The measurement blind spot that cost the most

The boot harness redirects stdout and stderr to two files:

```
-RedirectStandardOutput C:\gp-out.txt  -RedirectStandardError C:\gp-err.txt
```

Every prior conclusion in this document was drawn from `gp-out.txt` alone. `KernelFileTraceLog.Fail`
writes to **stderr**, so the entire `[LOADER][IO-FAIL]` channel had never been read. Fetching
`gp-err.txt` immediately produced the sound-config path list. Before concluding "X never happens"
from a log, confirm the channel that reports X shares the stream being read.

The same class of error applies to `[PROBE][sound-assert]`: that site is declared in
`probes/astro-sound.json` but `GuestProbeEngine.Fire("sound-assert", ...)` exists nowhere in `src/`.
The example output in `docs/guest-probes.md` cannot be produced by this tree. Absence of those lines
was never evidence.

## Address mapping: the code delta does not generalise

`file_off = 0x3b1f0 + (guest - 0x800000000)` is correct for the **code** segment and is confirmed
by runtime (`0x80E754C70` and `0x80E754C68` read exactly as computed). It does **not** hold for
rodata: the ELF program headers at file `0x1A0` describe the decrypted image, not this file, so
string references cannot be resolved with the code delta. Two separate attempts to locate the assert
message string by rip-relative `lea` failed for this reason.

Do not resolve rodata addresses by scanning. **Locate assert sites by their line-number immediate**
(`mov esi, <line>` followed by `int 0x41`) - it needs no address mapping and is exact.

## Object layout, verified at runtime

`this = 0x3009A22C0` (from `[0x80E754C70]`). Container objects start **8 bytes before** `begin`:

| member | container | begin | end | elem | runtime |
|---|---|---|---|---|---|
| descriptors  | `+0x2658` | `+0x2660` | `+0x2668` | 0x28 | begin=end=cap=0 |
| source2      | `+0x2698` | `+0x26a0` | `+0x26a8` | 0x28 | begin=end=cap=0 |
| source3      | `+0x26b8` | `+0x26c0` | `+0x26c8` | 0x28 | begin=end=cap=0 |
| **defaultBusses** | `+0x2728` | **`+0x2730`** | **`+0x2738`** | **0x18** | begin=end=cap=0 |

The assert reads `+0x2738 - +0x2730` and requires exactly `0x18` (one element):

```
0x800F5B14A: mov rax, [r14 + 0x2738]
0x800F5B151: sub rax, [r14 + 0x2730]
0x800F5B158: cmp rax, 0x18
0x800F5B15C: jne -> assert block
```

`cap == 0` on every container is the decisive field: storage was never allocated, so nothing was
ever pushed and later cleared.

## The source vectors are never populated by anything

Scanned every displacement (`0x2658/0x2660/0x2698/0x26a0/0x26b8/0x26c0`) across all 251 MB, all REX
forms `0x48-0x4F`, every mnemonic. Each container is touched by **exactly one** instruction in the
whole binary, all three inside the constructor:

```
0x800DBF670: vmovups ymmword ptr [rbx + 0x2658], ymm1
0x800DBF680: vmovups ymmword ptr [rbx + 0x2698], ymm1
0x800DBF688: vmovups ymmword ptr [rbx + 0x26b8], ymm1
```

`defaultBusses` is filled only by loop 1, from `descriptors`. So `defaultBusses.size() ==
descriptors.size() == 0` **on every platform, including retail hardware.** The assert fails on a
real PS5 too.

The config files that would fill them are dev-only. The retail package ships
`data/common/{font,gfx,haptics,odx,sound}` as **empty directories** - confirmed against a complete
148.50 GB / 156,133-file dump, in which `config.xml`, `sound_request_pairs.xml` and
`audio_propagation_config.xml` do not exist anywhere. The game asks for them only under the
unexpanded dev root `/host/%ASOBI_ROOT%/target/...`, which correctly misses.

## The builder runs to completion

`0x800DC0500` is called once, from `0x800F3E4D2`, with `esi=0`, after checking gate `0x80E754C68`
(runtime: `1`) and loading the owner from `0x80E754C70`. Its **last** instruction before returning is

```
0x800DC130E: mov byte ptr [rbx + 0x2900], 1
```

so the runtime reading of `+0x2900 == 1` means the builder *finished*, not that it was skipped. It
does real work: it allocates `0x18`-byte nodes into a **circular list** at `+0x2770` and registers
each through vtable `+0x420`. The runtime probe shows `+0x2770 = 0x3006197F0` with count `9` at
`+0x2778`. Nine buses exist. They simply are not `defaultBusses`.

## Why the assert is non-fatal

Both paths converge. `int 0x41` is the only divergence, and it is gated on the reporter's return:

```
0x800F5B15C: jne 0x800F5B3D5      ; -> assert block
   ...      mov esi, 0x132 ; call 0x800001AA0
0x800F5B402: test eax, eax
0x800F5B404: je  0x800F5B162      ; reporter returned 0 -> continue
0x800F5B40A: int 0x41
0x800F5B40C: jmp 0x800F5B162      ; and continue anyway
0x800F5B162: mov rax, [r14 + 0x2730]   ; <- single continuation
```

`0x800001AA0` is game code, not a thunk. Its return is computed as

```
0x800001E0E: cmp dword ptr [rbp - 0x318], 1
0x800001E15: setne al
0x800001E2E: movzx eax, al        ; return ([rbp-0x318] != 1)
```

where `[rbp-0x318]` is filled by two imported calls through GOT slots `0x80E937140` / `0x80E937148`.
A console that reports "continue" returns 0 and never traps. **Reporting the assert and proceeding
is the faithful behaviour, not a stub.**

Runtime agrees with the derivation exactly - the unwind resumes at the convergence instruction:

```
Astro assert frame unwound: return=0x0000000800F5B402
```

## Result

With the trap not taken, the boot goes from ~1.74M to **2.1M import calls**, guest-visible errors
drop to **2**, the material and shader pipeline runs (`Setup MaterialPackedShaderBinaries`,
`Material [...] is Replaced`), the draw threads spawn (`Draw Geometry`, `Draw Shadow`, `Draw Decal`,
`DrawThread`), and:

```
[LOADER][INFO] Vulkan device: Tesla T4 (DiscreteGpu)
[LOADER][INFO] Vulkan VideoOut presented first frame: 3840x2160
```

RDNA2-targeted AGC output presenting a 4K frame on NVIDIA hardware.

## Still open

The two imports behind GOT `0x80E937140` / `0x80E937148` are unidentified. Naming them would let the
reporter return 0 through its own logic instead of an unwind, removing the need for
`SHARPEMU_ASTRO_ASSERT_SKIP`. `SHARPEMU_LOG_ALL_IMPORTS` is too slow to reach audio init inside a
240 s budget; read the GOT slots with a probe and match the values against the
`SetupImportStubs: ... -> 0x...` lines from the **same** process.

---

# The two imports, named (2026-07-25, same day)

The "Still open" item above is closed, and it corrects the section before it.

## Resolving GOT slots to NIDs in this eboot

The thunk targets were miscomputed once (`0x8074EC306 + 0x194AE3A` is `0x808E37140`, not
`0x80E937140`); the correct slots sit beside `PltGot=0x8E34870`. They are **not** in `JmpRel` - they
are `R_X86_64_JUMP_SLOT` (type 7) entries in the main `RELA` table.

`[LOADER] Dynamic Info:` lines give table **vaddrs**, and no single delta converts them to file
offsets. Working values for this dump:

| table | vaddr | file |
|---|---|---|
| RELA | `0xEE13650` | `0xE2F3650` (`vaddr - 0xB20000`), 558551 entries |
| StrTab | `0xEDF7E10` | **`0xE342730`** |
| SymTab | `0xEDFF3A0` | **`0xE349CC0`** (null symbol verifies it) |

**Every NID string is exactly 16 bytes, so a StrTab base off by any multiple of 16 still yields
valid-looking NIDs.** Two wrong bases were accepted before this was caught. Anchor the base, never
eyeball it. Three independent anchors agree on `0xE342730`:

- `sym[1309] = eLdDw6l0-bU`, matching the runtime line `assert frame unwind ... nid=eLdDw6l0-bU`
  with `ret=0x800001BF7`, the return address of `0x800001BF2: call 0x8074EC290`
- `sym[37] = 3GPpjQdAMTw` = `__cxa_guard_acquire`, computed from the name, matching its call site
- `sym[1310] = YQ0navp+YIc` = `puts`, called as `lea rdi,[rbp-0x230]; call ...` - a message buffer

## The assert reporter is a message dialog

| thunk | NID | export |
|---|---|---|
| `0x8074EC2F0` | `6fIC3XKt2k0` | `sceMsgDialogUpdateStatus` |
| `0x8074EC300` | `Lr8ovHH9l6A` | `sceMsgDialogGetResult` |
| `0x8074EC310` | `ePw-kqZmelo` | `sceMsgDialogTerminate` |

```
loop { ... ; sceMsgDialogUpdateStatus() } while (eax != 3)   ; 3 = SCE_COMMON_DIALOG_STATUS_FINISHED
sceMsgDialogGetResult(&result)                               ; result at rbp-0x320
sceMsgDialogTerminate()
cmp dword ptr [rbp-0x318], 1                                 ; result+8 = buttonId, 1 = OK
setne al                                                     ; trap only if OK was NOT pressed
```

The game raises an assert dialog and continues **iff the user acknowledges it**. `int 0x41` is the
"developer chose to break" path, not the failure path.

## `SHARPEMU_ASTRO_ASSERT_SKIP` is not required

`src/SharpEmu.Libs/CommonDialog/MsgDialogExports.cs` already models this correctly:
`StatusFinished = 3`, and `sceMsgDialogGetResult` writes `buttonId = 1` at `result+0x08`. So the
guest's comparison succeeds, the reporter returns 0, and the assert is non-fatal on its own.

Measured, and this **corrects the "Result" section above**, which implied the flag was needed:

| run | assert printed | first frame presented | imports |
|---|---|---|---|
| no flag | yes, once | **yes** | 1.6M |
| `SHARPEMU_ASTRO_ASSERT_SKIP=1` | no | yes | 2.1M |

Astro Bot presents a 3840x2160 frame on `Tesla T4 (DiscreteGpu)` **with no assert flag set**. The
earlier "no frame without the flag" reading was wrong because `presented first frame` is written to
**stderr** and only stdout had been checked - the same stream mistake this document already warns
about, repeated. The flag's only effect is to unwind at `0x800001BF2` and skip the dialog
round-trip, which buys wall-clock (2.1M vs 1.6M imports in the same budget).
