<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Astro Bot (PPSA21564) bring-up notes

Working state of the Astro Bot bring-up effort on this fork. Read this before
touching the boot/render path — the "eliminated" table below exists so nobody
re-runs a dead hypothesis.

> **⚠ STALE — updated state as of 2026-07-24.** The section below is the 2026-07-22 snapshot. Since
> then ~30 commits (`acf729d..5f5f729`) worked the `SoundManager.cpp:306 defaultBusses.size()==1`
> assert named below:
> - **Audio contract regressions found + fixed** (`fc6a2d0`, `7b98f36`): a refactor had silently
>   replaced real SDK struct layouts with invented blobs (`SceAudioOut2SpeakerInfo` 0x50→0x20,
>   `sceAudioOutGetPortState` truncated to 0x10, Ngs2 buffer-info 0x40→0x18). All tests stayed green.
> - **Assert-frame unwinding** built in the CPU backend (VEH + return sentinel), plus a *diagnostic*
>   injection of a placeholder bus into the SoundManager singleton at `+0x2730` (`dded509`) — a probe
>   to advance the boot, **not a fix** (Astro-specific offsets, guessed element size).
> - **Likely root cause identified, NOT yet tested:** `defaultBusses` is empty because
>   `sceAudioOut2PortGetState` reports no port as active — our impl writes `0x01` at offset 0 with
>   **bit 7 clear** and zero-clobbers the struct, while the firmware treats offset 0 as a bitfield
>   (bit 7 = active/ready, connected-state `0x104`). See `docs/handoff-2026-07-24.md` UPDATE 1+2.
>   `AudioOut2Exports.cs:783` is still unchanged — testing that bit is the next cheap experiment.
> - **Diagnostic-flag regression:** `SHARPEMU_CAPTURE_DRAWS` / `DUMP_SWAPCHAIN` /
>   `PS_FORCE_EXPOSURE_SCALAR` referenced throughout this doc are **no longer present on master**
>   (see the drift banner in `docs/env-flags.md`).

## Current state (2026-07-25) — supersedes the 2026-07-22 section below

**Astro Bot does not reach its menu.** It halts at `SoundManager.cpp:306`
(`defaultBusses.size() == 1`). With that stepped over by the opt-in probe it
renders a **static** pre-menu screen (grey plus a progress bar) at ~2 FPS and
advances no further — a masked pixel measurement shows the bar byte-identical
across minutes, so it is genuinely wedged, not loading slowly.

The renderer is proven working: it presents real frames continuously. Earlier
"black screen" readings were an artifact — `SHARPEMU_DUMP_VIDEOOUT`'s BMP reads
the *guest* framebuffer, which is always black. Use
`SHARPEMU_TRACE_GUEST_IMAGES=present` to see what is actually presented.

### The audio wall, traced end to end (every link measured, not inferred)

```
producer of singleton+0x2660 never runs   (200 ms change-sampler: never written,
                                           not written-then-cleared)
  -> build routine 0x800DC0500's 0x28-stride loop iterates ZERO times
  -> the sole &defaultBusses append (0x800DC0B20) never executes
  -> end - begin != 0x18 -> assert at SoundManager.cpp:306
```

Guest addresses (image base `0x800000000`):

- `0x800F5B110` — per-tick readiness check; loads its object from global
  `0x80E754C70`, gate byte at `0x80E754C68`, size test `0x800F5B14A..5B15C`.
- `0x800DC0500` — the bus **build** routine, called per tick from `0x800F3E4D2`.
  Its entry gate `cmp byte [rdi+0x2900],0` is only an **idempotence latch** (set
  on *normal* completion at `0x800DC130E`, cleared by reset at `0x800DC3296`) —
  it is not a failure flag.
- `0x800DC0B20` — `lea rax,[rbx+0x2728]`, the only `&defaultBusses` handoff.
- `0x800DBF930` — the class's virtual destructor (vtable slot 0); its body tears
  down `defaultBusses`. Owner vtable is `0x8089187A8` and has only two non-null
  slots, so there is no virtual interface to enumerate.

Member layout of that object (do **not** conflate these three):

| Offset | Role | Observed |
|---|---|---|
| `+0x2660/+0x2668` | build-loop **input** vector; 0x28-byte elements each embedding a `std::string` (buf `+0x08`, size `+0x18`, cap `+0x20`) — named, config-shaped descriptors | **always NULL** |
| `+0x2730/+0x2738/+0x2740` | `defaultBusses`; 0x18-byte elements with a refcounted pointer at element `+0x08` | **always 0/0/0** |
| `+0x2770/+0x2778` | **per-thread** context registry keyed by `scePthreadGetthreadid`; `+0x10` is a THREAD ID, not a bus id | 9 entries |

