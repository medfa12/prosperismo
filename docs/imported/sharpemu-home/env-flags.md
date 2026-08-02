<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# SHARPEMU_* environment flags — working reference

> **⚠ DRIFT AUDIT 2026-07-24 — verify a flag exists before you plan a boot around it.**
> The tree actually defines far more flags than the ~150 once documented here: **306** as of
> 2026-07-25 (was 285 on 2026-07-24). A complete generated list is in
> **`docs/env-flags-generated.md`** — always regenerate with `python3 scripts/gen_env_flags.py`
> and trust that over any count written in prose, including this one.
>
> **These flags are documented below but are ABSENT from master** (verified by grep over `src/`):
> `SHARPEMU_CAPTURE_DRAWS`, `SHARPEMU_DUMP_SWAPCHAIN`, `SHARPEMU_PS_FORCE_EXPOSURE_SCALAR`,
> `SHARPEMU_PERF_PHASES`, `SHARPEMU_DRAW_TIMING`, `SHARPEMU_POOL_DRAW_OBJECTS`,
> `SHARPEMU_POOL_GUEST_ORCHESTRATORS`, `SHARPEMU_REUSE_GUEST_IMAGE_MEMORY`,
> `SHARPEMU_CACHE_RENDERPASS`, `SHARPEMU_DEEP_PIPELINE`, `SHARPEMU_RECOVER_UNBOUND_SMEM`.
> (`ASYNC_PRESENT` and `PS_FORCE_EXEC` were deliberately deleted; the rest most likely went with the
> 175-commit upstream catch-up merge `64db238`, which rewrote the presenter — *probable, not verified*.)
> **This matters:** the per-draw capture/dump diagnostics that produced most of this fork's render
> findings are currently NOT available on master. Re-add them (or their upstream equivalents) before
> planning a capture-based investigation.
> `SHARPEMU_COMPUTE_WRITEBACK` is correctly marked below as branch-only. `SHARPEMU_ASTRO_TONEMAP_FIX`
> and `SHARPEMU_FORCE_EXPOSURE` DO exist on master.

This documents the ones that matter for current bring-up work, grouped by
intent, so nobody has to reverse-engineer them from call sites. All are
default-off unless noted.

## Compatibility shims (needed to progress Astro Bot today)

| Flag | Effect |
|---|---|
| `SHARPEMU_NP_FAKE_SIGNED_IN=1` | `sceNpGetState` reports SIGNED_IN and `sceNpGetNpReachabilityState` reports Reachable. **It does NOT fire state callbacks** (corrected 2026-07-27): the delivery path hangs off `RegisterStateCallback` (`NpManagerExports.cs:705`), which none of the four registration exports call, so `_stateCallbacks` is always empty and `sceNpCheckCallback` always early-outs. Also note completed async requests still return `SIGNED_OUT` regardless of this flag (`:824`), so the flag leaves the sync and async views disagreeing. |
| `SHARPEMU_NP_FAKE_USERCTX=1` | **No-op (verified 2026-07-27).** `_fakeUserContext` is declared at `NpWebApi2Exports.cs:558` and never read; `sceNpWebApi2CreateUserContext` unconditionally returns a valid context, so the documented `NOT_SIGNED_IN` refusal path no longer exists in either direction. |
| `SHARPEMU_ASTRO_TONEMAP_FIX=1` | (branch `gpu/tonemap`, not yet on master) Title-scoped tonemap fix: decode GFX10 operand 125 as architectural NULL, recover the unbound SMEM cbuffer bindings the scalar evaluator zero-filled, and substitute a 0.25 auto-exposure. Turns the black display buffer fully non-black (VM-verified). Result is washed grey until real exposure lands — see `SHARPEMU_COMPUTE_WRITEBACK`. |
| `SHARPEMU_COMPUTE_WRITEBACK=1` | (branch, not yet on master) After a guest compute dispatch runs, copy its small writable outputs (1x1 R16F auto-exposure image, ≤4 KB global buffers) back into guest memory so the tonemap reads a REAL exposure instead of the 0.25 stopgap. Captures the guest sink at submit time (render thread has no CpuContext). Logs `[LOADER][WRITEBACK]`. |
| `SHARPEMU_RECOVER_UNBOUND_SMEM=1` | (branch) Generic form of the tonemap SMEM-binding recovery: bind reachable `s_buffer_load` PCs the scalar evaluator missed to the nearest same-descriptor binding instead of zero. `SHARPEMU_ASTRO_TONEMAP_FIX` applies it only to the known tonemap shader. |
| `SHARPEMU_PS_FORCE_EXPOSURE_SCALAR=<float>` | Legacy stopgap: pins the tonemap exposure scalar. SUPERSEDED — the real cause is the s125-NULL + missing-SMEM-bindings (see `docs/astrobot-bringup.md` "Tonemap black — ROOT CAUSE"), addressed by the two flags above. |

