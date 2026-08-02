# Superliminal boot log

> **This file is the chronological record, not the status.** For current state read
> **`docs/superliminal-status.md`**, which supersedes this file wherever the two disagree. Keep this
> one for what was tried and what it cost.
>
> **Read this first.** The gate is `PS5Manager.CurrentState`, permanently stuck at `Initializing(1)`
> because `Unity.PSN.PS5.Main::Initialize` never completes and the coroutine that would set
> `Initialized(2)` never runs its final store. The full chain, with addresses, is in
> **"The title-screen gate, localised to one byte"** below. Everything above that section predates
> the finding; several of its "it stalls waiting on X" readings are superseded - in particular
> `Loading.PreloadManager` is **normal idle**, not half of a deadlock.
>
> Two corrections from 2026-07-27, both measured:
> - It is **"never returned"**, not **"reported failure"**. `PsnInitResult` is all zeros, which per
>   `Ps5ManagerStateProbe.cs:172-186` cannot be distinguished from the `.cctor` default. Sections
>   below that read the zero blob as a reported error are wrong.
> - `Main::Initialize` blocks **above our HLE surface**. The import census counts 4 total
>   `libSceNpManager` dispatches for the entire process, so it never reaches `libSceNp` at all.
>   Anything below that hypothesises a missing or non-completing NP export as the cause is dead.

Astro Bot is blocked on missing dump data (`docs/boot-blocker-after-assert.md`), so primary boot
testing moved to Superliminal, whose package is intact: **162 files, 13.93 GB, zero empty
directories** - none of Astro's extraction-failure signature.

Layout differs from Astro's: a flat directory with `eboot.bin` (27,701,576 bytes) at the top level
plus `decrypted/eboot.bin`, `Media/`, `sce_module/`, `sce_sys/`, `fakelib/`, `ampr_emu.index`. Note
the directory name contains `[APR-EMU]` - PowerShell treats `[` `]` as wildcards, so any script
touching it needs `-LiteralPath` or it silently enumerates nothing.

## First boot: dies at ~4,100 imports

Far earlier than Astro (1.6M+). No frame, no PERF sample, one fault:

```
NATIVE EXCEPTION CAUGHT!  Code: 0xC0000005
Exception Address: 0x0000000000000000
RIP:               0x0000000000000000
```

**RIP = 0** - the guest called through a null function pointer. Immediately before it, `open`
(`wuCroIGjt2g`) failed four times.

## Fixed: `/dev/urandom` and `/dev/random`

```
open guest='/dev/urandom' reason=path-unmapped
open guest='/dev/random'  reason=path-unmapped
builtin=[/app0 /temp0 /download0 /hostapp /devlog/app]      <- no /dev
```

Prospero derives from FreeBSD, where both are ordinary character devices any libc entropy path can
open. They belong to no mount, so path resolution can never produce them - a title asking for
entropy this way simply gets a failed open, with no way to seed itself.

Implemented in `KernelMemoryCompatExports`: `open` on either path returns a synthetic descriptor,
`read` fills the entire request from `RandomNumberGenerator` (neither node short-reads on FreeBSD),
`close` releases it. Eight tests in
`tests/SharpEmu.Libs.Tests/Kernel/KernelRandomDeviceNodeTests.cs` cover descriptor validity, full
fills, non-repetition, and close, for both nodes.

Verified on the VM: `urandom fails: 0` where it was 4.

## Next, same class

Still `path-unmapped`, both from the same caller (`ret=0x800EF2C41`), leading into a failing
`sceKernelMkdir`:

```
stat guest='/devlog'  reason=path-unmapped
stat guest='/'        reason=path-unmapped
```

The mount table has `/devlog/app` but **not its parent `/devlog`**, and **no filesystem root**. A
guest walking a path to create a directory stats each component, so both need to resolve. These are
the same category as the device nodes: POSIX/FreeBSD surface the OS provides and we do not.

## Fixed: the filesystem root and the mount parent

Folded `"/"` into the guarded `/app0` exact-match branch rather than handling it separately, so it
inherits that block's `!IsNullOrWhiteSpace(app0Root)` guard - with no title loaded the app0 root is
unset and `"/"` must stay unmapped exactly like `/app0`, not resolve to `null`. `"/devlog"` maps to
the same root as the `/devlog/app` mount it parents.

(The first attempt got this wrong: an ungated branch returned `null` where `/app0` returns `""`, and
the test asserting "non-empty" was itself wrong, because an unloaded app0 root is legitimately
empty. The contract is that `"/"` resolves *identically to* `/app0`, which is what the test now
asserts.)

## Measured effect of both fixes

| | before | after |
|---|---|---|
| `open` failures (`/dev/urandom`, `/dev/random`) | 4 | **0** |
| `sceKernelMkdir` NOT_FOUND | 1 | **0** |
| unmapped path lookups (`/`, `/devlog`) | 4 | **0** |
| total guest-visible errors | 8 | **3** |

**Every file-I/O failure is gone.** Twelve tests cover both fixes.

## What remains

Three guest-visible errors, none file-related:

```
ORBIS_GEN2_ERROR_DELETED           BHouLQzh0X0  sceKernelDirectMemoryQuery
ORBIS_GEN2_ERROR_INVALID_ARGUMENT  k04jLXu3+Ic  sceLibcMspaceMallocStatsFast
ORBIS_GEN2_ERROR_NOT_FOUND         rVjRvHJ0X6c  sceKernelVirtualQuery
```

and the null-pointer fault (`RIP = 0`) still stands.

Their arguments matter before anyone treats them as bugs:

```
sceKernelVirtualQuery       rdi=0x600100000  rsi=1  rcx=0x48
sceKernelDirectMemoryQuery  rdi=0x010000000  rsi=1  rcx=0x18
```

**`rsi = 1` is the find-next flag on both** - these are memory-map *enumeration* calls. A walk
terminates when the API reports "nothing further", so `NOT_FOUND` and `DELETED` may well be the
correct terminal answers rather than failures, and our `[WARN] Import# result:` channel flags them
only because it flags every non-OK return.

**This is not yet settled.** Counting log lines does not distinguish "called once and failed" from
"called many times and failed on the last", because only failures produce a WARN line. Settle it
with the census (`SHARPEMU_HLE_EFFECT_CENSUS=1`, `_TOP=300`), which counts every call: if either
export shows a run of successful calls before the failing one, it is a normal enumeration terminator
and neither is worth chasing.

## Session-end state and the exact next steps

Fixed and verified this session: `/dev/urandom`, `/dev/random`, `/`, `/devlog`. All file-I/O
failures gone; guest-visible errors 8 -> 3. Twelve tests.

Open, with the specific action for each:

1. **Are the two memory queries real failures?** Not settled. Their `rsi = 1` (find-next) makes them
   enumeration-shaped, and counting WARN lines cannot answer it because only failures produce one.
   Run with `SHARPEMU_HLE_EFFECT_CENSUS=1 SHARPEMU_HLE_EFFECT_CENSUS_TOP=300` **and let the process
   exit or reach the interval** - a run killed at the harness timeout produced no report at all,
   which is why this is still open.
2. **The null-pointer fault (`RIP = 0`).** The dump gives `RSP = 0x7FFFF07FBAB8`, so the caller's
   return address is at `[RSP]`, and `R9 = 0x801899F13` is a guest code address worth disassembling.
   `SHARPEMU_DUMP_FAULT_STACK_WINDOW=32` did **not** add a stack dump to the exception output, so
   either that variable gates something else or the dump needs extending to print `[RSP]`.
3. **Get the eboot local.** Static analysis needs it. `scp` of
   `.../Superliminal-...(v01.010) [APR-EMU]-PS5/eboot.bin` fails on quoting - the path has both
   spaces and `[` `]`. Copy it to a plain staging name on the VM first
   (`Copy-Item -LiteralPath ... C:\sl-eboot.bin`) and fetch that.

---

# The null call, identified: Unity/IL2CPP internal-call registration

## A verified code mapping (finally)

The top-level `eboot.bin` is an **encrypted SELF** (`5414f5ee`), which is why a delta search on it
found nothing. The dump ships `decrypted/eboot.bin` (27,703,392 bytes) - a plain ELF at offset 0 -
and its program headers are real file offsets (`off - vaddr = 0x4000` for the code segment).

Unlike Astro, this mapping is **verified rather than assumed**: an instruction must end exactly at a
known return address, and it does -

```
0x800EF58A2 -> call at 0x800EF589D -> 0x80163C5C0   (sceKernelVirtualQuery)
0x800EF5905 -> call at 0x800EF5900 -> 0x80163C5D0   (sceKernelDirectMemoryQuery)
```

Local copies: `games/superliminal/eboot-decrypted.bin` (use this) and `eboot.bin` (encrypted).

## Two of the three "errors" were never errors

Disassembling around those calls shows a memory-accounting **walk**:

```
0x800EF5890: mov  ecx, 0x48 / mov esi, 1 / mov rdx, r14   <- loop head, findNext
0x800EF589D: call sceKernelVirtualQuery
0x800EF58A2: test eax, eax
0x800EF58A4: jne  0x800EF58C6        ; non-zero ENDS the walk
0x800EF58C4: jmp  0x800EF5890        ; else loop
0x800EF58C9: cmp  eax, 0x8002000D    ; which code ended it?
0x800EF58CE: cmovne rbx, r13         ; not that one -> DISCARD the accumulated total
```

A non-zero return is the normal exit. **But the guest checks which code**, and we returned
`NOT_FOUND (0x80020002)` where it expects `0x8002000D` (EACCES) - so it threw away the memory total
it had just accumulated across every region. `sceKernelDirectMemoryQuery` already returned EACCES
correctly, with a comment saying so; `sceKernelVirtualQuery` had been missed.

Fixed, with the guest's own `cmp` as the ground truth. Verified: both walks now end with
`ORBIS_GEN2_ERROR_DELETED`. The remaining `sceLibcMspaceMallocStatsFast` failure comes from inside
`libc.prx` and is a stats query, unlikely to be load-bearing.

## The fault

Added stack words to the exception dump (a null call leaves its caller at `[rsp]`, which the dump
never printed). That gave the caller immediately:

```
Stack: [rsp+0x0]=0x0000000800443E90 ...

0x800443E7C: lea  rdi, [rip+0x14096B9]      ; -> "UnityEngine.AI.NavMeshPath::InitializeNavMeshPath"
0x800443E83: lea  rsi, [rip+0xD34986]       ; -> the implementation function
0x800443E8A: call qword ptr [rip+0x1643CC0] ; slot 0x801A87B50 = NULL
```

**Superliminal is a Unity/IL2CPP title**, and this is internal-call registration - name plus
implementation, exactly the `il2cpp_add_internal_call` shape. The loop just above
(`call qword ptr [rax+rbx*8]`) registers a whole table the same way.

**No relocation targets `0x801A87B50`** - neither JMPREL (568 entries) nor RELA (28,988) - so it is
not an unresolved link-time import. It is a function-pointer variable the title fills at runtime,
and it is still zero when called.

## Next

Prosperismo already has the machinery this depends on: `DispatchIl2CppApiLookupSymbol`
(`DirectExecutionBackend.Imports.cs:553`, NID `r8mvOaWdi28`) plus `DispatchKernelDynlibDlsym`. The
title almost certainly asks for its IL2CPP API pointers through one of those and gets 0 back for the
registrar. Trace what name is requested and what is returned - if the lookup does not know the
symbol, that is the whole bug, and it is a table entry rather than a redesign.

## Root cause: the internal-call table is used before it is resolved

Following the null slot to its single writer closes the chain completely.

`0x801A87B50` has **7,588 indirect calls** through it and **exactly one write**:

```
0x800F032F3: lea  rdi, "il2cpp_add_internal_call"
0x800F032FA: call 0x80163C730            ; the IL2CPP symbol resolver
0x800F032FF: mov  [rip+0xB8484A], rax    ; <- the only write to the slot
0x800F03306: test rax, rax / jne ...     ; null check, then a failure call
```

That block resolves the whole API by name - `il2cpp_set_config`,
`il2cpp_set_memory_callbacks`, `il2cpp_get_corlib`, `il2cpp_add_internal_call`,
`il2cpp_resolve_icall` - and the resolver at `0x80163C730` is import **`r8mvOaWdi28`**, which is
exactly the NID Prosperismo special-cases as `DispatchIl2CppApiLookupSymbol`
(`DirectExecutionBackend.Imports.cs:553`).

**But the boot log contains zero `il2cpp` lines.** That handler logs
`il2cpp_api_lookup_symbol failed: name='...'` when it cannot resolve, and
`TryResolveIl2CppApiAddress` falls back to a **callable zero-return stub** for any name starting
`il2cpp_`. So had it run at all, the slot would hold a stub and there would be no fault.

It never ran. The registration path at `0x800443E30` - which calls the registrar 7,588 times, once
per Unity internal call - executes **before** the block at `0x800F032xx` that fills the table.

So this is an **ordering problem**, not a missing export: our resolver is correct and would have
answered, but nothing had asked it yet. Establish what runs `0x800443E30` (`.init_array` static
initialiser vs. an explicit call from the IL2CPP bootstrap) and why the API-resolution block has not
run by then. `DispatchBootstrapBridge` and `DispatchKernelDynlibDlsym` in the same file are the other
entry points worth checking - the title may resolve through one of those first on real hardware.

## Correction: the resolver DOES run - the ordering is timing-dependent

The "it never ran" conclusion above was a **measurement artifact**. Without
`SHARPEMU_DISABLE_MITIGATION_RELAUNCH=1` the emulator relaunches as a child process, and the stderr
being grepped was the **launcher's**, not the emulator's. (Same trap as attaching `dotnet-stack` to
the launcher and getting only `Program.TryRunMitigatedChild`.) Also note PowerShell `Select-String`
is case-insensitive by default while `grep` is not - that discrepancy is what first exposed it.

Armed a hardware write watchpoint on the registrar slot `0x801A87B50`:

```
Write watchpoint 0x0000000801A87B50: armed on 1 thread(s)
hits: 1   RIP: 0x0000000800F03306   Host thread: managed=2
```

`0x800F03306` is the instruction after `0x800F032FF: mov [rip+0xB8484A], rax` - so **the slot is
written**, by the expected instruction, on the same thread that later faults.

And in that run there was **no access violation at all**. Arming the watchpoint suspends and resumes
threads, which perturbs timing enough to flip the outcome.

A controlled A/B (child vs in-process relaunch, no watchpoint) is identical in both modes -
1 access violation, 4 Il2Cpp module lines, 3 guest errors, 0 frames - so relaunch mode is not the
variable. The variable is **timing**.

### What that means

Both the registrar call site (`0x800443E8A`) and the resolver block (`0x800F03030`) are in the eboot
and run on `managed=2`. The registrar function `0x800443E30` has **no direct callers** - it is
reached through a pointer table - and all module `dt_init`s (including
`Il2CppUserAssemblies.prx`) complete well before the fault. There are no
`il2cpp_api_lookup_symbol failed` lines, so when the resolver is called it **succeeds**.

So the title reaches the internal-call registration before the IL2CPP API table has been filled, and
whether it does so depends on run timing. Something our runtime sequences differently from hardware
decides which happens first.

### Next

Determine what invokes `0x800443E30`, since it is only ever called indirectly - find the table it
sits in and who walks it. Then compare against when `0x800F03030` runs. The instrument for this is
already built: put the write watchpoint on the slot **and** trace export calls in the same run, so
the ordering of the write against the registration is recorded rather than inferred.

## Resolved: the dispatcher was never reached

The timing theory above is **wrong**, and the way it was disproved is worth keeping: the handler was
made to log its *successes*, not just its failures.

```
lookups        : 0
access viol.   : 1
```

Zero. `DispatchIl2CppApiLookupSymbol` had never run in any failing boot. The watchpoint had shown a
write to the slot, so the call at `0x800F032FA` was returning *something* - just not from us.

`SHARPEMU_LOG_ALL_IMPORTS=1` named the culprit in three lines:

```
TryResolveDirectImportTarget: r8mvOaWdi28 not in HLE table, checking runtime symbols...
TryResolveDirectImportTarget: r8mvOaWdi28 -> runtime symbol 0x000002009D857AF0
SetupImportStubs: Direct bridge for r8mvOaWdi28 -> 0x000002009D857AF0
```

`r8mvOaWdi28` is `il2cpp_api_lookup_symbol`. The loader resolves an import in two stages: look for an
HLE export with that NID, and failing that, search the loaded modules for a runtime symbol and bind
the guest **directly** to it, bypassing the dispatcher. That is the right default - a direct bridge
is faster and more faithful than a round trip through managed code - but it is wrong for the handful
of NIDs whose implementation *is* the dispatcher branch.

Those NIDs have no row in the export table, precisely because the dispatcher handles them. So they
fall through the table lookup into the runtime-symbol search every time. `IsHlePreferredNid`, the
list that holds a NID back from direct binding, listed only `fputs` and `memcpy`;
`sceKernelDlsym` was protected by accident, because it *does* have an export row.
`il2cpp_api_lookup_symbol` had nothing.

The bridged symbol returned 0. Superliminal stored that 0 into its IL2CPP API pointer table and made
the first of 7,588 calls through it. Nothing reported an error, because from the loader's side the
import had resolved.

The fix is that the two sites now read one list, `IsDispatcherOwnedNid` - a dispatcher branch is only
reachable if its NID is also kept away from direct binding. `DispatcherOwnedNidTests` reads the
branches back out of `DispatchImport` and fails if a new one is added to only one place. It found a
fifth NID comparison on its first run; that one turned out to be a leftover debug probe with an
ordinary export behind it, which is why the test now matches only branches that actually dispatch.

### Result

| | before | after |
|---|---|---|
| `il2cpp_api_lookup_symbol` calls | 0 | 222 |
| failed lookups | - | 0 |
| access violations | 1 | 0 |
| guest errors | 3 | 0 |

The title now initialises the IL2CPP runtime, starts the Unity engine threads
(`UnityGfxDeviceWorker`, `Gfx Task Executor`, `Loading.AsyncRead`, `BatchDeleteObjects`), brings up
Vulkan on the host GPU, and presents a guest frame at 1920x1080.

### Not bugs

The remaining `[LOADER][IO-FAIL]` lines are Unity probing for a layout this build does not use:
`data.unity3d` is the single-bundle form, and `.res`/`.resS`/`.resG` are sidecars. This build ships
the loose form - `globalgamemanagers`, `level0` through `level18`, and the `.resS` files that do
exist are all present. `EOSNativePlugin.prx` is Epic Online Services and is optional.

`Forcing submitDone to avoid TRC R4089 breach` on guest stdout is Unity's own PS5 backend, not ours.
It prints when a frame ends without the title having submitted, so it is a sign the main loop is
turning.

## The stop-the-world deadlock

With IL2CPP initialising, the title reached its main loop and stayed there doing nothing:
94 `Forcing submitDone` over 300s, and only three files ever opened -
`global-metadata.dat`, `globalgamemanagers`, and `unity default resources`. It never got as
far as `level0`.

`SHARPEMU_LOG_EXPORT_CALLS` made the shape obvious. The tail of the ring was one thread
looping on mutex/clock calls with a **1.001-second gap** between an unlock and the next
lock, 173 times, with no HLE call in flight for the whole second. Meanwhile the thread that
had done all the work - 259,765 of the ring's 262,144 records - had stopped, and its last
three calls were:

```
_write
sceKernelRaiseException
sceKernelWaitSema        <- entered, never returned
```

That is not a crash. Type `0x1E` is 30, `SIGUSR1`, and the semaphore is named
`SuspendSemaphore` (max=256). It is IL2CPP's stop-the-world collector: raise a signal at a
thread, then wait for it to acknowledge.

The acknowledgement never came. `guest_exception.queued ... mode=scheduled` says the signal
was queued for delivery at the target's next HLE boundary, and `safe_point_enter` never
appeared - 0 deliveries in a 150s run, with the signal still queued 27 warnings later.

Sampling the target settled it. `host_rip=ntdll.dll+0x160BB4`, a stack of
`KERNELBASE!WaitForSingleObject <- coreclr`, and `last_export=pthread_cond_wait`. The
target was **inside** an HLE call, so it could never reach the boundary where we deliver.

`PthreadCondWaitCore` had `var cooperative = false;` hardcoded, so every condition wait took
the host-parking path, and for an untimed wait that path was:

```csharp
Monitor.Wait(state.SyncRoot);   // no timeout, no interrupt
```

Nothing could break it out. FreeBSD interrupts a thread blocked in `pthread_cond_wait` so
the handler can run; we could not. The collector waited for an acknowledgement from a thread
that was never coming back, and the process idled for as long as you let it.

The fix bounds that wait and gives up when a signal is queued for the current thread. POSIX
permits `pthread_cond_wait` to wake spuriously, so returning early is safe for any caller
that re-tests its predicate - which is every correct caller. `safe_point_enter` fires,
`still queued` warnings go to zero, and the title starts executing managed code instead of
turning an empty loop once a second.

## What is left

Past the deadlock, the title reaches the same point every run and fails in one of two ways,
**varying between runs**:

- a null dereference at `0x8004BFB2D`, `cmp byte ptr [rax + 0x10], 0`, immediately after a
  `Try(&result, &error)` call reports no error and returns null; or
- a CLR fail-fast, `attempted to call a UnmanagedCallersOnly method from managed code`.

Two changes were made here on their own merits, and neither should be credited with fixing
the blocker:

