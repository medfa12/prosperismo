<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell background - the native renderer

`docs/ps5-background.md` records the *managed* background layer: the state
machine, the enums, the one opacity it pushes per frame. It ends where the
managed code ends, with the honest note that "the wave itself is native" and
that the palettes, geometry and motion "live in the native shader" and "cannot
be matched".

This document is that native side, read out of the firmware with Ghidra. It
confirms most of the managed view, corrects two claims, and replaces the largest
"APPROXIMATED" row in that table with measured numbers.

> **2026-08-01 correction.** The earlier mesh-first conclusion below conflated
> dormant `Wave` code with the live PS5 background owner. Shader namespace and
> call-path analysis settles the visible moving background as the
> `BackgroundLayer::` compute particle system over Plane2's full-screen
> `wave_bg_p` plate. `wave0.fbxd` / `wave1.fbxd` are not required by the live
> 4.03 path. Plane2's runtime uniform writer, record map, and per-draw noise
> phase and the steady Home/Settings record route are now recovered below.
> Historical mesh details remain only because they
> accurately describe dormant code in the binary.

**Values/behaviour only.** Nothing below is Sony source. No code is reproduced.
What is recorded is: which module and which function/address a fact came from,
the numeric tables the renderer builds its colours from, and the shape of the
GPU interfaces (uniform and structure member names, which are interface, not
implementation). The firmware tree it was read from is gitignored and nothing
from it is committed.

## Provenance and method

| | |
| - | - |
| Tool | Ghidra 12.1.2 PUBLIC, headless (`analyzeHeadless`), on a JDK 21. Installed to a scratch directory outside the repository. |
| Loader | Ghidra's stock ELF loader. It accepts a PS5 4.03 module as-is: `EI_OSABI = 0x09`, `e_shnum == 0`, Sony's `PT_DYNAMIC` tags. No raw-binary fallback and no manual program-header work was needed. It resolves nothing external (3523 unresolved imports) but that does not matter for the code that matters here. |
| Target | `filesystems/system_ex/app/NPXS40087/eboot.bin`, 16,449,968 bytes, FreeBSD ELF64 x86-64, module name `eboot`. Full auto-analysis took 632 s. |
| Addresses | Every address below is a **module virtual address**, i.e. what Ghidra shows. The first `PT_LOAD` is `off=0x4000 va=0`, and every other loaded segment keeps the same delta, so `file offset = va + 0x4000` throughout. |

### Finding the module

The managed assembly is a Mono AOT image (`Sce.Vsh.ShellUI.BGLayer.dll.sprx`)
whose only `DT_SCE_NEEDED_MODULE` entries are `libkernel` and
`libSceLibcInternal` - it imports *nothing* graphical. Its native entry points
are therefore not a P/Invoke into a named library but **Mono internal calls**
registered by the host process. The class that declares them is `BGLayerNative`,
and the host that registers them is the ShellUI application itself: `eboot.bin`
carries the registration strings (e.g.
`Sce.Vsh.ShellUI.BGLayer.BGLayerNative::SetClearColorNative(uint)` at
`0xb00809`) and it is the only file in the dump that does.

The same binary carries the build's own source paths, which name the subsystem:
`vsh\shell\shell_ui\src\background_layer\...`. **The native background renderer
is inside the ShellUI process, not in a shared `libSce*` module.** That is a
correction to the assumption in the task brief; there is no `libSceVshCommon` or
`BGLayerNative` shared object.

## The scene graph

The layer's drawable nodes all pass a name to a common base constructor at
`0x000dd030`. Enumerating its callers gives the complete node list - eight nodes,
no more:

| Node | Constructor | Kind arg | What it owns |
| ---- | ----------- | -------- | ------------ |
| `Wave` | `0x000d6d10` | 2 | The preset-driven wave. Builds the wave ramp textures, loads the wave models. |
| `FirstWave` | `0x0007e6b0` | 2 | A second, tessellated, order-independent-transparency wave. |
| `DualWave` | `0x00073ea0` | 1 | A two-instance wave with hard-coded parameters. |
| `LightParticle` | `0x000938d0` | 2 | The light-particle system. |
| `ParticleBoids` | `0x0009ec20` | 2 | A flocking particle variant. |
| `Light` | `0x0009de50` | 0 | A light source. |
| `Plane2` | `0x000a2350` | 0 | The background plate. Builds the background ramp textures. |
| `Transition` | `0x000a38d0` | 0 | The image transition. |

## Transition node — custom-image clock, HOME caller and opaque slide recovered

The managed `BGLayerNative.SetBGTransitionNative` binding reaches the PSM thunk
at `0x0008e5d0`, wrapper `0x000709f0`, and native owner setup at `0x000a6980`.
For custom-image ABI types 6 through 10, the owner extracts degree from bits
16..23, clamps it to 0..3, and computes the duration as:

```
duration_ms = 300.0 + degree * 166.6666717529297
```

That yields 300, 466.6667, 633.3333, and 800 ms (subject only to `TimeSpan`
tick rounding in the host). Update `0x000a53f0` increments elapsed time and
writes `progress = min(elapsed / duration, 1)`; it does not apply an easing
curve.

The embedded `cross_fade_p` ELF at file offset `0x00d67a80` samples Texture0
and Texture1 at the same UV, optionally applies the two gradation blocks, then
evaluates `old + progress * (new - old)` before multiplying by overall opacity.
Consequently `CustomImageFade` (type 9) is now executable in the host with its
native linear progress.

The ordinary HOME caller is now exact too. In readable `NPXS40002.js`, module
196 maps a selection move left to `SlideInRight`, right to `SlideInLeft`, and no
direction to `Fade`. Module 511 attaches degree `Normal` to every background
request before `SceneControl.setBackground`. SharpEmu derives that direction
from the previous and next strand indices and sends ABI type 7, 8, or 9 with
degree 2; the former hand-tuned 450 ms / 5.5%-travel wallpaper implementation
was unused and has been removed.

The original `slide_in_p` embedded ELF begins at file offset `0x00d8d8c0`
(ELF size `0x1240`, shader text `0x6e0`). The decoder now accepts its SDK-12
scalar-memory utility opcode and yields 243 instructions, four image samples,
and one export. Native setup at `0x000a6980` reaches the type-7/type-8 branch at
`0x000a6d71`; its direction table is `[-1,+1]`. The four 24-byte parameter
records at file offset `0x00d652c0` are:

| degree | smoothness | slideFactor | displacementFactor |
|---:|---:|---:|---:|
| 0 CrossFade | 8 | 0 | 0 |
| 1 Subtle | 6 | 0.0013 | 0.0025 |
| 2 Normal | 4 | 0.0026 | 0.005 |
| 3 Strong | 2 | 0.0052 | 0.01 |

The host now executes the recovered spatial mask and both texture-coordinate
equations for opaque 16:9 title art, using a strip translation because this
Skia build has no runtime-shader facility. It does not claim the shader's
optional gradation/transparent-alpha blocks.

`ripple_p` begins at file offset `0x00d8a070` (embedded ELF size `0x1bb8`,
shader text `0x1020`). The decoder now yields 657 instructions, three image
samples, and one export. Its recovered user-SGPR ABI is two image descriptors
at `s0`/`s8`, two samplers at `s16`/`s20`, a 40-byte parameter buffer at `s24`,
and a 160-byte gradation buffer at `s28`. Program resource 1 is `0x022c0148`;
pixel input enable and address are both `2`. The translated SPIR-V validates
for Vulkan 1.2 and the original pixel program has executed on the AMD Vulkan
backend for the opaque, gradation-disabled path. Vtable draw
slot `0x000a4d70`, mode 2, selects the 40-byte degree table at virtual address
`0x00d61220`:

| degree | smoothness | ratio | scaleFactor | swirlFactor | fishEye |
|---:|---:|---:|---:|---:|---:|
| 0 CrossFade | 8 | 1.77777779 | 0 | 0 | 0 |
| 1 Subtle | 6 | 1.77777779 | 0.0125 | 0.025 | 0.025 |
| 2 Normal | 4 | 1.77777779 | 0.025 | 0.05 | 0.05 |
| 3 Strong | 2 | 1.77777779 | 0.05 | 0.1 | 0.1 |

The preceding fields are `origin.xy`, opacity, progress, and `progress_pow`.
Managed `BGTransition` chooses a focus point written in the preceding 100 ms,
then the focused widget centre, then `(960,540)`; it divides that point by
`1920x1080` into native request fields `+0x08/+0x0c`. Linear progress is
clamped `elapsed/duration`; the node then uploads:

```text
x            = 1 - linearProgress
progress     = 1 - 0.2*x^2 - 0.65*x^6 - 0.15*x^8
progress_pow = pow(progress, 2.2)
```

