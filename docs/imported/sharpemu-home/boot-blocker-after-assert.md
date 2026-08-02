# The blocker after the SoundManager assert (measured 2026-07-25)

`docs/audio-bus-hunt.md` closed the `SoundManager.cpp:306` assert: it is non-fatal by design and
needs no flag. This document characterises what stops the boot **after** it, because "the assert is
fixed" is not the same as "the game runs".

Ground rule this document exists to enforce: **an axis is not closed by the absence of a crash.**
The process staying alive for seven minutes is not progress. Frames are progress.

## What was measured

One 420 s run and three shorter runs on `astro-vm3` (Tesla T4), no assert flag set,
`SHARPEMU_LOG_VIDEOOUT_FPS=1`, `SHARPEMU_HLE_EFFECT_CENSUS=1` with `TOP=300` so all 219 called
exports are listed.

The process **does not crash**. It ran the full 420 s and was killed by the harness, not by a fault.

## It renders one blank frame and then nothing

```
[LOADER][PERF] videoout submitted_fps=0.0 presented_fps=0.1 draws=0 draw_ms=0 pipelines=0 spirv=0
```

**Exactly one `[LOADER][PERF]` sample in 420 seconds**, and the line is emitted per present. So:

- one present in seven minutes
- **zero draws**, zero pipelines, zero SPIR-V compiles in the sampled window

The `Vulkan VideoOut presented first frame: 3840x2160` line is real, but it is a **single blank
present with no geometry behind it**. Quoting it as evidence that RDNA2 output is running on NVIDIA
overstates it: nothing was drawn.

## The GPU path is set up, then abandoned

From the full census (these are total calls over ~260 s, not a sample):

| export | calls |
|---|---|
| `sceAgcCreateShader` | **1,278** |
| `sceAgcCreateInterpolantMapping` | 190 |
| `sceAgcDriverValidateDcbRange` | 33 |
| `sceAgcDriverSubmitDcbRange` | **33** |
| `sceVideoOutRegisterBuffers2` | 1 |
| `sceVideoOutOpen` | 1 |
| `sceVideoOutSetBufferAttribute2` | 1 |
| **`sceVideoOutSubmitFlip`** | **0 - never called** |

So the guest really does build shaders and submit 33 command buffers, and then stops. It never
flips. An earlier reading of this as "no GPU submission at all" was wrong - it came from the
census's default top-40 cut, which hides `sceAgcDriverSubmitDcbRange` because that row is
*effectful*, not inert.

## It never loads a single asset

With `SHARPEMU_LOG_IO=1 SHARPEMU_LOG_OPEN=1` and **no** path filter, a 200 s boot performs file
operations on **30 paths total**:

- `/app0/param.sfx`
- `/app0/app_sys/DebugFont.gnfp` (absent)
- a handful of `/host/%ASOBI_ROOT%/...` dev-root `.gnfp` / `.xml` misses

The dump contains **156,091 files under `data/prein/`**. The game opens **none** of them - no
`.jxm`, no `.szd`, no `.anim`, no `.odxb`.

This was checked for the obvious instrumentation gap: PS5 titles often stream through
`libSceFios2` or `sceKernelAio*`, which `KernelFileTraceLog` does not cover. The full census
settles it - **no Fios2 or Aio export appears among the 219 exports called.** The only file exports
called at all are `sceKernelOpen`, `sceKernelRead`, `sceKernelStat`, `sceKernelClose`. The absence
of asset loading is real, not a blind spot.

## What it does instead

Top of the activity profile, by total calls:

```
384,711  internal_memcpy_s          192,286  sceKernelGetProcessTime
310,438  scePthreadMutexUnlock       76,906  sceAudioOut2PortGetState
245,137  sceLibcMspaceMalloc         38,453  sceAudioOut2{PortSetAttributes,GetSpeakerInfo,ContextAdvance}
216,891  scePthreadRwlockUnlock      12,307  sceKernelWaitEventFlag / 12,304 SetEventFlag
```

The live traffic is the **Sndz audio threads** cycling and a **time-polling spin**
(`sceKernelGetProcessTime` at 192k). `sceLibcMspaceFree` sits at **69,711 - byte-identical across a
130 s run and a 300 s run**, which proves allocation stopped early and nothing has advanced since.

## Conclusion

After material and shader setup the title parks. It never starts asset loading, so there is no
geometry, so there are no draws, so there is no flip. This is a **boot/loading state machine that
does not advance**, and on the present evidence it is *not* a FreeBSD-shaped blocker - nothing is
failing a syscall, nothing is returning an error (23 guest-visible errors in the whole run).

Unknown, and the next thing to establish: **what the main thread is waiting on**. The census is
process-wide and cannot say. The paired `WaitEventFlag`/`SetEventFlag` at ~12.3k and the
`GetProcessTime` spin are the two threads to pull. A per-thread call attribution, or a stack sample
of the non-audio threads while parked, would name it directly.

## Refinement: the main thread is blocked, not spinning

The import trace logs each sampled call's **guest return address** (`ret=0x...`), which names the
caller without needing any rodata mapping. Every late-boot sample (`Import#500000` through
`#2100000`) has a caller in `0x800DE...`-`0x800E4...`:

```
1900000  scePthreadMutexUnlock  ret=0x800DED31B
2000000  scePthreadMutexUnlock  ret=0x800E46B59
1300000  scePthreadMutexUnlock  ret=0x800E46D89
1100000  pthread_self           ret=0x800E1269F
```

That is the **Sndz audio region** (its thread entry is `0x800DFD890`). The main thread appears in
**none** of the samples.

A spinning thread would dominate a call-sampled trace. It does not appear at all, so it is not
spinning - **it is blocked**, and the 192k `sceKernelGetProcessTime` calls belong to the audio
threads, not to a main-thread poll loop. This corrects the "time-polling spin" reading in the
section above.

Consistent with that, the census shows `scePthreadCondWait` at **3** calls and `sceKernelWaitSema`
at **19** - the shape of a wait that was entered and never returned, not of a poll.

**Working hypothesis to test next:** the main/loader thread is parked in a condvar or semaphore wait
that nothing ever signals, while the audio threads keep the process alive. Naming it needs guest
thread states at the parked moment (which thread, which primitive, which handle) - the census is
process-wide and cannot attribute.

## Dead ends - do not repeat

**Resolving rodata string addresses in this eboot.** Three approaches all failed:

1. The code delta `0x3b1f0` does not apply to rodata.
2. ELF program-header offsets (`file 0x1A0`) describe the decrypted image, not this file - parsing
   `DYNAMIC` at the phdr offset yields a RELA table misread as dynamic tags.
3. Deriving a delta by aligning `lea` targets against string starts: a global fit gave
   `-0x29415DE` with 251,218/1,713,531 matches, but it resolves **zero** references to the material
   format strings; a per-region fit near those strings gave `-0x28D94D` at only 426/6557, and
   disassembling at the implied address decodes overlapping garbage.

The SELF stores segments independently, so no single or per-region linear delta holds.
**Use the import trace's `ret=` field to locate guest code instead** - it is exact, needs no
mapping, and is how the finding above was obtained. For asserts, use the line-number immediate.

## Cross-check against the reference emulators (inspiration/, refreshed 2026-07-25)

`inspiration/` was refreshed: `acelogic-sharpemu` re-cloned at `main` (221a021), `shadPS4` advanced
3 commits to `4dc3cf8`; `KytyPS5`, `shadps5-rust`, `ps5-agc-tutorial`, `ps5-payload-sdk` already
current.

**KytyPS5** is the closest reference (PS5 + AGC, and it renders). Checked whether the GPU-completion
path Astro needs is missing on our side, since a game waiting on a GPU event that never fires would
produce exactly the observed symptom. It is **not** missing - our NIDs match KytyPS5's exactly:

| export | NID | KytyPS5 | ours |
|---|---|---|---|
| `sceAgcCbQueueEndOfPipeActionGetSize` | `hL7C0IRpWZI` | yes | yes |
| `sceAgcQueueEndOfPipeActionPatchAddress` | `0fWWK5uG9rQ` | yes | yes |
| `sceAgcQueueEndOfPipeActionPatchData` | `MlEw1feXcjg` | yes | yes |
| `sceAgcDriverAddEqEvent` | `w2rJhmD+dsE` | yes | yes |

We also register the event (`KernelEventFilterGraphics`) and do fire it from the DCB walker on
`IT_EVENT_WRITE` packets (`AgcExports.cs` ~3941). So end-of-pipe coverage is not the blocker.

Negative result, recorded so it is not re-investigated.

## Instrumentation note

The per-thread snapshot needed to name the blocked wait is gated behind
**`SHARPEMU_PERIODIC_SNAPSHOT_SECONDS`** (an integer, default 0 = off) - *not*
`SHARPEMU_LOG_GUEST_THREAD_SNAPSHOTS`, which gates a different dispatcher log. It prints
`[LOADER][ERROR] --- periodic snapshot ---` followed by `GuestThreadSnapshot` rows carrying
`Name / State / LastImportNid / LastReturnRip / BlockReason` - which is exactly what names the
parked wait. That run has not yet completed successfully; it is the next thing to do.

The stall watchdog's `No import progress for Ns while waiting in {name} ({nid})` line never fires
here because progress is measured **process-wide** and the Sndz audio threads keep calling imports
forever. A per-thread progress notion would have caught this immediately.

---

# ROOT CAUSE FOUND: `snprintf` returns negative, which diverts the assert into `int 0x41`

This **corrects** the claim in `docs/audio-bus-hunt.md` that the assert is "non-fatal without any
flag". It is non-fatal *by design*, but our `snprintf` diverts it onto the trap path.

## The chain, end to end

