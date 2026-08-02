# OS surface gap — how much of the Prospero export surface SharpEmu actually has

**Generated. Do not hand-edit. Reproduce every number with `python3 scripts/nid_gap.py`.**

## 1. The five buckets

| bucket | unique NIDs | TSV rows | meaning |
|---|---:|---:|---|
| MISSING | 13892 | 17112 | firmware exports it; we register nothing for that NID |
| STUBBED | 985 | 1277 | we register it, but the body is a stub / notimplemented / unclassifiable |
| MISPLACED | 178 | 220 | we register it and it does work, but under a library firmware disagrees with |
| PHANTOM | 249 | 249 | we register a NID that **no** cleartext firmware module exports |
| COVERED | 2849 | 4775 | we register it, it does work, library matches |

The script also writes `scripts/nid_gap.tsv` (one row per `(module_file, library, nid)`) and
runs 13 internal consistency assertions on the partition above — it prints each one, exits
non-zero and says the numbers are untrustworthy if any fails. Sources joined:
`scripts/fw_exports.tsv` (PS5 4.03 cleartext export tables), `scripts/our_nids.tsv`
(SharpEmu's `[SysAbiExport]` registry), `scripts/astro_import_routing.tsv` (a shipped
title's 1732-row import surface), and a re-parse of the 589 firmware ELFs for
`DT_SCE_IMPORT_LIB`. `inspiration/` was not used as authority anywhere. Numbers are tagged
**EXTRACTED** (read from ground truth), **DIFFERENTIAL** (computed by joining two
ground-truth sources) or **ASSUMED** (judgement — a liability).

**DIFFERENTIAL.** Bucketing is per NID, because SharpEmu's dispatch table is keyed on the
NID alone (`src/SharpEmu.HLE/ModuleManager.cs:33`, `_dispatchTable.TryAdd(export.Nid, …)`),
so one NID cannot have two verdicts. Every TSV row for a NID repeats its verdict; the
`primary` column marks exactly one row per NID.

Precedence, applied in this order: no firmware export anywhere → PHANTOM; not registered →
MISSING; registered but `kind` is `stub`/`notimplemented`/`unknown` → STUBBED; library
mismatch → MISPLACED; otherwise COVERED. The 123 `kind=unknown` registrations inside the
scored scope are counted as STUBBED **on purpose** — that is the conservative reading and
it is the largest single lever on the headline: if every one of them turned out to be a
correct implementation the headline would rise from 15.91% to 16.60%.

### Headline

```
COVERED, in the scored scope     2849
scored scope (denominator)      17904
                               ------
coverage                        15.91%
```

**15.91%** — DIFFERENTIAL. Numerator 2849, denominator 17904.

**Denominator = every distinct NID exported by a module in `filesystems/system/common/lib/`** — 17904 NIDs across 23384 rows and 240 modules. EXTRACTED.

What is excluded from the denominator, and why:

- **`system_ex/common_ex/lib/` (261494 export rows).** This is the shell/web-runtime library
  set: the managed .NET assemblies (`mscorlib`, `System.Reactive.Linq`, …), `libSceNKWebKit`,
  `Sce.Vsh.ShellUI.*`. A title's `DT_SCE_NEEDED_MODULE` never names these, and 80% of the
  firmware's entire export surface is managed-code metadata that is never dispatched by NID.
  Counting it would make the denominator 283419 and the headline **1.01%** — a number that
  measures nothing an emulator can act on.
- **`system/priv/lib/`, `vsh/app/*`, `app/NPXS*` (12481 export rows).** Privileged and
  per-system-app modules. Not on a title's link path.
- **The 38 still-encrypted modules** (`scripts/fw_encrypted.txt`). They export nothing
  measurable. Only 4 of them are in the SDK directory (`libSceDbg.sprx`, `libSceDbgAssist.sprx`, `libSceWorkspace.sprx`, `libc.sprx`), so the sealed surface cannot be
  large; but the denominator is a floor, not a ceiling.

What is **not** excluded, deliberately, even though excluding it would flatter the number:

- Libraries in the SDK directory that no game plausibly calls (`libSceAgcVsh`, `libSceAbstract*`,
  `libSceIpmi`, `libSceRegMgr`, …). They are on the title link path; dropping them is judgement,
  and the ranked list below already de-prioritises them via the tier column.
- C++ mangled exports (`libSceJson`, `libSceFreeType`, `libSceCes`). Games really do call these.
- PHANTOM NIDs. They are **not** in the denominator and **not** in the numerator, so inventing
  surface cannot move this metric up.

### The same measurement, three other ways

| view | numerator | denominator | value |
|---|---:|---:|---:|
| headline: COVERED / SDK-directory NIDs | 2849 | 17904 | **15.91%** |
| …counting MISPLACED as covered (dispatch is NID-keyed, so these do work today) | 3027 | 17904 | 16.91% |
| …counting every registration regardless of quality | 4012 | 17904 | 22.41% |
| COVERED / every NID in the whole cleartext firmware | 2849 | 283419 | 1.01% |

### Coverage of a real title's import surface

Astro Bot imports 1732 NIDs. Against this firmware:

| verdict for the NIDs Astro imports | count |
|---|---:|
| COVERED | 603 |
| MISPLACED | 88 |
| STUBBED | 12 |
| MISSING | 220 |
| PHANTOM | 7 |
| NOT_IN_FW | 802 |

923 of the 1732 are exported by some cleartext 4.03 module at all; 603 of those are COVERED (65.33%). EXTRACTED + DIFFERENTIAL.

A consistency check worth stating, because it decides how to read that file. Cross-tabbing
its routing column against "is this NID in our `[SysAbiExport]` registry":

| routing | registered by us | not registered |
|---|---:|---:|
| HLE | 691 | 0 |
| LLE | 13 | 917 |
| UNSERVED | 6 | 105 |

`HLE` is exactly "SharpEmu has an export" — it says nothing about whether the export does
anything. 11 of those 691 are stubs by our own classifier.

---

## 2. Ranked burn-down — top 100 of 14877 MISSING+STUBBED in scope

Score = Astro-import weight + firmware centrality + tier. Explicitly:

```
astro   1000 if Astro imports it and routing==UNSERVED   (title calls it, nothing serves it)
         900 if Astro imports it and routing==HLE        (title calls it, we answer with a stub)
         700 if Astro imports it and routing==LLE        (title calls it, currently sent to firmware)
           0 if no title in our ground truth imports it
centrality  min(distinct firmware modules importing that library, 100)   EXTRACTED
tier        60 * {3 load-bearing, 2 supporting, 1 other, 0 shell/system-only}   ASSUMED
```

The centrality term is measured, not guessed: 589 cleartext modules were re-parsed and their
`DT_SCE_IMPORT_LIB` entries counted (`libSceLibcInternal` 545 importers, `libkernel` 487,
`libSceSysmodule` 105, `libSceVideoOut` 39, …). The tier term is the one ASSUMED input; the
TSV carries `fw_importers` separately so the ranking can be redone without it.

| # | score | bucket | library | NID | firmware symbol | Astro | our kind |
|---:|---:|---|---|---|---|---|---|
| 1 | 1280 | MISSING | libkernel | `QzB4O+bJQyA` | `sceKernelAprResolveFilepathsToIdsAndFileSizesForEach` | UNSERVED | — |
| 2 | 1280 | MISSING | libkernel | `eYAh2vlCY-U` | `sceKernelAprResolveFilepathsToIdsForEach` | UNSERVED | — |
| 3 | 1280 | MISSING | libkernel | `i3HWvW35jao` | `sceKernelAprResolveFilepathsWithPrefixToIds` | UNSERVED | — |
| 4 | 1280 | MISSING | libkernel | `C+Khtbbx2g8` | `sceKernelAprResolveFilepathsWithPrefixToIdsAndFileSizesForEach` | UNSERVED | — |
| 5 | 1280 | MISSING | libkernel | `VB-BtuIW8Xc` | `sceKernelAprResolveFilepathsWithPrefixToIdsForEach` | UNSERVED | — |
| 6 | 1193 | STUBBED | libSceAgc | `Abendgtz+3o` | `sceAgcCbDispatchGetSize` | UNSERVED | unknown |
| 7 | 1189 | MISSING | libSceAudioIn | `X+4jdIS75P0` | _(no name)_ | UNSERVED | — |
| 8 | 1181 | MISSING | libSceNpSessionSignaling | `aBuX0PX-T7I` | `sceNpSessionSignalingCreateContext2` | UNSERVED | — |
| 9 | 1181 | MISSING | libSceNpUniversalDataSystem | `XY14n3jNIpE` | `sceNpUniversalDataSystemEventPropertyArraySetObject` | UNSERVED | — |
| 10 | 1181 | MISSING | libSceNpUniversalDataSystem | `Fidd8vWgyVE` | `sceNpUniversalDataSystemEventPropertyObjectSetBool` | UNSERVED | — |
| 11 | 1181 | MISSING | libSceNpUniversalDataSystem | `56QLTqx911s` | `sceNpUniversalDataSystemEventPropertyObjectSetInt64` | UNSERVED | — |
| 12 | 1181 | MISSING | libSceNpUniversalDataSystem | `AzD4irAcKE4` | `sceNpUniversalDataSystemEventPropertyObjectSetUInt32` | UNSERVED | — |
| 13 | 1181 | MISSING | libSceNpUniversalDataSystem | `xvsP5Yz6FmY` | `sceNpUniversalDataSystemEventPropertyObjectSetUInt64` | UNSERVED | — |
| 14 | 1180 | STUBBED | libkernel | `pO96TwzOm5E` | `sceKernelGetDirectMemorySize` | HLE | stub |
| 15 | 1180 | STUBBED | libkernel | `4J2sUJmuHZQ` | `sceKernelGetProcessTime` | HLE | unknown |
| 16 | 1180 | STUBBED | libkernel | `BNowx2l588E` | `sceKernelGetProcessTimeCounterFrequency` | HLE | stub |
| 17 | 1180 | STUBBED | libkernel | `1j3S3n-tTW4` | `sceKernelGetTscFrequency` | HLE | stub |
| 18 | 1180 | STUBBED | libkernel | `T72hz6ffq08` | `scePthreadYield` | HLE | unknown |
| 19 | 1180 | STUBBED | libSceLibcInternal | `bzQExy189ZI` | `_init_env` | HLE | stub |
| 20 | 1135 | MISSING | libSceJson2 | `9yLjn46Ypfs` | `_ZN3sce4Json5Array8iteratorD1Ev` | UNSERVED | — |
| 21 | 1135 | MISSING | libSceJson2 | `w5+VCznos5E` | `_ZN3sce4Json5Array8iteratorppEv` | UNSERVED | — |
| 22 | 1135 | MISSING | libSceJson2 | `hoINmSMlYjI` | `_ZN3sce4Json6Object8iteratorD1Ev` | UNSERVED | — |
| 23 | 1135 | MISSING | libSceJson2 | `DlWmn2ZQuWY` | `_ZN3sce4Json6Object8iteratorppEv` | UNSERVED | — |
| 24 | 1135 | MISSING | libSceJson2 | `WXF2ihRF+B8` | `_ZNK3sce4Json5Array3endEv` | UNSERVED | — |
| 25 | 1135 | MISSING | libSceJson2 | `bcH5EnFE2xY` | `_ZNK3sce4Json5Array5beginEv` | UNSERVED | — |
| 26 | 1135 | MISSING | libSceJson2 | `9uP25i6ipno` | `_ZNK3sce4Json5Array5emptyEv` | UNSERVED | — |
| 27 | 1135 | MISSING | libSceJson2 | `wcgr5mte7T8` | `_ZNK3sce4Json5Array8iteratordeEv` | UNSERVED | — |
| 28 | 1135 | MISSING | libSceJson2 | `5AZPp99ogrc` | `_ZNK3sce4Json5Array8iteratorneERKS2_` | UNSERVED | — |
| 29 | 1135 | MISSING | libSceJson2 | `ivMCitpSQNk` | `_ZNK3sce4Json6Object3endEv` | UNSERVED | — |
| 30 | 1135 | MISSING | libSceJson2 | `xhAcaIwnrgk` | `_ZNK3sce4Json6Object5beginEv` | UNSERVED | — |
| 31 | 1135 | MISSING | libSceJson2 | `ZCd6IYoD3Bc` | `_ZNK3sce4Json6Object8iteratordeEv` | UNSERVED | — |
| 32 | 1135 | MISSING | libSceJson2 | `+isUKw4zud4` | `_ZNK3sce4Json6Object8iteratorneERKS2_` | UNSERVED | — |
| 33 | 1135 | MISSING | libSceJson2 | `wM4LO2iK3s8` | `_ZNK3sce4Json6String5emptyEv` | UNSERVED | — |
| 34 | 1135 | MISSING | libSceJson2 | `VbFjEs--uiA` | `_ZNK3sce4Json6StringeqEPKc` | UNSERVED | — |
| 35 | 1127 | STUBBED | libSceSystemService | `Vo5V8KAwCmk` | `sceSystemServiceHideSplashScreen` | HLE | unknown |
| 36 | 1123 | MISSING | libSceAcm | `8fe55ktlNVo` | `sceAcmBatchStartBuffers` | UNSERVED | — |
| 37 | 1123 | MISSING | libSceAcm | `RLN3gRlXJLE` | `sceAcmBatchWait` | UNSERVED | — |
| 38 | 1123 | MISSING | libSceAcm | `u70oWo92SYQ` | `sceAcm_ConvReverb_SharedInput` | UNSERVED | — |
| 39 | 1123 | MISSING | libSceShare | `ErH6tKS7fzE` | `sceShareCaptureScreenshot` | UNSERVED | — |
| 40 | 1123 | MISSING | libSceShare | `GQTObcITIXI` | `sceShareCaptureScreenshotExtended` | UNSERVED | — |
| 41 | 1120 | MISSING | libSceAmpr | `Eul7AGEpjLo` | `sceAmprAprCommandBufferMapBegin` | UNSERVED | — |
| 42 | 1120 | MISSING | libSceAmpr | `bFEs0Gs6D2A` | `sceAmprAprCommandBufferMapDirectBegin` | UNSERVED | — |
| 43 | 1120 | MISSING | libSceAmpr | `X169CE6G3Y4` | `sceAmprAprCommandBufferMapEnd` | UNSERVED | — |
| 44 | 1120 | MISSING | libSceAmpr | `mZSbNJVJpV8` | `sceAmprAprCommandBufferReadFileGather` | UNSERVED | — |
| 45 | 1120 | MISSING | libSceAmpr | `BVmR1H8l+XI` | `sceAmprAprCommandBufferReadFileGatherScatter` | UNSERVED | — |
| 46 | 1120 | MISSING | libSceAmpr | `Jg-AgkdJHkk` | `sceAmprAprCommandBufferReadFileScatter` | UNSERVED | — |
| 47 | 1120 | MISSING | libSceAmpr | `YPxkUDhgoNI` | `sceAmprAprCommandBufferResetGatherScatterState` | UNSERVED | — |
| 48 | 1120 | MISSING | libSceAmpr | `4UkZbYKVF7c` | `sceAmprCommandBufferConstructMarker` | UNSERVED | — |
| 49 | 1120 | MISSING | libSceAmpr | `GmOguNIsuKk` | `sceAmprCommandBufferConstructNop` | UNSERVED | — |
| 50 | 1120 | MISSING | libSceAmpr | `RPCAhx-aabE` | `sceAmprCommandBufferGetBufferBaseAddress` | UNSERVED | — |
| 51 | 1120 | MISSING | libSceAmpr | `VEDMaQmJZng` | `sceAmprCommandBufferGetType` | UNSERVED | — |
| 52 | 1120 | MISSING | libSceAmpr | `tNn5WBkta60` | `sceAmprCommandBufferNop` | UNSERVED | — |
| 53 | 1120 | MISSING | libSceAmpr | `pFQ9UHpO52s` | `sceAmprCommandBufferNopWithData` | UNSERVED | — |
| 54 | 1120 | MISSING | libSceAmpr | `mv0O8Zg0woU` | `sceAmprCommandBufferPopMarker` | UNSERVED | — |
| 55 | 1120 | MISSING | libSceAmpr | `dXPaz65HNmk` | `sceAmprCommandBufferPushMarker` | UNSERVED | — |
| 56 | 1120 | MISSING | libSceAmpr | `f12ObAMEi9A` | `sceAmprCommandBufferPushMarkerWithColor` | UNSERVED | — |
| 57 | 1120 | MISSING | libSceAmpr | `4quckD2y7Pg` | `sceAmprCommandBufferSetMarker` | UNSERVED | — |
| 58 | 1120 | MISSING | libSceAmpr | `sWbST0oQKsc` | `sceAmprCommandBufferSetMarkerWithColor` | UNSERVED | — |
| 59 | 1120 | MISSING | libSceAmpr | `DLfoNxTFNVk` | `sceAmprCommandBufferWaitOnAddress_04_00` | UNSERVED | — |
| 60 | 1120 | MISSING | libSceAmpr | `cQb8Zr8Q0Y0` | `sceAmprCommandBufferWaitOnCounter_04_00` | UNSERVED | — |
| 61 | 1120 | MISSING | libSceAmpr | `enZm-6GjWqw` | `sceAmprCommandBufferWriteAddressFromCounterPair_04_00` | UNSERVED | — |
| 62 | 1120 | MISSING | libSceAmpr | `t4ExS+SwLjs` | `sceAmprCommandBufferWriteAddressFromCounter_04_00` | UNSERVED | — |
| 63 | 1120 | MISSING | libSceAmpr | `bt3LHR9xjK4` | `sceAmprCommandBufferWriteAddressFromTimeCounter_04_00` | UNSERVED | — |
| 64 | 1120 | MISSING | libSceAmpr | `jK+yuYCI7MA` | `sceAmprCommandBufferWriteCounter_04_00` | UNSERVED | — |
| 65 | 1120 | MISSING | libSceAmpr | `kdFImtTD0hc` | `sceAmprMeasureCommandSizeMapBegin` | UNSERVED | — |
| 66 | 1120 | MISSING | libSceAmpr | `qvbdJc7bG+s` | `sceAmprMeasureCommandSizeMapDirectBegin` | UNSERVED | — |
| 67 | 1120 | MISSING | libSceAmpr | `iwTNhyaemnw` | `sceAmprMeasureCommandSizeMapEnd` | UNSERVED | — |
| 68 | 1120 | MISSING | libSceAmpr | `NNIZ-FMyz3M` | `sceAmprMeasureCommandSizeNop` | UNSERVED | — |
| 69 | 1120 | MISSING | libSceAmpr | `Xp85BP3+BBI` | `sceAmprMeasureCommandSizeNopWithData` | UNSERVED | — |
| 70 | 1120 | MISSING | libSceAmpr | `pbnNnahE8vk` | `sceAmprMeasureCommandSizePopMarker` | UNSERVED | — |
| 71 | 1120 | MISSING | libSceAmpr | `0RdLmAh7WVo` | `sceAmprMeasureCommandSizePushMarker` | UNSERVED | — |
| 72 | 1120 | MISSING | libSceAmpr | `3OfeY4pzDV0` | `sceAmprMeasureCommandSizePushMarkerWithColor` | UNSERVED | — |
| 73 | 1120 | MISSING | libSceAmpr | `qesF88X4DRg` | `sceAmprMeasureCommandSizeReadFileGather` | UNSERVED | — |
| 74 | 1120 | MISSING | libSceAmpr | `DXmgc5op8Yw` | `sceAmprMeasureCommandSizeReadFileGatherScatter` | UNSERVED | — |
| 75 | 1120 | MISSING | libSceAmpr | `7nXGDGMXSqo` | `sceAmprMeasureCommandSizeReadFileScatter` | UNSERVED | — |
| 76 | 1120 | MISSING | libSceAmpr | `rddQYXM0CjM` | `sceAmprMeasureCommandSizeResetGatherScatterState` | UNSERVED | — |
| 77 | 1120 | MISSING | libSceAmpr | `VGkEj4d6-Kg` | `sceAmprMeasureCommandSizeSetMarker` | UNSERVED | — |
| 78 | 1120 | MISSING | libSceAmpr | `tmfr97+ED5I` | `sceAmprMeasureCommandSizeSetMarkerWithColor` | UNSERVED | — |
| 79 | 1120 | MISSING | libSceAmpr | `0BMj1hgG+kE` | `sceAmprMeasureCommandSizeWaitOnAddress_04_00` | UNSERVED | — |
| 80 | 1120 | MISSING | libSceAmpr | `ClnsFLLLcss` | `sceAmprMeasureCommandSizeWaitOnCounter_04_00` | UNSERVED | — |
| 81 | 1120 | MISSING | libSceAmpr | `2Hw8gjMdwSY` | `sceAmprMeasureCommandSizeWriteAddressFromCounterPair_04_00` | UNSERVED | — |
| 82 | 1120 | MISSING | libSceAmpr | `JYd9g9L+TmE` | `sceAmprMeasureCommandSizeWriteAddressFromCounter_04_00` | UNSERVED | — |
| 83 | 1120 | MISSING | libSceAmpr | `gAtc79UTt5E` | `sceAmprMeasureCommandSizeWriteAddressFromTimeCounter_04_00` | UNSERVED | — |
| 84 | 1120 | MISSING | libSceAmpr | `I-Qm+MEso5c` | `sceAmprMeasureCommandSizeWriteCounter_04_00` | UNSERVED | — |
| 85 | 1120 | MISSING | libSceContentExport | `tb3cZTCl8Ps` | `sceContentExportFinish` | UNSERVED | — |
| 86 | 1120 | MISSING | libSceContentExport | `AOWqIYsgVHs` | `sceContentExportFromData` | UNSERVED | — |
| 87 | 1120 | MISSING | libSceContentExport | `FCygF4Ec4so` | `sceContentExportStart` | UNSERVED | — |
| 88 | 1120 | MISSING | libSceVoiceChat | `hR8-CKMl2JQ` | `sceVoiceChatCreateRequest` | UNSERVED | — |
| 89 | 1120 | MISSING | libSceVoiceChat | `eEMpsX1fGHU` | `sceVoiceChatDeleteRequest` | UNSERVED | — |
| 90 | 1120 | MISSING | libSceVoiceChat | `spdS-hedavE` | `sceVoiceChatInitialize` | UNSERVED | — |
| 91 | 1120 | MISSING | libSceVoiceChat | `CscDZAFA5+c` | `sceVoiceChatProcessEvent` | UNSERVED | — |
| 92 | 1120 | MISSING | libSceVoiceChat | `Ptmkf9UnWBg` | `sceVoiceChatRegisterHandlers` | UNSERVED | — |
| 93 | 1120 | MISSING | libSceVoiceChat | `ajXKK3BOVc8` | `sceVoiceChatRegisterMicEventHandler` | UNSERVED | — |
| 94 | 1120 | MISSING | libSceVoiceChat | `y7MgGX889Mo` | `sceVoiceChatRequestCreateGameSessionVoiceChatChannel` | UNSERVED | — |
| 95 | 1120 | MISSING | libSceVoiceChat | `sW3km27c12M` | `sceVoiceChatRequestCreatePlayerSessionVoiceChatChannel` | UNSERVED | — |
| 96 | 1120 | MISSING | libSceVoiceChat | `7bu++dneYUU` | `sceVoiceChatRequestDeleteGameSessionVoiceChatChannel` | UNSERVED | — |
| 97 | 1120 | MISSING | libSceVoiceChat | `zbKF-ejbR0Q` | `sceVoiceChatRequestDeletePlayerSessionVoiceChatChannel` | UNSERVED | — |
| 98 | 1120 | MISSING | libSceVoiceChat | `hpG+mR4EbpE` | `sceVoiceChatRequestJoinGameSessionVoiceChatChannel` | UNSERVED | — |
| 99 | 1120 | MISSING | libSceVoiceChat | `X3BWlTuErbk` | `sceVoiceChatRequestJoinPlayerSessionVoiceChatChannel` | UNSERVED | — |
| 100 | 1120 | MISSING | libSceVoiceChat | `S+mOdmysfhw` | `sceVoiceChatRequestLeaveGameSessionVoiceChatChannel` | UNSERVED | — |

Full list: `scripts/nid_gap.tsv`, e.g.

```
awk -F'\t' 'NR==1||($3=="1"&&($2=="MISSING"||$2=="STUBBED"))' scripts/nid_gap.tsv \
  | sort -t$'\t' -k18,18nr | head -400
```

### Where the work is, by library

Keyed on the **firmware's** library name, over the 19276 distinct `(library, nid)` pairs the
SDK directory exports. A NID exported by two libraries is counted under both, so this table
sums to 19276 rather than to the 17904-NID denominator. `fw importers` is EXTRACTED: distinct
cleartext firmware modules whose `DT_SCE_IMPORT_LIB` names that library. Top 40 by workload.

| library | exported (lib,nid) | COVERED | STUBBED | MISSING | MISPLACED | fw importers | tier |
|---|---:|---:|---:|---:|---:|---:|---:|
| libSceLibcInternal | 3030 | 53 | 5 | 2895 | 77 | 545 | 3 |
| libkernel | 1389 | 451 | 56 | 869 | 13 | 487 | 3 |
| libSceUserService | 554 | 415 | 5 | 134 | 0 | 50 | 3 |
| libSceNpCommon | 1211 | 4 | 0 | 1183 | 24 | 22 | 3 |
| libSceNpManager | 505 | 43 | 5 | 457 | 0 | 33 | 3 |
| libSceLibreSSl3 | 488 | 0 | 2 | 486 | 0 | 14 | 2 |
| libScePosix | 197 | 142 | 13 | 39 | 3 | 11 | 3 |
| libSceNet | 234 | 115 | 97 | 22 | 0 | 54 | 2 |
| libSceFont | 229 | 105 | 124 | 0 | 0 | 11 | 2 |
| libSceNpTus | 142 | 139 | 3 | 0 | 0 | 0 | 3 |
| libSceLibreSSL | 399 | 0 | 2 | 397 | 0 | 14 | 2 |
| libSceJson2 | 223 | 41 | 0 | 137 | 45 | 15 | 2 |
| libSceAgc | 219 | 80 | 15 | 123 | 1 | 13 | 3 |
| libSceAgcVsh | 216 | 79 | 15 | 121 | 1 | 12 | 0 |
| libSceFreeType | 375 | 0 | 0 | 375 | 0 | 2 | 2 |
| libSceHttp | 115 | 106 | 9 | 0 | 0 | 29 | 2 |
| libSceVideoOut | 208 | 36 | 0 | 172 | 0 | 39 | 3 |
| libSceCes | 273 | 0 | 2 | 271 | 0 | 0 | 1 |
| libSceSaveData | 117 | 63 | 26 | 28 | 0 | 1 | 3 |
| libSceShellCoreUtil | 239 | 0 | 0 | 239 | 0 | 20 | 0 |
| libSceJxr | 225 | 0 | 0 | 225 | 0 | 3 | 1 |
| libScePad | 164 | 28 | 62 | 74 | 0 | 18 | 3 |
| libSceNpWebApi | 94 | 52 | 4 | 38 | 0 | 5 | 3 |
| libSceHmd2 | 191 | 0 | 0 | 191 | 0 | 8 | 1 |
| libSceMediaFrameworkInterface | 188 | 0 | 0 | 188 | 0 | 10 | 1 |
| libSceNpMatching2 | 69 | 58 | 0 | 11 | 0 | 3 | 3 |
| libSceXml | 184 | 0 | 0 | 184 | 0 | 1 | 1 |
| libSceHmd | 178 | 0 | 0 | 178 | 0 | 11 | 1 |
| libSceJson | 96 | 40 | 0 | 56 | 0 | 0 | 2 |
| libSceNgs2 | 66 | 55 | 11 | 0 | 0 | 7 | 3 |
| libSceGnmDriver | 168 | 0 | 0 | 168 | 0 | 11 | 3 |
| libSceSystemService | 108 | 27 | 53 | 28 | 0 | 47 | 3 |
| libSceHttp2 | 56 | 53 | 3 | 0 | 0 | 13 | 2 |
| libSceNpUniversalDataSystem | 115 | 23 | 0 | 92 | 0 | 1 | 3 |
| libSceAppInstUtil | 158 | 0 | 0 | 158 | 0 | 4 | 0 |
| libSceUlt | 64 | 47 | 1 | 16 | 0 | 0 | 2 |
| libSceAmpr | 117 | 20 | 0 | 97 | 0 | 0 | 2 |
| libSceAudioOut2 | 107 | 22 | 1 | 84 | 0 | 6 | 2 |
| libSceNpScore | 51 | 48 | 3 | 0 | 0 | 0 | 3 |
| libSceNpTrophy | 110 | 15 | 67 | 28 | 0 | 2 | 3 |

### Libraries in the SDK directory with **zero** registrations

232 of the 417 libraries the SDK directory exports have not a single NID in our registry —
6151 `(library, nid)` pairs, 31.9% of the scored surface. Top 25 by size, with tier:

| library | exported (lib,nid) | fw importers | tier |
|---|---:|---:|---:|
| libSceFreeType | 375 | 2 | 2 |
| libSceShellCoreUtil | 239 | 20 | 0 |
| libSceJxr | 225 | 3 | 1 |
| libSceHmd2 | 191 | 8 | 1 |
| libSceMediaFrameworkInterface | 188 | 10 | 1 |
| libSceXml | 184 | 1 | 1 |
| libSceHmd | 178 | 11 | 1 |
| libSceGnmDriver | 168 | 11 | 3 |
| libSceAppInstUtil | 158 | 4 | 0 |
| libSceAvSetting | 140 | 16 | 0 |
| libSceAbstractStorage | 139 | 6 | 0 |
| libScePlayReady4 | 139 | 3 | 0 |
| libSceComposite | 133 | 15 | 0 |
| libSceCustomMusicSysCallWrapper | 106 | 0 | 1 |
| libSceNpUtility | 102 | 4 | 3 |
| libSceMbus | 101 | 23 | 0 |
| libSceBgft | 97 | 1 | 0 |
| libSceIpmi | 93 | 100 | 0 |
| libSceCamera | 91 | 11 | 1 |
| libSceLncUtil | 84 | 27 | 1 |
| libSceSysCore | 83 | 20 | 0 |
| libSceCustomMusicPlayReady4 | 82 | 0 | 0 |
| libSceAbstractYoutube | 80 | 1 | 0 |
| libSceVnaInternal | 78 | 3 | 0 |
| libwvoec | 77 | 1 | 0 |

---

## 3. PHANTOM analysis — 249 NIDs we register that no cleartext module exports

| sub-class | NIDs | reading |
|---|---:|---|
| `ENCRYPTED_MODULE` | 2 | the library's module is present but still sealed (magic `5414f5ee`), so its exports are unmeasurable. Innocent. |
| `ABSENT_FROM_DUMP` | 18 | no cleartext module exports a library by that name and no file under `filesystems/` carries it — game-shipped `.prx`, middleware, or a library this dump lacks. Plausible but unverifiable here. |
| `VERSION_SKEW_ATTESTED` | 4 | a shipped title's import table names this exact NID, so it exists in *some* firmware — 4.03 is simply older. Innocent. |
| `GEN4_CLAIM_UNVERIFIABLE` | 214 | declared `Gen4 \| Gen5`. The library is cleartext here and has no such NID, so the **Gen5 half of the claim is false**; the Gen4 half cannot be checked because this tree holds no PS4 firmware. A partial excuse, not a clean one. |
| `NO_INNOCENT_EXPLANATION` | 11 | declared `Gen5` only, the library **is** present and cleartext in this firmware, and it exports no such NID. Nothing excuses these. |

A phantom can satisfy more than one of these; each is tested in a fixed order and the first
hit wins, most-innocent-first. Each test is mechanical:

1. does any cleartext module's **import** table name this NID? → `IMPORTED_BUT_UNEXPORTED`
2. is a module whose basename matches the claimed library listed in
   `scripts/fw_encrypted.txt`? → `ENCRYPTED_MODULE`
3. does *no* cleartext module export a library of that name? → `ABSENT_FROM_DUMP`
   (the note column records whether any file under `filesystems/` carries the name)
4. is the NID in `scripts/astro_import_routing.tsv`? → `VERSION_SKEW_ATTESTED`
5. does the declaration also claim `Gen4`? → `GEN4_CLAIM_UNVERIFIABLE`
6. otherwise → `NO_INNOCENT_EXPLANATION`

Test (1) is worth stating on its own, because it is the strongest available check and it
came back empty. Every one of the 589 cleartext modules was re-parsed and **all** of their
undefined NID symbols collected — 10156 distinct imported NIDs. **0 of the 249 phantoms appear
there.** So inside this firmware nothing exports these NIDs *and nothing calls them either*.
A sealed module can hide an exporter, but it cannot hide 589 modules' worth of callers.

**Every registration in the tree asserts `Gen5`.** The `Target =` of all 4261 registrations,
read back out of the attribute sites:

| `Target` | registrations |
|---|---:|
| `Generation.Gen4 \| Generation.Gen5` | 2677 |
| `Generation.Gen5` | 1553 |
| `Generation.Gen4` | 31 |

There is no `Generation.Gen4`-only declaration anywhere. So "it's PS4 surface" is never a
*complete* excuse for a phantom: every one of them asserts that **PS5** exports it.

### `ENCRYPTED_MODULE` — 2

| claimed library | NIDs | stubs | example |
|---|---:|---:|---|
| libc | 2 | 0 | `XKRegsFpEpk` catchReturnFromMain |

### `ABSENT_FROM_DUMP` — 18

| claimed library | NIDs | stubs | example |
|---|---:|---:|---|
| libScePsml | 9 | 0 | `gxv3i+MTEzU` scePsmlMfsrCreateContext800M3_2 |
| libSceNpCppWebApi | 2 | 0 | `UYPxv8MIzGo` _ZN3sce2Np9CppWebApi6Common10initializeERKNS2_10InitParamsERNS2_10LibContextE |
| libunity | 2 | 2 | `35NoyMOtYpE` SetDataFolder |
| libSceDbgPlayGo | 2 | 2 | `uEqMfMITvEI` sceDbgPlayGoRequestNextChunk |
| libfmod | 1 | 0 | `uPLTdl3psGk` FmodSystemSetOutput |
| libil2cpp | 1 | 0 | `cJ2Y4E-t258` il2cpp_api_register_symbols |
| libSceAudioOutSparkControl | 1 | 1 | `Mt7JB3lOyJk` sceAudioOutSparkControlSetEqCoef |

### `VERSION_SKEW_ATTESTED` — 4

| claimed library | NIDs | stubs | example |
|---|---:|---:|---|
| libSceAgc | 3 | 1 | `-KRzWekV120` sceAgcDriverUnknown_KRzWekV120 |
| libKernel | 1 | 1 | `mkgXxsoxWHg` sceKernelClearVirtualRangeName |

### `GEN4_CLAIM_UNVERIFIABLE` — 214

| claimed library | NIDs | stubs | example |
|---|---:|---:|---|
| libSceSsl | 161 | 161 | `Pgt0gg14ewU` CA_MGMT_allocCertDistinguishedName |
| libSceAudioOut | 23 | 20 | `Iz9X7ISldhs` sceAudioOutA3dControl |
| libSceAudio3d | 6 | 1 | `uJ0VhGcxCTQ` sceAudio3dPortFreeState |
| libSceAppContent | 4 | 1 | `9Gq5rOkWzNU` sceAppContentSmallSharedDataFormat |
| libKernel | 4 | 1 | `Ac86z8q7T8A` sceKernelExitSblock |
| libSceSaveData | 3 | 0 | `YbCO38BOOl4` sceSaveDataCopy5 |
| libSceIme | 2 | 2 | `16UI54cWRQk` sceImeOpenInternal |
| libSceSystemService | 2 | 2 | `f4oDTxAJCHE` sceSystemServiceGetAppIdOfBigApp |
| libSceAudioIn | 2 | 1 | `VoX9InuwwTg` sceAudioInDeviceOpen |
| libSceImeDialog | 2 | 0 | `oe92cnJQ9HE` sceImeDialogInitInternal2 |
| libSceVoiceQoS | 2 | 2 | `+0lOiPZjnBI` sceVoiceQoSSetMode |
| libSceUserService | 2 | 0 | `j-CnRJn3K+Q` sceUserServiceGetNpMAccountId |
| libScePad | 1 | 1 | `MLA06oNfF+4` scePadSetConnection |

Listed for completeness — the Gen5 half of each of these claims is false, and
the fix is either to demote the declaration to `Gen4` or to delete it.

| NID | claimed name | claimed library | kind | site |
|---|---|---|---|---|
| `Ac86z8q7T8A` | `sceKernelExitSblock` | libKernel | stub | `src\SharpEmu.Libs\Kernel\KernelExports.cs:398` |
| `4h6F1LLbTiw` | `sceKernelMapNamedFlexibleMemoryInternal` | libKernel | implemented | `src\SharpEmu.Libs\Kernel\KernelMemoryCompatExports.cs:3826` |
| `Hc4CaR6JBL0` | `sceKernelSyncOnAddressWait` | libKernel | implemented | `src\SharpEmu.Libs\Kernel\KernelSyncOnAddressCompatExports.cs:26` |
| `q2y-wDIVWZA` | `sceKernelSyncOnAddressWake` | libKernel | implemented | `src\SharpEmu.Libs\Kernel\KernelSyncOnAddressCompatExports.cs:65` |
| `9Gq5rOkWzNU` | `sceAppContentSmallSharedDataFormat` | libSceAppContent | stub | `src\SharpEmu.Libs\AppContent\AppContentExports.cs:387` |
| `xhb-r8etmAA` | `sceAppContentSmallSharedDataGetAvailableSpaceKb` | libSceAppContent | implemented | `src\SharpEmu.Libs\AppContent\AppContentExports.cs:390` |
| `QuApZnMo9MM` | `sceAppContentSmallSharedDataMount` | libSceAppContent | implemented | `src\SharpEmu.Libs\AppContent\AppContentExports.cs:393` |
| `EqMtBHWu-5M` | `sceAppContentSmallSharedDataUnmount` | libSceAppContent | implemented | `src\SharpEmu.Libs\AppContent\AppContentExports.cs:396` |
| `uJ0VhGcxCTQ` | `sceAudio3dPortFreeState` | libSceAudio3d | implemented | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:267` |
| `SEggctIeTcI` | `sceAudio3dPortGetList` | libSceAudio3d | implemented | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:281` |
| `flPcUaXVXcw` | `sceAudio3dPortGetParameters` | libSceAudio3d | implemented | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:292` |
| `CKHlRW2E9dA` | `sceAudio3dPortGetState` | libSceAudio3d | implemented | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:317` |
| `-pzYDZozm+M` | `sceAudio3dPortQueryDebug` | libSceAudio3d | implemented | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:335` |
| `yEYXcbAGK14` | `sceAudio3dSetGpuRenderer` | libSceAudio3d | stub | `src\SharpEmu.Libs\Audio\Audio3dExports.cs:343` |
| `VoX9InuwwTg` | `sceAudioInDeviceOpen` | libSceAudioIn | implemented | `src\SharpEmu.Libs\Audio\AudioInExports.cs:91` |
| `vYFsze1SqU8` | `sceAudioInSetAllMute` | libSceAudioIn | stub | `src\SharpEmu.Libs\Audio\AudioInExports.cs:160` |
| `Iz9X7ISldhs` | `sceAudioOutA3dControl` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:364` |
| `9RVIoocOVAo` | `sceAudioOutA3dExit` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:366` |
| `n7KgxE8rOuE` | `sceAudioOutA3dInit` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:368` |
| `5+r7JYHpkXg` | `sceAudioOutGetSparkVss` | libSceAudioOut | implemented | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:420` |
| `n16Kdoxnvl0` | `sceAudioOutInitIpmiGetSession` | libSceAudioOut | implemented | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:424` |
| `-LXhcGARw3k` | `sceAudioOutMbusInit` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:439` |
| `o4OLQQqqA90` | `sceAudioOutSetConnections` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:466` |
| `QHq2ylFOZ0k` | `sceAudioOutSetConnectionsForUser` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:468` |
| `r9KGqGpwTpg` | `sceAudioOutSetDevConnection` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:470` |
| `18IVGrIQDU4` | `sceAudioOutSetJediJackVolume` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:488` |
| `h0o+D4YYr1k` | `sceAudioOutSetJediSpkVolume` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:490` |
| `eeRsbeGYe20` | `sceAudioOutSetMorpheusParam` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:507` |
| `IZrItPnflBM` | `sceAudioOutSetMorpheusWorkingMode` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:509` |
| `Gy0ReOgXW00` | `sceAudioOutSetPortConnections` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:511` |
| `oRBFflIrCg0` | `sceAudioOutSetPortStatuses` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:513` |
| `d3WL2uPE1eE` | `sceAudioOutSetSparkParam` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:517` |
| `I91P0HAPpjw` | `sceAudioOutStartAuxBroadcast` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:523` |
| `uo+eoPzdQ-s` | `sceAudioOutStartSharePlay` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:525` |
| `AImiaYFrKdc` | `sceAudioOutStopAuxBroadcast` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:527` |
| `teCyKKZPjME` | `sceAudioOutStopSharePlay` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:529` |
| `95bdtHdNUic` | `sceAudioOutSuspendResume` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:531` |
| `JEHhANREcLs` | `sceAudioOutSystemControlGet` | libSceAudioOut | implemented | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:541` |
| `9CHWVv6r3Dg` | `sceAudioOutSystemControlSet` | libSceAudioOut | stub | `src\SharpEmu.Libs\Audio\AudioOutExports.cs:543` |
| `16UI54cWRQk` | `sceImeOpenInternal` | libSceIme | stub | `src\SharpEmu.Libs\Ime\ImeExports.cs:471` |
| `rM-1hkuOhh0` | `sceImeVshDisableController` | libSceIme | stub | `src\SharpEmu.Libs\Ime\ImeExports.cs:584` |
| `oe92cnJQ9HE` | `sceImeDialogInitInternal2` | libSceImeDialog | implemented | `src\SharpEmu.Libs\Ime\ImeDialogExports.cs:304` |
| `IoKIpNf9EK0` | `sceImeDialogInitInternal3` | libSceImeDialog | implemented | `src\SharpEmu.Libs\Ime\ImeDialogExports.cs:307` |
| `MLA06oNfF+4` | `scePadSetConnection` | libScePad | stub | `src\SharpEmu.Libs\Pad\PadExports.cs:915` |
| `YbCO38BOOl4` | `sceSaveDataCopy5` | libSceSaveData | implemented | `src\SharpEmu.Libs\SaveData\SaveDataExports.cs:1586` |
| `CWlBd2Ay1M4` | `sceSaveDataGetDataBaseFilePath` | libSceSaveData | implemented | `src\SharpEmu.Libs\SaveData\SaveDataExports.cs:1756` |
| `UMpxor4AlKQ` | `sceSaveDataGetFormat` | libSceSaveData | implemented | `src\SharpEmu.Libs\SaveData\SaveDataExports.cs:1760` |
| `Pgt0gg14ewU` | `CA_MGMT_allocCertDistinguishedName` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:317` |
| `wJ5jCpkCv-c` | `CA_MGMT_certDistinguishedNameCompare` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:320` |
| `Vc2tb-mWu78` | `CA_MGMT_convertKeyBlobToPKCS8Key` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:323` |
| `IizpdlgPdpU` | `CA_MGMT_convertKeyDER` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:326` |
| `Y-5sBnpVclY` | `CA_MGMT_convertKeyPEM` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:329` |
| `jb6LuBv9weg` | `CA_MGMT_convertPKCS8KeyToKeyBlob` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:332` |
| `ExsvtKwhWoM` | `CA_MGMT_convertProtectedPKCS8KeyToKeyBlob` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:335` |
| `AvoadUUK03A` | `CA_MGMT_decodeCertificate` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:338` |
| `S0DCFBqmhQY` | `CA_MGMT_enumAltName` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:341` |
| `Xt+SprLPiVQ` | `CA_MGMT_enumCrl` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:344` |
| `4HzS6Vkd-uU` | `CA_MGMT_extractAllCertDistinguishedName` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:347` |
| `W80mmhRKtH8` | `CA_MGMT_extractBasicConstraint` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:350` |
| `7+F9pr5g26Q` | `CA_MGMT_extractCertASN1Name` | libSceSsl | stub | `src\SharpEmu.Libs\Network\SslExports.cs:353` |

…154 more; full list:

```
awk -F'\t' '$19=="NO_INNOCENT_EXPLANATION"' scripts/nid_gap.tsv
```

### `NO_INNOCENT_EXPLANATION` — 11

| claimed library | NIDs | stubs | example |
|---|---:|---:|---|
| libSceAgc | 4 | 0 | `dZGYu5wObJs` sceAgcUnknownDZGYu5wObJs |
| libSceNet | 4 | 2 | `b9Ft65tqvLk` sceNetBandwidthControlGetIfParam |
| libSceSaveData | 2 | 1 | `SQWusLoK8Pw` sceSaveDataDelete5 |
| libSceNpTrophy | 1 | 1 | `eV1rtLr+eys` sceNpTrophySystemGetTrophyTitleIds |

These are the direct evidence of invented surface. Every one is a
`[SysAbiExport]` declaring a **`Gen5`-only** export of a library this firmware
ships in cleartext, for a NID that library does not have.

| NID | claimed name | claimed library | kind | site |
|---|---|---|---|---|
| `dZGYu5wObJs` | `sceAgcUnknownDZGYu5wObJs` | libSceAgc | implemented | `src\SharpEmu.Libs\Agc\AgcExports.cs:4227` |
| `zlqfTyrQSPk` | `sceAgcUnknownZlqfTyrQSPk` | libSceAgc | implemented | `src\SharpEmu.Libs\Agc\AgcExports.cs:4212` |
| `eAy8eGNsCuU` | `sceAgcWriteDataPatchSetCachePolicy` | libSceAgc | implemented | `src\SharpEmu.Libs\Agc\AgcExports.cs:3235` |
| `tmy-+rBpspY` | `sceAgcWriteDataPatchSetDst` | libSceAgc | implemented | `src\SharpEmu.Libs\Agc\AgcExports.cs:3243` |
| `b9Ft65tqvLk` | `sceNetBandwidthControlGetIfParam` | libSceNet | implemented | `src\SharpEmu.Libs\Network\NetExports.cs:967` |
| `PDkapOwggRw` | `sceNetBandwidthControlGetPolicy` | libSceNet | implemented | `src\SharpEmu.Libs\Network\NetExports.cs:970` |
| `g4DKkzV2qC4` | `sceNetBandwidthControlSetIfParam` | libSceNet | stub | `src\SharpEmu.Libs\Network\NetExports.cs:976` |
| `7Z1hhsEmkQU` | `sceNetBandwidthControlSetPolicy` | libSceNet | stub | `src\SharpEmu.Libs\Network\NetExports.cs:979` |
| `eV1rtLr+eys` | `sceNpTrophySystemGetTrophyTitleIds` | libSceNpTrophy | stub | `src\SharpEmu.Libs\Np\NpTrophyExports.cs:284` |
| `SQWusLoK8Pw` | `sceSaveDataDelete5` | libSceSaveData | implemented | `src\SharpEmu.Libs\SaveData\SaveDataExports.cs:299` |
| `uNu7j3pL2mQ` | `sceSaveDataPromote5` | libSceSaveData | stub | `src\SharpEmu.Libs\SaveData\SaveDataExports.cs:1802` |

---

## 4. Second-order check: do our own NID/name pairs even hash?

A NID is `SHA1(name + salt)` truncated (`scripts/nid_resolver.py`). So every
`[SysAbiExport(Nid = …, ExportName = …)]` is a checkable claim.

| result | count |
|---|---:|
| `ExportName` hashes to `Nid` | 4023 |
| hashes after stripping a C#-disambiguation prefix (`internal_`, `posix_`, …) | 114 |
| `ExportName` is an admitted placeholder (`Func_…`, `…Unknown…`, `Libraries`, `ORBIS`) | 90 |
| **`ExportName` asserts a real name that does not hash to its `Nid`** | 34 |

EXTRACTED (re-hashed all 4261 registrations). 28 of the 34 mismatches are NIDs the firmware
*does* name, so the correct spelling is available for free — mostly one-character misses:

| NID | our `ExportName` | firmware symbol | site |
|---|---|---|---|
| `sfKygSjIbI8` | `getdirentries` | `_getdirentries` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:470` |
| `PfccT7qURYE` | `kernel_ioctl` | `ioctl` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:474` |
| `wW+k21cmbwQ` | `kernel_ioctl` | `_ioctl` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:476` |
| `iWsFlYMf3Kw` | `posix_pthread_cleanup_pop` | `__pthread_cleanup_pop_imp` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:893` |
| `sHziAegVp74` | `posix_sigalstack` | `sigaltstack` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:993` |
| `+WRlkKjZvag` | `readv` | `_readv` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:813` |
| `YSHRBRLn2pI` | `writev` | `_writev` | `src\SharpEmu.Libs\Kernel\KernelExtraCompatExports.cs:821` |
| `kJmjt81mXKQ` | `sceAppContentAddcontEnqueueDownloadByEntitlementId` | `sceAppContentAddcontEnqueueDownloadByEntitlemetId` | `src\SharpEmu.Libs\AppContent\AppContentIroExports.cs:10` |
| `efX3lrPwdKA` | `sceAppContentAddcontMountByEntitlementId` | `sceAppContentAddcontMountByEntitlemetId` | `src\SharpEmu.Libs\AppContent\AppContentIroExports.cs:12` |
| `00oCq0RwSAY` | `_ZN3sce4Json11Initializer27setGlobalNullAccessCallbackEPFRKNS0_5ValueENS0_9ValueTypeEPS3_PvES7_` | `_ZN3sce4Json11Initializer27setGlobalNullAccessCallBackEPFRKNS0_5ValueENS0_9ValueTypeEPS3_PvES7_` | `src\SharpEmu.Libs\Json\JsonExports.cs:137` |
| `jnKaHGkrxZ4` | `sceUltConditionVariableCreate` | `_sceUltConditionVariableCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:991` |
| `RVmEia0vXMI` | `sceUltConditionVariableOptParamInitialize` | `_sceUltConditionVariableOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:984` |
| `mmt8Sa6tL6c` | `sceUltMutexCreate` | `_sceUltMutexCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:830` |
| `1+8t9aHLiz8` | `sceUltMutexOptParamInitialize` | `_sceUltMutexOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:823` |
| `9Y5keOvb6ok` | `sceUltQueueCreate` | `_sceUltQueueCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:595` |
| `TFHm6-N6vks` | `sceUltQueueDataResourcePoolCreate` | `_sceUltQueueDataResourcePoolCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:503` |
| `6gYjd50q0CE` | `sceUltQueueDataResourcePoolOptParamInitialize` | `_sceUltQueueDataResourcePoolOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:488` |
| `TkASc9I-xX0` | `sceUltQueueOptParamInitialize` | `_sceUltQueueOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:588` |
| `iIfTXvh1hiM` | `sceUltReaderWriterLockCreate` | `_sceUltReaderWriterLockCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:1374` |
| `Gw7yn0CEmv8` | `sceUltReaderWriterLockOptParamInitialize` | `_sceUltReaderWriterLockOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:1367` |
| `h5QlIYj+Ro8` | `sceUltSemaphoreCreate` | `_sceUltSemaphoreCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:1209` |
| `NPRRPNKDBN0` | `sceUltSemaphoreOptParamInitialize` | `_sceUltSemaphoreOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:1202` |
| `jw9FkZBXo-g` | `sceUltUlthreadRuntimeCreate` | `_sceUltUlthreadRuntimeCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:361` |
| `V2u3WLrwh64` | `sceUltUlthreadRuntimeOptParamInitialize` | `_sceUltUlthreadRuntimeOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:347` |
| `YiHujOG9vXY` | `sceUltWaitingQueueResourcePoolCreate` | `_sceUltWaitingQueueResourcePoolCreate` | `src\SharpEmu.Libs\Ult\UltExports.cs:425` |
| `LuLTRt0rfTw` | `sceUltWaitingQueueResourcePoolOptParamInitialize` | `_sceUltWaitingQueueResourcePoolOptParamInitialize` | `src\SharpEmu.Libs\Ult\UltExports.cs:411` |
| `D-CzAxQL0XI` | `sceUserServiceGetPlatformPrivacySetting` | `sceUserServiceGetPlatformPrivacyWs1` | `src\SharpEmu.Libs\UserService\UserServiceExports.cs:152` |
| `kGVLc3htQE8` | `sceVideoOutGetDeviceCapabilityInfo` | `sceVideoOutGetDeviceCapabilityInfo_` | `src\SharpEmu.Libs\VideoOut\VideoOutExports.cs:930` |

The remaining 6 have no firmware name to compare against:

| NID | our `ExportName` | claimed library | site |
|---|---|---|---|
| `uPLTdl3psGk` | `FmodSystemSetOutput` | libfmod | `src\SharpEmu.Libs\Audio\FmodCompatExports.cs:14` |
| `HV4j+E0MBHE` | `sceAgcCreateInterpolantMapping` | libSceAgc | `src\SharpEmu.Libs\Agc\AgcExports.cs:1332` |
| `dolOmWH+huQ` | `sceAgcDriverValidateDcbRange` | libSceAgc | `src\SharpEmu.Libs\Agc\AgcExports.cs:3931` |
| `V++UgBtQhn0` | `sceAgcGetDataPacketPayloadAddress` | libSceAgc | `src\SharpEmu.Libs\Agc\AgcExports.cs:1559` |
| `23LRUSvYu1M` | `sceAgcInit` | libSceAgc | `src\SharpEmu.Libs\Agc\AgcExports.cs:874` |
| `fd5Bp5tGTgo` | `sceAgcDriverSubmitDcbRange` | libSceAgcDriver | `src\SharpEmu.Libs\Agc\AgcExports.cs:3990` |

---

## 5. What this measurement cannot tell you

- **COVERED means "the C# does something and sits under the right library name". It does
  not mean the behaviour matches Prospero.** No routine in this tree has been compared
  against the corresponding firmware routine. This is the biggest single caveat, and it
  gets worse: the upstream inventory (`scripts/our_nids.py`, *its* measurement, not one
  reproduced here) reports that 733 of the 2962 `implemented` exports never write guest
  memory or mutate emulator state — they read arguments, validate, and return success.
  `our_nids.tsv` does not carry that per-NID flag, so this script cannot subtract them and
  they are counted as COVERED above. Adding that column to `our_nids.py` is the single
  cheapest honest downward revision available and it would lower the headline.
- **MISPLACED costs nothing at runtime today** — dispatch is NID-keyed. It is a metadata
  defect that becomes a real one the moment the loader starts honouring library identity
  (which it must, e.g. to keep `libkernel` and `libScePosix` apart).
- **The denominator is a floor.** 38 modules remain sealed; a library absent from this dump
  (`libSceNpCppWebApi`, 802 of Astro's 1732 imports) contributes zero to both sides.
- **Only one title's import surface is available.** "No game calls this" really means "the
  one game whose import table we have does not call this".