`Ps5Transitions.BackgroundRipple` now represents that exact CPU-side contract.
`Ps5NativeRippleCompiler` reads the embedded ELF from the user's firmware dump,
translates it at runtime, and `Ps5NativeRippleRenderer` batches the two supplied
RGBA plates and exact 40-byte records through the original pixel shader. The
shell transition path consumes those frames instead of a host-authored radial
substitute. `s_memrealtime` is decoded with its correct `vcc` destination but
is intentionally zero-valued in translation; that clock only feeds the still
unclaimed optional gradation/dither path, whose two enable blocks remain off in
the opaque shell route.

All EXACT.

## Plane2 plate — runtime writer and steady-shell route recovered

Plane2 update `0x000a2900` receives a 0–51 native state index. The 52-entry
u32 map at `0x00bd1f00` selects one of 37 records (stride `0x70`) at
`0x00bd0ed0`. The exact 4.03 steady-shell route is now closed by combining the
decompiled managed assembly with the native dispatcher:

1. `BackgroundLayer.Start()` writes `WaveColourPreset.HomeScreen` (`4`) to
   `PresetColourIndex`.
2. `[StructLayout(Sequential, Pack=4)] BackgroundLayerState` puts that field at
   `+0x0c`; `ThemeColourIndex` is the distinct field at `+0x10`.
3. Managed `BackgroundLayer.Update()` copies both fields into the state passed
   to `BGLayerNative.Update(ref state)`.
4. Native `0x00072814..0x00072827` reads `+0x0c` and `+0x10` and calls the
   owner dispatcher `0x00072e60`. For the steady-shell `ThemeColourIndex`
   family (`0x4x`), owner table `+0xa0` maps preset `4` to node state `5`.
5. The common node update at `0x00072d58` reaches `0x000dd210`, which tail-calls
   vtable slot `+0x40`; Plane2 owns that slot at `0x000a2900`.
6. `Plane2Map[5]` at `0x00bd1f00` is record `2`.

The full owner table for preset slots 0-29 is
`[2,3,4,3,5,6,0,7,3,8,9,1,4,25,4,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24]`.
Plane2's full 52-state record map is
`[0,1,2,2,2,2,2,2,1,1,6,8,3,4,9,5,7,21,22,23,24,25,26,27,28,10,11,12,13,13,13,13,13,13,12,12,17,19,14,15,20,16,18,29,30,31,32,33,34,35,36,10]`.
The high-contrast byte in the managed state selects the paired native state by
adding 26. Consequently normal Home (`4 -> 5`) and System Area (`2 -> 4`)
both select record 2, while their high-contrast states (`31` and `30`) both
select record 13. `Ps5NativeWavePlateEvaluator.ResolveSteadyRoute` now encodes
those two exact tables; custom-theme colour families remain separate.

The translated live plate now renders both selected records rather than only
record 2. Record 13 shares record 2's ramp, light, projection and specular
values; its last authored pair is dither `0.55`, light-colour scale `0.3`.
Native draw `0x000a2c8e..0x000a2cc7` divides the dither term by the separate
live node opacity at object `+0x58`, while `0x000a2d8e..0x000a2dce` multiplies
only `uLightColor.rgb` by record field `+0x64`. The CPU translation preserves
that distinction: record 13 retains the blue ramp, reduces its specular light
and grain, and is not a host-side darkening filter. `ShellBackground.HighContrast`
selects Home state 31 -> record 13; normal remains state 5 -> record 2.

The per-user theme-colour getter is now narrower than the old unknown implied.
Ordinary theme colours validate to `0..6`; `embedded:DUALCOLOR` packs as
`0x10..0x12` (fallback `0x10`) and `embedded:PARTICLE` as `0x20..0x22`
(fallback `0x20`). These packed values are the selector's theme-colour
argument, not Plane2 record indices.

Those selector branches can now execute their authored Plane2 records without
copying the table into the repository. `Ps5NativeWaveRecordSource` maps ELF64
load segments in the user's decrypted NPXS40087 `eboot.bin`, reads the selected
112-byte record from virtual table `0x00bd0ed0`, and feeds all three ramp stops,
light colour/position, plane, angle, exponent, specular, dither, and light scale
to the translated `wave_bg_p` evaluator. Records 2 and 13 retain embedded exact
fallbacks so default Home can still explain a missing dump; every other record
requires firmware and refuses to masquerade as record 2. A direct theme `0x01`
capture now routes Home state 10 -> record 6 and produces the firmware-authored
coral plate with moving grain. This closes the Plane2 half of custom themes;
the separate emitting particle program selected by `embedded:PARTICLE` remains
part of the native particle-state work.

Do not mask managed NoParticle `65` (`0x41`) to literal theme `1`. For special
preset Home (`4`), native `0x72e60` sees high nibble `4`, updates only the
effect-control state, then returns to the direct preset table: state `5`, record
`2`. System Area similarly returns state `4`, record `2`. Literal `0x01`, with
high nibble zero, is the distinct theme branch and selects state `10`, record
`6`. `Ps5NativeWavePlateEvaluator.ResolveRoute` test-pins this distinction and
the ordinary, dual-colour, particle and high-contrast mappings.

`BGLayerPlugin.SetPresetColour()` is empty in the decompiled 4.03 assembly, so
ordinary Home-to-Settings navigation does not replace that preset. The system
BG registry and per-user wave-colour getters are real, but they are not an
unresolved prerequisite for choosing the steady record-2 plate.

The Legacy compositor caller confirms the boundary. `ApplicationContainerScene
::UpdateSystemBg` queries `sceShellCoreUtilGetSystemBGState(0, appId, out
state)`: OFF (`0`) disables the system background, ON (`1`) enables it, and
DEFAULT (`3`) retains the per-application default. A change calls
`SetBackground(bool)` and `SystemBGMediator.UpdateSystemBG()`; this value gates
visibility and is not a colour-record index. `PLayerTransition` subscribes to
that mediator and its constructor requests `ShowWave(false)` followed by
`SetPresetColour(HomeScreen = 4)`. Both plugin methods are empty in 4.03, but
the caller still proves the intended preset is HomeScreen rather than a hidden
Settings-specific value.

Record 2 is, exactly:

- ramp stops `(0.035,0.21,0.58,0)`, `(0,0.15,0.5,0.5)`,
  `(0,0.14,0.55,1)`;
- light colour `(0,0.44,0.9)`, light position `(-150,50,400)`;
- plane/radius `-100`, angle `45`, specular exponent control `0.2`, specular
  intensity `0.15`, dither record value `1.4`, final output scale `1.0`.

Initializer `0x000d6780` expands the three ramp stops to 2160 float4 texels
using the firmware's cubic Hermite/Catmull-Rom interpolation. Plane2 draw
`0x000a2c50` writes the 128-byte `wave_bg_p` cbuffer. It derives reciprocal
render size and world extents, scales the light colour, writes the light and
centre values, and increments a process-global noise phase by exactly `1.0`
per draw, wrapping to zero after 255. The supposed "static plate" therefore has
a real, subtle animated grain even when the managed light mode is
`NoParticle`; that name only disables the separate light-particle override.

`Ps5NativeWavePlateEvaluator` is a direct CPU translation of record 2, the
shader, and the uniform writer. `ShellBackground.UseNativeWavePlate` now defaults
on for the untitled Home/Settings shell because the route is proven; selecting
a title still hands the background to that title's authored artwork. Motion-off
parks the phase; it does not substitute a hand-authored drift.

## Dormant mesh-wave code (not the live 4.03 background owner)

The binary also contains a skinned, animated 3-D `Wave` node, coloured through
ramp lookup textures. This code and its missing assets are real, but the live
4.03 PS5 background path does not consume them; the current owner is the
namespaced compute particle field over Plane2.

### Geometry and animation

The `Wave` node's load job (`0x000d7e40`) allocates an 832,800-byte model
context and loads **two** model files:

- `/app0/wave/wave0.fbxd`
- `/app0/wave/wave1.fbxd`

EXACT. `.fbxd` is Sony's cooked-FBX container. **These two files are not present
in the dump** - `/app0` for this title contains only `gls`, `hrtf`,
`lotusSetup`, `psm`, `recognition`, `sce_sys`, `systembgm` and `ugc`. A whole-tree
byte search for `fbxd` finds the two path strings in `eboot.bin` and nothing
else. The wave's silhouette and its animation curves are therefore **still
unrecovered**, and short of a fuller dump they cannot be recovered from here.

That the model is skinned and animated rather than static is EXACT from three
independent places:

- The wave vertex shader's uniform block is `localProjMat`, `localViewMat`,
  `invLocalViewMat`, `skinMat` - a skinning palette (`0xd814dc`).
- The renderer logs `[XF][Wave] AnimationTime camera:%8.3f sec (%.4f)
  model:%8.3f sec (%.4f)` (`0xb4e0fb`) - two independent animation clocks, one
  for the camera and one for the model, each with a phase.
- It also logs `[XF][Wave] setTimeRatio: %f` (`0xb70917`) and
  `[XF][Wave] pauseNoise: %d` (`0xb41515`) - a playback-rate control and a
  pausable noise term.