1. `SoundManager.cpp:306` fires. That is correct and unavoidable - `defaultBusses` is empty on real
   hardware too (see `docs/audio-bus-hunt.md`).
2. The reporter `0x800001AA0` formats the message first:
   ```
   0x800001BDD: lea rdi, [rbp-0x229]      ; dest
   0x800001BEB: mov esi, 0x1F9            ; size = 505
   0x800001BE4: lea rdx, [rip+0x8163EED]  ; format
   0x800001BF2: call snprintf             ; NID eLdDw6l0-bU
   0x800001BF7: test eax, eax
   0x800001BF9: js   0x800001E1F          ; snprintf < 0 -> EARLY EXIT
   ```
3. Our `SnprintfCore` (`KernelMemoryCompatExports.cs:4118`) returns
   `ORBIS_GEN2_ERROR_MEMORY_FAULT` on its failure paths **without writing RAX**.
4. The dispatcher then puts the status in RAX itself
   (`DirectExecutionBackend.Imports.cs:561-565`):
   ```csharp
   var returnValue = cachedExport.Function(cpuContext);
   if (!cpuContext.WasRaxWritten)
       cpuContext[CpuRegister.Rax] = unchecked((ulong)returnValue);
   ```
   `0x8002000E` as int32 is **negative**.
5. `js` is taken. The early-exit path prints the text with `puts` and returns `r14d = 1`.
6. Back at the assert site `test eax,eax` is non-zero, so **`int 0x41` executes**.

The MsgDialog acknowledgement path - the one that returns buttonId 1 and lets the game continue - is
**never entered**. Proof from the full census: `sceCommonDialogInitialize` 1 call, but
`sceMsgDialogInitialize` / `Open` / `UpdateStatus` / `GetResult` / `Terminate` **0 calls each**,
even though `0x800001CC5` is reached unconditionally by both arms of the branch above it. And
`snprintf` shows exactly **1** call, at the assert.

## Why everything downstream is idle

With the main thread trapped, nothing produces work. The per-thread snapshot
(`SHARPEMU_PERIODIC_SNAPSHOT_SECONDS=20`) shows the whole engine parked:

| threads | last import | state |
|---|---|---|
| 8 Draw*, 2 Gfx*StreamerThread, 8 WebApiJobWorker, Physics, Havok combiner, SystemService | `WKAXJ4XBPQ4` = `scePthreadCondWait` (22 total) | waiting for jobs |
| 8 BackgroundTaskWorker, 3 Havok Worker, SystemWakeup, ContentExportThread | `Zxa0VhQVTsk` = `sceKernelWaitSema` (13 total) | Blocked |
| 3 Sndz threads | `JTvBflhYazQ` = `sceKernelWaitEventFlag` | Blocked |
| PlayGo | - | **Exited** |

`GfxTextureStreamerThread` and `GfxModelStreamerThread` sit idle in a condvar wait, which is exactly
why **no asset is ever opened**: the streamers are alive and waiting for requests that never come.

## Two defects to fix

1. **`snprintf` must not report a negative.** Determine which of its two failure paths fires - the
   `TryReadCString` of the format at `0x808165AD6` (rodata) or the `TryWrite` to the guest stack -
   and fix it. C's contract is "characters that would have been written"; a negative return is
   reserved for encoding errors, and guests branch on it.
2. **Error statuses leak into RAX for libc functions.** The dispatcher's
   "if the handler did not write RAX, put the status there" rule is right for `sce*` calls that
   return an `SceError`, but wrong for libc functions whose return value has a completely different
   meaning. `snprintf` returning `0x8002000E` is indistinguishable from a legitimate `-2147352562`.
   Any libc export that fails should set RAX to the C-contract failure value explicitly.

Threads are classified `state=Running block=none` while parked in `scePthreadCondWait`, which is why
the stall watchdog never fired despite 22 idle threads. That classification is also worth fixing.

---

# CORRECTION: the `snprintf` root cause was WRONG (disproved by its own fix)

The section above claimed the boot stall was caused by `snprintf` returning a negative and diverting
the assert reporter to `int 0x41`. **That is wrong.** The libc return-contract repair was merged
(`56ae967`, `2306bcb`, `96be689`) and Astro was re-measured on the same VM with the same flags:

| | before | after |
|---|---|---|
| `snprintf` calls | 1 | 1 |
| `sceCommonDialogInitialize` | 1 | 1 |
| `sceMsgDialog*` calls | 0 | **0** |
| draws / presented_fps | 0 / 0.1 | **0 / 0.1** |
| assert printed | yes | yes |

Nothing changed.

**The disproof is in the data I already had, and I misread it.** `sceCommonDialogInitialize` is
called at `0x800001CAF`, which is *after* the `snprintf` branch at `0x800001BF9`. If that `js` had
ever been taken, control would have jumped to `0x800001E1F` and `sceCommonDialogInitialize` could
never have run. Its non-zero call count proves the early exit was **never taken** and that
`snprintf` was already returning a non-negative value. The reporter passes that branch fine.

Lesson: a call count of 1 on an export *downstream* of a suspected early-exit branch falsifies that
branch immediately. Check the downstream counts before building a causal chain on an upstream one.

## What is still true, and what the real question now is

Still measured and unchanged: the reporter reaches `sceCommonDialogInitialize` (1 call) and
`sceSysmoduleLoadModule`, but **never** `sceMsgDialogInitialize` (0 calls) - even though
`0x800001CB9` -> `0x800001CBE` -> `0x800001CC5` is straight-line code with no branch between them:

```
0x800001CAF: call sceCommonDialogInitialize   ; 1 call  <- reached
0x800001CB4: mov  edi, 0xA4                   ; module id = libSceMsgDialog
0x800001CB9: call sceSysmoduleLoadModule      ; called
0x800001CBE: mov  byte ptr [rip+0xE2D0AD5], 1
0x800001CC5: call sceMsgDialogInitialize      ; 0 calls  <- NOT reached
```

Three candidate explanations, none yet tested:
1. `sceSysmoduleLoadModule(0xA4)` does not return on our side.
2. `sceMsgDialogInitialize` **is** called but does not bind to our HLE export, so it never reaches
   the census. Astro imports it as `lDqxaY1UbEo#p#q`; we register it under
   `LibraryName = "libSceMsgDialog"`. The import binder matches on library name as well as NID, so a
   name mismatch would leave it unserved and invisible here. **Check this first** - it is the
   cheapest and it matches the evidence exactly.
3. The straight-line reading of that stretch is wrong.

Resolve it by booting with `SHARPEMU_LOG_ALL_IMPORTS=1` and grepping the load-time
`TryResolveDirectImportTarget` lines for the five MsgDialog NIDs, which states plainly whether each
bound to HLE or fell through.

## The libc work stands on its own

The wave is still worth keeping, independent of this misdiagnosis. It fixed **real** contract
violations found by a reflection sweep over the whole libc surface - 26 exports were returning
`ORBIS_GEN2_ERROR_MEMORY_FAULT` (`0x8002000E`) into RAX where C callers expect a byte count, a
pointer, or `EOF`. Those are latent guest-visible bugs regardless of Astro. Merged with 17 new tests
(1598 -> 1615 passing, 0 errors, no new warnings) plus `LibcContractSweepTests`, which now fails the
build if any libc export hands a non-OK status to the dispatcher's RAX fallback.

---

# THE BLOCKER, LOCATED: the main thread never returns from `libc:fflush`

Measured with `SHARPEMU_PERIODIC_SNAPSHOT_SECONDS=15`, 12 snapshots over ~200 s. The main guest
context is **byte-identical in every one**:

```
rip=0x00007000000052A0  rsp=0x00007FFFF07FF418  rbp=0x00007FFFF07FF810
rax=0  rbx=0x8080D6201  rcx=0  rdx=0x…C381D1  rsi=0x…C381D0  rdi=0x…C342B8
Stall import-stub: rip=0x00007000000052A0  nid=MUjC4lbHrK4 -> libc:fflush
```

Return address on the stack: `0x800001C61` in the **first** snapshot, then `0x800001E2B` for the
remaining **11**. So the thread advanced once and then stopped permanently.

`0x800001C61` is the return of the reporter's first `fflush` (called at `0x800001C5C`);
`0x800001E2B` is the return of the call at `0x800001E26`. Both are inside the assert reporter
`0x800001AA0`.

Identical rip **and** identical rsp/rbp/registers across 12 samples spanning three minutes is not a
thread that is being sampled at a busy address - it is a thread that is not running. A guest thread
executing inside an HLE export keeps its recorded guest rip parked at the import stub, so this is
the signature of **an HLE call that never returns**.

## What this overturns

Everything previously proposed as the blocker was downstream of this and is now moot:

- not a condvar/semaphore wait (the 22 `scePthreadCondWait` threads are idle *because* nothing
  produces work)
- not MsgDialog import binding (`sceMsgDialogInitialize` is never reached because the thread never
  leaves `fflush`)
- not `sceSysmoduleLoadModule`, not the GPU event path, not asset streaming

The zero draws, zero flips and zero asset opens are all consequences of one hung export.

## Next step

Instrument `LibcStdioExports.Fflush` (`src/SharpEmu.Libs/LibcStdioExports.cs:561`) directly - log
entry/exit with the handle value - and determine what does not return. `rdi = 0x…C342B8` is a host
pointer, so the `handle == 0` "flush every stream" branch is not the one taken; it reaches
`_fileHandles.TryGetValue(handle, out var stream)` and then `stream.Flush()`.

Candidates worth checking in order: a `Flush()` on a stream whose underlying handle is a console or
pipe that blocks; contention on `_fileHandles` if it is a plain `Dictionary` mutated from the other
~30 guest threads; and any lock taken by the export-dispatch wrapper around this call.

