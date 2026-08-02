# Astro Bot boot state

## 2026-08-01: inactive-lane LDS stores poisoned the clustered-light list

The persistent `ps=0x5008F1400` cost and its black lighting input had one
shared producer defect. A synchronized pre-fix readback of
`cs=0x5006E8500` at work sequence 1036906 found that its node pool was no
longer uniformly zero, but every node's `next` field was zero. The resulting
head table therefore pointed at node zero, whose next link pointed back to
itself. That is the exact data shape required to keep `0x5008F1400` in its
GPU-built-list loop until SharpEmu's 100,000-step dispatcher limit.

Sony SDK 10.00's `libSceShaderIsaP.dll` closes the producer contract. The
shader compares its local index with zero, lets only lane zero initialize the
LDS record at byte offset `0x510` to `{-1, 0, 0}`, inserts nodes with
`ds_wrxchg_rtn_b32`, and finally publishes the head with `ds_read_b32`.
SharpEmu lowered the lane-zero write as an ordinary load, a select between the
new and old value, and an unconditional store from every host invocation.
The 63 EXEC-inactive lanes could consequently race the active lane and write
the old zero back over its `0xFFFFFFFF` sentinel. This is a second LDS defect,
independent of the previously fixed high-byte offset decode.

Ordinary LDS loads and stores now share the Vulkan workgroup-atomic memory
domain with DS atomics, and a predicated store emits no write at all for an
inactive lane. Focused SPIR-V tests require `OpAtomicLoad` for an ordinary DS
read and three conditional `OpAtomicExchange` operations for a B96 write; the
DS atomic suite and those tests pass 37/37. A clean Release build succeeds
with zero warnings.

The first post-fix corpus run,
`artifacts/game-runs/astro/20260801-035524-corpus-gate`, reached LOGO at
96.719 seconds, TITLE at 254.328 seconds, and worldmap load at 331.328 seconds
without device loss. Its 262 timestamped `0x5008F1400` draws had a 0.238 ms
mean and 0.236 ms median (0.022--0.562 ms), versus the retained 132.039 ms mean
and 132.766 ms median. The approximately 560x reduction proves that the old
cost was the malformed list walk, not fragment count or a generic Vulkan
pixel-shader bottleneck.

Two targeted follow-ups close that checkpoint without claiming an interactive
worldmap. Run `20260801-040717-corpus-gate` captured the first corrected
`0x5006E8500` lifetime at work sequence 985406. Head binding 9 contained
52,288/129,600 nonzero bytes with architectural `0xFFFFFFFF` empty-list
sentinels, and binding 14 contained 16,139 nonzero bytes with populated node
indices. The node pool at binding 6 was zero in this first lifetime, so it is
not by itself evidence of a failure; the mixed sentinel/index head tables are
the discriminating change from the pre-fix self-linked-zero output.

That run also established that the CPU-side worldmap-load log falls between
presenter-side `0x5008F1400` occurrences 108 and 109. Its occurrence-24
target was therefore definitely pre-worldmap. Run
`20260801-041532-corpus-gate` selected occurrence 132 after the CPU-side
`LevelDocument Loaded: worldmap` message and read the exact 1920x1080 RGBA16F
target `0x514080000`. It contained 1,260,420/2,073,600
nonblack pixels with hash `0x8E7726F9AAD446AC`, but mean RGB was only
`(0.0000546, 0.002689, 0.012399)`, maximum RGB only
`(0.00398, 0.01147, 0.11749)`, and zero pixels exceeded 1.0. The lighting
writer therefore executes and preserves real color at this occurrence; it
does not write a healthy HDR magnitude.

`PrintWindow(PW_RENDERFULLCONTENT)` from both follow-ups shows the same
severely dark PlayStation Studios composition with blue strips and radial
geometry after the guest has loaded worldmap. The HUD still reports a queue
above 550, so this is stale logo-era presentation rather than evidence that
worldmap visibly rendered. The same backlog means CPU-log order alone cannot
prove that occurrence 132 contains worldmap commands; calling it a
"scene-era" capture would overstate the evidence. The next root-cause boundary
is a content-triggered lighting/color pair, with work-sequence and writer
identity recorded on the readback. Dropped target residency and a uniformly
black read are eliminated for the measured `0x5008F1400` occurrence only.

A performance fix also changes the work sequence reached at a wall-clock
milestone: the old late gate of 1035400 never fired because the corrected run
reached only about 996028. Recalibrate such gates from the immediately
preceding run rather than treating backlog-derived sequence numbers as stable.

## 2026-08-01: the remaining queue is host resource materialization, not a GPU shader

After the LDS fix, a late-gated broad timestamp census in
`artifacts/game-runs/astro/20260801-042552-corpus-gate` measured 2,811
offscreen draws. No replacement for the old `0x5008F1400` spin appeared.
`0x5008F1400` itself averaged 0.242 ms GPU total and 0.218 ms render across
336 samples. The largest individual GPU draw was 11.619 ms, and the largest
cumulative family (`0x5001C2A00`) accounted for only 222.178 ms across 156
draws. Those measurements cannot explain a roughly 1.4-second frame or a
queue above 550.

The paired host timings do. Pixel shader `0x500006E00` executed 663 times,
averaged 18.943 ms of host setup, reached 76.340 ms, and consumed 12.559
seconds cumulatively while its GPU total averaged 0.191 ms. The next five
host-heavy families added another 8.093 seconds. This is producer-side queue
growth: guest work is enqueued faster than SharpEmu can construct its Vulkan
resources, while the GPU consumes submitted draws quickly.

A phase-isolated diagnostic run,
`artifacts/game-runs/astro/20260801-043336-corpus-gate`, stopped deliberately
after TITLE once the selected boundary had fired; its missing WORLDMAP is a
short-run result, not a renderer regression. Repeated `0x500006E00` draws with
17--22 textures and 24--27 global buffers spent roughly 8--20 ms resolving
textures and 13--16 ms materializing global buffers. Descriptor allocation
was normally about 0.01 ms and cached pipeline lookup about 0.01 ms. One
measured draw spent 71.725 ms total: 13.913 ms in textures, 51.448 ms in
globals, 4.137 ms in geometry resources, 0.023 ms in descriptors, and 2.204
ms in pipeline creation/lookup. Optimizing descriptor or shader pipelines is
therefore the wrong next move. The next implementation boundary is reuse or
generation-aware refresh of CPU textures and guest global-buffer snapshots.

### Address-zero fallback caching removes the texture half of that cost

Run `artifacts/game-runs/astro/20260801-045738-corpus-gate` validates a
general resource-lifetime fix. A null Sony image descriptor is represented by
a deterministic 1x1 fallback image. Its guest address is necessarily zero,
but its complete descriptor identity is stable; recreating an image, device
allocation, view, and staging allocation for every binding added no guest
semantics. Vulkan now caches those fallback images by the same complete
descriptor identity as ordinary sampled textures. Synthetic address-zero
resources which are not fallbacks remain transient. Texture staging buffers
also use the fence-retired host-buffer pool and are released after the first
cached upload retires instead of living for the entire texture-cache lifetime.

For `ps=0x500006E00`, the 510-sample pre-fix run
`20260801-044205-corpus-gate` averaged 8.324 uploads and 9.803 ms in texture
materialization per draw (median 10.037 ms, maximum 23.627 ms). The 588-sample
post-fix run averaged 0.024 uploads and 0.042 ms (median 0.019 ms); the steady
state reports `texture_uploads=0` and 8--13 cached fallbacks. Mean total host
setup for this shader fell from 17.937 ms to 10.432 ms. This is a measured
removal of repeated host work, not a claim that all frame time is fixed:
global-buffer handling remains about 3--12 ms for most 46--47 MiB binding
sets, with larger outliers.

The corpus gate reached LOGO at 59.282 seconds, TITLE at 212.063 seconds, and
worldmap load at 292.813 seconds without device loss. The clean Release binary
SHA-256 was
`D4AD38A23A2E8B181B63E3562573D180308503D73E0791F209B3E275E2882501`.
`worldmap-printwindow.png` is a `PrintWindow(PW_RENDERFULLCONTENT)` capture;
it still shows the severely dark PlayStation Studios composition with a queue
near 564, not an interactive worldmap. The fix improves producer throughput
and does not close the stale/dim presentation boundary.

An attempted global-buffer shortcut is retained as a dead end. Reusing
`GuestImageWriteTracker` to page-protect every global-buffer range and skip an
exact byte comparison when its write generation was unchanged caused two
reproducible pre-LOGO CLR failures at about 2.45 million imports:
`Invalid Program: attempted to call a UnmanagedCallersOnly method from managed
code` in `ExecuteGuestContinuationEntry` (runs `20260801-045513` and
`20260801-045605`). Removing only that tracking change let the same path pass
16 million imports and complete every milestone. Generic GPU buffer ranges can
overlap CPU continuation or other unified-VA state; texture-cache protection
cannot be widened to them without a VM-level ownership/protection contract.
The unsafe change is not present in the code.

## 2026-07-31: source-triggered proof closes the black postprocess boundary

The current unmodified final tonemap produces real RGB when its live source
contains real RGB. This supersedes fixed-ordinal captures that sampled the same
shader during an earlier black lifetime.

Run `artifacts/game-runs/astro/20260731-210748-corpus-gate` triggered a paired
readback only when `ps=0x500640D00` sampled nonblack source `0x53C420000`.
At work sequence 992427 the 2432x1368 RGBA16F source contained 2,146,458
nonblack pixels. The same unmodified draw wrote 4,951,550/8,294,400 nonblack
pixels to 3840x2160 A2R10G10B10 target `0x5093F0000`; the target hash was
`0x6B41D60FCFEA4828`. No exposure, scalar-memory, texture, or export override
was enabled. The earlier draw-ordinal-400 result was real but nondiscriminating:
its source was black at that occurrence, so its black target did not identify
a shader or presentation defect.

The same live run produced
`printwindow-nonblack-tonemap.png` with
`PrintWindow(PW_RENDERFULLCONTENT)`. It visibly contains the PlayStation
Studios logo, blue vertical strips, and the dark navy right-side field. The HUD
is emulator-owned and is not counted as guest evidence. The guest image is
therefore no longer uniformly black, but its luminance/color is severely wrong.
The next rendering checkpoint is a content-referenced color/exposure
differential, not another binary black/nonblack probe.

The shader itself has also been bounded offline. The exact 2,088-byte pixel
program was recovered as
`artifacts/game-runs/astro/20260731-204643-corpus-gate/ps-500640D00.bin`
(SHA-256
`E3D4D0FD0D1189365FAAD2AACF2263D7F6C402F00382B523BBD86CEA31EC07E7`).
All 350 pixel instructions and all 69 instructions in its paired vertex program
match Sony SDK 10.00's `libSceShaderIsaP.dll` by mnemonic and size. The final
pixel sequence packs `v10/v12` and `v9/1.0` to FP16 and performs a compressed,
done MRT0 export from `v0/v1`. This eliminates outer decode and instruction
boundary disagreement for this pair; it does not claim that every ALU operand
or numerical semantic has been differentially proven.

The corpus gate reached LOGO at 59.375 seconds, TITLE at 218.328 seconds, and
worldmap load at 295.890 seconds. It missed no baseline milestone and produced
no Vulkan device loss. Only the two exact processes launched for this run were
stopped after the capture, and the harness finalized its manifest normally.

## 2026-07-31: DCC preservation carries real postprocess pixels into 65D500

Two consecutive host-side losses are now identified and corrected.

In pre-fix run `artifacts/game-runs/astro/20260731-190833-corpus-gate`,
`ps=0x5006CB800` wrote color image `0x53AA00000` at work sequence 994242,
writer serial 35948. The mode-6 metadata operation at sequence 994264 then
cleared the whole expanded Vulkan image and replaced it with writer serial
35949, even though the host image already contained newer GPU-authored pixels.
Sony SDK 10.00 defines mode 6 as DCC decompression with implicit FMASK
decompression and fast-clear elimination. That operation expands metadata
state; it does not replace pixels written by a newer draw with the target's
clear words. SharpEmu now preserves an initialized GPU-authored image in this
case. Constant-DCC materialization remains a separate writer-ordered path, so
this does not turn a different DCC lifetime into a compatible alias.

Run `artifacts/game-runs/astro/20260731-193230-corpus-gate` captured the
corrected chain at work sequence 998854. The `68FA` input `0x537060000` had
2,152,834/3,326,976 nonblack RGBA16F pixels. Writable `0x53AA00000` contained
1,333,889 nonblack pixels before dispatch and 2,146,458 afterward; its output
hash changed from `0x96CA322E41AA2A90` to `0x5D7483680EFBA7AE`. This proves
that `6CB800 -> mode 6 -> 6CE200 -> 68FA` carries real color. It is not a
PrintWindow result.

The same run exposed the next exact loss. `ps=0x50065D500` requested the
2432x1368 tile-27 image at `0x53AA00000` with DCC metadata
`0x57054E000`, but selected an uninitialized tile-0 cached upload with writer
serial zero. `ResolveStorageGuestImage` had discarded the storage texture's
DCC address while constructing its host-image binding. SDK 10.00
`agc/gnmp/texture.h` states that a texture aliasing a DCC render target points
at that target's DCC metadata buffer. The storage path now carries that field
through unchanged; exact-DCC alias rejection remains in force.

Targeted corpus run
`artifacts/game-runs/astro/20260731-195610-corpus-gate` used Release binary
SHA-256
`B32FCF1189FF9DAE37B67E102EBA9615991CC345DEA92A55E24EFFB1E3DDDE58`.
It reached LOGO at 62.906 seconds, TITLE at 221.531 seconds, and worldmap load
at 299.156 seconds, with no device loss. At sequences 998863, 1001899, and
1004917, `65D500` selected the initialized tile-27 GPU color image; requested
and resolved DCC were both `0x57054E000`, with nonzero writer serials 33364,
33712, and 34073. The harness finalized successfully after only this run's
two SharpEmu processes were stopped; it reports no missed baseline milestone.

Three preceding attempts are retained as dead ends, not renderer evidence.
Runs `194352`, `194652`, and `194841` exited `0xFFFFFFFF` after 43.469,
10.250, and 2.234 seconds respectively, all before LOGO and without a Vulkan
loss or an explicit managed/native exception. The last stopped during HLE
warm-up. A concurrent BOM-only edit then correctly tripped corpus-gate's
stale-binary guard; rebuilding the stable shared tree produced the successful
run above. Do not attribute those startup exits to `65D500` or count them as a
black-frame differential.

This closed the first measured full-resolution postprocess resource-selection
loss. The later source-triggered tonemap pair and PrintWindow capture above now
close the binary black-frame question as well. They do not close the visibly
incorrect luminance/color or prove interactive worldmap rendering.

## 2026-07-31: a plain production boot validates the Windows chunk-pacing fix

Corpus-gate run `artifacts/game-runs/astro/20260731-185733-corpus-gate`
used committed head `830d202`, the normal corpus environment, and Release
binary SHA-256
`81E766875A5803CE1D5B48218BB957A1A338B90A85C2C637E779933F15409F20`.
There was no TDR-boundary probe or other added diagnostic override. It reached
LOGO at 61.875 seconds, TITLE at 225.125 seconds, and
`LevelDocument Loaded: worldmap` at 309.516 seconds. The complete 480-second
observation contained no `Vulkan device lost`, and Windows produced no new
WATCHDOG dump after the pre-fix `WATCHDOG-20260731-1853.dmp`.

This is the required positive differential for the fix below. Waiting for
each non-final compute-chunk fence on Windows prevents the continuously queued
32-chunk `cs=0x500571000` workload from exceeding a WDDM scheduling interval;
`cs=0x5006EAC00` was only the next API observer of the old engine reset. The
corpus harness reports exit code 1 because the anti-milestone improvement is
not automatically recorded, not because the emulator failed: it explicitly
reports that no baseline milestone was missed.

The run manifest disabled capture (`capture_attempts=0`). It therefore proves
runtime progress through worldmap and removal of the old device-loss boundary,
but it does **not** prove a fresh nonblack title or worldmap frame. The measured
postprocess content boundary remains the nonblack `6CE200` output followed by
the unmeasured live `68FA` consumer described below.

## 2026-07-31: the repeated `6EAC00` reports are Windows watchdog resets, not shader attribution

The four recent failures at 16:39, 16:41, 18:07, and 18:36 each have a
matching Windows live-kernel dump under
`C:\Windows\LiveKernelReports\WATCHDOG`. Windows classifies the failures as
`LiveKernelEvent 141`; the dumps identify `SharpEmu.exe` and the video TDR
path. This is stronger than Vulkan's empty device-fault reply: WDDM reset the
GPU engine after a watchdog timeout, so `VK_EXT_device_fault` had no shader
address to report.

The submit named in the emulator log is not necessarily the guilty packet.
Run `20260731-162942` reached TITLE with the same executable fingerprint and
the same initial 5,462,508-byte Vulkan pipeline cache as the immediately
following `20260731-163746` and `20260731-164015` failures. The same-size
`6EAC00` SPIR-V has both retired successfully and appeared at a failed
`vkQueueSubmit`. In the two newest clean failures the logical ledger was empty,
but the last substantial physical-queue work was the 32-chunk
`cs=0x500571000` dispatch. `6EAC00` was the next Vulkan call to observe the
reset; retained evidence does not prove that its bounded 54-instruction
program caused it.

An opt-in discriminator now exists under
`SHARPEMU_TRACE_COMPUTE_TDR_BOUNDARY_CS`. For a selected compute address it
drains all prior submissions, submits and retires an empty named sentinel,
then records the target SPIR-V SHA-256, local/subgroup sizes, and every global
binding's guest identity, Vulkan handle/range, access flags, and content hash
before submitting the target. A loss during the drain or sentinel is reported
as `observer_only`; a loss after the sentinel is `target_or_later`.

Run `artifacts/game-runs/astro/20260731-185113-corpus-gate` used committed
head `ff3a51c`, runtime fingerprint
`34C1409434E013FAAC09F35FED5F53FB5002101AE35C961F61E3BEA9AADF826B`, and
selected `0x5006EAC00`. It reached `ps_logo` and retired 193 sentinel-separated
occurrences of that module. At the failing occurrence the probe began with 32
pending submissions. The drain completed every fence; the ledger's last entry
was the final chunk of `cs=0x500571000`, and even the oldest of those chunks had
occupied the queue interval for 2,533.300 ms. The subsequent *empty sentinel's*
`vkQueueSubmit` returned `ErrorDeviceLost` before a new `6EAC00` command buffer
was recorded. The probe classified this `observer_only`, and Windows created
`WATCHDOG-20260731-1853.dmp`. This is the causal boundary: `6EAC00` is not the
hanging packet; the back-to-back 32-chunk `571000` workload exceeds the Windows
watchdog interval.

Separate Vulkan command buffers and submissions were insufficient because all
32 were enqueued without a host scheduling boundary. On Windows, a chunked
compute dispatch now waits for each non-final chunk fence before enqueueing the
next. Other hosts retain asynchronous chunk submission. The guest dispatch
still uses `vkCmdDispatchBase`, one shared resource set, and ordered shader
barriers; the new wait changes host scheduling only. This fix has a zero-warning
Release build and the complete 2,560-test suite passes. The plain production
run above validates it through worldmap without device loss.

## 2026-07-31: add-TID/DCC-lifetime/NGG build still loses Vulkan at 6EAC00

Plain corpus-gate run
`artifacts/game-runs/astro/20260731-183337-corpus-gate` used committed head
`84a049a` and freshly rebuilt binary SHA-256
`B97F0CE822DDF4E7AD17DF9566DD10411942ADEFF1B742749C097C447BAF9B6F`.
The Release build had zero warnings and the full 2,554-test suite passed. No
diagnostic environment override was added beyond the corpus manifest.

The run reached `Level has started: ps_logo` at 173.000 seconds, then reported
Vulkan device loss at 193.062 seconds before TITLE or worldmap. The failing
submission was again `cs=0x5006EAC00`, sequence 448898. At the exception the
ledger reported `submit_timeline == completed_timeline == 53559`, zero in-flight
submissions, and a signaled retirement for `cs=0x500571000`. Device-fault query
again succeeded with no fault address or vendor record. Only this run's exact
emulator PIDs were stopped after that terminal evidence.

This run did not reach the later `cs=0x555F41F00` occurrence: there are zero
occurrences of that address and zero old `unknown-ds op=0xB0` rejects in the
log. It therefore validates startup compatibility of the general changes but
does not dynamically exercise the new add-TID lowering. Capture was disabled by
the corpus manifest, so this is not a pixel or black-frame result. The next
falsifiable Astro checkpoint remains the differential descriptor/data state at
the otherwise-bounded `6EAC00` submission, not GDS reset semantics.

## 2026-07-31: clean alignment build reaches LOGO, then loses Vulkan at 6EAC00

Run `artifacts/game-runs/astro/20260731-180513-corpus-gate` used the freshly
cleaned and rebuilt runtime (SHA-256
`25B622CC3E33B201111DC996AB38D7119B1A0908C74F4ABC617F710DCB36B0B4`).
The complete solution build had zero warnings and all 2,542 tests passed. The
run reached `Level has started: ps_logo` at 116.532 seconds, then
`vkQueueSubmit` for `cs=0x5006EAC00` returned `ErrorDeviceLost` at 131.516
seconds. It was stopped immediately by exact PID; TITLE and worldmap were not
reached, so this run did not measure the `68FA` output and is not a black-frame
result.

The failure ledger is unusually discriminating: `submit_timeline` and
`completed_timeline` were both 41919, `in_flight=0`, and the last named
retirement was the signaled 8160x1x1 `cs=0x500571000`. The device-fault query
succeeded but reported no address or vendor record. This supersedes the older
wording that `6EAC00` necessarily observed a predecessor that was already
lost; in this run the submit call itself is the first reported loss.

A Sony-SDK differential found no GDS defect to repair. SharpEmu matches the
append/consume range, byte offsets, ordered counter clear, wave reservation,
and old-counter broadcast. The exact `6EAC00` program is only 54 instructions,
has no backward branch, has previously written all four terminators, and has
retired successfully in other retained runs. The remaining differential is
runtime resource/descriptor content or Windows driver/TDR state. Also keep
the evidence boundary explicit: the translator's compile-time warning for
`s_trap pc=0x1f80` in `571000` proves that the instruction exists, not that a
runtime lane reaches it.

Offline follow-up found one separate title-reached shader rejection in prior
logs: `cs=0x555F41F00` rejected `op=0xB0 word=0xDAC00700`. Sony's SDK 10
generation-2 ISA oracle decodes the exact bytes as
`ds_write_addtid_b32 v5 offset:0x700`; it also accepts the paired add-TID read.
SharpEmu now decodes and lowers the pair with the documented LDS address
`M0[15:0] + thread_id*4 + offset16`. This has not yet been through a new Astro
boot and is not asserted to explain the `6EAC00` device loss.

The same offline pass removed the remaining Astro constants from the bounded
direct-export amplified-NGG replay admission path. Subgroup counts, output
budget, amplification, parameter stride, and target-20 slot allocation now
come from submitted registers and decoded exports. The general boundary is
still explicit: wave32/multi-wave, GS instancing, user launch VGPRs, culling,
emit/cut, and arbitrary topology conversion are unsupported.

## 2026-07-31: paired TAA-lite capture proves 6CE200 preserves real color

Run `artifacts/game-runs/astro/20260731-162942-corpus-gate` captured both
sides of one selected `ps=0x5006CE200` draw at occurrence 261/work sequence
`987555`. It reached LOGO, TITLE, and worldmap document load without device
loss and was deliberately stopped as soon as both raw files were complete.

The source selected at `0x53AA00000` is the expected 1920x1080 RGBA16F
tile-27 image:

```text
bytes=16,588,800
nonblack RGB pixels=1,333,889 / 2,073,600
SHA-256=8B301C0EF384488AD6DAB801067655F55FBDDA63CE4FEDDA31AD81B1B4791DB5
```

The draw's `0x537060000` MRT is a real nonblack 2432x1368 RGBA16F image:

```text
bytes=26,615,808
nonblack RGB pixels=2,129,899 / 3,326,976
SHA-256=8C791FDB84E15171F524BE354EA2FA9DFD5E3CCBE80753C9E2C24570B1433423
alpha half bits=0x3C00 (1.0) for all 3,326,976 pixels
```

Therefore `6CE200` does not turn Astro's scene black. It preserves and
upscales real color while writing opaque alpha. The first-loss boundary moves
to the immediately following `cs=0x50068FA00`: its sampled
`0x537060000` input and writable `0x53AA00000` output must be captured in this
same current-title lifetime. Historical `68FA` captures used an older/black
input lifetime and cannot override this paired result.

Two chained follow-up attempts did not reach that consumer capture:
`20260731-163746-corpus-gate` and `20260731-164015-corpus-gate` both lost the
Vulkan device shortly after LOGO while submitting `cs=0x5006EAC00`, before
the nonblack producer trigger armed. The exact failures were submission
708/work `228995` and submission 771/work `252076`. This is a repeated,
precisely named probe-run blocker, not evidence about `68FA` pixels. Do not
classify either run as slow or wait through it; stop after the device-loss line.
The two immediately preceding targeted runs on the same binary reached TITLE
and worldmap without device loss, so the new failure also remains differential
rather than a general baseline claim.

## 2026-07-31: fresh title lifetime routes the nonblack composite through 6CE200, not 690F

Targeted run `artifacts/game-runs/astro/20260731-160130-corpus-gate` reached
LOGO at 61.500 seconds, TITLE at 236.203 seconds, and worldmap document load at
324.688 seconds without device loss. It was deliberately stopped after the
targeted GPU evidence was complete and no later `690F` occurred; the repeated
AudioOut2 result is not classified as a blocker because it is also dominant in
the healthy control.

At work sequence `1003279`, `ps=0x5006CB800` wrote `0x53AA00000` as a
1920x1080 RGBA16F tile-27 image, writer serial `35837`. The paired raw captures
of its `0x514080000` source and `0x53AA00000` target are byte-identical:
16,588,800 bytes, SHA-256
`8B301C0EF384488AD6DAB801067655F55FBDDA63CE4FEDDA31AD81B1B4791DB5`,
with 1,333,889 nonblack pixels. This reconfirms real scene color before the
next postprocess stage.

The next exact consumer in this lifetime is `ps=0x5006CE200` at work sequence
`1003308`. Its nine traced reads at PCs `0xF8..0x150` all select the same
1920x1080 identity and writer serial `35837`. Retained AGC writer traces name
the draw `TaaLite_0` and its first MRT as `0x537060000`, 2432x1368 RGBA16F,
tile 27, DCC metadata `0x56E834000`. `cs=0x50068FA00` follows at sequence
`1003314`; the established shader-resource capture shows that it samples
`0x537060000` and writes the separate storage binding at `0x53AA00000`.

No `0x500690F00` dispatch occurs after writer serial `35837`. Therefore the
standing instruction to wait for `6CB800 -> 690F` was wrong for this runtime
lifetime. The next paired capture must arm `6CE200`, use `0x53AA00000` as the
source identity, and read `0x537060000` as the target. Until then, the run
proves the route but does not assert whether `6CE200`'s output is black.

The descriptor-snapshot deduplication is valid but the fresh 194 MiB
backpressure line cannot be attributed to `690F`: the diagnostic omitted the
incoming compute shader address. Exact offline evaluation of Astro's `690F`
state yields only two production snapshot keys (48 identical sampled views and
four identical storage views). Backpressure telemetry now records the shader,
binding count, and unique texture-array address/length pairs; counts without
that attribution are not findings.

## 2026-07-31: Prospero rect-list value 7 is now expanded post-VS; Astro remains black

Sony SDK 10.00's `agc/registerstructs.h` assigns
`UcPrimitiveType::kRectList = 7`, and `gnmp/constants.h` defines the three
vertices as upper-left, upper-right, and lower-left. SharpEmu previously knew
only the legacy GCN/PS4 rect-list value `0x11`; Astro's value-7 draws fell back
to a three-vertex triangle list. A first experiment mapped value 7 to a
four-vertex strip. That experiment was falsified: it invoked the guest vertex
shader for nonexistent vertex 3, and the title became completely black. Its
commits were reverted before this implementation.

The current Vulkan path leaves the guest vertex count at three and inserts a
geometry stage. It forwards all three post-VS positions and pixel varyings,
synthesizes lower-right as `v1 + v2 - v0`, and emits the four corners as a
triangle strip. Pipeline identity includes the geometry module, and the Vulkan
device feature is copied from reported support rather than assumed. Included
tests cover value 7's topology and the generated SPIR-V contract. A standalone
driver check created the complete three-stage graphics pipeline successfully
on the AMD Radeon Pro V620 MxGPU with four varyings.

The exact clean Release payload built with zero warnings and zero errors; all
2,470 solution tests passed. Targeted run
`artifacts/game-runs/astro/20260731-152701-corpus-gate` reached LOGO at 75.281
seconds and TITLE at 235.000 seconds without device loss before it was
deliberately stopped. Required `PrintWindow(PW_RENDERFULLCONTENT)` captures
are retained as `rectlist-gs-ps-logo-30s.png` and
`rectlist-gs-title.png`. The first is still the centered black movie rectangle;
the second is guest-black except for SharpEmu's HUD. This is not a recognizable
Astro frame and not a black-frame completion claim.

The same run separates the movie symptom from the title renderer. AvPlayer
opened `ps_studio_armadillo.mp4` at 3840x2160/59.94 Hz, started the in-process
decoder, and reported `host_present=True`. Timestamp 0 was held while the guest
GPU backlog advanced; later decoded timestamps arrived near the title
transition. The centered rectangle is therefore not evidence that no MP4 was
decoded. The persistent title black remains the guest postprocess/content
problem. Correct rect assembly is bankable SDK-grounded behavior, but the next
content checkpoint remains the live `6CB800 -> 690F -> 650600` handoff.

## 2026-07-31: exact eboot and Sony-oracle replay eliminates 690F translation as the black producer

The current title allocation ledger closes the previously contradictory
producer/consumer join. In
`artifacts/game-runs/astro/20260731-112902-corpus-gate/attempt-01.log`,
`ps=0x5006CB800` writes `0x53AA00000` at writer serial 33525/work 918241;
`cs=0x500690F00` reads that exact address and writer serial at work 918271 and
writes `0x53B9F0000`; `ps=0x500650600` then samples that output at work 918312.
This is a sampled-data dependency in one live title lifetime, not an
occurrence-number or same-address write-after-write inference.

The exact `690F` program was recovered from Astro's `eboot.bin` at file offset
`0xE1B73E0`, length `0x1FEC`, using its measured runtime-PC signature. It has
1,112 instructions, 48 image loads, and four image stores. Sony SDK 10.00's
`libSceShaderIsaP.dll` and SharpEmu agree on the size and opcode of all 1,112
instructions. Sony's canonical `v_add_nc_u32` / `v_sub_nc_u32` spellings are
encoding-equivalent to the shorter names retained by SharpEmu's IR; after
normalizing those aliases there are zero mnemonic differences as well.

An offline replay used the exact eboot bytes, the retained live scalar state,
the complete constant buffer at `0x502DF9DA0`, the sampled 1920x1080 descriptor
for `0x53AA00000`, and the 2432x1368 storage descriptor for `0x53B9F0000`.
The resulting SharpEmu SPIR-V was accepted by the same AMD Radeon Pro V620
Vulkan device used for title boots. At the live 1920x1080 input and 2432x1368
output dimensions, a synthetic nonblack RGBA16F input produced
3,326,976/3,326,976 nonblack output pixels and the same number of expected
constant matches. Runtime
telemetry also reports no zero-filled SMEM loads for this dispatch.

This eliminates gfx1013 decode, scalar constant reconstruction,
ImageLoad/ImageStore lowering, and the host Vulkan driver as causes of colour
loss inside `690F`. It does **not** claim a visible Astro frame. The remaining
falsifiable boundary is the live host-image content selected for `0x53AA00000`
and written to `0x53B9F0000` in the same command, followed by the small
`0x500650600` consumer. The next boot must capture that boundary directly;
waiting for a final present supplies less information.

The historical positive composite run
`artifacts/game-runs/astro/20260731-014000-corpus-gate` is a clean control. Its
retained environment contains no unidentified rendering override, and its
source and target raw RGBA16F captures are byte-identical with 995,072
nonblack pixels. Do not dismiss it as an override-bearing diagnostic run
unless a specific recorded setting is produced.

The current-master live capture
`artifacts/game-runs/astro/20260731-142057-corpus-gate` independently confirms
that control. At `6CB800` occurrence 399/work 999598, the live
`0x514080000` source and `0x53AA00000` target are byte-identical, each with
1,333,889/2,073,600 nonblack pixels and hash `0x25803815681333B4`. The run
reached LOGO, TITLE, and worldmap document load without device loss. It did
not retire the armed `690F` input/output capture, so it makes no claim about
`0x53B9F0000`.