The vertex stage reads vertices from a structured buffer indexed by vertex id
(`s_inputBuffer`, `vtxId`) and emits `viewPos (POSITION)`, `normalMatIdx
(NORMAL)`, `volColBoundary (TEXCOORD)`, `uvAlpha (TEXCOORD)` and `eyeVec
(TEXCOORD)`. EXACT, from the shader reflection at `0xd814dc`. `volColBoundary`
and a per-vertex `uvAlpha` say the surface carries an authored transparency and
an authored colour-volume boundary - the wave fades at its edges because the
mesh says so, not because a shader computes it.

### Colour: six ramp textures

The wave pixel shader (`0xd7fef6`) binds, besides one sampler:

`uRamp0Tex0`, `uRamp0Tex1`, `uRamp0Tex2`, `uRamp1Tex0`, `uRamp1Tex1`, `uRamp1Tex2`

and the uniforms `uLightPos`, `uCoeff0`, `uLightColor`, `uConst2Color`,
`uSpecularParam`, `uSpecularColor`, `uAttr`, `uColorCoeff`. Its inputs are the
vertex outputs above plus the front-face flag. All EXACT.

Two sets of three ramps is what a cross-fade between two colour presets looks
like. This is the mechanism the managed `WaveColourPreset` drives.

The ramps are built at start-up by the function whose assert string is
`initWaveRampWork` (`0x000d8040`), called from the `Wave` node constructor. It
allocates **8,709,120 bytes** and fills it with a doubly nested loop that is
exactly `21 presets x 6 textures`:

- Each ramp texture is **4320 texels of float32 RGBA**, i.e. 69,120 bytes.
  (`21 x 6 x 69,120 = 8,709,120`, matching the allocation exactly.)
- Per preset the six textures are built from a **1312-byte (0x520) control
  block**, laid out as three 432-byte (0x1b0) *ramp sets* plus a 16-byte tail
  of `(1,0,0,0)`.
- Each ramp set is:

  | Offset | Contents |
  | ------ | -------- |
  | `+0x000` | 8 colour stops, `float4 (r, g, b, position)` |
  | `+0x080` | 6 colour stops, `float4 (r, g, b, position)` |
  | `+0x0e0` | 5 alpha stops, `float2 (alpha, position)` |
  | `+0x108` | 7 alpha stops, `float2 (alpha, position)` |
  | `+0x140` | 28 floats of material parameters |

- The 8-stop array plus the 5-stop alpha array make one texture; the 6-stop
  array plus the 7-stop alpha array make the other. The six textures are laid
  down in that alternating order.

The control table starts at **`0xbd1fd0`** and strides `0x520` per preset. All of
the above is EXACT.

### How a ramp is interpolated

The builder is `0x000d6990`, signature
`(dst, texelCount, colourStops, nColour, alphaStops, nAlpha)`. For texel `i` of
`n` it evaluates the ramp at `t = i / (n - 1)` and:

- Finds the stop interval containing `t` by comparing `t` against each stop's
  `.w` (its position).
- Interpolates with a **cubic Hermite (Catmull-Rom) spline**, not a straight
  line. Interior tangents are `0.5 * (p[k+1] - p[k-1])`; the first segment uses
  `m0 = p1 - p0`, the last uses `m1 = p[n-1] - p[n-2]`. The basis is the standard
  `p0 + t*m0 + t^2*(3*(p1-p0) - 2*m0 - m1) + t^3*(2*(p0-p1) + m0 + m1)`.
- Clamps RGB to `[0, 1]`.
- Evaluates the alpha ramp with the same spline, clamps it to `[0, 1]`, and
  writes it into the texel's `.w`.
- Below the first stop's position it emits the first stop; above the last, the
  last.

All EXACT. **This is the single most useful correction to our implementation:
the wave's colour is a Catmull-Rom-splined gradient sampled from a 4320-entry
LUT, not a linear gradient.** A linear ramp through the same stops is visibly
flatter through the mid-tones.

### The 21 wave colour presets

Reading the table at `0xbd1fd0`, the 21 presets are ten hues in two intensity
families plus one monochrome. Below, `ramp0tex0`'s eight colour stops are shown
as 8-bit sRGB-step hex (the stored values are linear floats; the hex is a
readable rendering of them, not a colour-space conversion).

| # | Character | `ramp0tex0` colour stops |
| - | --------- | ------------------------ |
| 0 | black | all `#000000` |
| 1 | blue | `#0001C7 #002ADF #0073D5 #0051E6 #0073FF #0020EE #0AA0F4 #0009FF` |
| 2 | blue, brighter | `#0001FF #002DFF #0077E5 #0051FF #0073FF #0020FF #0AA0FF #0009FF` |
| 3 | blue, as 1 | same stops as 1 |
| 4 | pale lavender / white | `#F0F5FF #DFD9F7 #9D95AF #CCC8E5 #E5DAFF #AFA3BF #C3B6E4 #D6CCE6` |
| 5 | cyan / teal | `#5E5AC8 #7CC4E8 #6A99CC #507FBB #5EACCD #668EC8 #71CCFE #73D2ED` |
| 6 | rose / red | `#B45259 #BE4960 #C64C60 #CC3842 #E5504F #AF6F6F #C16077 #E55C6F` |
| 7 | violet | `#B801FF #782DFF #8540E5 #793FFF #8655FF #9020FA #8D6AFF #B609FF` |
| 8 | orange / red | `#FF0133 #FF2A1A #FF1A33 #FF331A #E52633 #CC2001 #FF3A00 #FF0822` |
| 9 | gold / yellow | `#807800 #807D00 #F2F500 #938500 #C39900 #CBA100 #665000 #946B00` |
| 10 | monochrome | `#1A1A1A #333333 #757575 #4C4C4C #737373 #202020 #C2C2C2 #090909` |
| 11-20 | presets 0-9 again, with the material's intensity term raised from ~0.6 to ~2.0 | identical colour stops |

All EXACT. The stop *positions* are shared across presets to a striking degree:
`0.0, 0.146718, ..., 0.872045, 1.0` recurs, and the alpha ramps are near-identical
across presets (e.g. `(0,0) (0.891,0.127072) (0.093,0.61828) (0,0.776344)
(0,1.0)` for the 5-stop array). The presets differ in hue and in the material
block, not in shape.

Preset 4's material block (28 floats at `+0x140` of its first ramp set) is
`(0, 0.015, 0.065, 0)`, `(1, 1, 1, 0)`, `(0.095, 0.09, 0.13, 0)`, `(1, 1, 1, 0)`,
`(0.26, 0.725, 0.015, 0.346)`, `(0.3, 0.3, 40.0, 0.6)`, `(0.130001, 0.82, 0, 0)`.
The `40.0` is a specular exponent (it is `35.9` for preset 6, `40.0` for most
others) and the following `0.6` is the term that becomes `~2.0` in presets 11-20.

**INFERRED, and important:** this 21-entry table is **not** the managed
`WaveColourPreset` enum. Index 5 is cyan and index 6 is rose, where
`WaveColourPreset` has `NoWave` and `Black` at 5 and 6. It is a *wave colour*
table, and the firmware has an explicit setter for exactly that
(`sceShellCoreUtilSetSystemBGWaveColor` at `0xb4365c`, and the per-user
`Sce.Vsh.UserServiceWrapper::_SetThemeWaveColor` at `0xb20f0a`). The managed
`WaveColourPreset` selects a *scene state*; this table selects the *hue*. Which
of the 21 the home screen lands on is **not recovered** - see gaps.

### The plate behind the wave

The wave is drawn over its own background pass, `wave_bg`. Its pixel shader
(`0xd822c8`) takes `uCoff0`, `uCoff1`, `uLightColor`, `uLightPos`, `uCenterPos`
and one texture. Its vertex shader (`0xd829d4`) takes only a vertex id - it is a
full-screen pass. EXACT.

Those uniforms are filled from a second ramp table, built by `initBgRampWork`
(`0x000d64e0`), called from the `Plane2` constructor. It allocates **1,278,720
bytes** = **37 ramps x 2160 texels x float32 RGBA**, from a table of 37
112-byte records at **`0xbd0ed0`**. Each record is:

| Offset | Contents |
| ------ | -------- |
| `+0x00` | 3 colour stops, `float4 (r, g, b, position)` |
| `+0x30` | light colour `(r, g, b)` |
| `+0x40` | light position `(x, y, z)` - `(-150, 50, 400)` for records 0-20, `(-150, 80, 400)` for 21-36 |
| `+0x50` | a second position `(-100, 45)` in every record |
| `+0x58` | two coefficients |
| `+0x64` | an intensity scale, multiplied into every colour stop |

The builder is `0x000d6780` - the same Catmull-Rom spline, but each stop's RGB is
pre-multiplied by the record's scale and the texel's `.w` is forced to 0. All
EXACT.

Selected records (colour stops, then light colour, then scale):

