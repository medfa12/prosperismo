<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# `prosperocore` crash dumps

A PS5 crash dump is the most talkative artefact in the whole corpus. It is not a
memory image with a stack trace bolted on; it is an ELF core file carrying **28 Sony
note sections**, two of which are complete console TTY logs. Those logs record the
shell narrating its own boot, naming the components it loads, in order, with timings.

Everything in this document was re-measured from the dumps in `games/coredumps/`.
Nothing from those files is committed; only short structural quotations appear here.

## How to read this

| Marker | Meaning |
|---|---|
| EXACT | Parsed straight out of a dump. The note and the byte layout are given. |
| DERIVED | Arithmetic or counting over EXACT values. |
| INFERRED | A structural reading consistent with the measurements but not stated by the artefact. |
| UNRESOLVED | Known undetermined. |

## 1. The corpus

**EXACT.** Five dumps, all from the shell process (`NPXS40087`), taken within about 31
minutes of each other on one console.

| Directory | Dump size, compressed | Decompressed | TTY lines |
|---|---|---|---|
| `NPXS40087_1697132312` | 1 061 793 | 8 257 496 | 1 233 |
| `NPXS40087_1697132610` | | 8 367 640 | 2 225 |
| `NPXS40087_1697133617` | | 8 424 088 | 3 558 |
| `NPXS40087_1697134063` | | 8 774 536 | 3 839 |
| `NPXS40087_1697134140` | | 8 411 800 | 4 087 |

Each directory holds four files:

| File | What it is |
|---|---|
| `prosperocore-<unix time>-0x<appid>-eboot.bin.prosperodmp` | the core file |
| `...prosperodmpmanifest` | plain-text key/value manifest |
| `prosperocore-systemcrash.prosperostate` | 11 960 bytes, not decoded, UNRESOLVED |
| `report.log` | one-line JSON: `{"report_log":{"app_info":{"auto send":false,"category":"gdf","reason":"0x00000001",...},"format":"1.00"}}` |

The manifest is cleartext and self-describing:

```
segmentVersion: 0x01
formatVersion: 0x10
numberOfFiles: 1
file1.name: prosperocore-1697132312-0x00000055-eboot.bin.prosperodmp
file1.attribute: 0x00
file1.size: 1061793
```

## 2. Container: LZ4 frame over ELF

**EXACT.** The `.prosperodmp` is a standard LZ4 **frame** (not a raw block) wrapping an
ELF core file. First bytes:

```
04 22 4d 18   64 40 a7   ...
^ magic       ^FLG ^BD ^HC
```

| Field | Value | Meaning |
|---|---|---|
| Magic | `0x184D2204` | LZ4 frame |
| FLG | `0x64` | version 01, blocks independent, no block checksum, content checksum present, no content size, no dict id |
| BD | `0x40` | max block size 64 KB |

`lz4.frame.decompress` on the whole file yields the ELF directly. Ratio on the
reference dump is 1 061 793 to 8 257 496, about 7.8x.

The inner ELF, EXACT:

| Field | Value |
|---|---|
| `e_ident` | `\x7fELF`, ELFCLASS64, ELFDATA2LSB, version 1 |
| `e_type` | **4, `ET_CORE`** |
| `e_machine` | **0x3E, `EM_X86_64`** |
| `e_phoff` | 0x40 |
| `e_phentsize` | 56 |
| `e_phnum` | **808** |

Program headers, DERIVED by counting: **780 `PT_LOAD` + 28 `PT_NOTE` = 808**. Identical
counts in all five dumps.

Anything that reads ELF cores reads these once the LZ4 wrapper is off. The x86-64
machine type is worth noting on its own: the shell process is not running on some
private Sony ISA, which is consistent with `docs/prospero-freebsd-hardware-constraint`
in the memory index.

## 3. The 28 note types

**EXACT.** Each `PT_NOTE` carries exactly one note. `n_name` is the note's own label
and doubles as the type name. Descriptor sizes are from the reference dump
`NPXS40087_1697134140`; they vary run to run.

