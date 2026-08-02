<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# Capability survey: what to build next, and what unblocks Astro Bot

> **Superseded snapshot.** This survey describes an older commit and its rwlock
> theory is refuted by later title traces. Keep it as investigation history;
> use `docs/source-alignment-audit.md` and the newest section of
> `docs/evidence-source-ledger.md` for current priorities.

Synthesis of three read-only surveys run 2026-07-28 at `master` = `edde987`. Lane A inventoried the
firmware dumps, lane B measured our export coverage against them, lane C mapped subsystem capability.
Every ranked item below carries the number that justifies it. Where a survey lane repeated a claim
that the repo contradicts, the contradiction is recorded in section 7 and the ranking uses the
contradiction, not the claim.

No build, no boot, no game run was used to produce this document.

---

## 1. The one-paragraph answer

**Astro Bot is not blocked by missing exports.** Of its 1,732 imports, exactly 3 land in the
stub census and **0 are LIE**. It is blocked by one synchronisation bug in
`KernelPthreadExtendedCompatExports.cs`, localised below to a specific branch. **Superliminal is not
blocked by missing exports either**: PSNCore.prx resolves 2,435 imports with 0 unresolved, and only
36 of its imports are LIE shells. What both titles share is that the next unit of progress is a
*contract* fix in code we already wrote, not new surface. Meanwhile the largest untapped asset in the
tree is not a subsystem at all: it is that 4,006 of our 4,255 registered exports have a real,
executable Sony body on disk, and the harness that runs them is already a merge gate with **5
hand-authored case files**.

## 2. Breadth versus depth: prioritise depth, on shared mechanisms only

The corpus is two AAA titles. PS5 has no homebrew ladder, no test-suite ROMs, and no small-title
onramp, so "number of titles that boot" is a quantity we cannot measure and therefore cannot
optimise. Every breadth argument in this survey is an argument from surface area (LIE counts,
unregistered libraries), and surface area has already proven to be a poor predictor: `libSceFont`
carries the biggest LIE pool in the project (124) and Astro imports 52 font NIDs of which **zero are
LIE**, while the thing that actually stops Astro is a single `if` in a rwlock.

The verdict is therefore **depth-first, with a mechanism test**: work an item only if its fix is
shared machinery rather than title-specific. Rwlock semantics, an ATRAC9 decoder, SCRATCH decode and
`syscall` handling are all depth items today and breadth items the moment a third title arrives. The
project already learned the opposite lesson the expensive way at `082ece3`, which deleted the
title-specific Astro hacks once the real fixes landed.

The one breadth item that earns funding regardless is the oracle case generator (section 4), because
it is the only work that converts an unmeasurable population (814 LIEs) into a mechanically drainable
queue. Fund exactly one breadth infrastructure item, not a breadth feature programme.

## 3. Ranked build order

### 1. Rwlock `CompatWriter` contract review. Named blocker, one file.

Evidence: run `20260728-071927-mutexowner`, `OdxAsyncLoader state=Blocked
reason=pthread_rwlock_wrlock` x30, identical resume address `0x0000000800010E31` every time,
`guest_threads.wake key=pthread_rwlock#1C count=34`, main thread spinning 492
`pthread_cond_broadcast` pairs with `waiters=0`.

`docs/methodology-execution.md:47` frames the question as "does the read path honour
`WaitingWriters`". **It does**: `ReaderMustWaitForRwlock` at
`src/SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs:1723-1737` returns true when
`WaitingWriters > 0` and the caller holds no read lock. That hypothesis is closed.

The live defect is one branch lower. `PthreadRwlockLockCore` (`:1569`) has a non-POSIX second
ownership mode. At `:1597-1605`, when `_strictRwlockWriterPreference` is off, which is the default
because it needs `SHARPEMU_STRICT_RWLOCK_WRITER_PREFERENCE=1`, **every guest write lock on a free
rwlock takes the "compat writer" path instead of setting `WriterThreadId`**. A compat writer is a
counter, and `:1591-1595` grants a further acquire whenever that count is already above zero, which
makes it an unbounded recursive write lock. POSIX rwlocks are not recursive. `CompatWriterTotalCount`
drains only through `RemoveCompatWriter` on unlock (`:1292`), so any imbalance leaves it permanently
above zero, after which `ReaderMustWaitForRwlock` (`:1730`) blocks every reader and the writer tests
at `:1607` and `:1688` block every writer. That is exactly the observed shape.

