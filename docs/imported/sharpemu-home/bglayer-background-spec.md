# PS5 home-screen animated background: implementation spec and completeness audit

Companion to `docs/bglayer-shaders.md` (GPU side, `tools/shader-recon`) and to
`tools/bg-recon` (host side). This document does three things:

1. audits every constant recovered so far for whether it is actually sourced;
2. settles the wave-vs-particle question;
3. states, bluntly, what can and cannot be built without inventing anything,
   and writes the spec for the part that can.

**Rule this document is written under:** a number that cannot be read out of
Sony's data is reported UNRECOVERED and is not filled in. Every constant below
carries a firmware version and a file, and an offset wherever an offset exists.

## Sources

| tag | meaning |
|---|---|
| **E403** | firmware 4.03, `system_ex/app/NPXS40087/eboot.bin`, 16,449,968 bytes. File offsets. `file = vaddr + 0x4000` for every `PT_LOAD` in this image. |
| **A403** | firmware 4.03, `system_ex/vsh_asset/` |
| **M<ver>** | the managed `Sce.Vsh.ShellUI.BGLayer.dll` carved out of `Sce.Vsh.ShellUI.BGLayer.dll.sprx` of that firmware version, decompiled with ilspycmd |
| **DB** | the 58-version `system/system_ex` extraction database |

---

## 0. Verdict, first

**The animated background cannot be built faithfully today.** One block is
missing and it is the block that decides what the animation looks like: the
numeric parameters of the particle simulation (`particle_c`). They are not
literals anywhere in the image, they are not in the managed layer in any of
58 firmware versions, and they are not in any asset file. They are host-code
`.rodata` floats reached through two levels of runtime-allocated structs.

Everything else is recovered: the shader set, the compute/vertex/pixel
contract, the struct layouts, the dispatch geometry, the per-frame state
machine, the boot timings, the plate algorithm.

The missing block **is present in the 4.03 `eboot.bin` already on disk**. It is
a bounded reverse-engineering job (one struct constructor), not a missing-data
problem. It is not in the 58-version database and never will be.

---

## 1. Constant audit: where the invention risk is

### 1a. Fully sourced (version + file + offset, independently re-derivable)

These were re-verified for this document by reading the bytes again.