| `n_type` | `n_name` | `descsz` | What it holds |
|---|---|---|---|
| 0x00501001 | `COREFILE_INFO` | 44 | binary, no strings, UNRESOLVED |
| 0x00501002 | `SYSTEM_INFO` | 7 468 | |
| 0x00501003 | `SYSTEM_INT_INFO` | 1 532 | |
| 0x00502001 | `PROCESS_INFO` | 532 | |
| 0x00502002 | `THREAD_INFO` | 96 020 | |
| 0x00502003 | `THREAD_REG_INFO` | 489 564 | register banks, the largest per-thread note |
| 0x00502004 | `MODULE_INFO` | 128 380 | loaded module list |
| 0x00502005 | `SYNC_PRIM_INFO` | 92 980 | mutexes, semaphores, event flags |
| 0x00502007 | `APP_INFO` | 2 724 | title ids of the crashing and neighbouring apps |
| 0x0050200a | `VMEM_INFO` | 301 804 | virtual memory map |
| 0x00503001 | `TTY_INFO` | 63 820 to 212 356 | **the process TTY log** |
| 0x00503002 | `SYSTEM_TTY_INFO` | 131 164 | **the system TTY log, see section 4** |
| 0x00504001 | `GPU_INFO` | 1 048 612 | exactly 1 MB plus 36 bytes |
| 0x00504002 | `GPU_META_INFO` | 76 | |
| 0x00505001 | `USER_INFO` | 140 | |
| 0x00505002 | `USERFILE_INFO` | 8 884 | |
| 0x00506002 | `VIDEOOUT_INFO` | 228 | |
| 0x00506004 | `STORAGE_INFO` | 52 | |
| 0x00506005 | `DEVICE_INFO` | 1 108 | |
| 0x00506007 | `FREQUENCY_INFO` | 10 116 | clocks |
| 0x00507001 | `EXTNL_PROC_INFO` | 20 972 | |
| 0x00507003 | `IPMI_INFO` | 111 876 | inter-process message interface state |
| 0x00507004 | `DMEM_INFO` | 25 812 | direct memory |
| 0x00507006 | `VMEM_INFO2` | 434 228 | |
| 0x00507007 | `RESOURCE_INFO` | 13 924 | |
| 0x00507008 | `EXTNL_APP_INFO` | 916 | |
| 0x00508001 | `MONOVM_LOG` | 9 900 | **a .NET/Mono crash report as XML, see section 5** |
| 0x00508004 | `SUMMARY_INFO` | 3 436 | binary, no strings, UNRESOLVED |

INFERRED from the type numbering: the high nibbles group by subsystem. `0x5010xx` is
the dump itself, `0x5020xx` the process, `0x5030xx` logging, `0x5040xx` the GPU,
`0x5050xx` the user, `0x5060xx` devices, `0x5070xx` system services, `0x5080xx` the
managed runtime.

## 4. What `SYSTEM_TTY_INFO` reveals about the shell

This is the payoff. The system TTY log is the shell talking about itself.

### 4.1 The shell is a managed application

**EXACT.** `MONOVM_LOG` is an XML `<csharp_report>` with a `<![CDATA[ ... ]]>`
unhandled-exception dump, and the type names in the TTY are .NET namespaces such as
`Sce.Vsh.ShellUI.AppMain.BootManager` and `ReactNative.Vsh.Common.RenderScene`.
JavaScript runs under JavaScriptCore inside it: exception frames read
`JSC.JSContext.Evaluate...` and the wrapper type is
`ReactNative.Vsh.Common.RenderScene+ReactApplicationRenderSceneAggregateException`.

So the shell is: **a Mono/.NET host, hosting JSC, hosting React Native.**

### 4.2 The boot state machine