Cost: one file, contract review not point fix, since `fix_storm.py` already flags the sibling
`KernelPthreadCompatExports.cs` at 5 commits in one day. Discriminator available before any code
change: `DetectRwlockWriterConflict` (`:1711`) and the `RWLOCK READER/WRITER COEXIST` line (`:1671`)
either appear in the failing log or do not, and that alone separates the two hypotheses.

**This is what unblocks Astro Bot.**

### 2. An oracle case generator. 814 unverifiable claims, 5 case files.

`scripts/premerge.py:141` runs `scripts/fw_oracle_gate.py` as a **gating** step, and the comment at
`:124-135` is explicit that it is the only check in the gate whose expected answers were written by
Sony. `oracle/cases/` contains **5 JSON case files across 2 libraries** (`libSceAgc` x4,
`libSceLibcInternal` x1). The census says **814 exports return constant success while Sony's body
exists and does work**. Those two numbers are the whole argument.

Measured cost from `docs/methodology-execution.md:446`: 3.9 s fixed plus 0.42 s per case, roughly 26
minutes for 4,108 NIDs at 5 cases each across 16 cores. Execution is not the bottleneck. **Case
authoring is**, because every case file today is hand-written from a disassembly. The LIE bodies are
small enough to make this tractable: median firmware body 140 B, p90 586 B, total 226,200 B across
all 814.

Build the generator that emits candidate cases from `scripts/fw_export_bodies.py` output plus a
register-shape guess, and the 814 becomes a drainable queue rather than a standing accusation.
Prerequisite already known: six integer arguments only, no float or SSE path, and module globals sit
outside the observation window.

### 3. Wire `src/SharpEmu.GUI/Atrac9/` into `AjmExports`. Decoder already in the repo.

`src/SharpEmu.GUI/Atrac9/` is **16 C# files**, a complete MIT-licensed LibAtrac9 port. Verified
today: its only consumer anywhere in `src/` is `SharpEmu.GUI/SndPreviewPlayer.cs`, a desktop file
preview player. The one hit in `Ngs2Exports.cs:532` is a comment.

The guest path does not touch it. `AjmExports.cs` recognises `CodecMp3, CodecAt9, CodecAac,
CodecLpcm, CodecOpus` (`:1516`), parses AT9 config properly (`TryParseAt9Config` `:1363`), tracks
superframe and sideband state correctly (`:1098-1138`), and then writes zeros through
`TryScatterZeroes` (`:1039`, `:1264`). Only LPCM has a real path (`:971`). Every ATRAC9, MP3, AAC and
Opus stream in every title is silence that reports success. Upstream does not solve this either:
`2272b9b` says in its own body "this is not a real codec".

Corpus number: Superliminal imports 12 of our 25 `libSceAjm` exports and **3 of them are LIE**
(`sceAjmBatchErrorDump` 2,347 B of firmware, `sceAjmModuleUnregister` 213 B, `sceAjmBatchInitialize`
42 B). The same move applies one step later to `Media/FfmpegNativeVideoDecoder.cs`, which exists
(16 KB), works, and is wired only into `AvPlayerExports.cs` while `Videodec2Exports.cs` clears output
structures at `:317`.

Days of work for a whole-category capability. Cheapest large win in the survey.

### 4. FLAT and SCRATCH shader decode. Verified missing today.

`DecodeFlat` at `src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:1807-1852` reads the segment
field at `(word >> 14) & 0x3` and produces a name **only for `segment == 0x2`**, which is GLOBAL, and
only for **24 opcodes**. Segments 0 (FLAT) and 1 (SCRATCH) fall to `string.Empty` and fail decode.

SCRATCH is where the shader compiler spills VGPRs, so the failure rate scales with shader complexity
rather than with any opt-in feature. This is the last of the three silent-decode findings in
`docs/ps5-shader-isa-audit.md` still standing; the other two are already fixed (section 7). Read
upstream `5228335 fix(gpu): support Gen5 flat memory and 3D images` before writing anything, but do
not fetch or merge, the histories are unrelated.

### 5. The 36 LIE exports a live title actually calls.

This is the entire Tier 1 of lane B, and it is Superliminal-driven because Astro's LIE intersection
is empty. Sixteen libraries, 36 exports, each with its firmware body size:

| library | LIE on the import path | worst offenders |
|---|--:|---|
| `libKernel` | 6 of 9 | `__pthread_cxa_finalize` 401 B, `_sigprocmask` 144 B |
| `libSceSystemService` | 5 of 37 | `sceSystemServiceShowControllerSettings` 328 B, `sceSystemServiceLoadExec` 37 B |
| `libkernel` (Posix side) | 4 of 27 | `_is_signal_return` 142 B |
| `libSceNpManager` | 3 of 5 | `sceNpRegisterStateCallbackA` 596 B |
| `libSceAjm` | 3 of 4 | covered by item 3 |
| `libSceSaveDataDialog` | 3 of 3 | Superliminal imports 10 of 10 exports we register here |
| `libSceAudioOut` | 1 of 13 | `sceAudioOutInit` 796 B, **on the boot path** |
| 9 others | 1 each | `sceImeKeyboardGetResourceId` 428 B, `scePadSetVibrationMode` 143 B |

Work these against the oracle from item 2 rather than by inspection. Thirty-six is small enough to
hand-author cases for immediately, which also makes it the natural pilot corpus for the generator.

### 6 to 10, in order

6. **Census the `syscall` instruction (`0F 05`) in guest modules.** There is no syscall handling
   anywhere in `src/`, confirmed by a zero-hit scan for `syscall`, `0x0F,0x05`, `SyscallDispatch`,
   `SyscallTable` and `amd64_set_fsbase`. The whole kernel surface arrives through HLE thunks, so a
   guest reaching a raw `syscall` executes a **Windows** syscall in our process, and there is no #UD
   to catch because the instruction is legal. Partially quantified already:
   `docs/methodology-execution.md:419` records 4 syscall byte sites in `libSceLibcInternal`, which is
   why the oracle gate exits 3. Finish the count across the game dumps; it is read-only and cheap.
7. **Repoint `fw_exports.py` / `nid_firmware_audit.py` / `stub_census.py` at the 9.00 tree as a
   second root.** See section 6.
8. **`libSceIme` + `libSceCommonDialog`.** 36 and 14 LIE, and `libSceCommonDialog` is 14 of 17
   exports, so the library is effectively unimplemented. Both are boot-path libraries for titles that
   need text entry or a user-select flow, and the firmware ships roughly 14 further dialog modules
   with zero exports registered.
9. **SALU integer with SCC plus the 177 expanded compare mnemonics**, as the first ISA slice.
   **Not f16.** See contradiction 5.
10. **NGG classifier tightening only** (`NggPrimitiveShader.cs:478`). One day, converts a silent
    wrong-geometry path into a loud one. Do **not** start the `VK_EXT_mesh_shader` project: the audit
    demotes the remainder of NGG to eighth-to-tenth on measured grounds and prices the real path at
    3 to 6 weeks, and it is blocked on a position misc-vector Z/W bit split that Sony's document does
    not state.

### Explicitly not now

`libSceNpTrophy` (62 LIE), `libSceGameLiveStreaming` (37), `libSceFont` (124), `libSceFontFt` (19),
`libSceSystemStateMgr` (18), `libSceNgs2` (11), `libSceSystemGesture` (9), `libSceAppMessaging` (8)
and roughly 35 small system-service facades: **neither corpus title imports a single NID from any of
them**. That is about 190 LIEs, a quarter of the whole bucket, with zero measurable payoff today.
Trophies are a submission requirement and will matter, but they are not a boot blocker.

## 4. What the dumps let us do that we are not doing

565 cleartext modules in 4.03 and 614 in 9.00, with real instruction bodies and `st_size` on every
native export. Against our 4,255 registered exports: **4,006 exist in 4.03, 3,974 in 9.00, 233 in
neither**. `SharpEmu --fw-oracle` maps a cleartext module through the ordinary loader, executes
Sony's body under the native backend, runs our HLE export against byte-identical guest state, and
compares RAX plus every arena byte, with no game involved.

That combination means **every one of the 814 LIEs is mechanically falsifiable without a boot**.
Not "reviewable", falsifiable, against an answer key we did not write. Nothing else in the project
has that property; `premerge.py:126` says as much, that every other gate step only proves we agree
with ourselves.

The scale we are leaving on the table: 814 LIEs at 5 cases each is about 4,070 cases, roughly half an
hour of machine time on 16 cores. We have authored 17 cases in 5 files. The gap between 17 and 4,070
is entirely tooling, and it is item 2 above.

