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

All EXACT.

## What the wave actually is

**It is not a procedural field.** It is a skinned, animated 3-D model, coloured
through ramp lookup textures, drawn over a separate gradient plate and then
anti-aliased.

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
`coldboot/colorchange/colorchange.`. Each is embedded at the head of a multi-
kilobyte binary blob in read-only data from `0xbb0dc4` onward. Those blobs are
the per-pattern data and are **not decoded** - see gaps.

### The two draw paths

The render function (`0x00096860`, assert string `RenderInternal`) walks **two
particle groups**, and per group:

- **8 compute buffers** are simulated (indices `0x33`-`0x3a` of the node, group
  stride `0x140`).
- **8 small-particle draws** are issued; each copies a 320-byte resource block
  and draws `numParticles * 6` indices - **6 indices per particle, i.e. a quad**.
- **2 large-particle draws** are issued.

A group only runs while `time + timeStep <= groupEndTime`. All EXACT.

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

- **The wave's shape.** `/app0/wave/wave0.fbxd` and `wave1.fbxd` are absent from
  the dump. Without them the silhouette, the vertex `uvAlpha` / `volColBoundary`
  authoring and the animation curves cannot be recovered. Everything about the
  wave's *colour* is now known; nothing about its *form* is.
- **Which of the 21 wave presets and which of the 37 background records the home
  screen selects.** The selection path runs through
  `sceShellCoreUtilGetSystemBGWaveColor` / `GetSystemBGState` and a per-user
  theme setting, and was not traced.
- **The particle pattern blobs** at `0xbb0dc4` onward (`coldboot`,
  `spread_expanded`, `bottom_fadeout`, ...). Their format was not determined, so
  the concrete emission counts, lifetimes, spawn ranges and rendezvous-point
  positions per mode are **not** recovered - only the parameter *names* and the
  simulation model are.
- **The basemat.** See above.
- **`ParticleBoids`** parameters.
- **Pass ordering.** The `INFERRED` claim in `docs/ps5-background.md` that the
  wave composites above the image was neither confirmed nor contradicted here.