| constant | value | source |
|---|---|---|
| `particle_p` embedded colours | `(0.693767, 0.459286, 0.204934)`, `(0.420054, 0.187302, 0.075132)`, `(0.501961, 0.329412, 0.211765)` = `(128,84,54)/255` | E403 `0xda7fd0`, 12 floats, last 3 are zero padding |
| billboard corner table | `(-1,-1) (1,-1) (-1,1) (1,-1) (-1,1) (1,1)` | E403 `0xda94c0`, `float2[6]` |
| `wave_bg_p` embedded constant buffer | `151,160,137,91,90,15,131,13,...` stored as floats, a permutation of `{0..255}` — Perlin's canonical hash permutation, **not** a colour ramp | E403 `0xd85a70`, 1024 B |
| particle shader blobs | `particle_c` `0xd9f1b0`, `particle_p` `0xda78d0`, `particle_vv` `0xda8ec0`, `large_particle_p` `0xdaa420`, `large_particle_vv` `0xdabbc0` | E403 descriptor table at vaddr `0xcc9c40`, stride `0x18`, `{name, blob, size}`; each blob vaddr `+0x4000` = the file offset above |
| texture paths | `/system_ex/vsh_asset/Sce.Vsh.ShellUI.BGLayer.Particle0.gnf`, `...Particle1.gnf` | E403 `0xb1a588`, `0xb273f9` |
| the two textures exist and are the only BGLayer assets | 167,936 bytes each | A403 |
| `wave0.fbxd` / `wave1.fbxd` path strings | `/app0/wave/wave0.fbxd`, `/app0/wave/wave1.fbxd` | E403 `0xb38a04`, `0xb118fb` — strings only, **no such directory exists in the dump and no `.fbxd` file exists anywhere in it** |
| `simulateParticles` entry | vaddr `0x96640` / file `0x9a640` | E403; identified by its own assert strings at `0x9680d` and `0x96829` referencing `"simulateParticles"` (`0xb2fb5e`) with source lines `0x2c4` and `0x2c7` |
| compute threadgroup size | **64** | E403 `0x967cc`: `groups = (Resources[0x28] + 0x3f) >> 6`, dispatch `(groups, 1, 1)` |
| particle count location | `Resources + 0x28`, `uint32` | E403 `0x9667e...0x9667f`, `0x9667c` gate `if (Resources[0x28] == 0) return`, and `0x9667cc` |
| `ResourcesCs` block size | `0xF8` bytes, copied CPU->GPU wholesale each dispatch | E403 `0x9666e5`-`0x096746`, eight `vmovups ymm` pairs covering `+0x00..+0xF8` |
| `timeRateForLifeCountDown` | `1.0f` when `ctx[0x90] == instanceIndex`, else `Resources[0x3c] * 0.15384616f` (= `float(1/6.5)`) | E403 `0x9679a` (`1.0f` at file `0xb03918`), `0x967ac` (`0.15384616f` at file `0xb0398c`) |
| `SRTCs.transPatternFlag` packing | low nibble = instance index (0/1); high nibble = `ctx[0x90] & 0xF` | E403 `0x966dc`, `0x96755`-`0x9677c` |
| `SRTCs` field writes | `+0x00` Resources ptr, `+0x08` time, `+0x0c` timeStep, `+0x10` timeRate, `+0x14` isPreSimulation, `+0x18` transPatternFlag | E403 `0x966e2`, `0x96788`, `0x9678d`, `0x967c2`, `0x96792`, `0x9677c` |
| dispatch driver | vaddr `0x96860` / file `0x9a860` | E403 |
| systems per instance | **10** — eight in a pointer array at `ctx + inst*320 + 0x198` (loop index 51..58 x 8 bytes) plus two singletons at `ctx + inst*80 + 0x5d8` and `+0x5e0` | E403 `0x968eb`-`0x96909`, `0x96984`, `0x969f4` |
| instances | **2**, index 0/1, each with its own time base `ctx[0x6d8 + i*4]` and threshold `ctx[0x6e0 + i*4]` | E403 `0x968c4`-`0x968e2`, `0x96959` |
| particle buffer ping-pong | selected by `ctx[0x708] & 1`; the two buffer arrays are `0x80` apart and the phase stride is `0x40` | E403 `0x96912`, `0x9693a`-`0x96949` |
| managed icall table | `{const char *name, void *impl}` pairs; `SetDebugParameterNative` name at `0xb495d1`, slot vaddr `0xcc8c80`, impl vaddr `0x8e1d0` | E403 via `.rela.dyn` |
| **`SetDebugParameterNative` is dead in retail** | impl `0x8e1d0` fans out to three sinks, `0x73880`, `0xd5fd0`, `0x93840` — **all three are a bare `ret`** | E403 |
| `WaveColourPreset` | 22 entries, `HomeScreen = 4`, `NoWave = 5`, byte-identical across versions incl. the `[Obsolete]` PS4 leftovers | M1.12, M4.03, M6.50 `WaveColourPreset.cs` |
| `LightRenderModeIndex` | 65 NoParticle, 66 InitialWelcomeNoParticle, 67 Bottom, 68 Spread, 69 ColdBoot, 70 WarmBoot, 71 InitialBoot, 78 Black, 79 None | M6.50 `BackgroundLayer.cs:53-63`; M1.12 `:77-86` (79 absent from the 1.12 enum but assigned at M1.12 `:549`) |
| `GlobalBackgroundState` -> `ThemeColourIndex` map | table in §4 | M6.50 `BackgroundLayer.cs:1754-1834`; identical values M1.12 `:1429-1506` |
| `waveOpacity` integrator | init `1f`; `ShowWave == false` -> `*= 0.9f`; `true` -> `min(1f, +0.01f)` | M6.50 `BackgroundLayer.cs:775-782`; identical M1.12, M4.03 |
| `ColdBootDurationTick` / `WarmBootDurationTick` | 60,000,000 (6.000 s) / 30,000,000 (3.000 s) | M1.12, M4.03, M6.50 `BackgroundLayer.cs` |
| `BasematDefaultColor` | `(0.00784, 0.01568, 0.03137)` = `(2,4,8)/255` | M1.12, M4.03, M6.50 `BGTransition.cs` |
| `BasematAnimationDuration` | **300 ms in 1.12/2.x, 1000 ms in 4.03/6.50** (the 3.x boundary is not narrowed) | M`BGTransition.cs` |
| `InitialWelcomeScreenFadeOut` | 1333.3334 ms | M1.12/M4.03/M6.50 `BackgroundLayer.UpdateGlobalBGState` |
| `BGLayerPlugin.ShowWave(bool)` and `SetPresetColour(...)` are empty method bodies | in **1.12, 2.00, 3.00, 4.03, 5.00, 6.00, 6.50** — every version whose managed assembly still contains IL | M, `BGLayerPlugin.cs` |
| from **7.00** the managed assembly is a reference assembly | every method body is `ret`; 336-368 ILSpy reference-assembly markers per version at 7.00/8.00/9.00/10.00/10.20/11.20/13.20, 0 for 1.12-6.50; exactly one PE in the sprx, so this is not a carving error | DB |
| DB is libraries only | 38,047 files across all 58 versions, exactly five extensions: `.sprx` 33,789, `.bin` 1,613, `.elf` 1,049, `.self` 832, `.prx` 764. Zero `.dds`, `.gnf`, `.fbxd`, `.mp4`, `.at9`, `.rco`, `.json`, `.xml`. No `vsh_asset`, no `app/NPXS*`, no `eboot.bin`. | DB, full recursive census, re-run independently for this document |
| no particle parameter name reaches host `.rodata` | `particleMinLife`, `particleMaxLife`, `blurRadiusPowerFactor`, `particleSpawnRange*`, `particleMaxAcceleration*`, `particleMaxRotationSpeed`, `particleCurl*`, `unblurMinSize`, `unblurMaxSize`, `ParticleProperty`, `ParticleBlendParam`, `LightSourceProperty`, `bgColor0`, `bgCurve`, `uCoff0`, `uLightColor`, `uCenterPos` — **every occurrence is inside an embedded AMDGPU ELF's reflection table; host-`.rodata` count is zero for all of them** | E403 via `tools/bg-recon/strhits.py` |