That missing capture exposed a diagnostic submission-accounting defect. The
nonblack-pair probe flushed guest commands and called `vkQueueWaitIdle`, but
left the already-completed entries in SharpEmu's pending-submission ledger.
The following `690F` dispatch is estimated at approximately 984 MiB because
its 48 sampled bindings alias the same large image; capacity control then
blocked behind stale retained-byte accounting and reached backpressure count
512 with no new guest work sequence. All synchronous guest-image diagnostic
drains now use `WaitForAllGuestSubmissionsForCpuVisibility`, which waits the
tracked fences and retires their resources. The raw `QueueWaitIdle` inside an
untracked one-shot readback remains appropriate because it creates no pending
ledger entry. Treat the stopped run as a probe dead end after its positive
composite evidence, not as proof that Astro's normal queue hangs there.

## 2026-07-31: gfx1013 DS high-offset alias corrupted Astro's clustered-list head; fixing it does not restore the title frame

The producer/consumer boundary is now byte-exact. Pixel shader
`ps=0x5008F1400` binds `0x553BA3A50` as `s24` and loads its current list index
at PC `0x1DD4`; it binds node storage `0x551BE0000` as `s0`, loads node data at
PC `0x1E00`, and loads the next index at PC `0x1E18`. It accepts every head
except `0xFFFFFFFF` and follows `node.next` through the two backward branches
at PCs `0x27A8` and `0x27B0`. Therefore `0x553BA3A50` is the A list-head table,
not a float depth/bounds buffer.

The exact `cs=0x5006E8500` producer explains why A contained repeated
`0x3F800000`. Its list head lives at LDS byte offset `0x510`:

```text
0x003C ds_write_b96 ... offset:0x510       # initialize the LDS record
0x0F7C ds_wrxchg_rtn_b32 ... offset:0x510 # publish a new head, return old head
0x0FF8 ds_read_b32 ... offset:0x510        # copy the final head to A
```

SharpEmu retained the two encoded bytes as `Offset0=0x10, Offset1=0x05` but
used only `Offset0` for ordinary LDS loads, stores, and atomics. That aliased
the head onto LDS byte offset `0x10`, where this shader's reduction data can be
`1.0f`; the producer then copied that bit pattern to A and the consumer treated
it as node index `1065353216`. This is an ISA lowering defect, not missing SMEM,
texture content, DCC, or host-image residency.

LLVM's GFX10 `DSInstructions.td` is explicit: ordinary DS operations have one
16-bit `offset`, with `offset0=offset{7:0}` and
`offset1=offset{15:8}`. Only DS2 forms interpret the bytes independently, with
their element/ST64 scaling. The Vulkan and Metal backends now combine both
bytes for every supported ordinary DS load, store, wide operation, and atomic;
DS2 and crossbar instructions retain their distinct rules. Included tests
compile `ds_read_b32`, `ds_write_b96`, and `ds_wrxchg_rtn_b32` and require the
generated SPIR-V to contain the full `0x510` byte offset.

This correctness fix is **not** a black-frame completion claim. After a clean
zero-warning Release build and all 2,466 tests, run
`artifacts/game-runs/astro/20260731-134537-corpus-gate` reached LOGO at 59.750
seconds, TITLE at 207.765 seconds, and worldmap preload at 286.015 seconds with
no device loss. Across 116 timestamped `0x5008F1400` draws, GPU render time had
a 132.766 ms median and 132.039 ms mean (114 draws at or above 100 ms averaged
134.355 ms). That is the previous approximately 123--140 ms 1080p control, not
a performance collapse. Required `PrintWindow(PW_RENDERFULLCONTENT)` captures
at title and after worldmap preload show the same dark, blue-striped PlayStation
Studios logo; they are nonblack guest composition but not Astro's menu or
worldmap.

An earlier address-instrumented run lost the Vulkan device before TITLE while
submitting later work. The reported `cs=0x5006EAC00` label is the submit that
observed an already-lost asynchronous queue, and that shader's GDS path is
unchanged by this LDS patch. The clean positive run did not reproduce the loss,
so it is not attributed to either the finalizer or this fix. The remaining
visual boundary stays downstream/upstream-content-specific: correct A-head
addressing alone does not make the half-resolution G-buffer/postprocess chain
nonblack.

## 2026-07-31: DB provenance now preserves Sony's sampled depth lifetime; recognizable Astro output remains open

Sony SDK 10.00 closes the ambiguity at the first full-resolution-to-half-
resolution boundary. In
`agc_metadata_compression/htile_depth_tests.cpp`, a depth clear with HTILE
enabled updates HTILE while the backing Z bytes may remain unchanged, and a
compressed texture view is required to interpret that state. Reading the raw
backing bytes as an independent R32 image is therefore not an equivalent
Windows/Vulkan implementation.

The retained detailed control
`artifacts/game-runs/astro/20260731-114639-corpus-gate/attempt-01.log` shows
that exact mismatch. Astro first creates the native `D32Sfloat` depth image at
`0x513560000`, then samples the same 1920x1080 DB lifetime through an R32-float
texture descriptor. SharpEmu found the compatible depth view but let an older
CPU-backed R32 upload supersede it:

```text
vk.depth_texture_alias addr=0x0000000513560000 ...
vk.depth_texture_alias_superseded ... color_serial=2 depth_serial=0
```

The missing state was GPU provenance, not a Vulkan format-class exception.
CB targets were entered into `_gpuProducedGuestImageAddresses` when queued;
DB targets were not. The presenter now registers both nonzero DB read and
write addresses at the same queue boundary and records their guest work
sequence. It deliberately does not publish a color format. Consequently the
existing D32 sampled-view path wins over stale guest-memory uploads, while a
real newer GPU-authored color/storage lifetime can still supersede depth by
writer order. A unit test covers distinct DB read/write aliases and proves
that this registration does not make exact-format color availability true.

The clean Release build completed with zero warnings and zero errors; all
2,455 solution tests passed. The address-scoped corpus run
`artifacts/game-runs/astro/20260731-124211-corpus-gate` then measured:

- LOGO at 64.718 seconds, TITLE at 217.672 seconds, and worldmap preload at
  300.578 seconds;
- no `Vulkan device lost` through the complete 480-second budget, so this no
  longer reproduces the old pre-TDR-fix GDS/device-loss differential;
- `cs=0x5006C6A00` and the later clustered-light passes consistently report
  `selected=depth` for `0x513560000`, rather than
  `vk.depth_texture_alias_superseded`;
- the required PrintWindow capture
  `printwindow-worldmap-loaded.png` remains the very dark PlayStation Studios
  strips/wordmark after worldmap preload. It is nonblack guest composition,
  but is not Astro's title, menu, or worldmap.

This banks a Sony-grounded cross-format depth/provenance correction, not a
black-frame completion claim. The configured compute-content probe did emit a
paired readback at work sequence 343513. The D32 input contains `1.0` in every
pixel (`4,147,200/8,294,400` nonzero bytes; the other four bytes per float are
zero), and the R32 output contains `1.0` in every one of its 518,400 pixels:

```text
input-depth 0x513560000 D32Sfloat head=0000803F... hash=BE3646ACE67E80CE
output-storage 0x53A500000 R32Sfloat nonblack_pixels=518400/518400
head=0000803F... hash=4D5239344DD6C2CE
```

Thus the full-to-half depth transfer is no longer black. It transports only
SharpEmu's neutral first-use depth, not measured scene geometry. The next
falsifiable checkpoint is the first other clustered-light input or postprocess
surface that remains zero after this now-nonzero hierarchical-depth level.

## 2026-07-31: a fresh depth-alias differential reproduces the already-refuted GDS/TDR path

Run `artifacts/game-runs/astro/20260731-121853-corpus-gate` rechecked the
full-resolution depth lifetime at `0x513560000` with an exact current Release
payload and only address-scoped writer-order tracing.  The experiment made a
CPU-backed `R32Sfloat` sampled upload ineligible to supersede the matching
native `D32Sfloat` depth image.  Its first arbitration changed from the clean
control's `selected=color` to:

```text
color_initialized=1 color_serial=2
depth_initialized=1 depth_serial=0 selected=depth
```

This is not a rendering fix.  It independently reproduces the July 30
refutation below: LOGO was reached at 61.328 seconds, then Vulkan device loss
was reported at 97.516 seconds while submitting the one-thread
`cs=0x5006EAC00` four-list finalizer.  The immediately preceding retired work
included `cs=0x500571000`, launched as `8160x1x1` against the full-resolution
lighting target.  CPU execution continued far enough to load
`title_controller_ship`, but corpus-gate correctly did not count TITLE or
worldmap after device loss.  A required `PrintWindow(PW_RENDERFULLCONTENT)`
capture at that point was guest-black.

The experiment also exposed a real presenter state smell: rebinding an
uploaded image as a render/storage target currently clears its CPU-backed
provenance before a GPU write is recorded.  Correcting that transition makes
the native-depth selection persistent, but cannot be banked independently:
the earlier neutral-depth experiment and this run both prove that activating
the present depth lifetime feeds unsafe state into the clustered-list chain.
Binding provenance is therefore a dependent correctness task, not permission
to substitute SharpEmu's neutral host depth for Astro's missing guest
depth/HTILE content.

All source and test changes from this experiment were removed byte-for-byte.
The Release output was cleaned again, rebuilt from `master` with zero warnings
and zero errors, and the complete 2,454-test solution passed.  Do not change
alias priority again until the exact guest depth/HTILE producer is
materialized and `0x5006E8500` publishes bounded A-head/node data without
driving the downstream GDS sequence into device loss.

## 2026-07-31: bounded compute submissions prevent the Windows TDR; the visible logo is still queued movie-era work

The previous long title run lost the Vulkan device immediately after
`cs=0x500525200` (`4096x1x1`) retired in 3.251 seconds and while
`cs=0x500529400` (`131072x1x1`) was being submitted.  The V620 advertises an
X workgroup-count limit of `4294967295`, so the second dispatch is legal.  The
failure instead matches Windows WDDM timeout detection: multiple
`vkCmdDispatchBase` calls in one command buffer are still one unpreemptible
queue submission, and the default TDR timeout is two seconds.  The absence of
`TdrDelay`, `TdrDdiDelay`, and `TdrLevel` overrides on this host removes a
machine-specific explanation.

The presenter now bounds each translated compute queue submission by a
workgroup budget derived from generated SPIR-V size.  It targets 64 MiB of
SPIR-V-by-workgroup work and clamps the result to 256..16384 workgroups.  Each
chunk is a separate command buffer and queue submission.  `vkCmdDispatchBase`
preserves the original guest `WorkGroupId`; the pushed guest thread limits
remain the unsplit dimensions.  Defined guest compute cannot depend on the
relative execution order of workgroups inside one dispatch, while the
existing shader-write/read barrier makes the deliberately stronger
cross-submission ordering visible.

Run
`artifacts/game-runs/astro/20260731-114639-corpus-gate/attempt-01.log`
is the first live validation.  The exact runtime payload had already passed a
zero-warning Release build and all 2,454 solution tests.  In this run:

- `0x500525200` has 630,992 bytes of SPIR-V and is submitted as 16 chunks of
  256 workgroups;
- `0x500529400` has 99,032 bytes of SPIR-V and is submitted as 194 chunks:
  193 consecutive 677-group ranges plus the final 411 groups;
- both complete 12 times, with continuous bases covering their complete guest
  ranges;
- LOGO is reached at 91.609 seconds, TITLE at 254.922 seconds,
  `title_controller_ship` starts, and worldmap preload is reached at 339.812
  seconds;
- the 480-second gate ends with no `Vulkan device lost` record.  This is a
  direct positive differential against the old failure boundary, not merely
  an absence from a shorter run.

This fixes the Windows device-reset boundary; it does **not** prove the main
menu renders.  `SHARPEMU_LOG_AGC_SHADER=1` produced 280 MB / 1.75 million
lines and let the guest build a large render backlog.  PrintWindow captures
made after `title_controller_ship` started contain very low-valued nonblack
pixels, but an offline gamma expansion identifies them as the PlayStation
Studios wordmark and blue strips from the earlier movie.  They are queued
movie-era guest composition, not title-scene geometry.  The user's live
observation of the blue-strip logo is therefore corroborated, while a
recognizable title/main-menu guest frame remains **UNMEASURED**.  The next
visual control is a clean corpus run without full AGC logging, which tests
whether the renderer can now catch up after surviving the former TDR.

## 2026-07-31: Astro's legacy context-state NID was a no-op; fixing it does not restore scanout

Sony SDK 10.00 defines `ContextStateOperation` exactly as clear (0), push (1),
pop (2), and push-clear (3). Two complete firmware bodies establish the packet
contract independently:

- PS5 9.00 `sceAgcDcbContextStateOp` (`HabmgqPwPw0`, vaddr `0x3DB0`,
  `st_size=665`) emits `CONTEXT_CONTROL`, `CONTEXT_STATE`, and, for the
  software-clear path, `LOAD_CONTEXT_REG_INDEX` against Sony's default table.
- PS5 4.03 export `qj7QZpgr9Uw` (vaddr `0x3320`, `st_size=0x2DE`) has the same
  0..3 switch and packet values. Astro has ten static call sites to this older
  NID; the visible call sites pass push-clear 3 and pop 2 in pairs.

SharpEmu had implemented the modern named NID but left Astro's older NID on a
one-DWORD no-op. Both NIDs now enter the same semantic builder. The submitted
DCB parser preserves the current Cx-register dictionary on push, loads the
known Sony register defaults on clear, and restores the saved dictionary on
pop. Tests cover both entry points, packet shapes, clear/default state,
restoration, invalid nesting, and the firmware size table.

Run `artifacts/game-runs/astro/20260731-064622-corpus-gate/attempt-01.log`
proves the exact fresh `win-x64` payload executed this path. It recorded 6,532
legacy push-clear calls and 6,532 pops with zero invalid or rejected parser
applications. It reached LOGO at 101.640 s, TITLE at 249.796 s, and WORLDMAP
preload at 327.671 s.

This is **not** claimed as Astro's black-frame root cause. Required
`PrintWindow(PW_RENDERFULLCONTENT)` captures at title and after worldmap preload
remain guest-black; their only nonblack pixels are SharpEmu's HUD:

- `artifacts/game-runs/astro/20260731-064622-corpus-gate/printwindow-title.png`
- `artifacts/game-runs/astro/20260731-064622-corpus-gate/printwindow-title-late.png`

At 396.015 s Vulkan device loss occurred while submitting
`cs=0x500529400`, `groups=131072x1x1`, after a preceding compute retirement
took 6,757.765 ms. The nearest current controls ended at or before worldmap
preload and do not cover this later interval, so causality is **UNRESOLVED**.
Do not attribute that loss to context-state restoration without a same-duration
paired control. The opt-in context trace also wrote 13,064 lines, so milestone
timing from this run is not a performance comparison.

## 2026-07-31 correction: the 68FA/537 interval is pre-title transition work, not a proved missing producer

The retained complete trace
`artifacts/game-runs/astro/20260731-051128-corpus-gate/attempt-01.log`
invalidates the causal conclusion that had been attached to the black
`0x537060000` input. Its game-level ordering is exact:

```text
line 489693  Level has started: ps_logo
line 511593  seq 13258 writes the 1920x1080 SMAA target at 0x53AA00000
line 511623  seq 13259 performs mode-6 metadata work on that target
line 511640  seq 13260 performs mode-6 metadata work on 0x537060000
line 511690  seq 13261 runs cs=0x50068FA00 and writes 0x53AA00000
line 543832  LevelDocument Loaded: title_controller_ship
```

Therefore the black sampled history occurs after `ps_logo` starts and before
the title level even loads. It is an intentional level-transition/preload
interval unless a retained title-era frame proves otherwise. The absence of
an earlier writer to `0x537060000` is real, and Sony SDK 10.00 still proves
that mode 6 is `kDccDecompress`, not a copy. Those facts do **not** prove an
emulator defect when the game has deliberately selected an uninitialized
history during a transition whose output need not be displayed.

Static analysis of this exact Astro executable strengthens the history
interpretation without identifying this address by itself. The executable
names four lazy resources: `taaLiteHistory0/1` at 3840x2160 and
`taaLiteHistoryLow0/1` at 2432x1368. Its resolution selector deliberately uses
the low-history pair for the 1920x1080 path and one 2432x1368 mode, selected by
the byte at the TaaLite object offset `+0x150`. Render-graph lookups use the
FNV-1a hashes of `history0` (`0x1FAF8B3D`) and `history1` (`0x1EAF89AA`). A
runtime address-to-resource-name join for `0x537060000` has not been retained,
so naming that address as a particular history remains an **INFERENCE**.

Likewise, the instruction shapes of `0x500690F00` and `0x50068FA00` resemble
FSR1 EASU and RCAS respectively, but this remains an **INFERENCE**, not a
source-level shader identity. Do not fabricate a `0x53AA00000` to
`0x537060000` copy or resume producer tracing from this interval.

The user directly observed the intro and save-select UI on the current line,
which independently shows that title rendering is no longer globally absent;
no matching PrintWindow capture was retained, so that remains a human
observation rather than a pixel measurement. Corpus-gate supplies no user
input. `LevelDocument Loaded: worldmap` can therefore be title-side preload,
not evidence that `worldmap` should already have started. Do not force
ProductNext state 8, 9, or 10 without first proving a real selection/input and
transition request.

The immediately following sections preserve the earlier investigation and
measurements, but their claim that this interval is the missing color handoff
is retracted by the exact game-level timeline above.

## 2026-07-31: 68FA consumes a distinct black surface; direct aliasing is ruled out

Run `artifacts/game-runs/astro/20260731-042644-corpus-gate/attempt-01.log`
is the first paired capture that labels sampled and writable compute resources
separately and records their Prospero direct-memory identities. It reached LOGO
at 66.500 s, TITLE at 209.422 s, and WORLDMAP at 297.406 s without device loss.
The selected `cs=0x50068FA00` dispatch reports:

```text
input-color 0x537060000:
  direct=0x184060000 offset=0x37060000
  nonblack=0/3326976 hash=0xCAC29F0465EBD4CE
pre-dispatch-storage 0x53AA00000:
  direct=0x187A00000 offset=0x3AA00000
  nonblack=995072/3326976 hash=0xF092CB88247970F7
output-storage 0x53AA00000:
  nonblack=0/3326976 hash=0x62C29F0465EBD4CE
```

The allocation ledger independently places `0x514080000` at physical
`0x161080000`. All three addresses lie in the one virtual mapping beginning at
`0x500000000`, but their physical ranges are distinct. SharpEmu's lack of
shared Windows backing for two virtual mappings of the same direct-memory
range is a real general defect, confirmed against Kyty's implementation and
alias tests, but it is **not** the cause of this Astro boundary.

This also closes the earlier probe-label ambiguity. `68FA` reads exact black
from `0x537060000` and overwrites the nonblack storage destination with opaque
black. Its retained program is straight-line (20 image loads, four stores, no
branch, compare, EXEC loop, or dispatcher-cap path). The shader faithfully
filters the value supplied to it; the first missing color remains upstream of
`0x537060000`.

No normal render-target, compute-image, global-buffer, or DMA writer to
`0x537060000` appears in the retained full trace. Its first observed use is
Sony mode 6 (`kDccDecompress`) with `writer=none`; raw color bytes, DCC bytes,
and clear words are zero. Faithful decompression of that captured state is
still black. The next falsifiable checkpoint is therefore the missing
producer or transfer into `0x537060000`, not another `68FA` control-flow probe,
a direct-alias rewrite, or a fabricated DCC color.

### The selector audio is delivered, but roughly 37-43 dB below the intro

The same run retained
`artifacts/astro-audio-20260731-042644.wav` from the final stereo mixer and
logged every 200th mix/submission. WinMM accepted the complete stream. The
intro reached `peak=0.2853` (about -10.9 dBFS); around WORLDMAP the 20 routed
ports produced only `peak=0.0021..0.0044` (about -53.6..-47.1 dBFS). This
matches the listening observation: the intro is audible while the selector is
effectively silent.

Sony SDK 10.00 and firmware both define the measured channel and mix-to-main
gains as linear floats, defaulting to `1.0`, and SharpEmu multiplies each once.
All MAIN, BGM, and 18 OBJECT_MAIN ports are routed. No dropped port class,
endpoint failure, or scalar-gain defect is established for Astro's formats.
Do not apply a guessed boost. A future audio probe must compare each port's
pre-gain peak with its post-gain contribution; if the source is already this
quiet, the incomplete game state owns the level.

## 2026-07-31 correction: the 68FA probe measured its destination, not a sampled input

Run `artifacts/game-runs/astro/20260731-035842-corpus-gate/attempt-01.log`
is the first same-process capture of the current title producer and a later
storage writer at the same guest address. It reached LOGO, TITLE, and WORLDMAP
without device loss. At work sequence `1007244`, `ps=0x5006CB800` copied the
live HDR scene into `0x53AA00000`:

```text
1920x1080 target: nonblack=995072/2073600 hash=0xEDE36ADC6F2EDAE5
```

SharpEmu then rebound the same guest address as a 2432x1368 storage image for
`cs=0x50068FA00`. The two descriptors have the same guest/Vulkan format and
tile mode, but the presenter stores them as separate Vulkan image variants.
Before this change, activating the larger retained variant discarded the
newer GPU-authored contents of the smaller variant.

The presenter now mirrors the overlapping mip-0 pixels only in the constrained
direction: a newer render-target writer into a compatible storage consumer at
the same guest address. It refuses CPU-backed sources, stale writers, format,
tile-mode, or mip mismatches, and it does not propagate storage contents back
into render-target variants. The selected dispatch now reports:

```text
variant copy: 1920x1080 -> 2432x1368, serial=37357
68FA pre-dispatch storage: nonblack=995072/3326976 hash=0xF092CB88247970F7
68FA output: nonblack=0/3326976       hash=0x62C29F0465EBD4CE
```

The original `stage=input-color` label is misleading. The implementation in
`RecordComputeInputImageProbes` iterates every entry in `resources.Textures`,
including entries with `WritesStorage=true`. The same resource is then selected
by `RecordComputeOutputImageProbes`. Therefore these two readbacks prove only
that the storage destination contained the copied color before `68FA` and was
opaque black after it. They do **not** prove that an `ImageLoad` sampled that
color.

The retained resource trace for this shader establishes the structural reason
to keep that distinction. In
`artifacts/game-runs/astro/20260730-191649-corpus-gate/attempt-01.log`, `68FA`
loads a sampled image through the `s0:s7` descriptor and stores through a
different descriptor loaded from its scalar table. In that lifetime the
sampled address is `0x53BA80000` and the storage destination is
`0x53D4A0000`. The paired later capture below likewise has distinct
`0x537060000` sampled and `0x53AA00000` destination surfaces.

Thus the variant-copy change restores same-address memory coherence, but this
run did not prove it repaired a sampled dependency in the postprocess chain.
The later paired run above completed that checkpoint: the real `ImageLoad`
surface is the distinct, black `0x537060000`; the copied color was only the
writable destination being overwritten.

This result does not establish a sampled dependency for `0x50068FA00`; that
same-address ordering remains only a write-after-write relationship. The
newer current-title allocation ledger documented at the top of this file does,
however, independently prove the distinct `0x5006CB800 -> 0x500690F00 ->
0x500650600` sampled-data chain. The earlier retraction conflated those two
claims and is superseded.

Process warning: run `20260731-035205-corpus-gate` was a valid baseline, not a
failed boot. A focused test build had updated `artifacts/bin/Release/net10.0`
but not the `net10.0/win-x64` binary used by corpus-gate. It reached LOGO,
TITLE, and WORLDMAP and retained the old black input. The solution-level
no-incremental build updated the actual boot DLL before the positive run.

The user directly observed the save-select screen during these runs. That is
useful evidence of visible title UI, but no matching PrintWindow capture was
retained, so it is recorded as a human observation rather than a measured
guest-frame pixel artifact.

## 2026-07-31: Sony Cx interpolant mappings use encoded selectors

The updated Kyty audit identified a real native-register contract gap, and
the local firmware closes it without relying on Kyty as an oracle. In PS5
9.00 `libSceAgc.sprx`, export `HV4j+E0MBHE` has `st_size=778` at vaddr
`0x106d0`. Its mapping loop loads the qword template at vaddr `0x45dd8`, adds
the pixel-input slot to the low dword, and stores the resulting offset/value
pair. The exact file-backed template is:

```text
00 00 00 10 00 00 00 00  -> offset 0x10000000, value 0
```

Sony SDK 10.00 independently defines `CxInterpolantMapping` as 32
`CxPsShaderUsage` entries and its `setSlot()` adds slots 0..31 to the default
register offset. Thus a native entry for slot 2 is encoded as `0x10000002`;
it is not the physical `SPI_PS_INPUT_CNTL_2` offset `0x193` yet.

SharpEmu previously violated both sides of that contract. Its HLE producer
wrote physical offsets, while indirect Cx ingestion stored native encoded
offsets verbatim. A native mapping could therefore populate dictionary key
`0x10000002`, while draw translation looked for `0x193` and silently missed
the pixel interpolant state. The parser now strips Sony's selector bits,
maps selector-1 slots 0..31 onto the physical `0x191..0x1B0` bank, and tests
the raw `0xFFFFFFFF` sentinel before normalization. SH and UC indirect lists
also have their selector bits normalized.

The HLE producer now emits the native selector form and implements the
firmware's semantic-ID match, uint16 GS-output count, flat/custom/default
rules, and packed-f16 mapping instead of the earlier identity approximation.
Included tests cover the exact selector endpoints, sentinel, semantic
matching, unmatched defaults, packed f16, and the uint16 count boundary.

This is a general pixel-input/GUI correctness fix and is a credible title-UI
dependency. It is not yet claimed as Astro's postprocess black-frame cause:
the already-positive fullscreen scene and SMAA captures do not require this
missing interpolant path. The next supported boot must distinguish improved
title/UI geometry from the still-unmeasured `0x500690F00` color boundary.

## 2026-07-31: corrected 690F probe reached worldmap but not its capture fence

Run `artifacts/game-runs/astro/20260731-024032-corpus-gate/attempt-01.log`
verified the corrected selector wiring: every match named
`cs=0x500690F00`, and `address_filter_match=True`. It reached `StartLevel
title` and `LevelDocument Loaded: worldmap`, but the guest work sequence
stopped at `999969`, below the requested minimum `1004500`. Backpressure then
grew through 512 and 1024 while no new GPU work sequence appeared. The exact
mitigated child PID was stopped rather than left spinning.

This run contains no `stage=` record, so it makes no input/output pixel claim.
The fence itself remains grounded by the positive SMAA run: occurrence 400 of
`ps=0x5006CB800` becomes nonblack at work sequence `1004619`; the next
periodic `0x500690F00` occurrence is `1006692`. Treat this attempt as a
runtime-performance/queue dead end, not evidence that either `0x53AA00000`
or `0x53B9F0000` was black.

## 2026-07-31 historical checkpoint: real PCM with no endpoint at that time

This historical endpoint-independent audio checkpoint was positive on the
then-current master.
Run `artifacts/game-runs/astro/20260731-010425-corpus-gate/attempt-01.log`
wrote `artifacts/audio/astro-chain-20260731-0130.wav`. After the run, the
complete RIFF contained:

```text
format:       PCM16 stereo, 48000 Hz
duration:     163.488 seconds
samples:      15,694,848
nonzero:      4,302,937
maximum:      10,531 / 32,768 (0.321381)
```

This proves that Astro reaches the AudioOut2 mix boundary with substantial
nonzero PCM. It supersedes the short all-zero startup WAV below as the current
audio result; that earlier file remains useful only as a warning not to stop
before the title audio begins.

At the time of that run, `waveOutGetNumDevs()` was zero and Windows reported no
sound device. That machine-state observation is not a standing description of
the VM: the later paired run above has accepted WinMM submissions and the user
heard its intro. Commit `02f9724` retries and reopens AudioOut2 after a failed
host open, which explains why an endpoint appearing later does not require an
emulator restart.

## 2026-07-31: the SMAA composite preserves the live scene byte-for-byte

The required-color edge immediately after the zero SMAA mask is positive.
Run `artifacts/game-runs/astro/20260731-014000-corpus-gate/attempt-01.log`
captured `ps=0x5006CB800` at title occurrence 400:

```text
source 0x514080000: nonblack=995072/2073600 hash=0xEDE36ADC6F2EDAE5
target 0x53AA00000: nonblack=995072/2073600 hash=0xEDE36ADC6F2EDAE5
```

The retained raw RGBA16F files are byte-identical. Sony SDK 10.00's
`libSceShaderIsaP.dll` disassembles this shader as three blend-weight samples,
an epsilon comparison, and a fast path that samples the HDR scene when the
weights sum to zero. The live zero edge map therefore selects the correct
scene-copy path; neither the edge shader nor the composite creates black.

The next current-title consumer is `cs=0x500690F00`, not
`cs=0x50068FA00`. The latter address came from an older lifetime and was
repeatedly selected by occurrence without proving adjacency.

The attempted work-sequence correction in
`artifacts/game-runs/astro/20260731-021942-corpus-gate/attempt-01.log` is
invalidated. Its minimum-sequence branch claimed the first compute dispatch
after the threshold without also requiring the configured shader address.
Likewise, the later `20260731-023422-corpus-gate` run requested
`0x500690F00` but explicitly reported readbacks for `0x50068FA00`; that
mismatch exposed the selector defect. Neither run measures the requested
shader. They are probe-wiring dead ends and must not support an image-content
claim.

Do not continue probing `0x50068FA00` as though it were the measured successor
of the composite. The bounded chain established by the current title trace is:

```text
ps 0x5006CB800 -> 0x53AA00000
cs 0x500690F00 -> 0x53B9F0000
ps 0x500650600 -> 0x53AA00000
```

The next required measurement is a same-command, live-host-image capture of
the selected `0x500690F00` after the proven nonblack composite, keyed by work
sequence and resource identity rather than a drifting occurrence number. The
selector now requires all three predicates before claiming the one shot:
configured shader address, minimum work sequence, and filtered bound resource.

## 2026-07-31: the live SMAA edge map is correctly black

The DCC-fixed run
`artifacts/game-runs/astro/20260731-010425-corpus-gate/attempt-01.log`
remeasured `ps=0x5006C8A00` at title occurrence 400. Its exact
`0x514080000` RGBA16F source contained 995,072 RGB-nonblack pixels, while its
`0x53B9F0000` R8G8 target remained zero. This is no longer an early-frame
measurement.

Sony SDK 10.00's `libSceShaderIsaP.dll` disassembled all 98 native
instructions from Astro's eboot, and every instruction matched SharpEmu's
decoder. The paired native primitive shader also supplies the expected
fullscreen positions, UVs, and `1/1920, 1/1080` texel offsets. The remaining
question was the shader's actual edge predicate, not decode or binding.

Replaying that predicate against the retained raw source settles it. The
source's maximum RGB value is `0.0491943`; the largest absolute RGB difference
between any pixel and the left/up samples is also `0.0491943`. The shader
requires a difference greater than `0.1`. Exactly zero pixels pass. Its
all-zero two-channel edge map is therefore the mathematically correct output
for this low-range HDR image, not the first black-producing stage.

Do not use a zero edge or mask surface as proof that the color chain failed.
The next required-color boundary is the `ps=0x5006CB800` composite feeding
`cs=0x500690F00`. Existing `SHARPEMU_TRACE_COMPUTE_IMAGE_CS`,
`SHARPEMU_TRACE_COMPUTE_IMAGE_MIN_WORK_SEQUENCE`, and
`SHARPEMU_TRACE_COMPUTE_IMAGE_ADDRS` telemetry captures both recognized
compute inputs and storage outputs. A selected dispatch now emits
`stage=none` with resource counts when no such live host image exists, so
silence from the readback queue is no longer ambiguous.

## 2026-07-31: DCC materialization preserves the real scene; the black frame is later

The SDK-grounded DCC fix is now runtime-verified. In
`artifacts/game-runs/astro/20260731-004138-corpus-gate/attempt-01.log`, the
exact metadata/data pair identified offline is joined by the live renderer:

```text
vk.dcc_metadata_clear metadata=0x570520000 addr=0x53AA00000
    size=960x540 format=R16G16B16A16Sfloat code=0x40
    source=render-target-bind
```

The later `JsParticleHalfResolution_0` draw is the exact destructive shader
from the older capture: `ps=0x50063F800` samples `0x53AA00000` and targets
the real HDR scene at `0x514080000`. This time the paired target readback is
identical before and after the draw:

```text
target pre:  nonblack=995072/2073600 hash=0xEDE36ADC6F2EDAE5
target post: nonblack=995072/2073600 hash=0xEDE36ADC6F2EDAE5
```

Therefore propagating Sony's DCC `0x40` opaque-black metadata fixes this
specific first black-producing boundary: the particle replacement no longer
erases the full-resolution scene. The run reached `ps_logo`, `StartLevel
title`, and `LevelDocument Loaded: worldmap` without Vulkan device loss.