---

# THE ACTUAL BRANCH: a guest flag at `0x80E2D2798` skips the dialog and forces `int 0x41`

Two earlier claims in this document are wrong and are superseded here:

- "`snprintf` returns negative and the `js` at `0x800001BF9` is taken" - **wrong**, disproved twice.
- "the main thread is wedged inside `libc:fflush`" - **wrong**. A `StdioCallTrace` that bypasses
  `Console` shows four clean `fflush` enter/exit pairs, microseconds each, and **zero** `puts`
  calls. The rip parked at the fflush stub is simply the last import boundary recorded; it is stale.

## The evidence

Per-thread import history for the main thread (`managed=2`), from the backend diagnostic dump:

```
#736718 g8cM39EUZ6o sceSysmoduleLoadModule ret=0x800F41C19
#736719 eLdDw6l0-bU snprintf               ret=0x800001BF7   <- the reporter's snprintf, returns fine
#736721 9UK1vLZQft4 scePthreadMutexLock    ret=0x263A89509B9 <- inside libc.prx (host address)
#736724 FxVZqBAA7ks <unnamed>              ret=0x263A89606E9
#736727 9UK1vLZQft4 scePthreadMutexLock    ret=0x263A89509B9
#736729 FxVZqBAA7ks <unnamed>              ret=0x263A89606E9
#736733 MUjC4lbHrK4 fflush                 ret=0x800001C61   <- LAST import the thread ever makes
```

`ret=0x800001BF7` matches the disassembly of the `snprintf` call site exactly, so that call
completed and the `js` was **not** taken. After `fflush` returns to `0x800001C61` the thread never
issues another import - it never reaches `sceSystemServiceHideSplashScreen` at `0x800001CAA`.

What sits in that gap:

```
0x800001C61: cmp  byte ptr [rip+0xE2D0B30], 0    ; flag at guest 0x80E2D2798
0x800001C68: jne  0x800001E2B                    ; flag set -> jump to the reporter tail
0x800001C6E: movzx eax, byte ptr [rip+0xE2D0B2B]
0x800001C77: je   0x800001E69
0x800001C9E: call qword ptr [rax+0x10]
0x800001CA1: cmp  byte ptr [rip+0xE2D0AF2], 0    ; separate "dialog module loaded" flag @0x80E2D279A
0x800001CAA: call sceSystemServiceHideSplashScreen
0x800001CAF: call sceCommonDialogInitialize
0x800001CC5: call sceMsgDialogInitialize
```

`0x800001E2B` is `mov eax, r14d`, and `r14d` is the reporter's third argument, which is `1` at the
Astro call site. So the reporter returns **1**, the assert site's `test eax,eax` is non-zero, and the
guest executes `int 0x41`.

This single branch accounts for every symptom: no `puts`, no `sceMsgDialog*`, no dialog
acknowledgement, and a trapped main thread whose idle worker pools then explain the zero draws,
zero flips and zero asset loads.

## What to do next

Find why the byte at guest `0x80E2D2798` is non-zero. It is almost certainly a
"assertions already reported / non-interactive / dialog unavailable" latch the engine sets, and on a
retail console it would be clear at this point so the acknowledge path runs.

1. Probe it: `SHARPEMU_PROBE_SPEC` with `{"at":"0x80E2D2798","as":"hex","len":"0x8"}` on an early
   export to confirm it is set, and when.
2. Find its writers by displacement scan, the same way `+0x2900` was found - but note the flag is a
   **rip-relative absolute**, so scan for `lea`/`mov` against the computed target rather than a
   struct offset.
3. The adjacent byte `0x80E2D279A` is a different latch, written at `0x800001CBE` after the dialog
   module loads. Do not confuse the two.

---

# MEASURED: `flagA = 1`, the branch is real, and my scan method had a blind spot

Probed the three guest globals at runtime (`SHARPEMU_PROBE_SPEC` on `sceAudioOut2ContextPush`):

```
flagA     @0x80E2D2798 = 01      <- set
guard     @0x80E2D27A0 = 01      <- magic static initialized, healthy
singleton @0x80E2D27A8 = 0x31484F020   <- valid pointer, healthy
```

So `cmp byte [0x80E2D2798],0 / jne 0x800001E2B` **is taken**. The reporter jumps over the dialog
block, returns `r14d = 1`, and the guest executes `int 0x41`. The trap vector documented in the
previous section is confirmed by measurement, not inference.

The magic-static guard and its singleton are fine, so the `0x800001C7D` null-dereference theory is
dead too.

## Who sets it

A one-instruction setter, alone between `int3` padding - a plain `SetSilentAsserts(bool)`:

```
0x8000018D0: mov byte ptr [rip+0xE2D0EC1], dil   ; flagA = (bool)arg0
0x8000018D7: ret
```

Exactly **one** caller: `0x8002B2AC6`. So the title deliberately puts its assert reporter into a
non-interactive mode during startup. That is normal retail behaviour - and it means the assert must
simply never fire on a real console, which puts the question back where it started: **why is
`defaultBusses` empty here when it has one entry on hardware?**

## A method defect that invalidates an earlier conclusion

The writer above was **missed** by the earlier scan, which only recognised four opcode forms
(`C6 05`, `88 05`, `0F B6 05`, `80 3D`). `mov byte ptr [rip+d32], dil` is `40 88 3D ...` - a REX
form outside that set. The scan reported "1 reference, nothing writes it", which was false.

The same limited technique produced the earlier claim that **nothing in the binary populates the
sound source vectors** (`+0x2658 / +0x2698 / +0x26b8`). That conclusion is now **unsafe** and must be
re-run with the displacement-first method used here:

> For every offset in the code segment, read the `disp32` and test whether
> `instruction_end + disp == target` for instruction ends at `disp+4/5/6/8`. This finds the
> reference regardless of opcode or REX prefix, then decode backwards to identify it.

That method found this writer immediately after the opcode-list method missed it. Re-running it on
the sound vectors is the next concrete step, and it may well overturn "nothing ever populates them",
which was the foundation of the whole bus investigation.

## Re-scan result: the sound-vector conclusion SURVIVES

The blind spot above was real, so the sound source vectors were re-scanned with the wider method
(back-offsets 2..11 instead of 3,4,7,8). It does **not** overturn the earlier finding.

Inside the SoundManager region (`0x800DB0000`-`0x800F70000`) the wider scan adds only:

| hit | verdict |
|---|---|
| `0x800DBF672: adc dword ptr [rbx+0x2658], ecx` | **misdecode** - lands 2 bytes inside the ctor's `vmovups ymmword ptr [rbx+0x2658], ymm1` at `0x800DBF670` |
| `0x800DBF682` / `0x800DBF68A` (`+0x2698`, `+0x26b8`) | same, inside the ctor's other two `vmovups` |
| `0x800F63988: mov byte ptr [rbx+0x2660], 0` | a single-byte store - a `std::string` SSO terminator on a different object, not a vector pointer |

Everything else the wider scan surfaced is outside the sound region on unrelated classes that happen
to share the displacement.

So the original statement holds: **the three source vectors are written only by the constructor's
zeroing, and nothing ever populates them.** The wider back-range mostly buys false positives from
misaligned decodes, which is the cost of the opcode-agnostic method - it must be paired with a
"does this decode start where a real instruction starts" check before its hits are trusted.

Net: the method defect was genuine and worth fixing, but on this particular question it changes
nothing. The bus investigation's foundation is intact.

---

# NARROWED TO 13 INSTRUCTIONS, and `int 0x41` is NOT involved

Two measurements kill the remaining theories.

**1. `defaultBusses` is never populated at any point in the boot.** Probed at four different stages -
`sceAudioOut2ContextPush#0` (early), `sceAgcSuspendPoint`, `sceVideoOutRegisterBuffers2`, and all
three `sceAgcDriverSubmitDcbRange` calls (render setup). Every sample:
`begin=0 end=0 cap=0`. `cap = 0` throughout means storage is never allocated at any time, so the
"the assert just fires too early, it fills in later" ordering theory is dead.

**2. `int 0x41` never executes.** `TryHandleGuestAssertTrap`
(`DirectExecutionBackend.Exceptions.cs:601`) already implements exactly the retail semantics -
detect `0xCD`, log `non-fatal, continuing`, and resume at `rip + 2`. Grepping every captured log
from every run: **zero** `Guest engine assert` lines. The trap is never reached, so it was never the
blocker, and `SHARPEMU_ASTRO_ASSERT_SKIP`'s benefit must come from something else.

## The remaining window

The main thread's last import is `fflush` returning to `0x800001C61`; `flagA = 1` sends it to
`0x800001E2B`. From there the reporter's epilogue is only 13 instructions:

```
0x800001E2B: mov   eax, r14d           ; r14 = 1, confirmed in the thread snapshot
0x800001E2E: movzx eax, al             ; eax = 1
0x800001E31: test  rbx, rbx
0x800001E34: mov   rcx, qword ptr [r12]      ; stack-cookie load  <-- prime suspect
0x800001E38: cmp   rcx, qword ptr [rbp-0x30]
0x800001E3C: jne   0x800001E62               ; -> __stack_chk_fail (Ou3iL1abvng) + ud2
0x800001E3E: add   rsp, 0x3C8
0x800001E45..0x800001E4E: pop rbx/r12/r13/r14/r15/rbp
0x800001E4F: ret                             ; -> assert site -> int 0x41
```

Ruled out by log evidence: `__stack_chk_fail` is **never** called, and `int 0x41` **never** executes.
So the thread neither takes the cookie-failure branch nor completes the return. It dies inside this
epilogue.

