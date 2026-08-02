<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Superliminal — current state

Status doc. `docs/superliminal-boot.md` is the 176 KB chronological log of how we got here and
keeps its value as a record of what was tried; it is not readable as a status. This file is the
short answer, and it supersedes the boot log wherever the two disagree.

Last verified 2026-07-27 against `18fab4d`, host `AMD Radeon Pro V620 MxGPU`, Windows.

## Where the title actually gets to

**Its own title screen, rendered correctly, indefinitely stable.**

```
SHARPEMU_IGNORE_STACK_CHK=1 SHARPEMU_GPU_WAIT_MODE=force
SHARPEMU_PENDING_GUEST_WORK_ITEMS=8192 SHARPEMU_NP_FAKE_SIGNED_IN=1
SHARPEMU_HLE_MEMCPY=0
```

32.0 FPS, 31.2 ms flips, 115 MB resident, CPU 6 %, memory flat over minutes. The storage-room
scene behind the logo renders in full - brick, shelving, boxes, the pendant lamp with its bloom,
the propped mirror panels with sky reflected in them. Audio advances. This is not a stall that
happens to draw something; it is a complete, healthy frame loop.

Measured with `SHARPEMU_IMPORT_CENSUS=1` (see below), per 15 s interval at the title screen:

| Export | Rate | Per frame |
|---|---|---|
| `sceAgcDcbSetFlip`, `sceAgcDriverSubmitDcb` | 32/s | 1 |
| `sceAgcDcbDrawIndex` + `DrawIndexAuto` | 224/s | 7 |
| `scePadRead` | 32/s | 1 |
| `sceMouseRead` | 64/s | 2 |
| `sceImeUpdate`, `sceUserServiceGetEvent` | 32/s | 1 |
| `sceAudioOut2ContextAdvance` | 105/s | ~3 |

Command buffers built, submitted, flipped; input polled; audio advanced - every frame, on time.
`libSceAgc` at ~10 k/s is the busiest library in the process.

**Two frames 45 s apart, pixel-diffed below the HUD, differ in exactly one 84x81 box at
(927,525)-(1011,606)** - the loading spinner. Every other pixel is identical. One animation runs
and nothing else advances.

## The blocker, stated precisely

`Unity.PSN.PS5.Main::Initialize` never returns. `PS5Manager.CurrentState` therefore never leaves
`Initializing(1)`, and `SplashLoader.<LoadScene>d__18::MoveNext` - which gates scene activation on
`CurrentState == Initialized(2)` - never releases the splash.

`PsnInitResult` is all zeros. Per the reasoning already recorded in `Ps5ManagerStateProbe.cs:172-186`,
an all-zero blob cannot be distinguished from the `.cctor` default, which means `Initialize` never
returned at all - as opposed to returning and reporting an error. Those are different bugs and this
is the first one.

**The constraint that narrows it:** the census counts **4 total `libSceNpManager` dispatches** for
the whole process lifetime, and **0** in any sampled interval. `Main::Initialize` is therefore
hanging *above* our HLE surface, inside Unity's managed PSN plugin, before it ever reaches
`libSceNp`. Adding or fixing NP exports cannot move this until something is shown to call them.

## What was falsified on 2026-07-27

Each of these was believed, is written down elsewhere in the docs, and is wrong. They are recorded
here because re-deriving a dead hypothesis is the main way time gets lost on this title.

**1. "The fail-fast is the native worker's `UnmanagedCallersOnly` thread-provenance hazard."**
Wrong. The crash is caused by `SHARPEMU_PS5MANAGER_PROBE=force` and nothing else:

| Config | Runs | Fail-fast |
|---|---|---|
| `force`, guest=1 main=1 | 4 | 4 |
| `force`, guest=1 main=0 | 2 | 2 |
| `force`, guest=0 main=1 | 2 | 2 |
| `force`, guest=0 main=0 | 2 | 2 |
| `off`, guest=0 main=0 | 2 | **0** - alive at title |

Eight for eight with the probe, zero without, across every native-worker combination. A fix aimed
at the thread-provenance theory (routing the two entries through a marshalled delegate instead of
`[UnmanagedCallersOnly]`) was built and measured: 4 of 4 runs still died. It was reverted.
`DirectExecutionBackend.cs:981` and `:6845` still describe the old theory and should be read with
this in mind.

**2. "Superliminal reaches the menu."** Only by forcing a lie into guest memory.
`Ps5ManagerStateProbe.cs:216-223` writes `CurrentState = 2` directly into the game's static field,
bypassing `Initialize`. The splash releases and the menu scene activates while PSN is half-built:

```
CurrentState=2  PsnInitialized=0  PsnInitResult=000000...000
psn_singletons [1..8] = NULL  (b12F=1, runs_init=1)
```