This is not yet a rendered-frame result. A `PrintWindow |
PW_RENDERFULLCONTENT` capture of the exact mitigated child at wall time
`00:04:59` still shows a black guest region; only SharpEmu's own performance
overlay is nonblack. The overlay reports about 0.1 presented FPS and a
566-deep guest-work queue. Historical target traces establish that
`ps=0x500640D00` writes the registered 3840x2160 VideoOut buffers
`0x507410000`/`0x5093F0000` from a later postprocess intermediate. The next
falsifiable boundary is consequently between the now-preserved
`0x514080000` HDR scene and that final tonemap input/output, not the DCC
particle draw and not whether any scene geometry rendered.

## 2026-07-31: current silence is bounded at the host endpoint

The retained intro run does not prove an AudioOut2 emulation failure. It was
recorded with `SHARPEMU_LOG_AUDIO` unset, so the absence of
`audioout2.context_mix` or `audioout2.context_submit` lines is absence of
telemetry, not absence of guest audio work. It does prove that host playback
could not start:

```text
[LOADER][WARN] AudioOut2 host backend unavailable:
waveOutOpen failed with MMRESULT 2.
```

The current Windows host reports `waveOutGetNumDevs() == 0`, no PnP audio
endpoints, and no MMDevice render entries. Windows Audio services are running,
but the RDP session has no active Remote Audio endpoint. WASAPI or DirectSound
would therefore have no device to select either; changing the host API cannot
make this machine audible.

Commit `02f9724` fixes a separate emulator defect exposed by that environment.
AudioOut2 no longer caches its first failed host-open forever: it retries with
bounded 1/2/4/8/10-second backoff, retires a stream that rejects or throws
during submission, and reopens when an RDP or physical endpoint appears.
Twelve focused retry/reopen tests pass.

The intro asset itself is not the unresolved part. With the game dump and
packaged FFmpeg 7.1 libraries present,
`AvPlayerNativeProbeTests.NativeDecodersReadAstroStudioMovieFrames` opens
`ps_studio_armadillo.mp4` and reads both a real video frame and 1024 stereo
PCM samples. Sony SDK 10.00 independently defines `sceAvPlayerGetAudioData`
as signed 16-bit interleaved PCM delivery and says the default player clock is
audio-master when an audio stream is enabled
(`sdk/target/include/sceavplayer.h`).

The next permitted boot should enable both endpoint-independent controls:

```text
SHARPEMU_LOG_AUDIO=1
SHARPEMU_AUDIOOUT2_WAV_PATH=<absolute output.wav>
```

A nonempty WAV plus nonzero `context_mix ... peak=` proves the game audio
pipeline without speakers. Audible verification additionally requires
`waveOutGetNumDevs() > 0`; after an endpoint appears, the expected recovery
line is `AudioOut2 host backend restored`, followed by
`context_submit ... result=accepted`.

The endpoint-independent checkpoint was attempted in
`artifacts/game-runs/astro/20260731-005226-corpus-gate`. The WAV tee opened
successfully before host-backend resolution and retained a valid
1,097,772-byte RIFF file. Its 548,864 PCM16 samples are all zero
(`max_abs=0`). This run lasted only 19.172 seconds, stopped before `ps_logo`,
and reached mix snapshots only through `context_mix#400`, all with
`peak=0.0000`. It therefore sampled the already-established silent startup
window and ended long before the retained nonzero `context_mix#10200`
checkpoint; it neither confirms nor contradicts later Astro audio.

The process returned `0xFFFFFFFF` with no fatal line, Vulkan device loss,
.NET/Windows application-crash event, or crash dump. A second rendering-only
run also exited early, after `ps_logo`, so attributing the first exit to the
audio flags would be unsupported. Retain this attempt as a dead end, not an
audio regression. Live audibility remains blocked by the host's zero playback
endpoints; the prior nonzero producer-to-host-boundary measurement remains
the positive audio result.

## 2026-07-30: Sony DCC clear propagation is the next runtime checkpoint

The previous title-control frontier was premature. Offline evidence now
identifies a concrete GPU representation gap immediately before the
`JsParticleHalfResolution_0` replacement:

```text
compute fill:  dst=0x570520000 bytes=24576, output 40 40 40 ...
texture view:  data=0x53AA00000 metadata=0x570520000
host sample:   data image remains byte-zero RGBA16F
```

This is not an interpretation of an AMD family table. Sony SDK 10.00 defines
`Core::DccMetadataCode::k0001 = 0x40` as a block interpreted as
`RGBA={0,0,0,1}` using metadata only
(`sdk/target/include_common/agc/core/metadatacompression.h`). Its shipped
`clearDccSurface` implementation expands the byte to
`0x01010101 * code` and calls `fillDwordsWithCompute`
(`sdk/target/src/agc/toolkit/toolkit.cpp`). Thus the observed 24 KiB
`0x40` fill and the texture's identical metadata pointer describe exactly the
opaque-black value that `ps=0x50063F800` tests as its empty sentinel. Sampling
SharpEmu's expanded host image as `(0,0,0,0)` instead makes the shader execute
the destructive RGB export.

The exact Astro eboot utility kernel is also recovered, without a boot or a
shader-address special case. SELF PHDR7 maps RVA `0x8E40000` at physical
offset `0x8EA9940`; runtime shader RVA `0x8E6AA00` is therefore physical
offset `0x8ED4340`. Its words are:

```text
D7460004 04010C08       v_lshl_add_u32 (thread byte address)
7E000204                 v_mov_b32 v0, s4
7E020205                 v_mov_b32 v1, s5
7E040206                 v_mov_b32 v2, s6
7E060207                 v_mov_b32 v3, s7
E01C2000 80000004       buffer_store_format_xyzw
BF810000                 s_endpgm
```

The clear value is runtime state in `s4..s7`, not an embedded constant. The
backend now annotates only this complete 64-lane uint4-fill shape, retains the
current Sony `CxRenderTarget::DccAddress` beside its expanded Vulkan image,
and records a queue-ordered image clear only when the full writable buffer
starts at that exact DCC address. The compute shader still executes and writes
guest metadata. There is no Astro address, shader address, forced alpha, or
skipped draw in the implementation. Partial fills and nonconstant DCC codes
are refused.

A second offline audit closed the remaining identity ambiguity in the
implementation, without claiming the runtime result. Astro reuses pixel
address `0x53AA00000` through at least two descriptors whose Sony
`Core::Texture` metadata-low byte differs:

```text
960x540  raw dword6/dword7 = 007B0000/00057052 -> DCC 0x570520000
1920x1080 raw dword6/dword7 = 607B0000/00057052 -> DCC 0x570526000
```

The exact `ps=0x50063F800` descriptor is the first row. Its metadata pointer
is therefore byte-exactly identical to the 24 KiB fill base; the second row
explains the apparently contradictory older warning. SharpEmu previously
decoded this pointer in AGC and then discarded it when constructing
`GuestDrawTexture`, leaving Vulkan dependent on the last render-target
binding. The sampled descriptor's DCC pointer now crosses the backend seam.
Recognized constant-DCC fills are retained as versioned metadata states; an
expanded image materializes each version once, either at the producer if its
current CB binding matches, or before the first matching render-target draw if
the image did not exist or another descriptor variant was active. A sampled
descriptor may materialize the state only when that image has no writer newer
than the fill; a late clear is refused rather than erasing draw output.
Read-only and synthetic buffers cannot invalidate the state, and an arbitrary
writable overlap makes it unknown instead of replaying an old clear.

This ordering guard is load-bearing in the captured Astro interval. Each of
the eight measured `0x570520000` fills is followed by exactly 44 writers to
`0x53AA00000` before `ps=0x50063F800` samples it. There are zero compute,
DMA, WRITE_DATA, or RELEASE_MEM writes overlapping
`0x570520000..0x570525FFF` between each fill and consumer. Therefore the
metadata state survives, but replaying its clear at the consumer would be
wrong; it must be represented before those 44 draws. The backend records it
at the producer or first following target bind and tracks the image-writer
serial to enforce that boundary.

First-use render-pass selection is part of the same boundary. The initial
implementation recorded the deferred DCC clear before the first draw but
selected `InitialRenderPass` earlier while the host image still reported
uninitialized. That render pass has a clear load-op and could immediately
replace metadata-derived opaque black with transparent black. A pending,
format-supported DCC materialization now counts as initialized when choosing
the render pass (for both one target and MRT), so the subsequent pass loads
the value that the queued DCC clear establishes.

**Runtime status: UNVERIFIED.** Existing logs cannot close the last identity
join. The texture descriptor and fill both say `0x570520000`, but the
deduplicated `gpu.unmapped_surface_dcc` warning for data address
`0x53AA00000` preserves an earlier CB binding of `0x570526000`. That warning
is keyed only by metadata kind and data surface, so it cannot establish which
DCC address was bound during the measured writer interval. The next supported
boot must first show:

```text
vk.dcc_metadata_clear metadata=0x570520000 addr=0x53AA00000 code=0x40
```

Existing target-writer telemetry previously omitted the producer-side
metadata identity, which made a missing clear ambiguous. It now includes
`dcc=0x...` in `agc.rt_writer` and each
`agc.shader_draw targets=[...]` entry. With
`SHARPEMU_TRACE_PIXEL_SHADER_ADDRESS=0x50063F800`, the same run will
therefore distinguish a failed recognizer/materialization from a target that
was actually bound to metadata other than `0x570520000`; no additional probe
build is required.

Then a target-only capture must prove that the half-resolution sample is
opaque black and that `ps=0x50063F800` no longer destroys the nonblack
full-resolution scene. Until both occur, this is an SDK-grounded candidate
root cause, not a claimed rendered frame.

Offline verification is complete: the focused tests cover the exact kernel,
both live descriptor metadata addresses, runtime pattern, full-range
requirement, all four Sony constant DCC codes, one-application-per-version,
late-clear refusal after a newer image writer, and refusal of
partial/nonconstant cases. The first-use test also proves that a pending DCC
materialization selects a load render pass. The exact nine kernel dwords above
are now decoded through production `Gen5ShaderTranslator.TryDecodeProgram`
before recognition; the test no longer hand-constructs an assumed decoder IR.
This closes only the decoder-to-recognizer join, not the unverified runtime
metadata-address join. Release builds with zero warnings and the full suite
passes 2,332/2,332.

## 2026-07-30: Sony's ISA oracle proves the particle shader orders the black RGB write

The target-only pre/post capture closes the first black-producing draw without
inferring from the final window. In
`20260730-225544-corpus-gate/attempt-01.log`, the exact submitted marker
`/Main_0_0_0/JsParticleHalfResolution_0` runs `ps=0x50063F800` with blending
disabled and RGB write mask `0x7`:

```text
source 0x53AA00000: 960x540 RGBA16F, all bytes zero
target pre  0x514080000: 995072/2073600 RGB-nonblack pixels
target post 0x514080000: 0/2073600 RGB-nonblack pixels
```

The post image retains nonzero alpha while RGB becomes zero. This is an actual
shader write through the guest's RGB-only mask, not a render-pass clear or a
missing target readback.

The earlier interpretation of the shader's compare sequence was wrong. Sony
SDK 10.00's authoritative `libSceShaderIsaP.dll` disassembles the exact live
bytes at PC `0x2c`,
`F9 06 04 7C F2 80 86 06`, as:

```text
v_cmp_eq_f32 s[0:1], 1.0, v3
```

The RGB comparisons use inline encoding `128` for zero; the alpha comparison
uses encoding `242` for `1.0`. LLVM's gfx10 encoder independently assigns
FP32 `1.0` to encoding `242`. The shader therefore rejects transparent black
`(0,0,0,1)`, not byte-zero `(0,0,0,0)`. With SharpEmu's measured source
equal to `(0,0,0,0)`, the saved mask remains active, the branch does not skip
the MRT export, and real gfx1013 hardware would also replace the target RGB
with zero.

Commit `c0c2b08` remains a general correctness fix: mapped color exports are
predicated by the guest EXEC mask instead of the final WQM mask. It is not the
cause of this Astro draw, because this occurrence executes its export. The
broader multi-export/NULL/Z validity model remains **UNIMPLEMENTED**.

This retracts the skipped-EXP causal claim and the downstream-presentation
frontier. The measured boundary is now:

```text
nonblack full-resolution scene
    -> guest orders JsParticleHalfResolution_0
    -> byte-zero half-resolution input passes the shader's alpha-one sentinel
    -> guest shader replaces scene RGB with zero
```

The next falsifiable checkpoint is upstream control and resource state: why
this title-era pass receives alpha zero and is ordered as a replacement while
`LevelDocument Loaded: worldmap` never advances to
`Level has started: worldmap`. Do not fabricate alpha one or force the branch.
The completed 49-writer audit still shows shader-consistent optional-empty
content, so another producer-count argument is not evidence of a missing
required color writer.

## 2026-07-30: all 24 `0x500006E00` writers bind a zero material source

Offline parsing of the complete live title interval in
`artifacts/game-runs/astro/20260730-190148-corpus-gate/attempt-01.log`
closes the next ledger checkpoint without another boot. All 24
`ps=0x500006E00` draws bind the same `1x1` texture at both mutually exclusive
material sample sites:

```text
PC 0x01B4 ImageSample -> 0x507405100, 1x1 fmt10, texel 0
PC 0x01D0 ImageSample -> 0x507405100, 1x1 fmt10, texel 0
```

The pairing is exact: ten draws use `es=0x5002AA400`, ten use
`es=0x5000F6700`, and four use `es=0x50011FC00`. Whichever material-mode path
the pixel shader selects, its source is guest-provided zero. The
`0x5002AA400` family also deliberately exports `(5,5)` texture coordinates;
the native-GS families are particle paths whose zero-record fallback is
already established. These 24 draws are not a hidden base-color writer.

Together with the prior results, the whole 49-writer interval is now
classified:

- 13 `0x5002AFC00` draws sample genuinely black clamped BC7 edge texels;
- 24 `0x500006E00` draws bind the explicit 1x1 zero source;
- six `0x5000FD100` draws retire the guest's optional-empty degenerate
  fallback; and
- six `0x500126600` particle draws leave the target zero, with the measured
  `0x50011FC00` record path also optional-empty.

This is not evidence that 49 independent color producers failed. The
half-resolution target is an optional effect/fade surface that remains at its
guest-requested clear value. `ps=0x50063F800` then uses guest fixed-function
state to replace the live full-resolution scene RGB from that black surface.
The next root-cause boundary is the title/level control state that orders and
keeps ordering this fullscreen black replacement, not another texture,
sampler, DCC, NGG, or material-color probe.

## 2026-07-30: the generalized `0x5000F6700` replay is optional-empty

The retained live checkpoint
`artifacts/game-runs/astro/20260730-184647-corpus-gate/attempt-01.log`
closes the post-fix question for the six
`es=0x5000F6700 / ps=0x5000FD100` writers. The first selected draw sees a
nonblack full-to-half depth input (`518400/518400` nonblack R32F pixels), but
the connected native-GS evidence is:

```text
PC 0x0058 structured records: all zero
allocation: 0x1001, one vertex, one primitive
retired vertex payload: all zero
retired index payload: all zero
raster coverage: one triangle, zero nondegenerate, zero clip candidates
```

The same result repeats for all six retirements. Offline reconstruction already
proved that `0x1001` is this guest shader's deliberate one-vertex,
one-primitive degenerate fallback when no structured input lane survives.
Therefore the generalized NGG replay and the writer-order alias correction are
live-exercised here, but these six particle/material draws are legitimately
empty. A nonblack bound BC7 texture and a nonblack depth input do not make an
inactive particle record a required color producer.

This moves the complete-writer audit to the 24 `ps=0x500006E00` draws, as the
evidence ledger prescribed. Do not force the PC-`0x0058` record, allocation
count, vertex payload, or texture color. The large zero guest buffer at
`0x40103C340` is also used by unrelated scene programs; its presence in a
binding list is not evidence of a missing GPU upload without a producer and
exact consumed offset.

## 2026-07-30: `0x5006EAC00` publishes all four empty-list terminators

Offline recovery locates the exact 54-instruction
`cs=0x5006EAC00` body in Astro's eboot at file offset `0xE1D3778`. It performs
four GDS `ds_append` operations at byte offsets 16, 20, 24, and 28, loads four
129,604-byte list descriptors, and stores `0xFFFFFFFF` as their empty-head
terminator. There are no backward branches.

The detailed live log initially appeared to report only the first changed
writeback. That was trace suppression, not a missing store: later
`global_heads` snapshots prove `0xFFFFFFFF` at all four exact addresses
`0x553BC3490`, `0x553BE2EE0`, `0x553C02930`, and `0x553C22380`.
The distinct A head table `0x553BA3A50` remains zero. Absence of a repeated
large-range writeback line must not be used as evidence that a shader store
failed.

Thus `0x5006EAC00` is a four-list finalizer, not the producer for A, and its
descriptor/store lowering is cleared by consumer-visible bytes. The separate
`0x5006E8500` producer owns A and the node pool. Its empty output remains tied
to the measured near-clear depth state; inventing an A terminator is still not
a valid fix.

## 2026-07-30: forcing the neutral host depth lifetime is refuted

The retained detailed run
`artifacts/game-runs/astro/20260730-191649-corpus-gate/attempt-01.log`
exposes a real same-address lifetime split at full-resolution depth
`0x513560000`. SharpEmu has both a D32 host depth image and an R32 color
image created from the texture upload. Sampling repeatedly selects the color
lifetime because its writer serial is 2 while the depth writer serial is 0:

```text
vk.depth_texture_alias_superseded
addr=0x0000000513560000 color_serial=2 depth_serial=0
```

The zero depth serial is explained by the presenter, not inferred from the
count. The first DB attachment is a read-only depth-tested draw. Vulkan
initializes the new host attachment to SharpEmu's neutral stale-1x1 value
`1.0`, but writer order advances only for an explicit guest depth clear or
depth write. The texture descriptor is `1920x1080 fmt4/tile24`; the DB
descriptor's logged `1x1` extent is not itself the defect because
`GuestDepthExtentResolver` expands the attachment to 1920x1080 before
allocation, and depth texture lookup resolves the host depth image by the
same guest address.

A bounded experiment made that implicit host initialization advance the
depth writer serial. It passed a zero-warning Release build and all 2,308
tests, then was tested by the only supported boot path in
`artifacts/game-runs/astro/20260730-213149-corpus-gate`. The run reached
`Level has started: ps_logo` at 64.141 seconds, but the corrected lifetime
activated the clustered-list chain and Vulkan device-lost before TITLE:

```text
vk.gpu_ledger_retired ... cs=0x5006E8500 ... status=signaled
Vulkan device lost ... cs=0x5006EAC00 groups=1x1x1 ...
```

`0x5006EAC00` is the measured one-thread four-list GDS finalizer immediately
downstream of `0x5006E8500`. This device loss occurs in no other
retained Astro run; the code-equivalent clean baseline
`20260730-205636-corpus-gate` has zero device-loss records. The experiment
was therefore **REFUTED AND REVERTED**, not banked as a rendering fix.

The result does not prove that the older zero color lifetime is correct, nor
that a clear 1.0 depth surface is valid guest content. It proves only that
substituting SharpEmu's neutral host initialization for the missing guest
depth state causes the list producer to publish work that the following GDS
chain cannot safely consume. The next checkpoint is the exact guest
depth/HTILE producer and the bounded A/node counts emitted by
`0x5006E8500` before `0x5006EAC00`. Do not change alias priority again until
that state is known, and do not describe the stale 1x1 extent, format, or
swizzle as the root cause.

## 2026-07-30: the exact cap is two zero-node walkers; the A/node producer runs but writes nothing

The address-filtered run
`artifacts/game-runs/astro/20260730-205636-corpus-gate` independently
reconfirms the performance boundary without relying on the older blended
probe. `SHARPEMU_SHADER_CAP_PROBE=0x5008F1400` bypassed guest blending for
that shader only, and the exact GPU-timestamp filter recorded repeated
1920x1080 render times of about 118-143 ms across vertex counts
3660/4164/7110/8604.

The first MRT capture
`tmp/astro-8f1400-exact-20260730/0003-0x0000000514080000-1920x1080-R16G16B16A16Sfloat.rgba`
contains 869,979 RGB-nonzero pixels. Exactly two pixels, `(1136,553)` and
`(1137,553)`, have the writable blue cap marker `1.0`; their red words encode
dispatcher blocks 48 and 49, whose guest-PC entries are `0x1DDC` and
`0x1DEC`. The target write mask is `0x7`, so alpha is preserved destination
data and cannot encode either steps or the cap predicate. The second MRT
`0x512570000` is byte-exact RGB zero in the paired capture. This is a
**CONFIRMED two-fragment cap hit**, not a frame-wide fragment-load result.

The producer attribution is also narrower than the older retained wording.
The detailed run
`artifacts/game-runs/astro/20260730-191649-corpus-gate/attempt-01.log`
shows `cs=0x5006E8500` dispatching at `120x68x1` with writable bindings for
the exact node pool `0x551BE0000`, A head table `0x553BA3A50`, and the five
adjacent head tables. Post-fence writeback reports `changed_bytes=0` for the
16 MiB node pool and for the coalesced head-table range beginning at
`0x553B84010`. This corrects the stale claim that no logged GPU producer
overlapped A or nodes: the producer is present and ordered, but this measured
dispatch publishes no node or A-head changes.

The same producer samples full-resolution depth `0x513560000`. A paired
depth/HiZ capture proves that the live D32 image reaches its consumer and that
the resulting HiZ image is populated, but the measured depth is almost
entirely the clear value `1.0`. It is therefore still **UNMEASURED** whether
the zero producer output is correct for this title state or is downstream of
missing depth/G-buffer coverage. Do not fill A with an invented sentinel and
do not lower the shader step cap as a fix. The next causal checkpoint is the
first required geometry/depth writer that should make `0x5006E8500` publish
an A head or node, paired with its exact producer output.

## 2026-07-30: Astro produces nonzero AudioOut2 PCM, but this host has no output device

The absence of audible sound had two separate causes. The first was a
**CONFIRMED emulator defect**: SharpEmu implemented AudioOut2 queue timing but
never submitted its PCM to a host audio stream. Astro uses the SDK 10.00
AudioOut2 sequence demonstrated by Sony's samples:

```text
sceAudioOut2PortSetAttributes(SceAudioOut2Pcm)
sceAudioOut2ContextAdvance
sceAudioOut2ContextPush(SCE_AUDIO_OUT2_PUSH_FLAG_SYNC)
```

Commit `d6607bf` now retains the pending PCM grain, applies per-channel and
mix-to-main gains, converts the supported float or signed-16-bit channel
layouts to stereo PCM16, and submits the mix to a lazily opened host stream.
The controller, personal, and vibration buses are deliberately excluded from
the Windows main-output mix. A focused contract test verifies that one known
nonzero grain reaches a fake host exactly once.

The proof run is
`artifacts/game-runs/astro/20260730-183509-corpus-gate`, at exact Git head
`4663ae53ca60221643baf8114ec185a65f9b0c4d`. Early AudioOut2 pushes were
genuinely silent: log lines 1461-1712 report peaks of `0.0000`. That is a
measurement, not evidence that the title never produces audio. At log line
359515 Astro produces and SharpEmu mixes a real signal:

```text
audioout2.context_mix#10200 handle=2 frames=512 ports=20 peak=0.0043
```

This is a **CONFIRMED producer-to-host-boundary result**. It proves that
Astro's nonzero PCM reaches the new AudioOut2 host submission boundary. It
does not prove audible output on a machine with a playback endpoint.

This particular Windows session has no endpoint. `Win32_SoundDevice` and the
PnP `AudioEndpoint` class both return zero devices, and the native
`waveOutGetNumDevs()` result is exactly zero. Accordingly, log line 1462
reports `waveOutOpen failed with MMRESULT 2` (`MMSYSERR_BADDEVICEID`). No
choice of Windows audio API can make this session emit live sound without a
host playback device. The next falsifiable checkpoint on a machine with an
endpoint is a successful host submission followed by an audible comparison;
live audibility remains **UNMEASURED** here.

Commit `02f9724` fixes a separate recovery defect exposed by that condition.
AudioOut2 previously remembered the first `waveOutOpen` failure for the
context's entire lifetime, so reconnecting RDP with Remote Audio enabled could
never restore playback. Host opening now retries with bounded
1/2/4/8/10-second backoff, rejected or throwing streams are retired and
reopened, and failed WinMM construction releases its callback event. Tests
cover both endpoint appearance after an initial failure and stream rejection.
The recovery log must name `backend restored`, followed by an accepted
`audioout2.context_submit`; neither line can occur on the present
`waveOutGetNumDevs()==0` host.

For endpoint-independent evidence, current master also supports
`SHARPEMU_AUDIOOUT2_WAV_PATH=<fresh explicit path>`. It tees the exact mixed
48 kHz stereo PCM16 snapshots before host-backend resolution, so the later
nonzero Astro signal is retained even when `waveOutOpen` fails. The file is
bounded, refuses to overwrite prior evidence, and finalizes a valid RIFF
header on context destruction. This is a diagnostic recording path, not a
substitute for live playback and not evidence that AvPlayer audio is used.

Commit `fac9d0f` separately corrects the Sony SDK stream-info ABI:
`SceAvPlayerStreamInfo` is 32 bytes with type at offset 0 and duration at
offset 24, while `SceAvPlayerStreamInfoEx` is 104 bytes with type at offset 8
and duration at offset 96. The old implementation wrote the stream index as
the stream type and reused the 32-byte layout for both structures. In the
same run, Astro's `ps_studio_armadillo.mp4` is now reported as stream type 1
(video), and video decoding starts. Astro does not import or call
`sceAvPlayerGetAudioData`, however, so this ABI correction is authoritative
but is **NOT PROVEN CAUSAL** for Astro's sound. The intro's visible AvPlayer
video does not imply that its AAC is polled through the AvPlayer audio API;
the measured game-audio path is Sndz/AudioOut2.

The implementation and telemetry commits are `d6607bf`, `fac9d0f`,
`4663ae5`, and `49c5f45`. Validation after the final queue fix is a
zero-warning Release build and the full 2,304-test suite passing. The proof
boot reached `ps_logo` and `StartLevel title`, then was deliberately stopped
after the nonzero PCM checkpoint; its missing WORLDMAP marker is not an audio
regression.

### AudioOut2 `Advance` owns the grain snapshot

Sony's SDK 10.00 `api_voice/voice3d/audioOut2Interface.cpp` closes a second,
general AudioOut2 contract question. The sample creates a context with
`queueDepth=2`, repeatedly fills one reusable stack PCM buffer and calls
`SetAttributes` plus `ContextAdvance`, while another thread performs the later
`ContextPush`. Therefore the guest PCM pointer cannot be retained until Push:
each Advance must snapshot that grain.

SharpEmu's first host-output implementation retained only the latest guest
pointer and submitted one mix per Push. That happened to match Astro's
measured one-Advance/one-Push sequence, so it is **not the cause of the present
host's silence**, but it loses or duplicates audio for valid SDK clients that
stage multiple grains. The corrected implementation queues an owned stereo
PCM16 snapshot per Advance and drains those snapshots in order at Push. A
focused `queueDepth=2` test overwrites the same guest address between two
Advances, then verifies that one Push submits the two distinct grains in
order.

Push must also latch its staged set once. Sony's
`api_audio_out2/basic_multithread` sample continuously Advances on a producer
thread while a separate thread performs synchronous Push. Re-reading the
pending queue on each blocking-wait iteration lets one Push absorb later
producer grains indefinitely and eventually submit a burst. The implementation
now folds pending count and snapshots only on the first wait iteration; later
Advances remain staged for the next Push. A coordinated blocking regression
fills a depth-2 queue, stages a third grain while Push waits, and verifies that
the first Push submits only the original two.

The retained Astro trace contains 10,864 Advances and 10,864 Pushes, always
one-to-one. It produced 115.88 seconds of guest audio over 238.02 seconds of
wall time (about 0.487x realtime), with later peaks of `0.1475` and `0.2418`.
On a machine with a playback endpoint that predicts audible but slow/choppy
audio; on this machine audibility remains impossible to measure because the
endpoint count is zero.

## 2026-07-30: title scene pixels survive the first HDR copy

The paired run
`artifacts/game-runs/astro/20260730-190148-corpus-gate/attempt-01.log`
closes the question of whether title rendering produces any real color. At
occurrence 405, pixel shader `0x500645400` sampled the active 1920x1080
R16G16B16A16 scene image `0x514080000` and wrote `0x53AD00000`.
The exact source and target readbacks each contained 873,195 nonblack pixels:

```text
source  hash=0x972B0E55422147AA  nonblack=873195/2073600
target  hash=0x09A08AC5BAD59CBA  nonblack=873195/2073600
```

The differing hashes are expected because this 46-instruction shader repacks
channels; the identical nonblack count and exact active-image readbacks prove
that a substantial title image is rendered and survives this edge. The
remaining defect is downstream. Do not return to the disproven question
"does the title render anything?"

The same run gives the ordered postprocess chain:

```text
0x514080000
  -> ps 0x5006C8A00 -> 0x53B9F0000 (1920x1080, fmt3)
  -> ps 0x5006C9F00 -> 0x53BE70000 (1920x1080, fmt10)
  -> ps 0x5006CB800 -> 0x53AA00000 (1920x1080, fmt12)
  -> cs 0x500690F00 -> 0x53B9F0000 (2432x1368, fmt12)
  -> ps 0x500650600 -> 0x53AA00000 (1920x1080, fmt6)
  -> downsample/bloom chain
```

This corrects an earlier overstatement: the `0x5006CB800` target is
overwritten later, but it is **not dead**. Compute `0x500690F00` consumes it
before the overwrite. Static disassembly shows that `0x5006CB800` first
samples `0x53BE70000`, uses `v_cmpx_gt_f32` to cull lanes, and only surviving
lanes sample `0x514080000`. A black mask can therefore produce black output
despite a live scene image. This is a live upstream boundary, not proof of a
shader translator defect.

Pixel shader `0x500650600` is only 21 instructions: one texture sample at PC
`0x20`, finite min/max clamps using a complete 32-byte constant buffer, two
FP16 pack operations, and one compressed export. It has no branches. A paired
readback of its `0x53B9F0000 -> 0x53AA00000` edge is the next falsifiable
checkpoint; if the source is black, investigation moves to compute
`0x500690F00` and the conditional predecessor rather than this copy shader.

That checkpoint is now closed by
`artifacts/game-runs/astro/20260730-191649-corpus-gate/attempt-01.log`.
Occurrence 126 ran about 34 title frames after `StartLevel title`; the source
was the active R16G16B16A16 image and its exact readback was
`source_nonblack=0/3326976`. The probe was stopped immediately, before
worldmap, with no device loss. Thus `0x500650600` inherits black and is not the
first failing stage. The next paired boundary is
`0x5006C9F00: 0x53B9F0000 -> 0x53BE70000`, immediately before the conditional
mask shader. It distinguishes a black mask generator from an earlier failure
in `0x5006C8A00`.

The follow-up paired run
`artifacts/game-runs/astro/20260730-192227-corpus-gate/attempt-01.log`
measured that boundary at title occurrence 400. The active 1920x1080
`R8G8Unorm` source `0x53B9F0000` was exactly black
(`source_nonblack=0/2073600`), so `0x5006C9F00` and its LUTs are not the first
failure either.

The proposed next edge is now **RETRACTED as a temporal dead end**. The paired
run
`artifacts/game-runs/astro/20260730-193019-corpus-gate/attempt-01.log`
measured `0x5006C8A00` at title occurrence 400 and found that its active
`0x514080000` source was itself byte-exact black
(`source_nonblack=0/2073600`). This instance of the SMAA chain runs on a
pre-scene frame. Its black RG edge map and all downstream black values are
legitimate; they say nothing about the later frame in which the scene exists.
Do not use occurrences from this early fixed-address chain as though they were
the live-scene postprocess frame.

The later live frame is ordered separately in the retained `190148` log. Scene
draws write real color to `0x514080000`; sequences 41686 through 41734 then run
the 960x540 writer interval on `0x53AA00000`; and sequence 41735 executes
`ps=0x50063F800`, which samples that half-resolution image and replaces the
full-resolution scene RGB. Only after that replacement do sequences 41738
through 41760 run SMAA, bloom, and final tonemap on the now-black scene. This
agrees with the older complete-writer-interval captures below. The optional
`0x500126600` particle draw and hierarchical-depth zeros are not proof that a
required color producer failed.

The exact live pair is now closed by
`artifacts/game-runs/astro/20260730-194450-corpus-gate/attempt-01.log`.
The probe triggered from destination content rather than a guessed occurrence.
Immediately before `ps=0x50063F800`, `0x514080000` contained 796,436 nonblack
pixels:

```text
target_pre_nonblack=796436
pre  0x514080000 nonblack=796436/2073600 hash=0xDDFF9F6244D85344
post 0x53AA00000 nonblack=0/518400       hash=0x0B709F104AFFC325
post 0x514080000 nonblack=0/2073600      hash=0xFA7749A1865382A5
```

The post-draw full-resolution image still had 2,068,774 nonzero bytes while
its RGB nonblack count was exactly zero, matching the alpha-preserving black
frame. This proves the mechanism byte-for-byte: the fullscreen draw samples a
zero half-resolution image and replaces live scene RGB with zero.

