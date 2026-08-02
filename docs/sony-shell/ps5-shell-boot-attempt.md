<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Booting the console's own software: what actually happens

An RPCS3-style experiment. Instead of pointing Prosperismo at a game, point it at
PS5 firmware 4.03's own `vsh` applications and at `SceShellCore.elf`, and record
truthfully how far each one gets.

**Headline: further than expected, and the wall is not where the hardware is.**
Six firmware modules were run. All six load, relocate, resolve their import
tables and execute their real entry point. Three of them run far enough to print
their *own* Sony diagnostic messages. One reaches a steady-state service loop with
zero unresolved imports. `SceShellCore.elf` — the real shell — maps all 27 MB and
runs ~450 imports of C++ static initialisation before stopping inside
`std::locale` setup, which is long before it reaches anything graphical.

Nothing here renders a frame. No target reached video output at all. The blocker
is the C/C++ runtime and the system-service surface, **not** the compositor.

---

## Targets and results

All runs use the decrypted dump at `games/PS5_4.03_reconstructed/filesystems`.
"Depth" is the highest import-dispatch index reached — a proxy for how much real
guest code executed.

| Target | Size | Module name | Imports | Impl. | Stub | Missing | Depth | Outcome / first hard failure |
|---|---:|---|---:|---:|---:|---:|---:|---|
| `system/vsh/app/NPXS40112/eboot.bin` | 0.50 MB | *(network daemon)* | 160 | 118 | 12 | **30** | #114 | **Reached its service loop.** Zero unresolved imports. Polls `sceNetCtlGetState` → `sceKernelSleep` forever; killed by the 25 s stall watchdog. |
| `system_ex/app/NPXS40028/eboot.bin` | 1.29 MB | `redis-server` | 206 | 124 | 7 | **75** | #158537 | Ran ~158 k import calls. Died on missing `Av3zjWi64Kw` (`libSceLibcInternal`) after spinning 44 293 calls on unresolved `NDcSfcYZRC8`. |
| `system/vsh/SceShellCore.elf` | 27.0 MB | `SceShellCore` | 2287 | 591 | 98 | **1598** | #446 | Maps all 27 MB, runs C++ static init. Dies on `std::locale::_Getgloballocale()` (`hEQ2Yi4PJXA`, `libSceLibcInternal`) — virtual call through the returned error code. |
| `system/vsh/app/NPXS40153/eboot.bin` | 0.48 MB | `SceCloudDaemon` | 271 | 141 | 11 | **119** | #318 | Guest printed its own error, then exited. Blocker: `libSceIpmi` (`Z5i0--Vqfwg`). |
| `system_ex/app/NPXS40001/eboot.bin` | 0.43 MB | `SceWebAppLauncher` | 1209 | 537 | 10 | **662** | #77 | Guest printed its own error with Sony's source path, then exited. Blocker: `libScePsm` (`FqHN0elWA6E`, `lWlBrUu77Kg`). |
| `system/vsh/app/NPXS40100/eboot.bin` | 0.20 MB | `SceAvCaptureManager` | 165 | 67 | 5 | **93** | #26 | Guest printed `Initialize failed`, called `exit(1)` cleanly. Blockers: `libSceLncUtil` (`f-Q8Nd33FBc`), `libSceIpmi` (`Z5i0--Vqfwg`). |

`Impl.` / `Stub` / `Missing` are a static join of each module's imported NIDs
against the 4 255 NIDs Prosperismo registers (3 065 implemented, 1 190
stub/not-implemented). They describe the whole module's surface, not just the
path actually executed — which is why a module with 662 missing NIDs can still
run: it only ever calls a fraction of them.

### The guest speaks for itself

The strongest evidence that this is real execution and not a loader illusion is
that three modules ran far enough to emit their own log messages, through their
own `printf`, using their own format strings:

```
[SceAvCatureManager] Initialize failed (0x80020002)
[CloudClientDaemon] ERROR ipmi_wrapper.cpp(132)[    2.461]: malloc() failed.
[SceWebAppLauncher/native:ERROR] 0x80020002 (W:\Build\J01736823\vsh\app\web_app_launcher\native\src\main\main.cpp:389)
```

These are Sony's strings, not ours. `[SceAvCatureManager] Initialize failed (0x%x)`
is present verbatim in the binary at file offset `0x23E47` — including the missing
`p` in "Capture", which is a typo in Sony's own source. The third line carries
Sony's internal build-server path and the exact line number of the failing check,
and identifies NPXS40001 as `SceWebAppLauncher`. `0x80020002` is precisely the
`ORBIS_GEN2_ERROR_NOT_FOUND` sentinel our unresolved-import stub leaves in `RAX`,
so each module is correctly reporting the failure we handed it.

`SceCloudDaemon` even reports a guest uptime of 2.461 seconds and blames
`ipmi_wrapper.cpp(132)` — its IPMI wrapper interpreted our error return as an
allocation failure.

---

## What was changed to get these numbers

One change, in `src/SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs`: the
Itanium C++ ABI allocation operators, exported from `libSceLibcInternal`.

A retail game never imports these — its toolchain statically links a private
`libc++`, so `operator new` is title code that reaches us as plain `malloc`.
Every firmware module is built the other way round: it links
`libSceLibcInternal` dynamically, so `_Znwm` and friends arrive as ordinary
imported NIDs. Prosperismo registered none of them.

The consequence was severe and identical in four of the six targets: the first
C++ allocation took the unresolved-import path, which leaves
`0xFFFFFFFF80020002` in `RAX`; the caller then stored through that value as if it
were the new object and took an access violation, typically within the first
handful of instructions.

Ten NIDs were added (`_Znwm`, `_Znam`, their `nothrow` forms, `_ZdlPv`, `_ZdaPv`,
and the sized/nothrow deletes), routed to the same heap as `malloc`/`free`. NIDs
were confirmed with the repo's own `scripts/nid_resolver.py`. This is strictly
additive — every one of these NIDs previously resolved to nothing, so no existing
behaviour can change.

The effect on how far the firmware runs:

| Target | Depth before | Depth after |
|---|---:|---:|
| `SceShellCore` | #12 | **#446** |
| `SceCloudDaemon` | #6 | **#318** |
| `SceWebAppLauncher` | #3 | **#77** |
| `SceAvCaptureManager` | #4 | **#26** |

`SceShellCore` went from dying on its first allocation to running its entire
static-constructor chain.

---

## Where it actually stops, per layer

### 1. Loading is a solved problem

This surprised me and is worth stating plainly. PS5 firmware modules are
structurally *different* from the game eboots Prosperismo normally runs:

* A game `eboot.bin` is a **SELF** container (magic `0x5414F5EE`) whose dynamic
  linkage lives in `PT_SCE_DYNLIBDATA` with `DT_SCE_*` tags holding offsets.
* A firmware module is a **bare ELF** (`EI_OSABI=0x09` FreeBSD,
  `EI_ABIVERSION=2`, `e_type=0xFE10 ET_SCE_DYNEXEC`) with **standard** `DT_*`
  tags holding virtual addresses, and no `PT_SCE_DYNLIBDATA` at all — the tables
  sit in an ordinary `PT_LOAD` segment with `p_flags=0`.

`SelfLoader` validates only magic, ELF64, endianness, `e_phentsize == 56` and
`e_machine == EM_X86_64`. It checks neither `e_type` nor `EI_OSABI`, and its
`TryLoadTableBytes` path resolves the dynamic tables through guest memory, so the
address-based firmware layout works unmodified. All six targets mapped every
segment and built a complete import-stub table on the first attempt.

### 2. `libSceLibcInternal` — the immediate wall

