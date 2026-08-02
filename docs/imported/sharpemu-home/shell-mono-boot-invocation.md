# Booting NPXS40087 far enough to run Mono

The invocation that gets the system shell (`NPXS40087`) past its C++ init phases and
into `PsmFramework` was not recorded anywhere in this repository. Several passes
reported results from it and none of them wrote it down, so every pass rediscovered it
and one pass (this one) first reproduced a *different*, much earlier failure and
mistook it for the current state. This file exists so that cannot happen again.

Everything below was run and observed on 2026-07-30 on the branch `exp/shell-boot`,
build `artifacts/bin/Release/net10.0/win-x64/SharpEmu.exe`.

## The invocation

```powershell
$env:DOTNET_ROOT              = "C:\dotnet"
$env:SHARPEMU_FIRMWARE_DIR    = "C:\sharpemu\games\PS5_4.03_reconstructed\filesystems"
$env:SHARPEMU_LLE_MODULES     = "libScePsm"
$env:SHARPEMU_LLE_ALLOW_UNRESOLVED = "1"
$env:SHARPEMU_IPMI_PSM_SHARED_DMEM = "1"

& artifacts\bin\Release\net10.0\win-x64\SharpEmu.exe --cpu-engine=native `
    "C:\sharpemu\games\PS5_4.03_reconstructed\filesystems\system_ex\app\NPXS40087\eboot.bin"
```

The process does not exit on its own quickly; run it redirected and give it a few
minutes, or kill it. Both streams matter — the loader writes to stderr.

## Why each variable is load-bearing

| Variable | What happens without it |
|---|---|
| `SHARPEMU_FIRMWARE_DIR` | Only needed when the working directory is not a tree containing `games/`. `KernelMemoryCompatExports.FirmwareRootsFor` otherwise searches `<cwd>/games/PS5_4.03_reconstructed/filesystems/<mount>` and then the 9.00 dump. In a `sharpemu-workers` worktree there is no `games/`, so the mounts resolve to nothing and the boot dies early. |
| `SHARPEMU_LLE_MODULES=libScePsm` | **This is the one that was missing.** Without it `libScePsm.sprx` is never mapped and the shell's static imports of it stay unbound. Measured: `[LOADER] Setup 3550/3550 import stubs (direct bridge, lle_redirects=0)` and then two unresolved imports followed immediately by a guest assertion (below). |
| `SHARPEMU_LLE_ALLOW_UNRESOLVED=1` | `SharpEmuRuntime.LoadFirmwareLleModules` fails closed on any unresolved strong import. `libScePsm` names 609 imports and 234 of them are unresolved against our HLE surface, so without this flag the emulator throws `InvalidOperationException: Firmware LLE module libScePsm has unresolved imports: …` before a single guest instruction of the shell runs. With it, the module loads and those 234 stay traps until called. |
| `SHARPEMU_IPMI_PSM_SHARED_DMEM=1` | Hosts the firmware-derived five-method `ScePsmSharedDmem` IPMI contract. Without it, PSM method `0x34560000` is refused, the guest logs `LockSharedDmemFunc error 0x80020003`, and no shared region exists. With it, the emulator creates `/ScePsmSharedDmem_00000000`, the create and lock calls return zero, and ShellUI advances through `TopMenuBG`. This remains an explicit experiment because a frame has not yet been submitted. |

## The failure you get if you forget `SHARPEMU_LLE_MODULES`

This is worth recognising on sight, because it looks like a shell bug and is not:

```
[LOADER][WARN] Import#7806 unresolved: nid=FqHN0elWA6E ret=0x0000000C0000E5B9 …
[LOADER][WARN] Import#7831 unresolved: nid=lWlBrUu77Kg ret=0x0000000C0000E8CB …
[GUEST][ASSERT] W:\Build\J01736823\vsh\shell\shell_ui\src\shell_ui_main\shell_ui_boot_manager.cpp(296) :
  Assertion Failed (0 <= sce::pss::orbis::framework::PsmFramework::Initialize(psm_init_param, argc, argv))
  in function InitializeFramework