The earlier classification of this draw as an intentional title-to-worldmap
fade is now **RETRACTED**. Log ordering alone did not prove a transition.
`Playing: , Continue: worldmap` can name queued or preloaded content; no
retained run prints a title-level end marker, and the ProductNext state probes
below show that its transition states were not entered in their measured
intervals. The pair therefore remains a live renderer boundary: it proves
that a black half-resolution image destroys an already-rendered title scene,
but does not yet prove whether the first defect is one of that image's writers,
its required initialization, or an earlier title-state condition that selects
the pass.

Two address-selected CPU probes move that control-flow frontier earlier. In
`20260730-195241-corpus-gate`, the manifest records
`SHARPEMU_PROBE_IMPORT_RET_ADDRESS=0xC06EC8F3C`, the relocated state-9 registry
comparison return. The run reaches `Continue: worldmap`, loads the worldmap
document, and remains alive for another bounded 50 seconds without one probe
hit. State 9 therefore was not reached in that measured interval; record type
6 and its state-10 StartLevel callee are downstream.

`20260730-195909-corpus-gate` similarly records the earlier state-8 second
intent-check return `0xC06EBD231`. It reaches the same milestones and runs for
another bounded 35 seconds after worldmap document load without a hit. The
ProductNext transition did not reach either retained anchor. These are bounded
negative execution results, not proof that the addresses could never execute
in an arbitrarily longer run. They do **not** support inventing an earlier
missing transition request: the title may simply be preloading worldmap while
remaining in its current level.

The fresh-save control closes that distinction. Run
`artifacts/game-runs/astro/20260730-200746-corpus-gate` records an exact empty
`SHARPEMU_SAVEDATA_DIR`, `SHARPEMU_LOG_PAD=1`, and no synthetic pad press.
During the run the directory acquired only the 2 MiB reserved
`sce_sdmemory/memory.dat` and trophy state—no ordinary save slot. No nonzero
pad read occurred. Nevertheless, Astro reached
`Level has started: title_controller_ship` and immediately printed the same
`Playing: , Continue: worldmap` line. A
`PrintWindow(PW_RENDERFULLCONTENT)` capture retained as
`printwindow-fresh-title.png` shows the guest region still black outside the
SharpEmu HUD.

This is a **CONFIRMED control**: persisted save selection and synthetic input
are not what produce the `Continue` line. It also invalidates the claim that
the line itself proves the title level ended. Until a real state write or level
end is observed, treat worldmap document loading as preload and return to the
first required title renderer/presentation boundary. Do not force
ProductNextSequence state 8, 9, or 10.

The historical nonblack `0x53AA00000` run still has no identified override and
remains a live contradiction, not evidence that may be dismissed.

## 2026-07-30: the Sony mode-27 correction is real but not sufficient

The first post-fix boot is
`artifacts/game-runs/astro/20260730-160229-corpus-gate`, at exact Git head
`829dcd96141b419f1a4c3c6801a390924960d819`. Its
`runtime.environment_effective` contains only the corpus-gate defaults; no
shader, texture, export, or capture override was inherited.

The run reached:

```text
Level has started: ps_logo             t+110.906 s
StartLevel title                       t+254.984 s
Level has started: title_controller_ship
LevelDocument Loaded: worldmap         t+332.656 s
Vulkan device lost                     absent
```

An immediate `PrintWindow(PW_RENDERFULLCONTENT)` capture after
`Level has started: title_controller_ship` is retained as
`printwindow-title-started.png`, SHA-256
`88CB8E0FFDB2C9896FDE4988FC12CD3D4C8D2F920954236E3E47938408358401`.
Excluding the title bar and SharpEmu HUD rectangle, all 1,219,104 analyzed
pixels are exactly RGB zero. The run was then stopped deliberately; its
`process-exit` result is not a crash or timeout.

This is a **CONFIRMED negative differential**: replacing the wrong mode-27
R_X equations does not by itself make Astro's required title frame visible.
The correction remains valid for guest-memory detiling, but it was not the
sole black-frame cause and may not be exercised by this host-resident title
lifetime.

The first explicit unsupported graphics semantic after the title starts is
not another tiling error. `es=0x500695D00` reports NGG allocation plus a
target-20 primitive export at log line 92129, followed later by the already
measured amplified programs `es=0x50011FC00` and `es=0x5000F6700`. Several
compute programs immediately around the same boundary also report four
unrecoverable scalar-memory loads each. Temporal proximity is not causality:
the next probe must bind one of these programs to a required title-menu
target/writer before changing NGG or SMEM semantics. A warning count or an
unconditional force-export experiment is not that proof.

## 2026-07-30: Sony's address oracle finds the first concrete half-res defect

The completed Prospero SDK extraction changes the surface question from an
inference to an exact differential. `libSceAgcGpuAddress.dll` reports Astro's
mode-27 960x540 RGBA16F target as:

```text
block                  128x64
padded extent          1024x576
tiled footprint        4,718,592 bytes (0x480000)
linear visible bytes   4,147,200
```

SharpEmu already computed the same footprint, but its old within-block R_X
equation was not Sony's. Exhaustively comparing all 518,400 visible texels
found 450,304 wrong offsets. The first mismatch was `(8,0)`: Sony returned
`0x2100`, SharpEmu `0x2000`. This is a **CONFIRMED emulator defect** capable of
turning guest-resident half-resolution render targets into black or scrambled
host uploads while leaving allocation-size checks green.

`GnmTiling` now uses SDK 10.00's `s_tableRX[2D][1xaa]` equations. The exact
960x540 8-byte Astro surface does match Sony after the correction.

**2026-07-31 correction:** the stronger sentence previously recorded here -
zero mismatches across 60,000 coordinates at every element size - was not
supported by a complete production-vs-vendor detile and is retracted. A new
direct `detileSurface` differential found that mode 27 still had X2/Y2 swapped
at four bytes per element. It also found broader default-policy/equation defects
outside Astro's exact format: modes 4 and 8 are Sony-reserved but were trusted,
public PRT mode 17 was refused, and modes 1 and 24 disagreed for every valid
tested element size.

`tools/SharpEmu.Tools.AgcGpuAddressOracle` now runs Sony's full detiler and
production `GnmTiling` over identical bytes. After the correction, all 29
valid public 2D, single-mip, single-slice, single-sample combinations match at
both 257x193 and 960x540. The original boot remains a valid negative result:
fixing Astro's already-correct 8-byte mode-27 layout could not change that run.

This does not yet prove that this path is the first live Astro black transition:
no post-fix game boot was run, by instruction. The next permitted run should
capture the first required title-menu writer and its sampled/target images,
then stop. It should not wait for worldmap or the final tonemap.

The same completed SDK supplies an authoritative raw-instruction oracle.
`libSceShaderIsaP.dll` requires option id 1 to select the generation; null
options silently select generation zero. The latter falsely rejects observed
Prospero 64-bit-compare and BVH encodings, so the earlier null-option
“contradictions” are refuted. Sony also decodes the exact
`00 00 D8 D9 0D 00 00 00` bytes as `ds_read_b64 v[0:1], v13`.

## 2026-07-30: the dropped DS shader is statically a clustered-list writer

The exact `cs=0x5006E8500` body is uniquely recovered from Astro's eboot at
file offset `0xE1D1170`, with executable SHA-256
`E130852D237F7F57B2E621FABAF93D952FC5E169E8BF62164703810AD468A102`.
After the `ds_read_b64` correction, current master decodes all 6,599 embedded
shader records and compiles this 814-instruction shader completely to a
349,412-byte SPIR-V module with the synthetic resources supplied offline.

Its role is no longer an inference from adjacency. Static data flow contains
six GDS `ds_append` allocations, two LDS exchange operations, and ten global
stores that publish linked-list nodes and category tails. This proves the
shader is a clustered-list producer with six write slots, not a clear.
Translation previously failed before live binding telemetry, so mapping those
slots to the captured head table `0x553BA3A50` and node pool `0x551BE0000`
remains **UNMEASURED**. Do not turn the plausible addresses into a fact until
a permitted run records them.

The historical `20260729-215508-corpus-gate` nonblack `0x53AA00000` pattern is
not a clean contradiction: that run explicitly exported multiplicative shader
factors into the target. No natural retained run has yet shown this
half-resolution lifetime nonblack.

## 2026-07-30: the missing worldmap start belongs to ProductNextSequence

Static xrefs in the exact title binary identify the guest owner without
guessing from worker-thread names. `ProductNextSequence::Update` starts at
eboot RVA `0x6EB9080`; its state field is `this+0x878`. The
title-to-worldmap path enters state 14
(`ProductNextSequence::UpdateMainToHub`) and assigns state 8 at
`0x6EC2982`.

State 8 normally tears the old state down and enters state 9. State 9 calls
the classifier at `0x6EC8B10`, finds the next-level registry record, and reads
its type at `0x6EC8F47`. A type of 7 or 8 selects state 12 at `0x6EC8F87`;
other types select state 10 at `0x6EC8FC6`. Both are real StartLevel owners:

- state 12 calls the StartLevel implementation at `0x6EB9B3A`;
- state 10 calls it at `0x6EBD484`;
- the common callee occupies `[0x6ECADB0, 0x6ECC120)`; and
- its sole `StartLevel` format-string xref is `0x6ECB2E1`.

This is an **EXTRACTED owner chain**, not yet the causal defect. The exact
registry loader at `0x11DF780` opens
`data/prein/save_data/levels.xml`; its record parser at `0x11E1FB0` hashes
the `LevelType` text with FNV-1a32. `WorldMap` hashes to `0x9A5B5D43`,
selects numeric type 6 at `0x11E2A23`, and is stored at `record+4` by
`0x11E2A2F`. Types 7 and 8 are PartsBuilder and RobotBuilder. Worldmap
therefore deterministically selects state 10 and its callsite at
`0x6EBD484`; state 12 is not the worldmap path.

This does not show whether runtime fails before state 9, fails to match the
worldmap record, reaches state 10 but skips the call, or enters the callee and
fails before its log.

The smallest live discriminator is change-only telemetry for
`ProductNextSequence+0x878`. Existing import-return telemetry can prove that
the state-9 classifier executed: its registry comparison calls a PLT import
at `0x6EC8F37` and returns at relocated
`imageBase+0x6EC8F3C`. Use
`SHARPEMU_PROBE_IMPORT_RET_ADDRESS` for that return address. A matched
worldmap record must expose type 6 at `r12-0x1C`, followed by state 10 and
callsite `imageBase+0x6EBD484`. If the comparison is never reached, the next
anchors are state 8's second intent-check return at `0x6EBD231` and final
state write at `0x6EC383A`. This replaces the broad Odx-wait theory with a
small set of falsifiable guest predicates.

At that classifier import, the preserved registers also identify the live
objects: `rbx` is `ProductNextSequence*`, `r12` is the candidate registry
record's libc++ string (`recordBase=r12-0x20`), `r14` and `r15` are candidate
and next-level ID lengths, and `rdi`/`rsi` are their data with
`rdx=min(r14,r15)`. `eax==0` plus equal lengths selects the record, whose type
is then read from `r12-0x1C`. The call is memcmp-shaped; its exact import name
is unresolved. The address-selected import probe now reports all argument and
preserved registers plus return `rax`, so this path needs no fabricated
registry type.

The address chain is tied to Astro Bot 1.007
`eboot.bin` SHA-256
`3B5100797FE83663E18A650F82F901D066ACF1029AE80CFB1BE638FE0839DEBD`.
This file is an fSELF, not a flat embedded ELF. SELF entry 1 maps ELF PHDR0
RVA zero to physical file offset `0x3B1F0`; entry 3 maps PHDR1 virtual address
`0x74F0000` to physical offset `0x7532DF0`. Revalidation through those mappings
finds the exact state writes, classifier, common callee, and strings recorded
above. In particular, RVA `0x6EC8F47` maps to physical `0x6F04137` and decodes
`mov eax,[r12-0x1C]; add eax,-7; cmp eax,1`; the `StartLevel` xref at RVA
`0x6ECB2E1` maps to physical `0x6F064D1` and targets the format string at
physical `0x80B3C89`. Treating the embedded ELF's `p_offset` as a physical
SELF offset produces unrelated bytes and a false retraction.

## 2026-07-30: the first required visual checkpoint is the title menu

The first steady, interactable frame that Astro's shipped data requires is
`title_controller_ship`, not worldmap. Its exact `level.lvx` links
`controller_ship_title_ui` to `ui_title_03`, declares SelectNewGame and
SelectContinue events, and loads the `title_screen` scene parameter. The
linked `ui_title_03/level.lvx` defines `main_menu_press_any_button`, a
`ui_selectable_list`, and New Game/Continue choices for three save slots.
`save_data/title_ui.xml` separately supplies Continue and New Game text
positions. These are title-owned acceptance anchors, not emulator HUD pixels.

The retained ordinary run `20260730-124106-corpus-gate` reaches
`Level has started: title_controller_ship`, then almost immediately logs
`Continue: worldmap`. Its current 2 MiB SaveDataMemory contains
`PlayCount=323` and `PlayingSaveSlot=1`, so the Continue choice is consistent
with persisted title state. SaveData HLE does not fabricate an ordinary slot
from `sce_sdmemory`: both save-directory enumeration and save-data count
exclude the reserved directory.

Fresh-root observations retain `PlayCount=1` without `PlayingSaveSlot`, but
the available clean runs ended before `title_controller_ship` fully started.
They therefore do not prove whether the first-boot menu renders. The next
permitted visual validation should use a fresh save root, wait for
`Level has started: title_controller_ship`, and capture the guest window
before selecting a slot. Enable `SHARPEMU_LOG_PAD=1` to distinguish real host
input from guest auto-selection. Until that capture exists, worldmap is a
later control-flow checkpoint, not the first visual acceptance test.

## 2026-07-30: the Odx condition is not yet the worldmap blocker

The longest retained control reaches `LevelDocument Loaded: worldmap`, runs
for about 214 seconds more, and never prints `StartLevel` or
`Level has started: worldmap`. Condition traces show `OdxAsyncLoader`
repeatedly leaving and re-entering the same condition wait while broadcasts
advance its epoch. This proves that the thread is not stuck inside
`pthread_cond_wait`. It does not prove that this wait gates `StartLevel`.

The same worker and condition wait for long intervals earlier in boot and then
resolve when work arrives. The final wait may therefore be an ordinary empty
work queue: an effect of the missing transition request rather than the cause
of it. The older wording "the worldmap predicate never becomes true"
overstated the causal evidence.

Two earlier explanations are refuted. The associated rwlock completed every
observed blocked acquisition (36 blocks, 36 wake-acquires, 36 releases), then
completed 1,251 more writer acquire/release pairs. Missing `.odx` names are
normal probes beside shipped `.odxb` content, and no file I/O occurs between
the worldmap document-load message and the first failed transition. AudioOut2
retry volume is also control behavior, not a discriminator.

The unresolved root is the guest decision that should request or authorize
`StartLevel: worldmap`. The exact ProductNextSequence chain above supersedes
the Odx wait as the next measurement target. Probe the state 8/9/10 anchors;
do not modify pthread semantics to force an ordinary worker queue awake.
Guest condition addresses move between runs; the return PC and behavioral
sequence are the identity.

## 2026-07-30: missing DS_READ_B64 drops a clustered-list dispatch

The retained trace exposes a concrete missing-ISA defect upstream of Astro's
clustered-list family. In
`20260729-124336-corpus-gate/attempt-01.log`, compute shader
`0x5006E8500` is dropped with
`unknown-ds op=0x76 word=0xD9D80000`; the next command is the
`0x5006EAC00` DsAppend reset/build dispatch.

Sony SDK 10.00's executable ShaderIsa oracle decodes the exact bytes
`00 00 D8 D9 0D 00 00 00` as `ds_read_b64 v[0:1], v13`.
LLVM's GFX10 instruction table and byte-exact MC fixtures independently agree.
The curated acelogic SharpEmu implementation also uses the same decode and
lowers it as two consecutive 32-bit LDS loads.
Master already constructed two destination operands for the opcode name, but
the decoder never produced that name and the Vulkan emitter had no matching
case. The correction adds both missing pieces and an included-solution
regression that decodes paired destinations and compiles the Workgroup-memory
access.

This is a **CONFIRMED dropped-work root cause**. The exact 814-instruction
program now decodes and compiles offline; static data flow proves six
clustered-list write paths built from GDS allocation, LDS exchange, and global
stores. Its precise live address mapping remains **UNMEASURED** because the old
translation failed before binding telemetry. A permitted run must map those
slots before claiming they initialize the zero A head table at `0x553BA3A50`
or node pool at `0x551BE0000`, then remeasure `ps=0x5008F1400` cap hits.
Filling a guessed sentinel is not a valid substitute.

## 2026-07-30: the apparent exact `0x50063F800` eboot match is refuted

The pixel shader archived at eboot offset `0xE15C418` is a real serialized
53-instruction program, but it is not the retained runtime program merely
because its first sample is also at PC `0x20`. Its executable bytes through
PC `0x108` hash to
`04AACB30E561699B808045239FF620A208F151A1A7587ECD730F554345804868`.
It has a second image at PC `0x28` and an `SBufferLoadDwordx16` at PC `0x68`.
Retained static discovery for runtime `0x50063F800` has one image, no SMEM
sites, and a smaller generated SPIR-V module. Static discovery cannot remove
those instructions, so the candidate mapping is rejected.

The retained trace still proves the narrower live fact: the translated
runtime program samples the live Vulkan image for `0x53AA00000` at PC `0x20`
and writes the full-resolution target. It does not prove the rest of the
runtime instruction stream from the eboot lookalike.

The audit also found a generic cache-identity defect. Decoded programs were
cached by `(shader address, size)` rather than AGC shader-object identity. A
same-sized shader-heap reuse can pair current header/user-data state with an
old instruction stream. Commit `d359578` adds the AGC header to the program
cache identity and reproduces the failure with an isolated same-address,
same-size, new-header regression. The correction passes a zero-warning Release
build and the full 2,289-test solution. Whether this specific Astro address
was reused remains **UNMEASURED** until a permitted run captures the live
header plus exact code bytes or hash.

## 2026-07-30: the measured black half-resolution interval is a transition frame

The retained ordered interval must not be used as proof that worldmap scene
geometry was submitted and lost. Its position in the title log is exact:

```text
line 1578126  GAME: Level has started: title_controller_ship
line 1583980  PLAY: Playing: , Continue: worldmap
lines 1596051..1597xxx  the selected 0x53AA00000 writer interval
line 1626697  LevelDocument Loaded: worldmap
```

The 49 selected writers therefore execute after the title level has ended and
before the worldmap document has even finished loading. No retained control
ever reaches `Level has started: worldmap`; the previous 480-second control
waited about 214 seconds after the document-load message without starting it.
A black dynamic-resolution target during this interval can be guest-requested
transition output. It is not by itself evidence that an expected base-scene
producer was dropped.

Exact shader evidence agrees with that narrower interpretation. The observed
`0x5002AA400 / 0x5002AFC00` family deliberately samples black atlas-edge
texels. The exact `0x50011FC00` amplified program receives inactive particle
records and emits only its one-vertex/one-primitive degenerate fallback. The
matching `0x5000F6700` family is now admitted to the same replay contract, but
its live input and coverage remain unmeasured. Bound textures, target write
masks, and draw counts do not prove that any of these optional effect draws
should contribute color.

**CORRECTED:** `ps=0x50063F800` certainly copies a numerically black
`0x53AA00000` image over a previously nonblack full-resolution target, but the
guest requests that copy during a level transition. Calling the copy
destructive describes the bytes, not guest intent. The visual acceptance
frontier is now the first frame after a proven
`Level has started: worldmap`, or a title-era frame whose exact guest state is
known to be visible on hardware. Until one of those exists, do not fabricate
scene pixels to make the transition nonblack.

This does not erase the two real implementation defects found on this path:
the amplified native-GS contract was address-gated (`3024210`), and
same-address color/depth views ignored writer order (`a5f0a3c`). Both are
fixed and offline-verified, but neither has a post-fix Astro measurement.

## 2026-07-30: exact NGG tail refutes compact-index and wrong-wave theories

Offline reconstruction from the title binary closes two suspected defects in
the amplified native-GS replay without another boot. The exact
2,858-instruction program is embedded in `eboot.bin` at file offset
`0xE2FDEAC`; its target-20 export is the retained PC `0x3CA4` / `v40`
program used as `es=0x50011FC00` and by the matching `es=0x5000F6700`
launch contract.

The shader computes `v45=(waveIndex<<6)+lane`, so `v45` is the physical local
invocation 0 through 191. At the tail, PCs `0x3C20..0x3C40` reconstruct
`v39=(waveIndex<<6)+lane` again. It is not a separately compacted destination.
Primitive lanes are the dense prefix `0..8N-1`; position/PARAM lanes are the
dense prefix `0..10N-1`. Consequently SharpEmu's capture stores indexed by
`GlobalInvocationId.x`, with 192 slots per workgroup, address the same records
as the guest's `v39` connectivity. Redirecting the stores through `v39` would
be an identity transformation for this shader.

Allocation ownership is also exact. PC `0x3C04` makes waves 1 and 2 skip the
`m0` pack and `GS_ALLOC_REQ`; only wave 0 owns the allocation word. The
indirect-command writer selected by `localInvocationId.x==0` is precisely
wave-0 lane 0. The retained retirement capture agrees: every group contains
`[0x1001,1,1, 0,0,0, 0,0,0]`. That is not a wrong-wave read. It is the
shader's deliberate dummy allocation after zero input survivors. The single
exported primitive is degenerate and the shader emits no POS/PARAM payload.

This reinforces the earlier PC-`0x0058` boundary. In the retained
`es=0x50011FC00` occurrence, all 19 structured-record loads are zero, and the
record was attributed to particle compute `cs=0x555F4F500`, which itself left
that consumed window zero after retirement. An inactive particle draw is
allowed to be empty; it is not evidence that the half-resolution base image
should contain color.

**REFUTED OFFLINE:** compact output indexing and the indirect allocation wave
are not root causes for this exact shader. **UNMEASURED LIVE:** the generalized
`es=0x5000F6700` replay and the writer-order alias fix have not been booted.
The first permitted checkpoint must report PC-`0x0058` input values,
allocation counts, replay raster coverage, and the paired target result. A
nonblack bound texture alone does not prove that an input-empty draw should
write a pixel.

The exact material pixel program was also recovered from `eboot.bin` at file
offset `0xE2413EC`. Its final compressed MRT0 path multiplies RGB separately
from alpha: the `1-s24` term feeds alpha only. This independently confirms the
later live conclusion that `s24=1.0` is not a black-RGB explanation.

## 2026-07-30: same-address color/depth aliases were sampled without guest writer order

The retained worldmap sequence closes the resource-residency side of the
half-resolution fork. Guest address `0x53A500000` has two host
representations in SharpEmu:

- `cs=0x5006C6A00` writes an initialized 960x540 R32
  `GuestImageResource`; the same-command readback contains 518,400/518,400
  nonblack pixels and repeated float `1.0`;
- graphics also creates a distinct `GuestDepthResource` at the same guest
  address; and
- the later material texture descriptor names that address, while
  `ResolveTextureResource` returned the depth image first whenever both host
  objects existed.

That last rule discarded guest work order. A later compute/color write could
be complete and visible in its Vulkan image, yet sampling still selected an
older depth image merely because it existed. This is a general guest-memory
coherence defect, not an Astro address or shader special case.

Sony's tiled-deferred sample is the contract-level cross-check. It derives the
sampled depth texture from the depth render target's same data address and
uses `kFlushCompressedDepthBufferForTexture` before the texture consumer.
Those are ordered views of one guest allocation, not unrelated resources
whose selection may be decided permanently by aspect.

The SDK makes the identity rule stronger than that single depth example.
`agc/core/translate.h` permits render-target-to-texture and depth-to-color
translations while controlling whether compression is maintained. The
tiled-deferred resource manager creates smaller render-target views by
reusing the donor's exact data, CMask, FMask, and DCC addresses. Sony's
metadata sample similarly binds a texture to the same render-target backing
before and after in-place DCC decompression. SharpEmu may use separate Vulkan
objects internally, but it must preserve the ordered contents of that one
logical allocation.

Commit `a5f0a3c` records a monotonically increasing writer serial on the
separate color/storage and depth host representations. Sampling an aliased
address now selects the newest initialized representation. Writer order is
advanced only for operations that actually write: nonzero color write masks,
enabled depth writes or clears, color/metadata clears, fills, image copies,
uploads, and writable storage bindings. Equal or absent order preserves the
old depth priority. The policy contains no guest address or shader ID.

**CONFIRMED OFFLINE:** the isolated correction passes five focused alias-order
tests, the exact no-incremental Release build with zero warnings, and the full
2,286-test solution. **UNMEASURED LIVE:** no Astro boot was performed after
the correction. The next permitted run must log which representation the
first `0x5000F6700 / 0x5000FD100` material draw samples and capture that
writer's target immediately; it must stop at that checkpoint rather than wait
for the final present.

This finding does not claim complete cross-aspect materialization. Writer
order fixes the proven stale-selection path when the newest host image already
contains the authoritative pixels. Operations that require converting between
incompatible depth/color representations still need an explicit copy or
resolve.

The current causal ledger is:

- **fixed, live outcome unmeasured:** native-GS/NGG replay was gated by one
  shader address and one vertex count (`3024210`);
- **fixed, live outcome unmeasured:** same-address sampled color/depth aliases
  ignored the latest writer (`a5f0a3c`);
- **eliminated at this boundary:** the full-resolution scene is black;
- **eliminated at this boundary:** final-tonemap constants or exposure first
  turn a live input black;
- **eliminated for the measured draws:** `ps=0x5002AFC00` should contribute
  color—its guest-produced `(5,5)` coordinates clamp to black BC7 edge texels;
  and
- **not causal for the measured R32 lifetime:** skipped
  `kDccDecompress` destroys live color. The ordered expanded host image is
  already authoritative there; DCC remains a general implementation gap, not
  a license to fabricate pixels.

Sony SDK 10 also fixes the exact limit of that conclusion. DCC keys can encode
whole RGBA blocks without corresponding expanded pixel bytes, and
`kDccDecompress` implicitly performs fast-clear elimination and FMask
decompression in place. A texture-compatible DCC surface can be sampled after
the prescribed synchronization without decompression; an incompatible view
must be materialized. Thus raw zero color bytes do not prove a compressed
logical image is black, while a skipped mode-6 packet also does not invent new
scene color. Use the exact lifetime, metadata state, and host representation.

## 2026-07-30: the remaining material writers exposed an address-specific NGG replay hole

The 49-writer interval has a concrete implementation defect after eliminating
the 13 intentionally black `es=0x5002AA400 / ps=0x5002AFC00` draws. Six
`ps=0x5000FD100` draws use a fully nonblack captured BC7 source, but their
paired native primitive shader `es=0x5000F6700` was never admitted to NGG
compute replay. The old gate recognized only `es=0x50011FC00` with exactly 120
input points. Consequently all `0x5000F6700` draws, plus the 2-point
`0x50011FC00` draws, fell into the plain-vertex fallback that already reports
it is correct only for pass-through primitive shaders.

The two export shaders have the same retained native-GS launch contract:

- `VGT_SHADER_STAGES_EN=0x00002030`: GS enabled, not passthrough, GS wave64;
- `GE_CNTL=0x00002613`: 19 input vertices and 19 input primitives;
- maximum 190 output vertices, triangle output, amplification factor 8;
- one allocation request and one target-20 primitive export;
- one POS0 export, eleven PARAM exports, and no export loop;
- vertex index in VGPR5, launch state in SGPR3, and no extra user VGPRs.

Sony's SDK 12 ISA specification is authoritative for the native-GS ABI:
SGPR3 is `s_gs_wave_id`, and with tessellation disabled VGPR5 is the vertex
index while VGPR8 is instance ID/user VGPR. Sony SDK 10's
`agc_basic_geometry_shader/native_prim.pssl` independently supplies the
per-subgroup count and target-20 connectivity contract. The implementation now
matches this complete submitted state instead of a guest shader address and
accepts every observed nonzero auto-index count (`2`, `6`, `67`, `120`, and
`135`). Any state deviation is still rejected.

**CONFIRMED OFFLINE:** the exact no-incremental Release build succeeds with
zero warnings; the focused contract tests pass 9/9; the full solution passes
2,281 tests. **UNMEASURED LIVE:** no Astro boot was run after this change, so
it is not yet evidence that `0x53AA00000` becomes nonblack. The next permitted
boot should stop as soon as the first `0x5000F6700 / 0x5000FD100` result is
captured, before waiting for the final frame.

## 2026-07-30: the observed 2AFC00 material family is intentionally black

The latest exact shader and texture evidence corrects the previous
interpretation of the `ps=0x5002AFC00` PC `0x01D0` probe. The useful pipeline
frontier is now:

```text
0x514080000 full-resolution HDR scene: nonblack
    -> 0x53A500000 half-resolution depth: nonblack
    -> 0x53AA00000 begins as the guest-requested clear value
    -> 49 material/effect writers observed, final target still black
    -> ps=0x50063F800 replaces the full-resolution scene from that black input
    -> tonemap inherits black
```

The observed `es=0x5002AA400 / ps=0x5002AFC00` occurrences are no longer
evidence that sampling or NGG replay failed. Their exact export-shader IR
writes `PARAM1=(5,5,0,0)` deliberately:

```text
0x3064 VCvtF32I32 v12 <- inline integer 5
0x3068 VMovB32     v13 <- inline 0
0x307C Exp target 33 <- v12,v12,v13,v13
```

The pixel shader interpolates attribute 1 at PCs `0x00D0..0x00D4` and uses
those coordinates for the ordinary sample at PC `0x01D0`. Sony's sampler mode
2 is clamp-to-edge, matching SharpEmu's Vulkan mapping. The 13 retained draws
select three captured 1024x1024 BC7 sources at PC `0x01D0`. An offline BC7
decode produced:

| Guest address | Format | SHA-256 of captured blocks | Nonblack texels | Bottom-right RGBA |
|---|---|---|---:|---|
| `0x10FB350000` | BC7 sRGB | `852952455e650f02acd1a5808e0ea7dfca6a69e1cbd0dea47656f981e968b4db` | 736,782 / 1,048,576 | `(0,0,0,255)` |
| `0x110D1B0000` | BC7 UNORM | `08c798081957afb02915d6dc60df6a4659b641dd3e90061563b88494f5407404` | 460,272 / 1,048,576 | `(0,0,0,255)` |
| `0x1111900000` | BC7 sRGB | `0ee60cd6243918e31a54514c560d7f4a0e476ab6afdaa781fd9afe8a1c1966ba` | 466,343 / 1,048,576 | `(0,0,0,255)` |

Thus the textures are not globally black, but `(5,5)` clamps to a genuinely
black bottom-right texel for every observed `2AFC00` source. The decoder is an
offline inspection tool, not a contract oracle; its selected-texture result
agrees with the earlier live Vulkan sample differential.

The shader-address plus binding-PC force-white differential remains valuable:
replacing only PC `0x01D0` with a known uncompressed white texture produced
31,436 nonblack pixels. It proves descriptor selection, upload, sampler,
`ImageSample`, and color export can work for this draw. It does not prove that
the real BC7 sample should have been white. Filling compressed payload bytes
with `0xFF` was an invalid diagnostic because arbitrary BC bytes do not
represent white texels; the diagnostic now substitutes an actual RGBA image.

NGG replay is also exonerated for this occurrence. The guest export shader,
300-group replay dispatch, per-group indexed-indirect commands, and observed
interpolant agree. There is no missing Vulkan instance divisor at this
boundary. The repeated `(5,5)` value came from guest code, not a replay
transport error.

The ordered writer interval contains 49 draws: 13 with
`ps=0x5002AFC00`, 24 with `ps=0x500006E00`, six with
`ps=0x5000FD100`, and six with `ps=0x500126600`. All 13 observed `2AFC00`
draws now have a shader-consistent reason to stay black. The remaining
question is narrower: which of the other 36 writers is expected to contribute
color, or which ordered initialization/copy before the interval is missing?
Select future texture probes by shader address and binding PC; transient guest
addresses changed across otherwise comparable runs.

There is no hidden color operation at sequence 46093. The ordered-action log
identifies it as a `release_mem` notification. Sequence 46092 is the last
1920x1080 writer to `0x5367F0000`; sequence 46094 is the first 960x540 writer
to `0x53AA00000`. Sony's tiled-deferred sample explicitly clears its G-buffer
before material writers, so a zero target before sequence 46094 is expected.
The next offline checkpoint must identify an expected contributing writer, not
re-litigate whether the clear itself is black.

`ps=0x50063F800` has blending disabled and RGB write mask `0x7`; Sony's
`CxBlendControl` definition therefore says the incoming fragment replaces the
destination. The destruction of the earlier full-resolution scene is
guest-requested fixed-function state, not a blend decode error. Exact
`0x50063F800` shader reconstruction is the next discriminator: determine
whether it is intended to copy/composite the half-resolution buffer and what
condition makes that buffer meaningful.

## 2026-07-30: checkpoint bisection moves the first zero into the material-color path