Prime suspect is `mov rcx, qword ptr [r12]` at `0x800001E34`. The thread snapshot recorded
`r12 = 0x0000028696D35630`, a **host** address - this is the stack-guard global load, and every run
sets `SHARPEMU_IGNORE_STACK_CHK=1`, which is itself an admission that guard handling here is not
faithful. A fault on that load would be an access violation delivered to the VEH, where
`TryHandleGuestAssertTrap` declines it (no `0xCD` at rip) and the thread is lost without any assert
log - which is precisely what the evidence shows.

## Where the fix belongs

Not in the HLE exports. This is the CPU/exception layer -
`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.*`, owned by the other session. The concrete
handoff:

1. Log every exception the VEH declines, with `rip`, exception code and faulting address. Right now
   an unhandled guest fault leaves **no trace at all**, which is why this took so long to corner.
2. Check what `r12` should hold at `0x800001E34` and whether the stack-guard global is mapped for
   this thread.
3. Revisit `SHARPEMU_IGNORE_STACK_CHK`: if the cookie is not faithfully maintained, the compare at
   `0x800001E38` is meaningless and the load feeding it may be the actual fault.

## CORRECTION and elimination: there is no fault at all

The previous section claimed "an unhandled guest fault leaves no trace at all" and proposed adding
decline-path logging. **That was wrong.** `DirectExecutionBackend.Exceptions.cs:166-210` already
prints a full `NATIVE EXCEPTION CAUGHT!` dump - code, address, rip, host thread, all 16 GPRs - for
any exception the VEH declines, plus `LogAccessViolationTrace` for `0xC0000005` and a
`VEH_FASTFAIL` line for fast-fail.

Grepped across every captured run: **zero** `NATIVE EXCEPTION CAUGHT`, zero access-violation traces,
zero `VEH_FASTFAIL`. The diagnostic exists and is silent because nothing faults.

So the stack-cookie-load theory for `0x800001E34` is dead too. Full elimination for the main thread:

| candidate | verdict | evidence |
|---|---|---|
| access violation / any fault | **no** | zero `NATIVE EXCEPTION CAUGHT` in any run |
| `int 0x41` assert trap | **no** | zero `Guest engine assert` lines; handler resumes at rip+2 anyway |
| `__stack_chk_fail` | **no** | never called |
| wedged inside an HLE export | **no** | `StdioCallTrace` shows clean `fflush` enter/exit pairs |
| condvar / semaphore wait | **no** | main thread is not in the guest-thread registry at all |
| ordering (populated later) | **no** | `defaultBusses cap=0` at four stages across the whole boot |

## What is left

Everything above is consistent with exactly one remaining explanation: **the main guest thread stops
being scheduled.** It does not fault, block, trap, or exit-with-a-record; it simply never runs again.
Supporting detail that was visible all along and not used: the periodic snapshot lists ~30 guest
threads by name - including `PlayGo` in state `Exited` - and the **main thread is not among them**.
Its context survives only as the backend's `_cpuContext`, which is why the same rsp/rbp/registers
repeat for twelve consecutive snapshots.

A thread that is not in the registry cannot be picked up by `StartReadyThreadDispatcher` or
`WakeExpiredBlockedGuestThreads`, which is precisely the shape of the observed freeze.

Next check, in this order:
1. Confirm whether the main/entry thread is ever registered as a guest thread, and if not, what is
   supposed to resume it after an HLE call returns.
2. Instrument the return-from-HLE path for the entry thread specifically - the last recorded event
   is `fflush` returning to `0x800001C61`, so the question is whether guest execution resumes at
   that address at all.

This is emulator scheduling, in `DirectExecutionBackend.*`. It is not guest logic, not the assert,
and not a libc contract.

---

# LOCATED: `__cxa_guard_release` is entered and never returns

Added `ExportCallTrace` (`src/SharpEmu.HLE/Diagnostics/ExportCallTrace.cs`), which brackets **every**
HLE export with an enter/exit record written straight to a file, bypassing `Console`. It hooks the
existing census decorator, so it covers the whole export surface, and it is independent of the
census because the census only records calls that *completed* - which is precisely the blind spot.

One boot, `SHARPEMU_LOG_EXPORT_CALLS=1`, **624,725 records**. Exactly one thread has a dangling
enter:

```
tid=2  depth=1  last_entered=libc:__cxa_guard_release
```

and the tail of the log is the magic-static sequence from `0x800001E69` that the disassembly
predicted:

```
2767526528 tid=2 exit  libc:__cxa_guard_acquire
2767528727 tid=2 enter libSceLibcInternal:sceLibcMspaceMalloc
2767531710 tid=2 exit  libSceLibcInternal:sceLibcMspaceMalloc
   (x3 - the object being constructed)
2767550953 tid=2 enter libc:__cxa_guard_release      <-- never exits
```

The main thread acquires the guard, constructs the singleton, calls `__cxa_guard_release`, and that
call never returns. Everything downstream - no further imports, frozen registers, all worker pools
idle, zero draws, zero flips, zero asset loads - follows from this one call.

This also explains the earlier confusion: the thread snapshot reports a parked thread by mapping its
recorded guest rip to an import stub, and that rip is only refreshed at import boundaries. It
pointed at `fflush` because that was the last boundary it recorded, which is why "wedged in fflush"
looked plausible and was wrong.

## What is not the cause

`CxaGuardRelease` (`src/SharpEmu.Libs/CxxAbiExports.cs:105`) has no blocking construct on
inspection:

- `lock (state)` - only `CxaGuardRelease` ever locks a `GuardState`; `CxaGuardAcquire` spins with
  `SpinWait` **without** holding it, so there is no lock inversion.
- `LogGuardState` / `LogGuardResult` - both early-return unless `SHARPEMU_LOG_GUARDS=1`, which was
  not set in this run.
- `TryWriteGuardState` - a plain `TryReadUInt64` / `TryWriteUInt64` read-modify-write.

The `exit` record lives in a `finally`, so the delegate did not return, throw, or unwind normally.

## Next suspect

The only thing `__cxa_guard_release` does that reaches outside managed code is the **guest memory
write** in `TryWriteGuardState` (`ctx.TryWriteUInt64`). Prosperismo has a guest write-watch path
(`GuestWriteWatch`, wired to `HleEffectCensus.CountsGuestWrites` / `NoteGuestWrite`) and a VEH that
intercepts guest page faults. A write that traps into that machinery and never comes back matches
every observation.

Instrument inside `CxaGuardRelease` at statement granularity - a record before and after
`TryWriteGuardState`, before and after `_inProgress.TryRemove` - and the exact statement falls out in
one boot.

## CORRECTION: `__cxa_guard_release` does not hang - the stall is a race

The previous section reported a dangling `libc:__cxa_guard_release` enter and named it the blocker.
Re-running with statement-level records **inside** that export retracts it.

Run 2 (`SHARPEMU_LOG_EXPORT_CALLS=1`, plus `step:` records bracketing each statement of
`CxaGuardRelease`), **637,558 records**:

```
--- dangling (entered, never exited) ---
NONE - every export returned

--- guard_release step records ---
tid=2 step:lock-enter    libc:guard_release
tid=2 step:before-write  libc:guard_release
tid=2 step:after-write   libc:guard_release
tid=2 step:after-remove  libc:guard_release      (x2, both complete)
```

Every export returns, `guard_release` completes all four statements twice, and the game **still
stalls**. So:

- run 1: dangling `__cxa_guard_release`, ~624,725 records
- run 2: no dangling call at all, ~637,558 records, same stall

**The stall is timing-sensitive.** Adding four file-append records was enough to move it. That rules
out a deterministic hang inside any single HLE export and reframes the whole thing.

## What the two runs jointly establish

For the main thread (`tid=2`), across both runs:

| candidate | verdict |
|---|---|
| fault / access violation | no - zero `NATIVE EXCEPTION CAUGHT` |
| `int 0x41` trap | no - never executes |
| `__stack_chk_fail` | no - never called |
| a hung HLE export | **no** - run 2 shows every export returning |
| condvar / semaphore wait | no - not in the guest-thread registry |
| ordering (vector filled later) | no - `cap=0` at four stages |

The thread issues ~637k export calls, every one returns, and then it stops executing guest code
without faulting, trapping, blocking or exiting. That is the signature of a **race in the emulator's
guest execution path**, not of a missing or wrong Prospero API.

Also worth recording: all 637,558 traced calls carry `tid=2`. Guest threads have distinct host
thread ids in the snapshot (`host_tid=3392, 6836, 6524, ...`), so either the other threads issue no
HLE calls in this window or they are being funnelled through one host thread. Which of those is true
is worth settling, because a cooperative funnel through a single host thread is exactly the kind of
structure where a lost wake-up produces this symptom.

Next: instrument the guest execution loop itself (enter/exit around the dispatch of the entry
thread), not the exports. The question is no longer "which call hangs" - it is "why does the loop
stop issuing guest instructions".

## The tracer invalidated its own first two runs - and what the fixed one shows

**Methodological failure, recorded so it is not repeated.** The first `ExportCallTrace` appended to a
file under a global lock on **every** export call. Checking which threads and libraries appeared in
its output exposed the problem:

```
run 2: TOTAL=637,558   distinct tids: tid=2 only
       libraries: libSceLibcInternal 320,734 / libc 263,770 / libKernel 53,052
       AudioOut2 records: 0
```

Untraced runs of the same length reach 1.6M-2.6M import calls **including ~38k AudioOut2 calls**.
This run never reached audio init at all. The instrument was so expensive it changed the boot it was
measuring, so **both traced runs are void**: run 2's "every export returned" only means it never got
as far as the stall, and run 1's dangling `__cxa_guard_release` cannot be trusted either.