**The one open question: what populates `singleton+0x2660`.** It is written
nowhere in the class, `0x2660` is too generic to scan for globally, and the class
exposes no virtuals. The instrument that would answer it is a call-trace filtered
on the owner pointer, enumerating every method invoked on it during early boot.

### Audio contract fixes landed this session (all firmware-verified)

- `PortStateSize` **0x20 -> 0x40** — we were writing half the struct, so callers
  read their own uninitialised stack. Copy-out proven at `0x16d16..0x16d30`.
- **Speaker layout byte**: the engine reads `speakerinfo[0x00]` and accepts only
  1 or 2; we wrote 0. Proven by disassembling the caller and confirming
  `rbp - out = 0x80`.
- **Connected bit is `0x40`, not `0x01`** — the engine does `shr al,6 / and al,1`.
  Bit 7 is "ready" and firmware only ever *clears* it, so a healthy port arrives
  with it set.
- **Every bed port reports connectivity**, not just MAIN: it is a property of the
  output *device*. Object ports (`type & 0x100`) stay clear.
- **User-id gate**: we whitelisted `{0,1,255,1000}` while our own user service
  hands out `0x10000000`, so no audio port was ever created. Firmware rejects
  only `-1`.
- **NpWebApi2 registries**: `CreateUserContext` / `PushEventCreateFilter` /
  `CreatePushEventHandle` handed out ids they never registered while six
  consumers gated on those permanently-empty dictionaries.

### Static analysis of the eboot (this replaces guess-and-boot)

`eboot.bin` is a SELF; the embedded ELF starts at file offset `0x1a0`. ELF
segment 0's data sits at SELF offset `0x3b1f0` and loads at guest base
`0x800000000`, so `file_off = 0x3b1f0 + (guest - 0x800000000)` (byte-verified
against a live memory dump). PLT base is `0x8074E7190` with 16-byte entries, so
`plt_index = (target - 0x8074E7190) / 16` -> the import list -> NID -> name.
Tooling: `scripts/nid_resolver.py` (100% calibrated, 32,520/32,520 oracle pairs),
`scripts/nid_names.tsv`, `scripts/eboot_imports.py`,
`scripts/astro_import_routing.tsv`.

Import surface: **1732 imports = 690 HLE + 930 LLE + 112 unserved**, verified
exactly against the loader's `lle_redirects=933`. Only two LLE modules exist —
the game's own `libc.prx` and `libSceNpCppWebApi.prx`; **no firmware module is
LLE-loaded**, so never assume "firmware handles it". Stubs are keyed by *bare*
NID (`SelfLoader.ExtractNid`, `src/SharpEmu.Core/Loader/SelfLoader.cs:2371`,
truncates the dynamic-symbol string `NID#libId#modId` at the first `#`). None of
the 112 unserved imports has ever been observed executing.

### Import precedence: **our HLE wins over LLE**, bar a 15-name libc allowlist

> ⚠️ **CORRECTION (2026-07-25).** This section previously read "**LLE wins over
> our HLE** when a NID exists in a preloaded `.prx`". That is the exact
> **opposite** of what the code does. Any conclusion drawn from the old sentence
> about which implementation serves a NID needs re-checking.