| # | Stops | Light colour | Scale |
| - | ----- | ------------ | ----- |
| 0 | black, black, black | `(0, 0, 0)` | 1.0 |
| 1 | `(0, 0.32, 0.58)`, `(0, 0.16, 0.58)`, `(0, 0.01, 0.16)` | `(0, 0.44, 0.90)` | 1.0 |
| 4 | `(0.4, 0.4, 0.45)`, `(0.25, 0.25, 0.31)`, `(0.09, 0.086, 0.15)` | `(0.2, 0.2, 0.215)` | 1.0 |
| 9 | `(0.83, 0.657, 0)`, `(0.72, 0.565, 0)`, `(0.607, 0.46, 0)` | `(0.83, 0.41, 0)` | 1.0 |
| 11-20 | identical stops to 1-10 | identical | **0.3** |
| 21 | `(0.055, 0.1568, 0.3725)`@0, `(0.015, 0.0745, 0.27)`@0.36, `(0.015, 0.0745, 0.225)`@1 | `(0.355, 0.682, 0.807)` | 1.0 |
| 24 | `(0,0,0)`@0, `(0,0,0)`@0.7, `(0.02, 0.04, 0.07)`@1 | `(0.33, 0.38, 0.40)` | 1.0 |
| 25-28, 33-36 | flat (second and third stop equal, both at position 1.0) | `(0,0,0)` | 1.0 |
| 29 | `(0.0235, 0.1411, 0.3803)`@0, `(0, 0.0627, 0.2235)`@0.36, same@1 | `(0.3294, 0.647, 0.8196)` | **0.53** |

All EXACT. Note the shape of the family: records 0-10 are a hue set, 11-20 are
the same hues at 0.3 intensity, 21-36 are a later, darker set with the light
raised from y=50 to y=80. Record 21 - a deep navy with a bright cyan light -
is the closest thing in the table to the PS5's shipped home backdrop.

**This replaces our "clear plate" guesswork with real numbers.** The plate is not
a flat colour; it is a three-stop Catmull-Rom vertical gradient with a coloured
light term at a fixed off-centre position.

### The rest of the wave pass

An FXAA pass closes it: `wave_fxaa_vv` / `wave_fxaa_p`, pixel shader at
`0xd834c4` with `uColorTex`, `uColorSampler` and one `uParam0`. There is also a
depth-only variant of the wave (`wave2_depth_vv` / `wave2_depth_p`, vertex shader
at `0xd7e964`) sharing the same skinning uniforms. EXACT.

The full shader-name registry for the wave is `wave2_vv`, `wave2_p`,
`wave2_depth_vv`, `wave2_depth_p`, `wave_bg_vv`, `wave_bg_p`, `wave_fxaa_vv`,
`wave_fxaa_p`. EXACT.

## FirstWave - the other wave

`FirstWave` (`fw_*` shaders) is a completely different renderer and worth
recording because it is where a "procedural sine field" mental model would
actually have been right - and it is still not one.

It is a **tessellated patch surface with order-independent transparency**:

- `VS_OUTPUT main(VS_INPUT)` at `0xd94ed8` - position + normal in, position +
  normal out.
- `HS_OUTPUT main(InputPatch<VS_OUTPUT,16>, unsigned int)` at `0xd93dbe` - a
  **16-control-point patch** hull shader, emitting four edge tessellation factors
  and two inside factors.
- `DS_OUTPUT main(float2, OutputPatch<HS_OUTPUT,16>)` at `0xd9338c` - the domain
  shader, emitting `normal`, `eyeDir`, `edgeValue`, `normalDx`, `normalDy`.
- `float4 main(DS_OUTPUT)` at `0xd9664e` - writes `RWOITBuffer` and
  `RWOITCountBuffer`.
- `float4 main(VSOUTPUT)` at `0xd97784` - the OIT resolve, reading those buffers.

Every stage shares one constant buffer, and this is where the managed layer's
opacity lands by name:

`worldViewMatrix`, `worldProjectionMatrix`, `worldViewProjectionMatrix`,
`worldMatrix`, `cameraPosition`, `BackgroundColour0`, `BackgroundColour1`,
`BackgroundLightColour`, `ReflectionColour`, `EnvironmentMapColour`,
`EdgeColour`, `BlurParameters`, `opacity`, `time`, **`waveOpacity`**,
`oitSliceOffset`, `screenDim`.

All EXACT. Supporting passes: `fw_basic_vv/p`, `fw_oit_p`, `fw_comp_oit_p`,
`fw_clear_vv/p`, `fw_background_p`, `fw_blur_vv`, `fw_blurh_p`, `fw_blurv_p`,
`fw_fxaa_p`, `fw_flow_h`.

`DualWave` (`dual_wave_vv`, `dual_wave_p`, `dual_wave_bg_p`) is a third path. Its
constructor (`0x00073ea0`) writes two identical 160-byte parameter blocks at
`0xebfe00` and `0xebfea0` and a control block at `0xebff40`. Decoded, the
parameters are world-space quantities - `1000.0, -1750.0, -1800.0, -150.0, 77.0,
182.0, 180.0, 90.0` for the first instance and `1300.0, -1750.0, -1800.0, -150.0,
77.0, 182.0, 180.0, 150.0` for the second, followed by an 8-bit colour
`(46, 159, 155, 255)` twice and the tail `0, 1.0, 2.0, 0.65, 3.0, 2.0`. The
control block starts `4, 21, 0, 0, 0, 1.0f, 1, 0`. EXACT as values; their meaning
is INFERRED (the leading `4` and `21` line up suspiciously with "preset 4 of 21",
but nothing here proves it).

## Light particles

This is a full GPU particle system and it is recovered in detail.

### The simulation

`simulateParticles` (`0x00096640`) dispatches a compute shader with
`(numParticles + 63) / 64` groups - **64 threads per group**. EXACT. It sets
`time`, `timeStep`, a 4-bit **pattern index** and the previous pattern index
packed into the same word, a `transPatternFlag`, and a
`timeRateForLifeCountDown` which is `1.0` when the current and requested patterns
match and otherwise a stored value scaled by `2/13` (`0.15384616`). EXACT.

The compute shader's interface (`0xda2eb0`) is the parameter list we never had:

```
SRTCs:        time, timeStep, timeRateForLifeCountDown, isPreSimulation,
              transPatternFlag
ResourcesCs:  particleIds1, particleProperties, particleOptions, randSeed,
              numParticles, maxParticleId, offsetParticle,
              indexStridePerParticle, particleMinLife, particleMaxLife,
              blurRadiusPowerFactor, blurRadiusClearEdgeThreshold,
              particleSpawnRangeMax, particleSpawnRangeMin,
              particleMaxAcceleration1, particleMaxRotationSpeed,
              particleCurlSizeP, particleCurlSpeedP, particleCurlTimeRateP,
              particleCurlSpeedInit, numRendezVousPoints,
              particleRendezVousPoints
ParticleProperty:       pos, blurBoundary, vel, fore, transPatternFlag, right,
                        curLife, maxLife, renLife
ParticleRendezVousParam: acceleration
ParticleBlendParam:      center, weight, beginDist, endDist
```

All EXACT. Two facts fall straight out:

- **The motion is curl noise.** `particleCurlSizeP`, `particleCurlSpeedP`,
  `particleCurlTimeRateP`, `particleCurlSpeedInit` are a curl-noise flow field
  with a spatial scale, a speed, a time rate and an initial speed.
- **The patterns are rendezvous points.** Particles are accelerated toward a
  set of `numRendezVousPoints` attractors. That is how `Bottom` and `Spread`
  differ: not different emitters, but different attractor sets. The pattern
  index is 4 bits, so at most 16 patterns, and the shader carries *two* of them
  (current and previous) at once so a pattern change is a blend, not a cut.

Named patterns appear as strings: `coldboot`, `spread_expanded`,
`spread_expanded_fadeout`, `bottom_fadeout`, `bottom_camCal`,
`initboot_to_spread_no_movie`, `initboot_to_bottom_no_movie`, and
`coldboot/colorchange/colorchange.`. The seven first names are embedded at the
head of multi-kilobyte binary blobs. Their outer selector is now EXACT. The
call chain below uses `0x00099280` for its small stack-protected wrapper,
`0x000992d0` for the seven-way name matcher, and `0x00099ac0` for the actual
versioned blob parser; these are three parts of one load, not competing parser
addresses.

At `0x00095920`, the native loader rejects selector values above 6, indexes a
seven-pointer name table at `0xd60e10`, and passes that table, a seven-pointer
blob table at `0xd60e50`, and a seven-entry byte-length table at `0xbb0d80` to
the wrapper at `0x00099280`. Its matcher at `0x000992d0` invokes the blob parser
at `0x00099ac0`. The ELF's `R_X86_64_RELATIVE` records prove
the pointer values; they are not inferred by scanning for nearby strings.