Rebuilt as a fixed in-memory ring (65536 records) flushed by a background thread every 2 s. Hot path
is now one `Interlocked.Increment` and one array store - no lock, no allocation, no I/O.

Re-run with the buffered version:

```
RING_RECORDS=65536
tids in window: tid=54 (62,112)  tid=52 (3,424)
AudioOut2 records: 9,385           <- reached the real stall state
dangling: tid=54 depth=0 · tid=52 depth=0     <- nothing hung
```

Audio runs, so this run reached the same state as the untraced boots, and **no export is dangling on
any thread**. The main thread (`tid=2`) does not appear in the window at all: the audio threads
produced 65k events in the sampling window while it produced none.

## Where that leaves the main thread

Established, now with an instrument that does not distort the run:

- it is not inside any HLE export (no dangling enter, anywhere)
- it does not fault, trap, or hit `__stack_chk_fail`
- it is not in the guest-thread registry, so it is not in a tracked wait
- it stops issuing export calls entirely, while other threads keep running normally

One explanation covers all of it: **the guest is spinning in code that calls no imports** - a
CPU-only wait on a memory location that nothing ever updates. That produces exactly this signature:
no exports, no faults, no traps, a thread that is "running" but never progresses, and a recorded
guest rip frozen at the last import boundary (which is why `fflush` looked like the culprit for
several rounds).

To confirm, sample the **host** rip of the thread executing the entry point while it is stalled. The
periodic snapshot already prints `host_rip` for registry threads; the entry thread is not in the
registry, which is precisely why its host state has never been captured. That is the gap to close
next, and it is a smaller change than anything attempted so far.

---

# ROOT LOCALIZATION: the main thread spins on a `jne` in generated stub code

Built `StalledThreadSampler` (`src/SharpEmu.HLE/Diagnostics/StalledThreadSampler.cs`): every thread
that makes an HLE call registers its native thread id, and a background sampler reports the **host**
rip of any thread silent past a threshold. This closes the gap that made the last several rounds
guesswork - the periodic snapshot prints `host_rip` only for registry threads, and the entry thread
is never registered.

Two fixes were needed before it produced anything: the AMD64 `CONTEXT` record must be 16-byte
aligned (a managed struct is not, so the first version reported `<unavailable>` for every thread),
and address classification needs `VirtualQuery` plus a module scan to tell generated code from a DLL.

## Result

```
managed=2  last_export=fflush  host_rip=0x...0285
           region=private base=0x...0000 size=0x1000
           state=MEM_COMMIT type=MEM_PRIVATE prot=PAGE_EXECUTE_READ offset=0x285
           bytes=0F 85 E0 FF FF FF 41 C7 41 08 01 00 00 00 E9 0B

managed=6  last_export=sceKernelWaitSema  region=module:ntdll.dll+0x160BB4
```

Every other thread is in `ntdll.dll` - an ordinary wait. The main thread is in a **4 KB private
executable page owned by no module**, i.e. emulator-generated code, and the bytes decode to:

```
rip      : jne  rip-0x1A        ; 0F 85 E0FFFFFF, backward 32 -> spin loop
rip+0x06 : mov  dword [r9+8], 1 ; the "acquired" store, just past the branch
rip+0x0E : jmp  ...
```

It is sitting on a **backward conditional jump**: a compare-and-retry loop whose condition never
becomes true, with the success store immediately after the branch it never falls through.

## Why every previous answer was wrong

The thread is **executing**, continuously, in generated code that issues no HLE calls. That is why:

- no export ever dangles (it is not inside one)
- no fault, no `int 0x41`, no `__stack_chk_fail` (it is not faulting)
- it is absent from the guest-thread registry (the entry thread is never registered)
- its recorded guest rip stays frozen at the last import boundary - which is `fflush`, and is exactly
  why "wedged in fflush" looked convincing for several rounds

A spin in generated code is invisible to every instrument that keys off imports, faults or waits.
That is the lesson worth keeping.

## Next

Identify which stub owns that page and what its retry condition is. The page is 4 KB, private,
`PAGE_EXECUTE_READ`, and the loop body is ~26 bytes ending in `jne`. Candidates are the guest atomic
/ lock-acquire helpers and the import trampolines. Dump the whole page from `base` and disassemble
it - the surrounding code names the stub, and `r9` at the sample point identifies the object being
waited on.

## The stub, decoded: a recursive mutex that is never released

Widening the sample to `r9` plus a 128-byte window around the rip identifies it exactly. The page is
a generated **recursive-mutex acquire** wrapper:

```
0x...258: movabs r9, 0x20E14880000        ; lock bookkeeping block
0x...262: mov    r10, qword ptr gs:[0x48] ; TEB ClientId.UniqueThread - the host thread id
0x...26B: mov    rax, qword ptr [r9]      ; reload owner            <-- retry target
0x...26E: cmp    rax, r10
0x...271: je     0x...29F                 ; already mine -> recursive
0x...277: test   rax, rax
0x...27A: jne    0x...298                 ; held by another -> pause; retry
0x...280: lock cmpxchg qword ptr [rcx], r10   ; try to claim
0x...285: jne    0x...26B                 ; claim FAILED -> reload  <-- STALLED HERE
0x...28B: mov    dword ptr [r9 + 8], 1    ; acquired, recursion = 1
0x...293: jmp    0x...2A3
0x...298: pause
0x...29A: jmp    0x...26B
0x...29F: inc    dword ptr [r9 + 8]       ; recursive re-entry
0x...2A3: mov    rcx, r13
0x...2A6: movabs rax, 0x7FFD75D2B4B0
0x...2B0: call   rax                      ; the guarded call
0x...2B2: movabs r9, 0x20E14880000
0x...2BC: dec    dword ptr [r9 + 8]       ; release
```

The main thread cycles `reload owner -> cmpxchg -> fail -> reload` forever. This is a **spin-lock
deadlock in emulator-generated code**, not guest logic: the owner word is a **host** thread id read
from the TEB, and the guarded target `0x7FFD75D2B4B0` is a fixed address in a loaded module.

Sampled values, same run: `r9 = 0x20E14880000` (lock block), page base `0x20E14960000`, and the
guarded call target in the `0x7FF...` module range.

## Why this is the whole story

A thread spinning on `lock cmpxchg` is running at 100% and issuing no HLE calls, which is exactly the
signature that defeated every earlier instrument:

- no dangling export - it is not inside one
- no fault, no `int 0x41`, no `__stack_chk_fail` - it never faults
- absent from the guest-thread registry - the entry thread is never registered
- guest rip frozen at the last import boundary (`fflush`) - which is why that looked like the culprit
  for several rounds

Everything downstream - 22 threads idle in `scePthreadCondWait`, `defaultBusses` never populated,
zero draws, zero flips, zero asset loads - follows from the main thread never getting this lock.

## Next, and it is small

1. Dump the owner word `[r9]` and the lock word `[rcx]` at sample time and match the owner against
   the live host thread ids. That names the holder outright.
2. If the owner is a thread that has exited, this is a lock leaked by a dead thread. If the owner is
   alive and itself blocked, it is a lock-order inversion between this stub and one of the
   `sceKernelWaitSema` waiters.
3. Find which emitter generates this page - grep the backend for the byte pattern
   `65 4C 8B 14 25 48 00 00 00` (`mov r10, gs:[0x48]`) or for `lock cmpxchg` emission - and the
   owning subsystem falls out.

## Confirmed: the spinning thread is not in any HLE export - and a caveat on the instrument

Both instruments in one run (`SHARPEMU_LOG_EXPORT_CALLS=1` with a 262144-entry ring, plus
`SHARPEMU_SAMPLE_STALLED_THREADS=1`):

```
managed=2 ... host_rip=...0285 region=private ... r9=0x...810000
tid=2 depth=0 last_entered=<none>   (records for tid=2: 0)
```

**Zero export records for `tid=2`** across the whole window, depth 0. The stalled thread is not
inside an HLE export - it is spinning in the generated recursive-mutex stub, which Prosperismo does not
emit: grepping the whole tree for the emitted bytes (`65 4C 8B 14 25 48 00 00 00`,
`F0 4C 0F B1`, `0x0F, 0xB1`) finds nothing, and the only gs-prefixed emitters in
`PosixHostStubs.cs` are TLS reads, not locks.

The guarded call target's low bits are identical across runs under ASLR
(`0x7FFD75D2B4B0`, `0x7FF8D8D2B4B0`, ...`D2B4B0`), so it is the same function in the same module
every time. A recursive lock keyed on the TEB thread id wrapping a single DLL call is the shape of a
**.NET runtime stub** - lazy P/Invoke resolution or a similar one-time-init guard.

### Caveat: `StalledThreadSampler` can deadlock what it observes

`DescribeAddress` calls `Process.GetCurrentProcess().Modules`, which takes the OS loader lock, and it
does so on the sampler thread while a **suspended** thread may hold that same lock. That is a real
deadlock hazard introduced by the diagnostic itself, and it overlaps suspiciously with the suspected
culprit (a lazy P/Invoke resolution lock).

It does **not** explain the underlying stall - the game parks identically in runs with no sampler at
all, and did so long before the sampler existed - but any conclusion drawn about *this specific lock*
from a sampler run must account for it.

**Fix the instrument before trusting it further:** resolve module ranges once at startup and cache
them, so the sampler never enumerates modules while holding a thread suspended. Then re-confirm.

### Next

Capture the lock's owner without touching the loader: read `[r9]` (owner thread id) and `[rcx]`
(lock word) directly in the sampler and print them raw. If the owner is a live thread, print what
that thread is doing; if it is a dead or suspended thread, the lock was leaked. That is a read of two
pointers and needs no module enumeration.

## ROOT CAUSE: the spin is a CAS that can never succeed