The rule lives in `DirectExecutionBackend.TryResolveDirectImportTarget`
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:1694`). Returning
`true` means "patch the stub straight at guest code" (LLE); returning `false`
means "leave the stub on the HLE trampoline" (HLE). In order:

1. `kernel_dynlib_dlsym`, and the two NIDs in `IsHlePreferredNid` — `QrZZdJ8XsX0`
   and `Q3VBxCXhUHs` — → **HLE**, unconditionally (`:1698`, `:1702`).
2. **NID is in the HLE export table** (`_moduleManager.TryGetExport`, `:1707`):
   - library name contains `Kernel` → **HLE** (`:1709`, `IsKernelLibrary` `:1785`);
   - library name does **not** contain `libc` → **HLE** (`:1717`);
   - libc, but the export name is not on the allowlist → **HLE** (`:1717`);
   - libc **and** allowlisted → LLE if a usable, executable guest symbol exists
     for it, otherwise HLE (`:1721`–`:1736`).
3. **NID is not in the HLE export table** → **LLE** whenever a preloaded `.prx`
   defines it (`:1744`). This is where the bulk of the LLE routing comes from.
4. Not in either, but Aerolib knows a name for the NID → the same libc allowlist
   gate, then a name-based symbol lookup (`:1755`–`:1770`).

**So an HLE implementation shadows a bundled `.prx` for every NID it covers.**
LLE serves what our HLE does *not* implement, plus the allowlist. Deleting an
HLE export is what hands a NID to the guest module, not the other way round.

The allowlist is `PreferLleForLibcExport` (`:1796`) — 15 names in two groups:

- `IsSafeLleLibcExport` (`:1877`), 7 names: `memmove`, `memcmp`, `_Getpctype`,
  `_Getptolower`, `_Getptoupper`, `puts`, `malloc_stats_fast`. (`memcpy`/`memset`
  are deliberately *not* here — they are served by guarded native intrinsics.)
- `IsLibcAllocatorExport` (`:1861`), 8 names: `malloc`, `free`, `calloc`,
  `realloc`, `memalign`, `aligned_alloc`, `posix_memalign`,
  `malloc_usable_size` — routed LLE **only if** `CanUseLleLibcAllocatorFamily`
  (`:1832`) resolves all seven core allocator NIDs in the bundled module, so the
  title can never end up with a split heap.

Overrides: `SHARPEMU_DISABLE_LLE_LIBC=1`, or `SHARPEMU_LLE_LIBC_SAFE_ONLY` set to
`off`/`false`/`none`, disables libc LLE entirely (everything falls back to HLE).
`SHARPEMU_LLE_LIBC_SAFE_ONLY=0` widens LLE to every libc export our table also
implements, *keeping* the allocator-family gate. `SHARPEMU_LLE_LIBC_ALL=1` does
the same but is tested at `:1813`, **before** the allocator gate at `:1817` — so
it is the one setting that can route part of the allocator family to the bundled
module without the rest, i.e. hand the title a split heap. Prefer
`SHARPEMU_LLE_LIBC_SAFE_ONLY=0`.

**Cross-check against the routing data** (join of
`scripts/astro_import_routing.tsv` on `scripts/our_nids.tsv` by NID column,
`awk`, 2026-07-25). Of the 1732 imports:

| In our HLE table? | Routed | Count |
|---|---|---|
| yes | HLE | 690 |
| yes | LLE | **13** |
| yes | unserved | 6 |
| no | LLE | 917 |
| no | unserved | 106 |

Those 13 are *exactly* allowlist names: `malloc`, `free`, `calloc`, `realloc`,
`memalign`, `aligned_alloc`, `posix_memalign`, `memcmp`, `memmove`, `puts`,
`_Getpctype`, `_Getptolower`, `_Getptoupper`. (`malloc_usable_size` and
`malloc_stats_fast` are on the allowlist but Astro imports neither.) **Outside
that allowlist, there is no NID our HLE implements that a bundled `.prx` wins.**
The totals reconcile with the line above: 690 HLE, 13 + 917 = 930 LLE,
6 + 106 = 112 unserved.

One caveat on that join, stated rather than smoothed over: the 6 rows that are in
`our_nids.tsv` yet marked `UNSERVED` in the routing table are
`sceAgcDriverAgrSubmitDcb`, `sceAgcDebugRaiseException`, `sceAgcCbDispatchGetSize`,
`sceAgcCbSetShRegisterRangeDirectGetSize`, `sceAgcAcbCopyData`, `sceAgcDcbCopyData`.
`astro_import_routing.tsv` was written 2026-07-24 17:49 and `our_nids.tsv`
2026-07-25 04:52, so the most likely reading is that those exports landed between
the two snapshots — but that is inference, not measurement. Re-derive the routing
table before treating any of the 112 "unserved" as still unserved.

### A `.prx` that fails to preload is now a hard error, not a counter

`SharpEmuRuntime.LoadAdjacentSceModules` used to swallow every load failure into
a `failed=N` summary line, which is how 800 imports could silently degrade and
kill the title much later with nothing pointing back at the load. It now (a)
names every failure with its cause the moment it happens — including the
zero-byte / oversized / vanished-file cases that previously hit a bare
`continue` with no message at all — and (b) attributes each failure to the
eboot's own import surface.

Attribution: the failed module's bytes are scanned for the
`<11-char NID>#<libId>#<modId>` strings its dynamic string table holds, that set
is intersected with the eboot's imports (PLT stub map ∪ imported-data
relocations), and the result is narrowed to the NIDs **no** HLE export and **no**
successfully loaded module can serve. A non-empty result throws
`ModulePreloadException` naming the module, the reason and the orphaned NIDs; an
empty one is a warning. That is what keeps a *broken* optional Unity plugin in
`Media/Plugins` non-fatal — an *absent* one is never enumerated in the first
place, since the loop only walks files that exist — while making a broken
`libSceNpCppWebApi.prx` or `libc.prx` stop the boot at the point of damage.
`SHARPEMU_MODULE_PRELOAD_FAILURE=warn` downgrades the throw for a deliberately
degraded boot.