## Danger: flags that force guest state

| Flag | Effect |
|---|---|
| `SHARPEMU_PS5MANAGER_PROBE=force` | **Not a compatibility shim. It makes the process die.** Writes `PS5Manager.CurrentState = Initialized(2)` straight into Superliminal's static field (`Ps5ManagerStateProbe.cs:216-223`), releasing the splash while PSN is half-built (`PsnInitialized=0`, 8 of 9 subsystem singletons NULL with their class-init guard already set). The menu scene then dereferences a NULL singleton and the runtime fail-fasts with 0x80131506. Measured 2026-07-27: **8 of 8 runs died with the probe, 0 of 2 without**, across every native-worker flag combination. Use `SHARPEMU_PS5MANAGER_PROBE=1` (observe only) to read the state; `force` exists to answer "what is downstream of the gate", not to boot a title. See `docs/superliminal-status.md`. |

## Shader translation

| Flag | Effect |
|---|---|
| `SHARPEMU_SPIRV_GRAPHICS_WAVE` | Controls whether a VERTEX or PIXEL SPIR-V module models a real RDNA wave (subgroup vote/ballot/broadcast/shuffle over the host subgroup) or the historical one-lane model. Unset/anything else = **auto**, and auto now decides from three published device facts rather than from a proxy for them: (1) the host subgroup size must EQUAL the guest wave width being modelled - one host lane per guest lane, no bridge - which is 64 for pixel and 32 for vertex (EXTRACTED: GPU Shader Core ISA Specification SDK 12.000, "Wave32 and Wave64 Modes": wave32 is the default for every stage except pixel, and pixel is wave64); (2) `VkPhysicalDeviceSubgroupProperties::supportedStages` must contain FRAGMENT resp. VERTEX; (3) `::supportedOperations` must contain at least VOTE and BALLOT. All three come from `VulkanVideoPresenter.LoadComputeDeviceLimits` via `HostSubgroupSize` / `HostSubgroupSupportedStages` / `HostSubgroupSupportedOperations`. Anything not queried yet fails closed, and the refusal message distinguishes "never queried" from "the device was asked and said no". `0` = force the one-lane model. `1` = skip checks (2) and (3) - **not** (1), which is a property of this translator's own lane maths - for a device whose graphics-stage subgroup support was established elsewhere; if the properties were never queried this also re-arms the `UNVERIFIED` warning. Decided once per compile in `Gen5SpirvTranslator.EvaluateGraphicsSubgroupWave`; whichever way it goes is announced once per stage on stderr, naming both widths when they disagree. When the wave is declined the module falls back to the one-lane model and still compiles - it does not fail the draw - unless a CALLER explicitly declared a width this translator cannot model, which is still a hard refusal. Does not affect compute, which needs no flag: Vulkan guarantees the subgroup operations in COMPUTE, so a compute module decides on width alone. Two compute cases changed with this flag's arrival and are worth knowing: a wave32 dispatch on a host subgroup WIDER than 32 (i.e. every AMD host, which reports 64) now numbers lanes, ballots and shuffle ids relative to the guest wave the invocation sits in, instead of declaring host lanes 32 and above to be outside the wave - which used to leave them EXEC-disabled from the prologue onward, so half of every subgroup wrote nothing; and a wave64 dispatch on a 64-lane host is now modelled natively rather than through the half-wave approximation, whose mask composition described lanes 0..31 in both halves. A wave64 dispatch on a 32-lane host still uses the LDS bridge or that approximation. |

## Diagnostics (each proved its worth; costs noted)