The current boundary should be debugged as an ordered pipeline, not by waiting
for the final present:

```text
0x514080000 full-resolution HDR scene: nonblack
    -> 0x53A500000 half-resolution depth: nonblack
    -> ps=0x5002AFC00 material color into 0x53AA00000: unresolved
    -> ps=0x50063F800 copies the black half-resolution input over the scene
    -> tonemap inherits black
```

Three bounded, same-PC measurements substantially narrow the unresolved edge.

First, `20260730-111549-corpus-gate` captured the material shader after PC
`0x1174`. Its same-PC marker covered 31,426 pixels. The value in `v4` survived
the PC `0x1008` scale and PC `0x1174` depth/range fade in 30,930 pixels, with no
nonzero value becoming zero at the fade. `v7` was exactly `1.0` for every
marked pixel. That fade is not the first-zero operation.

Second, `20260730-112127-corpus-gate` placed a marker and value captures after
PC `0x1278`. All were absent. Offline control-flow reconstruction explains the
absence: binding 1 byte offset 76 supplies `s106=0`; the compare at PC `0x1190`
is false and `s_cbranch_scc0` at PC `0x11A4` jumps directly to PC `0x127C`.
The entire PC `0x11A8..0x1278` smoothstep block is optional and was skipped.
The earlier marker-free observation at PC `0x1278` therefore measured no
value, not a zero value.

Third, the constant formerly described as the black-output cause is
alpha-only. Binding 6 byte offset 32 really does load `s24=1.0`, but
`1-s24` is multiplied into the packed alpha path at PCs `0x159C..0x15B0`.
The RGB export instead inherits `v48`, `v49`, and `v50`. At the selected
material record, `s32[29:27]=0`; the shader multiplies that selector by five,
writes `M0=0`, and executes:

```text
v_movrels_b32 v48, v4
v_movrels_b32 v49, v5
v_movrels_b32 v50, v6
```

SharpEmu and the independent Kyty lowering agree that `v_movrels` reads
`VGPR[encoded source + M0]`. Since this occurrence has `M0=0`, neither
relative-index wrapping nor a different register-bank choice can affect it.
The next live checkpoint is consequently the values of `v48..v50` after PC
`0x0D10`, with a same-PC marker. If those values are nonzero, the next edge is
the texture multiply at PCs `0x0FD4..0x0FDC`; if they are zero, the material
array feeding `v4..v6` is the boundary.

`20260730-112911-corpus-gate` completed that checkpoint. The marker is exactly
`1.0` in 31,429 pixels, while all three captured values after PC `0x0D10` are
exactly zero in all 518,400 pixels. `VMovrelsB32` therefore executed and copied
zero source values; it is not the first-zero operation for this occurrence.

The exact def-use frontier is earlier. PCs `0x00E4..0x0188` initialize
`v4..v43` to zero. A material-mode bit then selects one of two texture paths:

```text
PC 0x01B4: sample one channel into v7; v4..v6 remain zero
PC 0x01D0: sample RGBA into v4..v7
```

No instruction rewrites `v4..v6` between that choice and the `M0=0`
`v_movrels` sequence. The next paired checkpoint must mark both mutually
exclusive sample PCs and capture the corresponding sample result. It can
distinguish intentional mask-only guest output from an RGBA texture sample
that incorrectly returned black in one readback.

`20260730-113455-corpus-gate` resolves that choice. The PC `0x01D0` RGBA-path
marker is exactly `1.0` in 31,395 pixels; the PC `0x01B4` mask-only marker is
absent. All three RGB values loaded by the RGBA sample are exactly zero. The
first-zero boundary is therefore the ordinary `ImageSample` at PC `0x01D0`,
before `v_movrels` or any material arithmetic.

The sampled resource is a 1024x1024 BC7 texture in Sony tile mode 9
(`kStandard64KB`) with mip levels 0 through 10. This is not enough to blame
detiling. An offline call to Sony SDK 10.00's authoritative
`libSceAgcGpuAddress.dll` produced:

```text
block=64x64 BC7 elements, first mip in tail=3
mip0 offset=393216, mip0 size=1048576, chain size=1441792
```

SharpEmu computes the same base offset and sizes. Sony
`computeTiledElementByteOffset` also agreed with SharpEmu's mode-9,
16-byte-element equation at every checked block edge and interior coordinate,
including `(0,0)`, `(63,63)`, `(64,0)`, `(127,127)`, `(128,0)`, and
`(255,255)`. Mode-9 BC7 addressing is cleared by direct Sony comparison, not
assumed.

The SDK identified a separate sampler bug. `Core::Sampler` says its
depth-compare field is the function used by `SAMPLE_C`; it does not make
ordinary samples comparisons. Astro's material sampler carries the common
`kAlways` value in that field. SharpEmu incorrectly mapped any nonzero value
to Vulkan `compareEnable=true`, even though its SPIR-V lowering already
implements `SAMPLE_C` comparisons explicitly. Vulkan samplers are now always
non-comparing and a regression test preserves that contract.

That correction is not promoted as the Astro fix. In
`20260730-114653-corpus-gate`, the same occurrence-2 PC `0x01D0` checkpoint
still marked 31,363 pixels and returned exactly zero for RGB. The next
differential must force the exact occurrence-2 BC7 source texture white while
capturing interpolated `PARAM1.xy` and the sample result. A white result
localizes the remaining fault to guest texture contents/mip selection; a
black result localizes it to binding, coordinate, upload, or sample lowering.

The DCC theory is also refuted for this exact lifetime. In
`20260730-093944-corpus-gate`, the sole mode-6 operation at guest address
`0x53AA00000` is Sony `kDccDecompress` on a 1920x1080 target during
`StartLevel ps_logo`. The problematic writers use a later 960x540 lifetime
during `Playing/Continue worldmap`, and no 960x540 mode-6 operation occurs.
The presenter retains those size/format variants as distinct guest images.
An address match across those lifetimes is not evidence that a dropped
decompress emptied the worldmap material target.

Sony's tiled-deferred tutorial also establishes the intended initialization
contract. `GpuTaskClearBuffers::runTask` builds the four active G-buffer
subtargets, clears every DCC-enabled G-buffer to `{0,0,0,0}` with
`clearRenderTargetCs`, and explicitly memory-zeros the material-ID G-buffer
when DCC is disabled. `GpuTaskFillGBuffers::runTask` subsequently binds all
four targets and renders the scene with alpha testing. Therefore an all-zero
G-buffer before the first material writer is normal; the discriminating
failure is that no observed writer changes it. Preserving a cleared host image
is not itself loss of the producer.

## 2026-07-30: the retained full-depth draw set does not export MRTZ

Sony's shader ISA defines pixel export target `mrtz` as the one-component
depth value written by a pixel shader. SharpEmu has a real architectural gap:
pixel lowering declares only color locations and silently accepts an export
whose target is absent from `_pixelOutputs`, so a live MRTZ export would be
dropped.

That gap is not the cause of Astro's nearly-clear `0x513560000` depth image in
the retained draw set. The bounded run
`20260730-103453-corpus-gate/attempt-01.log` dumped the five previously
unmeasured pixel-shader families:

- `ps=0x5008BCD00` exports MRT0 and MRT1;
- `ps=0x500696600`, `0x50077AC00`, `0x5008BFC00`, and `0x5008CED00`
  export MRT0 only; and
- `ps=0x5001C2A00` exports MRT0 and MRT1.

Earlier complete dumps already show MRT0/MRT1 only for `0x500781D00` and
`0x5008F1400`. Thus all eight pixel-shader families observed with
`0x513560000` as their depth attachment have exact color-only export lists.
For the traced `0x5008BCD00` draws, Sony context register
`CxDbShaderControl` (`0x203`) is `0x00000010`, while decoded depth state has
testing enabled and writes disabled. The attachment is being consumed for
testing; this phase is not silently losing a pixel-depth export.

MRTZ-to-`FragDepth` support remains a confirmed general correctness task, but
implementing it cannot be labelled an Astro fix without a live Astro MRTZ
export. The active boundary returns to `ps=0x5002AFC00`: its depth-intersection
decision is nonzero, yet a later material multiplier reaches zero before the
packed color export.

## 2026-07-30: the material kill factor points back to depth coverage

The `v53` value that empties EXEC in `ps=0x5002AFC00` is not a direct
G-buffer channel. The shader's live def-use chain is:

- PC `0x0C38` samples `0x53A500000` into `v49`;
- PCs `0x0C50..0x0C70` reconstruct scene depth using
  `s16`, `s17`, `s25`, and `0.01`;
- `v46` is interpolated `PARAM6.x`, while `v48` is `PARAM7.w`;
- PCs `0x0C78..0x0CA8` compare reconstructed scene depth with fragment
  depth and compute a hard or soft intersection factor in `v53`;
- PC `0x0CC8` masks lanes only when that computed factor is zero.

An all-`1.0` HiZ sample can contribute to zero `v53`, but it is not sufficient
by itself: the interpolants and projection constants decide the comparison.
The next value capture must therefore retain all four sides of the decision,
not force the result:

```powershell
$env:SHARPEMU_CAPTURE_PIXEL_VGPR_ADDRESS='0x5002AFC00'
$env:SHARPEMU_CAPTURE_PIXEL_VGPR_POINTS='3128:49:240,3184:49:241,3184:46:242,3208:53:243'
$env:SHARPEMU_FORCE_PIXEL_EXPORT_VGPR_ADDRESS='0x5002AFC00'
$env:SHARPEMU_FORCE_PIXEL_EXPORT_VGPR_BASE='240'
$env:SHARPEMU_TRACE_GUEST_IMAGE_ADDRS='0x53AA00000'
$env:SHARPEMU_TRACE_GUEST_IMAGE_SHADER_ADDRS='0x5002AFC00'
$env:SHARPEMU_TRACE_GUEST_IMAGE_DRAW_OCCURRENCE='1'
```

The points capture sampled depth, reconstructed depth, `PARAM6.x`, and the
pre-alternate `v53`; PC `0x0C88` is before the later EXEC kill, so a visible
export also proves the measured path executed.

The ordered full-resolution D32 input is byte-count-close to clear depth
`1.0`. A completely clear 1920x1080 D32 image would have 4,147,200 nonzero
bytes, while the capture has 4,148,035—only 835 more. This does not count
non-clear pixels, because a changed float can alter multiple byte positions.
It does rank missing or under-covered depth rasterization into
`0x513560000` as the earliest current suspect, plausibly connected to the
still-incomplete NGG primitive-connectivity path. HiZ reduction and DCC
decompression are now downstream of that suspect.

## 2026-07-30: the first full-to-half HiZ dispatch produces nonzero R32F

The ordered pair in `20260730-041226-corpus-gate/attempt-01.log` closes the
hierarchical-depth edge. Occurrence 1719 of `cs=0x5006C6A00`, with
120x68 groups, reads the live full-resolution depth image and writes the first
half-resolution level:

```text
vk.compute_image_probe stage=input-depth
addr=0x513560000 size=1920x1080 format=D32Sfloat
nonzero_bytes=4148035/8294400 hash=0x7FFD6D0B0E3B8543

vk.compute_image_probe stage=output-storage
addr=0x53A500000 size=960x540 format=R32Sfloat
nonzero_bytes=1036909/2073600 nonblack_pixels=518400/518400
hash=0x92AA4A36482903FF
```

The output head is repeated `0000803F`, float `1.0`. The input copy,
dispatch, and output copy are recorded in one command buffer, so this is not a
later-retirement alias. The full-to-half reducer executes and does not produce
zero. Occurrence 1720 is the next 60x34 half-to-quarter edge,
`0x53A500000 -> 0x53AC40000`; it must not be described as the first
half-resolution producer.

This also corrects the remaining stale DCC hypothesis. Sony's SDK samples
require DCC decompression when target-only compressed backing must become
ordinary readable color, but the operation preserves logical pixels.
SharpEmu's initialized expanded Vulkan image is authoritative, so mode 6
preserving that host image is correct. Reinterpreting sparse/stale guest DCC
bytes as uncompressed pixels would be a regression.

This finding applies only to the R32F depth lifetime at `0x53A500000`.
Astro aliases addresses across formats and phases. It does not prove that the
separate half-resolution color/G-buffer targets read by the material/lighting
chain are populated. Their first actual writer or host-image alias transition
remains the color boundary.

## 2026-07-30: wave32 compute removes the title TDR; the guest frame is still black

The V620 advertises a default Vulkan subgroup size of 64 plus subgroup-size
control for 32 through 64 lanes in compute. Astro marks the title's expensive
compute shaders as guest wave32. SharpEmu previously translated and executed
them with the host's default wave64 subgroup, paying the two-part guest-wave
partition cost through their EXEC/ballot/shuffle control flow.

Commit `2e9c52d` carries an effective/required subgroup contract through
compute compilation, pipeline caching, and dispatch, then requests subgroup
size 32 with
`VkPipelineShaderStageRequiredSubgroupSizeCreateInfo`. Six focused tests cover
V620 selection, fallback, stage/range rejection, and cache separation. The
shared-tree validation after integration passed a zero-warning Release build
and all 2,272 tests.

The live differential is
`20260730-040135-corpus-gate/attempt-01.log`. Earlier persistent-GDS runs
compiled `cs=0x50057B800`, spent about 7.4 seconds retiring its 8,160-group
submission, then lost the Vulkan device when `cs=0x5005A8100` followed. With
required subgroup 32, Astro instead reached:

```text
PLAY: [2:51] StartLevel title
GAME: Level has started: title_controller_ship
LevelDocument Loaded: worldmap [worldmap]
```

The run logged no Vulkan device loss before it was stopped after worldmap
load. It did not enable shader GPU timestamps, so this is a watchdog/progress
differential, not a claimed new millisecond timing.

Visual verification remains negative. PrintWindow with
`PW_RENDERFULLCONTENT` captured
`20260730-040135-corpus-gate/printwindow-worldmap.png` at worldmap load. The
1550x882 window contains 125,076 RGB-nonzero pixels, but visual inspection
shows that every one belongs to the title bar or SharpEmu performance HUD.
The guest region is black. This fixes the compute device loss and reopens the
postprocess investigation; it does not satisfy the rendering goal.

The same run also confirms why the hierarchical-depth probe must be selected
by occurrence rather than by shader address alone. `cs=0x5006C6A00` executed
more than 1,700 times before the title/worldmap boundary, alternating
120x68x1 and 60x34x1 dispatches. The retained all-zero and all-one captures
were different boot occurrences. The current input-copy/dispatch/output-copy
probe is ordered within one command buffer, so the next paired capture must
use the exact occurrence/work sequence immediately preceding the first
material writer instead of reusing occurrence 1 or 2.

Finally, do not use the `agc.scalar_pointer_fallback` lines in
`0x50057B800`/`0x5005A8100` as permission to recover data. Sony's SDK says
their `srt=2` metadata is a direct two-dword SRT resource, and the selected
entries are genuinely zero optional BVH resources. Fabricating a pointer would
replace valid guest null state and corrupt the control.

## 2026-07-30: Sony's live PS linkage is identity; the first MOVRELS probe is bypassed

Sony's `CxPsShaderUsage` registers at CX `0x191..0x1B0` are the authoritative
GS-export-to-PS-input mapping. SharpEmu does not yet feed those controls into
shader translation, which remains a real general linkage defect. It is not,
however, the cause of the selected `es=0x5002AA400 / ps=0x5002AFC00` draw.
The address-selected trace in
`20260730-025201-corpus-gate/attempt-01.log` records:

```text
ps_ena=0x00000302 ps_addr=0x00000302
ps_cntl=[0:0x00000000,1:0x00000001,2:0x00000002,3:0x00000003,
         4:0x00000004,5:0x00000005,6:0x00000006,7:0x00000007]
```

All eight live controls are identity mappings with no flat, default, F16, or
custom-interpolation qualifier bits. Directly wiring PARAM locations for this
draw therefore produces the same mapping Sony requested. Do not implement
Sony's linkage controls as an Astro-specific black-frame fix; implement them
as a separately tested architectural correction.

The same trace closes the scalar-constant ambiguity. Binding 6 at byte offset
32 contains `00 00 80 3F`, so the `SBufferLoadDwordx4 s24..s27` at PC
`0x0BEC` loads `s24=1.0`. Parsing the retained full draw trace finds the same
`s24=1.0` in all logged instances of the structurally identical material
programs: 15 `ps=0x500006E00` draws, three `ps=0x5002AFC00` draws, and one
`ps=0x5000FD100` draw. The final `1-s24` factor is genuinely zero guest data,
not unbound-SMEM recovery; these programs also report `smem_zero_filled=0`.

A generic opt-in diagnostic now permits copying an SGPR into the pixel VGPR
file after an exact guest PC
(`SHARPEMU_CAPTURE_PIXEL_SGPR_POINTS=pc:sgpr:destination`). It complements
the existing VGPR point capture and forced-export sink without inventing a
runtime constant.

The first MOVRELS measurement also prevents a false conclusion. In
`20260730-030151-corpus-gate`, exporting `v48`, `v49`, `v50`, and `v55`
immediately after PC `0x0D10` left the selected host image numerically black.
That alone could not distinguish zero values from no surviving fragment.
`20260730-030448-corpus-gate` added an unconditional red marker at the same
PC beside the three captured material channels. The exact readback was still
RGB zero with half-float alpha one:

```text
vk.guest_image addr=0x53AA00000 size=1920x1080
format=R16G16B16A16Sfloat
nonblack_pixels=0/2073600 center=000000000000003C
```

The physical 1920x1080 size is the configured 2x host image for the logical
960x540 target. Since even the marker is absent, this first selected
occurrence does not enter the PC `0x0CF4..0x0D10` relative-register block.
The live material record begins with `s32=0x01FFFFFF`, so
`s32[29:27] == 0`; the scalar `s_cmp_lt_u32 selector,7` would enter the block.
Instead `v_cmpx_neq_f32 0,v53` at PC `0x0CC8` removes every lane and the
following `s_cbranch_execz` bypasses it. The missing marker is therefore
evidence that the earlier per-fragment `v53` factor is zero for this
occurrence, not evidence that `VMovrelsB32` returned zero or that its scalar
selector rejected the block. The run reached `StartLevel title` at 188.469 s,
then exited with code `0xFFFFFFFF` at 203.422 s before worldmap, with no logged
device loss or managed fatal. A future MOVRELS value probe must select an
occurrence whose EXEC survives PC `0x0CC8` and include an execution marker in
the same readback.

## 2026-07-30: the scene renders before a black half-resolution image replaces it

The current master no longer supports the broad statement that Astro never
renders scene colour. Exact same-writer readbacks place the live boundary:

- `20260730-003115-corpus-gate`, after the first
  `ps=0x5008BCD00` draw, reads `0x514080000` as RGB zero with half-float
  alpha one (`nonblack_pixels=0/2073600`).
- `20260730-003536-corpus-gate`, after the first
  `ps=0x5008F1400` draw, reads the same 1920x1080 RGBA16F target with
  `869977/2073600` nonblack pixels and hash `0x56141510827517BE`.
  The full-resolution scene writer therefore executes and produces real
  colour.
- The exact binding trace identifies `ps=0x50063F800` as the later
  full-viewport RGB writer. It samples only `0x53AA00000` (960x540
  RGBA16F), writes `0x514080000`, and preserves destination alpha. Its input
  is already black, so this draw replaces the real full-resolution scene RGB
  with black.
- In the clean current run `20260730-010416-corpus-gate`, eight consecutive
  post-draw readbacks selected by `ps=0x500126600` leave the live 960x540
  `0x53AA00000` image byte-identical zero after worldmap loads. The earlier
  `20260729-215508-corpus-gate` nonblack pattern is not a healthy control: that
  run intentionally exported multiplicative shader factors into the target.

Do not conflate the two pixel shaders that happen to write the same aliased
address. The packed-export and opacity-factor experiments in the section
below apply to `ps=0x5002AFC00`. The later `ps=0x500126600` program is a
different 2,317-instruction shader paired with `es=0x50011FC00`. That native
primitive draw is the already-attributed optional particle path: its inactive
record produces the guest's dummy one-vertex/one-primitive allocation and no
POS/PARAM payload. Its empty output does not prove that it was responsible for
creating the base half-resolution image.

The old next step of measuring only pre-versus-post `ps=0x500126600` was too
narrow. The full ordered AGC trace in
`20260729-071403-corpus-gate/attempt-01.log` records the complete 960x540
writer interval before `ps=0x50063F800`:

- sequences 46094 through 46121 alternate material draws
  `es=0x5002AA400 / ps=0x5002AFC00`, the `ps=0x500006E00` variant, and
  `es=0x50011FC00 / ps=0x500126600`;
- sequences 46122 through 46137 add
  `es=0x5000F6700 / ps=0x5000FD100` plus more `ps=0x500006E00` draws;
- sequences 46138 through 46142 are the last three
  `es=0x50011FC00 / ps=0x500126600` draws;
- sequence 46143 is the destructive `ps=0x50063F800` copy into the
  full-resolution scene target.

The adjacent mode-6 command is not a `0x53AA00000` operation. Sequence 46091
is followed by the same-command writer at sequence 46092 to
`0x5367F0000`. Treating it as the missing half-resolution DCC materialisation
would join two different targets.

The previously unclassified sequence 46093 is not a hidden compute producer.
The run-relative sequence probe in `20260730-022526-corpus-gate` maps that
historical gap to a three-vertex non-indexed draw with
`es=0x500650F00 / ps=0x500651900`. This is the existing colour-pyramid
downsampler: the pixel shader has one 16-byte read-only scalar-buffer binding,
samples an image, and exports an MRT value. It has no storage/global write to
the clustered A head or node buffers. The run was stopped immediately after
the exact trace fired.

`20260730-020510-corpus-gate` performs that ordered scan on current master
after the fast-clear, NGG replay, and persistent-GDS fixes. It reads the live
host image before each selected draw. Occurrences 1 through 49 cover one
complete writer interval, from the first `ps=0x5002AFC00` through the final
`ps=0x500126600` (13 `2AFC00`, 24 `006E00`, 6 `00FD100`, and 6 `126600`
draws); every one reports
`target_pre_nonblack=0`. Because every next pre-draw read is the preceding
writer's post-draw state, writers 1 through 48 each leave the target
numerically black. The retained eight post-draw reads after the final
`ps=0x500126600` in `20260730-010416-corpus-gate` close the last edge: that
writer also leaves it byte-identical zero. The scan reached
`LevelDocument Loaded: worldmap` and was stopped after the first complete
interval rather than spending another frame on duplicate probes.

This eliminates the competing “a later overlay clears a valid half-resolution
image” explanation. The target is zero before the first material draw, and no
`2AFC00`, `006E00`, `00FD100`, or `126600` draw produces color. The next
boundary is therefore inside the first material writer that direct evidence
shows should contribute, or in the missing initialization/producer before the
writer interval. Do not infer failure from hierarchical-depth input
`0x53A500000`, whose zero guest-byte probes are not a live depth-image
readback. Repeating final-tonemap, SMEM, exposure, or DCC-byte probes cannot
answer this boundary: the live tonemap has complete scalar bindings, and the
measured black is already present in its sampled host image.

## 2026-07-30: persistent GDS enables the consumer chain but does not fix the linked-list walk

The persistent-GDS fix is necessary, but a matched-resolution measurement
refutes the claim that it also removed the cost of `ps=0x5008F1400`.
`20260730-015743-corpus-gate` records 28 GPU timestamps at 1920x1080:
the mean is 123.443 ms, with a 111.681 ms minimum and 137.416 ms maximum.
The pre-fix 1920x1080 control averaged 127.806 ms. The old approximately
786 ms number came from a 3840x2160 run and is not the healthy control for
this differential. Queue depth now peaks at 214 and then falls into the low
teens, so persistent GDS repaired dispatch enablement/backlog without
repairing this pixel shader.

The retained pre-fix cap image makes the remaining failure precise. Only two
of 2,073,600 pixels, at zero-based coordinates `(1136,553)` and `(1137,553)`,
carry the 100,000-step marker. Both select head index zero, load node zero,
then read `node[0].next == 0`. The loop accepts every head/next value except
`0xFFFFFFFF`, so zero is a self-cycle rather than the empty-list terminator.
The cap identifies the last executed basic block, whose entry maps to guest
PC `0x1DEC`; it does not prove that the branch instruction itself stalls.

The exact resources are:

- A head table `0x553BA3A50`, 129,600 bytes;
- node storage `0x551BE0000`, 16 MiB.

The repaired `cs=0x5006EB500` producer writes adjacent B/C head tables
`0x553BC3490` and `0x553BE2EE0`, not A or the node buffer. The first B
address is exactly A plus 129,600 bytes. The subsequently enabled
`cs=0x500571000` and `cs=0x50059CD00` bridge shaders are also consumers:
`59CD00` binds A and nodes with `writable=0`, and the same-boundary captures
in `20260730-021241-corpus-gate` find both byte-exact zero before it. Those
two shaders write storage images, not the linked-list buffers. Same-queue
Vulkan ordering already inserts shader-write to shader-read/write barriers;
there is currently no direct evidence for a host-coherence failure.

The indirect-dispatch chain is now measured rather than inferred.
`cs=0x5006EC700` writes B with 16,136/129,604 nonzero bytes, initializes C to
the `0xFFFFFFFF` empty sentinel, and writes indirect arguments
`8160,1,1` and `1,1,1`. Those arguments launch `cs=0x500571000` at
8160x1x1 and `cs=0x50059CD00` at 1x1x1. Exact ISA/resource traces report
`global_writes=False` for both programs: neither contains a buffer/global
store or atomic; `571000` does not bind A/nodes, while `59CD00` reads A at PC
`0x1C30` and nodes at PCs `0x1C54`/`0x1FF4`. Sony's tiled-deferred sample
uses writable raw buffers plus `AtomicAdd` in its list builder and reserves
the indirect backend-lighting stages for consumption. These two Astro
dispatches are therefore neither missing nor candidate A/node producers.
The remaining approximately 123 ms pixel cost is still an upstream
initialization/build defect, not an indirect-dispatch failure.

The earlier interval-overlap audit was incomplete because it predated the
live binding and post-fence writeback trace. The retained detailed run now
identifies `cs=0x5006E8500` as a writable producer for both A and nodes. It
dispatches, but its node-pool and coalesced head-table writebacks report zero
changed bytes. No `DMA_DATA`, `WRITE_DATA`, or `RELEASE_MEM` destination
overlaps either range, and the adjacent consumer shaders remain read-only.
The open question is therefore why the real producer publishes nothing in
this state, not whether any GPU producer was submitted.

Sony's Prospero 10 tiled-deferred sample establishes the lifecycle without
guessing Astro's values. `GpuTaskClearBuffers::runTask` clears mutable raw
buffers through the `memzero_c` compute path before classification/build,
while DCC G-buffers use `clearRenderTargetCs` separately. This supports an
explicit clear/build/dependency boundary; it does not establish Astro's exact
linked-list sentinel. The next measurement is therefore the first command
that should initialize or build A/nodes, including command-processor writes,
not another B/C consumer probe and not a fabricated `0xFFFFFFFF` fill.

## 2026-07-29: fast-clear state now survives the half-resolution prepass; presentation is still black

Sony's Prospero SDK 10 closes the first postprocess contract without an AMD
table. `sce::Agc::CxCbControl::Mode` defines mode 2 as
`kEliminateFastClear`, and the metadata-compression samples materialize the
registered clear value before ordinary shader sampling. Astro's
`0x510D10000` target carries clear words
`0x00000000/0xBC003C00`, which decode as half-float
`(0, 0, 1, -1)`.

SharpEmu had two independent violations at this boundary:

1. mode 2 did not carry the clear words into the presenter, so there was no
   representation from which to materialize the fast clear;
2. Sony's helper shader then wrote only R/G, but SharpEmu recognized it as a
   fixed fullscreen clear and replaced the draw with `vkCmdClearColorImage`.
   That host command ignores the guest write mask and cleared B/A too.

`GuestRenderTarget` now carries the decoded clear words. The presenter
materializes the verified RGBA16F mode-2 value, and the fixed-clear
optimization is selected only for a full unblended RGBA overwrite with no
depth or raster rejection. Partial write masks use the translated graphics
draw. The compatibility bit is part of the graphics shader cache key, so an
earlier optimized clear cannot alias the later real shader.

The live differential is byte-exact:

- before the masked-clear fix, the relevant `cs=0x5006F7700` input was zero
  and its `0x539910000` RG16F output was NaN poison (`00FE00FE`);
- in `20260729-225253-corpus-gate`, the same input is
  `nonblack_pixels=2073600/2073600`, with repeating
  `00000000003C00BC`, and `0x539910000` becomes finite
  `00380038` (half-float `0.5, 0.5`);
- in `20260729-225851-corpus-gate`, occurrence 2 of
  `cs=0x500700A00` reads that finite RG16F image and writes
  518,400 nonblack pixels to `0x562370000`. The repaired value therefore
  survives its immediate compute consumer. Raw guest-memory descriptor probes
  that still print zero do not describe these live Vulkan images.

This is a verified first-handoff fix, not rendering acceptance. A later exact
title chain established:

- `ps=0x5006CB800` writes `0x53AA00000`;
- `cs=0x500690F00` consumes it and writes `0x53B9F0000`;
- `ps=0x500650600` samples that surface into the next full-resolution target.

The initial readbacks appeared to contain 32 and 64 nonblack half-float
pixels. Decoding the raw dumps proved every RGB value was either `+0` or
`-0`; the presenter had counted the IEEE sign byte in `-0` as colour.
`CountNonblackPixels` now masks the sign bit for every R16/R32 floating-point
format, with regression tests. Numerically, all three sources at the selected
early title composite are black. `cs=0x500690F00` only adds opaque alpha; it
does not erase colour that was present.

The clean post-fix boot
`20260729-232029-corpus-gate/attempt-01.log` reached
`LevelDocument Loaded: worldmap` without device loss. The uninstrumented
visual run `20260729-232557-corpus-gate/attempt-01.log` reached the same
milestone, and PrintWindow with `PW_RENDERFULLCONTENT` captured
`artifacts/codex-astro-worldmap-printwindow-20260729-8004-128911240.png`.
All 125,076 nominally nonblack window pixels belong to the title bar and
SharpEmu HUD. Excluding those regions leaves exactly
`0/1,225,150` nonblack guest pixels.

The remaining boundary is later than the repaired half-resolution prepass.
The source-triggered run `20260729-231255-corpus-gate` measured
`0x514080000` as numerically black through composite occurrence 363 and
reached the title scene; the synchronous probes slowed the title enough that
it did not reach worldmap. This does not contradict the separately captured
later worldmap-era `0x514080000` image with 869,977+ nonblack pixels. The next
decisive measurement is a non-perturbing, same-command source/target capture
when that later HDR image is consumed—not another early-title occurrence and
not a guest-memory byte scan.

## 2026-07-29: indexed NGG input and compressed export were two real black-output defects

The pass-through native-primitive writer of the first selected 960x540
postprocess target is now separated into transport, geometry, raster, and
fragment-output boundaries. The relevant programs are
`es=0x5002AA400` and `ps=0x5002AFC00`; the target is
`0x53AA00000` (`960x540`, `R16G16B16A16Sfloat`).

The compute/replay prologue originally loaded its synthetic index binding from
byte zero of Vulkan's aligned buffer range. The guest index address has a
discarded low-byte bias (`0x80` in this draw), recorded in the runtime scalar
spill just like decoded MUBUF accesses. Not applying it made `v5` read bytes
before the index stream and produced degenerate positions. The fix routes both
16-bit and 32-bit synthetic index paths through the normal guest-buffer bias
and unaligned-word helpers. In
`20260729-202314-corpus-gate/attempt-01.log`, the retired input probe then reads
the actual sequence `0,2,3,0,1,2,4,...`, and a later exact replay retires
nonzero vertex output. A structural test proves that replay binding 5 loads its
bias from scalar-spill descriptor 7, word 261, and that the value participates
in the stored `v5` dataflow.

That repaired geometry did not by itself color the target. Paired controls
located the next boundary:

- `20260729-202904-corpus-gate`: solid fragment output plus default raster
  state produced 93,790, then 156,795, then 207,548 nonblack target pixels.
- `20260729-203359-corpus-gate`: solid fragment output with the guest's
  original raster, depth, blend, and viewport state produced 93,801 nonblack
  pixels.
- `20260729-204532-corpus-gate`: an exact live replay produced 18 unique
  positions, 16/16 nondegenerate triangles, and 24/48 positions inside the
  clip volume. Its material alpha input ranged from about -0.755 to 0.912;
  15/48 sampled values survived the measured 0.1 threshold.

Those controls eliminate attachment residency, indirect replay, raster state,
and universally killed fragments as explanations for an all-zero target.
The original pixel shader dump in `20260729-203905-corpus-gate` has 1,054
instructions, no backedge, no unsupported instruction, all 73 reachable SMEM
sites and 38 SBuffer sites covered, 15 global bindings, and
`smem_zero_filled=0`. An EXEC-output differential in
`20260729-205109-corpus-gate` colored 76,193 and later 179,836 pixels. A
pre-export-register differential in `20260729-205652-corpus-gate` produced
nonzero target samples. The first remaining zero boundary was therefore
between the final packed material registers and compressed `EXP`.