Note the loader currently discards import-**library** attribution entirely:
`ExtractNid` throws away the `#libId#modId` suffix, and `ParseDynamicInfo`
(`SelfLoader.cs:1825`, the `switch (tag)` over dynamic tags) has no case for
`DT_SCE_NEEDED_MODULE` (`0x61000045`) or `DT_SCE_IMPORT_LIB` (`0x61000049`) —
`grep -n '61000045\|61000049' src/SharpEmu.Core/Loader/SelfLoader.cs` returns
nothing. That is why the attribution works by NID intersection rather than by
library name. Parsing those two tags in `SelfLoader` and surfacing them on
`SelfImage` would make it exact, and is the single highest-value follow-up here.

> **Superseded again by source-triggered 2026-07-31 evidence.** DCC lifetime
> preservation and storage-view metadata propagation now carry real pixels to
> the final source. In unmodified run `20260731-210748-corpus-gate`,
> `ps=0x500640D00` sampled 2,146,458 nonblack RGBA16F pixels and wrote
> 4,951,550 nonblack A2R10G10B10 pixels on the same draw. PrintWindow captured
> the real PlayStation Studios image, but it is severely too dark. The current
> issue is luminance/color correctness, not a uniformly black postprocess
> chain. The fixed draw-ordinal-400 black capture occurred during a black
> source lifetime and is retained only as a methodological dead end. All 350
> instructions in the exact final pixel program and all 69 in its paired vertex
> program match Sony's SDK oracle by mnemonic and size; this does not by itself
> prove every operand or numerical semantic.
>
> **Superseded by measured 2026-07-29 evidence.** The three-part tonemap
> diagnosis below is historical, not the current root cause. On current
> `master`, an exact per-PC audit of the actual final tonemap
> (`ps=0x500640D00`) reports all 15 reachable scalar-memory sites directly
> covered and `smem_zero_filled=0`. The dynamic 1x1 state at `0x556760000` is
> valid; `0x532830000` is an intentionally opaque-black auxiliary input in
> that allocation lifetime. A same-draw GPU readback proves the main HDR input
> `0x53B9F0000` is already opaque black before tonemap. A paired upstream
> compute capture then proves `0x537060000` is already all zero and the compute
> pass merely makes alpha opaque. The skipped mode-6 DCC operation immediately
> before that boundary is a live missing semantic, but zero surface bytes,
> zero metadata, and zero clear words mean it is not yet a proven cause of
> missing RGB. Separately, two surviving fragments of `ps=0x5008F1400`
> demonstrably hit the default 100,000-step dispatcher cap inside its second
> bindless EXEC loop, explaining the GPU backlog and ~0.1 FPS. See the latest
> section of `docs/astro-bot-boot.md`. Do not reintroduce either recovery flag
> or fabricate exposure from this stale section.
>
> **2026-07-29 continuation.** Architectural `ds_append` GDS lowering plus
> scheduling-only handling of `s_waitcnt_vscnt` restores the dropped list
> producer: `0x5006EC700` now writes `0xFFFFFFFF` empty-list sentinels and
> `1,1,1` indirect arguments. The queue drains from a transient peak instead
> of remaining near 501, and the HUD improves to about 1.9 FPS / 556 ms, but
> the frame remains black. Ordered metadata evidence also shows
> `0x537060000` reaches mode 6 with `writer=none` and zero guest/host contents;
> this exact surface is missing an earlier writer, not losing a live nonblack
> DCC image. See the newest sections of `docs/astro-bot-boot.md` for artifact
> paths and the still-unmeasured scene-state pair.
>
> **SDK 10 correction and 2026-07-29 runtime result.** Sony's Prospero SDK now
> supplies the authoritative contracts. `CB_COLOR_CONTROL.MODE=0x60` really is
> DCC decompression, but the measured `0x53AA00000` occurrence preserves an
> initialized GPU-authored expanded Vulkan image, so that operation is not the
> cause at this boundary. `CxVgtShaderStagesEn` also proves Astro's
> `es=0x50011FC00` runs as an enabled wave64 GS. Commits `1299e9b` and
> `6b89866` remove its one-lane fallback, but the verified guest capture remains
> RGB-black (`0/2,025,600` nonblack, alpha 255 throughout). The remaining
> independent defect is native primitive amplification: Sony defines target 20
> as three 10-bit connectivity indices, while this shader changes point input
> to triangles, may output 190 vertices from a 19-vertex group, exports target
> 20 from `v40`, and SharpEmu drops it. See the top section of
> `docs/astro-bot-boot.md`; do not revive the historical tonemap flags.

## Historical state (2026-07-22; superseded above)

- Boots stable, soft-continues past engine asserts (audio-propagation, font,
  and the still-open `SoundManager.cpp:306 defaultBusses.size()==1` audio-bus
  assert), presents 4K guest frames, loads all menu assets.