| selector | name / embedded blob name | blob vaddr | file offset | exact bytes |
|---:|---|---:|---:|---:|
| 0 | `coldboot` | `0xbb0dc0` | `0xbb4dc0` | `0x1faa` |
| 1 | `spread_expanded` | `0xbb2d70` | `0xbb6d70` | `0x1df5` |
| 2 | `spread_expanded_fadeout` | `0xbb4b70` | `0xbb8b70` | `0x276c` |
| 3 | `bottom_camCal` | `0xbb72e0` | `0xbbb2e0` | `0x2856` |
| 4 | `bottom_fadeout` | `0xbb9b40` | `0xbbdb40` | `0x2707` |
| 5 | `initboot_to_spread_no_movie` | `0xbbc250` | `0xbc0250` | `0x2960` |
| 6 | `initboot_to_bottom_no_movie` | `0xbbebb0` | `0xbc2bb0` | `0x3208` |

Every blob begins with a packed `u32` byte count, an ASCII name including its
NUL, then serialization version `1`. There is no padding before the version
(`bottom_camCal` places it at an unaligned address). The parser at `0x00099ac0`
accepts only versions 0 or 1. Exactly **25 u32 vector cardinalities** follow the
version before any element payload. Native helper `0x0009c030` consumes counts
0-7; the main parser then uses counts 8-17 to allocate ten vectors of 0x50-byte
records, counts 18-22 for five related 0x50-byte record-vector forms
(`0x0009c690`), count 23 for 0x30-byte records (`0x0009ca80`), and count 24 for
0x98-byte records (`0x0009cdd0`). That container-size map is EXACT.

The exact count arrays are:

| pattern | counts 0-7 | counts 8-24 |
|---|---|---|
| `coldboot` | `23 11 0 1 282 14 58 33` | `3 2 5 1 0 2 1 4 4 1 1 1 0 8 1 0 1` |
| `spread_expanded` | `50 8 1 1 237 8 184 33` | `0 0 0 0 0 21 26 1 1 1 0 8 0 0 0 1 1` |
| `spread_expanded_fadeout` | `23 2 0 0 390 2 52 0` | `1 0 0 0 0 11 8 1 1 1 0 0 0 0 2 0 0` |
| `bottom_camCal` | `21 8 1 0 385 8 65 0` | `0 0 0 0 0 10 8 1 1 1 0 8 0 0 0 1 0` |
| `bottom_fadeout` | `22 2 0 0 388 2 44 0` | `1 0 0 0 0 10 8 1 1 1 0 0 0 0 2 0 0` |
| `initboot_to_spread_no_movie` | `63 6 0 0 353 8 214 0` | `26 1 0 0 0 7 26 1 1 1 2 3 0 0 1 0 0` |
| `initboot_to_bottom_no_movie` | `32 6 0 0 483 8 81 0` | `14 1 0 0 0 6 8 1 1 1 2 3 0 0 1 0 0` |

Payload word 25 is therefore no longer confused with a count: interpreted as
float32 it is `6.5` for `coldboot`, `0.1` for both fadeout patterns, and `0.0`
for the other four. Its property meaning is not yet proven. Likewise, the
semantics of cardinalities 0-7 are not decoded, so those counts must not be
labelled as emission or rendezvous parameters. Fields 8-22 are now routed to
their exact compute/draw/local resource families below.
`scripts/ps5_particle_patterns.py` verifies the 4.03 SHA-256, follows the
relocations, and emits the exact cardinalities and payload boundary without
assigning guessed shader semantics.

The record mechanics and routed resource names are now decoded. Slots 8-17 are
**direct timed events**. Each 0x50-byte in-memory event has
a 12-byte serialized header: `f32 time`, `u32 destination-index count`, `u32
assignment count`; a vector of signed destination-block indexes; and 24-byte
typed assignments. Each assignment is `u32 opcode`, four reserved bytes, an
8-byte payload, then a `u64` destination byte offset. Evaluator `0x00097a70`
fires an event when `time - event.time` is within the current step. Its exact
opcodes are qword/dword/word copies, f64, f32, masked dword merge, and a
16-byte lookup copy.

Slots 18-22 are **interpolated timed events**. Their 16-byte header is `f32
start`, `f32 end`, `u32 destination-index count`, `u32 assignment count`.
Their 32-byte assignments are `u32 opcode`, four reserved bytes, 8-byte start
value, 8-byte end value, and `u64` destination byte offset. Evaluator
`0x00097fb0` computes clamped `(time-start)/(end-start)`; helper `0x0009dc30`
interpolates integer, f64 and f32 opcodes. The exact parsed-object routing is:

The evaluator is stateful. It applies a curve only when the current frame step
overlaps `[start,end]`; it does not clamp every future curve's start value into
the resource from time zero. Once crossed, the last written value persists.
Integer opcode 2 converts with `vcvttsd2si`, so interpolation truncates toward
zero rather than rounding. This matters visibly in coldboot: `large_draw[1]`
remains at zero particles until its 6.0 key, reads 26 rather than 27 particles
at 6.5, and is finally shut down by the later direct key at 8.5.

| slots | object members | event kind | exact destination pointer table |
|---|---|---|---|
| 8, 13, 18 | `+0x28`, `+0x80`, `+0x38` | direct/direct/interpolated | small-particle compute, node `+0x198`, parity stride `0x140` |
| 9, 14, 19 | `+0x30`, `+0x88`, `+0x40` | direct/direct/interpolated | small-particle draw, node `+0x418`, parity stride `0x140` |
| 10, 15, 20 | `+0x50`, `+0x90`, `+0x60` | direct/direct/interpolated | large-particle compute, node `+0x5d8`, parity stride `0x50` |
| 11, 16, 21 | `+0x58`, `+0x98`, `+0x68` | direct/direct/interpolated | large-particle draw, node `+0x678`, parity stride `0x50` |
| 12, 17, 22 | `+0x70`, `+0xa0`, `+0x78` | direct/direct/interpolated | local pattern values; no destination-index table |
| 23 | `+0x48` | 0x30-byte record; semantics unknown |
| 24 | temporary only | two-string descriptor consumed then destroyed |

This also retires a misleading boot-animation claim. The `30.0` in
`coldboot` is field 21 event 6, interpolation assignment 1's start value at
destination offset 232. Field 21 is now proven to mutate the large-particle
**draw** resource table, not the compute simulation table, so this value cannot
be a rendezvous acceleration. The large draw loop independently proves that
offset `0xac` in this same block is `numParticles`. Shader reflection names
offset `0xe4` `parMinSize` and offset `0xe8` `parMaxSize`: coldboot field 21
event 6 fades those values from `15.0`/`30.0` to zero on large-particle
resource index 1 over seconds 6.5-6.9. The same resource block names offset
`0xe0` `transparency`. The `300.0` in
`spread_expanded` is field 23 record 0's
middle fixed-tail word. They are different schemas and cannot both be called
one recovered acceleration property.

The destination resource layouts recovered from shader reflection and native
allocation sites are:

| family | object size | reflected resource | proven members used by coldboot |
|---|---:|---|---|
| small compute | `0xf8` | `ResourcesCs` | `particleMaxAcceleration1` `+0x60`, `particleCurlSpeedP` `+0x74`, rendezvous array `+0x8c` |
| small draw | `0x140` | `ResourcesVsPs` | `numParticles` `+0x20`, `cameraZ` `+0x138` |
| large compute | `0xf8` | `ResourcesCs` | same reflected compute layout |
| large draw | `0xec` | `ResourcesLargeParticleVsPs` | `particleColorInHsv.z` `+0x6c`, `numParticles` `+0xac`, `transparency` `+0xe0`, `parMinSize` `+0xe4`, `parMaxSize` `+0xe8` |

The `0x140` and `0x50` values in the routing table are renderer-node bank
strides, not resource-object sizes. The decoder emits both the resource family
and exact reflected member name for every mapped assignment. Only the nested
The nested `particleRendezVousPoints` layout is now exact. `particle_c` at
pc `0x2a50` and `0x2ab8` multiplies the point index by **36 bytes**. Each entry
is a 32-byte `ParticleBlendParam` followed by `f32 acceleration`:
`rv.center` at `+0x00`, `rv.weight` at `+0x0c`, `rv.beginDist` at `+0x18`,
`rv.endDist` at `+0x1c`, and `acceleration` at `+0x20`. The array begins at
`ResourcesCs + 0x8c` and the 0xf8 resource block holds at most three entries.
The decoder now emits these exact nested names.

`scripts/ps5_particle_patterns.py::sample_resource_state` now executes these
routed direct/interpolated writes into byte-exact resource blocks. Its firmware
test pins the t=0, 6.5, 7.25, and 8.5 states. The GUI's first bounded native
event player is `Ps5ColdBootLargeDraw1Evaluator`: it executes field-16 setup and
the overlapping field-21 count, transparency, and size curves for
`large_draw[1]`, including native step gating and integer truncation. This is
resource execution, not yet a framebuffer renderer.

### The two draw paths

The render function (`0x00096860`, assert string `RenderInternal`) walks **two
particle groups**, and per group:

- **8 compute buffers** are simulated (indices `0x33`-`0x3a` of the node, group
  stride `0x140`).
- **8 small-particle draws** are issued; each copies a 320-byte resource block
  and launches `numParticles * 6` **non-indexed** vertices. The recovered NGG
  body divides the sequential invocation ID by six for the particle index and
  uses its remainder to address the exact six-corner triangle-list helper
  `(-1,-1),(1,-1),(-1,1),(1,-1),(-1,1),(1,1)`.
- **2 large-particle draws** are issued.

A group only runs while `time + timeStep <= groupEndTime`. All EXACT.

The managed-to-native state boundary is exact now as well. Dispatcher
`0x72e60` consumes light-mode commands `0x41..0x4f`; its jump table sends
Bottom/Spread/ColdBoot/WarmBoot/InitialBoot to raw particle states
`1/2/3/4/6`, respectively. NoParticle, InitialWelcomeNoParticle, Black, and
None do not call state setter `0x97560`. The production shell routes eligibility
through this recovered table rather than a separate hard-coded ColdBoot enum
comparison. Raw state 3 uses the recovered large-particle cold-boot path. Raw
states 1 and 2 both select serialized body 1, `spread_expanded`, through the
setter's exact `(1,1,0,0,1,1)` selector table. The executable small-particle
cache is therefore shared by Bottom and Spread, while their separate light and
camera delta remains a distinct recovery target.

The `spread_expanded` and `bottom_camCal` records both address the eight
`small_compute`/`small_draw` bank tables; they do not use the accepted two-bank
large-particle visualization as a substitute. `spread_expanded` is now hosted
through its original `particle_vv`/`particle_p` stages and persistent property
history. `bottom_camCal` is not a complete replacement body: several banks
retain zero constructor fields such as `maxParticleId`, and a standalone replay
is correctly clear. It must be applied only after its native call site and
inherited resource state are recovered; it is not wired to raw state 1 by name.

**Small particles** (`particle_vv` `0xda5f6c`, `particle_p` `0xda4a5e`) are lit,
depth-blurred sprites. Their resource block adds `blendBlur`,
`distStartBlurAgain`, `distEndBlurAgain`, `unblurMinSize`, `unblurMaxSize`,
`blurMaxSize`, `blendDistDarken`, `intensityDistDarken`, `numLights`, `lights`,
`cameraZ`, `groupId`, and each light is

```
LightSourceProperty: pos, dir, enabled, diffuseIntensity,
                     diffuseDistOrCosineBeginFading,
                     diffuseDistOrCosineEndFading, ambientIntensity,
                     specularIntensity, specularPowerK
```

The vertex stage emits `centerPos`, `interPos`, `uvs`, `parNormal`,
`vertAndParId`, `screenPos`. All EXACT. So the motes are **3-D, lit, and
depth-of-field blurred by distance from the camera**, darkening with distance -
not flat additive sprites.

The small-particle launch ABI is now decoded rather than inferred. `particle_vv`
uses its sequential invocation ID directly: the reciprocal multiply by
`0xAAAAAAAB`, high-half extraction, and shift compute `floor(vertexId / 6)`;
the remaining arithmetic computes `vertexId % 6` for the corner table above.
An indexed four-corner launch replaces that ID stream and is therefore wrong,
even if a solid-colour diagnostic happens to resemble a quad.

Steady-state compute replay must refresh the complete firmware-authored
`ResourcesCs` body on every 60 Hz step. Replaying the inclusive 301 resource
frames from `spread_expanded` t=0 through t=5 evolves 400 native 0x44-byte
property records and produces SHA-256
`4749D336E9289AA2922C09396786C271DFC1DA432E93A446043A986952C57E3A`.
Holding the t=0 resource body and changing only the SRT time is not an
equivalent simulation.

There is an additional persistent draw side effect. `particle_vv` loads
property `+0x38` and stores it to `+0x40` **only while `renLife` at `+0x40` is
negative**. That is a latch, not a per-frame copy. `particle_p` loads both
values, subtracts them with a clamped x2 output modifier, squares the result,
and uses that as one of its final radiance factors. Copying every frame reduces
the age to one time step and incorrectly makes the particles nearly black.

The full five-second `spread_expanded` replay now executes 301 inclusive 60 Hz
frames across all eight banks: 2,408 compute dispatches, 1,820 populated
property records, and final shared-property SHA-256
`2080444BDC5F40649DCFC39B55C22296ECD3180C9F8BC7533A45EF51A66CB13E`.
The original Sony vertex/pixel pair then draws the seven non-empty banks in one
`ONE/ONE/ADD` render pass and produces 13,919 non-clear RGBA8 pixels. No debug
colour or intensity multiplier is present.

The production shell accepts the resulting 51-frame, 10 fps draw cache through
`SHARPEMU_PS5_NATIVE_SMALL_DRAW_CACHE` and batches every non-empty bank into one
render pass per displayed frame. Two shell-compositor captures at 0 and 500 ms
differ, proving that both the native background and small-particle layer advance.
The production adapter now keeps its Vulkan instance, device, firmware shader
modules, textures, pipeline, eight descriptor banks, target and readback storage
alive across frames. A same-frame oracle comparison is byte-identical to the
one-shot proof renderer (`DE31A3BB...CA70`), while a 100-draw run averages 66 ms
per 1920x1080 frame including process and first-frame setup. The live path is
therefore original-shader playback rather than pre-rendered PNG substitution.
The same source now accepts raw state 1 as well as raw state 2 because both
states select this exact serialized body. A dedicated Bottom/Login compositor
scene reports `native particle frame loaded for raw state 1`; captures one
second apart differ and retain the original shader's additive motes over the
native animated plate.

The cache has an optional live-compute payload at
`compute/{particle.spv,ids.bin,resource-banks/bank-*/frame-*.bin}`. When it is
present, `Ps5NativeSmallParticleReplay` executes every missing native 60 Hz
resource frame across all eight banks and carries the resulting shared 408,000-
byte property allocation into the next request. A resumed chunk deliberately
skips the already-executed head frame, while `particle_vv`'s negative-`renLife`
draw latch is interleaved from the preceding frame before the next compute
dispatch. A managed clock restart caused by a shell-state change moves only the
clock origin; it cannot rewind the native allocation. At the last supplied
resource frame the source holds the last exact state rather than looping the
property buffer or fabricating later resource bodies. Cached `properties.bin`
remains the compatibility fallback when this payload is absent.

`SharpEmu.Tools.GpuConformance --ps5-particle-banks-capture` accepts an optional
eighth argument naming that `compute` directory and copies the user-supplied
translated compute shader, native ID permutation, and all eight resource-bank
sequences into the schema after the byte-pinned capture succeeds. No firmware
bytes are added to the repository. The route remains limited to raw states 1
and 2, which the setter proves select `spread_expanded`; managed `NoParticle`
routing is unchanged.

**Large particles** (`large_particle_vv` `0xda8ac4`, `large_particle_p`
`0xda7502`) are the ones that answer the `.gnf` question. Their resources are

```
particleProperties, particleIds, backgroundTex, backgroundTex2, textureOptions,
particleColorInHsv, useCamera, camera, randSeed, numParticles, offsetParticle,
indexStridePerParticle, maxParticleId, blendEdgeBlur, edgeBlurMaxSize,
transparency, parMinSize, parMaxSize
CameraProperty: fovY, aspect, near, far, pos, fore
```