The defect was a non-architectural packed-half shadow. The shader executes:

```text
v_cvt_pkrtz_f16_f32 v1, v2, v3
v_cvt_pkrtz_f16_f32 v0, v0, v4
s_mov_b64 exec, s[44:45]
exp mrt0 compr v1, v0
```

SharpEmu used the nearest static `VCvtPkrtzF16F32` to feed `EXP` from a
separate `vgprPackedHalf` shadow. A pack write suppressed by EXEC does not
update that shadow; restoring EXEC can make the lane live again, in which case
the architectural raw VGPR still contains the value hardware must export.
Compressed export now reads the raw VGPR at `EXP` time and applies
`UnpackHalf2x16`. A def-use regression proves the MRT store depends on `vgpr`
and not on `vgprPackedHalf`.

The post-fix differential
`20260729-211018-corpus-gate/attempt-01.log`, with the existing NGG visibility
diagnostic enabled, changed the previously exact-zero target to one nonblack
pixel. That is evidence that the packed-export seam was real; it is **not**
rendering acceptance. The unforced run ended after the earlier legitimately
empty occurrence and did not measure the later live replay. A guest-visible
PrintWindow capture or a later exact unforced target readback is still
required.

### Correction after the later exact unforced run

The later measurement refutes compressed export as the cause of the broad
black target. In `20260729-212159-corpus-gate`, eight successive exact
960x540 readbacks after worldmap load remained byte-identical zero, and
PrintWindow remained black outside the HUD. The one-pixel forced-visibility
change was therefore a narrow differential, not the missing scene.

Three paired probes then moved the first zero upstream without forcing guest
state:

- `20260729-213426-corpus-gate` exported architectural `v4..v7` and produced
  31,364 through 117,530 nonblack pixels. This proved coverage and nonzero
  material/interpolation registers, but did **not** prove `v4` itself was
  nonzero.
- `20260729-214120-corpus-gate` recorded pack-time EXEC in red and the two
  packed RGB components in green/blue. Pack-time EXEC covered 31,431 through
  85,801 pixels while both packed RGB values were exactly zero. The mask was
  not lost.
- `20260729-214713-corpus-gate` saved the actual inputs at the two
  `VCvtPkrtzF16F32` instructions. Every pack-active pixel had zero RGB
  magnitude and zero alpha before packing. `PackHalf2x16` did not erase the
  material result; the material result was already zero.
- `20260729-215508-corpus-gate` saved the multiplicative sources at PCs
  `0x1278` and `0x15B0`. For pack-active pixels, both sources of the first
  opacity product were zero. The final factor `1 - s24` was also exactly zero,
  hence `s24=1`. The first nonzero-to-zero boundary is now upstream of
  `0x1278`, in the pixel shader's interpolated/material inputs, not compressed
  export.

The raw factor-probe pixel pattern makes this byte-exact rather than visual:
red is half-float 1 for pack-active lanes, while the final factor's blue
channel and the first product's alpha channel are zero. The old statement
that the `v8/v9/v10/v4` probe captured “final pre-pack registers” was wrong:
those RGB accumulators are multiplied again at PCs `0x15A4..0x15B0`.

Sony's EXP mapping is independently confirmed: in compressed mode EN[1:0]
selects the two halves of VSRC0 and EN[3:2] selects VSRC1. SharpEmu's
`source[component >> 1]`, `half[component & 1]` mapping is correct. The current
`TruncateFloat32ForPack` remains a real general RTZ edge-case defect for
half-subnormals and finite overflow, but ordinary half-normal material values
are handled correctly and it cannot explain this all-zero pre-pack input.

PR #689 is not a substitute for either fix. Its canonical-view issue is valid
in general, but this target is render-target-created with an identity view;
its uninitialized-image clear would only define unwritten content as black;
and its detile flushes target an upstream path absent from current master.
None changes the proven zero boundary inside this executed material export.

## 2026-07-29: Astro's native-primitive ABI is recovered; V620 rules out direct mesh replay

The dumped IR for Astro's amplifying shader
`es=0x50011FC00` narrows the required launch ABI substantially. This is direct
program evidence, not a generic AMD ABI guess:

- PC `0x0010` extracts `s3[27:24]` as the wave index within the subgroup.
- PC `0x0020` forms `groupTid = waveIndex * 64 + laneId`.
- PC `0x0028` bounds input work with the byte at `s3[15:8]`, the input
  primitive count for this point-list launch.
- PC `0x0054` consumes `v5` as the input vertex index.
- The allocation path computes `outputPrimitiveCount = survivorCount * 8` and
  `outputVertexCount = survivorCount * 10`, then packs those counts into `m0`.
- PC `0x3CA4` exports target 20 from `v40`; POS0 and eleven PARAM exports
  follow.

For the submitted state, one full subgroup therefore means 19 input points,
152 output triangles, and at most 190 output vertices. The 120-point draw needs
seven subgroups; the final subgroup has six input points. Three wave64 waves
give a 192-invocation workgroup. The capability file used for the host check is
`VP_VULKANINFO_AMD_Radeon_Pro_V620_MxGPU_2_0_317.json`.

The first host experiment lowered that contract directly to `MeshEXT`. The
2026-07-29 corpus run at
`artifacts/game-runs/astro/20260729-141406-corpus-gate/attempt-01.log` proves
that AGC selected the path:

```text
agc.ngg_mesh es=0x50011FC00 ... max_vertices=190 max_primitives=152 local_size=192 tasks=7
```

It reached LOGO at 62.610 s, TITLE at 181.532 s, and worldmap loaded at
258.516 s without device loss. It then blocked during the first graphics
pipeline creation after emitting the roughly 1.1 MiB mesh module. This is not a
guest rendering result.

The failure is explained by a host limit that the original count-only gate
missed. The shader exports POS0 plus eleven PARAM `vec4`s: 48 scalar
per-vertex components. The V620 reports
`maxMeshOutputMemorySize=32768`, `meshOutputPerVertexGranularity=256`, and
`maxMeshOutputComponents=127`. Applying Vulkan's mesh-output storage formula
to 190 vertices rounds the vertex count to 256 and requires
`256 * 48 * 4 = 49,152` bytes before any per-primitive attributes. That exceeds
the 32 KiB limit. Output vertex/primitive counts and workgroup invocations all
fit, but the complete interface does not. Direct mesh replay is therefore
invalid on this host unless the interface is redesigned or packed.

The dirty working tree now contains two pieces of follow-up infrastructure:

- it refuses anything other than a guest wave64 on a host wave64 subgroup;
- the backend publishes the actual mesh output-memory and count limits and
  rejects this configuration before translation; and
- compute capture can record POS0, PARAM exports, target-20 connectivity, and
  the raw `m0` allocation request plus decoded output counts.

The grounded replacement is one compute workgroup per guest subgroup followed
by indexed-indirect replay. For Astro that means seven workgroups of 192
invocations. Full groups seed `s3` with 19 input vertices and 19 input
primitives; the tail seeds six and six. The three waves carry wave indices
zero through two in `s3[27:24]`, with the workgroup wave count in
`s3[31:28]`. `v5` is the input vertex index and `v8` is zero. The shader's
`GS_ALLOC_REQ` supplies runtime output counts in `m0`; those counts, not the
input counts, must populate seven `VkDrawIndexedIndirectCommand` records.
Target 20 supplies the three packed 10-bit indices and each is rebased by
`groupId * 192`.

**Verification status:** the direct mesh experiment is refuted for the measured
V620. Compute capture plus indexed-indirect replay is now wired end to end and
passes a zero-warning Release build and the full 2,222-test suite. The
2026-07-29 corpus run at
`artifacts/game-runs/astro/20260729-145526-corpus-gate/attempt-01.log` reached
LOGO at 62.016 s, TITLE at 178.906 s, and worldmap loaded at 256.375 s without
device loss. It did not produce a nonblack guest frame.

An opt-in fence-retirement probe (`SHARPEMU_TRACE_NGG_REPLAY=1`) reads the
persistently mapped replay buffers only after `vkGetFenceStatus` returns
success and before the buffers return to the host pool. All eight sampled
submissions agreed:

```text
vertices    64512 dwords, 0 nonzero
indices      4032 dwords, 18 nonzero, first nonzero at dword 576
allocations    63 dwords, 21 nonzero
indirect       35 dwords, 20 nonzero
```

Each of the seven allocation groups contains one `0x1001,1,1` record and each
indirect command requests one three-index draw at first-index offsets
`0, 576, 1152, 1728, 2304, 2880, 3456`. The 18 nonzero index dwords are
explained entirely by rebasing a zero target-20 payload for groups one through
six: each emitted triangle is degenerate at its group base. The capture
compute shader therefore executes, reaches `GS_ALLOC_REQ`, exports target 20,
retires, and feeds validly shaped indirect commands, but the guest program
produces no POS/PARAM data and zero local connectivity. This rules out the
Vulkan replay barriers, indirect-command transport, rasterizer, and DCC
materialisation as the *first* failure for this draw. The next question is
narrowly why the translated native-primitive program sees or computes empty
vertex output—initial scalar/resource bindings, launch ABI, EXEC/LDS behavior,
or an unsupported instruction—not whether capture ran.

The last pixel evidence remains a real full-resolution HDR target followed by
zero half-resolution DCC-flagged inputs. The final-group count rule is exact for
this Astro point-list launch and must not be generalized to other topologies
without new evidence.

The follow-up corpus run at
`artifacts/game-runs/astro/20260729-151040-corpus-gate/attempt-01.log` moved the
first failure upstream again. The decoded shader sequence is:

```text
pc 0x0034  s_load_dwordx4 s[4:7], s[36:37], 16
pc 0x0048  s_buffer_load_dword s106, s[4:7], 16
pc 0x0054  v_add_i32 v0, s106, v5
pc 0x0058  buffer_load_dword v1, v0, s[16:19], offset:28, idxen
pc 0x0064  v_cmp_lt_f32 0.0, v1
```

The shader later derives its real output counts from that compare. When no
lane survives, PCs `0x3BFC`/`0x3C00` clamp both counts to one, producing the
observed dummy `m0=0x1001`; PC `0x3CC4` then skips all PARAM exports.

The fenced PC-`0x58` probe captured all 19 input lanes in group zero. The
descriptor is based at `0x553F41DD0`, `s106=0x291C`, `v5=0..18`, the stride is
64 bytes, and the effective host-buffer offsets are `0xA47EC`,
`0xA482C`, …, `0xA4C6C`. Every load returns exactly zero. The bound 16 MiB
buffer is not wholly empty—its first 4 KiB contains 512 nonzero bytes—so this
is not an unbound descriptor or a compute-dispatch failure. The first
demonstrated empty datum is now the structured record at guest
`0x553F41DD0 + 0xA471C` (the descriptor-relative offset before the host
alignment bias). The next attribution task is to identify the GPU/DMA producer
of that record, or prove that `s106=0x291C` itself is the wrong launch/user-data
value.

One later replay defect was previously suspected here: POS/PARAM and target-20
destinations appeared to require a compact `v39` index instead of the physical
compute invocation ID. Exact full-program reconstruction now refutes that
claim for this shader. `v39` is reconstructed from the wave index and lane and
is identical to physical local invocation `v45`; active primitive and vertex
lanes are dense prefixes. Changing the capture address would therefore be an
identity transformation, not a fix.

### The empty record is written by a particle compute dispatch

The targeted post-fence run
`artifacts/game-runs/astro/20260729-152201-corpus-gate/attempt-01.log`
attributes the exact record, rather than merely finding another overlapping
allocation. Every retired writable binding covering guest address
`0x553FE64EC` belongs to:

```text
SharpEmu compute cs=0x0000000555F4F500 192x1x1
```

Its binding is based at `0x553F41DD0`, so the record begins at byte offset
`0xA471C`. The 64-byte window around that offset is all zero after every
successful fence sampled. This rules out a missing fence, stale host mapping,
or a later alias replacing nonzero bytes between this producer and
`es=0x50011FC00`: the translated producer itself leaves the consumed record
zero.

That attribution does **not** yet prove a rendering bug. Existing decoder
coverage identifies `cs=0x555F4F500` as Astro's particle-emitter compute
shader. An inactive particle slot is allowed to remain zero, and the native
primitive shader's explicit one-primitive fallback is consistent with an
optional empty particle draw. This draw must not replace the independently
measured postprocess question: why the live full-resolution scene becomes an
empty half-resolution input.

The resource and shader dump run
`artifacts/game-runs/astro/20260729-153041-corpus-gate/attempt-01.log` narrows
the producer without inventing an output:

- the shader has 4,192 decoded instructions and eight backward branches;
- all 56 reachable scalar-memory sites are covered, including all four
  `s_buffer_load` sites, and `smem_zero_filled=0`;
- the dispatch compiles and submits with 15 global bindings;
- its writable particle buffers remain zero, while several read bindings and
  the initial scalar spill contain nonzero bytes.

Therefore unbound-SMEM recovery is not the explanation for this particular
zero record. The eight backedges are unconditional `s_branch` instructions,
so the listing alone cannot establish their trip counts. Commit `42a17e5`
restores `SHARPEMU_SHADER_STEP_PROBE`, but its runtime sink is intentionally
pixel-only; for compute it reports `sink=none`. A compute cap hit remains
**unmeasured**, not absent.

The next postprocess measurement should return to the first draw/dispatch that
reads the known nonblack `0x514080000` scene and writes the selected
half-resolution surface. Capture that source and destination at the same
ordered command boundary. Keep the particle record as a separate correctness
lead until a nonempty slot or a healthy control proves it should contain live
data.

## 2026-07-29: visible output, not level progress, is the acceptance boundary

Run `artifacts/game-runs/astro/20260729-161038-corpus-gate/attempt-01.log`
selected draw occurrence 339 for `ps=0x5002AFC00` and read back its live
`0x53AA00000` target immediately after the draw. The raw image is
`artifacts/codex-astro-first-half-target-occ339-20260729/0002-0x000000053AA00000-960x540-R16G16B16A16Sfloat.rgba`.
It is exactly zero in all 4,147,200 bytes:

```
size=960x540
nonzero_bytes=0/4147200
nonblack_pixels=0/518400
center=0000000000000000
sample_unique=1
```

The selected writer really executes: its live counter advances through 25,
50, ... 325 before the readback and continues to 350 afterwards. This rules
out "the selected writer never executes", but it does **not** yet distinguish
no covered fragments from a pixel shader that writes zero. It also does not
establish a full-resolution-to-half-resolution dependency: the readback is
target-only. The retained trace maps the ordinal to the first unusual
worldmap-era group of this shader, but that mapping remains a differential
rather than a value to force into game state.

Two controlled occurrence-339 runs now separate fragment output from coverage.
Both replaced only fragment shaders targeting `0x53AA00000` with opaque
magenta:

- `20260729-163048-corpus-gate`, normal depth state, produced exactly one
  nonblack pixel (`nonzero_bytes=3`, hash `0x1F269B4E75464361`);
- `20260729-164102-corpus-gate`, with depth disabled for the same target,
  produced the same one pixel and the same hash.

The raw 960x540 images are under
`artifacts/codex-astro-half-solid-occ339-20260729/` and
`artifacts/codex-astro-half-solid-nodepth-occ339-20260729/`.
Therefore the original pixel shader is not the only reason the surface stays
black, and depth testing is not what reduces this selected draw to almost no
coverage. The immediate frontier is its indexed vertex/raster path:
`es=0x5002AA400`, 48 indexed vertices, triangle-list topology. A forced
fullscreen-vertex control can confirm the attachment path, but must remain
labelled as a diagnostic rather than a rendering fix.

The translator already reports the exact unsupported semantic for that export
shader:

```
shader=0x5002AA400 pc=0x0030
error=ngg-prim-export-dropped target=20 src=v0 en=0x1 done=1
detail=... the draw's index buffer is used instead, which is correct only for
       a pass-through primitive shader
```

This is not a generic vertex shader. It is another native primitive program,
and SharpEmu currently discards Sony's target-20 connectivity and runs the
remaining program through the ordinary indexed-vertex fallback. The one-pixel
solid result makes that fallback the live geometry suspect, but does not by
itself prove that this particular object should cover more than one pixel:
target 20 comes from `v0`, which may describe pass-through connectivity. A
native-output capture or healthy control is still required before assigning
the one-pixel extent to NGG rather than the game's transform. If it is not
pass-through, the correct repair is to execute the program with its measured
native GS wave ABI and consume its output vertices/connectivity, extending the
compute/indirect replay rather than forcing a fullscreen triangle or invented
color.

Sony's tiled-deferred sample also exposes a separate resource-model defect.
Its sampled depth texture is translated from the same depth-render-target data
address and synchronized with
`kFlushCompressedDepthBufferForTexture`. SharpEmu instead lets
`cs=0x5006C6A00` write an R32 `GuestImageResource` at `0x53A500000`, while
`TryResolveGuestDepthTexture` gives sampling priority to a distinct Vulkan
depth image at the same guest address; no copy or coherency operation joins
them. This defect remains live, but the identical depth-on/depth-off solid
result means it is not the cause of occurrence 339's one-pixel coverage.
Likewise, retained `vk.depth_init` traces show compare `LessOrEqual`,
write-disabled first use initialized neutrally to 1, so the stale 1x1
descriptor did not initialize this surface to rejecting clear depth 0.

An apparent `960x540` guest / `1920x1080` Vulkan mismatch in the older detailed
trace is not an aliasing defect. That process used render scale 2, and
`GetOrCreateGuestImage` therefore correctly created a 2x physical image while
retaining `960x540` as its logical descriptor size.

The startup video supplies an independent, earlier visible-output failure.
Astro opens `data/prein/video/ps_studio_armadillo.mp4`, a 3840x2160,
59.94-fps, 8,508-ms PlayStation Studios video. Native decode succeeds:
`sceAvPlayerGetVideoDataEx` returns frames from timestamp 0 through 8,275 ms.
However, the guest `allocateTexture` callback returns one 12,441,600-byte
buffer and then null on index 1:

```
[AVPLAYER][INFO] texture_buffer index=0 data=0x00000003354432F0 size=12441600
[AVPLAYER][ERROR] Guest texture allocation failed index=1 ... returned null
[AVPLAYER][WARN] Guest texture allocator unavailable; using generic HLE memory.
```

All decoded NV12 frames are consequently redirected to generic HLE ring
addresses (`0x600001060000`, `0x600001C3D800`, and `0x60000281B000`) instead
of preserving the mapped graphics-allocation contract described by Sony's
`sceavplayer.h` and `api_avplayer/common/source/av_draw.cpp`. Master has no
production direct-present path for those fallback buffers. Reaching
`Level has started: ps_logo`, title, or worldmap therefore proves only guest
progress; it is not evidence that the intro or any menu pixel was displayed.
The next verification for the video path must be a PrintWindow guest capture
while decoded frames are active.

That verification now succeeds with the opt-in direct path added in the same
session. With `SHARPEMU_AVPLAYER_PRESENT=1`, SharpEmu converts the decoded
SDK-linear NV12 frame to BGRA8 and gives the owning AvPlayer handle presentation
priority until pause, stop, close, a new source, or non-looping EOF. The default
unset behavior is unchanged. Corpus-gate run
`artifacts/game-runs/astro/20260729-162633-corpus-gate/attempt-01.log` reached
decoded timestamps 1,602 through 5,155 ms, and PrintWindow with
`PW_RENDERFULLCONTENT` captured a visibly rendered PlayStation Studios logo:

`artifacts/astro-avplayer-direct-present-late-20260729-1628-18404-34080904.png`

The capture is 1550x882 with `1,359,372/1,367,100` nonblack window pixels. More
importantly, visual inspection shows the logo in the guest image outside the
SharpEmu HUD. This is the first verified nonblack guest content for Astro; it
fixes intro presentation, not the independently black AGC-rendered title and
worldmap chain.

## 2026-07-29: SDK 10 closes DCC and wave-size guesses; amplified NGG remains

This update uses Sony's freshly extracted Prospero SDK 10 under
`games/prospero-sdk-10.00/` as the register and rendering contract. It
supersedes the AMD GFX10.3 field guesses previously used by
`NggPrimitiveShader.cs`.

### Facts established by Sony's SDK

- `agc/registerstructs.h::CxCbControl::Mode` defines mode `0x60` as
  `kDccDecompress`. It decompresses DCC and implicitly performs
  `kEliminateFastClear` and `kFmaskDecompress`.
- `CxRenderTarget::DataWriteOnDccClearToRegister` explains why raw color bytes
  may remain zero: with the default disabled state, a clear may update only DCC
  metadata. Enabling it makes clear-to-register also write pixel data for
  texture-pipeline compatibility.
- `agc/core/sync.h` requires color-buffer data and metadata flush/invalidate
  before compressed render-target texture reads, unless the surface was
  created texture-compatible.
- Sony's `agc_metadata_compression/dcc_tests.cpp::dccRtTest` renders a
  color-buffer-only DCC target, demonstrates that its raw backing bytes are not
  an uncompressed image, calls `Toolkit::decompressDccSurface`, and only then
  samples the uncompressed pixels. A mode-6 operation is therefore a real
  materialisation operation when guest DCC bytes are the authoritative copy.
- `CxVgtShaderStagesEn` defines exactly five public fields: HS wave size bit
  `0x00200000`, GS wave size bit `0x00400000`, `GS_EN=0x20`,
  `HS_EN=0x4`, and NGG passthrough `0x02000000`. HS and GS default to wave64.
  Do not import the unrelated AMD GFX10.3 `PRIMGEN_EN`, `VS_EN`, or
  `GS_FAST_LAUNCH` layout.
- Sony's native primitive samples pack `S_NGG_OUTPUT_PRIMITIVE` as three
  **10-bit subgroup-relative vertex indices**:
  `v0 | (v1 << 10) | (v2 << 20)`. Output vertex and primitive counts are
  separate system values.

### What the Astro run proves

At the `0x53AA00000` mode-6 boundary SharpEmu reports
`host=PreserveHostImage location=active action=preserve`. The target is an
initialized GPU-authored expanded Vulkan image, not guest DCC bytes being read
as linear pixels. A no-op mode 6 is correct for that representation. The
generic `NeedsDecode`/`GuestMemoryBackedImage` path remains incomplete, but
mode-6 dropping is **not supported as the cause of this particular black
surface**.

The important native primitive shader is `es=0x50011FC00`. Its submitted state
is:

```
VGT_SHADER_STAGES_EN       = 0x00002030  -> GS_EN=1, GS wave64
VGT_PRIMITIVE_TYPE         = 0x1         -> PointList
VGT_GS_OUT_PRIM_TYPE       = 0x2         -> Triangles
GE_CNTL                    = 0x2613      -> 19 input vertices, 19 primitives
GE_MAX_OUTPUT_PER_SUBGROUP = 0xBE        -> 190 output vertices
VGT_GS_MAX_VERT_OUT        = 10
```

The shader exports target 20 at PC `0x3CA4` from `v40`, not the forwarded
connectivity value `v0`. SharpEmu drops that export and submits the original
120 points through a plain Vulkan vertex pipeline. Sony's registers and sample
contract therefore prove that the intended triangles are removed. A correct
implementation must execute the native primitive shader with wave/LDS
semantics, retain the `GS_ALLOC_REQ` output counts, route POS/PARAM exports by
output-vertex lane, decode target 20's three 10-bit indices, and draw the
declared output topology. A mesh-style backend or compute prepass plus indirect
indexed draw is required; silently treating it as an ordinary vertex shader is
not a valid fallback.

Commits `1299e9b` and `6b89866` partition explicit guest wave32 correctly on a
host wave64 and thread Sony's real GS width through compilation and shader-cache
identity. The latter changes `0x50011FC00` from the old one-lane fallback to an
exact guest wave64 on the measured host. Release builds with zero warnings and
the full suite passes 2,203 tests.

The corpus run
`artifacts/game-runs/astro/20260729-124336-corpus-gate/attempt-01.log` reached
LOGO, TITLE and `LevelDocument Loaded: worldmap`, with no device loss. It is a
negative rendering result:

```
vk.swapchain_image size=1920x1055 format=B8G8R8A8Unorm
nonzero_bytes=2025600/8102400
nonblack_pixels=0/2025600
```

An independent byte scan confirms RGB is zero in every pixel and alpha is 255
in every pixel. Exact wave64 removed a proven execution defect but did not make
the frame nonblack.

The earlier description of `0x53A500000` as a color/G-buffer boundary was
wrong. At the relevant occurrence `cs=0x5006C6A00` reads the 1920x1080
R32-float depth surface `0x513560000`, reduces four samples with
`VMaxF32`/`VMax3F32` through LDS, and writes the 960x540 R32-float level
`0x53A500000`; the next dispatch writes the 480x270 level
`0x53AC40000`. This is a hierarchical-depth max reduction. Sony's
`tutorial_super_resolution` hierarchical-depth shaders use the same
gather/max/write construction, and the runtime DB state independently binds
`0x513560000` as 32-bit float depth with HTILE.

The zero guest-memory probes for those addresses are not host-image evidence:
the live Vulkan depth image is authoritative and is resolved through
`vk.depth_texture_alias`. The ordered occurrence-1719 readback documented at
the top of this file now closes that audit: the live D32F input is nonzero and
the same-command R32F output contains 518,400 nonzero float pixels.

The first directly evidenced rendering defect remains the amplifying native
primitive shader `es=0x50011FC00`: Sony's state requests generated triangles,
but SharpEmu drops target-20 connectivity and submits the original 120 points.
That is the supported implementation direction; DCC or depth zero-fill is not.

### Recovered NGG implementation foundation

Commit `0c885d5` restores the previously orphaned NGG compute-capture compiler
path in the current split shader-compiler projects. It can seed the configured
vertex-index VGPR from a linear or 16/32-bit indexed invocation, preserve the
ordinary compute module byte-for-byte when disabled, and capture POS0 plus
selected PARAM exports to an SSBO. Seven focused tests cover that contract; the
full suite now passes 2,210 tests.

This committed foundation is not by itself the rendering fix. It deliberately
does not invent semantics for EXP target 20. Newer, still-uncommitted
experiments are described at the top of this document; their presence does not
supersede the committed-state result until runtime verification succeeds. The
experimental Vulkan host seam established one useful fact on the measured AMD
Radeon Pro V620: `VK_EXT_mesh_shader` is
advertised and can be enabled without regressing the corpus milestones. The
2026-07-29 corpus run
`artifacts/game-runs/astro/20260729-133021-corpus-gate/attempt-01.log` reached
LOGO at 59.359 s, TITLE at 177.203 s, and worldmap loaded at 255.172 s, with no
device loss. Capture was disabled for that run, so it provides no new pixel
result. The later direct-mesh run exercised the path but exceeded the host's
mesh output-memory limit. Compute capture and indexed replay are now the bounded
candidate.

Measured on the Azure V620 host, 2026-07-26. **This supersedes the older notes**, which said Astro
halts at the engine assert `SoundManager.cpp:306 defaultBusses.size() == 1`.

## The SoundManager wall is gone

It no longer fires. A 180 s run with no assert-skip switch:

```
result       : alive at 180s
AVs          : 0      fatal        : 0      guest errors : 0
SoundManager : 0      defaultBusses: 0
presents     : 3      swapchain    : 3840x2160
```

The entire audio investigation recorded in the old notes - the never-populated `singleton+0x2660`
input vector, the zero-iteration build loop at `0x800DC0500`, the never-executed `&defaultBusses`
append at `0x800DC0B20` - describes a state the title no longer reaches. Something in the
2026-07-26 fixes (cross-region guest writes, the time converters, the timezone scan, memcpy routing)
cleared it. **Do not resume that hunt without first re-measuring.**

Astro now gets through sound init, loads UI/worldmap assets (`data/prein/ui/odx/...`,
`data/prein/levels/ui_pause_next/anim`), and stalls later.

## FFmpeg is a runtime dependency, and a missing one looks like a guest bug

The next wall was:

```
Guest engine assert (int 0x41, non-fatal, continuing):
  VideoPlayer: mp4 initialization faild [data/prein/video/ps_studio_armadillo.mp4]
  ASSERT: D:\asobi\6.0\source\engine\app\Module\Media\VideoPlayerOrbis.cpp:278
```

That reads like a title or dump problem. It is neither. The file is present and valid - 32,592,645
bytes, proper `ftypmp42`/`moov`, and none of the 166 files in `data/prein/video/` is truncated.

`AvPlayerExports.AddSource` (`src/SharpEmu.Libs/AvPlayer/AvPlayerExports.cs:696`) resolves the guest
path and then calls `ProbeVideo` (`:1087`), which **shells out to `ffprobe.exe`** - it does not use
the FFmpeg DLLs. `FindFfmpeg` (`:1186`) looks, in order, at:

1. `SHARPEMU_FFMPEG_PATH`
2. `AppContext.BaseDirectory\ffmpeg.exe` and `AppContext.BaseDirectory\ffmpeg\ffmpeg.exe`
3. `PATH`

and `GetFfprobePath` (`:1240`) takes `ffprobe.exe` as a **sibling of wherever `ffmpeg.exe` was
found**. On a freshly provisioned machine none of those exist, `ProbeVideo` returns false, and the
guest is told the source could not be added - which it reports as its own mp4 assert.

**Install FFmpeg 7.1 shared** (the shipped `FFmpeg.AutoGen.dll` is 7.1.1, so `avcodec-61` /
`avformat-61` / `avutil-59`; an 8.x build with `avcodec-63` will not bind):

```
https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip
copy bin\*.exe and bin\*.dll next to SharpEmu.exe
```

Verify directly before blaming the emulator:

```
ffprobe -v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate,duration `
        -of default=noprint_wrappers=1 <path to mp4>
  -> width=3840 height=2160 avg_frame_rate=60000/1001 duration=8.508500