[LOADER][INFO] _Assert does not return - terminating guest (assertion failed (code=0xA0020087))
```

Both NIDs are exports of `libScePsm`, sourced from
`games/3.02/Stub call library/libScePsm.c`:

* `FqHN0elWA6E` — line 510, `sce::pss::orbis::framework::PsmInitParam::PsmInitParam()`
* `lWlBrUu77Kg` — line 505, `sce::pss::orbis::framework::PsmFramework::Initialize(PsmInitParam const&, int, char**)`

The shell asserts because `Initialize` returned the unresolved-import sentinel, not
because anything in the shell is wrong.

## What a correct run looks like

Markers, in order, from `stderr`:

```
[LLE] Loaded libScePsm: path=…\libScePsm.sprx range=0x0000000002000000-0x0000000002572660 exports=121 imports=609
[LLE][ERROR] libScePsm has 234 unresolved strong import(s): … Import stubs remain traps.
[LLE][WARN] loading libScePsm anyway (SHARPEMU_LLE_ALLOW_UNRESOLVED=1); 234 import(s) will trap if called
[RUNTIME] Starting module libScePsm.sprx: dt_init=0x0000000002000010
[LLE] lWlBrUu77Kg -> libScePsm:_ZN3sce3pss5orbis9framework12PsmFramework10InitializeERKNS2_12PsmInitParamEiPPc 0x00000000022C9460
[LLE] co1TwYJ2ybU -> libScePsm:_ZN3sce3pss5orbis9framework12PsmFramework3RunEv 0x00000000022D0870
[SYSMODULE][INFO] sceSysmoduleLoadModuleByNameInternal('libmono-btls-shared')
[LLE] libmono-btls-shared: runtime bind exports=417 stubs=127 bound=127 direct=0 failed=0 retargeted=0
```

`libScePsm` maps at `0x02000000`, so a guest address of the form `0x022xxxxx` is inside
it and `0x0C00xxxxx` is inside `eboot.bin`. That distinction is how you tell whether a
call came from Sony's PSM framework or from the shell's own C++.

The run observed on 2026-07-30 reached Mono, loaded `libmono-btls-shared` and 55 other
runtime modules, and then terminated with a host access violation (`0xC0000005`) shortly
after `[LOADER][INFO] Emulated generated-code fs: access #8`. Whether that is the same
end state as the "goes idle" runs described in earlier passes is **not established**;
those passes did not record their invocation, so the two cannot be compared.

## 2026-08-01 shell-scene milestone

A clean build of `exp/shell-boot` at `28dfe88e`, run with the invocation above and
all host-forced background variables unset, established the following sequence:

```text
[PGraphics] Create PGraphicsDevice v(0) s(6291456)
Initialized PGraphicsDevice
[IPMI][PSMDMEM] backed '/ScePsmSharedDmem_00000000' with 1048576 bytes of shared memory
[IPMI][PSMDMEM] method=0x34560000 ... -> status 0x00000000
[IPMI][PSMDMEM] method=0x34560003 ... -> status 0x00000000
[RNPS] sceRnpsAppMgrGetAppInfo titleId=NPXS40141 path=/system_ex/rnps/apps/NPXS40141
[eboot] I/PSM.UI : OnFocusActiveSceneChanged [] -> [TopMenuBG : Scene]
```

This is a stronger feasibility result than merely reaching Mono: the real firmware
shell loaded its managed UI, initialized PGraphics and RNPS, and activated a named
home-shell scene. It is not a rendering result. The run made 21 compositor calls but
no canvas/surface submission and no guest frame. It later took a host access violation
in the shell's stdout-reader thread at `eboot.bin+0x1047b`, reading address `0x20`;
that crash must not be described as a guest graphics failure.

The same run exposed `l96YlUEtMPk` immediately after `TopMenuBG`. Firmware 4.03
identifies it as `sceShellCoreUtilSetDeviceIndexBehavior`; its 247-byte body at
`libSceSystemService.sprx+0x127c0` packs the three integer arguments, retains the
new value, notifies ShellCore only when it changes, and returns zero. The HLE export
now preserves that packed state instead of returning the unresolved-import sentinel.

A packet trace also bounds the tempting AGC hypothesis. The only `COPY_DATA` built
during this phase has control `0x00016209`, source `0`, and destination
`0x0000600000147FE0`: AMD's public GFX9+ packet definitions decode that as a 64-bit
GPU-clock-count write to memory. It remains in PGraphics' initialization command
buffer; no compositor context command or flush submits it in this run. The missing
`IT_COPY_DATA` parser case is real work, but it is not the reason this run produced
no frame, so it was not guessed into the parser.