With module ranges cached at startup (so the sampler no longer touches the loader lock while a
thread is suspended) and the lock bookkeeping read raw:

```
managed=2  host_rip=...0285
r9=0x...948F0000  owner_tid=0  recursion=0
rcx=0x000000A5FAD7B7E0  lockword=0x000000A5FAD7BF70
```

`owner_tid = 0` is decisive. Walk the loop with those values:

```
0x26B: mov  rax, [r9]          ; rax = 0        (owner word says UNOWNED)
0x26E: cmp  rax, r10 / je      ; 0 != my tid    -> not taken
0x277: test rax, rax / jne     ; rax == 0       -> not taken
0x280: lock cmpxchg [rcx], r10 ; compares [rcx] against RAX = 0
0x285: jne  0x26B              ; [rcx] = 0xA5FAD7BF70 != 0 -> ALWAYS FAILS
```

`rax` is reloaded from `[r9]` every iteration and is always `0`; the CAS compares against `rax` but
targets `[rcx]`, which is never `0`. **The compare-and-swap can never succeed, and the loop can never
exit.** It is not waiting for another thread to release anything - `owner_tid` is 0, so by the stub's
own bookkeeping the lock is free.

The owner word `[r9]` is a fixed global; the CAS target `[rcx]` is a **stack address**
(`0xA5FAD7B7E0`) holding another nearby stack address (`0xA5FAD7BF70`, +0x790). A lock word that
contains a pointer to a wait block on a thread stack is the representation Windows `SRWLOCK` /
`CONDITION_VARIABLE` use when contended. So the stub is hand-rolling a CAS against a lock whose
value is not the simple 0/owner-id word it assumes, and livelocks the moment that lock is contended.

That the two locations disagree - `[r9]` says free, `[rcx]` says held - is the bug in one line.

## Status of this investigation

Located: a livelocking compare-and-swap in a generated stub Prosperismo does not emit, on the guest's
main thread, with the exact instruction, register values and failure mechanism captured. Everything
downstream (idle worker pools, empty `defaultBusses`, zero draws, zero flips, zero asset loads)
follows from the main thread never leaving this loop.

Not yet established: which component emits the page. The tree contains no matching emitter, the
guarded call sits at a fixed offset in a loaded module across runs, and the shape points at a .NET
runtime stub. Identify it by resolving `0x...D2B4B0` against the cached module table (the sampler now
has one) and printing the owning module and offset for the guarded call target, not just for the rip.

## The stub is JIT'd managed code - and it is NOT the backend gate

`Process.Modules` lists only loaded PE images, so `module:<none>` for both the stub page and its
guarded call target (after refreshing the table on a miss, in case of late loads) is itself the
answer: **both are JIT-compiled managed code**. Thread-id ownership + a recursion counter + an inline
`lock cmpxchg` is .NET's own `lock` fast path (`System.Threading.Lock` / `Monitor`), JIT-inlined -
not a hand-written stub, which is why grepping the tree for the emitted bytes found nothing.

That made the backend's global gate the obvious suspect: `LockGate` is
`Monitor.Enter(_guestThreadGate)` with eight-plus call sites, and it already records
`_gateOwnerSite`, `_gateOwnerManagedThreadId` and `_gateAcquireTimestamp` - **write-only bookkeeping
that nothing ever printed.** Wired it into the stall snapshot.

Result, at every stall snapshot:

```
[LOADER][ERROR] Stall gate: free
```

**The gate is not held.** So the main thread is not starving on the backend's guest-thread gate, and
that whole family of call sites is eliminated.

## Where this investigation actually ends

Established by measurement, with the instrument to reproduce each:

- the main thread spins in JIT'd managed code implementing a .NET lock fast path
- `owner_tid = 0`, `recursion = 0`, yet the CAS target `[rcx]` holds a stack pointer, so the
  compare-and-swap compares 0 against a non-zero word and **can never succeed**
- it is not inside any HLE export (zero records for `tid=2` in a 262144-entry window)
- it does not fault, trap, or hit `__stack_chk_fail`
- it is **not** the backend's `LockGate` (reported free at every snapshot)

Still unidentified: which managed lock. The remaining approach that does not require guessing is a
managed stack trace for that thread rather than a raw rip - either a debugger attach on the stalled
process, or `ClrMD`/`dotnet-dump` against a dump captured while it spins. That names the C# frame
outright instead of inferring it from a code page, and every inference-based step from here has a
poor track record in this document.

---

# FIXED: a missing REX.B bit made the VEH spinlock livelock

The managed stack trace ended the guesswork. `dotnet-stack report` against the emulator process
(note: `SHARPEMU_DISABLE_MITIGATION_RELAUNCH=1` is required, otherwise you attach to the launcher,
which only shows `Program.TryRunMitigatedChild`) gave 48 threads with real frames. The main thread:

```
Thread (0xD5C):
  [Native Frames]
  DirectExecutionBackend.CallNativeEntry(void*)
  DirectExecutionBackend.ExecuteEntry(...)
  DirectExecutionBackend.TryExecute(...)
  CpuDispatcher.DispatchEntryCore(...)
  SharpEmuRuntime.Run(...)
```

No managed frames above `CallNativeEntry`, so it was **not** in a .NET lock - correcting the previous
section's inference from `module:<none>` (emulator-emitted stubs are outside PE modules too, not just
JIT'd code). It was in an emitted stub, which located the emitter immediately.

## The bug

`DirectExecutionBackend.cs`, the VEH-entry recursive spinlock (and its guest-side twin):

```
0x49 0xB9 <imm64>       mov r9, lock*        REX.W|REX.B  ✓
0x49 0x8B 0x01          mov rax, [r9]        REX.W|REX.B  ✓
0x41 0xC7 0x41 0x08     mov dword [r9+8], 1  REX.B        ✓
0x41 0xFF 0x41 0x08     inc dword [r9+8]     REX.B        ✓
0xF0 0x4C 0x0F 0xB1 0x11   "lock cmpxchg [r9], r10"       <-- REX.B MISSING
```

`0x4C` is `REX.W|REX.R` with no `REX.B`, so modrm `0x11` (rm=001) decodes as **`[rcx]`**, not
`[r9]`. The exchange targeted whatever `rcx` happened to hold.

Consequence, exactly as sampled: the owner load reads `[r9]` and sees `0` (free), so `rax = 0`; the
exchange compares `0` against `[rcx]`, a stack address that is never `0`; the branch retries and
re-reads the same free owner word. **An unconditional livelock**, entered the first time the VEH
serialisation path was taken. Measured values that pin it: `owner_tid=0 recursion=0
rcx=0x...B8A0 lockword=0x...C030` (a stack pointer).

Fix: `0x4C` -> `0x4D` at both sites. One bit, two bytes.

## Verified against Astro Bot

| | before | after |
|---|---|---|
| `Stalled thread: managed=2` reports | every run | **0** |
| `Guest engine assert` (int 0x41 handled) | **0 in every run** | **1** |
| main thread | spins forever in the stub | runs on |

The `int 0x41` handler firing for the first time in this whole investigation is the proof: the guest
now reaches the assert trap, the handler logs `non-fatal, continuing` and resumes at `rip+2`, exactly
as designed. The message it prints is the same `SoundManager.cpp:306` assert - which was always
expected to fire and always expected to be survivable.

**The boot still does not render** - `draws=0`, `presented_fps=0.1`. This fixed the livelock, not the
title.

## The next blocker, already identified

Past the assert, the main thread now faults:

```
NATIVE EXCEPTION CAUGHT!  Code: 0xC0000005
RIP: 0x0000000800DBCFC0        (the SoundManager region)
RAX: 0xC0DEC0DECAFEBA00
RCX: 0xC0DEC0DECAFEBA00
```

`0xC0DEC0DECAFEBA00` is **our own** `StackCheckGuardValue`, written to `tlsBase + 0x28`
(`CpuDispatcher.cs:396`, `DirectExecutionBackend.cs:5383`, `HleDataSymbols.cs`,
`KernelRuntimeCompatExports.cs`). The guest loaded that TLS slot and dereferenced it **as a pointer**.

So Prospero does not use `TLS+0x28` for `__stack_chk_guard` the way we assume; we are writing a
sentinel over a slot the title uses for a real pointer. Establish what `TLS+0x28` holds on Prospero -
the firmware libkernel TLS setup is the ground truth - and stop poisoning it. That also explains why
every run in this document needed `SHARPEMU_IGNORE_STACK_CHK=1`.

## The TLS+0x28 guard slot is a Linux-ism on a FreeBSD ABI

Ground truth from the decrypted 4.03 firmware - scanning 265 modules for fs-relative absolute reads
(`64 REX 8B /r 25 imm32`):

```
fs:[0x10]  193 reads
fs:[0x38]   39 reads
fs:[0x28]    0 reads      <- never referenced by any firmware module
```

Prospero never reads `fs:[0x28]`. That offset is the **glibc/Linux** convention for
`__stack_chk_guard`; FreeBSD - which Prospero derives from - keeps the canary in an ordinary data
symbol instead, which matches the guest code observed earlier
(`mov r13, [rip+...]` then `mov rax, [r13]`: a global pointer, not a TLS read).

Prosperismo writes `0xC0DEC0DECAFEBA00` to `tlsBase + 0x28` in four places
(`CpuDispatcher.cs:396`, `DirectExecutionBackend.cs:5383`, `HleDataSymbols.cs`,
`KernelRuntimeCompatExports.cs`). Since nothing on Prospero uses that slot as a canary, the write
lands on whatever the module's TLS block genuinely holds there - and Astro loads it and dereferences
it as a pointer, which is the `0xC0000005` at guest `0x800DBCFC0` with
`RAX = RCX = 0xC0DEC0DECAFEBA00`.

