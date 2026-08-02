<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell background — recovered composition and current implementation

A clean-room reference for the console's background layer and the current
SharpEmu implementation gap. The shell does not show a wallpaper: it composes
a clear plate, two double-buffered image slots each with a blurred companion, an
overlay slot, native background rendering, light particles, and a basemat under
the UI. This document records which parts are recovered and which are currently
implemented. Read `ps5-reactive-shell.md` first for the product-wide contract.

> **Current implementation correction.** Older revisions of this document
> described `ShellWaveLayer`, `ShellParticleLayer`, a 26-second image drift, and
> an invented `ShellParticlePattern.Ambient` as if they were the current runtime.
> Those approximations were removed. `ShellBackground` now draws the clear
> basemat-colour plate, `Ps5BackgroundPlate`, and an optional basemat. Its motion
> and focus-rectangle seams are deliberately inert. The native renderer has since
> been decompiled and its particle event/resource structure partly decoded, but
> that native path is not yet executed by the home shell.

**Values/behaviour only.** Nothing below is Sony source. The managed background
layer was read from a gitignored location; only extracted numbers, enum values
and behaviour are recorded here. No code is reproduced.

## Provenance

- **Managed layer:** the system shell's background-layer assembly from a 4.03
  firmware dump, decompiled to C# in a scratch directory outside the repository.
  It is a thin driver: it computes indices, flags and one opacity, and pushes
  them at a native renderer once per frame.
- **Native renderer:** decompiled from NPXS40087 and documented in
  `ps5-background-native.md`. Its scene graph, shaders, resource routing, event
  records, and many field names/values are recovered. Full record semantics,
  default-home selection, and host execution remain incomplete.