| Flag | Effect / cost |
|---|---|
| `SHARPEMU_IMPORT_CENSUS=1` | Tallies **every** import at the single dispatch site and writes a sorted table on a timer (`DirectExecutionBackend.ImportCensus.cs`). Reports totals plus a per-interval delta, by library and by export, with the last calling guest thread. `SHARPEMU_IMPORT_CENSUS_PATH=<file>` (default `%TEMP%/sharpemu-import-census.txt`), `SHARPEMU_IMPORT_CENSUS_INTERVAL=<seconds>` (default 10). Survives a crash - a dedicated thread writes, so the last interval before a fail-fast is on disk. Use this instead of the per-library `SHARPEMU_LOG_*` gates when the question is "what is the guest calling": those gates cover a minority of exports, so a subsystem the title never touches is indistinguishable from one that simply has no trace calls. Negligible cost (one dictionary hit per dispatch). |
| `SHARPEMU_LOG_SYNC=1` | Runtime wait/signal trace of all guest sync HLE, tagged by guest thread. THE deadlock-hunting tool. Verbose but boot-speed-safe. |
| `SHARPEMU_DRAW_TIMING=1` | Per-draw sub-step timing (`[LOADER][DRAWTIME]`, incl. capFence/capReap split). Low overhead. |
| `SHARPEMU_PERF_PHASES=1` | Present-to-present frame-period phases incl. idleWaitForGuest (`[LOADER][PHASES]`). Low overhead. |
| `SHARPEMU_DUMP_SWAPCHAIN=1` | Presented-frame RGB dumps (`[LOADER][FRAMEDUMP]`, 24-frame budget). Decode with `scripts/framedecode.py`. |
| `SHARPEMU_TRACE_GUEST_IMAGE_ADDRS=0x..;0x..` | Dump specific guest surfaces (`[LOADER][GIMGDUMP]`). Moderate cost per addr. |
| `SHARPEMU_AUDIOOUT2_WAV_PATH=C:\path\capture.wav` | Diagnostic AudioOut2 tee. Creates a new 48 kHz stereo PCM16 WAV from the exact mixed snapshots submitted to the host boundary, even when no playback endpoint exists. Refuses to overwrite an existing file, stops before a partial snapshot at 256 MiB, and is off unless an explicit path is set. This proves waveform delivery; it does not make an endpoint-less host audible. |
| `SHARPEMU_TRACE_GUEST_IMAGE_DRAW_OCCURRENCE=N` | With `SHARPEMU_TRACE_GUEST_IMAGE_SHADER_ADDRS`, select the Nth matching draw/dispatch globally instead of advancing once per aliased descriptor. This is the exact-occurrence gate for paired same-draw target/input readback; use `SHARPEMU_TRACE_GUEST_IMAGE_OCCURRENCE` only for per-resource occurrence selection. |
| `SHARPEMU_TRACE_COMPUTE_IMAGE_MIN_WORK_SEQUENCE=N` | Suppresses both compute-image and selected compute-global-output readbacks until work sequence `N`. Recalibrate after performance fixes: work sequence is an ordering token, and a backlog reduction can make the same scene arrive at a lower value. |
| `SHARPEMU_GPU_TIMESTAMP_MIN_WORK_SEQUENCE=N` | With `SHARPEMU_LOG_GPU_TIMESTAMP=1`, delays per-draw Vulkan query allocation until work sequence `N`. Use it for a broad late-scene shader census without perturbing the several-minute movie/title boot. |
| `SHARPEMU_TRACE_GUEST_SURFACE_ORDER_ADDRS=0x..;0x..` | Logs every selected color/depth writer and sampled/storage reader for the listed guest addresses, including requested and resolved tile/DCC identity plus writer serial. Address-wide traces can be very noisy. |
| `SHARPEMU_TRACE_GUEST_SURFACE_ORDER_MIN_WORK_SEQUENCE=N` | Suppresses the surface-order trace until guest work sequence `N`. Use it with `SHARPEMU_TRACE_GUEST_SURFACE_ORDER_ADDRS` to inspect a late producer/consumer boundary without perturbing the entire boot. |
| `SHARPEMU_TRACE_GPU_MEMORY_ADDRESS=0x...` | Logs any parsed DMA_DATA or WRITE_DATA packet whose destination range overlaps the selected guest byte address, even when broad AGC logging is off. Diagnostic only; useful for proving whether a buffer initialization exists without the severe cost and volume of `SHARPEMU_LOG_AGC=1`. |
| `SHARPEMU_DUMP_SHADER_ADDR=0x5008F1400[,0x...]` | **Live again since 2026-07-28** (the old SPIR-V-base64 form died with `64db238`; this is a different, cheaper instrument). Decoded listing of a named shader on stderr, every line prefixed `[SHADERDUMP]` so it survives log filtering. One line per instruction (pc, encoding, opcode, dst/src, decoded control, raw words), then one block per **backward branch** (the loops) giving the branch target and `condition=` (SCC / VCC / EXEC / Unconditional, read off the branch mnemonic, never guessed), the nearest preceding instruction that writes **that** state (the guard), and for each guard operand the instruction that defined it plus `loopUpdate=` naming any redefinition inside the loop body. For a scalar load the operand line adds the descriptor registers (`s[b:b+1]` for `s_load_*`, `s[b:b+3]` for `s_buffer_load_*`) and the byte offset the value came from, which is how a wrong loop **bound** is read. When the branch is unconditional, its condition is not modelled, or nothing writes that state, the block prints `guard=none` / `guard=unresolved` with the reason instead of naming a nearby compare. Ends with `summary ... instructions=N backwardBranches=M`. Comma-separated list; each entry is hex with or without `0x`, and an entry that fits in 32 bits also matches the low half of the address (the guest prints the full 64-bit pointer, the GPU ledger prints it truncated), so `0x00000005008F1400`, `0x5008F1400`, `5008F1400` and `008F1400` all name the same program. Dumped once per address per process, so a shader recompiled at an address already dumped is not printed again. **Vulkan backend only**: emitted from `Gen5SpirvTranslator.CompilationContext.TryCompile`, so it prints for a shader that fails *inside* translation (a wave-width refusal, an unmodellable cross-lane op) but **not** for one that is refused before that context is built. **Silence is not evidence the shader is absent.** Four causes are byte-identical: a filter entry that does not parse or does not match; a pixel-layout refusal at `Gen5SpirvTranslator.cs:151-164`, which returns before the context exists; an upstream refusal such as `unsupported color target format` at `AgcExports.cs:7626`, before `TryCompilePixelShader` is reached at all; and a `_graphicsShaderCache` hit at `AgcExports.cs:7679`, where nothing is compiled so nothing is dumped (only the *first* compile of an address prints, and only once). Confirm the shader is actually being compiled before reading an empty log as an answer. The Metal backend has no call site and the flag does nothing there. Off unless set; no cost otherwise. |
| `SHARPEMU_TRACE_COMPUTE_TDR_BOUNDARY_CS=0x...` | **Diagnostic only; serializes the selected compute boundary.** Drains prior Vulkan work, submits and retires an empty sentinel, then logs the target SPIR-V SHA-256, local/subgroup shape, and every global buffer's guest/Vulkan identity and content hash before submitting the target. A loss before the sentinel is classified `observer_only`; a loss afterwards is `target_or_later`. Use one address per causality test. The waits materially change timing, so this is not a performance run or a workaround. |
| `SHARPEMU_SHADER_STEP_PROBE=0x<progAddr>` or `*` | Makes the SPIR-V PC-dispatcher's executed step count observable for one program (or all). Emits one `[SPIRV][STEPS]` host line naming the active limit and sink. Pixel stages encode `r=steps/limit`, `g=cap hit`, `b=(steps % 256)/255`, `a=1` into the lowest float MRT after the loop, destroying that draw's color output. Vertex and compute stages currently report `sink=none`; they have no non-destructive runtime sink. |
| `SHARPEMU_SHADER_MAX_STEPS=N` | Bounds the SPIR-V PC dispatcher; default `100000`, while `0` disables the bound. Reaching the cap is a diagnostic safety valve, not correct guest execution, and is silent unless separately instrumented. |
| `SHARPEMU_SHADER_CAP_PROBE=0x5008F1400` or `*` | **Diagnostic only; mutates the selected shader's first float pixel output.** Accepts one shader address or `*`, not a comma-separated list. At dispatcher exit it encodes final block low/high byte in R/G, exact-cap-hit in B, and `steps/limit` in A. The `[SPIRV][CAP-PROBE]` line prints the block-to-guest-PC map. Count surviving pixels with B=1 to prove the `SHARPEMU_SHADER_MAX_STEPS` valve was reached. The probe adds only a handful of instructions after the dispatch loop, but it destroys the selected color output and cannot report lanes already removed from EXEC. Off unless set. |
| `SHARPEMU_SHADER_CAP_PROBE_SGPR=s107` | With `SHARPEMU_SHADER_CAP_PROBE`, replaces the primary float output's RGB payload with the selected SGPR's low 24 bits (one byte per channel); alpha remains the exact cap-hit predicate. This deliberately uses the primary target because later MRTs can have narrower guest write masks. Diagnostic only; accepts a decimal register with optional `s` prefix. |
| `SHARPEMU_CAPTURE_DRAWS=1` | Authoritative per-draw VkImage readback. **~156 full GPU drains per frame — makes boots ~2 orders slower. Never combine with perf/progress measurements.** |
| `SHARPEMU_TRACE_GUEST_IMAGES=1` | Same cost warning as CAPTURE_DRAWS. |
| `SHARPEMU_LOG_IO=1`, `SHARPEMU_LOG_AMPR=1`/`_READS=1` | File-IO / AMPR command-buffer tracing (found the `~~N` variant batch-abort). |
| `SHARPEMU_LOG_NP=1`, `SHARPEMU_LOG_VIDEOOUT_FPS=1` | NP call trace; submitted/presented FPS lines. |