This is exactly the class of defect the FreeBSD axis is about: a Linux ABI assumption applied to a
FreeBSD-derived target. It is also why every run in this document needed
`SHARPEMU_IGNORE_STACK_CHK=1` - the flag was papering over a convention error.

**The fix needs care, not a quick edit.** `__stack_chk_guard` is already exported as a data symbol
(`HleDataSymbols.cs`), so the TLS write is both redundant and harmful, but four call sites write it
and something may have come to depend on it. Establish what Prospero's TLS block holds at +0x28 from
the firmware's own thread-setup code first, then remove the write and re-test with
`SHARPEMU_IGNORE_STACK_CHK` **unset** - if the convention is right, that flag should no longer be
needed at all.

## Three concrete TLS/ABI conformance gaps

`InitializeTls` (`CpuDispatcher.cs`, mirrored in `DirectExecutionBackend.cs:5383`) writes:

```csharp
TryWriteUInt64(tlsBase - 0xF0, 0)
TryWriteUInt64(tlsBase + 0x00, tlsBase)                    // tcb_self - correct for FreeBSD
TryWriteUInt64(tlsBase + 0x10, tlsBase)                    // firmware reads this 193x
TryWriteUInt64(tlsBase + 0x28, 0xC0DEC0DECAFEBA00UL)       // the Linux guard slot
TryWriteUInt64(tlsBase + 0x60, tlsBase)
```

**1. `+0x28` is written but never read by Prospero.** Zero firmware reads of `fs:[0x28]` across 265
modules. It is the glibc convention, not a FreeBSD one.

**2. `+0x38` is read but never written.** 57 read sites, and every one dereferences it as a
**pointer to a per-thread struct**:

```
mov r14, qword ptr fs:[0x38]
mov rax, qword ptr [r14 + 0x10]     ; ->  field at +0x10
or  dword ptr [r15 + 0x20], 1       ; ->  flags at +0x20
mov r15, qword ptr [r14 + 0x18]     ; ->  field at +0x18
```

We leave that slot uninitialised, so anything reading it dereferences garbage. The struct needs at
least `+0x10`, `+0x18`, `+0x20`.

**3. The fault register pattern points at a data-symbol binding bug.** The access violation has
`RAX = RCX = 0xC0DEC0DECAFEBA00` and faults *on* that value, i.e. the guest used the canary as an
**address**. `__stack_chk_guard` is a *data* symbol: the guest's slot must hold the **address** of
the canary, and the guest dereferences it to read the value. If the binding puts the canary's
**value** in the slot instead, the guest computes `[0xC0DEC0DECAFEBA00]` and faults - exactly what is
observed. Check how `HleDataSymbols` entries are bound for data (not function) imports before
assuming the TLS write is to blame.

Note: searching the firmware for the literal string `__stack_chk_guard` is meaningless - PS5 exports
are NID hashes, so the name never appears. That check was run and returned 0; it proves nothing
either way.

All three are FreeBSD/ABI conformance items rather than missing functionality, which is what the
FreeBSD axis is actually about. The success criterion for the group is concrete: with them correct,
`SHARPEMU_IGNORE_STACK_CHK` should no longer be needed at all.

---

# CORRECTION: the assert is NOT survivable, so `defaultBusses` must be populated

Two earlier conclusions in this document are now disproved by the post-fix run.

**1. "The stack guard poison is corrupting a pointer" - wrong.** The faulting instruction at
`0x800DBCFC0` is `cmp dword ptr [rdi], 0`; it dereferences **RDI**, not RAX. The prologue above it
is the ordinary stack-protector sequence:

```
0x800DBCFB1: mov r12, [rip+0x8077550]   ; &__stack_chk_guard
0x800DBCFB8: mov rax, [r12]             ; rax = canary   <- CORRECT, expected
0x800DBCFBC: mov [rbp-0x30], rax        ; save canary
0x800DBCFC0: cmp dword ptr [rdi], 0     ; FAULT - rdi is NULL
```

`RAX = 0xC0DEC0DECAFEBA00` is the canary being loaded exactly as designed. The direct experiment
confirmed it: writing `0` instead of the sentinel at `tlsBase + 0x28` left the fault **completely
unchanged** - same RIP, same registers. The TLS+0x28 theory is dead, and the write has been reverted.
(The `fs:[0x38]` gap from the previous section stands on its own evidence and is unaffected.)

**2. "The assert also fires on retail, so it must be survivable" - wrong.** The caller is
`0x800F5B196`, inside the assert function itself, with `R8 = 0x132 = 306` - the assert line number.
The code immediately after the size check is:

```
0x800F5B158: cmp  rax, 0x18           ; is size exactly one element?
0x800F5B15C: jne  0x800F5B3D5         ; no -> report the assert (non-fatal)
0x800F5B162: mov  rax, [r14+0x2730]   ; begin
0x800F5B173: mov  rdi, [rax]          ; read element[0]  -> 0
0x800F5B196: call 0x800DBCFA0         ; pass it as `this`
0x800DBCFC0: cmp  dword ptr [rdi], 0  ; NULL dereference
```

The reporting branch **rejoins the main path**. The game reads `defaultBusses[0]` whether or not the
assert fired, so an empty vector always leads to a NULL `this` a few instructions later. **The title
cannot run with `defaultBusses` empty.**

That settles the question this document opened with. `defaultBusses` having exactly one element is a
hard precondition, not a debug nicety, so on real hardware it *is* populated and **Prosperismo is
failing to populate it**. The earlier reasoning - "nothing in the eboot writes the source vectors,
therefore the assert must fire on retail too" - had the implication backwards: the population path
exists and has not been found.

## Where to resume

The search for the populator was bounded by displacement scans off `this`, which cannot see a writer
that reaches the vector through an inner-struct pointer (small displacement off a different base).
That blind spot was never closed. With a working `ExportCallTrace` and the boot now running past the
livelock, the direct approach is available: set a hardware write watchpoint on
`[0x80E754C70] + 0x2730` and let the guest name its own writer, instead of inferring one statically.

---

# HARDWARE PROOF: `defaultBusses.begin` is written exactly once - by the constructor

Built `GuestWriteWatchpoint` (`src/SharpEmu.HLE/Diagnostics/GuestWriteWatchpoint.cs`): arms DR0 for
an 8-byte write on every thread in the sampler's registry, so the writer names itself regardless of
how it computed the address. This closes the blind spot every earlier search had - a displacement
scan cannot see `[reg + 0x8]` against a pointer into the middle of the object, but a debug register
does not care how the address was formed.

Armed on `0x3009A49F0` (= `this 0x3009A22C0` + `0x2730`, `defaultBusses.begin`):

```
[LOADER][WARN] Write watchpoint 0x00000003009A49F0: armed on 2 thread(s)
[LOADER][INFO] NATIVE EXCEPTION CAUGHT!  Code: 0x80000004   (EXCEPTION_SINGLE_STEP)
[LOADER][INFO]   RIP: 0x0000000800DBF723
[LOADER][INFO]   Host thread: managed=2
```

**One hit, for the whole run.** `0x800DBF723` is the instruction after
`0x800DBF71B: vmovups ymmword ptr [rbx+0x2728], ymm1` - the constructor's 32-byte zeroing store,
which spans `+0x2728..+0x2747` and therefore covers both `begin` (`+0x2730`) and `end` (`+0x2738`).
Data breakpoints report the instruction *after* the store, so this is that `vmovups`.

So the field is written **once, by the constructor, to zero**, and never again. This is no longer a
static inference - it is a hardware fact for this run.

## What that means, and the caveat

Combined with the previous section (the title NULL-dereferences `defaultBusses[0]` immediately after
the assert, so an empty vector is not survivable), the conclusion is forced:

> The populating code exists in the binary - retail could not run otherwise - but **its path is never
> reached in our run**. The question is no longer "which instruction writes the vector"; it is
> "which upstream branch goes the wrong way and skips the population entirely".

That is a different search, and a more tractable one: it is a control-flow divergence, not a hidden
writer.

**Caveat on this measurement.** The watchpoint armed on only **2 threads**, 8 s into the boot,
because the registry only contains threads that have already made an HLE call. A writer on a thread
that never called an export, or one that ran before arming, would be missed. The constructor hit
proves arming happened early enough to catch SoundManager's construction, which is the relevant
window, but re-running with a shorter delay and re-arming as new threads appear would close it
properly.

## Hardware proof, both vectors: only the constructor ever writes them