Sony's C/C++ runtime is the single biggest blocker, and it is what stops the
shell. `SceShellCore` is missing **198** of its `libSceLibcInternal` imports; it
dies at `std::locale::_Init()` / `std::locale::_Getgloballocale()`, i.e. inside
the Dinkumware STL's global-locale construction, during static initialisation.
`redis-server` burned 44 293 calls on one unresolved `libSceLibcInternal` NID.

The names that show up here (`_ZNSt6locale5_InitEv`, `_ZSt14_Throw_C_errori`,
`_Atomic_compare_exchange_weak_4`, `_Assert`, `_Stderr`) are Dinkumware/MSVC-STL,
not libc++ — a different C++ standard library from the one games carry, which is
exactly why the existing HLE surface does not cover it.

### 3. No firmware filesystem, and no IPC constellation

Two architectural gaps that no amount of NID work fixes on its own:

* **There is no firmware filesystem.** The guest mount table is
  `[/app0 /temp0 /download0 /hostapp /devlog/app]`, with `/app0` bound to the
  eboot's own directory. `/system`, `/system_ex`, `/vsh`, `/dev/*` do not exist
  and resolve `path-unmapped` → `ENOENT`. Observed directly: `SceShellCore` fails
  to open `/dev/npdrm`; `redis-server` fails to open `/dev/urandom`.
* **PS5 system software is a multi-process constellation.** These modules talk to
  each other over `libSceIpmi`. `SceAvCaptureManager` and `SceCloudDaemon` both
  die in IPMI, and `SceCloudDaemon` names `ipmi_wrapper.cpp` when it does.
  Running one module in isolation, with no `SceShellCore` on the other end of the
  socket, IPMI can never succeed regardless of how many NIDs are implemented.
  Booting "the shell" really means booting a *set* of cooperating processes.

### 4. The compositor is not the wall

The task brief predicted the hardware-bound compositor path would be where this
died. The data does not support that. Of `SceShellCore`'s 1 598 missing NIDs:

| Library | Missing | Library | Missing |
|---|---:|---|---:|
| `libSceNpCommon` | 493 | `libSceSysCore` | 55 |
| `libSceLibcInternal` | **198** | `libSceJson2` | 51 |
| `libSceFsInternalForVsh` | 174 | `libSceLibreSSL` | 44 |
| `libkernel` | 158 | `libSceShellCoreUtil` | 26 |
| `libSceNpManager` | 130 | `libSceRegMgr` | 21 |

The graphics- and hardware-bound libraries are a rounding error by comparison:
**`libSceComposite` 6, `libSceVideoOut` 4, `libSceHmd` 15, `libSceAudioOut` 3,
`libSceCamera` 2.** The shell is overwhelmingly blocked on the C++ runtime, the
PSN/account stack, the VSH filesystem service and the kernel surface. We never
get close enough to the compositor for it to matter.

---

## Verdict

**Booting the PS5's own UI is not a realistic near-term goal, but "run a PS5
system application" is already partly achieved and is a reasonable near-term
goal.**

Honest reasoning:

* **Already true today.** Firmware modules load and execute correctly. One
  system daemon (NPXS40112) reaches its main service loop with a fully resolved
  import table and no errors — it is arguably *running*, just waiting for a
  network link that will never come up. That is a genuine, verifiable result.
* **Cheap next step.** The C++ ABI allocators cost ten NIDs and moved
  `SceShellCore` 37× deeper. Finishing `libSceLibcInternal`'s Dinkumware STL
  surface (~200 NIDs for ShellCore, and it is the top blocker for four of six
  targets) is the highest-leverage work available and is ordinary, testable,
  non-speculative code.
* **Why the UI itself is still far.** Past static init, `SceShellCore` needs
  `libSceNpCommon` (493), `libSceFsInternalForVsh` (174), `libkernel` (158) and
  `libSceNpManager` (130) — roughly 1 600 NIDs, most with no public
  documentation and no name in our catalogue (the vast majority of the missing
  NIDs above resolve to `?`). That is reverse-engineering work per function, not
  transcription.