`_is_signal_return` was **unimplemented**, and an unresolved import leaves
`ORBIS_GEN2_ERROR_NOT_FOUND` in RAX. 0x80020002 is not zero, so the unwinder was told
"yes, this is a signal frame" about every frame it asked about. Its real contract, from
libkernel.sprx 4.03 @0x24BA0, is a capability read: zero a 0x88-byte SelfAuthInfo, call
syscall 0x25F (`sys_get_self_auth_info`), return -1 on failure, else bit 26 of `attrs[0]`.
We answer 0, which is the truthful answer for a runtime that builds no sigframe.

`sceKernelGetModuleInfoForUnwind` returned NOT_FOUND for the address every unwind ends on.
That address turned out to be a single 4KB private executable page whose bytes decode as
`mov rax, 0x800000070 / call rax` - our own thunk calling the guest entry point. On hardware
that frame is inside the executable's `_start`: the unwinder finds a module, finds no FDE
inside it, and stops. It now reports a named boundary region with zeroed eh_frame pointers,
which reproduces that outcome. KytyPS5 reached the same answer independently and left the
reasoning in `libKernel.cpp` ("synthetic boundary with no unwind tables so libc stops
cleanly instead of raising").

One run after that change showed 0 access violations, and I wrote that the fault was gone.
Two further runs showed the access violation and no fail-fast. **n=1 was not evidence.** The
two failure modes alternate; neither change removed them.

The CLR fail-fast is a hazard the tree already documents, in
`DirectExecutionBackend.NativeWorker.cs`: guest entry stubs must not run above CLR-managed
frames on a CLR-created thread, or the runtime fail-fasts with exactly this message. Guest
threads already run on raw OS threads for that reason, but the main entry path does not,
because migrating it wholesale previously increased splash hangs. That is the next thing to
settle, and it wants a controlled A/B rather than another single run.

## Resolved: Superliminal renders. The black frame was a debugging flag.

Everything above was measured with `SHARPEMU_DISABLE_MITIGATION_RELAUNCH=1` set. That flag was on
for every run of a very long session, to keep the emulator in one process so its stderr could be
read without chasing a relaunched child. It is also what produced the "black frame, one flip"
symptom that the whole investigation was chasing.

The relaunch exists to turn **off** Windows process mitigations. Guest execution redirects RIP from
a vectored exception handler - to dispatch imports, to emulate SSE4a instructions the host lacks,
and to deliver guest signals. On a CET-enabled build (Windows 11 / Server 2025, build 26100) the
kernel answers that by terminating the process:

```
Exception code: 0xC0000409     STATUS_STACK_BUFFER_OVERRUN (__fastfail)
Exception Data:  0x0D          FAST_FAIL_INVALID_SET_OF_CONTEXT
Faulting module: ntdll.dll
```

Nothing is written to stderr. From the outside it is indistinguishable from a stall.

| | flag set | as designed |
|---|---|---|
| flips | 1 | 343 |
| draws | 1 | 2,407 |
| `Wanted to force submitDone but not safe` | 6-7 | 0 |
| non-black pixels | 0 / 769,972 | 766,527 / 769,972 |
| process lifetime | dies ~30s, silently | ran 400s, killed at timeout |

The captured frame is the title screen: the storage room, the logo, lighting and shadows, the
hanging lamp's light cone. Reproducibility over three runs: two rendered to 300-400s, one died at
32s with an access violation after 8 flips and 62 draws, so an intermittent crash remains.

### What this invalidates

Every conclusion of the form "the title stalls waiting on X" in the sections above was measured
under that flag and is unsafe to build on: the one-flip ceiling, the ~10s gaps between submits, the
TRC watchdog spam, and the reading that a GPU `WAIT_REG_MEM` never retires. They are consequences
of the process being killed thirty seconds in.

The seven fixes made along the way are real and still under test:

1. dispatcher-owned NIDs were being direct-bridged past their own implementation, which broke
   `il2cpp_api_lookup_symbol`
2. `pthread_cond_wait` was an unbounded `Monitor.Wait`, so a raised signal could not interrupt it
   and IL2CPP's stop-the-world deadlocked
3. il2cpp symbol lookup needed a catalogue entry; 50 of 222 names had none and silently became a
   zero-return stub, including `il2cpp_stop_gc_world` and `il2cpp_object_header_size`. Fixed by
   hashing the name to a NID directly
4. `sceKernelVirtualQuery` reported every range as committed, so the title never committed its
   memory pool and `tlsf_add_pool` starved
5. `SceVideoOutFlipStatus` left five of eight fields unwritten, including `flipPendingNum`
6. `DMA_DATA` inferred its source mode from packet layout instead of `SRC_SEL`
7. `RELEASE_MEM` never raised the end-of-pipe interrupt its `INT_SEL` field asked for

Of these, 1-4 are load-bearing: the title cannot reach a rendered frame without them. The
contribution of 5-7 can no longer be separated from the flag and should be re-measured.

`Program.cs` now warns loudly when the flag is set, and says it must not be used to judge whether a
title works.

### Where it stops

The title reaches its **title screen and stays there**. The logo is up and a two-dot loading
indicator is still spinning at 400 seconds; it never reaches a start prompt, so it never gets to
gameplay. The renderer is not the problem - the scene draws correctly. The load does not finish.

Two things are worth knowing before picking this up:

- `sceKernelDlsym failed: handle=0xC symbol='UnityPluginLoad'` (and `UnitySetGraphicsDevice`,
  `UnityRenderEvent`, `UnityRenderingExtEvent`, ...) is Unity probing each loaded plugin for the
  optional native-plugin interface. Handle 0xC is `libfmodstudio.prx`, which does not export it.
  Benign.
- The eboot contains no EOS strings at all, so the missing `/app0/Media/Plugins/EOSNativePlugin.prx`
  is not referenced by the executable. If online services gate the start prompt it is from managed
  code in `Il2CppUserAssemblies.prx`, not from the eboot.

## Fixed: two blockers behind the loading screen

Measured on the Azure V620 (RDNA2). Both were found by reading logs rather than the guest.

### 1. The time converters threw, and Unity looped forever

`sceKernelConvertLocaltimeToUtc` and `sceKernelConvertUtcToLocaltime` passed the guest's raw
seconds straight into `DateTimeOffset.FromUnixTimeSeconds`. Unity resolves its timezone by
walking backwards an hour at a time until a conversion succeeds, and it starts from a value far
outside the representable range, so every call threw. `DispatchImport` turns an exception into
`0xFFFFFFFF80020002`, so the loop never converged:

| | before | after |
| --- | --- | --- |
| `0NTHN1NKONI` dispatch errors | 41,172 | 0 |
| stderr lines | 183,776 | 3,880 |

The two converters are called alternately, which is the giveaway: it is a `mktime` round trip,
and a round trip cannot converge if both directions fail. Both now clamp only the date used for
the timezone lookup and keep the returned seconds raw, so in-range inputs are unchanged and
`local -> utc -> local` stays exact.

### 2. A guest write could not span two mapped regions

`PhysicalVirtualMemory.TryWrite` resolves the destination with `FindRegion`, which binary-searches
for the single region with the greatest `VirtualAddress <= address`, then requires the whole span
to fit inside it (`TryResolveRegionOffset`, `size > region.Size - offset`). The guest sees one flat
address space, but consecutive direct-memory mappings land here as separate `MemoryRegion` entries,
and `TryBackFixedRange` inserts one region per gap it fills. A write crossing a seam therefore
failed even though every byte was mapped and writable.

Superliminal hit this through AMPR, which copies in 1 MB chunks (`AmprExports.cs`, `ChunkSize`).
A chunk straddling a seam returns `EFAULT` and the whole chunk is rejected atomically, so:

- `read=0` means chunk 1 already crossed a seam,
- `read=N MB` means chunk N+1 did.

That is why size, offset and alignment all looked innocent: a 34 MB read succeeds if every one of
its chunks lands inside one region, and a 1.4 MB read fails if its first chunk does not.

The guest never checks the AMPR result. It loads the length straight back out of the destination
(`mov r14d, dword ptr [rsi]` at `0x800dda369`), so a stale `0xFFFFFFFF` becomes a string length and
the cursor advance `(0x619D9DF7C + 0xFFFFFFFF + 3) & ~3` lands on `0x719D9DF7C` - exactly the
faulting address in the crash dump.

| | before | after |
| --- | --- | --- |
| AMPR reads not fully serviced | 4 of 160 | 0 of 410 |
| largest read completed | 34 MB, failed | 34 MB, complete |
| loading thread | AV at 36 s | no fault at 150 s |

Fix: split the span at region boundaries and write each piece. Contiguity is implicit because the
next address must itself land inside a region, so a genuine hole still faults. The single-region
fast path is untouched and the write lock is only taken when the start address is mapped, so
invalid-address probes still fail fast.

Do **not** fix this by coalescing regions at insert time. `ReleaseUntrackedAllocation` removes the
region whose base matches exactly and calls `_hostMemory.Free` with `MEM_RELEASE`, which on Windows
only accepts an allocation base; merging two mappings makes the second unfreeable.

`TryRead`, `TryReadExclusive` and `TryCopy` carry the identical single-region constraint and have
not been fixed yet. `TryCopy` is the HLE memcpy path, so a guest memcpy across a seam still fails
silently.

## Fixed: a host time zone without DST sends IL2CPP scanning through centuries

With the two fixes above the title stopped crashing but still sat on the loading spinner, and the
import stream was dominated by `sceKernelConvertLocaltimeToUtc` / `sceKernelConvertUtcToLocaltime`
called alternately. Tracing the inputs (`SHARPEMU_LOG_TIME_CONVERT=1`) showed a perfectly linear
walk *backwards* through history on the `Loading.PreloadManager` thread, about 43,800 conversions
per year scanned - roughly five per hour of history.

The reason is in the guest's own libc. `localtime` at `libc.prx+0x717C0`:

```
lea  rsi,[rbp-0x30]      ; &local_time out
lea  rdx,[rbp-0x28]      ; &timesec out
xor  ecx,ecx             ; dst_sec = NULL
mov  rdi,[rdi]           ; *time_t
call ...                 ; sceKernelConvertUtcToLocaltime
test eax,eax
cmp  dword ptr [rbp-0x1c],0   ; timesec+0xC == dst_sec
setne dl                      ; -> isdst for struct tm
```

The only thing it takes from us is whether `dst_sec` is non-zero. IL2CPP looks for a
daylight-saving transition by walking `localtime` backwards an hour at a time, so a zone that never
reports one is never satisfied.

| host zone | conversions in 90 s | position reached |
| --- | --- | --- |
| UTC (no DST) | 5,900,000 | 1893, still descending |
| Pacific (DST) | 1,820,000 | stops at the 2026-03-08 transition |

Extrapolated, the UTC case is about 88.7 million calls and 19 minutes before it reaches year 1.
With DST present it terminates in the first minute.

`SHARPEMU_GUEST_TIMEZONE` now selects the zone reported to the guest, because that is console
configuration rather than a host detail, and startup warns when the resolved zone has no DST rules.

Two things that were tried and did **not** help, recorded so they are not retried:

- Resolving `TimeZoneInfo` once instead of per call. Measured A/B over 90 s it moved 5.90M to
  5.92M conversions. The per-call cost is HLE dispatch, not `TimeZoneInfo`. The caching was kept as
  a cleanup, not as a fix.
- Suspecting the `dst_seconds` out-parameter width. It is `int32_t*` in
  `ConvertLocaltimeToUtc` and `uint64_t*` in `ConvertUtcToLocaltime` (confirmed against Kyty's
  `pthread.cpp:3732`/`:3760`), and both already matched.

## Where it stops now: the two loading threads park and are never woken

With the crash and both loops gone, the title renders its title screen at ~40 fps indefinitely and
never leaves it. `SHARPEMU_LOG_THREAD_STATE_MS=<ms>` (added for this) dumps every guest thread's
state, block reason and last import on a timer, and it localises the stall exactly. Reproduced
across runs:

| thread | imports | state |
| --- | --- | --- |
| `Loading.PreloadManager` | ~8.7M then **frozen** | Blocked `sceKernelWaitSema` ret=`0x800D878E0` |
| `Loading.AsyncRead` | ~4,760 then **frozen** | Blocked `sceKernelWaitSema` ret=`0x800BE5721` |
| `UnityGfxDeviceWorker` | climbing | Blocked `sceKernelWaitEventFlag` ret=`0x800F17094` (normal) |
| `UnityEOPThread`, `Gfx Task Executor` | climbing | healthy |

The render side is fine - that is why the spinner keeps animating. The loading side is dead.

With `SHARPEMU_LOG_SEMA=1` the handshake is exact. `Loading.PreloadManager` waits on semaphore
`0x39` (`Baselib_SystemSemaphore`, created `init=0 max=2147483647`, so the count is not the issue):

```
5596:  wait-block  handle=0x39  ret=0x800D878E0        <- waits
5684:  signal      handle=0x39  ret=0x800D87523  guest=0x0   <- woken, once
5685:  wait-resume handle=0x39  outcome=Acquired
10788: wait-block  handle=0x39  ret=0x800D878E0        <- waits again, never signalled
```

Line 10,788 of 183,399: it parks 6% into the run and the remaining 94% is the render loop spinning.
`Loading.AsyncRead` likewise parks on semaphore `0x23` (its work queue) at line 8,281, and `0x23` is
never signalled again either - `PreloadManager` is what signals it (from `0x800D923B9`), so both
halves of Unity's loader are waiting on each other's next handoff.

The single signal of `0x39` came from `guest=0x0`, i.e. the main entry thread, which is NOT
registered with the guest scheduler and uses the host-wait fallback. That thread is **alive and
looping** to the end of the log (waiting on `0x2A`, signalling `0x27`), so it is not itself stuck -
it simply never enqueues another load operation.

Ruled out while narrowing this, each by measurement:

- **AMPR is not the cause.** 405 reads, 405 completes, 405 resets, 810 wait/submit pairs, every
  `result=0`, and the guest then stops asking for data on its own.
- **The `PresentDoneFlag` event flag is healthy.** 2,115 sets against 1,047 waits, and the cycle is
  still granting (`set granted=1`) at the very end of the log.
- **Not a semaphore-layer bug.** 188,480 semaphore ops in one run, waits resuming normally
  throughout; only these two handles go quiet.
- **Not waiting for input.** `scePadInit` runs (the keyboard-mapping banner prints once), and
  `SHARPEMU_PAD_AUTO_PRESS=1` - which alternates Cross and Options every second - does not change
  the screen. Window-message injection is not usable here: the emulator's surface is not enumerable
  from a non-interactive session (`MainWindowHandle=0`, and `EnumWindows` finds no window for the
  pid).

### The main thread is parked in `JobGroup::Complete()`

Disassembling the main thread's wait site settles what it is waiting for. `0x800A9F753` is the
return address inside this loop:

```
0xA9F709: sub  eax, [rdi+0x2c8]       ; target - completedCount
0xA9F711: jle  done                   ; completed >= target -> return
loop:
0xA9F725: lock xadd [rbx+0xb8], -1    ; take a token from the userspace count
0xA9F72F: jle  block                  ; none left -> go to the kernel
0xA9F734: sub  eax, [rbx+0x2c8]       ; re-check completion
0xA9F73C: jg   loop
block:
0xA9F740: mov  rdi, [rbx+0xf8]        ; semaphore handle
0xA9F74E: call sceKernelWaitSema      ; edx=0, infinite
0xA9F753: jmp  recheck                ; <- where the main thread sits
```

So the main thread is waiting for a job group's completion counter at `object+0x2c8` to reach the
target in `r14`.

**CORRECTION — it is NOT stuck there.** Reading a parked thread and concluding its wait never
completes is exactly the mistake to avoid; the counters have to be read. `SHARPEMU_LOG_SEMA_DEREF`
(added for this) prints guest words alongside a semaphore trace, and with
`SHARPEMU_LOG_SEMA_DEREF=rbx+0x2c8,rbx+0xb8,r14+0` the loop is plainly healthy — note the dword at
`+0x2c8` is the LOW half of the 64-bit read:

| | completed `[rbx+0x2c8]` | target `r14` |
| --- | --- | --- |
| block | 0x972 | 0x973 |
| wake | **0x973** reached | 0x973 |
| block | 0x973 | 0x974 |
| wake | **0x974** reached | 0x974 |

The job group completes every time and the main thread immediately waits on the next one. That is a
normal per-frame job loop, running for as long as the process lives. The main thread and the job
system are both fine; the title simply never queues more loading work.

The semaphore signal path Unity uses is the mirror image of this and is visible at
`0x800D87523`:

```
0xD87232: lock xadd [r12+0xb0], 1     ; release a token, old value in eax
0xD8723E: js   0xD87511               ; old < 0 -> a waiter exists, wake it
0xD87511: mov  rdi, [r12+0xf0] ; mov esi,1 ; call sceKernelSignalSema
```

Both sides are userspace fast-path counters that only enter the kernel on contention, so the
kernel objects themselves are not where this breaks - which matches the traces, since every kernel
primitive involved behaves correctly. **The open question is why a queued job never completes while
the workers stay idle**, i.e. whether the work item is never made visible to a worker or the
completion counter increment is never observed by the main thread.

Note the main thread is not registered with the guest scheduler (`guest=0x0` in every semaphore
trace line) and takes the host-wait fallback rather than the scheduler's guest-thread path. That
asymmetry is the most suspicious remaining difference between it and the worker threads, and is
where to look next.

Also ruled out here: no semaphore signal is being rejected. `sceKernelSignalSema` returns
`INVALID_ARGUMENT` above `MaxCount` *before* reaching its trace call, so rejections are invisible
in the log - but every Baselib semaphore in play is created with `max=2147483647`, so none can be
hitting it.

## The oracle, and the exact scene the title never reaches

KytyPS5 runs this title, so it is the reference for "what should happen next". There is no C++
toolchain on the V620 host and building Kyty needs VS 2022 Build Tools plus Qt 6, but the project
ships prebuilt Windows binaries - grab the release instead:

```
https://api.github.com/repos/KytyPS5/KytyPS5/releases        # list
KytyPS5-2026-07-22-8587638.zip (18.6 MB) -> inspiration/kyty-build/    (gitignored)
kyty_emulator.exe --game "C:\sharpemu\games\superliminal" --printf-direction File --printf-output-file <path>
```

It emits ~125 MB of printf in 90 s. Read it with `Select-String` (it streams); `Get-Content` loads
the whole file and times out.

**The diff is one scene wide.** Both emulators load the same assets in the same order right up to
`level1`, and then Kyty goes on to `level2` and we never do:

| scene | Kyty | SharpEmu |
| --- | --- | --- |
| `level0` (+ res/resG/resS) | lines 9,430-11,724 | read |
| `level1` (+ res/resG/resS) | lines 31,818-64,935 | read (4x level1, 3x level1.resS) |
| **`level2` (+ res/resG/resS)** | **lines 3,979,649-3,990,912, ~88 s** | **never, even at 300 s** |

Kyty loads `level0` and `level1` within its first few seconds, then in ONE run went on to open
`level2` at ~88 s. That open is real, not a probe:

```
3986743: Open: .../Media/level2, [ok]
3990825: Open: .../Media/level2.resS, [ok]
3990912: Close: .../Media/level2
```

**CORRECTION — it is NOT reliably automatic.** Kyty reached `level2` in **1 of 3 runs**:

| Kyty run | length | save data | level2 |
| --- | --- | --- | --- |
| 1 | 90 s | none (first ever launch) | **opened at ~88 s** |
| 2 | 240 s, 61,386 flips | present | never |
| 3 | 130 s | deleted first | never |

Run 2 created `_SaveData/PPSA06084/SaveData`, which suggested a fresh-save auto-start, but deleting
it did not reproduce the transition. So the trigger is something else and is most likely **input** -
a button press to start the game - which makes run 1 the anomaly, not the rule. Do not plan around
"it happens on its own".

What the oracle DOES establish, and it is worth a lot: the dump is good, the V620/Vulkan path is
adequate, and `level2` is loadable on this exact host. It also gives a progress yardstick: Kyty had
rendered **11,338 flips** when it made the jump.

**Our run is not merely slower.** A 900 s run reached **14,460 flips** - past that yardstick - with
AMPR reads frozen at 407 the whole time and no `level2`. So this is a stall, not a pace problem.

## FOUND IT: the gate is `PS5Manager.CurrentState`, and it is measured, not inferred

The title's own coroutine `SplashLoader.<LoadScene>d__18::MoveNext` will not activate the loaded
scene until `PlasticFern.Scripts.PS5Manager.CurrentState == Initialized(2)`. Module-relative
addresses in `Il2CppUserAssemblies.prx`:

```
0x140e8b  call SceneManager::LoadSceneAsync(index)
0x140ea5  call AsyncOperation::set_allowSceneActivation(FALSE)   <- set false immediately
0x140f06  vmovss xmm1,[0x203bf7c]=0.9f ; vucomiss ; jbe          <- wait for progress >= 0.9
0x141160  lea rbx,[0x282c040]        ; Il2CppClass* PS5Manager
0x141180  mov rax,[rdi+0xb8]         ; klass->static_fields
0x141187  cmp dword ptr [rax+0x138], 2   ; CurrentState == Initialized
0x14118e  jne 0x14137e               ; -> current=null, state=4, return true (yield null)
0x1411ad  call PS5Manager::IsLoading ; true -> yield null too
0x141236  call AsyncOperation::set_allowSceneActivation(TRUE)    <- the flip
```

The jump table at `0x22610f0` sends state 4 back to `0x141040`, which re-enters the same test, so
state 4 is an unbounded `while (gate) yield return null;`. `CurrentState` is written to 2 at exactly
ONE instruction in the whole 27 MB code segment - `0x1434547`, at the very end of
`<Initialize>d__84::MoveNext`. So the contract is simply: **`PS5Manager.Initialize()` must run to
completion.**

`SHARPEMU_PS5MANAGER_PROBE=observe` reads that field live (chain: module base + `0x282C040` ->
`Il2CppClass*` -> `+0xB8` static_fields -> `+0x138`; the base is re-randomised per run so it is
resolved through the module registry at call time, driven off `sceSystemServiceGetStatus` because the
title calls it once per frame). Result, 121 samples in one run:

```
ps5manager: base=0x20E2CD90000 klass=0x102231630 statics=0x605B6DD80 CurrentState=1 CurrentSaveState=0
```

**`CurrentState=1` (Initializing) forever.** Initialize starts and never finishes. Every earlier
symptom now has one cause: the scene bytes are all read, the loader parks, the render loop keeps
spinning, input is polled and discarded.

`=force` writes 2 once, as a diagnostic override. It is not a fix - it asserts a completion that did
not happen - but it proves the gate:

| | normal | forced |
| --- | --- | --- |
| AMPR reads | 405 (invariant) | **686** |
| draws | 69 | **827** |
| `not safe` | 353 | 0 (with main entry on native worker) |
| outcome | alive, parked | dies ~90 s |

Past the gate the title loads content it otherwise never touches (388 further reads of
`sharedassets1.assets.resS`) and then hits **our own** fail-fast:
`Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code` - the hazard
documented in `DirectExecutionBackend.NativeWorker.cs`. `SHARPEMU_MAIN_ENTRY_NATIVE_WORKER=1`
improves it (draws 69 -> 827, `not safe` 353 -> 0) but does not remove it, so the offending guest
stub is on another thread.

**Two things to fix, in order:** (1) make `PS5Manager.Initialize` able to complete - the workers
traced its first await to Unity's PSN `WorkerThread::ExecuteOp`, which sets `IsCompleted` only on the
success path, so any request that throws leaves the op permanently incomplete; and (2) the
`UnmanagedCallersOnly` fail-fast, which is now on the critical path rather than theoretical.

### Where PS5Manager.Initialize actually stops

`SHARPEMU_LOG_IMPORT_FILTER=Np` gives the ordered PSN call sequence (the filter is a single
substring matched against library name, export name or NID). Ours:

```
sceNpGetAccountIdA / GetAccountCountryA / GetState / GetNpReachabilityState   ret=0x800EF4xxx (eboot)
sceNpWebApi2Initialize                                                        ret=0x2CAB3158AE2 (PSNCore.prx)
sce::Np::CppWebApi::Common::initialize                                        ret=0x2CAB3158AF9
sceNpGameIntentInitialize                                                     ret=0x2CAB3131CE1
sceNpSessionSignalingInitialize                                               ret=0x2CAB314879F   <- LAST
```

Kyty, immediately before it loads level1 (`kyty.log:31435-31449`), continues:
`NpWebApi2PushEventCreateHandle` -> `NpWebApi2CreateUserContext` -> `NpTrophy2CreateContext` ->
`NpTrophy2RegisterContext` -> the UDS pair, then loads `/app0/Media/Plugins/SaveData.prx`
(`kyty.log:31457-31592`), and opens level1 at 31818.

We never make any of those calls. The census confirms it: **zero** `sceNpTrophy2*`, **zero**
`sceNpUniversalDataSystem*`, **zero** `sceSaveData*` in 15,829,799 calls, even though all three are
implemented (`NpTrophy2Exports.cs:79`, `NpUniversalDataSystemExports.cs:251/375`,
`SaveDataExports.cs:636/752`). `SaveData.prx` is loaded as a dependency but never *started* by the
guest - only `PSNCore.prx` and `PSNCommon.prx` are.

And there is **no PSN worker thread**. Unity's PSN plugin drives its requests from a `WorkerThread`,
and the full guest thread list (44 threads) contains no such thread - only Unity job workers, FMOD,
the two Loading threads, Gfx/EOP and three unnamed `Thread-XXXX`. That matches the shape the audit
found in the guest's own code: `WorkerThread::ExecuteOp` sets `IsCompleted` **only on the success
path**, so a request that throws leaves the op permanently incomplete, and `PS5Manager.Initialize`
awaits `IsCompleted` forever.

**Ruled out: missing exports.** The obvious theory was that the guest calls
`sceNpWebApi2PushEventCreateHandle` next, finds it unimplemented, and throws. It does not hold. We do
have zero `libSceNpTrophy2` and zero `libSceNpWebApi2` exports, and Kyty's NIDs `WV1GwM32NgY`
(PushEventCreateHandle), `sk54bi6FtYM` (CreateUserContext), `Bagshr7OQ6Q` (Trophy2CreateContext) and
`5zBnau1uIEo` (UDS CreateContext) are all absent from our tree - **but the guest never calls them**
(0 hits for each NID in the run log) and there is not a single unresolved-import warning. The guest
decides not to proceed; it does not fail on a missing symbol.

**Ruled out: init return contracts.** Every value we return matches the oracle.
`NpSessionSignalingInitialize` -> 0 (`KytyPS5/src/libs/libNet.cpp:1406`), `NpGameIntentInitialize` ->
0 (`:3122`), `NpWebApi2Initialize` -> a positive context id (`:3219`, `return ++id`); ours returns 1.
The full init chain, in order and with import indices, is:

```
#4886846  libSceJson       Initializer ctor
#4886852  sceNpWebApi2Initialize
#4886853  CppWebApi::Common::initialize
#4886856  libSceJson       Initializer::initialize
#4886909  sceNpGameIntentInitialize
#4886960  sceGameUpdateInitialize
#4887092  sceNpSessionSignalingInitialize      <- last, nothing after
```

(`SHARPEMU_LOG_IMPORT_FILTER=Initialize` produces this; exclude `libSceAjm`, whose per-frame
`sceAjmBatchInitialize` floods the filter.)

So the next question is narrow and concrete: **what happens on that worker between
`sceNpSessionSignalingInitialize` returning and the next expected call
(`NpWebApi2PushEventCreateHandle`)?** Everything before it succeeds; nothing after it is ever
attempted. Note also that Kyty patches unresolved PLT imports to stubs
(`Relocate: unresolved PLT import patched to stub ...[NpSessionSignaling_v1]`, `[PSNCommon_v1]`,
`[Net_v1]`), so PSNCore's init proceeds there even where an API is unimplemented - worth comparing
against what our stubs return on the same NIDs.

## Reading the managed side: IL2CPP metadata

The stall is in the title's own C# code, so the useful move is to read it. `global-metadata.dat`
(`Media/Metadata/`, 11.2 MB) carries every type, method and string-literal name.
`scripts/il2cpp_metadata.py` dumps identifiers and literals; `scripts/il2cpp_types.py` lists a
type's methods.

Two format notes cost time and are worth keeping. This build is **metadata v24**, but the record
sizes are not the ones usually quoted: `Il2CppTypeDefinition` is 92 bytes (confirmed: 1,044,292 /
92 = 11,351 types exactly) while `Il2CppMethodDefinition` is the **32-byte** variant, not 52.
Derive it rather than assume - only 32 gives a method count (82,159) above the largest
`methodStart` in the type table (74,627). And string literals are length-prefixed through the
`stringLiteral` table (`{uint32 length; uint32 dataIndex}`), not NUL-terminated.

**What it shows.** The title's loading flow is its own, not Unity's:

```
SplashLoader        : Awake, OnEnable, Start, OnActivityLaunched, LoadScene, FadeOut, Update, DebugLog
LevelLoadHelper     : LoadSceneAsync, LoadingScreenLoadAsync, _CalculateLoadProgress
also present        : LevelLoadingState, levelLoadingRoutine, loadingScreenIndexOverride,
                      TitleScreen, MainMenu, SplashScreen literal, 'Loading Started', 'Loading complete'
```

`SplashLoader.OnActivityLaunched` is the interesting one: a **PS5 Activity / GameIntent** callback
sitting right next to `LoadScene`. Kyty's log shows the title's own `PSNCommon_v1` plugin building
an `event_name = activityTerminate` object, with those plugin functions late-resolved at runtime.
Our census shows the title calling `sceNpGameIntentInitialize` exactly once and **nothing else from
that library**. Whether `SplashLoader` gates `LoadScene` on an activity event it never receives is
the next thing to settle - it is the first hypothesis in this whole investigation that names a
specific guest method next to a specific unimplemented system service.

The metadata carries the whole activity surface, and it is Unity's PSN plugin rather than raw
libSceNpGameIntent:

```
Unity.PSN.PS5.GameIntent
add_ActivityLaunched / remove_ActivityLaunched      <- what SplashLoader subscribes to
OnGameIntentNotification / add_OnGameIntentNotification / GameIntentNotification
GameStartFromActivity, InitializeActivity, StartActivity, ResumeActivity, EndActivity
ActivityStartEventName / ActivityTerminateEventName  <- Kyty's "activityTerminate"
BeforeSplashScreen, LaunchActivity, LaunchMultiplayerActivity
```

That lines up end to end: `SplashLoader` subscribes to `ActivityLaunched`, the title asks whether it
was `GameStartFromActivity`, and in Kyty it gets far enough that its own `PSNCommon_v1` plugin builds
an `activityTerminate` event. In our runs the title never posts a UDS event at all and calls
`sceNpGameIntentInitialize` exactly once.

The plugin layer itself loads cleanly, so this is not a load failure: `Imported data rebind:
rebound=76, unresolved=0`, 4,221 import stubs with 2,692 LLE redirects, and `PSNCore.prx` resolves
all 2,435 of its imports. Unity's PSN plugin operations are asynchronous and pumped from managed
code, so the question to answer next is whether one of those async requests never completes and
parks `SplashLoader` before it reaches `LoadScene`.

Not yet ruled in: the dlsym failures are all Unity's optional plugin-interface probes
(`UnityPluginLoad` and friends) against the audio and PSN modules, which fail on real hardware too.

**To go further you need method-to-address mapping.** The names above come from the metadata; to see
what `SplashLoader.Start`/`Update` actually wait on you must resolve them to native addresses via
the `Il2CppCodeRegistration` method-pointer table inside `Il2CppUserAssemblies.prx`. That is the next
concrete piece of work, and `SymbolMap` does not shortcut it - it is 56,685 `(address, size)` records
with no names at all.

### The scene data is fully delivered, and the stop is deterministic

Two measurements worth having before anyone theorises about truncated loads or races.

**level1 is read completely.** The furthest offset AMPR reaches matches the on-disk size exactly on
every level file, so nothing is missing when the title stops:

| file | bytes on disk | furthest offset reached |
| --- | --- | --- |
| level0 | 10,832 | 10,832 |
| level0.resS | 131,232 | 131,232 |
| level1 | 2,541,088 | 2,541,088 |
| level1.resS | 6,558,192 | 6,558,192 |

**It stops in the same place every time.** Four consecutive 150 s runs: `levels=[level0,level1]`,
405-411 AMPR reads, 0 access violations, no `level2`. Kyty's 1-in-3 looked like a race, but ours is
not probabilistic - we are blocked, not unlucky.

Taken with the rest, the shape is a Unity `LoadSceneAsync` parked with `allowSceneActivation`
false: all bytes read, PreloadManager idle by design, render loop healthy, input polled and
discarded. What is missing is whatever makes the title flip that flag.

### Ruled out by measurement: PSN sign-in state

`SHARPEMU_NP_FAKE_SIGNED_IN` on versus off is identical - `levels=[level0,level1]`, 409 vs 406 AMPR
reads, 0 access violations both ways. The theory was that believing itself signed in sends the title
down a PSN/activity path it cannot finish, and that signed-out would skip it. It does not.

`sceUserServiceGetEvent` is also correct: it delivers exactly one LOGIN event (type 0, primary user)
and then `NoEvent`, which is precisely what the census shows (1 success, 2,046 no-event, one call per
frame).

### Ruled out by measurement: input

`SHARPEMU_LOG_PAD=1` logs the button word actually written into the guest's pad record on every
`scePadRead`. With `SHARPEMU_PAD_AUTO_PRESS=1` the guest demonstrably receives real presses:

```
pad.read #4  buttons=0x00000008   (Options)
pad.read #25 buttons=0x00004000   (Cross)
...
0x00000000 x94   0x00004000 x47   0x00000008 x46
```

The title polls `scePadRead` once per frame (2,046 calls in a census run) and gets Cross and Options
alternating, and does not advance. So it is not sitting at a "press any button" prompt and input is
not the blocker. Two earlier readings of this were wrong and are recorded above: the auto-press test
was valid all along, and `WriteCurrentPadData` (once named `WriteNeutralPadData`) has always filled
the record from live input.

### Probably not the blocker: the dropped NGG primitive exports

Six shaders warn `ngg-prim-export-dropped`, all with the identical trivial signature
(`target=20 src=v0 en=0x1 done=1` at `pc=0x0028`) and all falling back to the draw's index buffer.
The warning text says that fallback "is correct only for a pass-through primitive shader", and six
distinct shaders sharing one trivial export pattern are very likely exactly that. Worth building the
real NGG amplification path (`AgcExports.cs` ~4337) for other titles, but do not expect it to move
this one.

### Not a bug: the contended trylock

`scePthreadMutexTrylock` (`upoVrzMHFeE`) returning `BUSY` thousands of times on one mutex looks
like a deadlock and is not. The owning thread keeps locking and unlocking normally throughout, and
the BUSY count tracks the frame count almost exactly - it is a try-acquire-else-skip-this-frame
pattern. `SHARPEMU_LOG_PTHREAD_MUTEX_FILTER=<addr>` traces a single mutex with owner and recursion
depth, which is what settled it.

### Not a bug: the direct-memory `TRY_AGAIN`

`sceKernelAllocateMainDirectMemory` returning `EAGAIN` is correct. `rdi` is `len`, not a search
start, and the guest asks for 100-600 GiB. The simulated pool is 16 GiB, already larger than a real
PS5 (Kyty uses 13,824 MiB).

### Diagnosing this class of failure again

A process that appears to stall and then vanishes with no log line is a fail-fast, not a stall.
`Get-WinEvent -FilterHashtable @{LogName='Application'}` gives the exception code, and the WER
report under `C:\ProgramData\Microsoft\Windows\WER\ReportArchive` carries the `__fastfail` subcode
in `Sig[8] Exception Data`. Run-after-run this session reported `exited code=` and it was read as a
stall every time. Ask the operating system why a process died before theorising about the guest.

## The title-screen gate, localised to one byte

Measured 2026-07-27. **This supersedes every earlier "it stalls waiting on X" reading in this file.**

`PS5Manager.CurrentState` is `Initializing(1)` for 89/89, 48/48 and 46/46 samples across three runs.
Probe: `SHARPEMU_PS5MANAGER_PROBE=observe` (or `force`), implemented in
`src/SharpEmu.Libs/SystemService/Ps5ManagerStateProbe.cs`. The chain is module base + `0x282C040` ->
`Il2CppClass*` -> `+0xB8` static_fields -> `+0x138` CurrentState, `+0x13C` CurrentSaveState,
`+0x140` CurrentMountState, `+0x1D0` the PSN `InitResult`. The module base re-randomises every boot,
so it is resolved through `KernelModuleRegistry` at call time, never cached.

`State` enum: `Waiting=0, Initializing=1, Initialized=2, ShuttingDown=3`.

### Why it never advances

Addresses are `Il2CppUserAssemblies.prx` module-relative; **file offset = vaddr + 0x4000**.

`CurrentState = 2` is written at **exactly one address**, `0x1434547`, at the end of
`<Initialize>d__84::MoveNext` (`0x1433F80`). Reaching it requires, in order: PSN init succeeds, then
three `while (!op.IsCompleted) yield return null` waits complete (UDS, Trophies, AddUser), then
SaveData init succeeds. The first gate is the one we fail:

```
0x1434850  call 0x1889BC0            ; Unity.PSN.PS5.Main::Initialize(2048, 2048, 0)
0x1434860  mov  [rsi+0x1D0], rax     ; InitResult low  - UNCONDITIONAL
0x1434867  mov  [rsi+0x1D8], rdx     ; InitResult high - UNCONDITIONAL
0x1434875  cmp  byte [rax+0x1D0], 0
0x143487C  je   0x1434E1E            ; LogError "PSN: Failed to initialize!" -> return false
```

**Measured: `statics+0x1D0` is 16 zero bytes on every sample.** That byte is the guest's own branch
condition, so the guest takes the abort. `InitResult` comes back in RAX:RDX and the store is
unconditional, so all-zero is equally consistent with "returned `{initialized=false, sdkVersion=0}`"
and with "never returned" - do not over-read it either way.

**The stall is self-latching and there is no retry.** `PS5Manager::Awake` starts the coroutine only
when `CurrentState == 0` (`0x882F43`), and `PS5Manager::Initialize` has no other caller. Once the
coroutine returns false, nothing restarts it, ever.

**The downstream gate is literal**: `SplashLoader/<LoadScene>d__18::MoveNext` spins
`while (CurrentState != Initialized) yield return null` (`0x141187`). That is precisely why the
title screen renders forever. `PS5Manager::Update` is also a no-op while state is 0 or 1
(`0x88309B`), so `Unity.PSN.PS5.Main::Update` never runs - which is why `CurrentSaveState` and
`CurrentMountState` stay 0. Those are consequences, not independent blockers.

### What PSN init actually does, and why "no failing import" is not "no failure"

`Main::Initialize` resolves native `PSNCore!PrxInitialize` and throws on its error out-param:

```
0x1889CA6  call rax                  ; PSNCore!PrxInitialize(&initResult, &error)
0x1889CA8  cmp  dword [rsp+0x60], 0
0x1889CAD  jne  0x188A30B            ; object_new -> PSNException ctor -> raise_exception -> ud2
0x1889CB3  ...                       ; success: starts 9 WorkerThreads, Start()s ~14 subsystems
```

`PrxInitialize` is export NID `MX0DVW-YA6Q` at PSNCore vaddr `0xCD0` -> `0xCE0`. It loads 8
sysmodules (`0x105,0x110,0x113,0x115,0x9D,0xA8,0x127,0x112`), writes
`InitResult{initialized=1, SceSDKVersion=0x5000033}` at `0xE81`/`0xE86`, then brings up the network
stack - `sceNetPoolCreate("WebApiUserProfile", 0x4000, 0)` -> `sceSslInit(0x60000)` ->
`sceHttp2Init(netMemId, sslCtxId, 0x80000, 1)` - and runs ~24 subsystem inits at `0xE9C..0xF26`.

An HLE effect census (`SHARPEMU_HLE_EFFECT_CENSUS=1`) of a live run shows **every one of those
succeeded**, each called exactly once: `sceNetPoolCreate`->1, `sceSslInit`->1, `sceHttp2Init`->1,
`sceNpWebApi2Initialize`->1, and `sceNpSessionSignalingInitialize`, `sceNpGameIntentInitialize`,
`sceNpEntitlementAccessInitialize`, `sceNpGetState`, `sceNpGetAccountIdA`,
`sceNpGetNpReachabilityState`, `sceNpGetAccountCountryA`, `sceUserServiceGetAgeLevel` all ->0.
Census totals: 287 exports, 20.4M calls, 4,586 errors, **every error in the known-benign set**
(accept EWOULDBLOCK 1,971; no-event 1,229; equeue timeouts 1,227; mutex-busy 76; missing-file
`stat`/`open` probes; `sceSysmoduleIsLoaded` x2; `sceLibcMspaceMallocStatsFast` x1).

So the remaining suspect is **an export that returns success while writing an out-param the guest
then rejects** - our synchronous success being a lie. `sceNpGetAccountIdA` handing back id 0,
`sceNpGetState`, `sceNpGetNpReachabilityState`, `sceUserServiceGetAgeLevel` and
`sceNpGetAccountCountryA` are all in that shape and are the place to look next.

**Method note that matters here:** `ShouldLogImportResult` caps `[LOADER][WARN] Import#... result:`
at 8 samples per (nid, result) pair and suppresses a known-benign list entirely. "Only 9 failing
imports" therefore means "only two *distinct* non-suppressed failing NIDs", not "only nine
failures". Use the census for a real count.

**Also measured:** none of the 9 named PSN `WorkerThread`s exist among the 45 guest threads. And the
guest's own `Debug.Log` ladder ("PS5 Intialize(): Initializing...", "PSN: SceSDKVersion ", ...) never
reaches our log - engine `printf` does (`[DEBUG][PRINF]` prints Unity's `todo:` messages), but
`Debug.Log` is stripped in this release build. It would have answered this in one grep; it is not
available.

### Forcing the gate: what is behind it, and the second blocker

`SHARPEMU_PS5MANAGER_PROBE=force` writes `CurrentState = 2`. It is a diagnostic override, not a fix -
it asserts an initialization that did not happen. With it the title clearly advances:

| | observe | force | force + `SHARPEMU_MAIN_ENTRY_NATIVE_WORKER=1` |
|---|---|---|---|
| draws | 181 | 454 | **827** |
| lifetime | alive at 130 s | died 53 s | died 81 s |

and compute dispatches (`VulkanComputeGuestDispatch`) start appearing, which they never do otherwise.

It then dies in **our own runtime**, not the guest:

```
Fatal error.
Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.
  at DirectExecutionBackend.ExecuteGuestContinuationEntry(...)
  at DirectExecutionBackend.RunGuestThread(GuestThreadState, String)
  at DirectExecutionBackend+GuestExecutionRunner.ThreadMain(UInt64)
```

This is the tradeoff already documented at `DirectExecutionBackend.cs:6723`: a guest stub entered
from a CLR-created thread sits above managed frames, so when the guest re-enters managed code the
runtime fail-fasts and takes the process. Only `tbb_thead` was migrated to a native worker, because
migrating everything once increased splash hangs. Superliminal reaching this on its real critical
path is exactly the "compare the two on a real title" case that migration was deferred for.

`SHARPEMU_GUEST_THREADS_NATIVE_WORKER=1` (new, off by default) routes every guest thread and
continuation to a native worker. **Measured: it does not fix the fail-fast and it regresses
throughput** - 450 draws / 54 s, versus 827 / 81 s for `SHARPEMU_MAIN_ENTRY_NATIVE_WORKER=1` alone.
Recorded so nobody re-tries it blind. The wholesale migration is not the answer; the fail-fast needs
root-causing at the specific re-entry site.


### The exact throw: all eight PSN subsystem singletons are null

Narrowed 2026-07-27, and it corrects the section above. `Main::Initialize` does **not** report
failure - it computes success and then never returns.

Reading `Unity.PSN.PS5.Main`'s own statics (class slot vaddr `0x2829908`, `+0xB8` static_fields)
while the title sits on the gate:

```
PsnMainStatics = 01000000 33000005 90AFCD08 06000000
                 ^^ initialized = 1      ^^^^^^^^^^ SceSDKVersion = 0x05000033
```

That is exactly the `InitResult{initialized=1, SceSDKVersion=0x5000033}` `PSNCore!PrxInitialize`
writes at `0xE81`/`0xE86`. **PSN init succeeded.** `Main::Initialize` has exactly one `ret`
(`0x188A24B`) and it returns these very qwords (`0x188A213`: `mov rax,[r12]` ->
`mov rcx,[rax+0xB8]` -> `mov rax,[rcx]` / `mov rdx,[rcx+8]`). So the result was ready and the
caller never got it - `PS5Manager`'s unconditional store at `0x1434860` never executed.

Between computing the result and returning it, `Main::Initialize` starts nine subsystems. Each site
is the same unrolled idiom: load the class, run its cctor if `[klass+0x12F] & 2` is clear, read
`static_fields[0]`, **null-check it**, then call `0x1886300`:

```
mov  rax, [rdi + 0xB8]     ; static_fields
mov  rdi, [rax]            ; static_fields[0] - the singleton
test rdi, rdi
je   0x188A304             ; -> call 0x1A6BD40 ; ud2   (null reference throw)
```

Probed at runtime (`SHARPEMU_PS5MANAGER_PROBE=observe` prints `psn_singletons`):

```
[0] chk=0x1889D08 : 0x500003300000001     <- Main itself, the InitResult, non-null, passes
[1] chk=0x1889E17 : NULL                  <- throws here
[2] chk=0x1889E94 : NULL
[3] chk=0x1889F11 : NULL
[4] chk=0x1889FBE : NULL
[5] chk=0x188A03B : NULL
[6] chk=0x188A0B8 : NULL
[7] chk=0x188A181 : NULL
[8] chk=0x188A1FE : NULL
```

Class slots, in call order: `0x2829908` (Main), then `0x2829890`, `0x2829850`, `0x2829920`,
`0x2829928`, `0x2829900`, `0x2829930`, `0x28298E8`, `0x28298A8`.

**So the whole chain is:** the first subsystem singleton is null -> `NullReferenceException` at
`0x1889E17` -> `Main::Initialize` abandoned -> `PS5Manager`'s `InitResult` store never runs ->
`CurrentState` stays `Initializing(1)` -> `SplashLoader` spins on `!= 2` forever -> title screen
forever. It also explains, without any extra assumption, why none of the nine PSN `WorkerThread`s
exist: the throw is on the first one.

**Eight of eight null is a common-cause signature, not eight independent bugs** - and the guest's own
class-initialization guard says what the common cause is. Every one of those singleton reads is
preceded by:

```
test byte [klass+0x12F], 2
je   use                     ; bit clear -> skip the initializer
cmp  dword [klass+0xE0], 0
jne  use                     ; nonzero   -> skip the initializer
call 0x19D7620               ; IL2CPP class init
mov  rdi, [rbx]              ; reload the class, then read static_fields[0]
```

so the initializer runs only when bit 1 of `+0x12F` is **set** and `+0xE0` is **zero**. Probed:

```
[0] Main        : 0x500003300000001  (b12F=0, dE0=0, runs_init=0)
[1].. [8] all   : NULL               (b12F=1, dE0=0, runs_init=1)
```

**All eight are in the "run the initializer" state, and their static is still null after it.** And
`+0xE0` never becomes nonzero across a 60 s run, so the class is never successfully marked
initialized either - the initializer is being entered over and over and never completing. That is
one bug, upstream of all eight singletons, not eight missing objects.

(An earlier version of this section read that guard backwards and called `+0x12F` "initialized". It
is the opposite: bit set means the initializer still has to run. The probe now reports both words
raw for that reason.)

The two calls immediately preceding the first start - `0x188A360` and `0x188A4A0` (from `0x1889D7F`
and `0x1889DAA`) - open with the same one-shot metadata-usage idiom
(`cmp byte [rip+flag],0 / call 0x1A6BC00 / mov byte [rip+flag],1`), which is the same family as the
"internal-call table is used before it is resolved" problem recorded earlier in this file.

**Next step: find why IL2CPP class initialization never completes for these types** - `0x19D7620`
is the entry to instrument. Note this is guest-side state, so the emulator bug is upstream of it. Do
not "fix" it by writing singletons into guest memory.


### Independent confirmation, and one inference of mine that was unfounded

A second analysis pass (static only, no emulator) reached the same conclusion from a different
direction, and rated it ~85%: `Main::Initialize` **has no normal path that returns
`{initialized=false, sdkVersion=0}`** after the observed PSNCore calls. Its only native-error branch
(`0x1889CA8 -> 0x188A30B`) raises a `PSNException`; its only normal return (`0x188A213..0x188A24B`)
loads the unchanged static result. So if execution had reached `0x188A23D`, the returned low byte
would necessarily be 1. A zero at `PS5Manager statics+0x1D0` can therefore only mean the store at
`0x1434855` was never reached - which is exactly what the null singleton at `0x1889E17` produces.

It also settled the out-parameter question that motivated the search: **the five suspect getters
cannot fail PSN init, because their NIDs do not occur in PSNCore's dynamic symbol table at all**
(symtab vaddr `0x51E18`, `DT_SCE_SYMTABSZ=0xE538`, 2,445 entries). Superliminal calls them from
`eboot.bin`, and each call site ignores the value we were worried about:
`sceUserServiceGetAgeLevel` (`eboot+0xEF455F`) tests only `EAX != 0`; `sceNpGetAccountIdA`
(`+0xEF46B1`) branches only on the return code and never compares the id; `sceNpGetAccountCountryA`
(`+0xEF46FC`) copies two bytes; `sceNpGetNpReachabilityState` (`+0xEF471A`) and `sceNpGetState`
(`+0xEF4763`) ignore `EAX` entirely. Our written sizes and layouts match the reconstructed 4.03
firmware. Likewise the `0x28A40` chain tests only return values, and we return positive ids from all
four creators. **Do not patch NP out-params for this.**

**Correction to the section above: "none of the nine PSN WorkerThreads exist" was not a sound
inference at the time it was made.** `scePthreadRename` used to trace and return success without
recording anything, so any thread the guest renamed after creation kept a synthetic `Thread-<id>`
label and could not be recognised. That is now fixed and unit-tested. Re-measured afterwards: 3
unnamed threads out of 45, no PSN worker names, and `scePthreadRename` is called only 3 times in a
whole run - so the workers really are absent, but that is now a measurement rather than an
assumption. The conclusion is unchanged; the reasoning behind it was not sound until the export was
fixed.



## The one experiment that decides it

A static audit of `Unity.PSN.PS5.Main::Initialize` established something the state probe could not:
**there is no normal path that returns `{ initialized=false, sdkVersion=0 }` after the PSNCore calls
we observed.** So the zero in `PS5Manager` statics is not a false return. It is an untouched
destination - the call never came back, or it raised.

The proof, as far as static analysis goes:

1. `PSNCore+0xE81/+0xE86` writes `{1, 0x05000033}` before the subsystem chain runs.
2. Every firmware export in that chain completed. This is stronger than an entry count: the census
   records a call only after the export delegate returns
   (`src/SharpEmu.HLE/Diagnostics/HleEffectCensus.cs:162-164,217-230`).
3. On return from native, managed code loads the result at `Il2CppUserAssemblies+0x1889CC1`, writes
   its own static at `+0x1889CF2/+0x1889CF9`, and never clears it.
4. The only native-error branch is `+0x1889CA8 -> +0x188A30B`, and it constructs a `PSNException` and
   raises. It does not return a false `InitResult`.
5. The only normal return is `+0x188A213..+0x188A24B`, loading that unchanged static into `RAX:RDX`.

Therefore if execution reached `+0x188A23D`, the low byte of the return **must** be 1. A zero at
`PS5Manager statics+0x1D0` can only persist because `+0x1434855` was never reached, or because of an
aggregate-return corruption that no measurement currently supports.

**Ranked:**

1. *Managed post-native non-return or exception* - ~85%. `Main::Initialize` enters
   `Il2CppUserAssemblies+0x1889CB3` after native returns, calls the common worker constructor at
   `+0x1889660` ten times (`+0x1889249..+0x188959E`), starts the worker set through `+0x1886300`,
   and starts managed subsystems up to `+0x188A20E`. The null/error exits at `+0x188A304` and
   friends all raise; none returns false. This fits the measured absence of PSN worker threads.
2. *Native guest-only non-return* - ~14%. The interval is after the last completed import in
   `PSNCore+0xE9C..+0xF30` and before the epilogue at `+0xF3E`. `PrxInitialize` has no retry loop and
   ignores subsystem return values, which makes this much less likely, but the census cannot prove an
   internal non-HLE callee returned.
3. *`PrxInitialize` never reached* - ruled out. PSNCore uniquely makes the measured net-pool / SSL /
   HTTP2 / WebApi2 / GameIntent / EntitlementAccess / SessionSignaling sequence with the exact
   constants at `+0x28A7E..+0x28AF9` and `+0x1CDC/+0x1683/+0x1879A`.

The 26 calls in `PSNCore+0xE9C..+0xF26` are mostly static constructors and registrars. Only five are
real initializers - `sceNpGameIntentInitialize` (`+0xEB0`), a sysmodule/GameUpdate init (`+0xEC9`),
`sceNpEntitlementAccessInitialize` (`+0xEF1`), `sceNpSessionSignalingInitialize` (`+0xF12`) and a
final sysmodule check (`+0xF26`) - **and every one of their return values is ignored or merely
printed**. The rest are allocators, no-ops and error-text registrars, and none reads any of the five
suspected out-parameters.

### The experiment

One non-stopping address trace, hit counters plus `RAX:RDX`, at six addresses:

| # | Address | Meaning |
|---|---|---|
| 1 | `PSNCore+0xF3E` | native epilogue |
| 2 | `Il2CppUserAssemblies+0x1889CA8` | returned from native |
| 3 | `+0x1889CB3` | managed success tail |
| 4 | `+0x1889E27` | first direct worker `Start` |
| 5 | `+0x188A23D` | normal managed return |
| 6 | `+0x1434855` | coroutine returned from `Main::Initialize` |

It has an unambiguous reading:

- **no hit at 2** - hypothesis 2, native non-return.
- **hit at 2 but not at 5** - hypothesis 1, and the last address hit names the exact failing call.
- **hit at 5 with low `RAX` byte 1, but zero at 6** - aggregate-return corruption, currently
  unsupported by any measurement.

If the trace lands on hypothesis 1 and the failing call is `Thread.Start`, the emulator-side targets
are `KernelExports.cs:417-469` (`PthreadCreateCore`) and
`DirectExecutionBackend.cs:3800-3828` (`TryStartThread`) - make the created guest worker actually
become runnable and return promptly to its creator, or propagate a real error instead of reporting
success while stranding `Main::Initialize`. Effort M, confidence medium and UNVERIFIED until the
trace runs. If it lands on hypothesis 2, the last internal PSNCore address before `+0xF3E` is the fix
site, and nothing currently ties that path to any of our HLE functions - naming a patch now would be
guesswork.

**Proof of fix, so it cannot be claimed prematurely:** `PS5Manager statics+0x1D0` begins
`01 00 00 00 33 00 00 05`; `CurrentState` reaches 2 in every sample; `+0x188A23D` and `+0x1434855`
each hit once; PSN worker starts become observable and named.
## 2026-07-27: the load never finishes because a GC suspend acknowledgement can never arrive

This supersedes the ranked hypotheses above. Hypothesis 1 ("managed post-native non-return") was
right in outcome but the mechanism is lower down than the managed tail, and it is not PSN-specific at
all.

### What Unity is doing

IL2CPP's collector suspends the world by calling `sceKernelRaiseException(target, 30)` at every other
guest thread and then waiting for each to acknowledge. Type `0x1E` = 30 is exactly what
`kernel_unityExports.RaiseException` accepts (`kernel_unityExports.cs:14`). Measured over a 180 s
boot with `SHARPEMU_LOG_GUEST_EXCEPTIONS=1`:

```
guest_exception.raise            27
guest_exception.queued           21
guest_exception.safe_point_enter 21
guest_exception.delivery_enter    6      <- only six ever complete
guest_exception.delivery_exit     6
```

and the backend eventually reports:

```
Guest exception still queued: target=0x18B9B070060 name='Thread-18B9B070060' state=Running
  type=0x1E native=16300 host_rip=0x7FFB36780BB4 region=module:ntdll.dll+0x160BB4
  stack=[KERNELBASE.dll+0x229F3 <- coreclr.dll+0x16E4D3 <- ...]
  never made an HLE call - the target has not reached an HLE boundary since it was raised
```

The backend's own docstring already describes the consequence exactly: *"The suspender is meanwhile
blocked waiting for an acknowledgement that cannot come, which is a process-wide hang whose only
visible symptom is that nothing happens."*

### The exact cycle

A guest exception is only delivered **after an HLE export returns** - the single delivery site is
`DirectExecutionBackend.Imports.cs:581`, in the `finally` of the import dispatch. A thread stuck
*inside* an export therefore never gets one. And the stranded thread is stuck inside one:

1. Its last import is `Op8TBGY5KHg` = **`pthread_cond_wait`**.
2. `PthreadCondWaitCore` handles this correctly - it waits in 20 ms slices and breaks out when
   `CurrentThreadHasPendingGuestException()` is true (`KernelPthreadCompatExports.cs:1889-1908`).
   The comment there already names the collector.
3. But POSIX requires the mutex to be re-acquired before `pthread_cond_wait` returns, so it then
   calls `WaitForHostMutexLock` (`:1941`).
4. **`WaitForHostMutexLock` ends in a bare `hostSignal.Wait()`** - no timeout, no poll
   (`KernelPthreadCompatExports.cs:2126-2158`). That is the `KERNELBASE <- coreclr` frame in the
   stranded stack.
5. The mutex it wants is held by a thread that is itself waiting for the collector to finish. The
   collector cannot finish until our thread acknowledges. Our thread cannot acknowledge until it
   returns from the export. It cannot return until it gets the mutex.

Deadlock, and every subsequent allocation blocks behind the collector - which is why
`Main::Initialize` never completes, why the PSN subsystem class initializers never set
`Il2CppClass+0xE0`, and why `PS5Manager.CurrentState` stays at 1. **None of those is a PSN defect.**

### Supporting measurements

- Native PSN init genuinely succeeded: the probe reads slot 0 as `0x0500003300000001`, i.e.
  `{initialized=1, sdkVersion=0x05000033}`. The managed copy is zero only because the managed tail
  never ran.
- Only 9 HLE calls in a whole boot return an error, and all are `stat`/`open` probes for optional
  files. Nothing PSN-side fails.
- `SHARPEMU_IGNORE_GUEST_EXCEPTIONS=1` is **not** a fix: it reaches fewer threads (36 vs 45) and the
  PS5Manager probe never fires at all, because the collector then never suspends correctly either.
- The unbounded-wait pattern is not unique to this path. `KernelPthreadExtendedCompatExports.cs:1632`
  and `:1665` are bare `Monitor.Wait(rwlock.SyncRoot)` calls with neither timeout nor poll, and are
  the same defect waiting to happen on the rwlock paths.

### The fix, and why the obvious one is wrong

Returning early from `WaitForHostMutexLock` without the mutex would let the export return and the
exception deliver, but it breaks the `pthread_cond_wait` contract: the guest would then unlock a
mutex it does not own. That trades a hang for corruption.

The correct shape is to **deliver the exception in place, while still blocked**, which is what
FreeBSD does - the handler runs on the blocked thread and the wait then resumes. The machinery
already exists (`DeliverPendingGuestExceptionAtSafePoint`), it is simply only wired to the import
return path.
### RETRACTION: the stranded exception is not the gate

The section above concluded that the load hangs on a GC suspend acknowledgement that cannot arrive.
**That conclusion does not survive measurement and is withdrawn.** The reasoning was sound, the
mechanism is real, but it is not what holds this title.

What actually decided it - four boots, correlating the stranded-exception count against the gate:

| run | stranded exceptions | gate |
|---|---:|---|
| base | 2 | `CurrentState=1` |
| rip | 1 | `CurrentState=1` |
| mtx | **0** | `CurrentState=1` |
| extfix | 1 | `CurrentState=1` |

`CurrentState` never leaves 1 whether two exceptions are stranded or none are. The two quantities are
uncorrelated, so the stranding cannot be the cause.

The delivery accounting also reads better than first thought. `guest_exception.safe_point_enter` is
logged **after** the pending exception is removed and its context written, immediately before the
handler call (`DirectExecutionBackend.cs:5116-5125`) - it records a delivery that is proceeding, not
one merely attempted. So the real split is:

```
raise 27 = delivery_enter 6 (delivered inline) + safe_point_enter 21 (delivered at a safe point)
```

Every raise is accounted for. The occasional `still queued` warning is a transient sampled between a
raise and its delivery, not a permanent strand.

**What was wrong in my reasoning:** I saw one `Guest exception still queued` warning whose docstring
described a process-wide hang, and the description matched the symptom so exactly that I treated the
match as evidence. It was a hypothesis that happened to be well written. The correlation table above
is one grep and should have come first.

### What the two committed changes are, and are not

They are defensible on their own merits and are kept, but neither is a fix for this title:

1. `WaitForHostMutexLock` waited on a bare `hostSignal.Wait()` with no timeout
   (`KernelPthreadCompatExports.cs`). It now waits in 20 ms slices and re-tests the predicate, which
   is semantically identical but makes a stall observable, and warns once with the mutex owner when
   it stalls past 3 s with an exception pending. Measured: that warning never fired, so this path was
   not stalling.
2. `CurrentThreadHasPendingGuestException` only consulted `_currentGuestThreadHandle`, so it was
   blind to primary/external executors whose identity lives in `_currentExternalGuestThreadHandle` -
   the very fallback that delivery itself uses. Blocking waits polling through it could not be
   interrupted on those threads. Fixed via a default interface member so no fake scheduler breaks.
   Measured: delivery counts unchanged, so no thread was actually hitting the blind spot in this run.

### Where the gate actually is - still open

Everything established earlier still holds and is not affected by this retraction: native PSN init
succeeds (`{initialized=1, sdk=0x05000033}`), no HLE export fails except `stat`/`open` file probes,
and `Main::Initialize` has no path that returns a false result. The managed tail still does not
complete.

The next measurement should be the one this retraction skipped: **correlate, do not pattern-match.**
Specifically, `Loading.PreloadManager` shows `imports=8698702` blocked on `sceKernelWaitSema` - 8.7
million calls in 180 s, which is not an idle thread and was previously written off as one. That is a
concrete anomaly with a number attached, and it should be checked before any further theory.
## The load blocks on two semaphores that are signalled thousands of times

Measured with `probes/superliminal-load.json` (no rebuild - probe sites only).

**First, a correction to the lead in the previous section.** `Loading.PreloadManager` showing
`imports=8698702` is **not** a spin. `_nextImportDispatchIndex` is `[ThreadStatic]`, so it is that
thread's lifetime import count, and it reads *identically* in all 8 samples of a 180 s boot:

```
imports=8698702 block=sceKernelWaitSema     x8, unchanged
```

A constant counter is a thread that is genuinely parked, not one making 48k calls a second. The
"8.7M calls in 180 s" reading was wrong; the thread made those calls earlier in the boot and then
stopped.

### What is actually stuck

Every `sceKernelWaitSema` call in the run passes `timeoutPtr=0x0`, so **every wait is infinite**.
Counting entries against returns:

```
waits entered  2336
waits returned 2334      <- exactly two never come back
signals        3226
```

and per semaphore, the only two with an imbalance:

| semaphore | entered | returned | stuck | signalled |
|---|---:|---:|---:|---:|
| `0x1D` | 11 | 10 | **1** | 20 |
| `0x46` | 1495 | 1494 | **1** | 2373 |

Both are signalled heavily - 0x46 gets 2,373 signals - yet one waiter on each never wakes. **So this
is not a missing signal. It is a lost wakeup**, and the two stranded waiters are the two permanently
blocked loader threads.

### Gotcha: probe sites must be named by NID

`{"name": "export:sceKernelWaitSema"}` silently never fires. The same site as
`{"name": "export:Zxa0VhQVTsk"}` fires normally. An export-name site that matches nothing looks
exactly like an export that is never called, which cost a run and nearly produced the wrong
conclusion ("nothing waits on semaphores"). Prefer the NID form for anything load-bearing.

### Ground truth to use instead of more boots

Two sources beat re-running the title, and both are static:

1. **KytyPS5 boots Superliminal.** Its semaphore is `inspiration/KytyPS5/src/kernel/semaphore.cpp`,
   and its design differs from ours in exactly the way that matters here. Kyty uses an explicit
   **hand-off**: each `WaitingThread` carries `need_count`, `result` and a `ready` flag, and the
   signaller assigns the result and sets `ready = true` *under the mutex* before waking the condvar.
   A wakeup therefore cannot be lost - a waiter that was granted is already marked granted before
   anyone wakes.

   Ours is predicate-based: the waiter parks with a `WakePredicate` under a wake key, the signaller
   calls `WakeBlockedThreads(semaphore.WakeKey)`, and the waiter re-evaluates. The window between
   queueing and the scheduler registering the block is already known and handled by a re-check
   (`KernelSemaphoreCompatExports.cs:660-678`), so the remaining loss is elsewhere in that protocol.

2. **The decrypted firmware**, `games/PS5_4.03_reconstructed`, holds the real `libkernel` bodies for
   `sceKernelWaitSema` / `sceKernelSignalSema` and is authoritative for grant order, `need_count`
   semantics and FIFO vs priority wake order.

### Next step

Compare grant/wake protocols rather than boot again: for semaphore `0x46`, which has 1,495 waits and
2,373 signals, find the single grant that is decided but never delivered. Kyty's hand-off is the
reference for what "decided" should mean. Adopting a hand-off - the signaller marks the specific
waiter granted before waking, instead of waking and letting each waiter re-test - removes this whole
class of race rather than patching one window in it.
### RETRACTION 2: the semaphore imbalance is normal parking, not a lost wakeup

`SHARPEMU_LOG_SEMA=1` names the handles and settles it. The "two waits never return" figure is not a
defect - it is the ordinary steady state of parked worker threads, sampled at an arbitrary instant.

Every handle with an unresolved block has **exactly one**, and there are nine of them:

| handle | name | blocked | resumed | outstanding |
|---|---|---:|---:|---:|
| `0x22`, `0x23`, `0x27`, `0x2F`, `0x39` | `Baselib_SystemSemaphore` | 1 / 15 / 845 / 4 / 1 | 0 / 14 / 844 / 3 / 0 | 1 each |
| `0x46`, `0x48`, `0x4A`, `0x4E` | `FMOD Semaphore` | 79 / 20 / 1 / 7 | 78 / 19 / 0 / 6 | 1 each |

Nine different semaphores each with one waiter parked is nine idle workers waiting for work - Unity's
Baselib pool and FMOD's audio threads. `0x27` blocked 845 times and resumed 844: it is being used
constantly and simply happened to be parked when the process was killed. A pool that is *not* parked
between jobs would be the anomaly.

So `waits entered 2336 / returned 2334` counts two threads currently asleep, nothing more. There is
no evidence of a lost wakeup, and the previous section's conclusion is withdrawn. The grant path was
never suspect either: `GrantWaitersLocked` already implements the same hand-off Kyty uses - it
decrements the count, sets `Outcome = Acquired` and dequeues the waiter, all under the gate
(`KernelSemaphoreCompatExports.cs`), so a granted waiter is marked granted before anything wakes it.

**Second time in this investigation that an imbalance was read as a defect before checking what
normal looks like.** The rule that would have caught both: before treating a count as evidence,
establish the baseline the healthy case produces. One parked waiter per idle worker is the baseline
here.

### Where this leaves it

The semaphore layer looks healthy: 3,226 signals, 2,334 completed waits, correct hand-off, no
starvation. The parked loader threads are therefore a *symptom* - they are waiting for work that is
never produced - and the producer is somewhere upstream that has not been found yet.

Still true and unaffected: native PSN init succeeds, no HLE export fails except `stat`/`open` file
probes, exception delivery accounts for all 27 raises, and `CurrentState` never leaves 1.

The honest next step is to identify what should be *producing* work for `Loading.PreloadManager`
rather than to keep examining the thing it waits on. KytyPS5 boots this title, so the highest-value
move is a behavioural diff against it at the point the load stalls - not further inference from our
own traces, which have now produced two retracted conclusions in a row.
## The stall is one condition variable that is waited on and never signalled

`SHARPEMU_LOG_PTHREAD_CONDS=1` plus a new stall warning in the cond-wait loop give an unambiguous
answer, and this one has a healthy baseline to compare against - the mistake made twice above.

### The stalled thread

```
pthread_cond_wait stalled: thread=0x2522DC18500 cond=0x0000000100000BA0
  mutex=0x0000000100000B98 timed=False waited_ms=5000 signal_epoch=0
  pending_exception=False - still inside the condition wait
```

`timed=False` (infinite), `pending_exception=False` (nothing to do with the collector), and
critically **`signal_epoch=0`** - `SignalEpoch` increments on every signal and broadcast, so this
condvar has never been signalled even once. Exactly one thread stalls this way, and the mutex
re-acquire warning added earlier never fires, so the condition wait itself is the site.

This is the thread seen earlier as `Thread-18B9B070060`, `state=Running`, last import
`pthread_cond_wait`, with `imports=150` **unchanged across all 8 samples of a 180 s boot**. It did
almost nothing and then parked forever.

### The address census - and the orphan

Across a whole boot there are only three condvars, and they do not pair up:

| address | region | waits | signals |
|---|---|---:|---:|
| `0x00007FFFB17FFF18` | stack | 4173 | 4172 |
| `0x0000000100000BA0` | low guest mapping | **15** | **0** |
| `0x0000000100000BD0` | low guest mapping | 1 | 1 |
| `0x000000080411A6A8` | **eboot image** (`0x800000000 + 0x411A6A8`) | **0** | **4** |

The first row is the healthy baseline: 4,173 waits against 4,172 signals, which is what a working
condvar looks like. Against that, two rows are anomalous in opposite directions - one condvar is
waited on fifteen times and never signalled, and another is signalled four times and never waited on.

**A signal that reaches no waiter and a waiter that receives no signal, in the same run, is the
signature of an address that resolves to two different states.** `TryResolveCondState` normalises a
guest `pthread_cond_t` before keying it, so if the wait path and the signal path normalise
differently - one following a pointer stored in the object, the other keying the object itself - the
two sides register on separate states and neither ever sees the other.

That is a hypothesis, not a finding. What makes it worth testing first is that it is the only
explanation offered so far that accounts for **both** anomalies with one mechanism, and it is cheap
to falsify.

### The check that decides it

Trace `TryResolveCondState`'s input and output for both addresses. If `0x080411A6A8` and
`0x0000000100000BA0` resolve to different `PthreadCondState` instances, that is the bug and the fix
is to make both call paths normalise identically. If they resolve to genuinely different condvars,
the orphan signal is unrelated and the real question becomes which guest code should signal
`0x100000BA0` - answerable by finding the writer of the mutex/cond pair at `0x100000B98`/`0xBA0`.

Note the pairing is not numerically clean (15 waits versus 4 signals), so the aliasing story does not
fully account for the counts on its own. Establish the resolution mapping before building on it.
### Sharpened hypothesis: one logical condvar, two registry objects

Framing owed to the user, and it is a better one than "the two paths normalise differently": the
guest very likely has **one** condvar that our kernel layer tracks as **two**, because it exists at
two addresses - copied, or keyed by address after a relocation.

Reading `TryResolveCondState` (`KernelPthreadCompatExports.cs:1592`) makes the mechanism concrete.
It keys in three steps:

1. direct hit on `_condStates[condAddress]`;
2. otherwise read the qword **at** `condAddress` as a handle, and if `_condStates[pointedHandle]`
   exists, alias `_condStates[condAddress] = state` and return it;
3. otherwise, with `createIfZero`, allocate an opaque object and register it under **both**
   `condAddress` and the new handle.

Step 2 means a copy of an **already-initialized** `pthread_cond_t` aliases correctly - the copy
carries the same handle qword, so both addresses land on one state. That is the benign case.

**The failure is a copy made while the struct is still zero.** A `PTHREAD_COND_INITIALIZER` in
`.bss` has not been lazily created yet, so its handle qword is zero. If the guest copies or relocates
it before first use, each address independently reaches step 3 and mints its own handle and its own
`PthreadCondState`. One logical condvar, two registry objects, and a signal on one can never reach a
waiter on the other.

The addresses fit that story: `0x080411A6A8` is inside the eboot image (`0x800000000 + 0x411A6A8`),
i.e. static storage where a `PTHREAD_COND_INITIALIZER` would live, while `0x0000000100000BA0` is a
low runtime mapping. Signals land on the static address; the waiter sits on the runtime one.

There is a second, independent failure in the same function worth checking at the same time: when
`pointedHandle != 0` but is **not** in `_condStates`, step 2 returns **false** outright rather than
creating. A cond initialized through a path we did not observe would then fail to resolve at all.

**The check, still cheap and still decisive.** Trace `TryResolveCondState`'s `condAddress`,
`pointedHandle`, `resolvedAddress` and the resulting state identity for both `0x080411A6A8` and
`0x0000000100000BA0`. Two distinct `PthreadCondState` instances confirms it. The fix is then a
keying fix in the registry - make initialization publish one identity that both addresses resolve to -
not a missing-signal fix, and it would be a whole class of bug rather than this one condvar.

This also predicts the mismatched counts noted above (15 waits versus 4 signals): the two sides are
simply different objects with different traffic, so their totals were never expected to pair.
## RETRACTION 3: an independent audit killed the two-address hypothesis

An independent read-only review (`docs/superliminal-reasoning-audit.md`) was run specifically to
check whether this investigation was circling. It was, and the sharpened hypothesis above is
withdrawn.

**The decisive counter-argument, which I missed: Kyty does the same thing.** Two distinct
still-zero guest slots each get their own private condvar in Kyty as well - `CreateObject` checks the
qword at the supplied address, locks, re-checks, and initializes *that address*
(`inspiration/KytyPS5/src/kernel/pthread.cpp:1290-1331`, `:2736-2749`). Kyty boots Superliminal. So
"two zero addresses produce two states" cannot be the defect, because the oracle that works behaves
identically. If the title genuinely needed two zero slots to alias as one condvar, Kyty would need an
alias mechanism it does not have.

**And I repeated the counting mistake a third time.** Condvar waits and signals are not conserved
one-for-one: a signal with no waiter is discarded, a broadcast releases many, and this codebase
deliberately permits exception-induced spurious returns
(`KernelPthreadCompatExports.cs:1912-1928`, `:1984-2005`). So "15 waits, 0 signals" is not the
anomaly I presented it as. The healthy-baseline check I wrote into RETRACTION 2 should have been
applied to the census itself.

**Nothing ever connected `0x100000BA0` to `PS5Manager.CurrentState`.** A thread parked in an infinite
condvar wait proves that call is blocked. It does not prove the thread should have been signalled,
that it is on the load-critical chain, or that waking it would let `Main::Initialize` return. Calling
that finding "unambiguous" was wrong.

### What survived the audit, and is now fixed

**A real, VERIFIED resolver race - just not the claimed one.** Lazy creation for the *same* zero
address was not atomic. The deciding lookup happens before the allocation, so two threads reaching a
still-zero `PTHREAD_COND_INITIALIZER` concurrently could both miss, both allocate, and both assign,
the second overwriting the first. The losing caller then returned a state the registry no longer
held - so a signal and a wait on the **same address** could land on different objects and the wake
would be lost. Fixed by re-checking under `_stateGate` before publishing, which is exactly what
Kyty's creation lock does (`pthread.cpp:1295-1315`).

Whether that race actually fires in Superliminal is **UNVERIFIED** - the observed low address has
wait traffic only and the eboot address signal traffic only, neither of which is evidence of
concurrent first use of one raw address. It is a correctness fix on its own merits.

### What I discarded too fast

**RETRACTION 1 was right about the mechanism not being this boot's gate, but wrong to leave the
architecture alone.** Delivery only at HLE-export return remains defective: Kyty dispatches a pending
signal *inside* `pthread_cond_wait`, before reacquiring the application mutex
(`inspiration/KytyPS5/src/kernel/pthread.cpp:3043-3054`). Ours must finish the condition wait **and**
reacquire the mutex before the import-boundary delivery site runs at all
(`DirectExecutionBackend.Imports.cs:1378-1383`). That is a genuine divergence from the oracle on the
exact path a stalled thread sits in, and it should be fixed regardless of whether it gates this
title. RETRACTION 2, by contrast, was fully correct: our semaphore hand-off matches Kyty's.

### The experiment that would actually decide it

Not a state-identity trace - both implementations predict two states for two zero slots, so it proves
nothing. The audit recommends a **call-site and predicate differential against Kyty**: for each of the
four census addresses capture the caller as module+offset, guest thread, raw RDI, the qword before
and after resolution, the resolved state identity, the mutex address, the wait exit reason, the guest
predicate the wait loop tests, and `CurrentState` - then capture the module-relative equivalents in
Kyty. Raw addresses need not match across emulators; caller offsets and predicate flow do.

The outcome that would end this line entirely: **if Kyty also parks a waiter at the equivalent site
while `CurrentState` reaches 2, the waiter is normal idle state** and three sections of this document
were chasing a non-defect.
### In-place exception delivery: feasibility established, design ready

The one VERIFIED divergence from the oracle that survives all three retractions is that Kyty
dispatches a pending signal *inside* the condition wait, and we cannot. Kyty
(`inspiration/KytyPS5/src/kernel/pthread.cpp:3043-3054`):

```cpp
while (!ready()) {
    cond_value->cv.wait_for(cond_lock, microseconds(SIGNAL_APC_POLL_MICROS));
    if (!ready()) {
        cond_lock.unlock();
        KernelDispatchPendingSignalForCurrentThread();   // handler runs, still waiting
        cond_lock.lock();
    }
}
```

The wait is never abandoned and the application mutex is never reacquired first. Ours must break out
of the wait, reacquire the mutex, and return from the export before delivery can run at all
(`DirectExecutionBackend.Imports.cs:1378-1383`).

**This is implementable, and the blocker I assumed does not exist.** Delivery needs a
`GuestCpuContinuation`, whose three non-register fields are built at the import boundary from
`argPackPtr` - a host pointer the HLE layer does not see. But the same three values are already
handed to `EnterImportCallFrame(num7, (ulong)argPackPtr + 104, ActiveGuestReturnSlotAddress)`, so:

| continuation field | source at the import boundary | already tracked as |
|---|---|---|
| `Rip` | `returnRip` (`num7`) | `_currentImportReturnRip` |
| `Rsp` | `argPackPtr + 104` | `_currentImportResumeRsp` |
| `ReturnSlotAddress` | `argPackPtr + 96` | the third `EnterImportCallFrame` argument |

the remaining fields come from the live `CpuContext`. So a scheduler method
`TryDispatchPendingGuestExceptionInPlace(CpuContext)` can reconstruct the continuation from the
current import call frame and call `DeliverPendingGuestExceptionAtSafePoint` without any new plumbing
from the CPU backend.

**Lock discipline is the part to get right, and Kyty shows it:** the cond gate must be released
before the guest handler runs and retaken afterwards. Running guest code while holding
`state.SyncRoot` would deadlock against any signaller.

Not attempted here. It adds a new point where guest code runs with an HLE export on the stack, which
is the riskiest subsystem in the emulator, and this investigation has already demonstrated three
times over how easily a plausible change gets mistaken for a verified one. It wants a fresh context
and its own verification, and the feasibility work above means that session can implement rather than
re-derive.
### In-place delivery implemented — and it changes nothing here

Kyty's model is now implemented: on a pending exception the condition wait drops the gate,
dispatches the handler, retakes the gate and **keeps waiting**, instead of returning and forcing a
mutex reacquire before delivery can run. `TryDispatchPendingGuestExceptionInPlace` rebuilds the
continuation from the tracked import call frame, exactly as the feasibility note predicted, so no new
plumbing from the CPU backend was needed. There is a fallback to the previous spurious-return
behaviour when no deliverer is available.

**Measured against baseline: no change whatsoever.**

| | baseline | with in-place delivery |
|---|---|---|
| `CurrentState` | 1 | 1 |
| raises / inline / safe-point | 27 / 6 / 21 | 27 / 6 / 21 |
| `pthread_cond_wait stalled` | 1 | 1 |
| threads scheduled | 45 | 45 |

That is the expected result and it corroborates RETRACTION 1 rather than undermining it: the stalled
thread reports `pending_exception=False`, so the new path never fires for it. The fix removes a real
divergence from the oracle on the path a stalled thread sits in - a thread that *does* hold a signal
can now take it without first winning a mutex - but nothing in this boot was in that state.

Kept because it is correct and oracle-matched, not because it helps this title. Tests: same 3
pre-existing failures.

**The gate remains unidentified.** Three mechanisms have now been implicated and cleared by
measurement: GC stop-the-world exception stranding, semaphore lost wakeups, and condvar address
aliasing. What has *not* been done, and is what the audit actually recommended, is the call-site and
predicate differential against Kyty - establishing what the equivalent guest code does in an emulator
where the title boots. Every conclusion reached by inspecting our own traces alone has been retracted.
### CORRECTED: Kyty CAN be run — a build exists outside the repo

The audit recommends a behavioural differential against Kyty. **That experiment is not executable
here as written.** `inspiration/KytyPS5` contains a curated source tree with a README and no build
system - no `CMakeLists.txt`, no solution file, no prebuilt binary anywhere in the tree - and the
machine has no C++ toolchain at all: `cmake`, `ninja`, `g++`, `clang++` and `cl` are all absent.

So "Kyty boots Superliminal" is a fact about Kyty, not something reproducible here without first
installing a toolchain and obtaining a buildable checkout. Plan accordingly:

- **Source-oracle use works and has been productive.** It supplied the counter-argument that killed
  the aliasing hypothesis (`pthread.cpp:1290-1331`) and the reference for in-place signal dispatch
  (`pthread.cpp:3043-3054`), which is now implemented. Keep using it this way - read the algorithm,
  compare it with ours, fix divergences.
- **Behavioural differential needs setup first.** A toolchain plus a buildable Kyty checkout, which
  is a task in its own right and should be costed as one rather than assumed available.

Given that, the realistic next move is to keep mining Kyty's *source* for divergences on the load
path - the two found so far were both real - rather than to plan a runtime comparison that cannot be
run yet.
### Oracle sweep of the blocking primitives — where we agree and where we did not

Reading Kyty's source (the only way it can be used here) across the three primitives Superliminal
actually blocks on. The blocking census from a 180 s boot is 230 samples on `sceKernelWaitEventFlag`,
63 on `sceKernelWaitSema`, 15 on `sceKernelWaitEqueue`, so event flags dominate and were the last
unexamined subsystem.

| primitive | Kyty model | ours | verdict |
|---|---|---|---|
| semaphore | durable hand-off: signaller assigns `result`, sets `ready`, deducts tokens under the mutex (`semaphore.cpp:132-149,172-179`) | same - `GrantWaitersLocked` sets `Outcome = Acquired`, deducts and dequeues under the gate | **match** |
| condvar create | locked re-check of the slot before init (`pthread.cpp:1290-1331`) | did **not** re-check before publishing | **divergence, fixed** |
| condvar signal delivery | dispatches a pending signal inside the wait, before reacquiring the mutex (`pthread.cpp:3043-3054`) | required the wait to end and the mutex to be reacquired first | **divergence, fixed** |
| event flag mode decode | `&0xF` -> And 0x01 / Or 0x02; `&0xF0` -> None 0x00 / ClearAll 0x10 / ClearBits 0x20 | identical constants (`KernelEventFlagCompatExports.cs:66-69`) | **match** |
| event flag wake | `m_bits \|= bits; SignalAll()` - broadcast, every waiter re-tests its own predicate | predicate plus wake key via `WakeBlockedThreads` | different mechanism, equivalent provided every waiter is re-readied; **no divergence found** |

So of the five comparisons, two were real divergences and both are now fixed; three match. The
dominant blocking primitive is not a divergence source, which removes the largest remaining
suspicion from the kernel layer.

That is worth stating plainly: **the kernel synchronisation layer has now been compared against a
working emulator across every primitive this title blocks on, and no unexplained difference remains.**
Whatever gates the load is most likely not in semaphores, condvars or event flags. The next search
should move up a level - to what produces the work these threads are waiting for - rather than
continue auditing the waiting itself.
## The blind spot: the main thread was never observed

Every thread census in this investigation excluded the one thread that matters.

`[LOADER][THREADSTATE] --- 45 guest threads ---`, and exactly 45 `Scheduled guest thread` lines
appear in the same boot. The dumper enumerates `_guestThreads`, which is populated by
`TryStartThread`. **The primary/external executor never goes through `TryStartThread`**, which is
precisely why `TryRaiseGuestException` has a separate `mode=external` path for it
(`DirectExecutionBackend.cs:4728-4743`) and why delivery needs the
`_currentExternalGuestThreadHandle` fallback (`:5049-5053`).

So the main thread - the one running `Main::Initialize`, the one that would *produce* the work the
loader threads are parked waiting for - appears in no census, no block-reason tally, and no
`imports=` comparison made here. Every statement in the sections above of the form "N threads are
blocked on X" describes the worker pool only.

That reframes the whole investigation. The parked `Loading.PreloadManager`, the parked
`Loading.AsyncRead`, the nine idle Baselib/FMOD waiters - these are consumers with nothing to consume.
Chasing which primitive they sleep on was always downstream of the real question, and the answer to
"what produces their work" was never in the data being examined.

### What to do about it

1. **Make the primary executor visible first.** `StartGuestThreadStateDumper` should include the
   external handle, or a separate line should report it: its state, last import, import counter and
   host RIP. Until then, nothing about the main thread is measurable.
2. Only then ask what it is doing. A frozen import counter would mean it is stuck in an export
   (nameable directly); a climbing one would mean it is running guest code that is not making
   progress, which is a different search entirely.
3. `docs/superliminal-boot.md`'s six-address managed trace still stands as the follow-up once the
   main thread's coarse state is known, since those addresses are all on the main thread's path.

This is offered as the highest-value next step precisely because it is not another hypothesis: it is
a gap in instrumentation that made a whole class of explanation unreachable.
**CORRECTION to the section above.** The claim that Kyty cannot be run here was **wrong**. It was
based on looking only at `inspiration/KytyPS5`, which is indeed a source-only tree with no build
system - but a full Windows build exists outside the repo at:

```
C:\Users\sharpemu\Downloads\KytyPS5-2026-07-27-c71bb9f\KytyPS5\
    kyty_emulator.exe    18 MB
    launcher.exe         406 KB
    + Qt runtime DLLs
```

So the behavioural differential the audit recommended **is** executable. The error was searching one
directory and generalising to the machine - the same shape of mistake as the count misreadings
elsewhere in this document: I checked a narrower thing than the claim I then made.

Source-oracle use remains valid and productive (it produced the two condvar divergences), but it is
no longer the only option. A runtime differential against a build that boots this title is now the
highest-value experiment available, exactly as the audit said.
## The main thread is now observable, and it is the gate

With `DumpExternalGuestThreads` added, the primary executor appears for the first time. Sampling every
2 s from startup (`SHARPEMU_LOG_THREAD_STATE_MS=2000`):

```
guest_rip=0x0000700000001990      <- moving
guest_rip=0x00006FFFF9001010
guest_rip=0x0000700000000C30
guest_rip=0x0000700000000C00      <- and then this, x10 consecutive, forever
```

**The field is live** - that was checked before drawing any conclusion, because "unchanged across
samples" is worthless if the value is a registration-time snapshot. Early samples differ, so the
value tracks real execution. It then settles within roughly 8 seconds and never moves again, in three
separate boots.

`0x70000000xxxx` is the **import stub table** (`LLE redirect: 0x0000700000000010 zr094EQ39Ww -> ...`
lines map slots in it). So the main thread enters one HLE import about 8 s into boot and never
returns from it. Every parked loader thread and every idle Baselib/FMOD waiter examined in the
sections above is downstream of that single fact.

Stub `0x700000000C00` has no LLE redirect, so it is HLE-dispatched. A full import trace
(`SHARPEMU_LOG_ALL_IMPORTS=1`) ends with the main thread repeatedly calling:

```
scePthreadMutexLock (9UK1vLZQft4) rdi=0x0000000804117BB0
scePthreadMutexLock (9UK1vLZQft4) rdi=0x0000000804117BC0
```

alternating between those two mutex addresses, then silence. Those are in the eboot image
(`0x800000000 + 0x4117BB0`), i.e. static/global mutexes.

**Do not yet conclude the stall is `scePthreadMutexLock`.** The stub address has not been mapped to a
NID, and "last thing logged before silence" is not the same as "the call that never returned" - the
trace is written on entry. Mapping stub `0xC00` to its NID is the next concrete step and needs no
guesswork: the import table is built at load time and can simply be dumped.
## RESOLVED to a single wait: the complete causal chain

Three corrections and one new instrument closed this. In order.

### First: I never looked at the screen

**Superliminal renders its title screen perfectly.** Full scene, lighting, shadows, the logo, 32 FPS,
192 draws/s, with a loading spinner turning. Calling it "blocked at the load screen" throughout this
document was misleading - rendering is fine and always was, exactly as the older sections at L505 and
L630 recorded. I ran roughly fifteen boots this session without once capturing the window, having
written "screenshot before believing anything about the screen" into memory during the same session.

### The chain, end to end

```
main thread blocks in sceKernelWaitSema
        v
Unity.PSN.PS5.Main::Initialize never returns
        v
statics+0x1D0 stays 16 zero bytes (the store is UNCONDITIONAL, so zero == never returned)
        v
guest branch at 0x1434875 takes the abort: "PSN: Failed to initialize!"
        v
CurrentState stays Initializing(1); PS5Manager::Awake only starts the coroutine at state 0,
and Initialize has no other caller - so nothing ever retries
        v
SplashLoader/<LoadScene>d__18::MoveNext spins `while (CurrentState != Initialized)` at 0x141187
        v
title screen renders forever with the spinner turning
```

Every earlier section of this document was investigating a link downstream of the first line.

### The wait, named

The primary executor parks at import stub `0x700000000C00`, which
`SHARPEMU_LOG_IMPORT_STUB_MAP=1` resolves to **`libKernel:sceKernelWaitSema (Zxa0VhQVTsk)`**.

It takes the **host-thread** wait path, not the cooperative one, and those traces carry
`guest=0x0000000000000000` - the primary executor has no `_currentGuestThreadHandle`, which is the
same blindness fixed earlier in `CurrentThreadHasPendingGuestException`. That is why it never appeared
in any `sema.wait-block` line keyed by guest handle, and why the earlier semaphore census missed it
entirely.

```
sema.wait-host-block handle=0x0000002A name='Baselib_SystemSemaphore' need=1 count=0
  timeout=infinite guest=0x0000000000000000 native=16776 ret=0x0000000800A9F753
```

with `wait-host-block 1299` against `wait-host-wake 1298` - **exactly one outstanding**, on that
thread.

So the main thread is blocked on `Baselib_SystemSemaphore` handle `0x2A`, called from guest
`0x800A9F753`, and it is not a semaphore that never works: it blocked and woke 1,298 times before
this. The final acquisition is the one that never completes.

### Why this was so hard to find, and the instrument that fixed it

`SnapshotThreads` enumerates `_guestThreads`, populated by `TryStartThread`, which the primary
executor never calls. So the main thread appeared in **no** census, block tally or import comparison
made in this investigation - three retracted hypotheses all lived in that gap. `DumpExternalGuestThreads`
plus `SHARPEMU_LOG_IMPORT_STUB_MAP` now make it a two-command question.

### Next

Find the producer for `Baselib_SystemSemaphore 0x2A`. It is Unity's job-system semaphore, so the main
thread is waiting on a job whose worker never completes it. `ret=0x800A9F753` gives the exact guest
call site to disassemble. Kyty boots this title and a working build is available, so the differential
is finally worth running against a named, single wait rather than a whole subsystem.
## The gate is a two-semaphore cycle between the main thread and one job worker

Walking the dependency chain from the named wait gives a closed loop.

```
main thread   native=16776 guest=0x0 (primary executor)
    blocks on Baselib_SystemSemaphore 0x2A   at guest 0x800A9F753
    and is the thread that SIGNALS         0x27  at guest 0x8008622A7 / 0x800A844E1

job worker    native=11940 guest=0x2CAB0536300
    blocks on Baselib_SystemSemaphore 0x27   at guest 0x800E27376
    and is the thread that SIGNALS         0x2A  at guest 0x800A98645
```

Each is blocked on the semaphore the other one signals. Both blocked with `count=0`.

Until it stops, the pair runs a clean ping-pong - `signal 0x2A` from the worker, `wait-host-wake` on
the main thread, `wait-host-block` again, repeat, 754 signals on `0x2A` and 2,245 on `0x27`. It is a
working producer/consumer handshake that stops on one particular exchange.

### Not obviously a lost signal

Checked before concluding, because a dropped wake looks identical from outside: **114 signals in the
run land with `granted=0`**, i.e. no waiter present, and the count does bank - `count=1` is observed
72 times. So signals to an empty semaphore are retained rather than discarded, and the final state is
genuinely both parties blocked with `count=0`, not a token that went missing at this instant.

That does **not** clear us: an earlier mis-ordering could have produced a state where this cycle
becomes reachable, and Unity's job system does not deadlock on hardware. But it does mean the bug is
unlikely to be "our semaphore dropped a signal", which is where the previous three hypotheses would
have pointed.

### The exact call sites to disassemble

All in the eboot (`0x800000000` base), so `file offset = vaddr - 0x800000000`:

| guest address | who | what |
|---|---|---|
| `0x800A9F753` | main | waits `0x2A` |
| `0x800A98645` | worker | signals `0x2A` |
| `0x800E27376` | worker | waits `0x27` |
| `0x8008622A7`, `0x800A844E1` | main | signals `0x27` |

Disassembling those four sites gives the handshake protocol directly, and the question becomes narrow
and answerable: which side is supposed to signal before it waits, and why did it not this time.

Kyty boots this title and a build is available, so a differential now targets **one exchange between
two named threads** rather than a subsystem - which is the first time in this investigation that a
comparison would be cheap enough to be worth running.
### CORRECTION: not a cycle on 0x27 — and how the filter lied again

The "two-semaphore cycle" above is **wrong in one link** and is corrected here.

`sema.wait-resume` lines carry **no `guest=` field** (`KernelSemaphoreCompatExports.cs`, the
wait-resume trace prints handle/name/need/count/outcome only). So filtering the trace by the worker's
guest handle could show its **blocks but never its resumes** - the filter structurally excluded half
the pair. On that view the worker looked permanently blocked on `0x27`.

Counting properly - and note the first count was also wrong because `sema\.\w+` does not match
hyphenated names like `wait-block`:

```
0x2A   wait-host-block 755   signal 754   wait-host-wake 754      -> 1 outstanding
0x27   wait-block     2137   wait-resume 2137   signal 2245      -> 0 outstanding
```

**`0x27` has zero outstanding waits.** Nobody is parked on it. Across all semaphores there are nine
outstanding blocks, one each on `0x22 0x23 0x2A 0x39 0x46 0x48 0x4A 0x4E 0x53` - the same
one-per-idle-worker pattern documented in RETRACTION 2.

### What is actually true

- **The main thread is blocked on `0x2A` inside `Main::Initialize`.** That stands, and it is the gate:
  `statics+0x1D0` stays zero, so the guest takes its "PSN: Failed to initialize!" branch and
  `CurrentState` never leaves 1.
- **The thread that signals `0x2A` is `BatchDeleteObjects`** (guest `0x2CAB0536300`, native 11940),
  and it is itself `state=Blocked ... nid=Zxa0VhQVTsk ret=0x0000000800E27376 block=sceKernelWaitSema`
  with only 23 imports to its name - it did almost nothing before parking.
- **Which semaphore `BatchDeleteObjects` is parked on is not yet established.** It is one of the eight
  other outstanding handles, not `0x27`. Naming it is the next step and needs the wait trace joined on
  native thread id rather than guest handle.

So the shape is still "main waits for a producer that is itself waiting", but the second link is
unnamed. Do not restate it as a cycle until the handle is measured.

**Third filter error of this investigation**, all the same species: reading a count without checking
what the emitter includes. Sampled `flip=` lines, `sema\.\w+` missing hyphens, and now a join key that
exists on only one side of the pair.
### Reconciliation: the cycle was right, my correction over-corrected

Joining on **native thread id** - which appears on both sides, unlike `guest=` - settles it:

```
sema.wait-block handle=0x00000027 name='Baselib_SystemSemaphore' need=1 count=0
  timeout=infinite waiters=1 guest=0x000002CAB0536300 native=11940 ret=0x0000000800E27376
```

every one of `BatchDeleteObjects`'s last waits is on `0x27`, and the thread census reports it
`state=Blocked ... block=sceKernelWaitSema ret=0x0000000800E27376` - the same site.

**My "zero outstanding on 0x27" arithmetic was wrong.** It assumed every block ends in a
`wait-resume`, but the `wait-recheck` path (41 on this handle) resolves a wait *without* emitting
one - it fires when the predicate resolves between queueing and the scheduler registering the park
(`KernelSemaphoreCompatExports.cs:660-678`). So blocks and resumes are not expected to balance and
the difference proves nothing either way.

**A direct state snapshot outranks a derived count.** `THREADSTATE` reads the thread's actual state;
my figure was arithmetic over a trace whose emitter I had not fully read - the same root cause as the
other three filter errors, one level up. I corrected a correct finding, which is worse than the
original error.

### Standing conclusion

```
main thread (native 16776, guest=0x0)  blocked on 0x2A at guest 0x800A9F753
        ^                                                     |
        | signals 0x2A at 0x800A98645                         | signals 0x27 at 0x8008622A7 / 0x800A844E1
        |                                                     v
BatchDeleteObjects (native 11940)      blocked on 0x27 at guest 0x800E27376
```

Each is blocked on the semaphore the other signals. `BatchDeleteObjects` has only **23 imports** to
its name, so it parked almost immediately after starting and has done essentially nothing.

That last detail is the most suggestive thing here: a thread that blocks after 23 imports is one that
went straight to a wait it never leaves, which makes it the better end to attack. `0x800E27376` is
its wait site; disassembling it gives the predicate it expects, and `0x8008622A7` / `0x800A844E1` are
where the main thread would satisfy it.
### Kyty was actually run — and fail-fasts before reaching the guest

The binary works and takes `--game <dir>`:

```
kyty_emulator.exe --game "C:\sharpemu\games\superliminal" --printf-direction File --printf-output-file <path>
```

It gets through its own self-tests and Vulkan setup - `direct-memory backing self-test: shared aliases
ok`, `placeholder address-space self-test: ok`, `WindowCreate(): width = 1280, height = 720`, the
full extension enumeration - and then exits with:

```
PageManager fail-fast: fault access is incompatible with active page watchers
  frame[0]=0x14004111c image_rva=0x4111c
  frame[1]=0x140042e71 image_rva=0x42e71
  ...
```

That is Kyty's own memory-manager assertion, not a rejection of the dump. It never reaches guest
code, so **no behavioural differential was obtained.**

Worth knowing before planning around it: the oracle boots this title *somewhere*, but not on this
host in this configuration. Possible causes, none investigated: the V620 MxGPU virtualized memory,
another process holding page watchers, or a Kyty regression at `c71bb9f` - the build is dated the
same day. Earlier tags exist (`31ea008`, `7351eae`, `7921269`) and trying one is cheap.

So the differential remains the right next experiment and is now one step closer - the binary runs,
the invocation is known, and only Kyty's page manager is in the way.
### The semaphore layer is exonerated with exact arithmetic

Before blaming our primitives again, the books were balanced properly. On `0x27`, which has exactly
**one** waiter (native 11940, `BatchDeleteObjects`) and **one** signaller (native 16776, main) - so no
wrong-waiter theft is possible:

```
signals   2178 granted=1 count=0     (handed straight to a parked waiter)
          + 67 granted=0 count=1     (no waiter; banked, count incremented)
          = 2245 total

consumed  2137 wait-block satisfied by a grant
          +  41 wait-recheck         (granted between queueing and parking)
          +  67 wait                 (immediate, consumed a banked token)
          = 2245 total
```

**Exactly balanced. No signal was lost and no token was discarded.** Banking works: a signal to an
empty semaphore raises `count`, and a later waiter consumes it.

On `0x2A`: 755 blocks, 754 signals, 754 wakes. Main is parked awaiting the **755th** signal, and the
worker has produced 754.

An earlier draft of this section claimed 108 signals were unaccounted for. That was wrong - it
compared signals against `wait-block` only, ignoring the immediate-wait and recheck paths. Same
species as every other count error here.

### What that leaves

The stall is **not** a kernel-primitive defect. Ordering is FIFO as `attr=0x1` requires and matches
Kyty; grants are a durable hand-off; nothing is dropped. Both threads are parked in a state the
accounting says is internally consistent.

So the divergence is above the primitives: the main thread reaches a point where it waits for a
completion on `0x2A` without having requested it on `0x27`, and the worker is idle awaiting exactly
that request. One of them took a branch it would not take on hardware.

That is guest-level, and it needs the four call sites disassembled rather than more kernel tracing:

| guest address | who | what |
|---|---|---|
| `0x800A9F753` | main | waits `0x2A` |
| `0x800A98645` | worker | signals `0x2A` |
| `0x800E27376` | worker | waits `0x27` |
| `0x8008622A7`, `0x800A844E1` | main | signals `0x27` |

Every kernel-side avenue examined in this document is now closed by measurement. The remaining
question is what the guest code at those four addresses expects.
## What I am actually stuck on: a thread model Kyty does not have

Measured, not argued. Searching Kyty's kernel for the concepts our blocking paths are built on:

| concept | occurrences in `inspiration/KytyPS5/src/kernel` |
|---|---:|
| `RequestCurrentThreadBlock` | **0** |
| `WakeBlockedThreads` | **0** |
| external / primary executor | **0** |

Kyty has no cooperative scheduler and no external-executor concept. Every guest thread is a real host
thread that blocks on real host primitives. One path, no asymmetry.

We have three: a **cooperative** path where a guest thread parks in our scheduler and is woken by a
wake key, a **host** fallback that blocks for real, and a **primary/external executor** for the main
thread which never registers in `_guestThreads` and carries `guest=0`.

That is exactly the fault line the Superliminal stall sits on:

- the main thread blocks on `0x2A` through the **host** path (`sema.wait-host-block`, `guest=0x0`);
- `BatchDeleteObjects` blocks on `0x27` through the **cooperative** path (`sema.wait-block`,
  `guest=0x2CAB0536300`);
- and the two are waiting on each other.

Every kernel primitive checked in this document matches Kyty exactly - semaphore hand-off, FIFO
ordering under `attr=0x1`, event-flag mode decode, condvar creation after the fix. The accounting
balances to the unit. What does **not** match is the machinery around them, and the two deadlocked
threads are on opposite sides of it.

So the productive move is not another hypothesis about which semaphore. It is to stop maintaining a
scheduling model the reference implementation does not need. Kyty runs more titles with strictly less
machinery here, which is the strongest argument available that the machinery is the problem.

**The structural difference also explains why this took so long to see.** The primary executor's
absence from `_guestThreads` kept the main thread out of every census, block tally and import
comparison in this document - three retracted hypotheses were all searching a set that structurally
could not contain the answer.
### Tested: routing every semaphore wait through the host path makes it far worse

`SHARPEMU_SEMA_HOST_WAIT_ONLY=1` skips `RequestCurrentThreadBlock` so every waiter blocks for real,
which is what Kyty does. Measured against the normal run:

| | default | host-wait-only |
|---|---|---|
| screen | title screen, logo, lighting, spinner | **black** |
| FPS / FLIP | 32.0 / 32.0 | **0.0 / 0.2** |
| draws | 192/s | **0/s** |
| guest TIME | 00:01:17 | **00:00:00** |
| `ps5manager` probe | fires ~300x | **never fires** |

So the title does not even reach the title screen it otherwise renders. **The hypothesis that we can
simply adopt Kyty's single-path model is refuted as a drop-in change.** Our cooperative scheduler is
load-bearing, not incidental complexity - plausibly because blocking the primary executor for real
stalls the whole emulator, or because the host thread pool cannot absorb every guest waiter at once.
Neither was investigated.

The flag is kept, defaulting off, because it turns "would Kyty's model work here" into one run.

**What survives:** the structural divergence is real and is still where the deadlock sits - main
host-blocked, worker scheduler-parked, each waiting on the other. What is now known is that the fix
cannot be "delete the cooperative path". It has to be either making the primary executor participate
in the same scheduler as everything else, or making the two paths wake each other correctly. Those
are different changes and this measurement does not choose between them.
### Cross-path waking is correct too — the last kernel hypothesis closes

If a signal granted a waiter on one path but only notified the other, that would produce exactly this
deadlock. It does not. Both signal paths notify both waiter kinds:

- `sceKernelSignalSema` (`KernelSemaphoreCompatExports.cs`, the handle family Baselib uses for `0x2A`
  and `0x27`): `GrantWaitersLocked` under the gate, then **`Monitor.PulseAll(semaphore.Gate)`** -
  commented in situ as "Wake host-thread waiters parked in the fallback path" - then
  **`WakeBlockedThreads(semaphore.WakeKey)`** outside the gate for the cooperative ones.
- POSIX `sem_post`: the same three steps, plus a legacy umtx wake for guests parked in the firmware's
  own path.

So a granted host waiter is pulsed and a granted cooperative waiter is readied, from either
signaller, regardless of which path it is on itself.

**That closes the kernel side completely.** Every layer has now been checked against Kyty or against
the code and found correct: hand-off grants, FIFO ordering under `attr=0x1`, event-flag mode decode,
condvar creation (after the race fix), token banking, and now cross-path wake. The arithmetic
balances to the unit. Routing everything through one path makes things strictly worse.

So the deadlock is not a lost wake, a lost token, a wrong wake order, or a path that cannot see the
other. **Neither thread reaches its signal call at all**, which makes this a guest-level control-flow
question and not a kernel one.

The four addresses remain the whole remaining question:

| guest address | who | what |
|---|---|---|
| `0x800A9F753` | main | waits `0x2A` |
| `0x800A98645` | `BatchDeleteObjects` | signals `0x2A` |
| `0x800E27376` | `BatchDeleteObjects` | waits `0x27` |
| `0x8008622A7`, `0x800A844E1` | main | signals `0x27` |

`BatchDeleteObjects` reached its wait after only **23 imports**, so it is the cheaper side to read:
whatever it expects to have happened before `0x800E27376`, almost nothing had.
### Disassembly started: address resolution works, stream sync does not yet

`scripts/eboot_disasm.py` resolves a guest address to a file offset through the eboot's PT_LOAD
table and disassembles with Capstone. Verified working:

- the loader confirms the eboot's **runtime base really is `0x800000000`** (`ImageBase runtime`), with
  `libkernel` at `0x804000000` and two further modules at host-range addresses;
- segment 0 maps `vaddr=0` at file `0x4000`, so `file = vaddr + 0x4000`, matching the convention
  recorded for the decrypted `.sprx` files;
- the bytes at each of the five call sites are real code, not padding.

**What does not work yet: none of the five recorded `ret=` addresses has an instruction ending on
it.** For `main waits 0x2A` the preceding bytes end `e9 c3 67 00 00`, a 5-byte `jmp rel32`; for
`BatchDeleteObjects waits 0x27` the address falls inside a 7-byte `mov rdi, [rip+...]`. A return
address cannot be mid-instruction in valid code, so the decode is desynchronised - x86 is
variable-length and a fixed backward window is not a reliable way to find the boundary.

The fix is to sync forward from a known function start rather than backward from the target: scan
back for a prologue (`push rbp; mov rbp, rsp` or the `sub rsp, imm` form), disassemble forward from
there, and take the instruction that ends at the target. Worth also confirming what
`FormatCallSite`'s `ret=` field actually holds - if it is the import frame's return RIP it should sit
after a `call`, and the `jmp` seen here suggests these imports are reached through a tail-call thunk,
in which case the recorded address belongs one frame further out than assumed.

That is the next concrete step, and it is bounded: one function, no emulator runs.
### CORRECTION: the four "call sites" are not call sites

The `ret=` field I have been treating as a guest call site throughout this document is
`FormatCallSite` reading **`[rsp]` at HLE entry** (`KernelSemaphoreCompatExports.cs`). After the
import trampoline that is not a guest return address, so those values are not code pointers and the
four-address table repeated above is **wrong**.

Disassembling them proves it:

| address | claimed | actually decodes as |
|---|---|---|
| `0x800E27376` | BDO waits `0x27` | **undecodable** - not a code boundary at all |
| `0x800A9F753` | main waits `0x2A` | `add byte ptr [rax], al` (`00 00`) - padding/misalignment |
| `0x800A98645` | BDO signals `0x2A` | a clean function **prologue** - an entry, not a return site |

`scripts/eboot_disasm.py` is sound - it resolves segments correctly and refuses to invent a decode,
which is how this surfaced. The addresses were the problem, not the tool.

**The right instrument already exists and the docstring names it.** `SHARPEMU_LOG_SEMA_DEREF` prints
chosen guest words alongside each wait:

```
SHARPEMU_LOG_SEMA_DEREF=rbx+0x2c8,rbx+0xb8
```

described in situ as what "turns 'blocked on a semaphore' into 'waiting for N of M jobs'". That is
exactly the question here - what the main thread believes it is waiting for - and it answers it from
guest memory rather than from a stack slot that means nothing at this point.

So the next step is that flag with offsets chosen from Unity's Baselib job structures, not
disassembly of addresses that were never code.

**Fifth correction of this investigation, same root cause every time:** using a field without reading
what produces it. Sampled `flip=` lines, `sema\.\w+` missing hyphens, a join key present on one side
only, block/resume counts ignoring the recheck path, and now a stack slot read as a return address.
### First guest-state read at the main thread's wait

`SHARPEMU_LOG_SEMA_DEREF=rdi,rsi,rbx,r12,r13,r14,r15` on the `0x2A` host-block:

```
rdi=0x2A          semaphore handle (arg0)
rsi=0x1           need count (arg1)
rbx=0x000000060229BC50  -> [rbx] = 0x0000000801971730   (into eboot data: a vtable, so rbx is an object)
r13=0x0000000602215888  -> 0
r14=0x4DB, then 0x4DC on the very next block
r15=0x5
```

Two things follow, and only two - the rest would be guessing.

**`r14` increments between consecutive blocks** (1243 -> 1244). At the moments sampled, the main
thread was completing an iteration and coming back for the next, i.e. **progressing**, not wedged.
Whether it still increments at the *final* block is not established by this sample and is the first
thing to check - if it does, "deadlock" is the wrong word for what happens at the end and the search
should move to why the loop terminates rather than why a wake is missing.

**`rbx` is a live object with a vtable** at `0x801971730`. That is the handle to follow: the job
counters this loop is waiting on will be fields inside it, and `SHARPEMU_LOG_SEMA_DEREF` takes
`rbx+<offset>` directly, so walking it costs one run per batch of offsets and no rebuild.

Concretely: dump `rbx+0x00` through `rbx+0x40` in one run, look for a pair that reads as
"completed / requested", then watch that pair across the final block. That is the measurement the
`ret=` addresses were never going to give.
## RETRACTION 4: there is no deadlock — it is a poll loop that never exits

`r14` sampled across the last five host-blocks on `0x2A`:

```
0x4DD  0x4DE  0x4DF  0x4E0  0x4E1
```

It increments through the final block. **The main thread is not blocked - it is looping.** Wait on
`0x2A`, get woken, complete an iteration, wait again, ~1,249 times and still counting after 75 s
(roughly 17 iterations a second).

So the "two-semaphore deadlock between main and `BatchDeleteObjects`" described above is **wrong**.
The pair is a working producer/consumer handshake running exactly as designed. The single
outstanding block seen in every census is just "currently waiting for the next tick" sampled at an
arbitrary instant - the same normal-parking pattern that RETRACTION 2 already identified for the
idle worker pools, which I then failed to apply to this pair.

### What it actually is

`Unity.PSN.PS5.Main::Initialize` never returns because it is **polling for a condition that never
becomes true**, not because a wake was lost. Every kernel-side finding in this document is consistent
with that and none of it was ever the problem: the semaphores work, the hand-off works, the wake
reaches both paths, and the arithmetic balances - because nothing was ever broken there.

That also explains cleanly why `statics+0x1D0` stays sixteen zero bytes: the store is unconditional
and simply never executes, because the function never reaches its return.

### Where to look now

The question is no longer "who fails to signal" but **"what is this loop waiting for, and why does it
never arrive"**. That is a guest-state question and the instrument is already in hand:

- `rbx = 0x60229BC50` is a live object (first word `0x801971730`, an eboot vtable). Its fields are the
  loop's condition.
- `SHARPEMU_LOG_SEMA_DEREF=rbx+0x00,...` reads them per iteration with no rebuild.
- Because the loop iterates ~17 times a second, a field that is *supposed* to advance and does not
  will stand out immediately against `r14`, which does.

Compare a field that changes with one that does not, across iterations. That is a much easier
question than any asked earlier in this document, and it is the first one here that is actually
well-posed.
### The rbx object is the wrapper, not the condition

Fields across four consecutive iterations (`r14` = `0x4DE`, `0x4DF`, `0x4E0`, `0x4E1`):

```
rbx+0x08  0  0  0  0
rbx+0x10  1  1  1  1
rbx+0x18  0  0  0  0
rbx+0x20  0  0  0  0
rbx+0x28  0  1  0  0     <- only field that moves; reads as a transient "work pending" flag
```

So `rbx` holds semaphore/handshake state, not the loop's exit condition - nothing here is a
"completed of requested" pair and nothing is stuck at a value the loop would be waiting to change.

The remaining candidate from the same register dump is **`r13 = 0x602215888`**, a second object in the
same guest heap range, which was read as `0` at `[r13]` but whose other fields were never sampled.
`r12 = 0x7FFFF07FB7D8` is stack. Widening the deref set to `r13+0x00..0x40` is the same one-run,
no-rebuild step that produced this table.

Worth stating plainly for whoever picks this up: the loop condition may not be reachable from the
registers live at the *semaphore* call at all - the wait is one step inside the loop, not the test.
If `r13` also comes back static, the better move is to find the loop head rather than sample more
registers, and `scripts/eboot_disasm.py` can do that from a real code address once one is in hand
(the `ret=` values are not - see the correction above).
### A frozen object beside an advancing loop counter

`r13 = 0x602215888` dumped across four consecutive iterations, while `r14` climbs
`0x4DE -> 0x4E1`:

```
r13+0x00  0x0000000000000000
r13+0x08  0x0000000300000000     -> dwords (0, 3)
r13+0x10  0x0000000F00000003     -> dwords (3, 15)
r13+0x18  0x0000000000000000
r13+0x20  0x0000001000000049     -> dwords (73, 16)
r13+0x28  0x0000000000000000
r13+0x30  0x00000001016D8F50     -> a pointer
```

**Every field is identical in all four samples.** Nothing in this object moves while the loop runs.

That is the shape worth chasing: a stationary structure beside a counter that advances ~17 times a
second is what "waiting for something that never completes" looks like from outside. The dword pairs
`(3, 15)` at `+0x10` and `(73, 16)` at `+0x20` have the form of progress counts, and `3 of 15` frozen
is exactly the reading that would explain the stall.

**Stated as a lead, not a finding.** Two dwords that look like a ratio are not a ratio until
something confirms it - this document has four retractions in it from exactly that kind of inference,
and the pointer at `+0x30` is the honest way to check: follow `0x1016D8F50` and see what the
structure actually is before naming its fields.

The next run is the same one-line change that produced this table:
`SHARPEMU_LOG_SEMA_DEREF=r14,r13+0x30` plus derefs through that pointer.
### The frozen structure, wider window

```
r13+0x10  0x0000000F00000003   (3, 15)
r13+0x20  0x0000001000000049   (73, 16)
r13+0x38  0xFFFFFFFFFFFFFFFF   sentinel / invalid marker
r13+0x40  0x0000000100000000   (0, 1)
r13+0x48  0
r13+0x50  0
r13+0x58  0x0000000801A940C0   -> eboot, module_rel 0x1A940C0
```

Identical in every sample, as before. `+0x58` resolves into the eboot **BSS gap** - past segment 3's
file extent (`0x19AD2C4`) and before segment 4's vaddr (`0x1ADD1F0`) - so it is runtime-initialised
static storage, which is what a type or vtable pointer looks like. Combined with the `0xFFFF...FFFF`
sentinel at `+0x38`, this reads as a managed/engine object rather than a raw counter block.

**Stopping point, deliberately.** The next inference from here would be naming those fields, and that
is exactly the move that produced four retractions in this document. The structure's identity should
come from its type pointer (`0x801A940C0`) resolved against the IL2CPP metadata or the eboot's RTTI,
not from the shape of its numbers.

Everything needed to continue is in place and costs no rebuild:

- `SHARPEMU_LOG_SEMA_DEREF` reads any `reg+offset` per iteration;
- `DumpExternalGuestThreads` keeps the main thread visible;
- `SHARPEMU_LOG_IMPORT_STUB_MAP` names any stub address;
- `scripts/eboot_disasm.py` disassembles from a real code address.

And the framing is finally right: **no deadlock, no kernel defect** - a poll loop, ~17 iterations a
second, waiting on a structure that never changes.
## Forcing the gate reaches the menu transition — then crashes in shader translation

`SHARPEMU_PS5MANAGER_PROBE=force` writes `CurrentState = 2`, which is exactly the value
`SplashLoader` spins on. It is a **diagnostic override, not a fix** - it does not make PSN init
succeed, it just stops the wait. But it settles what is downstream of the gate, which was unknown:

| | normal boot | gate forced |
|---|---|---|
| `CurrentState` | 1 | **2** |
| draws | 192/s, 6/frame | **571/s, 390/frame** |
| guest memory | 149 MB | **928 MB** |
| screen | static title screen | **fading out, UI widgets laying out bottom-left** |

So the title genuinely proceeds: it dissolves the splash, loads menu assets, and begins laying out
menu widgets. **Everything downstream of the PSN gate works far enough to start building the menu.**

It then dies:

```
[COMPAT][SHADER] ps=0x603F25A00 es=0x603F00500 error=invalid-load-address pc=0x1A4
                 op=SBufferLoadDwordx16 words=[F430062E,FA000000] base=s92[0x00000001:0x00000000]
[COMPAT][SHADER] ps=0x603F27700 es=0x603F00500 error=invalid-load-address pc=0xB8
                 op=SBufferLoadDwordx16 words=[F4300824,FA000000] base=s72[0x00000001:0x00000000]
Fatal error.
[DEBUG] PROCESS EXIT code=-2146233082          (0x80131506, ExecutionEngineException)
```

Two separate problems, and they are worth keeping apart:

1. **The PSN gate** - the poll loop documented above. Still the real blocker for an unforced boot.
2. **A menu-scene crash** - `SBufferLoadDwordx16` with `base=sNN[0x00000001:0x00000000]`, i.e. a V#
   whose low dword is 1 and high dword 0. That is not a plausible buffer address, so the scalar
   evaluator is handing the load a descriptor it never resolved. This is only reachable *past* the
   gate, which is why no amount of work on the loading stall would ever have surfaced it.

That second one is new information and it is a real defect independent of PSN. `0x80131506` is a
.NET `ExecutionEngineException`, so the process is not failing the shader gracefully - it is taking
the runtime down, which is its own bug regardless of the bad descriptor.
### CORRECTION: the menu crash is ours, not a shader problem

I attributed the crash to the `SBufferLoadDwordx16` descriptor errors that precede it. **Wrong** -
those are `[COMPAT][SHADER]` lines, i.e. the shader was already refused *gracefully*. The actual
fatal error is in our own CPU backend:

```
Fatal error.
Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.
  at SharpEmu.Core.Cpu.Native.DirectExecutionBackend.ExecuteGuestContinuationEntry(
        SharpEmu.HLE.CpuContext, UInt64, UInt64, System.String, System.String ByRef, Int32 ByRef)
  at SharpEmu.Core.Cpu.Native.DirectExecutionBackend.RunGuestThread(GuestThreadState, System.String)
  at SharpEmu.Core.Cpu.Native.DirectExecutionBackend+GuestExecutionRunner.ThreadMain(UInt64)
```

`0x80131506` is the CLR reporting an invalid program, not a guest fault: a method marked
`[UnmanagedCallersOnly]` is being invoked directly from managed code, which .NET forbids outright.
The repo has hit this class before - `cdee775` is literally "Fix UnmanagedCallersOnly boot crash".

So the menu-scene blocker is a **managed-side defect in `ExecuteGuestContinuationEntry`**, entirely
independent of PSN and of shader translation. It is reachable only past the gate, which is why it has
never appeared in this investigation before.

That makes the current picture:

1. **PSN poll loop** - the unforced-boot blocker, still open.
2. **`ExecuteGuestContinuationEntry` calling an `UnmanagedCallersOnly` method** - what stops the menu
   once the gate is forced. Concrete, ours, and with a stack trace.

The second is the one standing between here and a visible menu, and it is a far better-defined
problem than anything else in this document: a named method, a known .NET rule, and prior art in the
history for the same mistake.
## THE MENU RENDERS — two flags, both already in the repo

```
SHARPEMU_PS5MANAGER_PROBE=force        writes CurrentState = 2, the value SplashLoader spins on
SHARPEMU_GUEST_THREADS_NATIVE_WORKER=1 runs every guest thread on a native worker
SHARPEMU_MAIN_ENTRY_NATIVE_WORKER=1    ...including the main entry, which had stayed behind
```

With all three, Superliminal renders its **main menu**: `NEW GAME` and `OPTIONS` are on screen, the
splash has dissolved, and the process is alive past 110 s with no fatal error. `DRAWS 637/s, 390/frame`,
`MEM 1803 MB`.

### Why the third flag was the one that mattered

The fail-fast is documented in `DirectExecutionBackend.cs:974-992`: *"A guest stub entered from a
CLR-created thread sits above managed frames, so when the guest re-enters managed code the runtime
fail-fasts with 'attempted to call a UnmanagedCallersOnly method from managed code' and takes the
process with it."* Only `tbb_thead` had been migrated to native workers; the comment says the rest
was deferred because migrating everything once increased splash hangs, and names this exact case as
the reason to revisit it - *"Superliminal reaches that fail-fast on its real critical path once
PS5Manager stops gating the scene load."*

Measured here:

| flags | result |
|---|---|
| none | title screen, spinner forever, `CurrentState=1` |
| `PROBE=force` | `CurrentState=2`, menu assets load, **fatal** in `ExecuteGuestContinuationEntry` |
| `+ GUEST_THREADS_NATIVE_WORKER` | still fatal, but the stack frames are gone - path moved |
| `+ MAIN_ENTRY_NATIVE_WORKER` | **menu renders, no crash** |

Guest threads alone was not enough because the **main entry** is where the guest re-enters managed
code on this title's critical path. Both migrations are needed together.

### What this is and is not

- **Not a fix for PSN.** `PROBE=force` is a diagnostic override; the poll loop documented above is
  still the real blocker for an unforced boot. `PsnInitialized` remains 0 and the PSN subsystem
  singletons are still NULL.
- **The native-worker flags are arguably real fixes**, and now have the per-title measurement their
  docstring asked for. They stay opt-in until the splash-hang regression they were deferred over is
  re-measured on other titles - the comment is explicit that it was never root-caused.
- Performance is poor (1.6 FPS) and the menu is mid-transition, so this is "renders" rather than
  "playable".

### Next

1. Re-measure the deferred splash-hang regression with both flags on, across titles. If it does not
   reproduce, they should become the default and this class of crash disappears.
2. The PSN poll loop still needs solving for an unforced boot.
3. 1.6 FPS at the menu wants its own look; `ALLOC 1321 MB/s` is the obvious suspect.
### The menu renders badly, and the log says why

Warnings during the run that produced the menu screenshot:

| count | warning | consequence |
|---:|---|---|
| 66 | `[SPIRV] ngg-prim-export-dropped` - "NGG primitive connectivity cannot be expressed in the vertex stage; the draw's index buffer is used instead" | **wrong geometry** |
| 23 | `gpu.unmapped_surface_htile` - no HTILE decoder, depth consumed as raw | wrong occlusion/depth |
| 15 | `agc.scalar_pointer_fallback reason=read-failed op=SBufferLoadDwordxN` | wrong shader constants |
| 4 | wave64 program in a workgroup smaller than 64 threads | corrupt lane-derived output |
| 28 each | `sceKernelDlsym failed: UnityRenderingExtEvent`, `UnityRenderEvent`, `UnityPluginLoad`, ... | Unity native render plugin never binds |

The visible artefact - a diagonal edge across the screen with banding on one side - is what a
mis-built primitive looks like, and NGG is the largest count by a wide margin.

**These are exactly the two gaps this host was provisioned to close.** `docs/host-azure-v620.md`
says so directly: *"NGG primitive shaders and HTILE depth compression are RDNA2 features. On a Radeon
PRO V620 they have native equivalents, so this host exists to stop guessing at them"* - and names the
same two warnings firing on Superliminal's own draws. That note predates the menu ever rendering, so
it was written from the title screen alone; now there is a menu's worth of geometry to test against.

The `sceKernelDlsym` failures are a separate thread worth pulling: Unity looks up its native render
plugin entry points and gets nothing, 28 times each. Whatever that plugin does for this title is
simply absent.

Ranked by likely visual impact: **NGG primitive export first** (66 hits, and geometry errors dominate
what is on screen), then HTILE, then the scalar-load fallbacks.
### The menu is stable; the artefact was a wipe, the real gap is a missing background

A later capture of the same process, guest clock `00:04:36` (first capture was `00:01:37`), so ~4.5
minutes of continuous running with no crash:

- the diagonal banded edge is **gone**. It was the splash **wipe transition** caught mid-frame, not a
  mis-built primitive. My earlier reading of it as broken geometry was wrong.
- `NEW GAME` and `OPTIONS` render cleanly and stay put.
- `DRAWS 632/s, 393/frame`, `MEM 1326 MB`, `FPS 1.6` - steady, not degrading.

**What is actually wrong: the menu background never draws.** Behind the two items is flat white where
the scene should be. That is a real gap and it is not the wipe.

So the correct state is: **menu reached and stable, background missing, 1.6 FPS.** Not "corrupted
mid-transition", which is how the previous entry described it.

The NGG/HTILE/scalar-fallback counts in the section above are still the leading suspects for the
missing background - a draw whose geometry is dropped renders nothing, which is exactly what a blank
background looks like - but that is now a hypothesis about a *specific* missing element rather than
an explanation of an artefact that turned out to be an animation.

Correct order of work from here:

1. Find which draw should paint the menu background and why it produces nothing. `SHARPEMU_LOG_AGC=1`
   plus the render-target census used on Astro applies directly.
2. NGG primitive export (66 hits) as the leading cause once that draw is identified.
3. 1.6 FPS, with `ALLOC 1253 MB/s` the obvious suspect.
### Correction: NGG is probably not why the background is missing

I ranked `ngg-prim-export-dropped` first for the blank menu background on the strength of its 66 hits.
Reading what the diagnostic actually means says otherwise
(`Gen5SpirvTranslator.cs`, `ReportNggPrimitiveExportDropped`):

> A Vulkan vertex shader has nowhere to put it: connectivity comes from the draw's topology and index
> buffer. That is **CORRECT** when the invocation contributes at most one primitive, which is what
> every NGG program in PS5 firmware 4.03 does - a disassembly census of all 120 decodable ones found
> exactly one target-20 export each, always `en=0x1` and `done=1` ... It is WRONG when an invocation
> can export a primitive more than once, **which the AGC layer checks per draw with exactly that
> test**.

So the message is an announcement of a *correct* transformation for pass-through primitive shaders,
deliberately printed once per shader so it can be found in a disassembly - not a defect report. All
66 hits carry `en=0x1 done=1`, which is precisely the shape the census says is safe, and the unsafe
case is already gated elsewhere.

**Count is not severity.** I ranked by hit count without reading what the diagnostic asserts, which
is the same mistake as every other count error in this document.

That leaves the missing background unexplained, and the honest next step is the one that does not
start from a warning tally: find the draw that should paint it. The render-target census used on
Astro (`SHARPEMU_LOG_AGC=1`, group draws by CB target address) applies unchanged - if the background
draw never appears, that is a dropped draw; if it appears and targets a surface nothing scans out,
that is the Astro failure mode on a second title and far more interesting.

The remaining candidates from that run, now unranked: 23 `unmapped_surface_htile` (no HTILE decoder),
15 `agc.scalar_pointer_fallback` on `SBufferLoadDwordxN`, and 28 each of `sceKernelDlsym` failing for
Unity's native render-plugin entry points.
## The blank background is the Astro failure mode, on a second title

Render-target census at the menu (`SHARPEMU_LOG_AGC=1`, `agc.rt_writer` grouped by target, filtered
to backbuffer-sized draws):

| target | 1920x1080 draws |
|---|---:|
| `0x0000000655C90000` | **780** |
| `0x0000000651ED0000` | 668 |
| `0x0000000656CD0000` | 667 |
| `0x00000006109D0000` | 665 |
| `0x0000000657590000` | 665 |
| `0x0000000012AF0000` | 52 |
| `0x0000000011B00000` | **44** |

and what actually reaches the screen:

```
vk.flip_capture version=1 queue=dcb.graphics work_sequence=24
   addr=0x0000000011B00000 size=1920x1080 pitch=1920
```

**The flip presents `0x11B00000`, which takes 44 draws. The five surfaces carrying 600-780 draws each
are never scanned out.** Those 44 are the menu text and widgets - which is exactly what is visible -
while the scene behind them goes somewhere the presenter never reads.

This is the same shape as Astro Bot: *presented buffer and rendered memory are disjoint*
(`astro-bot-boot.md`). The difference is that Superliminal renders **some** of its frame into the
presented surface, so the failure shows as a missing background rather than a black screen - which is
strictly more informative, because the same frame contains both a draw that lands correctly and draws
that do not.

That makes this the better title to debug the problem on. Both `0x11B00000` (works) and
`0x655C90000` (does not) are backbuffer-sized, same frame, same queue - so whatever distinguishes
them is visible in one trace rather than inferred across runs. Astro never offered that contrast.

Note also the address ranges: the presented target is `0x11B00000` and `0x12AF0000`, while the
unpresented ones are all `0x6xxxxxxxx`. Two distinct allocations, and the presenter only knows the
low one.

**Next:** diff the render-target state between a draw targeting `0x11B00000` and one targeting
`0x655C90000` in the same frame - format, tiling, and how each address was registered. The Astro
investigation could never do this because nothing there ever hit the presented surface.
### Refined: both display buffers work — it is the scene composite that is missing

The flip is healthy. `agc.dcb_set_flip` alternates `index=0` and `index=1` eight times each, and
`vk.flip_capture` picks up both registered addresses:

```
0x0000000011B00000  x8
0x0000000012AF0000  x7
```

So double buffering, registration and capture are all correct, and the earlier "the guest switched
backbuffers and we kept presenting the old one" reading is **wrong**.

The actual split:

| surface | draws | presented |
|---|---:|---|
| `0x11B00000`, `0x12AF0000` | 44 + 52 | **yes** - these are the menu text and widgets |
| `0x655C90000` and four siblings | 665-780 each | no - offscreen scene targets |

So Superliminal draws its **UI straight into the display buffer** and renders the **scene offscreen**,
expecting a composite to bring it forward. The UI arrives because it never needed the composite. The
background is missing because the composite does not land.

That is Astro's defect stated precisely: not "nothing reaches the display buffer" but "**the final
composite from offscreen scene targets into the scanout buffer does not happen**". Astro could not
show this because none of its frame reached the display buffer, so there was no working case to
contrast against.

**The next query is bounded and answerable from a trace already captured:** find draws whose target is
`0x11B00000` and whose `agc.texture_binding` includes `0x655C90000`. That is the composite.

- If it exists and runs, the composite is executing and its output is wrong - a shader or descriptor
  problem, and the `agc.scalar_pointer_fallback` hits become the prime suspect.
- If it does not exist, the composite draw is being dropped before it reaches the render path, and the
  question moves to why - which is where Astro's investigation stalled, but now with a title that
  proves the surrounding machinery works.
### The composite chain exists, runs offscreen, and its shaders fail their scalar loads

Joining `agc.texture_binding` to `agc.rt_writer` on the pixel-shader address answers what the scene
targets feed:

```
ps=0x603ECC800  samples a scene target -> renders into 0x666050000
ps=0x603ECE100                         -> 0x658E90000
ps=0x603F20F00                         -> 0x657E50000
ps=0x603F25A00                         -> 0x655C90000
ps=0x603F27700                         -> 0x655C90000
ps=0x603F29400                         -> 0x655C90000
```

All five scene targets **are** sampled (35-102 times each), so the post-process chain is running. But
every stage writes into another `0x6xxxxxxxx` surface. **No stage in this chain writes into
`0x11B00000`** - the final step into the scanout buffer is the one that never happens.

**And two of those shaders are already known to be broken.** From the earlier crash log, verbatim:

```
[COMPAT][SHADER] ps=0x0000000603F25A00 es=0x0000000603F00500 error=invalid-load-address
                 pc=0x1A4 op=SBufferLoadDwordx16 base=s92[0x00000001:0x00000000]
[COMPAT][SHADER] ps=0x0000000603F27700 es=0x0000000603F00500 error=invalid-load-address
                 pc=0xB8  op=SBufferLoadDwordx16 base=s72[0x00000001:0x00000000]
```

`ps=0x603F25A00` and `ps=0x603F27700` are the same shaders. So the two observations that looked
unrelated - "the background never composites" and "15 scalar pointer fallbacks" - are the same defect
seen from two angles: **the composite shaders cannot resolve their descriptors.**

A V# whose low dword is `1` and high dword `0` is not an address. The scalar evaluator is handing
these loads an unresolved descriptor, which is codex-gpu's rank-1 finding on Astro - branch-insensitive
resolution inventing or failing bindings - now with a named shader, a PC, and a visible consequence.

**This is the most concrete lead either title has produced.** Next: disassemble `0x603F25A00` at
`pc=0x1A4` and find where `s92` is meant to come from. `SHARPEMU_DUMP_SPIRV_ADDRESS` and
`SHARPEMU_STRICT_SHADER_DESCRIPTORS` both take a shader address and apply directly.
### The failing load, decoded from its raw words

`SHARPEMU_STRICT_SHADER_DESCRIPTORS=1` does not help - the title exits earlier and the same shader
repeats - but the diagnostic carries the instruction words, so the decode can be checked
independently of our own decoder:

```
words=[F4300824, FA000000]   base=s72[0x00000001:0x00000000]   pc=0xB8   ps=0x603F27700
```

- bits 31:26 = `0x3D` -> SMEM  (matches the Sony encoding diagram: SMEM is `111101`)
- OP = `(w >> 18) & 0xFF` = `0x0C` -> `s_buffer_load_dwordx16`, which is what the diagnostic names
- SBASE = `w & 0x3F` = `0x24` = 36, and SBASE counts SGPR **pairs**, so the descriptor is **s72:s73**

So our decode is right, and the fault is upstream of it: **`s72 = 0x00000001`, `s73 = 0x00000000`**.
A V# whose base is 1 is not an address, so those two SGPRs were never given the descriptor the
shader expects.

That narrows the question to one thing: **what should have written s72:s73 before `pc=0xB8`?** Either

- a user-data SGPR the AGC layer never populated for this draw, or
- an earlier scalar load in the same shader whose own result the evaluator could not resolve, making
  this the second failure in a chain rather than the first.

The evaluator already distinguishes those - `agc.scalar_pointer_fallback` reports `reason=read-failed`
for the second case. Both failing shaders report exactly that reason, which points at a chain rather
than at missing user data. Worth confirming before acting on it: that is one inference deep, and this
document records what my inferences are worth at that depth.
## Root of the composite failure: the extended-user-data load cannot be read

The `agc.scalar_pointer_fallback` diagnostic carries the whole chain, and it resolves the question the
previous section left open.

```
reason=read-failed shader=0x603F25A00 pc=0x24 op=SBufferLoadDwordx2
  base=s76[0x00000000:0x00000000] base_addr=0x0 imm=112
  definitions=[0x18: SLoadDwordx4[F408130E, FA0000F0]]
  user_data=[s0=0x06195500 ... s28=0x64C0FE80, s29=0x00000006]
  metadata=srt=0, eud=72
```

Reading it in order:

1. `s76` is **not** user data. It is *defined* at `pc=0x18` by an `s_load_dwordx4`, so this is a chain
   and the failing `SBufferLoad` is the **second** link, not the first.
2. Decoding that defining load, `F408130E`: bits 31:26 = `0x3D` (SMEM), OP = `(w >> 18) & 0xFF` =
   `0x02` = `s_load_dwordx4`, SBASE = `w & 0x3F` = `0x0E` = 14, and SBASE counts pairs, so its own
   base is **s28:s29**.
3. `user_data` gives `s28 = 0x64C0FE80`, `s29 = 0x00000006`, i.e. the address **`0x0000000664C0FE80`**
   - a perfectly ordinary guest heap pointer, and exactly the range the scene targets live in.
4. `metadata=eud=72` says extended user data begins at **s72**, which is precisely the register range
   that comes back as `1` and `0`.

So the shader's own user data is fine - 30 SGPRs are populated and `s28:s29` names a sane address.
**What fails is reading the extended user data from `0x664C0FE80`.** That read returning nothing
leaves `s72..s76` empty, so every descriptor built from them is garbage, so every composite stage
using them produces nothing, so the background never reaches scanout.

**One defect explains the whole visible symptom**, and it is a memory read rather than anything in the
shader translator: `reason=read-failed` is the evaluator saying it could not fetch guest memory at
that address at the time it evaluated.

Why it cannot read there is the next question and it is narrow: whether `0x664C0FE80` is mapped when
the evaluator runs, whether the evaluator is reading through the right address space, or whether the
buffer is written by GPU work that has not been made visible to the CPU yet. The last of those would
connect to the writeback path examined much earlier in this document.
### Why the read fails: a 4096-byte floor on a ~128-byte need

`Gen5ShaderScalarEvaluator.TryReadGlobalMemory` walks sizes down from
`MaxGlobalMemoryBindingBytes`, halving, and **stops at 4096**:

```csharp
for (var size = MaxGlobalMemoryBindingBytes; size >= 4096; size >>= 1)
{
    if (ctx.Memory.TryRead(baseAddress, rented.AsSpan(0, size))) { ... return true; }
}
return false;
```

Every attempt requires the **whole span** to be readable from `baseAddress` in one call. So the read
succeeds only if at least 4 KB is contiguously mapped from the exact base.

Against the failing case:

- base `0x0000000664C0FE80` - **not page aligned**, 0x80 bytes into a page;
- the shader wants `imm=112` plus a `dwordx4`, i.e. roughly **128 bytes**;
- but nothing under 4096 is ever attempted, so a buffer that is small, or simply close enough to the
  end of its mapping that 4 KB overruns it, fails **entirely** - and reports `read-failed` exactly as
  if the address were bogus.

That is consistent with everything observed: the address is sane, it is in the same range as the
scene targets which are demonstrably mapped, the user data around it is fully populated, and yet the
read returns nothing.

**Stated as the leading hypothesis, not a proven cause.** What would confirm it in one run: log the
`baseAddress` and the size that failed, or simply try smaller sizes and see whether the read starts
succeeding. The fix, if confirmed, is to keep halving below 4096 - or better, read only the bytes the
instruction actually needs, which is knowable from `imm` and the load width and is what the caller
already has.

This is the first explanation in this investigation that accounts for the whole chain - blank
background, composite stages producing nothing, garbage descriptors, `read-failed` on a valid
address - with a single mechanism, and it is four lines of code.
### REFUTED: the 4096 floor was not the cause

Lowering `TryReadGlobalMemory`'s floor from 4096 to 64 bytes changed **nothing**: the same **15**
`agc.scalar_pointer_fallback` hits, same shaders, same `read-failed`. So the read is not failing for
want of a smaller span, and the previous section's hypothesis is withdrawn.

It was a good-looking story - unaligned base, ~128 bytes needed, 4 KB demanded - and it was wrong.
The one thing it had going for it was being cheap to test, which is the only reason it cost a build
rather than a day.

**The change is kept anyway.** Requiring 4 KB for a descriptor fetch is indefensible on its own
terms: a wrong-but-mapped pointer near the end of a region would fail identically to a bogus one, and
the floor exists only to stop a stray readable byte satisfying a garbage pointer. 64 bytes serves
that purpose. It simply is not this bug.

**So `ctx.Memory.TryRead` genuinely cannot read `0x664C0FE80` at evaluation time**, at any size. The
remaining explanations are narrower than before, since size is eliminated:

1. the address is **not mapped when the evaluator runs**, even though the range is mapped later - a
   lifetime/ordering problem rather than an addressing one;
2. the evaluator reads through an address space that does not see this allocation - the same class as
   the primary-executor blindness found earlier in this document, where two views of "current thread"
   disagreed;
3. the buffer is **GPU-written and not yet visible to the CPU**, which would connect directly to the
   writeback path examined much earlier.

The cheap discriminator is to log whether the address resolves to any known region at the moment of
failure, rather than only that the read returned false. That distinguishes (1) and (2) from (3)
immediately, and it is the datum the diagnostic is currently missing.
### I patched the wrong overload — and the read is failing for a simpler reason

`TryReadGlobalMemory` has **two** overloads. That is why adding a constant next to "the" definition
collided earlier, which I treated as a duplicate-edit accident instead of the signal it was.

- 4-arg `(ctx, baseAddress, out data, out dataLength)` - the one I lowered the floor on and added
  `agc.global_read_failed` to.
- 5-arg `(ctx, baseAddress, sizeBytes, out data, out dataLength)` - **the one the failing path calls**
  (`Gen5ShaderScalarEvaluator.cs:553-560`, inside the buffer-descriptor branch).

So the floor change and the diagnostic both went somewhere the failure never reaches, which is
exactly why the new warning never printed. Neither change is harmful and both stay, but neither
touched this bug.

**And the failure is simpler than I had it.** The buffer read is called with
`bufferDescriptor.BaseAddress`, and for `pc=0x24` that descriptor is `s76[0x00000000:0x00000000]` -
base address **zero**. The read fails because it is asked to read address 0, not because guest memory
is unreadable. Everything I wrote about mapping lifetime and address-space views was chasing a
failure mode the data never supported.

The real chain is unchanged and was correct two sections ago: **`s76` is never populated**, because
the `s_load_dwordx4` at `pc=0x18` that defines it does not produce a value. That is the only thing
worth investigating, and the question is why that load - whose own base `s28:s29` reads as the
perfectly plausible `0x664C0FE80` - leaves its destination empty.

The instrumentation to answer it belongs on the **defining** load at `pc=0x18`, not on the consumer
at `0x24` that merely reports the consequence.
## RETRACTION 5: the whole thing is racy, and the background DOES render

Three runs, identical build and identical flags
(`PS5MANAGER_PROBE=force`, `GUEST_THREADS_NATIVE_WORKER=1`, `MAIN_ENTRY_NATIVE_WORKER=1`):

| run | outcome |
|---|---|
| A | menu reached, **blank white background**, survived past 110 s |
| B | crashed before the menu - `Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code` |
| C (plus `PAD_AUTO_PRESS=1`) | menu reached **with the storage-room background fully rendered**, then access violation `0xC0000005` in `ImportDispatchGatewayManaged` |

**Run C retracts a conclusion I stated as measured.** The menu background is not permanently broken:
the scene renders - shelves, boxes, window, hanging lamp, ladder, all correct behind the logo and the
NEW GAME / OPTIONS items. So "the composite never reaches scanout" was **one run's behaviour reported
as a property of the emulator**.

Everything downstream of that in this document is affected. The composite chain, the `s72:s73`
descriptor, the extended-user-data load - those observations are real, but they describe *a run where
the race lost*, not a fixed defect. `agc.scalar_pointer_fallback` firing 15 times in one boot and the
background rendering correctly in another are consistent only if the descriptor resolution depends on
timing, which is what a race means.

**Three distinct failure modes across three runs, same inputs.** That is the headline, and it
outranks every individual finding above it: the emulator is not deterministic on this title, so any
single-run conclusion about it - including several of mine - is a sample, not a measurement.

### What this changes for anyone continuing

1. **Run it several times before believing any symptom.** I did not, repeatedly, and this document
   carries the cost.
2. The `UnmanagedCallersOnly` fail-fast and the `ImportDispatchGatewayManaged` access violation are
   both entries into managed code from a guest stub. Two symptoms, one area.
3. The background composite working in run C means the shader path can succeed. Whatever makes it
   fail is timing-dependent, which points back at descriptor state being read before the producing
   work is visible - the writeback question raised much earlier and never settled.
### Six runs, measured: the menu is reached about a third of the time

Same build, same three flags (plus `PAD_AUTO_PRESS` where noted). Outcomes:

| # | flags | outcome |
|---|---|---|
| A | 3 flags | menu, blank background, alive past 110 s |
| B | 3 flags | crash before menu - `UnmanagedCallersOnly` fail-fast |
| C | 3 + auto-press | menu **with background fully rendered**, then AV `0xC0000005` in `ImportDispatchGatewayManaged` |
| D | 3 + auto-press | crash - `UnmanagedCallersOnly` fail-fast |
| E | auto-press only* | title screen, 32 FPS, stable |
| F | 3 + auto-press | crash - `UnmanagedCallersOnly` fail-fast |
| G | 3 + auto-press | crash - `UnmanagedCallersOnly` fail-fast |

\* Run E is void: **environment variables do not persist between shell invocations here**, so it ran
with only `PAD_AUTO_PRESS` and none of the gate/worker flags. Every flag must be set in the same
command as the launch. Worth recording because the run looked like a legitimate regression - stable
title screen - and was purely my own harness error.

So of six valid runs, **two reached the menu and four died before it**, all four with the same
`UnmanagedCallersOnly` fail-fast. No run reached gameplay, and no gameplay was ever captured.

**The fail-fast is the dominant failure, not an edge case.** The native-worker flags reduce it enough
to reach the menu sometimes; they do not eliminate it. That matches the docstring's own warning that
the migration was deferred because it "increased splash hangs" and was never root-caused.

### Honest status

The user reports the title reaching gameplay. I cannot reproduce that in six attempts and have no
capture of it, so I am not recording it as reached - but a ~1-in-3 success rate to the menu means my
failure to reproduce is weak evidence either way, and a run that gets further than mine is entirely
consistent with what is measured here.

The single highest-value fix remains the `UnmanagedCallersOnly` entry: four of six runs die there,
and every downstream question about this title is gated behind surviving it.
### The fail-fast is located: the native-worker prologue/epilogue

The dominant crash - four of six runs - is .NET refusing a call into an
`[UnmanagedCallersOnly]` method from managed code. The stack frames
(`ExecuteGuestContinuationEntry`, `ImportDispatchGatewayManaged`) are **not** the offenders:
`ImportDispatchGatewayManaged` is a plain delegate reached through
`Marshal.GetFunctionPointerForDelegate` and carries no attribute. Those frames are simply what is on
the stack when the runtime refuses.

The only `[UnmanagedCallersOnly]` methods on this platform's guest-execution path are:

```
DirectExecutionBackend.NativeWorker.cs:695   RunPrologue(nint executorHandle)
DirectExecutionBackend.NativeWorker.cs:717   RunEpilogue(nint executorHandle, int nativeResult)
```

- the remaining ones are macOS-only (`MetalVideoPresenter`, `PosixCoreAudioStream`) or POSIX signal
  handling, none of which is live here.

**These are the native-worker entry points - precisely the code the two flags turn on.** That closes
the loop on why the flags both help and hurt: they move guest execution onto raw OS threads whose
prologue and epilogue are `[UnmanagedCallersOnly]`, which is correct for a thread the CLR never
created, and a fail-fast the moment a CLR-created thread reaches the same entry. Whether a given run
survives depends on which thread services that entry, which is exactly the ~1-in-3 behaviour measured
above.

That also explains the docstring's deferred migration: migrating *some* threads leaves a mixed
population where both kinds can reach the same entry point.

**The fix is bounded and does not require choosing between the two thread models:** ensure only
genuinely native threads enter `RunPrologue`/`RunEpilogue` - assert it, or route CLR-created threads
through a managed-callable wrapper that does the same work. Either removes the fail-fast without
reverting the flags that made the menu reachable at all.
**Refinement on the fix shape.** `RunPrologue`/`RunEpilogue` have **no managed call sites** - they are
only taken as function pointers (`&RunPrologue`, `NativeWorker.cs:432-433`) and embedded in emitted
native code. So nothing in the codebase "calls an UnmanagedCallersOnly method from managed code" in
the literal sense, and saying so above was imprecise.

What the runtime is objecting to is **thread provenance**: when the emitted stub runs on a thread the
CLR created, the transition back into the attributed method is treated as a managed caller and
fail-fasts. That is the same conclusion the file's own comment reaches - "a guest stub entered from a
CLR-created thread sits above managed frames" - and it means the fix is *not* adding a
managed-callable wrapper, which would not help. It is ensuring the emitted stub only ever executes on
a thread the CLR did not create, which is what full migration was supposed to guarantee and what
partial migration cannot.

That reframes the flags: `GUEST_THREADS_NATIVE_WORKER` and `MAIN_ENTRY_NATIVE_WORKER` do not fix the
hazard, they change how many threads are on the safe side of it. Reaching the menu two runs in six is
consistent with that and not with a wrapper-shaped bug.