### Sound-bus investigation (2026-07-25)

| Flag | Effect / cost |
|---|---|
| `SHARPEMU_LOG_SOUND_STATE=1` | Dumps the sound object's whole state at the `SoundManager.cpp:306` bus check — vtable + slots, the `defaultBusses` header, the build-loop source container, the per-thread entry list, and the build gate. Also enables a 200 ms change-sampler on the source container that prints only when it changes (so it can distinguish "never written" from "written then cleared"). Needs `SHARPEMU_ASTRO_ASSERT_SKIP=1` for the one-shot dump. Cheap. |
| `SHARPEMU_ASTRO_DEFAULT_BUS_PROBE=1` | **Diagnostic probe, default OFF, NOT a fix.** Synthesises one placeholder element in the title's `defaultBusses` vector from the assert unwind so the boot advances past the audio gate and later blockers become visible. Hardcodes a title-specific singleton offset and element size; it cannot survive a game patch and generalises to no other title. |
| `SHARPEMU_LOG_SPEAKER_CALLER=1` | On the first few `sceAudioOut2GetSpeakerInfo` calls, logs the guest return address, `rbp`/`rsp`/out-pointer and 0x300 bytes of caller code as hex (decode offline with capstone). This is how the "speaker layout byte must be 1 or 2" contract was found. |
| `SHARPEMU_DUMP_GUEST_CODE=addr:len,...` | Companion to the above: dumps arbitrary guest regions as hex from the same hook (hex addr:len pairs, max 0x2000 each). Used to disassemble guest routines without a debugger. |
| `SHARPEMU_LOG_AUDIO=1` **and** `SHARPEMU_LOG_AUDIO_OUT2=1` | **Set BOTH.** They gate different traces with confusingly similar prefixes: `audioout2.port-get-state` / `audioout2.port_create` come from the first, `audio_out2.get-speaker-info` from the second. Grepping only `audio_out2\.` silently misses every port-state line — this produced a wrong conclusion once. |