All EXACT. `backgroundTex` and `backgroundTex2` are the two textures the resource
job (`0x00096240`) loads by name:
`/system_ex/vsh_asset/Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and `Particle1.gnf`.

**This settles what those two 480x270 BC7 light fields are for.** They are not
mote sprites and they are not a full-frame wash. They are the source texture for
the *large* particle path - big, soft, out-of-focus bokeh discs sized between
`parMinSize` and `parMaxSize`, edge-blurred by `blendEdgeBlur` /
`edgeBlurMaxSize`, tinted through `particleColorInHsv` (**colour is specified in
HSV**, not RGB), and placed with a real perspective camera. Both loads have a
fallback: on any failure the renderer substitutes a 1x1 opaque black texture
(format `0x12`).

The host draw state is now exact enough to remove several probe assumptions.
At ordinary coldboot t=6.0 the active group issues two non-indexed triangle-list
`DrawIndexAuto` calls: `4*6=24` vertices for `large_draw[0]`, then `20*6=120`
for `large_draw[1]`. The large-particle blend descriptor is enabled
`ONE/ONE/ADD` for both RGB and alpha. Viewport and scissor cover the full
current PSM UI colour context. `UIRenderer` creates that context with
`Sce.PlayStation.Core.Graphics.PixelFormat.Rgba` (enum `1`), whose distinct
`Bgra` member is `19`; the host equivalent is `R8G8B8A8_UNORM`. Particle0 is
bound to `backgroundTex` at resource `+0x20`, Particle1 to `backgroundTex2` at
resource `+0x40`, and the pixel shader samples both at the same UV.

The former “target format `0x11`” conclusion was a type error. At NPXS40087
vaddr `0x9722d`, `0x11` enters PSM's pixel-format mapper at `0xdf9c0` while a
separate image surface is constructed. The decompiled PSM enum names that
value `PixelFormat.Dxt3`; it is not an AGC `RenderTargetFormat`. This is also
consistent with the following 2 MiB address step for a 1920x1080
8-bits-per-pixel DXT3 surface. The 4.00 SDK's unrelated AGC enum happens to use
`0x11` for `kRenderTargetFormat1_5_5_5`, which is why interpreting the raw
immediate without its call-site type produced a plausible but wrong result.

The sampler is now exact too. `large_particle_p` builds sampler SGPR words
`{0x00000092, 0, 0x02500000, 0}` before both `image_sample` instructions. The
firmware/backend decoder resolves them to linear minification and
magnification, nearest/base-mip selection, clamp-to-edge on U/V/W, zero LOD
bias, and min/max LOD zero. The remaining pixel-stage semantic question here
is the post-sample meaning of `textureOptions` 0 versus 1.

The two original large-particle shader ELFs now translate to validating SPIR-V
and create a Vulkan graphics pipeline together. A dedicated off-screen probe
binds the evaluator's scalar resources, the native compute readback, and both
decoded GNF images. The first clear result was traced to two host/backend bugs,
not to Sony's shader: the SRT pattern selector had been sampled from property
`+0x20` instead of the ISA-proven `+0x28`, and `v_interp_mov_f32` was discarded
then smooth-interpolated instead of loading a `Flat` parameter. The first bug
disabled every vertex lane before the corner load; the second flushed the raw
integer particle-id bit pattern to zero in the pixel stage. Both are fixed and
regression-pinned. MTBUF's instruction-owned format is also separated from
MUBUF descriptor formatting.

The continuous t=6.5 proof advances Sony's compute shader for thirty native
60 Hz steps, clears `particleOptions` spawn bit `0x1000` at the decoded 6.1 s
event, samples the simultaneous count/transparency curves (26 particles,
transparency 0.1), and submits 156 vertices. The original vertex/pixel pair and
both original GNF sprites produce 1,449,449 non-clear pixels on the AMD Vulkan
probe. This is the first visible firmware-shader particle frame in this branch.
The sampler and colour-target mapping are now source-pinned. This still does
not by itself prove pixel-identical output; the later two-bank oracle and
production-compositor checks below are the stronger current evidence.

The compute pipeline now requires a 32-lane **host** subgroup through Vulkan
subgroup-size control. This is load-bearing: Sony's shader declares a 64-lane
guest wave and the translator implements that guest wave across two host
wave32 subgroups, using LDS for the cross-subgroup half. Letting the AMD driver
select a different host subgroup size changes that emulation contract; the old
default produced a different property-buffer hash and invalid cross-lane
results. The corrected t=6.5
property readback is SHA-256
`AB0FE483C70D8B9E9E83A6F239C00308D6D4F43EC1DD7365FF405C9713EA6F33` and
contains 40 populated records. The probe now refuses particle execution on a
device that cannot require wave32 instead of silently producing approximate
motion.

The particle-ID allocator is no longer guessed. Constructor `0x94020`
allocates the shared `6000 * 0x44` property buffer at `0x944a9`, then allocates
two `6000 * 4` ID buffers at `0x9452f` and `0x9465a`. Both loops execute an
inside-out Fisher-Yates permutation of the zero-based IDs `0..5999` using the
continuous renderer-global xorshift128+ state initially stored at
`0xD60E88/0xD60E90` (`0x112210F47DE98115`, `0x7B`). Their SHA-256 values are
`428b84f2d1f34a1173304a50ae4f048b962b158f3f80c1f6e4c72d881d6f5ab3`
and `3fedba3856f72c2d610eb877ff6c0cc3f5c9b8466e64bd06e417115a094929ab`.
Large-compute initializer callback `0x978e0` copies descriptor `+0x72c` (the
first permutation) into `ResourcesCs.particleIds1`. The enclosing initializer
at `0x94ed0` invokes that callback for both large groups. Although the event
evaluator also exposes `+0x73c` through its generic descriptor lookup table,
none of coldboot's direct assignments uses opcode 11, the only operation that
can copy an alternate 16-byte descriptor. Both coldboot groups therefore keep
the first permutation. This is byte-pinned against the 4.03 eboot in
`test_ps5_particle_patterns.py`.

This recovered allocator exposed the missing first large-particle bank. It
starts at native t=0 with four particles and `particleOptions=0x1101`; the
spawn bit remains set through the authored 0.1-second event and is cleared for
the first step at or after 0.116667. At t=6.5 it produces four populated native
property records and the original large-particle vertex/pixel pair renders a
visible pass. `render_ps5_native_particle_sequence.ps1` now executes this bank
from t=0, preserves the accepted second-bank pass, and combines them in the
native `ONE/ONE/ADD` draw order. A five-frame 10 fps proof changes between
371,060 and 2,072,726 pixels between adjacent frames.

The older second-bank sequence passed direct visual inspection, but its
sequential one-based IDs were a probe initializer, not firmware behavior. It is
retained only as a regression reference. The live shell now prioritizes the
firmware-proven primary shuffle; future comparisons must distinguish visual
acceptance of the shader/texture appearance from correctness of the simulation
state feeding it.

That proof frame now has a production compositor seam. `ShellBackground` owns
an additive native-particle layer above the plate and exposes it only for
`ColdBootAnimation` while motion is enabled. The `shell-shot`
`native-background` scene exercises the same control off-screen. The cache
generator emits both PNG fallback frames and per-bank shader/five-buffer draw
snapshots. With `SHARPEMU_PS5_NATIVE_DRAW_CACHE`, the shell runs those snapshots
through `VulkanPs5NativeParticleRenderer` in-process, decodes the user's GNF
sprites, composites the native two-bank order, and advances from its live
clock. Two PNG-free captures differ, proving the control is not static.

The production large-group draw is now one native render pass. A single
`VulkanPs5NativeParticleRenderer` belongs to the cache frame source for its
whole lifetime, retaining the Vulkan device, original large-particle shader
pair, decoded GNF textures, pipeline, descriptors, target and readback storage
between frames. While the seven-second overlap is active it submits group 1
then retiring group 0 to that same target under the recovered
`ONE/ONE/ADD` state, matching `RenderInternal`'s group walk. Previously each
group was rendered into a separately cleared target and combined afterward by
CPU subtraction of the duplicated clear colour. That approximation is no
longer used by the live source. A mixed cache whose frames or groups name a
different shader pair is rejected instead of pinning frame zero's programs
over incompatible draws.

The draw cache now also carries the recovered bank-0 compute SPIR-V, constructor
resource blob, and first native ID permutation. The shell executes that shader
in-process at the selected native time, substitutes its property readback into
vertex binding 2, then performs the two native draws. Its `t=6.416667` property
hash exactly matches the generator (`67A18FD3...A697A`). The second bank is
replayed in-process too. With the firmware-proven primary ID descriptor, its
spawn-window program produces `1F5A9938...91181D` and 40 populated records at
`t=6.416667`; `AFA477C4...A484A8` is retained only as the older one-based
reference. The production source now executes bank0 first, continues bank1
from bank0's 408,000-byte output, and binds the resulting
`E7439935...FD226B` shared allocation to both draw passes. Cached property
bindings are fallback only. The coldboot group interval is live too: selector 1
activates group 1 at native time 6.0, group 0 retires at 13.0 after the recovered
seven-second overlap, and draw eligibility uses the firmware's inclusive
`time + step <= groupEnd` comparison. The state-3/state-4 transition delays and
ramp are encoded without inventing semantic names for those raw values.
Direct resource evaluation for every body and routing the full shell state set
remain required for the finished renderer.

`ParticleBoids` (`particle_boids_vv`, `particle_boids_p`) is a separate flocking
variant of the same system; its parameters were not chased.

## Basemat

**Not recovered.** This is the honest gap in this pass.

A shader at `0xda9b9e` looked at first like the basemat - it carries
`cbMat { matT, bgColor, alphaParam }`, `cbStable { colConvR, colConvG, colConvB }`
and a `dist1d` texture. It is not: `dist1d` is fed by a function
(`0x000ab3f0`, assert string `setupDist1dData`) that logs
`SetupOpticalParam failed update distortion1d texture` and converts 1024 entries
of three float arrays to fp16 per eye variant. That is **VR lens distortion**,
and `cbMat` is a transform matrix, not a background mat. Recording the
near-miss so it is not repeated.

The managed side's `SetBGBasematNative` string exists at `0xb6c592` but has no
code reference Ghidra could resolve, so the dispatcher was not reached in the
time available. `Flat` / `Linear` / `Ellipse` geometry remains **APPROXIMATED**
in our implementation, exactly as `docs/ps5-background.md` says.

## Reconciliation with `docs/ps5-background.md`

### Confirmed

- **The wave is native and parameter-driven.** Confirmed, and the parameters are
  now enumerated.
- **The managed side pushes one opacity.** Confirmed: `waveOpacity` is a named
  member of the wave constant buffer (`0xd9338c` and four sibling stages),
  distinct from a separate `opacity` in the same buffer.
- **A mask-wave switch exists.** Confirmed: `SetMaskWaveNative` is a registered
  internal call (`0xb6c5d0`).
- **The two `.gnf` files are opaque soft light fields, not mote sprites.**
  Confirmed, and their consumer is now identified.
- **Light modes select a simulation the managed layer does not own.** Confirmed:
  the light modes map onto a 4-bit *pattern* index that drives a set of
  rendezvous attractors in a compute shader.

### Corrected

1. **"The native renderer is not available."** It is. It is the ShellUI process
   `eboot.bin` itself, and Ghidra's stock ELF loader reads it. The managed
   assembly imports nothing graphical because `BGLayerNative` is a set of Mono
   internal calls, not a P/Invoke into a shared library. Anyone repeating this
   search should start from `eboot.bin`, not from `common_ex/lib`.

2. **"The wave palettes live in the native shader."** They do not live in the
   shader; they live in a **data table** at `0xbd1fd0` that the shader samples
   through six ramp textures. They are recoverable numbers, and 21 presets of
   them are recorded above.

3. **The wave is a model, not a field.** `ShellWaveLayer`'s "three soft sine
   ribbons" is structurally wrong, not merely differently tuned. The firmware
   draws a skinned animated mesh loaded from `/app0/wave/wave0.fbxd` and
   `wave1.fbxd`.

### Extended

- The layer has exactly **eight** drawable node types, named above.
- The background plate is a **three-stop Catmull-Rom gradient plus a positioned
  coloured light**, chosen from **37** authored records at `0xbd0ed0`.
- The wave has **three** implementations in the binary (`Wave`, `FirstWave`,
  `DualWave`), of which only `Wave` is preset-table-driven.
- Small light particles are **lit and depth-of-field blurred**; large ones are
  **HSV-tinted bokeh discs** sampling the two `.gnf` textures.
- All ramp interpolation in this layer is **cubic Hermite / Catmull-Rom**, never
  linear. This is the cheapest single fidelity win available to us.

## What is still unknown

### 12.40 themed-light background correction (2026-08-02)

A current-console reference supplied during the RNW migration shows a near-black
room/light-field: a broad warm vertical shaft at the upper left, a soft pool at
the lower left, and subdued animated grain/folds. This is **not** the opaque blue
Bottom capture previously replayed by the RN shell.

Newer firmware provides a concrete native route for this observation. The
carved managed image from 12.40
`Sce.Vsh.ShellUI.BGLayer.dll.sprx` adds
`BGLayerNative.SetThemedLightColorNative(in Vector4)` and
`SetColorThemeNative(int colorTheme, int themeAppliedUser)`. `BackgroundLayer`
exposes the matching `SetThemedLightColor` and `SetColorTheme` entry points.
The 12.40 ShellUI theme image also exposes `ThemeParam.BackgroundLightColor`,
`ThemeConstants.DefaultThemeBGLightColor`, and the design-token keys
`background.enabled`, `base-mat.overlay.enabled`, and `focus.stroke.enabled`.
This is strong source evidence that the current dark light-field is rendered
and theme-driven rather than a renamed static wallpaper.

Do not conflate that pass with older recovered material:

- 4.03 Home preset 4 still resolves exactly to blue Plane2 record 2.
- `bg_NPXS40032.dds`, `bg_NPXS40110.dds`, and `bg_NPXS40144.dds` are static
  per-title hub images (PS Now, disc player, and unsupported-title surfaces),
  not the persistent Home/Settings base.
- the warm portion of `initial_boot_movie.mp4` is disposed when boot playback
  ends and is not evidence for steady shell ownership.

The native implementation is now identified in the 12.40 NPXS40087 executable.
The persistent pass is the full-screen `fw_background_p` FirstWave shader, not
an image: it uses projected/normalized coordinates, `fract(time)`, sine/cosine,
hash noise with seeds `23189` and `13181`, phase `0.04774648`, and an
`exp(-14.42695045 * r^2)` light field. The complete procedural folded-room stack
is `fw_flow_vl/h/dv -> fw_oit_p -> fw_comp_oit_p -> blur H/V -> fw_fxaa_p`.
12.40 contains no `.fbxd` dependency for this stack.

The shared FirstWave constant-buffer ABI is now recovered: projection at
`0x40`; BackgroundColour0/1 at `0x110`/`0x120`; BackgroundLightColour at
`0x130`; Reflection, Environment, and Edge at `0x140`, `0x150`, and `0x160`;
opacity/time at `0x180`/`0x184`; and screen dim at `0x190`. Reset selects palette
record 4. Its exact vectors, divided by 255 at upload, are BG0
`(-20,-20,-10,255)`, BG1 `(81,160,245,255)`, light `(22,57,79,255)`,
reflection `(90,60,230,255)`, environment `(15,15,15,255)`, and edge
`(123,123,123,255)`.

The remaining work is translation and validation of that shader stack plus the
managed Settings-to-owner selection call. Until it is translated, the product
must keep the proven `#020408` basemat as a visibly incomplete fallback and must
not recolour the 4.03 blue Plane2 shader or repurpose title art under a 1:1
claim. Home may add the separately recovered particle overlay; Settings must
gate that overlay off while retaining the animated FirstWave base.