**EXACT**, quoted in order from `SYSTEM_TTY_INFO`. `Sce.Vsh.ShellUI.AppMain.BootManager`
walks a named sequence, each step logged by `[SceShellUI][RunManager]`:

| # | Step |
|---|---|
| 0 | `BackgroundInitResetSurfaceRect` (phase `BootBackground4th`) |
| 1 | `EvalBase` |
| 2 | `EvalMainOnStandby` |
| 3 | `EvalRegMgrInitOK` |
| 4 | `EvalExpansionSlotWarning` |
| 5 | `EvalBackupRestore` |
| 6 | `EvalSelectResolution` |
| 7 | `EvalInitialSetup` |
| 8 | `EvalCrashReport` |
| 9 | `EvalPowerOffWarning` |
| 10 | `EvalRemoteStorageManagement` |

Steps 1 through 10 run under phase `BootMain`; step 0 runs under `BootBackground4th`,
so the background layer is initialised on a separate, earlier phase. That ordering is
consistent with `docs/ps5-background.md`: the background is up before the shell decides
what screen to show.

### 4.3 The scene graph

**EXACT.** Every scene load is logged as
`[SceShellUI] I/PSM.UI : SceneQ : Loaded[<ms>] : <instance> : <type>`. The plugin set
loads first, in this order:

```
LayoutManager, BGLayerPlugin, ProfileCachePlugin, BasePlugin, GameLayerPlugin,
DebugPlugin, VnaPlugin, NotificationPlugin, GlsPlugin2, HomeUIPlugin,
SystemModalDialogPlugin, CaptureMenuPlugin, BigAppCompanionPlugin,
UniversalCheckoutPlugin, ControlCenterPlugin, LoginMgrPlugin,
ShareVideoTranscoderPlugin
```

Then each React Native app is mounted as a **three-scene stack**, once per title id:

| Instance | Type |
|---|---|
| `Render.<TITLEID>` | `AppScene` |
| `ReactApplicationRenderScene.<TITLEID>` | `RenderScene` |
| `ReactApplicationScene.<TITLEID>` | `ReactApplicationScene` |

and once, not per app, a single container:

| Instance | Type |
|---|---|
| `ReactApplicationScene` | `ReactApplicationStackScene` |

INFERRED: `ReactApplicationStackScene` is the shared parent that all per-title
`ReactApplicationScene` instances live in, which is what makes the shell able to hold
several RN apps resident at once and switch between them without a reload.

Root scenes seen alongside these: `Sce.Vsh.ShellUI.AppSystem.LayerManager.RootScene`,
`LoginMgrRootScene`, `LaunchFlowBG`, `LaunchFlowContainer`, `OverlayScreen`.

### 4.4 Two app pools, not one

**EXACT, and it corrects a claim that was circulating as "a 4-slot TwinTurbo pool".**
There are **two** pools with different capacities, both managed by
`RNPS.ReactAppFactory`:

| Pool | Capacity | Instance naming | Log evidence |
|---|---|---|---|
| Turbo | **8** | `TurboPoolInstance1` .. `TurboPoolInstance4` observed | `Creating app from Turbo pool. ([App Pool] available: 7 \| capacity: 8 \| running: 0)` |
| TwinTurbo (TT) | **4** | `TTPoolInstance [<uuid>]` | `Creating app from TwinTurbo pool. ([App Pool] available: 3 \| capacity: 4 \| running: 0)` |

The TwinTurbo lifecycle, EXACT, in the order the log prints it:

1. `Creating app from TwinTurbo pool. ([App Pool] available: 3 | capacity: 4 | running: 0)`
2. `Delaying TT Pool population for 500ms.` The refill is deliberately deferred half a
   second so it does not compete with the app that was just taken.
3. `App created from TTPoolInstance [647a375c-...]. ([App Pool] available: 3 | capacity: 4 | running: 1)`
4. The manifest is logged field by field, see 4.5.
5. `instance promoted from TTPoolInstance [647a375c-...]` and the logger prefix changes
   from `RNPS.TTPoolInstance [uuid]` to `RNPS.<applicationName>.<TITLEID>`. A pool slot
   is a **pre-warmed anonymous instance that is renamed when a title claims it.**