> **Flags in the tables above that NO LONGER EXIST on master** (verified by grep over `src/`): `SHARPEMU_DRAW_TIMING`, `SHARPEMU_PERF_PHASES`, `SHARPEMU_DUMP_SWAPCHAIN`, `SHARPEMU_CAPTURE_DRAWS`, `SHARPEMU_PS_FORCE_EXPOSURE_SCALAR`, `SHARPEMU_POOL_DRAW_OBJECTS`, `SHARPEMU_RECOVER_UNBOUND_SMEM`, `SHARPEMU_COMPUTE_WRITEBACK`. They went with the 175-commit upstream merge `64db238`. Live equivalents: swapchain readback is `SHARPEMU_TRACE_GUEST_IMAGES=present` + `SHARPEMU_GUEST_IMAGE_DUMP_DIR=<dir>` (raw BGRA + a `nonblack_pixels` line); tonemap exposure is `SHARPEMU_ASTRO_TONEMAP_FIX` / `SHARPEMU_FORCE_EXPOSURE`. **Always grep a flag before recommending it**; the complete generated list is `docs/env-flags-generated.md`.
>
> Note `SHARPEMU_LOG_IO=1` is consumed (`KernelMemoryCompatExports.cs:7564`) but the actual open entry points (`PosixOpen`, `KernelOpenUnderscore`) never call that helper, so **guest file opens are currently untraceable**. Add a trace there before concluding anything about what the title loads.