### 1b. Sourced to a shader but without a cited instruction offset

These came out of ISA reading. They are almost certainly right — the two that
were spot-checkable (`particle_p` colours, the corner table) verified exactly —
but the listing offset was not written down, so **re-derive before shipping**.
Treat as provisional, not as invention.

- `ParticleProperty`, 0x44 B: `pos@0x00, blurBoundary@0x0c, vel@0x10, fore@0x1c, transPatternFlag@0x28, right@0x2c, curLife@0x38, maxLife@0x3c, renLife@0x40`. Cross-validated twice (loads in `particle_vv`, stores in `particle_c`).
- `ParticleBlendParam`, 32 B: `center@0x00, weight@0x0c, beginDist@0x18, endDist@0x1c`.
- `SRTVsPs` / `SRTLargeParticleVsPs`: `Resources*@0x00, time@0x08, timeStep@0x0c, transPatternFlag@0x10`.
- `ResourcesVsPs` through `+0x64`; `ResourcesLargeParticleVsPs` through `+0x70`, with the two texture descriptors at `+0x20` and `+0x40`.
- 6 vertices per particle; three cull tests including a 4-bit `transPatternFlag` match; life latch `if (renLife < 0) renLife = curLife`.
- Park/Miller RNG: `seed = 16807 * seed % 2147483647; r = seed % 1000` (literals `0x41a7`, `0x7fffffff`, magic-divide `0x10624dd3` + `>>6`), feeding `size = unblurMinSize + (r / 1000) * (unblurMaxSize - unblurMinSize)`.
- Blend curves are `t*t*(3-2t)`.
- `wave_bg_p` cbuffer, 128 B: `uCoff0`, `uCoff1`, `uLightColor`, `uLightPos`, `uCenterPos`; ramp sampled by `uv.y`; specular `pow(max(0, -dot(V, L)), exp2(10 * uLightColor.w + 2)) * uLightPos.w`; two-tap hash dither at `1/510` (`0x3b008081`); everything scaled by `uCenterPos.w`.
- `particle_p` contains no `image_sample` at all.