- **Textures:** `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and `Particle1.gnf`
  under `filesystems/system_ex/vsh_asset` in the same dump.
- **Implementation:** `src/SharpEmu.GUI/SystemAssets/Shell/ShellBackground.cs`,
  `ShellBackgroundComposition.cs`, `src/SharpEmu.GUI/Ps5Home/Ps5BackgroundPlate.cs`, and
  `SystemAssets/Textures/GnfImage.cs`.

## Layer order

Bottom to top:

| # | Layer | Source of the ordering |
| - | ----- | ---------------------- |
| 1 | Clear colour | The layer has its own `SetClearColor` entry point and a "clear colour is transparent" flag, both independent of everything else. EXACT that it exists and is separately settable. |
| 2 | Image slots | The old image, then the current slot's blurred companion, then its sharp image, then the overlay. See below. |
| 3 | Wave | INFERRED, see the argument below. |
| 4 | Light particles | Light is drawn in front of the background body; the modes that emit are exactly the modes with no background art of their own. INFERRED. |
| 5 | Basemat | EXACT. Setting the basemat also sets the UI framework's own "full-screen basemat type", i.e. it is the plate the UI draws its foreground over. |

### Why the wave is above the image, not below

The managed layer cannot answer this directly, so it is argued from two facts:

- The layer exposes a **mask-wave** switch that is turned on together with the UI
  framework's "stop focus animation for automated test" switch, driven by a
  registry value. Its only purpose is to make screenshots reproducible. Masking a
  layer that an opaque full-screen background image already hides would achieve
  nothing, so the wave must be visible over the background art.
- The per-frame diagnostic timings the layer reads back are ordered
  wave, wave-post, render-body, render-post, compositor - consistent with the
  wave being its own pass composited into the body rather than the base plate.

This remains **INFERRED**, not exact. Native renderer recovery has not yet
confirmed or contradicted this pass order; the outstanding trace is recorded in
`ps5-background-native.md`.

## The wave

| Property | Value | Status |
| -------- | ----- | ------ |
| Preset the layer selects for itself at start-up | `HomeScreen` (index 4) | EXACT |
| Preset enum numbering | 0 `InitialSetup`*, 1 `MiniApp`*, 2 `SystemArea`, 3 `MusicUnlimited`*, 4 `HomeScreen`, 5 `NoWave`, 6 `Black`, 7 `WhatsNew`, 8 `MusicUnlimitedSplash`*, 9 `Login`, 10 `LoginNoUserLogined`, 11 `Boot`, 12 `Store`, 13 `PsVideo`, 14-21 `ThemeFlow0..7` (* obsolete in 4.03) | EXACT |
| Show-wave flag | Set true once at start-up; the public "show wave" and "set preset" entry points are **empty** in 4.03, so in practice the preset never changes and the wave is always on | EXACT |
| Fade-in ramp | `opacity = min(1, opacity + 0.01)` per frame | EXACT |
| Fade-out ramp | `opacity = opacity * 0.9` per frame | EXACT |
| Frame | 60 Hz, 16.667 ms | EXACT |
| Visibility threshold | 0.0001 | EXACT |
| Focused rect | Pushed at the renderer every frame outside the theme-flow modes (theme flow is the `0x2x` band of the light-mode index) | EXACT |
| Colours, band shapes, motion | **RETIRED HOST APPROXIMATION.** The removed `ShellWaveLayer` drew three soft sine ribbons and a broad glow in project-authored colours. This was never native evidence. | RETIRED |

A full fade-out at x0.9 a frame takes ~88 frames (~1.5 s) to fall under the
visibility threshold - it is a tail, not a cut. The removed `ShellWaveLayer`
once copied this ramp on a vsync-paced clock; the current runtime does not draw
that approximation.

## Image slots

The renderer's texture map has fixed slot indices:

| Slot | Index | Role |
| ---- | ----- | ---- |
| `BackgroundImage0` | 0 | Sharp background, buffer 0 |
| `BackgroundImage1` | 1 | Sharp background, buffer 1 |
| `TransitionOldImage` | 2 | The outgoing image during a transition |
| `OverlayBackgroundImage` | 4 | Overlay art above the background |
| `BackgroundBlurImage0` | 5 | Blurred companion of buffer 0 |
| `BackgroundBlurImage1` | 6 | Blurred companion of buffer 1 |

All EXACT. Index 3 is a hole in 4.03.

How they combine (EXACT):

- A transition **flips the buffer id** (`0 <-> 1`) and maps the new sharp image to
  `BackgroundImage<id>` and its blur plate to `BackgroundBlurImage<id>`. The two
  buffers are what the cross-fade fades between.
- The blur plate is a **separately authored image with its own URI**, not a
  filter: the transition carries a "next blur image" URI alongside the "next
  image" URI, and a flag tells the renderer whether one was provided.
- A **fallback image** URI is used when the main image fails to load; when even
  that is absent the transition is run in fallback mode with no image at all.
- Transition types: `LaunchingGame` 0, `Hide`/`HideGameSplash` 1,
  `LaunchingGameBC` 2, `SystemDefault` 5, `CustomImageRipple`/`CustomImage` 6,
  `CustomImageSlideInLeft` 7, `CustomImageSlideInRight` 8, `CustomImageFade` 9,
  `CustomImageRippleBack` 10. Degrees: `CrossFade`, `Subtle`, `Normal`,
  `Strong` (default `Strong`), packed into the type word's high half.
- The transition **centre** is the focused widget's centre in screen pixels,
  normalised by 1920x1080. With no focused widget it is (960, 540), i.e. dead
  centre.
- The centre may instead be seeded from a **ring buffer of the last 3 focused
  rects**; a focus move under **10 px** of Manhattan travel is ignored, and a
  recorded position has to be at least **100 ms** old to be used.
- A game-capture background carries a **capture scale**, clamped to at most
  **2.0**; under **0.1** it is replaced by the display's pixel density.
- A background image wider than 1920 and taller than 1080 that is not a DDS is
  re-requested at 2K and swapped back to 4K later, both times with a
  `CrossFade`-degree ripple transition.
- A transition that has not finished within **10 s** is abandoned.

**APPROXIMATED in Prosperismo:** the dump ships no separate blur plate for its
hub backgrounds, so `ShellBackground` derives the blurred companion from the same
decoded pixels with a heavy box decimation. Everything else - the slot pair, the
buffer flip, the cross-fade, the overlay slot - follows the model.

## Light particles

The layer never names a "particle count" or "speed": it selects a **light render
mode** index, and the native renderer owns the simulation.

| Global state | Light mode index | Emission |
| ------------ | ---------------- | -------- |
| `None` 0 | 79 `None` | nothing drawn |
| `Black` 1 | 78 `Black` | nothing drawn |
| `ColdBootAnimation` 2 | 69 `ColdBoot` | spread |
| `WarmBootAnimation` 3 | 70 `WarmBoot` | spread |
| `InitialBootAnimation` 4 | 78 `Black` (71 `InitialBoot` once the boot movie reaches its light phase) | spread |
| `InitialSetup` 5 | 67 `Bottom` | bottom |
| `InitialWelcomeScreenAnimation` 6 | 67 `Bottom` | bottom |
| `InitialWelcomeScreenFadeOutAnimation` 7 | 66 `InitialWelcomeNoParticle` | none |
| `Login` 8 | 67 `Bottom` | bottom |
| `ParticleBottom` 9 | 67 `Bottom` | bottom |
| `ParticleSpread` 10 | 67 `Bottom` | bottom (**not** spread - the firmware really does map both steady particle states to the same index) |
| `NoParticle` 11 | 65 `NoParticle` | none |
| `Shutdown` 12 | 68 `Spread` | spread |
| `FadeOutShutdownAnimation` 13 | 78 `Black` | nothing drawn |

All EXACT. The `NoParticle` row is also the only state that selects the home
background music, which identifies it as the home screen. The name proves that
this managed state applies no explicit light-particle override. It does **not**
prove that the native default background body is inert: the resolved steady
HOME path selects Plane2 state 5 / record 2, whose `wave_bg_p` noise phase moves
every draw, while it selects no emitting particle-state setter.

The native half of that table is now resolved too. NPXS40087 dispatcher
`0x72e60` receives `LightRenderModeIndex` itself as command `0x41..0x4f`.
Its low-nibble jump table at `0xbb03b8` reaches particle-state setter `0x97560`
only for the emitting modes:

| light mode | command | raw particle state | owner weight |
|---|---:|---:|---:|
| `NoParticle` | `0x41` | no setter call | `1.0` |
| `InitialWelcomeNoParticle` | `0x42` | no setter call | `0.2` |
| `Bottom` | `0x43` | 1 | `1.0` |
| `Spread` | `0x44` | 2 | `0.33333334` |
| `ColdBoot` | `0x45` | 3 | `1.0` |
| `WarmBoot` | `0x46` | 4 | `1.0` |
| `InitialBoot` | `0x47` | 6 | `0.33333334` |
| `Black` | `0x4e` | no setter call | `-1.0` |
| `None` | `0x4f` | no setter call | `-1.0` |

The state setter's exact selector table is `(1,1,0,0,1,1)` and its resource
weight table is `(1,1,0,0,1,1)`. These numeric routes are now test-pinned
against the 4.03 bytes. They are not renamed: selector 1 is the serialized
`spread_expanded` body even when reached from managed `Bottom`, proving that an
additional event/pattern step still has to be recovered before the steady
small-particle modes can be rendered faithfully.

The production renderer consequently shares the byte-exact
`spread_expanded` particle body between raw states 1 and 2, matching this table,
while keeping the unresolved Bottom light/camera delta explicit. It does not
rename or replay `bottom_camCal` as a standalone raw-state body: that partial
event set retains constructor state and renders clear when incorrectly sampled
from zero.

A separate light flag carries `IsReady` (bit 0, set by the renderer when the
particle system is up - the boot animation waits on it) and `PauseParticle`
(bit 1, set while a boot animation is starting). EXACT.

### The textures

Both `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` and `Particle1.gnf` are 167,936-byte
GNF containers. Measured, not assumed:

| Field | Value |
| ----- | ----- |
| Size | 480 x 270 |
| Format | unified format 181, BC7 unorm |
| Swizzle | SW_MODE 5 (`SW_4KB_S`) |
| Resource type | 9 (2D), single mip |
| Surface bytes | 163,840 (8 x 5 tiles of 4 KiB), i.e. exactly the padded footprint |
| Alpha | 254-255 everywhere: **opaque** |

They are **not** mote sprites. Each is a soft out-of-focus light field - Particle0
cyan/teal, Particle1 warm amber. `GnfImage` decodes both correctly (verified by
eye against the untiled output; a wrong swizzle produces visible 16x16 block
scrambling, and there is none).

**Retired host approximation:** an older implementation used them as a slow
full-frame wash and as cropped mote sprites with a radial mask. The current
runtime does neither. Native resource routing is documented separately in
`ps5-background-native.md`.

### Retired deviation: ambient drift

An older host implementation added a slow `ShellParticlePattern.Ambient` drift.
It had no firmware counterpart and has been removed. The replacement is the
translated record-2 `wave_bg_p` phase recovered from firmware, not host motion.

## Basemat

| Property | Value | Status |
| -------- | ----- | ------ |
| Types | 0 `None`, 1 `Flat`, 2 `Linear`, 3 `EllipseWide` = 3 `EllipseNarrow` | EXACT (the two ellipse names collide on 3 in 4.03, so only one ellipse survives) |
| Default colour | linear RGB (0.00784, 0.01568, 0.03137), i.e. **#020408** in 8-bit sRGB steps | EXACT |
| Default cross-fade | **1000 ms** | EXACT |
| Update rule | A basemat requested while a transition already owns one is queued and applied with the next transition; otherwise it is applied immediately unless a basemat animation is already in progress | EXACT |
| Fallback path | The image-viewer fallback sets type `None`, the default colour, and duration 0 | EXACT |
| Mat shapes | **APPROXIMATED.** The native renderer owns the geometry. `CreateBasematBrush` draws `Flat` as a uniform dim, `Linear` as a vertical wash and `Ellipse` as an elliptical vignette centred slightly above the middle. | APPROXIMATED |

The basemat is what this codebase previously called "the scrim". It is the top of
the background composite and the bottom of the UI, and `ShellBackground` uses it
for exactly that.

## State-transition durations

| Transition | Duration | Status |
| ---------- | -------- | ------ |
| Cold boot animation | 6000 ms (60,000,000 ticks / 100 ns) | EXACT, corrected, see note |
| Warm boot animation | 3000 ms | EXACT |
| Boot animation renderer timeout | 15,000 ms, after which the layer gives up and advances the state | EXACT |
| Initial boot movie | advances 1100 ms after the player disappears; 30,000 ms hard timeout | EXACT |
| Welcome screen animation | 30,000 ms hard timeout | EXACT |
| Welcome screen fade-out | **1333.3334 ms** | EXACT |
| Shutdown fade-out | 3000 ms | EXACT |
| Background transition timeout | 10,000 ms | EXACT |
| Loading indicator / blank timeouts | 1000 ms / 500 ms | EXACT |

> **Corrected.** Earlier revisions of this table printed the cold boot as
> `6000 ms (600,000,000 ticks / 100 ns)`. The tick count carried one extra zero and
> was internally inconsistent with its own millisecond figure: 600,000,000 ticks at
> 100 ns is 60 seconds, not 6. The managed metadata gives
> `BackgroundLayer.ColdBootDurationTick = 60,000,000`, which is 6000 ms and matches
> the millisecond figure that was already right. Nothing downstream was affected,
> because every consumer used the 6000 ms number. Provenance:
> `docs/ps5-shell-metadata.md` "Contradictions", read reflection-only from the
> managed `.dll.sprx` set.

Next state after each animation (EXACT): cold and warm boot both go to `Login`;
the initial boot movie goes to `InitialSetup`; the welcome animation and its
fade-out both go to `NoParticle`; the shutdown fade-out goes to `Black`.

## What SharpEmu Home currently draws

`ShellBackground` currently draws, bottom to top:

1. **Clear plate** in the recovered basemat colour.
2. **Background plate** through `Ps5BackgroundPlate`.
3. **Optional basemat** above the plate and below shell content.

The background composition exposes global state, image, overlay, basemat,
motion, and focus-rectangle inputs. The native ripple consumes a recent explicit
transition point or the focus-rectangle centre, with screen centre as its exact
firmware fallback. The steady native Plane2 route and title plate remain separate
layers beneath shell content.

### Known gaps

- The **overlay slot** is wired but never filled: no dump asset maps to it.
- `CustomImageFade` executes the native 4.03 linear cross-fade with its recovered
  per-degree clock (`300 + degree * 166.6666717529297` ms). Ordinary HOME title
  selection is also traced: RN modules 196 and 511 select Normal-degree
  `SlideInLeft`, `SlideInRight`, or `Fade` from strand direction. Opaque 16:9
  title art now runs the recovered `slide_in_p` mask, parameter records, and UV
  equations. `CustomImageRipple` executes the original NPXS40087 pixel shader
  for opaque plates with the recovered ABI, origin, degree record, and progress
  curve. The ripple/slide optional gradation and transparent-alpha branches
  remain unclaimed rather than approximated.
- The shell focus rectangle now supplies the ripple origin when the firmware
  caller has not provided a newer explicit transition point. Focus-ring drawing
  remains a separate foreground system.
- Cold-boot particle records execute through the original compute/draw programs,
  but persistent GPU ownership and the complete steady Bottom/Spread
  light-and-camera state distinction remain unfinished.