## Module preload and import routing

These decide *which implementation serves a NID* and *whether a broken guest
module stops the boot*. The precedence rule they modify is written out in
`docs/astrobot-bringup.md` → "Import precedence" (short version: **our HLE wins
over a bundled `.prx` for every NID it implements**, except a 15-name libc
allowlist).

| Flag | Effect |
|---|---|
| `SHARPEMU_MODULE_PRELOAD_FAILURE=warn` | Downgrades the hard `ModulePreloadException` to a warning. By default (`SharpEmuRuntime.cs`, `ReportModulePreloadFailures`) a game-shipped `.prx` that fails to load **and** orphans at least one eboot import — an import no HLE export and no other loaded module can serve — stops the boot, naming the module, the cause and the orphaned NIDs. Set this only for a deliberately degraded boot; those imports will resolve to nothing. |
| `SHARPEMU_PRELOAD_ALL_SCE_MODULES=1` | Preloads every adjacent `.prx`/`.sprx` including the ones normally skipped (`libkernel.prx`, `libkernel_sys.prx`, `SharpEmuRuntime.PreloadSkipModules`). |
| `SHARPEMU_DISABLE_LLE_LIBC=1` | Never route a libc export to the bundled module — the HLE serves all of them. Same effect as `SHARPEMU_LLE_LIBC_SAFE_ONLY=off\|false\|none`. |
| `SHARPEMU_LLE_LIBC_ALL=1` | Route **every** libc NID our table also implements to the bundled module. Checked *before* the allocator gate (`PreferLleForLibcExport`), so the allocator family goes LLE even if `CanUseLleLibcAllocatorFamily` would have rejected it — this is the one setting that can hand the title a split heap. |
| `SHARPEMU_LLE_LIBC_SAFE_ONLY=0` | Also widens LLE to every libc export, but is checked *after* the allocator gate, so `malloc`/`free`/… still go LLE only when the whole allocator family resolves. Prefer this over `_ALL=1`. |
| `SHARPEMU_LLE_LIBC_SAFE_ONLY=1` | Explicitly the default: the 7 `IsSafeLleLibcExport` names plus the 8-name allocator family when it fully resolves. |

## Perf experiments (env-gated, effect measured; keep until superseded)

| Flag | Status |
|---|---|
| `SHARPEMU_POOL_DRAW_OBJECTS=1` | Pools per-draw Vulkan objects (fences, command buffers, descriptor pools, per-draw texture image/view/memory) across draws instead of destroy-per-reap; adds LRU + stats to the host-buffer pool. Targets the measured ~570 ms/draw capReap. `[POOLSTATS]` line every 64 draws proves engagement. Not yet boot-verified. |
| `SHARPEMU_REUSE_GUEST_IMAGE_MEMORY=1` | Guest-image device-memory reuse pool. Structurally cannot engage during pure load (no destroys happen); POOLDBG `releases=` counter proves activity either way. Relevant only once eviction/replacement fires. |
| `SHARPEMU_CACHE_RENDERPASS=1` | Caches transient render passes/framebuffers. Correct, but measured cost lives in the reap (capReap), so gain was small. |
| `SHARPEMU_DEEP_PIPELINE=1` | Raises submission/work caps 8/16→32/48. No measured effect (retire speed dominates). |
| `SHARPEMU_POOL_GUEST_ORCHESTRATORS=1` | Pools guest-orchestrator host threads. No measured effect on frame time. |

Removed (do not resurrect without new evidence): `SHARPEMU_ASYNC_PRESENT`
(frames-in-flight present ring; liveness bug, and present measured at 2 ms —
recover from git history at 778838f/e3d1bdf if ever needed),
`SHARPEMU_PS_FORCE_EXEC` (hypothesis refuted before use).