### 1c. Reported without any source — the actual invention risk

Only one class of statement falls here, and it is interpretive rather than
numeric:

- **Role assignments for the three `particle_p` embedded colours.** The bytes
  are sourced (E403 `0xda7fd0`); which one is the core, which the rim and which
  the additive tint is **not**, and no offset was given for the arithmetic that
  consumes them. Do not assign roles until the `particle_p` listing is re-read.
- **The meaning of instance index 0/1 and of `ctx[0x6e0 + i*4]`.** The values
  and the control flow are sourced (§1a); calling instance 0/1 "a crossfade
  pair keyed to `Particle0.gnf` / `Particle1.gnf`" is an *inference* from the
  `transPatternFlag` nibble and the two-texture asset set. Marked as inference
  everywhere below.
- Everything else previously reported carries at least a version and a file.

### 1d. Claims that were checked and are wrong

- The three candidate offsets for the M1 plate uniforms are all wrong.
  `0xbd0ed0` is a smooth monotonic transfer LUT (starts `0.943949, 0.944277`);
  `0xebfe00` and `0xebfea0` are **all zero in the file image** — runtime-written
  storage, not data.
- `bgColor0` / `bgColor1` / `bgCurve` are not in `wave_bg_p`'s cbuffer. They
  belong to the `Constants` buffer of `dual_wave_*`. The original M1 statement
  conflated two shaders.
- The task brief's database path does not exist verbatim; the extracted tree is
  at `C:\Users\sharpemu\Downloads\system_system_ex_database.part\system_system_ex_database\<major>\Firmware <ver>\...`.
  (The `.rar` parts are still beside it; the extraction is present and complete.)

---

## 2. Wave vs particle: settled. It is particles.

The user's hypothesis is confirmed, and the managed history strengthens it into
a stronger claim than "PS5 replaced the wave".

GPU-side evidence (E403):

1. `particle_c` exists and is a 29,760-byte compute shader — the largest in the
   entire eboot.
2. The five `particle*` shaders are the **only** shaders in the eboot whose
   reflection structs are namespaced `BackgroundLayer::`. Nothing `wave*` is.
3. `large_particle_p` samples exactly two 2D textures, `backgroundTex` /
   `backgroundTex2`, which are `Particle0.gnf` / `Particle1.gnf` — the only two
   BGLayer assets that exist.
4. Nothing consumes `wave0.fbxd` / `wave1.fbxd`: only the two path strings
   exist, there is no `wave/` directory under `NPXS40087`, and there is no
   `.fbxd` file anywhere in the dump.
5. `wave_bg_vv` has 0 input and 0 output semantics and `wave_bg_p` has 0 inputs:
   a fullscreen plate, not a mesh.

Host-side evidence added here (E403): `simulateParticles` is a real, live,
per-frame function with 20 dispatches per frame (2 instances x 10 systems). No
comparable wave simulation entry point exists.

Managed-history evidence (DB, 58 versions):

6. `GlobalBackgroundState` already contained `ParticleBottom`, `ParticleSpread`
   and `NoParticle` in **1.12** — launch firmware — with the same numeric
   values 9/10/11 it has in 4.03 and 6.50. `LightParticleFlag`,
   `LightRenderModeIndex.NoParticle/InitialWelcomeNoParticle/Bottom/Spread` and
   `enableParticleDebugPad` are all present in 1.12 too.
7. Nothing named for wave motion ever had an implementation: `ShowWave` and
   `SetPresetColour` are empty in all seven versions that still contain IL,
   `WaveColourPreset` did not change a single member across the whole history,
   and `MaskWave` is a boolean gate with no parameters.