```

With it installed the assert disappears and the player runs end to end:

```
[AVPLAYER][INFO] source guest='data/prein/video/ps_studio_armadillo.mp4' host='...'
[AVPLAYER][INFO] set_av_sync_mode handle=... mode=1
[AVPLAYER][INFO] start handle=...
[AVPLAYER][INFO] decoder_started pid=17900 source='...'
[AVPLAYER][INFO] texture_buffer index=0 data=0x334B0BAA0 size=12441600     <- 3840x2160 NV12
```

`mp4 initialization faild` goes from 1 to **0**, and the title stays alive with 0 access violations.

Note this is separate from the Bink2 path: `Bink2MovieBridge` uses FFmpeg through
`FfmpegNativeBinkFrameSource`, and upstream sharpemu/sharpemu carries the CMake plumbing for a
native bridge (`f704586`, `912883d`, `4191a9e`). The AvPlayer MP4 path used here is the
shell-out-to-ffprobe one and needs the executables regardless.

## Benign, do not chase

- 485-501 `[LOADER][IO-FAIL]` lines per run are existence probes for variant assets that are simply
  not in the retail dump - `nx_ui_worldmap_bot_icon~~364.odx` through `~~371.odx`, `/app0/Transition.xml`,
  `data/prein/levels/ui_pause_next/anim`. The engine probes a range and takes what exists.
- The `%ASOBI_ROOT%` dev-root misses (`data/common/sound/config.xml`, `sound_request_pairs.xml`,
  `physics_config.xml`) are a dev-path fallback that correctly misses on a retail dump. The real
  sound data is `data/prein/sound/`.

## KytyPS5 is not an oracle for this title

It cannot boot Astro Bot at all - it dies inside the first guest `malloc` and exits 321 at about
4 s. Use it as a reference for Superliminal; for Astro there is nothing to compare against.

## sceAudioOut2PortCreate charged OBJECT ports against maxPorts

This was the real wall, and it was silent. `SceAudioOut2ContextParam` carries **two** pools:

```
+0x00 maxPorts             Astro passes 0x14   (20)
+0x04 maxObjectPorts       Astro passes 0x200  (512)
+0x08 guaranteeObjectPorts
+0x0C queueDepth
+0x10 numGrains
+0x14 flags                the object-pool branch only accepts 1 or 2
```

`ContextMemorySize` already modelled the split, and the guest's own numbers prove the firmware
does too: Astro asks for `memorySize=0x78E9C`, and `ContextMemorySize(20, 512)` returns exactly
495,260 = 0x78E9C, byte for byte. The admission test in `AudioOut2PortCreate` did not - it counted
every port for the context against `MaxPorts`. Astro creates MAIN + BGM + 18 OBJECT ports, hits 20,
and from then on **every** create returned `PORT_FULL (0x80268012)` forever:

```
PortCreate calls  11,856      errors 11,836      successes 20
SceSndzAudioOutMain   11,284,026 imports, retrying
SceSndzRenderThread          9 imports, blocked forever
```

Kyty enforces no per-context maxPorts at all (`libAudio2.cpp:557-578`), which is why the shape was
never questioned.

The fix splits admission the way the allocation was already split: ports with `PortType & 0x100`
count against `min(maxObjectPorts, 0x80)`, the rest against `maxPorts`. The object table is 128
entries wide regardless of what the guest asks for - the same `Math.Min(maxObjectPorts, 0x80)`
`ContextMemorySize` uses. Measured after:

```
PortCreate calls      23      PORT_FULL 0      successes 23
context_create param=... size=0x78E9C -> handle=2
port_create type=0x0 -> 0x280000001     MAIN
port_create type=0x1 -> 0x280000002     BGM
port_create type=0x100 -> 0x280000003.. OBJECT, no longer refused
```

`context_push` / `context_advance` / `context_get_queue_level` now cycle every frame, where the
render thread used to be parked forever. **The title now advances through levels**:

```
PLAY: [0:18] StartLevel ps_logo : Heap Used 852.888MB
GAME: Level has started: ps_logo
LevelDocument Loaded: title_controller_ship [title]
PLAY: [2:34] StartLevel title : Heap Used 835.454MB
```

Untraced it gets further still - past the title into the **worldmap**, which is the first thing
that is not a logo or a menu:

```
PLAY: [2:26] StartLevel title : Heap Used 840.199MB
GAME: Level has started: title_controller_ship
PLAY: Playing: , Continue: worldmap
LevelDocument Loaded: worldmap [worldmap]
```

That run: 474 draws, 0 AVs, 0 fatals, 0 asserts, 0 guest errors.

A context that creates object ports must now declare them. That is a real behaviour change: a
context with `maxObjectPorts=0` refuses object ports instead of quietly borrowing from `maxPorts`.
The refusal is traced (`SHARPEMU_LOG_AUDIO=1`) with used/budget/both maxima, so a starved pool is
diagnosable instead of silent - the 11,836-call storm above produced no log line at all.

## Where to look next

- **The exit is a lost Vulkan device, and it is now the wall.** It looked like a silent quit - 0
  AVs, 0 fatals, 0 asserts, 0 guest errors, no WER record, no guest exit syscall. It is none of
  those. The process exits *cleanly*, and the reason is three lines up from the end:

  ```
  [LOADER][ERROR] Vulkan device lost; dropping subsequent guest GPU work.
    work=offscreen vs=0x00000005008F0900 mrt=2 textures=41 vertices=4164 queue=dcb.graphics submission
  [LOADER][WARN]  Vulkan VideoOut window closing; requested=False deviceLost=True
  [DEBUG]         PROCESS EXIT code=0 managedThread=0x1
  ```

  A single offscreen pass - vertex shader `0x5008F0900`, 2 MRTs, 41 textures, 4164 vertices - hangs
  the GPU. `VulkanVideoPresenter`'s `Closing` handler then calls
  `VideoOutExports.NotifyPresentationWindowClosed()`, which calls `RequestHostShutdown(
  "videoout-window-closed")` (`src/SharpEmu.Libs/VideoOut/VideoOutExports.cs:148-155`), and the
  emulator tears itself down in an orderly fashion with exit code 0. **The clean exit code is the
  host being polite about a GPU hang, and it hid the failure completely.** Two diagnostics settle
  this instantly on any future run: the `Vulkan device lost` line, and whether
  `[DEBUG] PROCESS EXIT` appears at all - it fires on a managed shutdown and does *not* fire on a
  hard abort, which is how the traced and untraced runs are told apart.

  Next step is that shader/draw, not the guest. `docs/ps5-shader-isa-audit.md` lists 33 verified
  translator divergences against Sony's Shader Core ISA docs; a mistranslated loop bound or an
  out-of-range VOP3 in `0x5008F0900` would produce exactly this.

- Tracing perturbs the run measurably - `SHARPEMU_LOG_AUDIO=1` (27 MB of trace) reaches the device
  loss at 180 s, untraced at 304 s and a strictly later point in the level sequence. Measure
  untraced or the instrument changes the answer.

- **The device loss is INTERMITTENT, not deterministic.** Two back-to-back attempts under the
  milestone harness (`scripts/game-test.py`, run manifest
  `artifacts/game-runs/astro/20260727-015323-objport-fix/run.json`):

  | | LOGO | TITLE | WORLDMAP | DEVICELOST | ran for |
  |---|---|---|---|---|---|
  | attempt 1 | t+100.4 s | t+194.3 s | t+272.5 s | **never** | 363.9 s |
  | attempt 2 | t+56.7 s | t+144.7 s | t+223.2 s | t+301.7 s | 314.8 s |

  So worldmap is reproducible n=2, and one attempt ran a full minute past it with no device loss at
  all. Do not treat a single clean run as proof the GPU hang is fixed, and do not treat a single
  lost device as proof it always happens. Note also how much the timings move between attempts
  (LOGO 100 s vs 57 s) - wall-clock thresholds are not a usable gate here, milestones are.

### Reproducing this

```
py -3 scripts/game-test.py test --game <astro eboot.bin> --game-label astro --tag <tag> \
  --binary artifacts/bin/Release/net10.0/win-x64/SharpEmu.exe --build never \
  --timeout 360 --no-screenshot \
  --env SHARPEMU_IGNORE_STACK_CHK=1 --env SHARPEMU_GPU_WAIT_MODE=force \
  --env SHARPEMU_PENDING_GUEST_WORK_ITEMS=8192 --env SHARPEMU_NP_FAKE_SIGNED_IN=1 \
  --env SHARPEMU_HLE_MEMCPY=0 \
  --milestone LOGO="Level has started: ps_logo" \
  --milestone TITLE="StartLevel title" \
  --milestone WORLDMAP="LevelDocument Loaded: worldmap" \
  --milestone DEVICELOST="Vulkan device lost"
```

Pass `--no-screenshot`. Window capture yields 0 frames from ~80 attempts in a non-interactive
session (`MainWindowHandle=0`), and without it the run is reported as
`required-screenshot-missing` even when every milestone hit. Put FFmpeg on `PATH` first
(`artifacts/bin/Release/net10.0/win-x64` already has it) or the harness refuses to start.
- **Audio still does not reach the host mixer.** `sceAudioOut2PortSetAttributes` captures the
  guest's PCM pointer into a field nothing reads, and `.szd` payload reads are still 0. The gate is
  open and the pipeline cycles, but no samples are being consumed.
- Benign: 34,236 `8XTArSPyWHk` warnings with `rdi=0xFFFFFFFFFFFFFFFF` are `PortSetAttributes` on the
  `PORT_HANDLE_INVALID` sentinel - the guest walks unused voice slots and ignores the error. Guest
  error count stays 0.

## The main menu is two draws (2026-07-27)

Everything else in Astro works. It loads `ps_logo -> title -> title_controller_ship -> worldmap`
reliably (~222-236 s, n>=8), presents 504 times, issues 155 draws in its last traced frame, and with
`f9f98b2` never loses the device. What is missing from the screen is exactly two passes:

```
ps=0x5008F0900  mrt=2  textures=41     <- scene composite
ps=0x500125F00  mrt=1  textures=44
```

Both fail the same way and there is no third option today:

- **skipped** (the `f9f98b2` guard) - no crash, but the swapchain freezes. 25 dense samples across a
  full run contain exactly TWO distinct images: `0xFE39B93727A9A9DF` x20 (all 2,025,600 pixels
  non-zero) and `0x7AEE9C1FB9A0F725` x5 (pure black). That is the "black screen".
- **submitted** - they draw, and the device is lost within ~20 s of the worldmap.

Both trace to one instruction:

```
ps=0x5008F0900 pc=0x124C  s_buffer_load_dwordx16
base=s16[0x0CC3CC10:0x00100005]  base_addr=0x50CC3CC10  imm=8288  soffset=0xFFFFFFC0
metadata=srt=0,eud=208
definitions=[0x678:SLoadDwordx4; 0xAE8:SLoadDwordx8; 0xCBC:SMovB32; 0xD58:SBufferLoadDwordx4;
             0xEF8:SAndB64[8790441E]; 0x1054:SBufferLoadDwordx4; 0x1228:SLoadDwordx4]
```

`0x8790441E` decodes as SOP2 with **SDST=s16** - the very register pair the failing load uses as its
descriptor base. So the descriptor is produced by that `s_and_b64`, and whatever it computes leaves
`soffset` as `0xFFFFFFC0`.

**Tried and measured, so do not repeat blind:**

| change | result |
|---|---|
| wave32 lane masks 32-bit (`75b156f`) | no change to Astro; conformance only |
| strict scalar load covers buffer loads (`c5112cf`) | translation now refused instead of zero-filled; device still lost |
| skip refused-translation draws (`f9f98b2`) | device loss 1-2/run -> **0**, survives 423 s; screen freezes |
| SMEM offset wrapped to 32 bits (8288-64=8224) | read succeeds, **device loss returns** at ~253 s. NOT committed |
| expf as a native intrinsic | 21.8M calls, 58% of all HLE calls, and **no measurable change**. Reverted |
| ISA audit #3 (FLAT/SCRATCH) | ruled out: `unknown-flat` and `scratch` are both 0 in Astro |
| `VK_EXT_robustness2` nullDescriptor | already queried AND enabled (`VulkanVideoPresenter.cs:4406-4410`), so an all-zero descriptor is safe - the hang needs a garbage NON-null one |

**Next action.** A zeroed guest V# must lower to `VK_NULL_HANDLE`, not to a fabricated buffer
binding at address 0 with a bogus size - the former is legal under the nullDescriptor feature we
already enable, the latter is undefined and is the remaining candidate for the hang. Failing that,
resolve what `s_and_b64` at `0xEF8` should produce so `soffset` is not `0xFFFFFFC0` in the first
place.

**Measurement traps that cost time here, all real:**

- `[FRAMEPKT] flip=` lines are SAMPLED. 16 logged lines meant flip counter 480-540. Read the
  counter in the line, never the line count.
- `Vulkan VideoOut presented ...` are ONE-TIME info messages. The real per-frame count is
  `vk.present` (504 in the same run where "presented" appears 3 times).
- Swapchain dumping needs THREE vars together: `SHARPEMU_SWAPCHAIN_DUMP_EVERY`,
  `SHARPEMU_TRACE_GUEST_IMAGES=present` and `SHARPEMU_GUEST_IMAGE_DUMP_DIR`. Miss the middle one
  and you silently get zero samples.
- `ShouldLogImportResult` caps import-failure warnings at 8 per (nid, result) and suppresses a
  benign list. Use the HLE effect census for real counts.

### Tested against the codex-gpu audit, and refuted (2026-07-27)

Both of the audit's top-ranked device-loss candidates were implemented and measured. Neither fixes
it, so do not spend time re-deriving them:

- **Arbitrary donor descriptors** (`Gen5ShaderScalarEvaluator.cs:713-755`). The second
  `FirstOrDefault` there matches on nothing at all, so an unresolved image binding can be filled
  with an unrelated texture's T#/S# - wrong but well-formed, which `nullDescriptor` cannot make
  safe. Removing that fallback entirely: **device still lost 2/2, swapchain unchanged.** The change
  is defensible on its own terms but was reverted rather than shipped unproven; it changes binding
  for every shader with unresolved images and there was no budget to regression-test Superliminal.
- **T# dimension cap** (`8b860da`, kept). Astro's legal 10240x320 textures were all being replaced
  by 1x1 fallbacks. Fixing it takes `agc.texture_fallback reason=invalid-descriptor` to **0** - a
  real bug - but with correct textures the composite draws **still lose the device 2/2**, so the
  fallbacks were not the cause either.

`nullDescriptor` and `robustImageAccess2` are already queried AND enabled
(`VulkanVideoPresenter.cs:4406-4410`), so an all-zero descriptor is safe; the hang needs something
else.

**The most important thing the audit says, still untested:** the device loss is **asynchronous**.
The work item before the reported failure took 6251 ms, and the next submission merely observed
`ErrorDeviceLost`. So `0x5008F0900` is NOT established as the guilty draw, and the whole "the menu
is two draws" framing above may be a localization error. **Instrument fence completion by guest
sequence before trusting any attribution here.** That is the next step, ahead of any further fix
attempt.

### Ground truth from actual screenshots (2026-07-27, later)

**This corrects the "frozen, not black" claim above, which was wrong.** We run ON the capture
machine, so `PrintWindow` with `PW_RENDERFULLCONTENT` grabs the emulator window directly - no
`OpenInputDesktop`/`SetThreadDesktop`, and **it works with the session locked**. The rewritten
`%TEMP%\shot.ps1` does this and prints a non-black ratio per capture. Note the window belongs to the
**mitigation-relaunch child**, not the launched pid, whose `MainWindowHandle` is 0 - that is why
earlier "no window" checks were misleading.

Nine captures across one run (t+20s .. t+245s), all successful:

| | t+20..160s | t+190..245s |
|---|---|---|
| non-black | 1,359,504/1,367,100 | 125,076/1,367,100 |
| HUD | `FPS 4.0 FLIP 4.0 DRAWS 238/S 60/F` | `FPS 0.8 FLIP 2.4 DRAWS 65/S 80/F` |

**The game area is black for the entire run**, and the drop from 99.4% to 9.1% is only the clear
colour going from near-black grey to pure black. Two things follow:

1. **`nonblack_pixels` in the swapchain dump is a misleading metric.** It counts any pixel != 0, so a
   uniform very dark grey scores ~100% "non-black" while being visually black. Every earlier
   conclusion drawn from that number - including "the swapchain is not black, it is frozen" - was
   wrong. Read the ratio AND look at the image.
2. **Nothing is frozen.** The HUD shows 2.4-4.0 flips/s and 60-80 draws *per frame*, sustained. So
   the two repeating swapchain hashes mean the draws produce identical black output, not that
   presentation stopped.

So the real defect is: Astro submits 60-80 draws every frame, for the whole run, and none of it
reaches the screen.

`ExecuteOrderedGuestFlip` (`VulkanVideoPresenter.cs:5587-5600`) presents `_guestImages[work.Address]`
whenever it exists and is `Initialized`. Measured: `flip_capture_failed` is **0**, so it always finds
an initialized image and presents it - but `Initialized` only means the image was created and
cleared, not that anything drew into it this frame. The run presents registered display buffers
`0x507410000` and `0x5093F0000`; the first appears once in the whole log and the second never. That
matches the audit's ranked-#3 black-screen finding: a cached display image always beats the
translated-draw fallback without proving a current-frame writer.

**Next step:** establish where the 60-80 draws per frame actually land, and whether anything ever
copies an offscreen target into the flip address. If nothing does, the flip is presenting a cleared
buffer and the composite result is being discarded - which would explain a black screen with a fully
healthy draw rate, and is independent of the device-loss investigation above.

### The display buffers are never written by anything

Measured on the same run as the screenshots above, 11.7 MB of log:

- The flip presents registered display buffers `0x507410000` and `0x5093F0000`.
- Searching the whole log for `0x5074` and `0x5093` returns **0 hits** each - the only appearance of
  `0x507410000` anywhere is the one-time `presented guest frame` info line itself.
- No `dma_data`, no `image_write`, no `blit`, no `copy_image`, and no `[SYNC] cpu-write` names either
  address.
- The guest images that DO see traffic are elsewhere entirely: `0x555F59000` (1024x1024, 54
  cpu-writes), `0x56E35A800`, `0x5704F0000`, `0x562FF0000`.

So the buffer we present and the memory the title actually renders into are disjoint. `Initialized`
on the presented image means only "created and cleared", and `flip_capture_failed` is 0 because that
test can never fail - `ExecuteOrderedGuestFlip` (`VulkanVideoPresenter.cs:5587-5600`) never checks
that a writer touched the image for this frame.

That is sufficient on its own to produce a black screen at a healthy 60-80 draws per frame, and it is
independent of the device-loss thread. **Next: find what the title expects to move its rendered
result into a registered display buffer** - a final composite, a resolve (15 `resolve` mentions in the
run are the only candidate seen), or a flip that should name the offscreen target rather than the
registered one. Confirm against `sceVideoOutRegisterBuffers2`'s recorded addresses which buffer the
guest believes it is scanning out.

## What a fresh session needs

The knowledge from this work is in this file and in the commits; what a new session cannot recover
on its own is the *environment*. Supply these and it can start measuring within minutes instead of
rediscovering the setup.

**Already on this machine, just point at them:**

- Game dumps under `games/` - Astro at
  `games/asTRO.BOT-PPSA21564-USA-Game-v01.007.000-PS5/PPSA21564-app/eboot.bin`, Superliminal at
  `games/superliminal/eboot.bin`.
- Decrypted 4.03 firmware, 565 cleartext modules: `games/PS5_4.03_reconstructed`.
- Sony PS5 GPU Shader Core ISA PDFs: `games/gpu shit_forzen`.
- psdevwiki mirror, 304 pages: `games/psdevwiki_ps5/wikitext`.
- FFmpeg 7.1 lives next to the binary (`artifacts/bin/Release/net10.0/win-x64`); put that directory
  on `PATH` before running the milestone harness or it refuses to start.

**Useful to hand over, if a run needs discussing off-machine:**

- The run manifest and log excerpt from `artifacts/game-runs/<game>/<timestamp>/` - `run.json` plus
  the first divergence in `attempt-NN.log`. These are large; share the excerpt, not the whole log.
- A screenshot from `%TEMP%\shot.ps1` (see below). A single capture settles questions that log
  inference gets wrong - it did exactly that here.
- The codex audit reports if they are still around:
  `%TEMP%\claude\...\scratchpad\codex-gpu\REPORT.md` (29 KB, GPU/RDNA2) and `codex-nid\REPORT.md`.
  These are analysis, not secrets, and worth preserving somewhere durable - the scratchpad is
  session-scoped and will be lost.

**Do not share off-machine:** anything from `games/` (game and firmware content) or
`games/do notopen` (keys). Findings and addresses are fine; bytes are not.

### Screenshots: use them early

`%TEMP%\shot.ps1` captures the emulator window with `PrintWindow` + `PW_RENDERFULLCONTENT`. It works
**with the session locked**, needs no RDP, and prints a non-black ratio per capture:

```
& $env:TEMP\shot.ps1 -Out C:\path\shot.png        # emulator window
& $env:TEMP\shot.ps1 -Desktop -Out full.png       # whole screen; this one DOES need unlocked
```

The window belongs to the **mitigation-relaunch child**, not the pid you launched - the parent
reports `MainWindowHandle=0`, which is why a naive check says "no window". The script enumerates by
pid and handles that.

Take a screenshot before trusting any conclusion about what is on screen. Three claims in this
file's history were wrong until one was taken.

### The targetless-composite theory is also refuted

The audit's ranked-#3 black-screen cause is that a Unity-style targetless final blit
(`AgcExports.cs:6883-6891`) is executed at flip only when the display address is already in
`state.KnownRenderTargets` (`:4280-4287`), so a miss there drops the only composite and the cached
cleared image is presented instead.

Instrumented directly - a trace on the `else` branch of that gate, run for 100 s with
`SHARPEMU_LOG_AGC=1`:

```
agc.composite_dropped  : 0
agc.deferred_composite : 0
```

**Neither fires.** `state.PendingTargetlessDraw` is never set when a flip arrives, so Astro does not
render targetless-and-blit-at-flip at all and that gate is never even reached. The diagnostic was
reverted rather than committed, because a trace that never fires for the title it names is
misleading.

So all three ranked mechanisms are now eliminated for Astro specifically: the arbitrary donor
descriptors, the T# dimension cap (a real bug, fixed, but not this), and the suppressed targetless
composite. What remains established by measurement:

- Astro submits 60-80 draws per frame, sustained, at 2.4-4.0 flips/s.
- Those draws land in guest images around `0x555F59000` / `0x56E35A800` / `0x5704F0000` / `0x562FF0000`.
- The flip presents `0x507410000` / `0x5093F0000`, which **nothing ever writes** - 0 hits across
  11.7 MB of log for either prefix.
- Every flip reports `drawKind=None hasPixels=False hasTranslatedDraw=False`.

**The open question is narrow and concrete: what is supposed to move an offscreen result into a
registered display buffer for this title?** Candidates not yet checked - the 15 `resolve` operations
seen per run (`AgcExports.cs:6703-6704` registers resolve source and destination into
`KnownRenderTargets`, so a resolve whose destination is the display buffer would be the natural
mechanism); a DMA/copy path we do not decode; or a flip that should name the offscreen target rather
than the registered buffer. Start by dumping the source and destination of every resolve and
checking whether any names `0x507410000`.

### The resolve lead is dead too, and where the draws actually land

The 15 `resolve` hits per run are `ResolveMappedAddressOrFallback` - **module load-time address
resolution, not GPU MSAA resolves**. There are no GPU resolve operations in the run at all, so the
"a resolve should target the display buffer" idea is void. Do not re-chase it.

Render-target addresses the draws actually use, from a 100 s AGC-traced run
(`SHARPEMU_LOG_AGC=1`), by frequency:

```
0x53B300000  x112     0x53CB60000  x109     0x53B680000  x109
0x53B580000  x103     0x53B640000  x103     0x53CC20000  x103
0x555F59000  x90      (plus 10,670 hits of 0x0, i.e. unset)
```

The display buffers are `0x507410000` and `0x5093F0000`. **Every render target is in the 0x53B-0x55x
range; neither display address appears anywhere.** So the title genuinely never renders into the
buffers we scan out, and no copy, DMA, blit or resolve moves anything into them.

Note also **10,670 render-target slots decode as address 0**, far more than any real target. Whether
those are legitimately unbound MRT slots or a decode failure is UNVERIFIED and worth checking - if
some of those should be the display buffer, the CB BASE/BASE_EXT decode is the bug. `GetRenderTargets`
is at `AgcExports.cs:8313-8349`; codex verified its field positions agree with Kyty's independent
`pm4.h:629-657,725-735`, so a plain field-offset error is unlikely, but the address-0 volume is not
explained.

**Remaining candidates, in order:**

1. Those 10,670 address-0 render targets. Dump the raw CB_COLOR BASE/BASE_EXT for them; if any should
   resolve to `0x507410000` the decode is wrong and that is the whole bug.
2. A guest-side copy path we do not decode at all (nothing in the log moves bytes into the display
   buffer, so if the title does copy, we are dropping the packet type silently).
3. The flip naming the wrong buffer - i.e. Astro scans out one of the `0x53B*` targets and our
   `sceVideoOutRegisterBuffers2` bookkeeping hands the flip a different address. Cross-check what the
   guest passed to RegisterBuffers2 against what the flip resolves.

### Candidate 3 got firmware evidence, so run it first

A read-only firmware audit (`astro-agc-conformance.md`, finding 9) found that
`sceVideoOutRegisterBuffers2` walks its buffer array with a **`0x20` stride but keeps only qword
`+0x00`**. It reads `+0x08` and throws it away, and never touches `+0x10` at all
(`VideoOutExports.cs:1413-1422`). The firmware bookkeeping worker at `libSceVideoOut.sprx` vaddr
`0x17F79-0x18025` iterates the same `0x20` stride and reads **three** qwords - `+0x00`, `+0x10` and
`+0x08` (`0x17FC0-0x17FEB`).

So there are 0x18 bytes per entry that the guest filled in and we discarded, and the symptom is that
the one address we did keep is never written while the title renders busily somewhere else. That is
not proof - what the other qwords mean is UNVERIFIED and they may be DCC metadata, tiling state or
anything else - but it is a far cheaper test than decoding 10,670 zero slots, and it is the only
candidate with independent evidence behind it.

The dump is already wired in. Run with `SHARPEMU_LOG_VIDEOOUT=1` and grep:

```
videoout.register_buffer_entry index=N q0=... q1=... q2=... q3=...
```

Read it like this:

- **any of q1/q2/q3 is in the `0x53B*`/`0x55x` family** - that is the whole bug. We register the
  wrong qword, the title has been rendering into the buffer it registered all along, and the fix is
  to keep the right one.
- **q1/q2/q3 are small, zero, or obviously not addresses** - finding 9 is a real conformance gap but
  not this bug. Cross it off and go to candidate 1.

Only after that is settled is it worth decoding the address-0 render targets.

### Do not confuse a conformance defect with the cause

The same audit found that several bound AGC exports emit private markers or NOPs where firmware
emits real packets (`sceAgcDcbSetFlip` 6 DWORDs vs firmware's 0x40, `qj7QZpgr9Uw` reduced to a no-op
at 10 call sites, the parser stall reduced to a NOP, `sceAgcSuspendPoint` never submitting). Those
are real and worth fixing, and they are catalogued in `astro-agc-conformance.md`.

They are **not** evidence for the black screen, and the audit says so itself: it never ran the
emulator. Our DCB parser is semantic, not byte-accurate, so a private 6-DWORD flip marker is a
deliberate design choice that works, not an accident. Anyone who "fixes" the builders to emit
firmware bytes without simultaneously making the parser byte-accurate will delete the flip path
that currently works and make things strictly worse. Builder and parser move together or not at all.

The one cheap exception is `sceAgcInit` (`AgcExports.cs:695-706`): Astro passes a writable pointer
and selector 13, and we return success without writing the DWORD. That one is worth doing on its own
merits whatever the black screen turns out to be.

### GPU/RDNA2 audit: what it settled and what it left open

A second read-only audit went over the shader translator, descriptors and present path. Its verdicts
that changed this session's course:

- **The device-loss attribution was unsound.** Vulkan device loss is asynchronous, and the work item
  before the reported one took 6251 ms. `0x5008F0900` is not proven guilty. Instrument fence
  completion by guest sequence before believing any name attached to a loss.
- **The SMEM wrap is necessary but not sufficient.** The shader deliberately builds `0xFFFFFFC0` with
  a 32-bit scalar multiply immediately before the load, and the wrapped offset 8224 is the only one
  consistent with the valid 17,904-byte V#. Do not revert it just because the value looks negative.
  It also does not fix the loss - the post-wrap run still died.
- **Resource discovery is branch-insensitive and can invent bindings.** The scalar evaluator does not
  model the `EXECZ` branch that governs the terminal block, and its fallback could copy an unrelated
  T#/S# into any unresolved image instruction. So "the read succeeded" never established that the
  resulting 41 texture bindings describe the real path. The donor fallback was removed and reverted
  once already; if it is attacked again, the correct shape is true/false/unknown branch states plus a
  precise refusal, not a substitute descriptor.

Items it flagged that were then fixed and measured: the 8192 T# dimension cap (`8b860da`, Astro binds
legal `10240x320` type-13 arrays; `agc.texture_fallback reason=invalid-descriptor` went to 0) and the
wave32 mask store (`75b156f`).

Items it flagged that remain open, all already catalogued in `ps5-shader-isa-audit.md` and confirmed
still present in current code:

| Item | Where | Why it matters |
|---|---|---|
| `SBarrier` emits memory semantics `0x108`, audit requires `0x948` | `Gen5SpirvTranslator.cs:2458-2466`, and the wave64 bridge barrier at `:6573-6580` | buffer/image writes never become visible across the barrier |
| subgroup size never pinned at pipeline creation | queried at `VulkanVideoPresenter.cs:4180-4203`, absent at `:7294-7310` | shader `0x500529F00` declares a 64-lane wave in a 25-thread workgroup; ballot/EXEC/VCC compute output corrupts |
| ~~MIMG opcode decoded as 7 bits~~ **REFUTED** | - | Sony's own encoding diagram shows `OP7`, seven bits. Our decode is correct; the audit finding came from AMD's RDNA2 guide. See `prospero-isa-source.md` |
| MTBUF FORMAT discarded | `Gen5ShaderTranslator.cs:1686-1709` | vertex positions can collapse to zero |
| VOP3 promoted ranges fall through to `Vop3Raw***` | `Gen5ShaderTranslator.cs:1473-1558` | drops whole complex shaders |
| FLAT/SCRATCH segments unimplemented | `ps5-shader-isa-audit.md:99-121` | pixel scratch spills in long shaders |
| DS opcode 0x3E/0x3F misdecode | `Gen5ShaderTranslator.cs:1652-1654` | - |
| DCC/CMASK/FMASK/HTILE state and the guest's decompress passes unimplemented | - | compressed storage read as raw pixels |
| COMP_SWAP dropped from CB state | - | wrong channel order; actual Astro values UNVERIFIED |

The barrier semantics constant is the cheapest of these by a wide margin.

Two warnings it declared **stale**, so do not chase them: the missing `VK_KHR_maintenance8`
(non-gather sample offsets are already folded into normalized coordinates at
`Gen5SpirvTranslator.cs:4614-4629`, so the forbidden dynamic `Offset` operand is not being emitted),
and the eight zero-dimension dispatches (rejected before submission).


## 2026-07-27, later: the composite is a compute dispatch, and it writes nothing

Four measured runs collapsed the candidate list to one fact.

**Candidate 3 is dead.** The `videoout.register_buffer_entry` dump ran. The guest registers exactly
two buffers and fills in only qword 0 of each `0x20` entry:

```
videoout.register_buffer_entry index=0 q0=0x0000000507410000 q1=0 q2=0 q3=0
videoout.register_buffer_entry index=1 q0=0x00000005093F0000 q1=0 q2=0 q3=0
```

We register the right addresses. The RegisterBuffers2 conformance gap
(`astro-agc-conformance.md` finding 9) is real but is not this bug. Cross it off.

**Astro composites its scanout image with a compute shader, not a render target.** With
`SHARPEMU_LOG_VK_RESOURCES=1`, both display buffers appear as global (storage) buffers bound to
compute dispatches:

```
vk.global_buffer base=0x0000000507410000 bytes=16777216 writable=True writeback=True
vk.global_buffer base=0x00000005093F0000 bytes=16777216 writable=True writeback=True
vk.render_work_enter #5 sequence=3442 queue=acb.compute[64] ... VulkanComputeGuestDispatch
```

`vk.global_buffer` is a first-use trace, not per-dispatch (16,020 dispatches, ~592 traces), so once
each is expected. Only 46 of 592 global buffers are `writable=True`, and the two display buffers are
among them.

**This is why the render-target scan never found them.** Every previous search looked at CB colour
targets and concluded "the title renders into `0x53B*` and never touches the buffers we present."
That was true and misleading: the final composite is not a draw. It is a compute dispatch writing
through a V#. The `0x53B*` targets are the scene passes feeding it - note the two inputs bound right
after the dispatch, `0x510D10000` 1920x1080 `R16G16B16A16Sfloat` (HDR scene) and `0x5104A0000`
1920x1080 `R8G8B8A8Unorm` (tonemapped), both `initialized=True`.

**And the dispatch writes nothing.** `SHARPEMU_LOG_AGC_SHADER=1` gives the writeback accounting:

```
vk.global_writeback base=0x0000000507410000 potential_bytes=16777216 changed_bytes=0
  changed_runs=0 changed_pages=0 written_pages=0 probe_nonzero=0/256 changed_head=
vk.global_writeback base=0x00000005093F0000 potential_bytes=16777216 changed_bytes=0 ...
```

Zero changed bytes across the whole 16 MB, every time. Not a stale-copy problem, not a writeback
problem: the shader produced no output at all.

**Compute writeback works in general**, so this is specific to that dispatch. 24 of 357 writebacks in
the same run carry real data, including `0x5627F0000` turning 4 MB of an 8 MB buffer, and
`0x514050000` writing all 196,608 bytes with `probe_nonzero=256/256`.

So the black screen reduces to: **one compute dispatch, correctly bound to the correct display
buffer, with its inputs present and initialized, produces zero bytes.**

### Why that dispatch might produce nothing - in rank order

1. **Its EXEC mask is empty because the scalar evaluator followed a branch the hardware skips.** This
   is the GPU audit's rank-1 finding, and it predicts exactly this symptom: the dispatch runs, binds,
   and stores nothing. Check whether the composite shader's translation refused, and what
   `SHARPEMU_CFG_RESOURCE_DISCOVERY` reports for it.
2. **The store target resolves to a manufactured descriptor.** Same audit finding, other half: if the
   output V# could not be proven, the fallback could hand the shader an unrelated donor, so it writes
   somewhere real but wrong. That would also show 4 MB of changed bytes *somewhere else* - check
   whether any unexplained writeback is 8 MB of 1920x1080 shaped data.
3. **Dispatch dimensions collapse to zero.** Zero-dimension dispatches are rejected before submission
   and were counted at eight in an earlier run; confirm this dispatch is not among them.
4. **The store is a format/opcode we drop.** MTBUF FORMAT is discarded and the MIMG opcode is decoded
   at 7 bits instead of 8 (see the ISA table above); an image-store composite could be lost that way.

### Next step, precisely

Identify the compute shader address bound to the dispatch that binds `0x507410000`. Run with
`SHARPEMU_LOG_VK_RESOURCES=1 SHARPEMU_LOG_AGC_SHADER=1` together and correlate the
`vk.global_buffer base=0x507410000` line with the `render_work_enter` that follows it and the
`cs=0x...` shader address in that batch. Then check that shader for a refused translation, an
unresolved descriptor, or an empty EXEC path. Everything else on the candidate list is now
downstream of that one shader.
### Correction and sharpening: the compute pass on the display buffer is a CLEAR

Two things above were wrong and are corrected here.

**First, the original attribution was made from log adjacency and was unsound.** `vk.global_buffer
base=0x507410000` happened to be printed next to a `render_work_enter ... VulkanComputeGuestDispatch`
line, and that is not evidence of association - the log interleaves three queues. Proper attribution
needed a trace that names the shader at the point the buffer is bound.

**Second, the first version of that trace was silently filtered.** It was placed under the existing
`traceResources` local in the compute path, which is `dispatch.Textures.Count >= 8`
(`VulkanVideoPresenter.cs:6582`). The dispatch being hunted binds **zero** textures, so the trace
reported "no compute shader writes the display buffer" for two runs. Same class of error as the
sampled `flip=` counter. The trace is now gated on `_traceVulkanShaderEnabled` with a comment saying
why.

With that fixed, `vk.compute_global_write` names it:

```
vk.compute_global_write cs=0x0000000808E6AA00 base=0x0000000507410000 bytes=16777216
  writeback=True groups=32640x1x1