6. `Populating TwinTurbo pool. ([App Pool] available: 4 | capacity: 4 | running: 1)`
   and `PreInit TTPoolInstance [<new uuid>]`.

Each pool instance owns **two named threads**, EXACT:
`SceRnJs-TTPoolInstance [<uuid>]` and `SceRnNm-TTPoolInstance [<uuid>]`. INFERRED from
the names: `Js` is the JavaScriptCore thread and `Nm` the native-modules thread. The
log prints a running `count = 13`, `14`, `15` as they are created, so the shell tracks
a global thread budget across pool instances.

### 4.5 App manifest fields

**EXACT.** Every pool instance logs its manifest. The field set is small and complete:

| Field | Example |
|---|---|
| `applicationName` | `rnps-home` |
| `applicationVersion` | `4.1.0+12349` |
| `jscGcHeapMaxSize` | `0` in every observed case |
| `enableHttpCache` | empty in every observed case |
| `titleId` | `NPXS40002` |
| `enableAccessibility` | `[textToSpeech,fontEmboldening,fontScaling]` |

`enableAccessibility` naming `fontScaling` is direct evidence that the font size scale
is a runtime-variable quantity, which is why `docs/ps5-rn-layout.md` could not find
pixel values behind `FontSizePS.*` in the bundles.

The `applicationVersion` strings are all of the form `major.minor.patch+build`:
`3.0.0+33023` (control centre), `4.1.0+12349` (home), `3.0.0+15974` (settings),
`3.0.0+8146` (notification overlay), `0.0.1+2062` (system modal dialog). EXACT.

> **Corrected.** An earlier revision of this section used those strings to claim the
> four little-endian words at `RNPSPACK` header offsets `0x64`, `0x68`, `0x6C`, `0x70`
> are major, minor, patch and build, and DERIVED from that reading that the 4.02
> `rnps-action-cards-host-app` package is version "4.2.0+45353".
>
> **That derivation does not hold.** A survey of all 19 `.epkg` packages in
> `games/rnps_4.02` shows `0x64 = 4` in **every one of them**, which cannot be a major
> version: the dump above has apps at major 0, 3 and 4 simultaneously. Worse,
> `rnps-action-cards` and `rnps-control-center` carry byte-identical
> `(0x68, 0x6C, 0x70) = (2, 0, 0xB129)` despite file sizes of 7 675 856 and 6 824 240,
> and `0x70` correlates with neither file size nor payload size across the set
> (`size / 0x70` ranges from 102 to 22 447).
>
> What survives: the version strings above are EXACT, and the `applicationName` to
> `titleId` mapping is EXACT. The meaning of `0x64` through `0x70` is **UNRESOLVED**.
> See `docs/ps5-rn-bundle-map.md`, which now carries the full 19-package survey.
>
> This is a straightforward case of a plausible pattern fitting one sample. One package
> read as "4.2.0" and one app in the dump read as "4.1.0", and that was treated as
> confirmation instead of as a coincidence to be tested against the other eighteen.

### 4.6 Titles resident at once

**EXACT**, from `MONOVM_LOG`'s `exception_data`:

| Key | Value |
|---|---|
| `ReactApplicationRenderScene.TitleId` | `NPXS40008` |
| `ReactApplicationRenderScene.LaunchedTitles` | `NPXS40011,NPXS40002,NPXS40003,NPXS40008,NPXS40024,NPXS40048,NPXS40062` |
| `ReactApplicationRenderScene.TitleInfo` | `{"titleId":"NPXS40008","applicationName":"rnps-settings","applicationVersion":"3.0.0+15974"}` |
| `ReactApplicationRenderScene.LaunchedTitleInfo` | array of the same shape, one per launched title |

