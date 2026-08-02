# The PS5 focus highlight

How the console draws the thing that marks the selected item, recovered from the
4.03 firmware. Every value here is measured; anything unrecovered says so.

## Where the shaders actually live

Not in the shell eboot, and not in the managed `Sce.PlayStation.PUI` assembly.
They are embedded AMDGPU ELFs inside **`libScePsm.sprx`**, which hosts PUI's
native renderer:

| shader | `InternalShaderType` | file offset | size |
|---|---|---|---|
| `AreaFocus` | 34 | `0x004F5AE0` | 5,472 B |
| `LineFocus` | 35 | `0x004F7040` | 5,840 B |
| `FocusUI3` | 36 | `0x004F37D0` | 8,976 B |

`libScePsm.sprx` carries 81 embedded shaders in total (ELF magic with
`e_machine == 224`).

An earlier search enumerated the shell eboot's 160 embedded shaders and found
nothing, because they are not there. What found them was scanning each embedded
ELF for the **uniform name strings** rather than for a shader name -
`ColorTable`, `IsLine`, `StrokePosition`, `NoiseScale`, `Shimmer`, `AlphaGamma`,
`MinOpacity`. Each shader then identifies itself by which uniforms it carries:
the line pass has `StrokePosition`, the area pass has a colour table without
one, and `FocusUI3` has `IsLine` because it branches on it.

### Reproducing the disassembly

Two facts are load-bearing; without either you get nothing:

- The disassembler is the SDK's own `libSceShaderIsaP.dll`
  (`prospero-sdk-10.00/sdk/host_tools/bin/`), generation **2** (gfx1013).
- The harness runs under **net8**, not net10.

The first ~0x100 bytes of each blob are ELF header and decode as garbage; the
real instruction stream starts after it. The area pass is 697 real instructions.

## What the area shader computes

Read off the instruction stream:

```
q  = |p| - halfExtent + radius
sd = length(max(q, 0)) - radius
```

A **rounded-box signed distance field**. This is the part a stroked rounded
rectangle cannot imitate: the falloff is a true distance, so it stays even
around the corners instead of bunching where the arc tightens. It is why the
console's highlight reads as light resting on the tile rather than a border
drawn around it.

Shaping that follows, from the instruction census:

- a **smoothstep** - the `-2t+3` polynomial appears literally, as
  `v_madak_f32` against `#0x40400000`
- **17 `v_log`/`v_exp` pairs** - `pow` as `exp2(log2(x) * n)`; the alpha gamma is
  one of them
- **one `sin` and one `cos`** alongside `1/(2*pi)` (`0x3e22f983`) - the shimmer's
  angular term, a single rotation rather than a loop
- **26 clamp-family ops**, 8 reciprocals, 3 square roots
- **two `image_sample`** - a single-component fetch (noise) and a `dmask:0x7` RGB
  fetch (the colour table)

## Architecture

The highlight is **two full-quad shader passes**, not a border, drawn by a
pooled widget that is not a child of the target but is transform-slaved to its
screen rect.

- **At rest both passes remain visible.** `FocusRenderWidget.SyncOpacity()`
  assigns the line `num * Showing * (1 - Moving * 4)` in the `Shown` state.
  The only forced-zero branch is a state that is neither `Showing` nor `Shown`
  while `Showing == 1`.