The first compositor command after PGraphics initialization is
`sceCompositorSetResolutionCommand(1)`. The 4.03 client body at
`libSceComposite.sprx+0x1660` consumes only the 32-bit mode, emits ring command type
`0x13`, returns `0x80D40004` when the client context is null, and otherwise returns
zero. It does not translate the mode into dimensions; the absent compositor daemon
owns that reply. The HLE now records the mode and preserves those return rules instead
of unconditionally returning zero from a log-only stub.

### Post-activation run after removing the import-region ceiling

The loader previously searched only 64 fixed addresses for each import-stub and import-
data mapping. ShellUI can load more than 64 firmware modules, so the next module failed
even though almost the entire reserved address band was free. The search now covers the
bounded 1 TiB stub and data bands. A native run with inflight tracing enabled validated
the change in the guest: it loaded modules beyond the old ceiling through handle 154,
including `libSceIme`, `libSceSystemLogger2`, `Sce.Vsh.Np.RifManager`, LoginMgr,
DbPreparationWrapper, and VisionRecognition.

That run advanced beyond the earlier marker:

```text
[eboot] I/PSM.UI : OnFocusActiveSceneChanged [] -> [TopMenuBG : Scene]
[eboot] I/PSM.UI : SceneQ : Loaded[...] : Sce.Vsh.ShellUI.AppSystem.LayerManager.RootScene : RootScene
[eboot] I/PSM.UI : SceneQ : Loaded[...] : LayoutManager : Scene
[LOADER][TRACE] Import#5100000: libKernel:pthread_mutex_lock (7H0iTOciTLo)
```

It still did not create a canvas, issue an AGC submission, flush a compositor context,
or present a frame. It then faulted in the firmware's `SceShellUIConsoleWriter` at
`eboot.bin+0xfb48`, where the writer dereferenced an invalid queued string pointer
(`rbx=0x000000626f4a7265`). This is again a shell output-worker failure after scene
activation, not evidence of a graphics failure and not Mission F. A filtered rerun
showed the writer's exact error-check mutex (`0x0000000100001488`) locking, handing off,
and unlocking successfully; that rerun later hit the already-known nondeterministic Mono
SGen suspend stall before it could reproduce the writer fault. Do not bypass the writer
or synthesize a frame from this evidence: the next frame target remains the first real
canvas/context creation and compositor/AGC submission from firmware code.

### Console-writer queue lifetime evidence

A subsequent bounded run armed the existing store-only detours at the firmware queue
producer (`eboot.bin+0x10410`) and consumer (`eboot.bin+0xf950`). The producer receives
the console object in `rdi`, a byte buffer in `rsi`, and its length in `rdx`; the
consumer swaps the protected active list, then reads a `shared_ptr<string>` pair from
offsets `+0x10` and `+0x18` of each 0x28-byte list node. Disassembly of the producer's
helper at `eboot.bin+0x10260` confirms those two fields are populated with the string
object and its control block when the node is allocated.

The rerun reproduced the failure earlier and more directly. The console object still
had a structurally valid active-list sentinel (`0x00000001000015a0`) and count (`1`)
after the mutex hand-off, but its first node (`0x000000010017f980`) no longer contained
the pointer pair. The two fields were ASCII payload bytes:

```text
node+0x10 / rbx = 0x6974616369666974  ("tificati")
node+0x18 / r12 = 0x6a2d62642e326e6f  ("on2.db-j")
```

The consumer then called firmware `_Atomic_fetch_add_4` on `r12+8` while retaining the
supposed shared pointer and faulted there. The same node address had appeared as a live
argument in the NotificationDb path immediately before the fault. This localises the
defect to a queue-node lifetime/reuse or allocator-overlap problem: the node was validly
linked but its payload storage had been reused or overwritten. It does **not** support
blaming the console mutex, bypassing the writer, or adding a graphics stub. The trace is
`tmp/shell-validation/frame-target-queue-detour.stderr.log`; the relevant firmware
disassembly is preserved as `tmp/shell-validation/eboot-10410.disasm.txt`.

The store-only trace record now captures `rbx` and `r8` through `r15` as well as the
four ABI argument registers. A clean first drain in
`tmp/shell-validation/queue-ownership-registers3.stderr.log` establishes the control
case before reuse: the active sentinel was `0x1000015a0`, the detached list count was
one, and the consumer saw node `0x10005e700`, string object `0x10005e2c0`, and control
block `0x10005e300`. Those are distinct, mapped addresses. The later ASCII control
block is therefore a transition from a valid owned node, not a bad structure layout at
construction time.