**Conclusion: it was particles from day one.** The `wave*` naming is vestigial
PS4-era API surface carried forward — `WaveColourPreset`, `ShowWave`,
`WaveOpacity`, `WaveGpuTime`, `MaskWave`. There was never a PS5 wave
implementation to replace.

**M3 is retired. The missing `wave0.fbxd` / `wave1.fbxd` mesh blocks nothing.**
What `wave_bg_p` still matters for is the static gradient plate the particles
are drawn over, and that is a fullscreen pass with no mesh input.

---

## 3. Completeness verdict

**No. Not with zero invention.** Exactly one thing is missing:

### Missing: the `particle_c` simulation parameters (M5)

`particleMinLife`, `particleMaxLife`, `particleSpawnRange*`,
`particleMaxAcceleration1`, `particleMaxRotationSpeed`, `blurRadiusPowerFactor`,
`unblurMinSize`, `unblurMaxSize`, the four `particleCurl*`, the per-system
particle count, and the `ParticleBlendParam` attractor set.

What is known about where they are, all measured:

- They are **not** literals in the eboot instruction stream. A scan of every
  executable `PT_LOAD` for `mov dword ptr [reg+disp], imm32` whose immediate
  decodes to a float in `(1e-4, 1e5)` found exactly one cluster in the whole
  16 MB image, and it is not this struct. Release codegen loads these from
  `.rodata` with `vmovss xmm, [rip+disp]`.
- They are **not** keyed by name on the host: every one of those names occurs
  only inside shader reflection tables, host-`.rodata` count zero (§1a).
- They are **not** reachable through the debug channel:
  `SetDebugParameterNative`'s three sinks are all a bare `ret` in retail 4.03.
  The index namespace is not merely unrecovered — the whole path is compiled
  out. Stop looking there.
- They **are** in a runtime-allocated `ResourcesCs` block, `0xF8` bytes, one per
  system, held in the pointer arrays at `ctx + inst*320 + 0x198` (8 systems) and
  `ctx + inst*80 + 0x5d8/0x5e0` (2 more). `simulateParticles` copies that block
  verbatim into GPU memory and points the SRT at it. So the values are written
  by whatever constructs those systems.

**Is any of it in the 58-version database? No — and it cannot be.** The
database contains `.sprx` / `.prx` / `.elf` / `.self` / `.bin` only; there is no
`app/NPXS40087` tree and therefore no `eboot.bin` in any of the 58 versions.
Separately, the managed BGLayer assembly — the only BGLayer artifact the
database does contain — has never held a particle parameter in any version, and
from 7.00 onward it holds no method bodies at all. The database is a dead end
for M5. Its value was the managed history in §2 and §4, and that value is now
extracted.

### The route that is open

The 4.03 `eboot.bin` on disk contains the values. The job is: find the
constructor that fills a `ResourcesCs`, by walking up from the two pointer
arrays at `ctx+0x198` / `ctx+0x5d8`, and read its `vmovss [rip+...]` constants
with `tools/bg-recon/ebdis.py`. That is one struct constructor. Bounded,
mechanical, and it needs no data that is not already on disk.

A cross-check exists: a second `NPXS40087/eboot.bin` (21,695,212 bytes) is
present in the 12.40 system dump, so any recovered constant can be confirmed
against a second firmware generation.

### What is also still open, smaller

- `LightSourceProperty` field offsets — names only. UNRECOVERED.
- M1 plate **runtime** uniform values (`uCoff0/1`, `uLightColor`, `uLightPos`,
  `uCenterPos`). The layout and the algorithm are recovered; the values are
  written at runtime by the same class of host code. UNRECOVERED.
- The role of instance index 0/1 (inference only, §1c).

---

## 4. Implementation spec

Everything in this section is marked **[M]** measured or **[U]** UNRECOVERED.
An **[I]** item is an inference from measured facts and is flagged as such.

### 4.1 Per-frame managed state machine

**[M]** Once per UI frame the shell fills a single `BackgroundLayerState` and
calls `UpdateNative`, then reads four fields back.