- **The black-screen tonemap root cause is now FOUND and firmware-confirmed
  (see below).** With the fix, the display buffer 0x507410000 goes from
  0-nonblack to fully filled — the presented frame is no longer black, but a
  washed grey (the exposure is still a stopgap; see the two remaining items).
- NOT yet reached: the true interactive menu render. Two gates remain, both
  under active work on branches: (a) the SoundManager default-bus audio gate,
  (b) real auto-exposure so the tonemap produces the correct image instead of
  washed grey.

### Tonemap black — ROOT CAUSE (2026-07-22, cross-validated two ways)

Confirmed both empirically (per-draw capture on the T4) and against the
decrypted PS5 firmware (`libSceAgcDriver`/oracle disassembly). The tonemap
pixel shader read its gamut/exposure constant buffers as ZERO because of two
independent bugs plus a missing input:

1. **GFX10 scalar operand 125 is architectural NULL, not a mutable `s125`.**
   Our SMEM decoder treated encoding 125 as a real register, which offset the
   base of *every* constant-buffer read in the shader. Fixed: decode 125 as
   constant zero (ISA-correct; matches Kyty + the firmware disassembly).
2. **The shader scalar evaluator recorded only ONE `s_buffer_load` binding
   (PC 0x38) and zero-filled the destinations of every other reachable SMEM
   load.** So the gamut constants (e.g. s0=0.1915, s1=-0.57 at cbuffer
   byte 48) read 0 → the grade cancelled the scene to black. The proper fix
   is CFG-based resource discovery in `Gen5ShaderScalarEvaluator.cs`;
   `SHARPEMU_RECOVER_UNBOUND_SMEM` / `SHARPEMU_ASTRO_TONEMAP_FIX` recover the
   nearest same-descriptor binding as an interim.
3. **The 1x1 auto-exposure luminance at ~0x532830000 is never written back to
   guest memory** (the game's luminance-reduction compute runs on-GPU but its
   output stayed in device memory). The tonemap sampled zero exposure. Real
   fix is compute→guest-memory writeback (`SHARPEMU_COMPUTE_WRITEBACK`);
   `SHARPEMU_ASTRO_TONEMAP_FIX` substitutes a 0.25 constant as a stopgap
   (hence the washed grey).

## Fixed root causes (keep these in mind, they were hard-won)

| Fix | Commit | Mechanism |
|---|---|---|
| Tonemap outputs black | `a55d1c9`+`8bdc546` (stopgap); root cause 2026-07-22 (branch `gpu/tonemap`) | Originally patched with `SHARPEMU_PS_FORCE_EXPOSURE_SCALAR`. The actual root feed is now known and firmware-confirmed — the s125-NULL decode + zero-filled SMEM cbuffer bindings + un-written-back auto-exposure (see "Tonemap black — ROOT CAUSE" above). |
| Online-init loop | `ec0de11`+`dafb50a` | NP state machine reported SIGNED_OUT via 4 coupled signals; the title retries forever. `SHARPEMU_NP_FAKE_SIGNED_IN=1` + `SHARPEMU_NP_FAKE_USERCTX=1` make it coherently signed-in (incl. firing the registered state callback). |
| Post-load total deadlock | `8ab12f4` | Condvar signal-stealing lost-wakeup in PthreadCondWaitCore: a thread signaling then immediately re-waiting on the same cond consumed the token meant for an older waiter (DrawThread/Draw-Extra-Geometry handshake). Fixed default-on with an epoch guard; covered by KernelPthreadCondvarTests. |
| APR resolve batch-abort | `691b790` | One missing `~~N` variant file aborted registration of a whole resolve batch. Now registers what resolves. |

## Eliminated hypotheses — 2026-07-25 additions (DO NOT RE-RUN)

All of these were falsified with measurements, not abandoned:

- **NP blocks the main thread.** False — a full thread dump shows no deadlock
  anywhere and the main thread is not parked at all. The game-side log simply
  goes quiet after `NpJobFailed: UserProfileGetPublicProfiles 0x80553502`, which
  the pre-merge tree also hits and survives.
- **The audio thread is deadlocked on event flag 4.** False — that flag is set
  7,826 times with 7,853 wakes. What looked like a lost wakeup was a healthy
  wait/wake/work cycle.
- **A startup ordering race (check runs before the audio thread ticks).** False —
  the audio thread has ticked before the assert. The earlier "zero calls before
  the assert" came from a log filter that missed `audioout2.port-get-state`
  (see the prefix trap in the flags doc).
- **GPU resource churn / a retry storm.** False — ~1.2 `sceAgcDriverRegisterResource`
  calls per frame is normal, and our return `0x8A6C9018` is byte-identical to
  firmware (`mov eax,0x8a6c9018; ret`, 6 bytes). Do not "fix" it.