- **The area pass is size-gated**: skipped entirely when
  `(w/W) * (h/H) >= 0.4` (`AreaRenderingThrethold`, the source's spelling). A
  large focused item gets the line only.
- **During a move the line abandons its ring mesh** and draws a plain full quad,
  snapping back once movement ends.
- **The line is blanked for the first quarter of any move** - the opacity term is
  `1 - Moving * 4`.
- Corner radius is **inherited from the focused widget**, never hard-coded.
- The ring, when drawn, is a 16-vertex triangle strip over an 8x8 grid with
  outer-corner factor `2 - sqrt(2)`.

## Constants

| name | value |
|---|---|
| `LineThickness` / `LineOffset` | 3 / 3 |
| `AreaEdgeFadeLength` / `AreaEdgeFadeOffset` | 5 / 0 |
| `EdgeFadeMinLength` | 10 |
| `MaxInOutExtendingLength` | 80 |
| `LineScaleRatioOnHiding` | 1.2 |
| `AreaRenderingThrethold` | 0.4 |
| `LineNoiseScale` / `AreaNoiseScale` | 5 / 5 |
| `LineMinOpacity` / `AreaMinOpacity` | 0.065 / 0 |
| `LineAlphaGamma` / `AreaAlphaGamma` | 1.0 / 0.8 |
| `NoiseMoveFrequency` | 0.25 |
| `ShimmerSpeed` / `ShimmerFrequency` | 1 / 5 |
| `AreaOpacityDecreaseRateBySize` | 30 |
| `AreaOpacityMinimumDecreaseValueBySize` | 0.5 |
| `AreaWarpFadePixel` | 80 |
| `AreaWarpFadeThreasholdRatio` | 0.1 |
| `WarpGradientCurveByRatioIntensity` | 0.2 |
| `WarpGradientCurveToeValue` | 0.3 |
| `RadialMaskIntensity` | 0 (off) |
| `MorphByRatioIntensity` | 0 |

The gammas are stored as above but passed to the shader as the **reciprocal**
(`1/0.8 = 1.25`).

## Motion

Its own curves, not the `ParametricAnimationCurve` family used elsewhere:

| motion | curve | duration |
|---|---|---|
| moving between items | `1 - (1 - 0.5t)^5` | 0.3 s |
| show / hide / pressing | `1 - (1 - 0.5t)^10` | 0.3 s |
| warp | - | 0.25 s |

Both land short of 1 by design (0.969 and 0.999), the same house style as the
parametric easings.

The rect **morphs** rather than slides: `RectX`, `RectY`, `RectWidth`,
`RectHeight` **and** `RoundedCornerRadius` are all `[AnimatableProperty]`, so
position, size and corner radius interpolate together.

The idle shader clock is global to the UI3 focus manager, not local to one
visible focus widget. In the 4.03 embedded managed PE,
`FocusRenderManager.startTime` is a static snapshot of
`UIValues.CurrentUITime`; every update writes:

```csharp
time = DebugDisableAnimation
    ? 0.0
    : (UIValues.CurrentUITime - startTime).TotalSeconds;
```

`CalcNoiseChangeParam` and `CalcShimmerParam` both consume that value. Hiding,
pooling, or recreating a focus widget therefore does **not** pause or restart
the orbit/shimmer phase. The host implementation uses the same process-wide
monotonic focus clock; its deterministic capture path may still drive the
clock manually.

## Colour

Not one colour - a seven-entry table uploaded as a `Count x 1` RGBA8 texture and
sampled by the shader. In order, 8-bit:

```
204,255,255   199,227,255   229,229,255   187,196,237
235,199,223   255,223,191   255,204,204
```

Pale cyan through blue and lavender to pink and peach. Per-pass filter colours
default to opaque white and are overridable per widget.

## The noise uv, traced

From the area shader at `0x021c`-`0x02ec`:

```
uv = (p / noiseScale + noiseOffset) * 0.5 + 0.5
```

`p` is the interpolated local position, `noiseScale` comes from the constant
buffer at `+0x50` (reciprocal taken once), `noiseOffset` from `+0x58`. The
`* 0.5 + 0.5` is the usual remap of a signed coordinate into texture space.

The sampler state is also closed. `FocusRenderManager.NoiseImage` calls only
`Texture.SetFilter(TextureFilterMode.Linear)`. The embedded 4.03
`Sce.PlayStation.Core` implementation initializes `TextureState` to Linear
filtering and `TextureWrap(ClampToEdge, ClampToEdge)`, so the focus texture is
**linear + clamp-to-edge**, not repeating. Normalized sampling uses texel
centres at `(n + 0.5) / size`. This matters because the sine/cosine noise offset
regularly pushes the computed UV outside `[0,1]`; wrapping there produces a
plausible but non-Sony moving pattern.

## The shimmer and its disabled rotating mask, traced

The area shader contains a rotating diagonal-mask subpath at
`0x0224`-`0x0330`:

```
angle    = time * (1 / 2pi)              ; 1/2pi is 0x3e22f983
s, c     = sin(angle), cos(angle)
swept    = (s * y) - (c * x)             ; project onto the rotated axis
swept   /= |c| + |s|                     ; L1 norm - keeps the band width
                                         ; constant at every angle
band     = swept * 0.5 + 0.5
t        = saturate((band - threshold) / (1 - threshold))
shimmer  = pow(band * t * t * (3 - 2t), gamma)
```

The `|cos| + |sin|` normalisation is the neat part: without it a diagonal sweep
would cover a longer span than an axis-aligned one and the band would appear to
breathe as it rotates. `threshold` and `gamma` come from the constant buffer at
`+0x64`.

That is not, however, the stock card's continuously visible idle wash.
`FocusRenderManager` sets `ShimmerGradientMaskIntensity = 0` and
`ShimmerGradientMaskGamma = 1`, disabling this rotating-mask contribution at
the managed default. The visible stock shimmer is the separate two-channel
`ShimmerParam` produced by Sony's `CalcShimmerParam(time, 1, 5)`: both channels
remain at -1 for the first three seconds, then sweep across the card during the
last two seconds of each five-second cycle. Calling the ring "static" from a
capture inside that quiet interval is therefore ambiguous; the line's noise
must still move continuously, while the area is allowed to be transparent.

A separate +/-45 degree basis appears alongside it - `0.70710`, `-0.70710`,
`0.35355` (`1/sqrt2` and `1/(2*sqrt2)`) - the fixed diagonal ramp.

## The colour table uv, traced

The table is **indexed by the computed intensity itself**, not by position. At
`0x0374` the accumulated intensity becomes the sample coordinate, so the seven
entries are a *ramp the alpha walks along*: faint edges pick up one end of the
table, the bright core the other. That is why it reads as a colour gradient
without any positional gradient being drawn.

Just before it, at `0x035c`-`0x0364`, the intensity is blended toward a floor of
`0.15` by a constant-buffer weight - the min-opacity term.

## The colour conversion, traced

The four constants that were previously left out as an unidentified group are a
**colour-space conversion**, at `0x0420`-`0x04f0`:

```
c = pow(rgb, 2.35)                       ; decode, 0x40166666
c = M * c                                ; 3x3, below
c = c * 0.025                            ; 0x3ccccccc
c = pow(c, 1/2.2)                        ; re-encode, 0x3ee8ba2e
```

The matrix, as emitted (note the row order in the instruction stream is G, R, B):

```
0.06480824   0.9353735   -0.0001436224
0.6398802    0.3273893    0.03271094
0.01050037   0.07885341   0.9105756
```

Each row sums to 1 within float error (1.000038, 0.999980, 0.999929), which is
the signature of a white-preserving chromaticity conversion. Reordered to RGB it
is close to a Rec.709 to Rec.2020 primaries matrix but does not match the
standard one exactly, so the precise pair is **not asserted here** - what is
measured is the matrix, the two gammas and the scale.

The whole block is branch-gated on `s0` (`s_cmp_lg_u32 2, s0` and
`s_cmp_lg_u32 1, s0` at `0x0408`/`0x0410`), i.e. it runs only in particular
display modes - an SDR/HDR split.

**This is why guessing would have been wrong.** The group looked like a
hash/gradient set for the noise. It is a wide-gamut conversion, and inventing a
noise function from it would have produced something arbitrary.

## What is implemented here, and what is not

Implemented from the recovered values: separate line-band and translucent area
wash passes; the distance field, smoothstep, alpha gamma, size gate,
size-dependent falloff, seven-entry colour table and intensity indexing; the
recovered noise texture and moving UV; Sony's two-channel five-second shimmer;
focus curves, durations, and geometry constants. The shader's rotating mask is
not added because Sony disables its contribution at the stock managed default.

**Recovered since the shader trace:** the bound noise is
`image_focus_noise`, a 64x64 indexed PNG in
`Sce.PlayStation.PUI_UI3.rco`. The renderer opens it read-only from the user's
firmware dump and uses the traced moving UV for the line's colour-table lookup.

The former deliberate divergence evaluated the field on the CPU into a small
scaled grid rather than running the original shaders. That implementation is
retained only as the startup or missing-firmware fallback; the firmware-backed
path described below now executes the original Area/Line fragment and vertex
programs.

### Original-shader execution checkpoint

The deliberate CPU-grid divergence now has a hosted replacement.
`Ps5NativeFocusCompiler` reads `AreaFocus` and `LineFocus` directly from the
user's 4.03 `libScePsm.sprx`, with the serialized AGC header and decoded shader
agreeing on this contract:

| resource | user SGPRs | size |
|---|---:|---:|
| colour-table texture | `s0..s7` | 7x1 RGBA8 |
| `image_focus_noise` | `s8..s15` | 64x64 |
| colour/noise samplers | `s16..s23` | two 16-byte descriptors |
| focus constants | `s24..s27` | Area 128 B; Line 160 B |
| global display constants | `s28..s31` | 8 B |

Those are the fragment-stage records. The dedicated focus vertex programs use
their own constant records: Area is 112 bytes and Line is 116 bytes. Line's
extra final float is `u_InOutScale` at `0x70`; it supplies the scaled
`FrameSt.zw` export rather than belonging to the fragment record.

Both headers carry `PGM_RSRC1=0x022c0142`; `SPI_PS_INPUT_ENA` and
`SPI_PS_INPUT_ADDR` are both `2`, with three interpolated attributes. The
translated Area and Line programs validate as Vulkan 1.2 SPIR-V and create
graphics pipelines on the AMD Radeon Pro V620. Execution tests run the original
pixel programs against Sony's seven-entry colour table and both controlled and
firmware noise textures. The production line path now uploads the recovered
per-frame managed record and matching three packed vertex attributes;
diagnostic alpha output is never used live.

This checkpoint also exposed a host-wave defect: the V620 fragment subgroup is
64 lanes and cannot request wave32. The translator formerly sent both guest
wave32 halves to lanes 0..31, producing alternating scanlines. DPP shuffles,
ballots, EXEC masks, and per-wave branch predicates now select the current
32-lane half inside a host wave64. The corrected proof covers every target
pixel; literal `v_readlane`/`v_readfirstlane` on a wider host subgroup remain a
separately announced limitation and neither focus program uses them.

### Persistent live-render boundary

The host layer is implemented and both original focus passes are now active in
the visible shell. `Ps5NativeFocusUniforms` packs the AreaFocus 128-byte and
LineFocus 160-byte fragment records from the decompiled 4.03 `FocusRenderManager`
formulas, including the normalized radius/thickness/offset, noise orbit,
five-second shimmer, size falloff, warp fade, morph term, stock line spline,
`ShowAlpha`, and the 8-byte global alpha/intensity record. The three fragment
high-level inputs are closed by ELF metadata as `input.Color`,
`input.ClipPos`, `input.St`, and `input.FrameSt`. Sony's dedicated Area/Line
vertex programs pack those into three parameter exports: Color; St.xy with
ClipPos.xy; and FrameSt.xy with the line program's scaled FrameSt in zw. The
host now executes those original dedicated vertex programs, with the recovered
112-byte Area and 116-byte Line vertex constants. Line's `u_InOutScale` is
written at `0x70`. The dynamic focus viewport is cropped to each pass while
the vertex constants preserve the global-screen transform, so exported
`ClipPos` remains global rather than being incorrectly rebased to local NDC.

The line cbuffer has a packing difference reflection order alone concealed.
The ISA loads `u_NoiseScale` at `0x4c`, `u_MinOpacity` at `0x50`, and
`u_NoiseChangeParam.xy` at `0x54/0x58`; `0x5c` is padding before the tone
vectors. Supplying the Area offsets to LineFocus made the visible ring flat and
made two clock samples byte-identical. These instruction-confirmed offsets make
the original `ShowAlpha=0` output both non-transparent and time-varying.

`VulkanPs5NativeFocusRenderer` keeps separate AreaFocus and LineFocus shader
modules, textures, descriptors, pipelines, targets, and readback buffers alive
between frames. The decompiled `FocusRenderWidget.SyncTransform` proves that
AreaFocus owns the exact no-margin card rectangle while LineFocus owns the
larger focus plane, so the host now renders the area at card size and centers
it under the line target instead of incorrectly stretching both passes over one
surface. It uploads only c0/c1 on a frame. This removes per-frame Vulkan
instance/device creation from the prospective live path.

The original shader's diagnostic `ShowAlpha=1` branch remains test-only. The
stock `ShowAlpha=0` branch now renders through the persistent path, changes when
the Sony noise resource is replaced, and produces different RGBA frames at
different recovered clock values. `ShellFocusRing` reads the original 7x1
colour table and decoded `image_focus_noise`, renders AreaFocus and LineFocus asynchronously,
drops intermediate frames when Vulkan is busy, and retains the last valid frame
while geometry is stable. The shader-derived CPU ring is only the startup or
missing-firmware fallback.

AreaFocus is now native and active in the product surface. Decompiled
`UIRenderer.Draw` proves that PUI always begins `u_Flag` with clip-coordinate
bit `0x08`; its round-corner path adds `0x04`, and framebuffer colour format
occupies bits 20-23. The earlier local-fullscreen stand-in supplied local NDC
where Sony expected global-screen `ClipPos`, which projected a dark diagonal
wedge. Hosting the original Area/Line vertex path closes that transform: the
renderer uses a cropped dynamic viewport for each focus target while the
vertex constants and position input retain global design-space coordinates.
The native Area shimmer is consequently clipped cleanly to the card and has
replaced the CPU area wash in the firmware-backed path.

The recovered paper-white/display-output conversion is implemented but remains
opt-in through `SHARPEMU_PS5_FOCUS_PAPER_WHITE`. It is not the default because
the shader branches by display mode and the active console mode has not yet
been established by a paired capture. The latest line/area output has also not
been visually certified against a console capture. Therefore the current focus
renderer is firmware-derived, but it is not yet a justified 1:1 claim.

Idle line motion depends on the original `image_focus_noise` payload. The shell
loads it in place from `Sce.PlayStation.PUI_UI3.rco`, found through
`SHARPEMU_FW_DUMP` or `SHARPEMU_PS5_UI3_RCO`. If neither route resolves, the
sampler deliberately returns a constant 0.5 rather than inventing a substitute;
the result is a visibly static line. This is an asset-routing failure, not a
Sony focus state. `shell-shot --scene focus-idle` holds geometry fixed and
advances only the recovered focus clocks, making that failure easy to detect.
With the 4.03 RCO routed, the 1 s and 2 s proof captures differ in 12,958 pixels
inside `(711,371)-(1209,709)`, exactly the stationary focus surface.
The current compositor-frame validation produced 13,022 and 13,024 changed
pixels at 1 s and 2 s respectively, in that same bounding box. `shell-shot`
now fails instead of producing a false motion proof when `--firmware-root` was
supplied but `image_focus_noise` could not be loaded. Changing the configured
firmware location and invalidating the UI3 icon library also invalidates the
focus texture's failed-probe cache, so a restart is no longer required.
The full five-second `focus-idle` capture also isolates the card interior:
seconds 0-3 and 5 contain zero non-background interior pixels, while second 4
contains 128,738. That matches `CalcShimmerParam`; adding a constant white fill
during the quiet interval would be authored behavior, not recovered firmware.

The firmware sampler audit corrected two host divergences that those earlier
counts did not detect: the texture now clamps rather than repeats, and the live
focus clock continues while the pooled highlight is hidden. With the same 4.03
RCO, the corrected 1 s to 2 s fixed-card capture changes 13,025 pixels in
`(710,370)-(1210,710)`. The similar count is expected—the correction changes
*which field is sampled*, not Sony's deliberately slow 0.25 rad/s rate.

The earlier line-only acceptance capture is
`tmp/native-focus-live3/focus-idle_{0000,1000,2000}ms.png`. All three PNG hashes
differ, and visual inspection shows the stationary 3 px line moving from the
lavender/cyan portion of the Sony table into peach. That capture predates the
now-hosted original vertex path; the former AreaFocus wedge condition is no
longer the product route.

Card focus and icon focus are separate presentation states. They may share the
recovered field mathematics, but the card requires the translucent surface wash
and shimmer while compact icons require their own thinner geometry. A single
uniform ring thickness is not the acceptance target.

## Why this matters

The previous highlight was a hand-drawn 3px white rounded rectangle that
translated between tiles. It was wrong in every dimension at once: wrong element
(the resting highlight combines the wash and stroke), wrong colour (one white
instead of a seven-entry ramp), wrong motion (a slide instead of a morph), and
missing the size gate, the falloff and the blanking entirely. No amount of
tuning a stroke reaches it, which is why it had to be read out rather than
approximated.