vk.compute_global_write cs=0x0000000808E6AA00 base=0x00000005093F0000 bytes=16777216
  writeback=True groups=32640x1x1
```

32,640 groups x 64 lanes = 2,088,960 = exactly **1920x1088** (1080 padded to the wave). Full-screen,
once per buffer. The shader address `0x808E6AA00` is in the **eboot image**, not GPU memory, so it is
an engine utility routine rather than a compiled scene shader. It is the busiest writable-global
shader in the run at 1,383 dispatches.

And its disassembly settles what it is:

```
agc.compute_shader cs=0x0000000808E6AA00 wave=64 local=64x1x1 textures=0 globals=1
  global_writes=True
  opcodes=[VLshlAddU32, VMovB32, BufferStoreFormatXyzw, SEndpgm]
```

Address from the thread id, a constant into a register, one store, end. **That is a buffer clear, not
a composite.** `BufferStoreFormatXyzw` is handled (`Gen5SpirvTranslator.cs:3495-3503`), so it is not a
dropped opcode.

So `changed_bytes=0` is not a failure. The clear writes zeros into a buffer that was already zero, so
nothing changes and `probe_nonzero=0/256` is exactly right. **The display buffer is being correctly
cleared to black every frame, and nothing ever draws over it.**

That is a different bug from the one stated above, and a narrower one. The question is no longer "why
does the composite write nothing" but **"where is the composite at all"**:

- No compute dispatch other than the clear binds either display buffer writably - `cs=0x555F4F500`
  (370), `cs=0x50740A700` (150) and `cs=0x500757800` (72) are the only other shaders with writable
  globals and none names them.
- No draw binds them writably either: `vk.draw_global_write` is **0** for the entire run.
- No `agc.compute_writer` ImageStore names them: 0 of 2,413.
- No colour render target names them: every target is in the `0x53B*`/`0x55x` range.

So across every write path the emulator can see - colour targets, image stores, compute buffer
stores, draw buffer stores - the only thing that ever touches Astro's scanout memory is a clear.
Either the title issues its composite through a path we do not decode at all, or the composite is
being dropped before it reaches any of these traces. That is where the next session starts.
### DMA and copy packets are not the composite either

`SHARPEMU_LOG_AGC=1`, 150 s: **198 `agc.dcb_dma_data` calls, zero of them naming a display buffer.**
There are no `acb_dma_data`, `dcb_copy_data` or `acb_copy_data` calls at all in the run. The DMA
destinations are:

```
0x553C22374 x142   0x553C02924 x142   0x553BE2ED4 x142   0x553C41DC4 x142
0x56F6E6000 x106   0x553B84000 x71    and small constants (0x4, 0xC68, 0xC70, 0xC74)
```

The builders are real, not stubs - `sceAgcDcbDmaData` (`AgcExports.cs:2509`) writes an 8-DWORD packet
carrying dst, src and byte count, and the trace fires in the builder, so a call could not be missed
even if the parser dropped it. The guest simply never asks for a DMA into scanout memory.

**Write paths now eliminated for the display buffers, each by measurement:**

| Path | Evidence |
|---|---|
| colour render targets | every target is `0x53B*`/`0x55x`; neither display address appears |
| compute ImageStore | 0 of 2,413 `agc.compute_writer` |
| compute buffer store | only `cs=0x808E6AA00`, and it is a clear |
| draw buffer store | `vk.draw_global_write` = 0 for a whole run |
| GPU DMA / copy packets | 198 DMA calls, 0 to scanout; no copy calls at all |
| CPU writes | 0 hits for `0x5074`/`0x5093` in 11.7 MB |
| GPU resolves | there are none; the 15 `resolve` hits are address resolution |

Every mechanism the emulator can observe has been ruled out. The composite is therefore either issued
through a packet type we do not decode at all - in which case it is being dropped silently before any
builder trace, so look at the DCB parser's unknown-opcode handling rather than at any export - or the
title is not compositing to scanout at this point in its boot and the worldmap is genuinely expected
to be black until something later runs.

Do not add another export-level trace before checking the parser's unhandled-packet path; six write
mechanisms have now come back negative and the next one probably will too.
### The per-frame packet census (ground truth, reusable)

`SHARPEMU_TRACE_DRAWS=1 SHARPEMU_TRACE_FRAME_PACKETS=1` produces a complete per-frame opcode
histogram. Steady state is identical across flips 180, 240, 300 and 360, so the frame is stable and
this is a reliable baseline to diff against:

```
[FRAMEPKT] flip=360 submission=2520 packets=2173 draws=26 dispatches=42
opcodes=[0x10/r24:693, 0x46:223, 0x76:197, 0x58:163, 0x10/r11:160, 0x10/r12:160,
         0x10/r10:83,  0x10/r0:79, 0x10/r21:75, 0x10/r18:65, 0x10/r17:55, 0x15:42,
         0x2F:40, 0x10/r19:33, 0x10/r22:25, 0x10/r4:23, 0x10/r28:12, 0x13:12,
         0x26:12, 0x10/r25:5, 0x11:5, 0x16:4, 0x35:2, 0x8E:2, 0x10/r6:1,
         0x10/r23:1, 0x25:1]
```

An earlier frame (flip=60/120) differs slightly - 2,032 packets, 31 draws, 26 dispatches, and no
`0x35` or `0x8E` at all - so those two appear only once the steady-state frame begins.

`0x10` is `IT_NOP` with a private register selector; those are our own builders' semantic markers. The
non-NOP opcodes come from builders that emit real PM4 headers.

**Where to look first.** The parser matches a small explicit set - `ItSetPredication`, `ItAcquireMem`,
`ItWriteData`, and `ItNop` with registers `RDmaData`, `RFlip`, `RWriteData`, `RReleaseMem`,
`RAcquireMem`, `RWaitMem32`, `RWaitMem64` - and everything else falls through `offset += length`
(`AgcExports.cs:4425`) with **no diagnostic whatsoever**. That silence is why six write-path searches
came back negative without ever surfacing a "we ignored this" line.

The candidates worth auditing against the handled set, in order of how well their frequency matches a
once-per-frame present operation:

| Opcode | Per frame | Note |
|---|---:|---|
| `0x25` | 1 | exactly once per frame; only appears in steady state |
| `0x10/r6`, `0x10/r23` | 1 each | private markers used once per frame |
| `0x35`, `0x8E` | 2 each | absent from early frames, appear with steady state |
| `0x16` | 4 | - |
| `0x11` | 5 | - |

The next concrete step is small and does not need a run first: enumerate the `(op, register)` pairs
the parser actually handles, diff that set against this histogram, and add an unhandled-packet counter
so the silence stops. Anything in the table above that turns out to be unhandled is a candidate for
the missing composite.
### The parser stall is now real - and it was not the cause

The census pointed at indirect work, and the trail was concrete. Four `agc.dispatch_reject
source=base-indirect ... raw=00000000/00000000/00000000 reason=zero-dimension` warnings name
`0x5074063C0`, `0x5074063E0`, `0x50740A520`, `0x50740A540` - and those are **exactly** the four
addresses passed to `sceAgcDcbStallCommandBufferParser`:

```
agc.dcb_stall_parser cmd=0x5027034A4 size=1 addr=0x5074063C0 reference=0x500000000
agc.dcb_stall_parser cmd=0x502703520 size=1 addr=0x5074063E0 reference=0x500000000
agc.dcb_stall_parser cmd=0x50270359C size=1 addr=0x50740A520 reference=0x500000000
agc.dcb_stall_parser cmd=0x502703618 size=1 addr=0x50740A540 reference=0x500000000
```

That is the textbook GPU-driven pattern: a compute pass publishes indirect dispatch arguments, the
parser stalls until they land, then `DISPATCH_INDIRECT` consumes them. We were emitting a bare
`IT_NOP/RZero` for the stall (`AgcExports.cs`, `DcbStallCommandBufferParser`) on the reasoning that
direct execution has no command processor to stall. That reasoning is wrong: our GPU work is
submitted asynchronously and only reaches guest memory on writeback, so the stall has real content.

**Also note the count trap.** `agc.dispatch_reject` is deduplicated by
`(dimensionsAddress, initiator, reason)`, so "4 warnings" is 4 *distinct buffers*, not 4 events. With
`0x16 ItDispatchIndirect` at 4 per frame over ~500 frames that is roughly **2,000 rejected dispatches
per run**, reported as four lines. Do not read a deduplicated warning as a frequency.

**The fix, which is correct and is kept.** The stall now emits a private `RStall` (0x1D) marker in the
same two DWORDs firmware uses - there is no room for the awaited address, so the parser treats it as a
full barrier, which is conservative and never weaker than waiting on one address. On parse it submits
an ordered no-op and waits for it, draining outstanding guest work.

Verified plumbed, by diffing the packet census before and after:

```
before   ... 0x10/r0:79 ...                 (no r29)
after    ... 0x10/r0:74 ... 0x10/r29:5 ...
```

Exactly five stalls per frame, `r0` down by exactly five, `packets=2173 draws=26 dispatches=42`
unchanged. Zero `stall_wait_failed`. The three failing unit tests
(`GuestMemoryAllocatorTests`, `KernelFileEventCompatExportsTests`) were confirmed pre-existing by
stashing the change and re-running.

**And the dispatches are still rejected with identical all-zero arguments.** So the arguments are not
arriving late - they are never written at all. The stall was a real conformance defect sitting
directly on the suspicious path, and fixing it changed nothing observable. Astro still reaches
worldmap with a black screen.

That still narrows things usefully: the producer of those indirect arguments is missing, not
mistimed. The next question is which pass is supposed to write `0x5074063C0` and why its output never
lands - note that `0x50740A700` (a compute shader with writable globals, 150 dispatches/run) sits in
the same allocation as the argument slots, and the display buffer starts immediately above at
`0x507410000`.
### The indirect-dims retry was dead code, and the flag that would have run it was inverted

Two defects, one on top of the other.

**1. The retry was never wired up.** `TryReadComputeDispatch` computes an
`indirectDimsRetryAddress` out-parameter whose whole purpose is to let the parser suspend on a
zero-dimension indirect dispatch rather than drop it - the comment at its declaration even names
Astro Bot. `HandleSubmittedIndirectDimsWait` is fully implemented: it registers a `GpuWaitRegistry`
waiter, re-parses the same packet when the dims buffer changes, and gives up after a 150 ms budget.
**It had no callers.** The single call site in the DCB parser passed `out _`, so the address was
discarded and the dispatch was dropped every time. Now wired.

**2. `SHARPEMU_GPU_WAIT_MODE=force` disables the mechanism, and it is in the run script baseline.**
The flag reads

```csharp
private static readonly bool _gpuWaitSuspendEnabled = !string.Equals(
    Environment.GetEnvironmentVariable("SHARPEMU_GPU_WAIT_MODE"), "force", ...);
```

It is **negated** - `force` means force-through, not force-wait. Every Astro run in this session, and
presumably many before, carried `SHARPEMU_GPU_WAIT_MODE=force` as a baseline flag and therefore had
GPU wait suspension switched off. A suspend that silently declines is indistinguishable from one that
was never attempted, which is why this survived so long; the new `agc.indirect_dims_retry` warning
prints the flag state alongside the outcome so it cannot hide again.

**With both fixed, the retry demonstrably runs:**

```
agc.indirect_dims_retry dims=0x5074063C0 suspended=True  gpu_wait_suspend=True
agc.indirect_dims_retry dims=0x5074063C0 suspended=False gpu_wait_suspend=True   <- 150 ms later
```

Each of the four argument buffers suspends once, waits the full 150 ms budget, and is then dropped
because the dimensions are still zero.

**Which is the useful result: the arguments are never produced.** Not late by a frame, not late by a
submit - absent after 150 ms of real time with the parser genuinely blocked. Combined with the stall
fix above, both plausible timing explanations are now eliminated by direct experiment. Whatever
should write `0x5074063C0`, `0x5074063E0`, `0x50740A520` and `0x50740A540` never runs at all.

**The default is deliberately left as `force`.** Enabling suspension costs up to 150 ms per rejected
dispatch and there are four per frame, so turning it on globally stalls roughly 600 ms per frame and
the title makes visibly less progress in a fixed run. The wiring is correct and should stay; flipping
the default is a separate decision that needs the producer fixed first, otherwise it only converts a
dropped dispatch into a slow dropped dispatch.
### Our address arithmetic is correct, and the argument slots hold nothing

The remaining way to explain all-zero dimensions without a missing producer was that we compute the
wrong address. `dimensionsAddress = state.IndirectArgsAddress + dataOffset`, and both halves are ours
to get wrong - `sceAgcDcbSetBaseIndirectArgs` masks the base with `& ~7u` and splits it across two
DWORDs, and the packet offset could plausibly be dword-scaled rather than byte-scaled.

Measured, with the base, the offset, and a dword-scaled probe all logged:

```
agc.indirect_dims_window dims=0x5074063C0 base=0x5074063C0 offset=0x0 dword_scaled=...=0/0/0
agc.indirect_dims_window dims=0x5074063E0 base=0x5074063E0 offset=0x0 dword_scaled=...=0/0/0
agc.indirect_dims_window dims=0x50740A520 base=0x50740A520 offset=0x0 dword_scaled=...=0/0/0
agc.indirect_dims_window dims=0x50740A540 base=0x50740A540 offset=0x0 dword_scaled=...=0/0/0
```

**The offset is zero.** The guest points the base directly at each argument slot, so there is no
scaling question and no arithmetic to get wrong - `base + 0` is the address the guest named.

The 512-byte window around each slot is also revealing. It is full of float constants and contains no
plausible group counts anywhere:

```
BF800000 (-1.0)  3F800000 (1.0)  40400000 (3.0)  40000000 (2.0)
3F000000 (0.5)   BF000000 (-0.5) 447A0000 (1000.0)  000026E0 (9952)
```

Real dispatch dimensions would be small integers. There are none within +/-256 bytes of any of the
four slots. So the arguments are not misplaced, not mis-scaled and not nearby - the slots are simply
never written.

**Every explanation except a missing producer is now eliminated by direct measurement:** not late
(the stall drains and the retry waits 150 ms), not misaddressed (offset is zero, base is the guest's
own), not nearby (the window is float constants), not a refused shader (no compute translation
failures in a run), and not an unwritable binding by accident (the region is bound read-only because
its only binder is the consumer).
### The four dropped dispatches are four distinct shaders that never run

Logging the compute shader bound at reject time (`ComputePgmLo/Hi` from `state.ShRegisters`) shows
this is not one composite pass being missed - it is four separate programs, each with its own
argument slot:

| Argument slot | Shader | Ever executed? |
|---|---|---|
| `0x5074063C0` | `cs=0x500570500` | no |
| `0x5074063E0` | `cs=0x50059C200` | no |
| `0x50740A520` | `cs=0x5005CB600` | no |
| `0x50740A540` | `cs=0x5005FD000` | no |

None of the four appears in any `agc.compute_shader` trace anywhere in a run, so none has ever been
translated or dispatched. They sit in the `0x5005xxxxx` range alongside the scene shaders that do run
(`0x5006C5F00`, `0x500665C00`, `0x500757800`, `0x500529F00`), so they are ordinary compiled programs,
not something exotic.

Four GPU-driven passes, each gated on arguments nobody writes. That shape - one argument slot per
pass, slots allocated in two adjacent pairs - suggests a chain or a fan-out rather than a single
final composite, so **"the composite is one of these four" is an assumption, not a finding.** It may
be that these four feed something else, or that they are unrelated to presentation entirely and the
composite is missing for its own reasons.

### Where a fresh session should start

1. **Disassemble the four shaders** at `0x500570500`, `0x50059C200`, `0x5005CB600`, `0x5005FD000`.
   What they write tells you immediately whether they are the composite, a culling chain, or
   irrelevant - and that decides whether this whole thread matters.
2. **Find the producer of the argument slots.** The region is covered by
   `vk.global_buffer base=0x5074050F0 bytes=262144 writable=False` - bound read-only, by the consumer
   only. No shader in a whole run binds it writable. Either the producer never runs, or it runs and
   its store resolves to the wrong address. The latter is codex-gpu's rank-1 finding (the scalar
   evaluator is branch-insensitive and can mis-resolve a V# base), and the way to test it is to look
   for a writeback with changed bytes at an address nothing should be writing.
3. **Do not re-derive the eliminated set.** Not late, not misaddressed, not mis-scaled, not nearby,
   not a refused shader, not DMA, not a draw, not an ImageStore, not a colour target, not a CPU
   write, not RegisterBuffers2, not GPU resolves. Each of those cost a run.

## 2026-07-29: the measured black boundary and dispatcher-cap cause

This section supersedes the older `docs/astrobot-bringup.md` claim that
unbound SMEM plus an unwritten 1x1 exposure texture makes the tonemap output
black.

### The live tonemap has no missing or zero-filled scalar load

The exact final tonemap is `ps=0x500640D00`, not the separate expensive
`0x5008F1400` scene shader. Run
`artifacts/game-runs/astro/20260729-071403-corpus-gate/attempt-01.log` audited
the live tonemap by instruction PC:

```
smem_sites=15 smem_direct_covered=15 smem_missing=0
sbuffer_sites=15 sbuffer_direct_covered=15 sbuffer_missing=0
smem_zero_filled=0
```

It has one real `s28` constant buffer, 160 bytes long, and every reachable
scalar load is covered. This directly falsifies the historical "one
`s_buffer_load` binding and all other reachable SMEM loads become zero"
account for the shader that writes the scanout. The two recovery flags named
in the old document do not exist in `src/`; do not reintroduce them.

The real dynamic 1x1 state is also present. `0x556760000` reads back as
RGBA32F bytes `0000803F000080BF0000003FEC51383E`, or approximately
`(1,-1,0.5,0.18)`. `0x532830000` is explicitly filled with opaque black and
sampled as an auxiliary tonemap input in that allocation lifetime; it is not a
missing exposure writeback. `SHARPEMU_FORCE_EXPOSURE` overrides more than one
small texture and is not a valid fix or a discriminating experiment.

### The final tonemap inherits black; it does not create it

The retained shader trace identifies the post-title draw:

```
ps=0x500640D00
target=0x507410000:3840x2160:fmt9/num0/tile27
textures=[
  0x53B9F0000:2432x1368:fmt12,
  0x532830000:1x1:fmt12,
  0x556760000:1x1:fmt14
]
```

Run `artifacts/game-runs/astro/20260729-080048-corpus-gate/attempt-01.log`
read back the target and all three inputs from that exact draw, after the title
transition. Raw images are in
`artifacts/codex-astro-title-tonemap-paired-20260729-0801/`.

- Scanout `0x507410000` is exact opaque black.
- Main HDR input `0x53B9F0000` is already RGB zero at every pixel and alpha
  half-float 1 at every pixel.
- Auxiliary `0x532830000` is the expected opaque black.
- Dynamic `0x556760000` contains the valid vector shown above.

This closes the earlier question: the tonemap does not turn a nonblack main
input black. It receives black and preserves it.

The earlier nonblack capture at `0x514080000` must not be described as the
connected final scene. It is one of the three MRTs written by
`ps=0x5008F1400`; it is spatially varied, but no measured dependency connects
it to `0x53B9F0000`. A nonblack render target somewhere in the frame is not
evidence that the final HDR chain is nonblack.

### The earliest directly measured black boundary is upstream

Run `artifacts/game-runs/astro/20260729-074927-corpus-gate/attempt-01.log`
captured both sides of the same selected `cs=0x50068FA00` occurrence. Raw
images are in
`artifacts/codex-astro-title-postprocess-paired-corrected-20260729-0751/`.

- Source `0x537060000`, 2432x1368 RGBA16F, is all zero: all 26,615,808 bytes.
- Destination `0x53AA00000` has RGB zero everywhere and alpha half-float 1
  everywhere.

That compute pass also inherits black and merely makes alpha opaque. No normal
render-target, compute-image, global-buffer, or DMA writer for `0x537060000`
appears in the retained full trace. Its preceding color-backend operation is a
mode-6 DCC decompress that SharpEmu deliberately skips.

DCC is therefore a live missing semantic and the earliest measured frontier,
but it is **not yet a proven root cause**. The measured surface bytes and DCC
metadata bytes are all zero, and the captured color state is
`info=0x10040730`, `dcc_control=0x00180028`, clear words `0/0`. Those values
decode to black too. A valid differential must show that honoring the
metadata operation produces nonblack RGB before blaming DCC.

The only guest frame that has reached present is still exact opaque black.
Because hundreds of later submissions remain queued, absence of a later
nonblack present is not evidence that every queued guest frame is black.

### The four zero-dimension indirect shaders are not the composite

The four programs previously stopped at unknown VOP3 opcode `0x0E4`.
Cross-checking the current SDK-derived decoder in
`inspiration/acelogic-sharpemu` gives:

- VOP3 `0x0E4` = `v_cmp_gt_u64`;
- the next instruction is split MIMG opcode `0xE6` =
  `image_bvh_intersect_ray`.

After adding the unsigned-64 compare, run
`artifacts/game-runs/astro/20260729-070047-corpus-gate/attempt-01.log` advanced
all four decodes to the same `unknown-mimg op=0xE6`. Porting the MIMG decode
and its no-hit sentinel lowering makes their ray/BVH identity explicit.
Their indirect dimensions are still genuinely zero, so they never execute.
They are ray-tracing fan-out passes, not evidence of a dropped final
presentation composite.

### Performance is a proven dispatcher-cap failure

The old 4K `786 ms` number is not current-master performance. At 1080p,
timestamp run `20260729-071403-corpus-gate` records 656 samples of
`ps=0x5008F1400`; 643 exceed 50 ms and average `127.806 ms`. This
2292-instruction, 41-texture, three-MRT shader costs nearly the same across
vertex counts and did not scale with the fourfold 4K-to-1080p fragment-count
reduction. Ordinary fragment fill is not the dominant term.

The cap probe in
`artifacts/game-runs/astro/20260729-080602-corpus-gate/attempt-01.log`, with
raw images in
`artifacts/codex-astro-cap-probe-5008F1400-20260729-0804/`, proves the default
valve is reached on current wave64 master. It changes only the first float MRT
and encodes the final dispatcher state into surviving fragments. Compared
with the non-probe control, exactly two pixels gain the cap marker. Both
report `steps=100000` and dispatcher block 49, whose guest-PC map starts at
`0x1DEC`.

`0x1DEC` lies inside the shader's second bindless EXEC-convergence loop. Its
headers are `0x1DA4`/`0x1DDC`; the unconditional backedges at
`0x27A8`/`0x27B0` return to them, while `v_cmpx` and
`s_cbranch_execz` are supposed to retire lanes and exit. The probe records
where the fixed counter expires, not a claim that `0x1DEC` itself is the
faulty instruction. Two pathological surviving fragments are enough to keep
the whole draw alive until the 100,000-step guard.

That measured control-flow failure explains the nearly resolution-invariant
GPU cost, 501-deep submission queue, low host CPU, and approximately 0.1
presented FPS. It is separate from the black-color chain: this shader writes a
nonblack MRT, so the cap is not evidence that it creates black. A lower
`SHARPEMU_SHADER_MAX_STEPS` remains a diagnostic only; it silently truncates
guest execution and is not a fix.

#### The cap is a zero-node linked-list cycle, not an EXEC-width guess

Paired SGPR probes on 2026-07-29 identify the state inside block 49. The
`s107` run is
`artifacts/game-runs/astro/20260729-083337-corpus-gate/attempt-01.log`; the
`s66` run is
`artifacts/game-runs/astro/20260729-083749-corpus-gate/attempt-01.log`.
The same two pixels account for the entire four-byte readback difference:
`s107` has low byte 1 and `s66` is zero.

The disassembly explains those exact values. The inner loop loads the current
index at `0x1DD4`, rejects only `0xFFFFFFFF` at `0x1DE4`, computes
`2*index+1` in `s107`, loads the next index into `s66` at `0x1E18`, then
copies `s66` back to `s107` at `0x27A4`. A zero next-index therefore repeats
forever as `s107=1` inside block 49 and `s66=0` after the load. The bound
129,600-byte head buffer and 16 MiB node buffer are both zero in the retained
resource snapshot. This is a missing or malformed linked-list producer (most
likely the empty-head sentinel initialization), not evidence that wave64
EXEC lowering itself is wrong.

The color target has guest write mask `0x7`, so alpha cannot carry the probe's
cap predicate. The conclusion comes from the paired two-pixel differential
and the already-established default cap marker, not from reading a masked
alpha channel.

#### Mode-6 host-image preservation cannot rescue the measured black input

The ordered metadata-operation trace in
`artifacts/game-runs/astro/20260729-082751-corpus-gate/attempt-01.log`
classifies the exact 2432x1368 input `0x537060000` first as
`NeedsDecode location=none`, later as a guest-memory-backed image. There is no
compatible initialized host image to preserve at its first mode-6 operation.
GFX10 DCC byte `0x00` means `CLEAR_0000` (`0x20` is clear-register color and
`0xFF` is uncompressed); its metadata, clear registers, and physical color
bytes are all zero. Faithful decompression of that measured state remains
black. The preservation path is architecturally necessary for live expanded
host images, but DCC is not the source of nonblack pixels in this occurrence.

#### The missing linked-list producer was real; fixing it collapses the backlog

The 2026-07-29 producer run
`artifacts/game-runs/astro/20260729-101059-corpus-gate/attempt-01.log`
closes the zero-node cycle above. Two independent omissions had dropped the
producer chain:

- `ds_append` had no architectural GDS lowering. It now reserves one range per
  active wave, broadcasts the pre-add counter to the wave, and uses M0's
  high 16 bits as the GDS byte base and low 16 bits as the region size.
- `s_waitcnt_vscnt` was treated as an unsupported scalar-evaluator operation
  instead of the scheduling-only no-op it is in this static evaluator.

After both fixes, compute shader `0x5006EC700` writes the expected empty-list
sentinel (`0xFFFFFFFF`) to `0x553BE2EE0`, fills the companion head buffer at
`0x553BC3490`, and writes `1,1,1` indirect-dispatch arguments to both
`0x5074063C0` and `0x5074063E0`. The post-fix HUD improved from roughly
0.1 FPS / 1.1-1.5 s / queue 501 to roughly 1.9 FPS / 556 ms. Logged queue
samples in `20260729-101639-corpus-gate` drain from a transient peak of 214 to
a steady low-teens value instead of remaining near 501. This proves that the
producer defect and most of the backlog were one bug.

#### GDS lifetime and its DMA resets are device-global

The first `ds_append` implementation still gave each translated dispatch a
fresh zero-filled 48 KiB storage buffer. That made the producer and finalizer
observe different counters. Simply sharing the buffer exposed a second
omission: the command processor was dropping Astro's per-frame GDS reset
packets, so indirect dispatches accumulated `1, 1, 2, 3, 4, 5...` across
frames and eventually lost the device in
`20260730-011635-corpus-gate` and
`20260730-011956-corpus-gate`. Those two runs are failed experiments, not
evidence for the final behavior.

Sony's SDK and firmware remove the ambiguity:

- Prospero SDK 10.00 defines DMA source `kImmediate32b=2` and destination
  `kGds=1`.
- The size exports in decrypted 4.03 `libSceAgc.sprx` return 28 bytes.
  `sceAgcAcbDmaData` (`st_size=409`, `0x6F0`) and
  `sceAgcDcbDmaData` (`st_size=442`, `0x3CD0`) both emit native seven-DWORD
  `IT_DMA_DATA` packets. The old HLE builders used a private `IT_NOP` packet
  and also read the SysV selector arguments from the wrong registers.
- The firmware patch helpers write the source at packet offset `+8` and the
  destination at `+16`; they do not use the old private-layout offsets.

The corrected run
`artifacts/game-runs/astro/20260730-013659-corpus-gate/attempt-01.log`
decodes and executes zero fills at GDS byte offsets
`0xC68/0xC70/0xC74/0xC78/0xC7C` on every asynchronous-compute submission
and at `0x4` on the graphics queue. These addresses match the measured
`M0=0x0C600020` byte base plus the shaders' `ds_append` offsets; therefore
the live title differential, not an assumed unit conversion, selects byte
offsets in the emulator.

With one persistent Vulkan GDS allocation and queue-ordered
`vkCmdFillBuffer` resets, `0x5006EC700` repeatedly publishes stable
`8160,1,1` and `1,1,1` argument blocks while `0x50059CD00` remains
`groups=1x1x1`. There is no monotonic accumulation and no device loss. The
run was stopped at the title path once that sequence repeated; it did not
reach `ps=0x5008F1400`, so the remaining cap-hit count and pixel-shader
timing are still **unmeasured**, not zero.

The default-cap question is still open. The `20260729-101639` cap probe was
armed for `ps=0x5008F1400`, but no probed render target retired into a capture,
so the surviving cap-hit count is **unmeasured**, not zero. Orphan commit
`8d6b619` does not solve that evidence gap: its runtime count is also painted
into an MRT, while its host log is compile-time only. It is intentionally not
cherry-picked.

#### The zero half-resolution predecessor has no observed writer

The ordered boundary run
`artifacts/game-runs/astro/20260729-102557-corpus-gate/attempt-01.log`
records the first mode-6 use of `0x537060000` with `writer=none`,
`backend_known=0/2`, and `NeedsDecode location=none`. Its 2432x1368 RGBA16F
guest upload and GPU readback are both exactly zero
(`nonblack_pixels=0/3326976`, hash `0x3DA5831AA90CA325`). A later
guest-memory-backed occurrence is identically zero and still has
`writer=none`. Therefore mode 6 is not discarding a live nonblack host image at
this boundary; faithful DCC decompression would still produce black. The
missing writer is earlier.

A same-draw logo-path pair in
`artifacts/game-runs/astro/20260729-102808-corpus-gate/attempt-01.log` proves
that `ps=0x500645400` executes and preserves the 64 nonblack source pixels from
`0x514080000` into `0x53AD00000`. The later scene-state pair with the known
~869,979-pixel source remains unmeasured: synchronous per-candidate readback in
`20260729-103709-corpus-gate` perturbed boot enough that the 480-second gate
reached only LOGO. Do not treat that absence as evidence that the scene state
ceased to exist.
# 2026-07-29: completed frames were evicted before presentation

The black window and the black render targets are separate facts. Exact
readback in `20260729-113820-corpus-gate` proved that the active 1920x1080 HDR
scene target `0x514080000` contained 1,195,139 nonblack pixels after writer
3440. The old deferred readback was not reliable enough to place that content
at a particular draw, but this synchronous writer-ordinal capture is.

The window nevertheless presented only the cleared registration frame. The
bounded `_pendingGuestImagePresentations` queue retained four flips by
discarding its oldest entry on overflow. With Astro hundreds of guest work
items ahead of the renderer, that oldest flip was the only completed one.
Every enqueue discarded it and retained four newer, incomplete snapshots, so
`TryTakePresentation` could never consume another guest frame.

The queue now preserves its head plus the newest bounded tail. The clean boot
`20260729-121741-corpus-gate` immediately reported live `FLIP` rates (about
3.1 FPS during early boot and 1.1 FPS at title) instead of a single frozen
guest frame. This fixes presentation starvation, not the remaining black RGB:
PrintWindow capture
`%TEMP%\shot-122408-23772-6226878.png` at
`LevelDocument Loaded: worldmap` is still black apart from the SharpEmu HUD.

The remaining measured color boundary is now narrow:

- `ps=0x50063F800` is the final full-viewport RGB writer to
  `0x514080000`; it samples `0x53AA00000` and preserves the existing alpha.
- Its half-resolution input is black, so it overwrites a previously nonblack
  full-resolution scene with black RGB.
- The final writer to `0x53AA00000` is the large lighting shader
  `ps=0x500126600`. Its primary 960x540 input is `0x53A500000`.
- `0x514080000` is created once, never recreated or retained as a format
  variant, uses guest format `0x80000C07` throughout, and all relevant work is
  ordered on `dcb.graphics`. Format-alias loss and cross-queue reordering are
  therefore eliminated.

Do not use `20260728-171313-corpus-gate` as a healthy worldmap-start control.
It also loads worldmap, never starts it, and times out after the same
AudioOut2-dominated loop. The worldmap predicate stall remains separate from
the GPU color boundary.

Later SDK-grounded correction: `0x53A500000` in that lighting draw is the
R32-float hierarchical-depth level produced from DB surface `0x513560000`, not
a color G-buffer. The word "black" above describes raw guest-byte probes, not
an exact readback of the live host image. Do not infer a failed depth producer
from those probes.