A second, read-only allocator diagnostic narrows the boundary further. Host-side libc
allocation logging placed the HLE compatibility allocations in a separate, high-address
range; the reused node is in the firmware's `0x100...` mspace and was not returned by
that host allocator. More importantly, the firmware then emitted its own
`[SceLibc] A heap error is detected.` / `SceLibcInternalHeap` self-check and asserted
from the `libSceLibcInternal` free path. The bounded trace is
`tmp/shell-validation/frame-target-libc-28.stderr.log`. This rules out overlap with the
HLE `Marshal.AllocHGlobal` heap, but it does not yet identify the first invalid free or
metadata write inside the firmware heap. Attempts to place additional store-only
detours at the LLE allocation/free sites ended in the known CLR reverse-P/Invoke
fail-fast before producing a trustworthy lifetime trace. No allocator substitution or
queue-lifetime fix is justified until that first corrupting operation is observed.

Two tempting host-thread workarounds were also measured and rejected. Running all guest
entries on raw native workers avoids the early CLR reverse-P/Invoke fail-fast and reached
PGraphics plus synchronous loads of `TopMenuBG` and `LayoutManager`, but then entered a
high-frequency AppDb/Party polling loop without activating the scene or creating a
canvas (`tmp/shell-validation/native-worker-verification.stderr.log`). Capping native
workers at one deadlocks immediately: the console writer holds the sole permit while
parked in its condition wait, preventing the producer/main entry from running
(`tmp/shell-validation/queue-ownership-serial.stderr.log`). Neither setting is a shell
fix and neither should become a default.

### System background state ABI recovered from 4.03

The ShellCore-facing background state calls are no longer unresolved contracts.
Direct disassembly of `libSceSystemService.sprx` establishes both packed fields:

| export | vaddr | exact field |
|---|---:|---|
| `sceShellCoreUtilSetSystemBGState` | `0x11d60` | `(arg0 & 3) << 7`, mask `0x180` |
| `sceShellCoreUtilGetSystemBGState` | `0x11dc0` | `(shared & 0x180) >> 7` |
| `sceShellCoreUtilSetSystemBGWaveColor` | `0x12000` | `(arg0 & 15) << 10`, mask `0x3c00` |
| `sceShellCoreUtilGetSystemBGWaveColor` | `0x12060` | `(shared & 0x3c00) >> 10` |

Both getters take the user id in RDI and an optional output pointer in RSI. They
return zero without writing when RSI is null. The HLE preserves those exact masks,
null-output semantics and process-global values; it does not guess which state or
colour the home scene will select. Focused tests cover export registration, masking,
round-trip storage and the null-output branch.

## What an unresolved import actually does

MEASURED, `DirectExecutionBackend.Imports.cs:582-596`. An import with no HLE export is
not silent and does not abort. `DispatchImport` sets
`rax = ORBIS_GEN2_ERROR_NOT_FOUND` (`0x80020002`,
`SharpEmu.HLE/OrbisGen2Result.cs:41`) and prints, on **every** call:

```
[LOADER][WARN] Import#106046 unresolved: nid=mPYKD12UDQI ret=0x0000000C00345A7C rdi=0x0000000002020000 rsi=0x00007FFFD17FFDEC rdx=0x0000000C00D4C010 rcx=0x0000000000000000 r8=0x00007FFFD17FF858 r9=0x0000000000000000
```

Two consequences worth internalising:

1. **The call order and arguments of every unimplemented NID are already free.** You do
   not need to add a logging stub to learn which unimplemented functions a boot reaches
   or in what order. Adding a stub changes exactly one thing: the return value, from
   `0x80020002` to whatever the stub returns.
2. **`0x80020002` propagates.** In the same run, `Import#106318 rdi=0xFFFFFFFF80020002`
   — the sentinel from one unresolved call being handed to the next function as an
   argument. When reading a boot log, an argument of `0xFFFFFFFF80020002` means an
   unresolved import upstream, not a guest bug.

An LLE module's unresolved imports behave differently: `SHARPEMU_LLE_ALLOW_UNRESOLVED`
leaves them as *traps*, not as error-returning stubs.