Seven RN apps launched, at least four resident, against a TwinTurbo capacity of 4 and a
Turbo capacity of 8. Observed pool occupants: `reactSystemModalDialog` (NPXS40021),
`rnps-control-center` (NPXS40003), `rnps-home` (NPXS40002), `rnps-settings`
(NPXS40008).

### 4.7 Incidental findings

**EXACT**, single log lines, recorded because they are hard to obtain any other way:

| Line | Why it matters |
|---|---|
| `[SceRnpsAppMgr] BlockAppInstall(): NPXS40021(system_ex,NPXS40021) is blocked by NPXS40087(appId=0x0000a007)` | the shell holds an install lock over RN apps while it is running |
| `[SceShellUI] I/BGLayer : Enable Caesar Rendering: True` | the background layer names its renderer path "Caesar" |
| `[XFD] Alloc 2dvrCompositorIdx:4` and `[XFD] Alloc Caesar CompositorIdx:5` | the compositor allocates indexed slots, and "Caesar" is one of them |
| `[XF] Initialize XfGraphics(PGraphics)` | the graphics facade name |
| `PRINT_TIME_TICK#: 4509921 msec -- [Performance Warning] framedrop count : 54` | the shell counts its own dropped frames and warns |
| build paths `W:\Build\J01531101\vsh\shell\shell_core\...` and `W:\Build\J01531101\vsh\common_lib\lnc_util\...` | source tree layout of the native shell |
| `Sce.Vsh.ShellUI.Legacy` appears in every `RunManager` line | the boot manager lives in a component explicitly labelled legacy |

The Ghidra-facing side of `BGLayer` is in `docs/ps5-background-native.md`; the
`Enable Caesar Rendering` line is the first evidence from a second source that the
background renderer is a separately named subsystem.

## 5. How to read one

No tool is committed for this. The whole reader is short enough to state:

1. `lz4.frame.decompress(open(path,'rb').read())`.
2. Parse the ELF64 header at offset 0; program headers at `e_phoff`, `e_phnum` of them,
   56 bytes each.
3. For each `PT_NOTE`, walk `(u32 namesz, u32 descsz, u32 type)` followed by the name
   padded to 4 bytes and the descriptor padded to 4 bytes.
4. `TTY_INFO` and `SYSTEM_TTY_INFO` are the text ones. `MONOVM_LOG` is XML.

## 6. Still unknown

| Item | Status |
|---|---|
| Descriptor layouts for all 26 binary notes | UNRESOLVED. Only the two TTY notes and `MONOVM_LOG` were read; the rest were counted and sized, not parsed. |
| `prosperocore-systemcrash.prosperostate`, 11 960 bytes | UNRESOLVED, format not identified |
| `COREFILE_INFO` (44 B) and `SUMMARY_INFO` (3 436 B) | UNRESOLVED, contain no printable strings |
| `GPU_INFO` being exactly 1 MB + 36 | INFERRED to be a fixed-size ring or register snapshot plus a header; not confirmed |
| Whether the note set is fixed | All five dumps carry the same 28 notes, but all five are the same process on one console. UNRESOLVED for game processes. |
| Which app id maps to which `0x000000NN` in the filename | The reference dump is `0x00000055` for `NPXS40087`; the log separately prints `appId=0x0000a007` for the same title. Two different id spaces, relationship UNRESOLVED. |

### Particle-state recovery audit

The five decompressed cores were also searched for the exact 4.03
`large_compute[0/1]` and `large_draw[0/1]` resource tails and for multiple
256-byte windows from the recovered 4.03 `particle_c` shader text. No match was
present in any `PT_LOAD` segment or note payload. Therefore these dumps cannot
currently be used as the live allocator/property-buffer seed for the 4.03
native particle probe. Their GPU note may describe a different firmware build
or capture form, but treating its opaque bytes as 4.03 particle state would be
an invention. This negative result does not change the accepted bank-1 frame
sequence; it only leaves the bank-0 allocator seed unresolved.