The first `descriptors` run had a coverage hole - the watchpoint armed on **0** threads at 3 s
(the registry only fills on a thread's first HLE call) and the re-arm cadence was 10 s, so real
coverage started ~13 s in, after the constructor had already run. Fixed: arm at 500 ms, re-arm every
250 ms, and log only on change.

With that coverage, watching `descriptors.begin` (`this + 0x2660` = `0x3009A4920`):

```
Write watchpoint 0x00000003009A4920: armed on 1 thread(s) ... 2 thread(s)
NATIVE EXCEPTION CAUGHT!  Code: 0x80000004
  RIP: 0x0000000800DBF678
hits: 1
```

`0x800DBF678` is the instruction after `0x800DBF670: vmovups ymmword ptr [rbx+0x2658], ymm1` - the
constructor's 32-byte zeroing store, spanning `+0x2658..+0x2677` and therefore covering `begin`
(`+0x2660`) and `end` (`+0x2668`).

So both containers now have the same hardware-verified answer:

| field | writes in a whole run | writer |
|---|---|---|
| `descriptors.begin` (`+0x2660`) | **1** | constructor zeroing, `0x800DBF670` |
| `defaultBusses.begin` (`+0x2730`) | **1** | constructor zeroing, `0x800DBF71B` |

**Nothing populates either vector, ever.** Not a static inference, not a scan artefact - a debug
register on every guest thread, from half a second into the boot.

## The problem, stated precisely

- the title cannot survive an empty `defaultBusses` - it dereferences `defaultBusses[0]`
  unconditionally a few instructions after the assert, which is the observed NULL fault
- `defaultBusses` is filled only by the builder's loop over `descriptors`
- `descriptors` is never written by anything

Therefore the divergence is **upstream of both**: code that should populate `descriptors` never runs
at all. This is a control-flow question - "why is the sound-bus setup path never entered" - and the
tooling to answer it now exists. `ExportCallTrace` gives the call history of the thread that does
enter `SoundManager`, and the watchpoint can be pointed at any guest address to catch its writer.

The natural next move is to trace the *caller* side: `0x800DC0500` (the builder) has exactly one
caller, `0x800F3E4D2`, which is reached with the owner gate `0x80E754C68` already set. Walk that
caller's own callers with the same watchpoint technique applied to the gate byte, and the entry point
of the whole sound-init sequence falls out.

## The gate found - and the chain bottoms out at "no sound data is ever loaded"

Mapping the builder's control flow between its entry and the descriptors loop found the branch that
skips everything:

```
0x800DC0959: mov rax, [rbx+0x2688]   ; end of a FOURTH source vector
0x800DC0960: mov r14, [rbx+0x2680]   ; begin
0x800DC096E: cmp r14, rax
0x800DC0971: je  0x800DC0B02         ; EMPTY -> jump straight to the descriptors loop
```

The block it skips (`0x800DC0977`-`0x800DC0B02`) is the one that allocates and constructs -
`operator new`, `0x800DB2000`, `0x800DC2F40`, `0x800DB0570`, `__cxa_guard_*` - i.e. the population.
A second `je 0x800DC0B02` at `0x800DC09AE` closes the same loop.

So the vector at `+0x2680` gates the whole thing. Watching it (`0x3009A4940`):

```
hits: 1   RIP: 0x0000000800DBF680
```

`0x800DBF680` is the instruction after `0x800DBF678: vmovups ymmword ptr [rbx+0x2678], ymm1` - the
constructor's zeroing again, covering `+0x2680` and `+0x2688`.

### Four vectors, one answer

| field | writes per run | writer |
|---|---|---|
| `+0x2660` descriptors | 1 | ctor zeroing `0x800DBF670` |
| `+0x2680` gating source | 1 | ctor zeroing `0x800DBF678` |
| `+0x2698`, `+0x26B8` | (ctor zeroing) | `0x800DBF680`, `0x800DBF688` |
| `+0x2730` defaultBusses | 1 | ctor zeroing `0x800DBF71B` |

Every container in the chain is zeroed by the constructor and **never written again by anything**.
The SoundManager is built and then no data is ever loaded into it.

### What that points at

This is consistent with, and now explains, the very first observation in this document: the only
sound-related file access in an entire boot is three failed `stat` calls on
`/host/%ASOBI_ROOT%/target/data/common/sound/{config,sound_request_pairs,audio_propagation_config}.xml`.
The title asks for its bus configuration **only** under the unexpanded dev root, never under `/app0`
- unlike `DebugFont.gnfp`, which is tried at `/app0` first and only then at the dev root.

So the title is taking a **dev-filesystem path for sound configuration**, that path cannot resolve,
no data is loaded, every vector stays empty, and ~0x500 bytes later it dereferences
`defaultBusses[0]` and dies. The bus data is not optional and the title has no fallback for it.

Two things to establish next, in this order:
1. **Why sound config resolves to `%ASOBI_ROOT%` and not `/app0`.** Something makes the engine pick
   the host/dev root for this asset class. Find that decision - it is a branch on a mode flag or a
   mount-table query - and the whole class of dev-path lookups is likely to move with it.
2. **Whether the data exists at all.** `data/common/` is an empty directory tree in a complete
   148.50 GB dump, and `config.xml` appears nowhere in it. If the retail package genuinely ships
   these files, this dump is missing them; if it does not, the engine must obtain them another way
   and (1) is the whole answer.

---

# THE COMPLETE CHAIN, root selection to crash

Adding the guest return address to failed path lookups (`KernelFileTraceLog`) named the requesters
directly:

```
[IO-FAIL] resolve guest='/host/%ASOBI_ROOT%/target/data/common/sound/config.xml'
          reason=path-unmapped ret=0x0000000800F3C486
[IO-FAIL] ... sound_request_pairs.xml       ret=0x0000000800F3EF34
[IO-FAIL] ... audio_propagation_config.xml  ret=0x0000000800F416DB
```

All three sit in the sound-init function `0x800F3B640`. The composition at the first one:

```
0x800F3C45B: call 0x800296C00          ; returns the path ROOT
0x800F3C467: lea  rdx, [rip+0x71D5813] ; the relative path literal
0x800F3C476: call 0x800293AD0          ; compose(root, dest, relative)
0x800F3C481: call 0x8074E7580          ; stat  ->  fails
0x800F3C488: jne  0x800F3E4B5          ; and gives up
```

`0x800296C00` is a magic-static that lazily builds a path-resolver object and caches it:

```
0x800296C19: movzx eax, byte ptr [rip+0xE4BDBE8]  ; guard @0x80E754808
0x800296C22: je   <init>
0x800296C24: mov  rax, qword ptr [rip+0xE4BDBE5]  ; cached resolver @0x80E754810
0x800296C51: mov  edi, 0x90 / call operator new   ; 0x90-byte object
0x800296C83: call 0x8002988D0                     ; its constructor
```

That resolver hands back `/host/%ASOBI_ROOT%/target/`, not `/app0/`. Both roots exist as literals in
the binary; the engine chooses.

## The chain, each link now measured rather than assumed

1. The path resolver (`0x800296C00`, constructed at `0x8002988D0`) yields the **dev root**.
2. Sound config paths compose against it and cannot resolve - `%ASOBI_ROOT%` is never expanded and
   `/host` is not a mount.
3. No sound data is loaded.
4. All four SoundManager containers stay exactly as the constructor left them. **Hardware-verified**:
   a debug-register write watchpoint on `+0x2660`, `+0x2680` and `+0x2730` records exactly one write
   each, every one the constructor's own `vmovups` zeroing.
5. The builder's gate `0x800DC0971` (`cmp [rbx+0x2680] vs [rbx+0x2688]; je`) sees the source vector
   empty and jumps straight past the entire population block.
6. `descriptors` stays empty, so the loop that fills `defaultBusses` runs zero times.
7. `SoundManager.cpp:306` asserts `defaultBusses.size() == 1`, reports, and **falls through**.
8. `0x800F5B173: mov rdi, [rax]` reads `defaultBusses[0]` from the empty vector, gets 0, and
   `0x800DBCFC0: cmp dword ptr [rdi], 0` faults. The title cannot survive this - there is no
   fallback path.

## The one remaining unknown

**Why the resolver selects the dev root.** That decision is inside the constructor `0x8002988D0`,
which builds the 0x90-byte resolver object. Read it, find the condition, and the whole class of
dev-path lookups moves with it - the same resolver is what composed the `%ASOBI_ROOT%` paths for
`physics_config.xml` and the `data/system/gfx/*.gnfp` files seen in the very first I/O trace.

The second-order question stays open and is worth settling in parallel: `data/common/` is an empty
directory tree in a complete 148.50 GB dump and `config.xml` exists nowhere in it. Even with the root
corrected, the file has to exist somewhere for the title to load it.

---

# CONCLUSION: the sound bus data is not in this dump

The title requires `defaultBusses` to hold exactly one element and dereferences `defaultBusses[0]`
with no fallback. That data comes from the sound configuration files. An exhaustive search of the
dump:

```
config.xml / sound_request_pairs.xml / audio_propagation_config.xml
conditions.xml / sound_property.xml            -> NOT PRESENT anywhere
data\common\  -> five EMPTY directories: font, gfx, haptics, odx, sound
files matching 'bus'  -> vegetation only (veg_*_bush_*)
```

The package is otherwise complete - 148.50 GB, 156,133 files, `data\prein\` alone holding 156,091 of
them. Five empty directories in an otherwise full tree is the signature of an extraction that
created the directory structure but not its contents.

**So Astro Bot cannot complete sound initialisation with this dump, and no emulator fix changes
that.** The path-resolver root question (`0x8002988D0`) is still a real defect worth fixing - it
sends every `%ASOBI_ROOT%` lookup to an unmountable dev path - but even a correct `/app0` root would
find nothing, because the files are absent from `/app0` too.

This reframes the last several days of work on this title: the SoundManager chain was traced
correctly and each link verified, and the chain simply terminates in missing input data rather than
in a bug.

## What to do instead

1. **Switch the primary boot target.** The VM already holds
   `Superliminal-PPSA06084-USA-Game-(v01.010)` and a GTA V package. Superliminal was named as the
   alternative test title at the start of this work. A title whose data is complete will exercise
   the same CPU, kernel and GPU paths without dead-ending on absent assets.
2. **Keep the REX.B fix and the diagnostics.** The spinlock encoding bug was real, title-independent,
   and would have livelocked any guest that entered the VEH serialisation path. `ExportCallTrace`,
   `StalledThreadSampler` and `GuestWriteWatchpoint` are equally title-independent.
3. **Re-dump Astro if it stays a target.** Specifically `data/common/`; everything else appears
   intact.
4. The path-resolver root defect is worth fixing on its own merits - it also explains the
   `%ASOBI_ROOT%` lookups for `physics_config.xml` and `data/system/gfx/*.gnfp` in the first I/O
   trace - but it is not what stops this title.