Eight of nine PSN subsystem singletons are NULL with their class-init guard byte already marked
finished. The menu scene dereferences one and the process dies. Runs that appeared to "work" were
the ones that had not touched a NULL singleton yet. **Menu-reached is not progress toward in-game**;
it is the emulator writing the answer the game was supposed to compute.

**3. "The main thread polls PSN forever."** No. See the 4-dispatch count above.

**4. "The background composite never lands."** It lands. It was reported as a property of the
renderer from a single run; three subsequent runs rendered the scene correctly.

**5. Several deadlock hypotheses** (GC/condvar aliasing, semaphore lost-wakeup, two-address
condvar, cross-thread stranding). All died the same way, and the census explains why in one line:
there was never a deadlock. The title dispatches ~22 k imports/s and flips at 32 Hz while
"stuck".

## Tools

**`SHARPEMU_IMPORT_CENSUS=1`** (`DirectExecutionBackend.ImportCensus.cs`, commit `18fab4d`) - tallies
every import at the single dispatch site and writes a sorted table every N seconds.

```
SHARPEMU_IMPORT_CENSUS=1
SHARPEMU_IMPORT_CENSUS_PATH=<file>        # default %TEMP%/sharpemu-import-census.txt
SHARPEMU_IMPORT_CENSUS_INTERVAL=<seconds> # default 10
```

Reports totals and a per-interval delta, by library and by export, with the last calling guest
thread handle. The delta column is the one that matters: since-boot totals are dominated by
startup and say nothing about what a title is stuck on.

It exists because the per-library `SHARPEMU_LOG_*` hooks cannot answer "what is the guest calling" -
only 6 of 26 NP files carry the `SHARPEMU_LOG_NP` gate, and within `NpManagerExports.cs` only 3 of
~50 exports call `TraceNp`. A subsystem the title never touches and one that simply has no trace
calls look identical. That ambiguity produced falsified item 3 above.

Dumps survive a crash: a dedicated thread writes on a timer, so the last interval before a
fail-fast is on disk.

## Harness notes

- **Environment variables do not persist between PowerShell tool invocations.** Every flag must be
  set in the same command as the launch, or the run silently uses a different configuration. One
  earlier "regression" was this.
- **This title is non-deterministic.** Run three times before believing a symptom. Several
  retracted findings above were single runs reported as properties.
- Screenshot comparison by non-black pixel count is a usable cheap identity check: the title screen
  is consistently `nonblack=1357031/1367100`.

## Next

1. **Find where `Main::Initialize` blocks inside Unity's managed PSN plugin.** It is above our HLE
   layer, so the tools are IL2CPP-side: the class/static chain the probe already resolves, plus the
   coroutine state machine at `<Initialize>d__84::MoveNext`.
2. **Use Kyty as the oracle.** It boots this exact title. Its call sequence through PSN init is the
   reference for what a completing `Initialize` looks like, and the census gives a comparable
   profile on our side, so the comparison is a directed diff rather than a hypothesis.
3. **Do not add NP exports for this title** until something is measured calling them.

## Known-defective, unrelated to this blocker

Real defects found while chasing the above, worth fixing on their own merits but **not** what is
holding Superliminal:

- `NpManagerExports.cs:101,118,129,140` - all four state-callback registrars discard their arguments
  and never reach `RegisterStateCallback` (`:705`), so `SHARPEMU_NP_FAKE_SIGNED_IN`'s callback
  delivery is unreachable dead code and `sceNpCheckCallback` can never fire.
- `NpManagerExports.cs:842` - a dozen request-consuming exports never move a request off `Ready`, so
  `sceNpPollAsync`/`sceNpWaitAsync` return `0x80550015 INVALID_ID` indefinitely. `sceNpWaitAsync`
  (`:352`) does not wait; it is an alias for poll.
- `NpManagerExports.cs:824` - completed request results are hard-coded to `SIGNED_OUT` regardless of
  `SHARPEMU_NP_FAKE_SIGNED_IN`, so the flag produces an incoherent pair: `sceNpGetState` reports
  signed in while `sceNpCheckNpAvailability` reports signed out.
- `NpWebApi2Exports.cs:558` - `_fakeUserContext` is declared and never read; `SHARPEMU_NP_FAKE_USERCTX`
  is a no-op in both directions.
- `NpCommonCompatExports.cs:173` - `sceNpCreateThread` returns a handle and starts nothing;
  `sceNpJoinThread` returns success immediately.
- `Gen5ShaderScalarEvaluator` - `invalid-load-address` on `SBufferLoadDwordx16` with
  `base=s72[0x2:0x0]` appears in menu-scene runs. An earlier attempt to lower the global-read floor
  patched the 4-argument `TryReadGlobalMemory`; the failing path calls the 5-argument overload.