- **Pure throughput (it is just slow).** False — ~700 frames rendered with zero
  pixels of progress-bar movement.
- **The libSceJson2 iterator gap.** False — 0 of the 15 NIDs is ever called.
  (Also: our Json model has no Array/Object container storage at all; it is a
  build-and-serialise model, so iterators would need a new container first.)
- **Two SoundManager instances (fill on one, check the other).** False — the
  global and the unwound object are the same pointer.
- **`%ASOBI_ROOT%` / a missing guest `getenv`.** False — the title imports no
  environment-variable API whatsoever (`getenv`, `sceKernelGetenv`, `setenv`,
  `putenv`, … all absent from its import table).
- **"Bus id 1 is missing."** Void — the ids in that list are *thread* ids from a
  per-thread registry, not bus indices.

## Eliminated hypotheses — DO NOT RE-RUN

Rendering-black (each disproven by measurement, mostly via
`SHARPEMU_CAPTURE_DRAWS=1` per-draw VkImage readback):
geometry/transform collapse; s107/VRcp NaN; EXEC mask zero at export
(measured TRUE via forced select); exposure 1x1 TEXTURE at 0x532830000
(forcing it non-zero *alone* changed nothing — but see below, it IS one of
three coupled causes); HDR10 A2R10G10B10 output format; cbuffer re-read race;
viewport 1x1 clip; post-draw clear; input aliasing; present-selection as the
cause of the black tonemap output.

⚠️ CORRECTION (2026-07-22): "unbound-smem zeroing" was previously listed here
as eliminated — that was WRONG. Recovering the unbound SMEM cbuffer bindings
(plus the s125-NULL decode) is exactly what fixed the black tonemap. See the
"Tonemap black — ROOT CAUSE" section above. And forcing the 1x1 exposure alone
"changed nothing" only because the missing cbuffer bindings *independently*
produced zero/NaN — with the bindings recovered, the exposure DOES matter.