Two cautions carried forward from the existing write-up, both earned. First, **read a divergence as
a question, not a conviction**: the `sceAgcDcbDrawIndexAuto` "we emit IT_NOP where Sony emits
IT_DRAW_INDEX_AUTO" verdict was one inference away from "draws never happen" and would have been
wrong, because our own DCB parser recognises that private pair at `AgcExports.cs:4247-4260`. Second,
the harness itself had four defects found by adversarial audit, one of which made a dispatch that
never reached the firmware indistinguishable from one returning zero, aimed straight at the VERIFIED
NO-OP census bucket. Any generator must keep those checks live.

Also unused: 199 of the 203 ABSENT exports are real PS4-era symbol names present in the public
catalogs, so the ABSENT bucket is mostly PS4 surface we carried into PS5 library tables rather than
error. `libSceSsl` is the clear case: 4.03 exports 56 functions and we register 217, of which 161 are
Mocana NanoSSL internals PS5 does not expose. Deleting surface is cheaper than implementing it.

## 5. What 9.00 adds over 4.03

Measured, not assumed:

| | 4.03 | 9.00 |
|---|--:|--:|
| cleartext modules | 565 | **614** |
| encrypted SELF | 40 | **0** |
| NIDs with a sized body | 42,171 | **46,900** |
| NIDs unique to that tree | 8,487 | **13,216** |
| export library names | 683 | **763** |
| non-code data files | **1,740** | 281 |

9.00 is bigger on every code axis: more file bytes, more executable `PT_LOAD`, more FUNC exports,
more summed `st_size`, zero FUNC exports whose vaddr has no file bytes, zero parse failures. Every
one of the 26 game-facing modules measured has at least as many sized bodies in 9.00 as in 4.03,
and several have far more: `libkernel` +68 NIDs, `libkernel_sys` +99, `libSceLibcInternal` +277,
`libScePad` +48, `libSceUserService` +109. 9.00 also carries the BD-J and Java stack **cleartext**
that 4.03 hides behind encryption, plus 278 pre-generated per-module RE text reports under
`common/lib/analysis_sprx/`.

What 9.00 does not rescue: `libc.sprx`, `libSceDbg`, `libSceDbgAssist`, `libSceWorkspace` and the
entire Deci5 devkit stack are absent from it and encrypted in 4.03. There is **no cleartext `libc.sprx`
on disk in either tree**, so any plan depending on reading it needs a different dump. Twenty-two of
the 28 things 4.03 uniquely holds are encrypted or opaque; stop budgeting effort against them.

Practical consequence: mine 9.00 for code, keep 4.03 for everything that is not code (fonts, IME
dictionaries, HRTF and SRTF audio tables, shell assets, the PUP payload) and as the cross-check for
version-sensitive struct layouts. **Treat cross-version agreement as the strongest signal available**:
where a NID's body has the same shape in both dumps, implement with confidence; where it differs,
record the divergence.

The version-matching assumption needs one qualification, though. Two of the 203 ABSENT exports exist
only in 9.00, `sceAgcGetIsTrinityMode` and `sceKernelClearVirtualRangeName`, and **Astro Bot imports
both**. Astro was built against an SDK newer than 4.03, so "4.03 is the version-matched dump for our
titles" is not safe as a blanket rule.

## 6. Contradictions found

Listed because contradictions are the most valuable output of a survey, and because four of these
would each have sent a worker at stale work.

1. **"9.00 `common_ex/lib` is reference facades with IL stripped" is false.** The memory record says
   this and it has been steering us to the smaller dump. Measured: 0 modules with zero executable
   bytes in either tree's lib directories, 9.00 larger on every axis. The observation that produced
   the claim was the zero-`st_size` FUNC symbols in the managed `*.dll.sprx` assemblies, which are
   equally zero-sized in 4.03 (230,066 of 4.03's zero-size FUNC exports are **100.0%** in
   `*.dll.sprx`). It is a property of Sony's managed-assembly packaging, present identically in both
   versions. Retire the note.
2. **`docs/methodology-execution.md:445` says "`premerge.py` does not call
   `scripts/fw_oracle_gate.py`". It does, and gatingly**, at `scripts/premerge.py:141`, with a
   `--skip-oracle` escape hatch documented at `:124-135`. The doc understates what the project
   already has.
3. **`docs/methodology-execution.md:47-48` asks whether the rwlock read path honours
   `WaitingWriters`. It does** (`:1723-1737`). Following that framing means auditing correct code.
   The defect is the `CompatWriter` counter at `:1591-1605`, see item 1.