Managed -> native: `Opacity`, `WaveOpacity`, `PresetColourIndex`,
`ThemeColourIndex`, `CurrentArea = 0`, `FrameMilliseconds` (UI frame tick, ms),
`FrameRealMilliseconds` (elapsed, ms), `RenderingMode`, `Morpheus`, `PowerState`
(6.50+), `HighContrast`, `BgSceneVisibility`, `DisplayWidth`, `DisplayHeight`,
`DisplayBufferColorFormate`, `LightFlag`, `DrawSomething`.
Native -> managed: `TransitionType`, `LightFlag`, `Opacity`,
`Morpheus.ModeFlags`. (M6.50 `BackgroundLayer.cs:894`, `:898`.)

**[M]** `PresetColourIndex` is set to `4u` (`HomeScreen`) once in `Start()` and
never changes for the life of the session. It is not an animation selector.

**[M]** `ThemeColourIndex` **is** the animation selector. It is a
`LightRenderModeIndex`:

| `GlobalBackgroundState` | `ThemeColourIndex` | side effects |
|---|---|---|
| None (0) | 79 None | |
| Black (1) | 78 Black | |
| ColdBootAnimation (2) | 69 ColdBoot | plays `sfx_coldboot.at9`; `LightFlag \|= PauseParticle` |
| WarmBootAnimation (3) | 70 WarmBoot | plays `sfx_warmboot.at9`; `LightFlag \|= PauseParticle` |
| InitialBootAnimation (4) | 78 Black | spawns the InitialBoot video player |
| InitialSetup (5) | 67 Bottom | |
| InitialWelcomeScreenAnimation (6) | 67 Bottom | `sfx_transition.at9`; next state NoParticle |
| InitialWelcomeScreenFadeOutAnimation (7) | 66 InitialWelcomeNoParticle | next state NoParticle |
| Login (8) | 67 Bottom | |
| ParticleBottom (9) | 67 Bottom | |
| ParticleSpread (10) | 68 Spread | |
| **NoParticle (11)** | **65 NoParticle** | BgmState = Home |
| Shutdown (12) | 68 Spread | BgmState = Mute |
| FadeOutShutdownAnimation (13) | 78 Black | next state Black |

`InitialBootPlayer.PlayerState.LightStart` sets `ThemeColourIndex = 71`
(`InitialBoot`). (M6.50 `:1754-1834`, `:1855`; identical values M1.12
`:1429-1506`.)

**The steady-state home screen is `NoParticle` / 65.** That is Sony's own name
for it. `Bottom` (67) and `Spread` (68) are the *transition* motions used for
login/setup and shutdown. **[U]** what `Bottom` and `Spread` do geometrically.

**[M]** `LightParticleFlag`: `None = 0, IsReady = 1, PauseParticle = 2`,
unchanged 1.12 -> 13.20. Round-tripped through `state.LightFlag` every frame:
native sets bit 0, managed sets/clears bit 1. Bit 1 is set on entering Cold or
Warm boot animation and cleared on every `SetGlobalBGState`, on boot-animation
start, and on boot-animation renderer timeout.

**[M]** `WaveOpacity` integrator, unchanged 1.12 -> 6.50
(`BackgroundLayer.cs:775-782`):

```
waveOpacity = 1.0                        // at init
per frame: if (!ShowWave) waveOpacity *= 0.9
           else           waveOpacity  = min(1.0, waveOpacity + 0.01)
```

`BackgroundLayer.ShowWave` is an `internal bool` property set `true` in
`Start()` — distinct from the empty `BGLayerPlugin.ShowWave(bool)` method.
6.50 adds `VisibleOpacityThreshold = 0.0001f` (`BackgroundLayer.cs:74`).

**[M]** Boot timings: `ColdBootDurationTick` 60,000,000 (6.000 s),
`WarmBootDurationTick` 30,000,000 (3.000 s), `BootAnimTimeout` 15000 ms,
`ColdBootWaitCount` = `WarmBootWaitCount` = 0, `InitialWelcomeScreenFadeOut`
1333.3334 ms (= 80 frames at 60 Hz), InitialBoot timeout 30000 ms with
early-exit 1100 ms, `FadeOutShutdown` 3000 ms. All identical 1.12 -> 6.50.