* **Two structural blockers beyond NID count.** A firmware filesystem would have
  to be mounted (`/system`, `/system_ex`, `/vsh`, `/dev/*`), and IPMI would need
  either several modules hosted concurrently or a convincing HLE fake of the
  peers. Neither is a NID-count problem, and the second is a design decision
  about what "booting the shell" even means.

A fair summary: the loader and CPU are not the limitation; the OS surface is.
The realistic near-term target is **more system daemons reaching their service
loops**, not a rendered PS5 home screen.

---

## Reproducing this

```powershell
$env:DOTNET_ROOT = "C:\dotnet"
dotnet build SharpEmu.slnx -c Release

$exe = "artifacts\bin\Release\net10.0\win-x64\SharpEmu.exe"
$fs  = "games\PS5_4.03_reconstructed\filesystems"
$env:SHARPEMU_STALL_WATCHDOG_SECONDS = "25"   # daemons idle-loop forever

# One target; stdout carries guest printf output, stderr the import trace.
& $exe --cpu-engine=native --log-level=debug `
    "$fs\system\vsh\SceShellCore.elf" 1> shellcore.out 2> shellcore.err
```

Targets used:

```
$fs\system\vsh\app\NPXS40100\eboot.bin      $fs\system_ex\app\NPXS40001\eboot.bin
$fs\system\vsh\app\NPXS40112\eboot.bin      $fs\system_ex\app\NPXS40028\eboot.bin
$fs\system\vsh\app\NPXS40153\eboot.bin      $fs\system\vsh\SceShellCore.elf
```

Reading the logs:

```powershell
# how deep it got
Select-String -Path shellcore.err -Pattern 'Import#(\d+)' -AllMatches

# what it could not resolve (one line per call)
Select-String -Path shellcore.err -Pattern 'unresolved: nid=(\S+)'

# guest filesystem denials
Select-String -Path shellcore.err -Pattern 'IO-FAIL'

# the guest's own messages
Select-String -Path shellcore.out -Pattern 'PRINF'
```

Static import/coverage numbers come from joining each module's import table
against `scripts/our_nids.tsv`:

```powershell
python scripts\our_nids.py        # regenerates scripts/our_nids.tsv (4255 NIDs)
python scripts\nid_resolver.py _Znwm   # symbol name -> NID
```

Two notes for anyone repeating this. Firmware import symbols are
`NID#libraryId#moduleId` — field 1 indexes `DT_SCE_IMPORT_LIB` (`0x61000049`),
field 2 indexes `DT_SCE_NEEDED_MODULE` (`0x61000045`). Getting that order
backwards silently misattributes every NID to a plausible-looking wrong library;
it was caught here by checking NIDs whose library the emulator resolves
independently (`sceNetCtlGetState` → `libSceNetCtl`, `printf` →
`libSceLibcInternal`). And `--trace-imports` is inert —
`LastImportResolutionTrace` is only ever assigned `null` — so import tracing at
`--log-level=debug` is what actually produces the `[LOADER][TRACE] Import#N`
lines.

---

## Defects found along the way

* `LibcStdioExports.Fopen` throws `ArgumentException: The value cannot be an
  empty string. (Parameter 'path')` when the guest path fails to resolve,
  instead of returning `NULL`. Reproduced by `redis-server` opening
  `/dev/urandom`. A guest calling `fopen` on any unmapped path takes an HLE
  dispatch error rather than the `NULL` the C library contract requires.
* `--strict` / `StrictDynlibResolution` is inert plumbing: it is threaded from
  the CLI into `CpuExecutionOptions` and never read.
* `Summary: ... imports=0 unique_nids=0` is always zero on the native backend —
  every `CpuSessionSummary` construction site passes literal zeros.