4. **`docs/ps5-shader-isa-audit.md` findings 1 and 2 are stale.** The audit describes VOP3 as a
   54-entry table over a 1024-entry space with VOPC and VOP1 unmapped, and wave32 lane masks written
   as 64 bits. Both landed in Track 1 wave 1 at `4ca38f7`. Verified in the tree today:
   `Gen5ShaderTranslator.cs:1523-1530` resolves `< 0x100` to `VopcName`, `0x100-0x13F` to
   `Vop2Name`, `0x180-0x1FF` to `Vop1Name`; `_waveLaneCount` is now tested in 40 places across four
   files including `Gen5SpirvTranslator.Alu.cs`. So are MIMG's eighth opcode bit, MTBUF format,
   `ds_append`/`ds_ordered_count`, LDS at 64 KiB and the DPP bank formula. **Only the FLAT/SCRATCH
   finding survives**, and it survives fully. Any ranking that still lists VOP3 or wave32 near the
   top, including lane C's, is reading a superseded document.
5. **`queues/isa-gaps.json` ranks f16 VALU (43 instructions) first; this is overturned.**
   `docs/methodology-execution.md:400-404`: the f16 chapters of Sony's ISA documents were never
   captured, so there are no f16 semantics to compile against, and f16 can be neither implemented to
   contract nor differentially tested until a different source exists. The first ISA slice is SALU
   integer with SCC plus the 177 expanded compare mnemonics with EXEC masking. The queue file is also
   weaker than its size suggests: 90 exact contract joins, 150 name-only placeholders, 0 templated,
   so two thirds of its 240 items hand a worker a `Select-String` recipe rather than a contract.
6. **"Astro is blocked by stubs" is false.** Of Astro's 1,732 imports, 3 land in the shell census and
   0 are LIE. It imports 52 `libSceFont` NIDs and the overlap with our 124 `libSceFont` LIEs is zero.
   Astro's import surface has already been de-stubbed by prior work.
7. **A third firmware tree exists and neither inventory lane covered it.** `games/3.02` is on disk
   (confirmed), with 842 files and a `Stub call library/` carrying **39,158 exported symbol names**
   across 275 libraries, of which 10,952 are `sce*`. Lane A's inventory compares 4.03 against 9.00
   only, so its set-difference numbers are not a full-disk statement. Those 39,158 names hash
   directly to NIDs and are the obvious source for naming unresolved imports.
8. **Two registered exports have no corroboration anywhere.** `sceVoiceQoSSetMode` and
   `sceVoiceQoSTerminate` (`src/SharpEmu.Libs/Voice/VoiceQoSExports.cs:42` and `:31`) appear in
   neither firmware dump nor in any of the three public name catalogs (154,457 + 308,901 + 94,276
   entries). `libSceVoiceQoS` is 4 exports, 2 ABSENT and 2 LIE, so the whole library is
   unsubstantiated. All other 201 ABSENT names hash correctly and are real PS4-era symbols.
9. **Upstream is not "N commits behind".** `git merge-base master upstream/main` returns nothing;
   the histories are unrelated (181 commits versus 419, zero shared). There is no merge, only
   per-feature reading. Treat `99004a3`, `a158960`, `5228335`, `f9d9213`, `e1a3b92` as a reading
   list.

## 7. Standing risks with no owner

- **Raw `syscall` in guest code.** No handler, no #UD trap possible, and a hit executes a host
  syscall in our process. Invisible to every metric we have until item 6 counts it.
- **`pthread_cancel`, `pthread_cleanup_push/pop` and `pthread_kill` are all no-ops**
  (`KernelExtraCompatExports.cs:450`, `:845-849`, `:869`). Cancellation and cleanup are one
  capability and neither half exists; guest-to-guest signals are swallowed.
- **#UD recovery covers BMI/ABM plus four AMD instructions only**, and refuses FS/GS-relative
  operands by its own admission. Title compatibility is therefore a function of the developer's host
  CPU, which is a testing hazard as much as a runtime one.
- **`0xC0000409` fastfail is logged and then fatal.** Guest CRT stack-cookie and CFG failures are
  terminal with no interpretation of the failure code.
- **Four active fix storms** flagged by `fix_storm.py`: `DirectExecutionBackend.cs` 9 commits in
  3 days, `AgcExports.cs` 8, `VulkanVideoPresenter.cs` 7, `KernelPthreadCompatExports.cs` 5. All four
  sit in areas this survey ranks. Under P6 each deserves a contract review rather than the next point
  fix.