Performance ~0.5 FPS (each disproven by instrumented boots):
GPU sync/present flush (measured gpuFenceWait=0.0ms, present=2ms);
Windows timer quantum (already 1 ms via HostTimerResolution, PR #130);
guest-orchestrator thread churn (pooling it changed nothing);
VRAM pressure / allocation reuse ("gross" is a cumulative counter, live is
flat ~700 MB; and DestroyGuestImage never fires during pure load, so the
reuse pool structurally cannot engage — see POOLDBG `releases=` counter).

MEASURED remaining perf cost: `capReap` ~570 ms/draw — CPU-side per-draw
Vulkan object teardown in the submission reap (image views, command buffers,
fences, buffers destroyed every draw). `capFence=0`: the GPU itself is fast.

## Next steps, ranked

1. ⚠️ **GONE FROM MASTER (verified 2026-07-25) — this step is not actionable as
   written.** It described per-draw Vulkan object pooling "landed opt-in as
   `SHARPEMU_POOL_DRAW_OBJECTS=1` (partial
   `VulkanVideoPresenter.DrawObjectPool.cs`)" with a `[POOLSTATS]` line to check
   first. On master: `grep -r SHARPEMU_POOL_DRAW_OBJECTS src/` → 0 hits,
   `grep -r POOLSTATS src/` → 0 hits, and `find src -name '*DrawObjectPool*'`
   returns only `src/SharpEmu.Tests/DrawObjectPoolMathTests.cs` — a project that
   is **not in `SharpEmu.slnx`**, so CI (`dotnet test SharpEmu.slnx`,
   `.github/workflows/workflow.yml:115`) never builds or runs it. The pooling
   work most likely went with the 175-commit upstream merge `64db238`; recover it
   from history or re-derive it before planning a capReap measurement. The
   measured target below (~570 ms/draw in the reap) still stands.
2. Present-election black: DIAGNOSED (code-read, high conf) — the election
   is NOT broken. The game flips 0x507410000/0x5093F0000, written only by the
   tonemap ps=0x500640200, which multiplies by the zero exposure scalar; with
   the exposure stopgap unset the elected buffer is legitimately black and
   faithfully blitted (the nodeadlock1/scene1 boots did not set the flag). The
   scene target 0x520440000 is an intermediate that never enters the election.
   ⚠️ The discriminating boot originally written here used
   `SHARPEMU_PS_FORCE_EXPOSURE_SCALAR` + `SHARPEMU_CAPTURE_DRAWS` +
   `SHARPEMU_DUMP_SWAPCHAIN`; **all three are absent from master** (0 hits each
   under `src/`). The live equivalents are `SHARPEMU_ASTRO_TONEMAP_FIX` /
   `SHARPEMU_FORCE_EXPOSURE` for the exposure and
   `SHARPEMU_TRACE_GUEST_IMAGES=present` (+ `SHARPEMU_GUEST_IMAGE_DUMP_DIR`) for
   the presented frame; there is no per-draw capture on master, so the
   `[CAPTURE].*ps=…` in0/out0 discrimination cannot be run until one is re-added.
   See the drift banner in `docs/env-flags.md`.
3. ANSWERED (2026-07-22): why the exposure/gamut cbuffer scalars read 0 — the
   s125-NULL decode + zero-filled SMEM bindings (see ROOT CAUSE above). Proper
   fixes in flight: CFG-based SMEM resource discovery in
   `Gen5ShaderScalarEvaluator.cs` and `SHARPEMU_COMPUTE_WRITEBACK` for real
   auto-exposure. Still open: the `SoundManager` default-bus audio gate, and
   why libScePad never loads (what gates the interactive gamemode).
4. Watch run-to-run variance — if threads park again, use
   `SHARPEMU_LOG_SYNC=1` (see methodology) to find the next lost wakeup.

## Methodology — 2026-07-25 additions (each learned by being wrong)

Fourteen conclusions were retracted in a single session. The recurring shapes:

1. **Absence of log output is not evidence.** Before concluding "X never
   happens", verify X *would* have been logged: that the flag is consumed **and**
   that the specific code path calls the logger. This caused most of the wrong
   conclusions. Concrete trap: `SHARPEMU_LOG_IO` is consumed but `PosixOpen` /
   `KernelOpenUnderscore` never call it, so "the title opens only one file" was
   an artifact of missing instrumentation, not a finding.
2. **A static "who calls this" argument is not sufficient — confirm with a
   runtime hit count.** This killed the libSceJson2 iterator lead (0 of 15 NIDs
   ever called) before implementing 15 functions plus a container model for
   code that never runs.
3. **A `std::vector` passed to an out-of-line helper is addressed as
   `[base+0x10]`=end, `[base+0x18]`=cap — the pointer handed around is
   `begin - 8`.** Scan for `begin-8` as well as `begin`. Missing this hid the
   `defaultBusses` append site for most of a session.
4. **When locating instructions by disp32 byte-pattern, back-track
   longest-first.** Starting one byte late strips a REX prefix and still decodes
   plausibly (`mov r14,[rbx+X]` becomes `mov esi,[rbx+X]`), and capstone's
   `disp_offset` anchor is satisfied by both. Tell-tales: impossible
   instructions (`lea esp,...`), r8–r15 collapsing to 32-bit names, addresses
   off by one. Also: requiring the displacement to be the *last* bytes silently
   drops every form with a trailing immediate.
5. **Compute rip-relative targets as `nextInsn + disp`** — never read the
   displacement itself as an address.
6. **Vtables and RTTI cannot be read statically from the eboot.** The file image
   is zero-filled; they are populated by RELA relocations at load. Read vtables
   at runtime.
7. **Measure a specific field; do not eyeball screenshots.** Two "the progress
   bar advanced" readings were false — a masked pixel-span measurement showed it
   byte-identical across minutes, and the whole-frame diff had only picked up the
   perf overlay's own digits.
8. **Ask the import table whether the title even calls something before
   implementing it.** With the NID resolver this is a two-second lookup.

## Methodology (learned the expensive way)

- Measure before fixing. Two multi-boot detours (AMPR zero-fill, GPU-sync)
  came from acting on a plausible theory without confirming the actual code
  path / phase timing first. ⚠️ `SHARPEMU_DRAW_TIMING` and `SHARPEMU_PERF_PHASES`,
  the flags that made one boot attribute cost precisely, are **absent from
  master** (0 hits each under `src/`, 2026-07-25) — the attribution instrument
  has to be re-added before the next perf claim.
- NEVER run progress/perf boots with `SHARPEMU_TRACE_GUEST_IMAGES` (or
  `SHARPEMU_CAPTURE_DRAWS`, if it is ever restored — it is absent from master)
  — they QueueWaitIdle per draw (~156x/frame) and invalidate all timing (and
  slowed a week of boots once).
- Deadlock hunts: boot with `SHARPEMU_LOG_SYNC=1`, grep `[LOADER][SYNC]`,
  find threads whose last action is a park with no later wake, then trace the
  `owner=` of the mutex they block on.
- Frame inspection: `[LOADER][FRAMEDUMP]`/`[GIMGDUMP]` base64 → PNG via
  `scripts/framedecode.py`; target a specific surface with
  `SHARPEMU_TRACE_GUEST_IMAGE_ADDRS=0x<addr>[;0x<addr>]`.

## Boot loop (GCP VM) — ⚠ SUPERSEDED, see the next section

**This VM no longer exists under this identity.** The current VM is `astro-vm` in project `pfe-ey`
(see "fast VM iteration" below). Kept only for the metadata-watcher mechanics.

VM `sharpemu-t4` (project plated-life-480308-b1, us-central1-a), Windows +
T4-vWS, persistent 500 GB disk holding the game dump at
`C:\games\astrobot-rar\PPSA21564-app`, ffmpeg (Bink2 build) at
`C:\ffmpeg\bin`, VB-Audio Virtual Cable installed. A watcher on the VM polls
instance metadata key `sharpemu-job`; `scripts/vm-fastboot.sh` drives the
zip→upload→job→poll loop (see script header for usage). The VM is normally
STOPPED (start it first); Spot preemption is common — retry.

## Boot loop — fast VM iteration (2026-07-22, supersedes the metadata-watcher path above)

The current, much faster loop uses a real git checkout on the VM + incremental
build instead of tarball upload + full publish:

- VM `astro-vm` (project `pfe-ey`, us-central1-a, Windows 11 + T4-vWS). NOTE the
  external IP is EPHEMERAL and changes on every (re)start (spot preemption is
  common) — update `scripts/vm-astro.sh` and the git remotes after each start.
- ONE build on the VM: `C:\r1` is a git repo (push target,
  `receive.denyCurrentBranch=updateInstead`) built incrementally to
  `C:\r1\artifacts\bin\Release\net10.0\win-x64\SharpEmu.exe`. `C:\dotnet` = SDK,
  `C:\mingit` = git, `C:\glfw3.dll` = the loose glfw the build copies in.
- `scripts/vm-astro.sh <r1> <secs> "<ENV k=v;k=v>"` from a worktree pushes HEAD
  (delta, ~1s), builds incrementally on the VM (~10–25s vs ~90s publish), boots
  Astro Bot in the interactive session for `<secs>`, and prints render signals.
  The GLFW window needs an interactive session (headless CopyFromScreen returns
  blank) — use `SHARPEMU_DUMP_SWAPCHAIN=1` + `scripts/framedecode.py` for a
  reliable frame instead of a desktop screenshot.

## Firmware "oracle" — replace stubs with correct implementations (2026-07-22)

> ⚠️ **PATH CORRECTED (2026-07-25).** This section named
> `games/ps5-403-oracle/filesystems/merged/`, which **does not exist in this
> tree** (`ls games/` → `PS5_4.03_reconstructed`, `psdevwiki_ps5`, and the keys
> directory). The real root is `games/PS5_4.03_reconstructed/filesystems/`, which
> is also the ground truth `scripts/fw_exports.py` documents in its own header
> (line 4: `games/PS5_4.03_reconstructed/filesystems/{system,system_ex}`).

`games/PS5_4.03_reconstructed/filesystems/{system,system_ex}` is a decrypted PS5
4.03 firmware. Use it as ground truth:

- `find … -iname '*.sprx' -o -iname '*.prx' -o -iname '*.elf'` under
  `filesystems/` returns **584** files; `scripts/fw_encrypted.txt` lists the
  **38** that are still encrypted and cannot be mined.
- `python3 scripts/fw_exports.py` → `scripts/fw_exports.tsv` (oracle_index.py is SUPERSEDED; its output was wrong — see that file's header).
  The checked-in `fw_exports.tsv` currently holds **297,359 export rows /
  283,419 distinct NIDs across 538 module files and 687 export libraries**
  (`wc -l`, plus `awk -F'\t'` distinct-counts on columns 1/3/4, 2026-07-25). The
  earlier "279k exported symbols / 569 native modules" figures do not match the
  current file.
- `python3 scripts/oracle_disasm.py <NID>…` → x86-64 disassembly of the real
  function. Classify (pure-logic / syscall-wrapper / ioctl), extract the
  OBSERVABLE contract (struct field offsets, return/error code per branch,
  side effects) and reimplement faithfully. Match what the game observes; verify
  two ways (disasm ground truth + boot behaviour). Some "stubs" are stubs in the
  firmware too (e.g. `sceAgcDriverRegisterResource` = 6 bytes returning
  `0x8A6C9018`).
- **There is no `games/PS5_SDK_4_00` in this tree** — the official Sony SDK is
  not checked in, so do not plan a step around naming exports from its headers.
  `inspiration/ps5-payload-sdk` carries signatures only; `scripts/nid_names.tsv`
  and `games/psdevwiki_ps5/wikitext/` are the naming sources that actually exist.

> Keeping this doc current: when a boot-blocking hypothesis is confirmed or
> refuted, or a `SHARPEMU_*` flag is added, update the sections above AND
> `docs/env-flags.md` in the same change. The living detail lives in the
> maintainer's memory map; this file is the shareable summary.