### Multi-firmware asset audit (2026-08-01)

The missing-model result is not limited to the original 4.03 reconstruction.
A path audit covered every file under the available 3.02/3.00 material, the
4.03 reconstruction, the decrypted 9.00 system image, and the 12.40 system
dump. Both later dumps contain `system_ex/app/NPXS40087/eboot.bin`, but their
application directories contain only `psm/`, `eboot.bin`, and
`libScePSNowGkp.sprx`; neither contains `wave/wave0.fbxd` or `wave/wave1.fbxd`.
No copy of either model, or of the two named BGLayer particle GNF files, exists
elsewhere in the available game/firmware trees. Therefore enabling the current
background-motion preference cannot yet run the native wave honestly: its
authored geometry and animation curves are absent across all locally available
firmware versions, not merely overlooked in 4.03.

- **The old wave-mesh path.** `/app0/wave/wave0.fbxd` and `wave1.fbxd` are absent
  from the dump. Shader-level reconciliation in `C:\sharpemu\docs\bglayer-shaders.md`
  shows that this path is dead in 4.03: `wave_bg_*` is the fullscreen plate and
  the moving home body is the compute-driven particle field. The missing mesh
  is therefore not a blocker for the faithful 4.03 home background.
- **Which of the 21 separate wave-hue presets a customized user theme selects.**
  The steady Home/Settings *plate* route is now proven as record 2. The separate
  hue selection still involves `sceShellCoreUtilGetSystemBGWaveColor` and the
  per-user theme-wave-colour setting and matters when the dormant/model wave or
  theme customization path is brought up.
- **The nested particle records.** The seven-way selector, exact blob pointers,
  exact byte lengths, packed name field, serialization version, 25 vector
  cardinalities, container sizes and payload boundary are now recovered above.
  Fields 8-22 now expose concrete counts, lifetimes, spawn ranges, curl values,
  rendezvous entries, and routed draw parameters. The complete 29,760-byte
  `.shader_text` section of `particle_c` now decodes to 5,024 instructions.
  With `COMPUTE_PGM_RSRC1=0x402c00d1`, `COMPUTE_PGM_RSRC2=0x88`, a 64x1x1
  launch, the exact coldboot `large_compute[1]` state, and
  genuine `SRTCs`/ID/property descriptors, SharpEmu's scalar evaluator resolves
  four memory bindings without fallback and emits SPIR-V. The exact 6.0-second
  spawn interval (`particleOptions=0x1101`) has also been dispatched on Vulkan:
  it writes 40 `ParticleProperty` records selected through the constructor's
  native zero-based ID permutation, with finite positions, velocities, bases
  and lifetimes. The stateful probe now continues
  that buffer to 6.5 while applying the 6.1 spawn-bit event, then feeds it to
  the visible original draw pair. Starting a zeroed buffer at 6.5
  correctly produces Sony's `(100000,100000,100000)` off-screen sentinel,
  because field 10 clears spawn bit `0x1000` at 6.1; timeline continuity is a
  real input, not optional polish. The constructor's two ID permutations and
  RNG seed are recovered; native binding traces select the first permutation
  for the first active renderer group. Still unresolved is the meaning of
  counts 0-7 and later group/ping-pong turnover. Do not turn the still-raw
  leading u32 values into motion constants by analogy.
- **The basemat.** See above.
- **`ParticleBoids`** parameters.
- **Pass ordering.** The `INFERRED` claim in `docs/ps5-background.md` that the
  wave composites above the image was neither confirmed nor contradicted here.