**[M]** Basemat: type in `{None, Flat, Linear, EllipseNarrow}` (`EllipseWide`
existed up to 3.21 and was removed by 4.03); default colour `(2,4,8)/255`;
animation duration 300 ms in 1.12/2.x, 1000 ms in 4.03/6.50.

**[M]** The focus-rect effect is sent only when `(ThemeColourIndex & 0xF0) != 32`;
otherwise the last cached rect is replayed.

### 4.2 Per-frame GPU work

**[M]** Per frame, per instance `i` in `{0, 1}`, host code
(`simulateParticles` driver at E403 vaddr `0x96860`) walks 10 particle systems:
eight from the pointer array at `ctx + i*320 + 0x198`, then two singletons at
`ctx + i*80 + 0x5d8` and `+0x5e0`. A system with a null pointer is skipped.

**[M]** Gate: the systems for instance `i` are dispatched only while
`time + timeStep <= ctx[0x6e0 + i*4]`.

**[M]** Each dispatch is `simulateParticles(ctx, Resources, bufA, bufB, i,
isPreSimulation, time, timeStep)` where:

- `time` passed in is `ctx[0x700] - ctx[0x6d8 + (ctx[0x90] & 1) * 4]`
  (wall time minus a per-pattern time base) and `timeStep` is `ctx[0x704]`;
- `bufA` / `bufB` are the ping-pong `ParticleProperty` buffers, selected by
  `ctx[0x708] & 1` (phase stride `0x40`, the two arrays `0x80` apart);
- the `0xF8`-byte `ResourcesCs` master is copied verbatim into GPU-visible
  memory and `SRTCs.Resources*` points at the copy;
- `SRTCs.time = time + ctx[0x7b0]`, `SRTCs.timeStep = timeStep`;
- `SRTCs.timeRateForLifeCountDown = 1.0f` if `ctx[0x90] == i`, else
  `Resources[0x3c] * float(1/6.5)`;
- `SRTCs.transPatternFlag` low nibble `= i`, high nibble `= ctx[0x90] & 0xF`;
- dispatch is `((particleCount + 63) >> 6, 1, 1)` with **64 threads per group**,
  `particleCount = Resources[0x28]`;
- a dirty flag `ctx[0x719]` is set after each dispatch.

**[I]** The pairing of two instances, two time bases, two thresholds, a
`transPatternFlag` nibble that the vertex shader tests, and exactly two
textures reads as a crossfade between two particle patterns. Stated as
inference; the control flow is measured, the interpretation is not.

**[M]** Render, per particle system: `particle_vv` + `particle_p` for the small
dust, `large_particle_vv` + `large_particle_p` for the large motes.

**[M]** `particle_vv` expands **6 vertices per particle** into a billboard quad
using the corner table at E403 `0xda94c0`
(`(-1,-1) (1,-1) (-1,1) (1,-1) (-1,1) (1,1)` — two triangles, not a strip).
It applies three cull tests, one of which is a 4-bit match against
`transPatternFlag`, and latches life with `if (renLife < 0) renLife = curLife`.

**[M]** Size comes from a Park/Miller minimal-standard RNG:
`seed = 16807 * seed % 2147483647`, `r = seed % 1000`,
`size = unblurMinSize + (r / 1000) * (unblurMaxSize - unblurMinSize)`.
**[U]** `unblurMinSize`, `unblurMaxSize`.

**[M]** `particle_p` contains **no `image_sample` instruction at all** — the
small dust is procedural. Only `large_particle_p` samples textures, and it
samples exactly two: `backgroundTex` = `Particle0.gnf`, `backgroundTex2` =
`Particle1.gnf`.

**[M]** `particle_p`'s embedded constants (E403 `0xda7fd0`) are three float3
colours: `(0.693767, 0.459286, 0.204934)`, `(0.420054, 0.187302, 0.075132)`,
`(0.501961, 0.329412, 0.211765)` = `(128, 84, 54)/255`. **[U]** their roles.

### 4.3 Composition order

**[M]** Bottom to top: the `wave_bg_p` fullscreen gradient plate, then the
particle systems, then anti-aliasing. The eboot carries `fxaa2_p/_vv`,
`wave_fxaa_p/_vv`, `fw_fxaa_p` and a full SMAA set
(`smaa_edge_*`, `smaa_weight_*`, `smaa_blend_*`).

**[U]** **Which** AA path the background actually runs, and where in the frame.
Do not pick one by taste — it is decided by host code that has not been read.

**[U]** Blend state for the particle passes (src/dst factors, equation,
depth/stencil). Not yet read out of the AGC state that `simulateParticles`'s
render sibling sets. Given `particle_p` emits three colours and no texture, an
additive or premultiplied path is likely — **that is a guess and must not be
shipped as a value.**

### 4.4 The plate: `wave_bg_p` (M1)

**[M]** Fullscreen, no mesh: `wave_bg_vv` has 0 in / 0 out semantics and
`wave_bg_p` has 0 inputs.

**[M]** 128-byte cbuffer: `uCoff0`, `uCoff1`, `uLightColor`, `uLightPos`,
`uCenterPos`. Algorithm: a 1-D ramp sampled by `uv.y`; plus specular
`pow(max(0, -dot(V, L)), exp2(10 * uLightColor.w + 2)) * uLightPos.w`; plus a
two-tap hash dither at `1/510` (`0x3b008081`); the whole thing scaled by
`uCenterPos.w`.

**[M]** Its 1024-byte embedded constant buffer at E403 `0xd85a70` is Perlin's
canonical hash permutation table stored as floats — a noise permutation, **not**
a colour ramp. Do not use it as a gradient.

**[U]** All five uniform runtime values.

**[M]** `bgColor0` / `bgColor1` / `bgCurve` belong to `dual_wave_*`'s
`Constants` buffer, not to `wave_bg_p`. **[U]** their values.

### 4.5 Assets

**[M]** `/system_ex/vsh_asset/Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and
`...Particle1.gnf`, 167,936 bytes each. These are the only BGLayer assets that
exist. The only other asset paths anywhere in managed BGLayer are the `.at9`
SFX, the two boot `.mp4`s, and `/system_ex/vsh_asset/bg_image_load_error.dds`.

---

## 5. Do-not-invent list

If you are implementing this, these are the values you must not make up. Each
one is a hole with a known shape:

1. Every `particle_c` simulation parameter (§3).
2. Particle count per system.
3. `unblurMinSize` / `unblurMaxSize`.
4. The roles of the three `particle_p` colours.
5. Blend state and AA path for the particle passes.
6. All five `wave_bg_p` uniform values, and `dual_wave_*`'s `bgColor0/1` and
   `bgCurve`.
7. `LightSourceProperty` field offsets.
8. The geometry of `Bottom` (67) and `Spread` (68).

Shipping a plausible-looking number for any of these reproduces exactly the
failure that made the previous background wrong.

## 6. Reproducing every measurement here

```
# GPU side
python tools/shader-recon/scan_elfs.py <eboot.bin> out/ > elfs.json
python tools/shader-recon/table.py     out/            > table.json
python tools/shader-recon/names.py                     > names.json
python tools/shader-recon/isadis.py    out/<off>.elf   > dis.txt

# host side
python tools/bg-recon/strhits.py <eboot.bin> particleMinLife SetDebugParameterNative
python tools/bg-recon/xrefs.py   <eboot.bin> slot 0xb455d1
python tools/bg-recon/xrefs.py   <eboot.bin> data 0xb2fb5e
python tools/bg-recon/xrefs.py   <eboot.bin> call 0x96640
python tools/bg-recon/ebdis.py   <eboot.bin> 0x96640 0x220

# managed side
#   carve the single MZ+PE out of Sce.Vsh.ShellUI.BGLayer.dll.sprx and run
#   ilspycmd 10.1.1 under DOTNET_ROOT=C:\dotnet (10.0.10; it fails on 8.0.1).
```

No dump-derived bytes are committed to this repository. The offsets above are
coordinates into files that stay where they are, read-only.
