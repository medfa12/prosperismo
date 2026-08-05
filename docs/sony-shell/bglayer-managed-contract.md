<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# BGLayer managed contract — ripple transitions, wave presets, particle states

Recovered 2026-08-05 from `Sce.Vsh.ShellUI.BGLayer.dll.sprx`, cross-checked
across **four firmware versions** (3.20 from `ps5oracle/PS5_3.20/sprx/`, and
3.00 / 12.40 / 13.00 / 13.20 from the local firmware database, copied to the
gitignored `ps5oracle/fwdb/`). Each file is a decrypted FreeBSD ELF carrying a
Mono AOT image; the managed assembly starts at the first `MZ` whose `e_lfanew`
resolves to a valid `PE\0\0` signature (3.20: `0x54050`; 12.40: `0xa7820` —
note that earlier `MZ` byte pairs in these files are coincidental and must be
validated, not taken as the header). Decompiled locally with ILSpy for reading
only.

**Version stability.** The 3.20 assembly is 489,840 bytes / ~6,000 decompiled
lines; 12.40, 13.00 and 13.20 are all 925,944 bytes / ~8,850 lines. Every
value in this document is **identical across all versions checked**, with two
additions in 12.40+ noted inline below. That stability is why these constants
are safe to build against. **No Sony code is reproduced here or in the
repository** — this document records constants, enum values and the arithmetic
relationships between them, which is what the shell needs in order to behave
correctly.

This is the *managed* layer that drives the background. It answers questions
[`bglayer-shaders.md`](bglayer-shaders.md) §2.4 left open on the parameter
side, and supplies the ripple contract the shell's transition owner needs. It
does **not** recover `wave_bg_p`'s float uniforms (`uCoff0`, `uCoff1`,
`uLightColor`, `uLightPos`, `uCenterPos`) — those are still filled by native
code that is not present in this dump. See "Still unrecovered" below.

## 1. The ripple transition — origin, type word, and degree

`BGLayerNative.BGTransitionParam` is the struct handed to the native layer:

| Field | Type | Meaning |
|---|---|---|
| `Type` | `int` | packed transition type **and** degree, see below |
| `Flag` | `int` | `BackgroundTransitionFlag` bits |
| `CenterX` | `float` | ripple origin, **normalized** |
| `CenterY` | `float` | ripple origin, **normalized** |
| `CaptureScale` | `float` | capture scale for the outgoing plate |

The type word is packed, EXACT:

```
Type = (int)transitionType | ((int)degree & 0xFF) << 16
```

The ripple origin is normalized against the fixed design space, EXACT:

```
CenterX = screenPoint.x / 1920
CenterY = screenPoint.y / 1080
```

**Where the ripple starts is the focused widget, not the screen centre.** When
the caller supplies no explicit transition point (either component `NaN`), the
shell takes the currently focused widget and uses its centre
(`ConvertLocalToScreen(width * 0.5, height * 0.5)`). Only when there is no
focused widget does it fall back to `(960, 540)` — i.e. normalized
`(0.5, 0.5)`. So selecting a game and launching it ripples out of *that tile*,
which is the behaviour a centre-only implementation gets visibly wrong.

> **Scope of the origin rule.** The `BGTransitionParam` struct — including
> normalized `CenterX`/`CenterY` — is byte-identical in every version checked,
> so the *native ABI* is settled. The focused-widget selection above is EXACT
> for **3.20**, whose shell is the managed Avalonia-style UI that owns its own
> focus tree. In 12.40+ that selection code is not in this assembly: Home is
> the React Native `NPXS40002` bundle, so the transition point is supplied by
> JS. The RN bundle is known to request `{ type: "Ripple", degree: "CrossFade" }`
> (see [`ps5-hub-and-cards.md`](ps5-hub-and-cards.md) §1.9). Treat
> "origin = focused item centre" as EXACT for 3.20 and as the best available
> evidence — not a byte-level proof — for 12.40+.

`BackgroundTransitionType`, EXACT:

| Name | Value |
|---|---|
| `Invalid` | `-1` |
| `LaunchingGame` | `0` |
| `Hide` / `HideGameSplash` | `1` |
| `LaunchingGameBC` | `2` |
| `SystemDefault` | `5` |
| `CustomImageRipple` / `CustomImage` | `6` |
| `CustomImageSlideInLeft` | `7` |
| `CustomImageSlideInRight` | `8` |
| `CustomImageFade` | `9` |
| `CustomImageRippleBack` | `10` |
| `FadeToBlack` | `11` | **12.40+ only**, absent in 3.20 |

`CustomImageRipple` and `CustomImage` share value `6`: the ripple *is* the
default custom-image transition. `CustomImageRippleBack` (`10`) is the reverse.

The five transition types that flip the double-buffered plate id
(`bgImageId ^= 1`) are `LaunchingGame`, `LaunchingGameBC`, `CustomImageRipple`,
`CustomImageSlideInLeft`, `CustomImageSlideInRight`, `CustomImageFade` and
`CustomImageRippleBack` — i.e. every type that shows a *new* image. `Hide` and
`SystemDefault` do not.

`BackgroundTransitionDegree`, EXACT: `CrossFade = 0`, `Subtle = 1`,
`Normal = 2`, `Strong = 3`. The default on a fresh
`BackgroundTransitionParam` is **`Strong`**, and the shell explicitly
downgrades to `CrossFade` in several paths.

This confirms the duration relation already used by the React Native shell
(`300 + degree * 166.66667` ms): degree `Normal = 2` gives the 633.333 ms
HOME selection transition. The enum values above are the authority for the
multiplier.

`BackgroundTransitionFlag`, EXACT: `EndTransition = 1`,
`RequestOldImage = 2`, `RequestNextImage = 4`, `RequestNextOverlayImage = 8`,
`CanceledTransition = 0x10`, `Cancelable = 0x20`, `RequestFallbackImage = 0x40`,
`BasematAnimationInProgress = 0x80`.

Internal transition state machine: `LoadingImage`, `Animation1`, `Animation2`,
`Canceled` — two animation phases, not one.

## 2. Basemat types

`BackgroundBasematType`, EXACT: `None = 0`, `Flat = 1`, `Linear = 2`,
`EllipseWide = 3`, `EllipseNarrow = 3`.

`EllipseWide` and `EllipseNarrow` are **the same value (3)** — the two names
are aliases in this firmware. [`ps5-hub-and-cards.md`](ps5-hub-and-cards.md)
§1.9 records the hub selecting `"EllipseNarrow"` for scene 0 and `"Flat"` for
later scenes; numerically that is `3` then `1`.

## 3. Wave colour presets

`WaveColourPreset : uint`, EXACT ordinal order (several are `[Obsolete]`,
retained here because the ordinals matter):

| Ordinal | Name | Note |
|---:|---|---|
| 0 | `InitialSetup` | obsolete |
| 1 | `MiniApp` | obsolete |
| 2 | `SystemArea` | |
| 3 | `MusicUnlimited` | obsolete |
| 4 | `HomeScreen` | **the home wave** |
| 5 | `NoWave` | |
| 6 | `Black` | |
| 7 | `WhatsNew` | |
| 8 | `MusicUnlimitedSplash` | obsolete |
| 9 | `Login` | |
| 10 | `LoginNoUserLogined` | |
| 11 | `Boot` | |
| 12 | `Store` | |
| 13 | `PsVideo` | |
| 14–21 | `ThemeFlow0` … `ThemeFlow7` | eight theme slots |

The colour *values* behind each preset are chosen natively; this enum fixes
the identity and count. Home is preset `4`. The eight `ThemeFlow` slots are
what a user theme selects between.

## 4. Particle / light render states

`LightRenderModeIndex` (private), EXACT:

| Name | Value |
|---|---|
| `NoParticle` | `65` |
| `InitialWelcomeNoParticle` | `66` |
| `Bottom` | `67` |
| `Spread` | `68` |
| `ColdBoot` | `69` |
| `WarmBoot` | `70` |
| `InitialBoot` | `71` |
| `Shutdown` | `72` | **12.40+ only**, absent in 3.20 |
| `Black` | `78` |
| `None` | `79` |

These are the render-mode ids behind the particle layer. `Bottom` (`67`) and
`Spread` (`68`) are the two steady-state home modes; the existing native
frame-producer work in
[`ps5-background-native.md`](ps5-background-native.md) targets raw **Bottom**
state and the `spread_expanded` body, which these ids now name numerically.
The values are not contiguous from zero — they are offsets into a larger
shared table, so they must be used as-is rather than re-indexed.

`GlobalBackgroundState`, EXACT: `None = 0`, `Black = 1`,
`ColdBootAnimation`/`BootAnimation` = `2`, `WarmBootAnimation = 3`,
`InitialBootAnimation = 4`, `InitialSetup = 5`,
`InitialWelcomeScreenAnimation = 6`, `InitialWelcomeScreenFadeOutAnimation = 7`,
`Login = 8`, `ParticleBottom = 9`, `ParticleSpread = 10`, `NoParticle = 11`,
`Shutdown = 12`, `FadeOutShutdownAnimation = 13`.

Note the pairing: `GlobalBackgroundState.ParticleBottom`/`ParticleSpread`
(`9`/`10`) are the public state names for the private
`LightRenderModeIndex.Bottom`/`Spread` (`67`/`68`).

## 5. Other recovered surface

- `WaveOpacity` is a `float` field on the state block, separate from the
  preset — opacity and colour are independent controls.
- `SetMaskWave(bool)` / `SetMaskWaveAndStopFocus(bool)` — masking the wave is
  coupled to focus suppression in the second variant.
- `SafeAreaRenderingMode` `[Flags]`, EXACT: `Normal = 0`, `HideFrame = 1`,
  `DisableBackgroundScaling = 2`.
- Transition start time is stamped from `UISystem.FrameTickBasedTime.Ticks` —
  a frame-tick clock, not wall time, so transitions are frame-quantized.

## Still unrecovered

- The float uniforms of `wave_bg_p` (`uCoff0`, `uCoff1`, `uLightColor`,
  `uLightPos`, `uCenterPos`) — the **rays**. `bglayer-shaders.md` §2.2 has
  their exact arithmetic and §2.1 their register mapping, but the runtime
  values come from native code not present in this dump. The managed layer
  above selects *which* preset/state is active; it does not carry the floats.
- The per-preset colour triples behind `WaveColourPreset`. *(This bullet
  appeared twice in an earlier revision; the duplicate is removed.)*
- ~~`particle_c`'s simulation body.~~ **Recovered 2026-08-05** — see
  [`particle-system.md`](particle-system.md) for the simulation and
  [`particle-draw.md`](particle-draw.md) for the four draw programs. What
  remains unrecovered there is not the *body* but the `Resources` block's
  runtime **float values** (spawn box, curl frequency/strength, lifetimes,
  accelerations, attractor set), which are host-written and are item **M5** of
  [`bglayer-background-spec.md`](bglayer-background-spec.md).

## The 12.40 eboot does carry the FirstWave shaders

`ps5oracle/PS5_12.40/filesystems/system_ex/app/NPXS40087/eboot.bin`
(21,695,212 bytes) embeds **62 AMDGPU ELFs** (`e_machine == 224`), first at
file offset `0x10eacdc`. Its named background set is:

| Shader | Role |
|---|---|
| `fw_background_p` | the plate — already translated |
| `fw_flow_vl` / `fw_flow_h` / `fw_flow_dv` | the wave **mesh** — the ripples |
| `fw_basic_vv` / `fw_basic_p` | plain **textured blit** — a utility pass, *not* the mesh |
| `fw_oit_p`, `fw_comp_oit_p` | order-independent transparency + composite |
| `fw_blur_vv`, `fw_blurh_p`, `fw_blurv_p` | separable blur |
| `fw_fxaa_p` | antialias resolve |
| `fw_clear_vv` / `fw_clear_p` | target clear |
| `particle_c` / `particle_vv` / `particle_p` | particle sim + draw |
| `large_particle_vv` / `large_particle_p` | large-particle variant |

**Correction, 2026-08-05.** This table previously assigned
`fw_basic_vv`/`fw_basic_p` the role "the wave **mesh** — the ripples", and
omitted the `fw_flow_*` triple entirely. That was an assignment by position,
made before the programs were decoded, and it is **wrong**. `fw_basic_vv` (22
instructions) fetches a position and a texture coordinate from two vertex
buffers; `fw_basic_p` (14 instructions) does one `image_sample` and exports —
there is no lattice, no tessellation and no constant buffer. The ripples are
the `fw_flow_*` pipeline, decoded in
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md), **which is
authoritative for every `fw_*` role assignment**; this table is a
convenience index. The `pipeline_order` quoted at the end of this document
already named `fw_flow_*` correctly, so the two halves of this note previously
contradicted each other. The ISA decode wins.

`wave_bg_p`, `dual_wave*` and `wave2*` are **4.03-generation names and are
absent from 12.40** — FirstWave (`fw_*`) is the newer implementation of the
same surface, which is why the repo's plate translation targets 12.40. The
`bglayer-shaders.md` offsets (`0xd85790` etc.) are 4.03 file offsets and do
not apply to this binary.

This means the remaining mesh/OIT/blur/FXAA recovery boundary **is reachable
from the dump already on disk** — no further firmware is required for it. An
earlier note in this document claimed otherwise; that was a `grep` artifact on
binary input, corrected here after locating the strings with a byte search.

### The FirstWave programs are raw, not ELF-wrapped

The ten stages in
[`firstwave-12.40-shader-contracts.json`](firstwave-12.40-shader-contracts.json)
live at **raw file offsets** in a contiguous region (`0x11F4800` …
`0x11F9530`), not inside the 62 AMDGPU ELF containers. The ELFs carry an
unrelated video/display set (`Yuv2Y…` and friends), which is why a scan of ELF
`.metadata` sections finds no `fw_*` name. Any future recovery pass must slice
by offset, as the existing manifest does.

Scanning the region `0x11F0000`–`0x1201000` for `s_endpgm` (`0xBF810000`)
yields 22 terminators. Ten match the manifest exactly — a useful independent
check of those boundaries, and confirmation that `fw_flow_vl` really does
return through `s[6:7]` rather than reaching a terminator of its own. The
remaining twelve were unmapped by that scan.

**This has since been resolved properly.** Rather than guess from position,
the eboot's own descriptor array was located and walked; every entry is now
named from firmware data in
[`shader-entry-map-12.40.json`](shader-entry-map-12.40.json). See
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md) §"Naming every
entry".

## Pipeline order

The manifest's own `pipeline_order` is
`fw_flow_vl → fw_flow_h → fw_flow_dv → fw_oit_p → fw_comp_oit_p →
fw_blurh_p → fw_blurv_p → fw_fxaa_p`.

Read against the stage contracts, this names the two effects directly:

- **The ripples** are the tessellated flow pipeline. `fw_flow_vl` reads `time`
  at constant offset `+0x184` and writes two 128-bit LDS records; `fw_flow_h`
  is the hull stage over four LDS pairs; `fw_flow_dv` is the domain stage —
  700 instructions, 16 four-dword control-point reads spanning `0x0..0x1E0` —
  which is where the wave surface is actually displaced.
- **The rays** are the lit OIT resolve plus the separable blur.
  `fw_oit_p` reads the colour/light block (`BackgroundLightColour` at `+0x130`)
  together with `BlurParameters` (`+0x170`), `time`, `waveOpacity` and
  `oitSliceOffset`; `fw_blurh_p`/`fw_blurv_p` are the same 122-instruction
  program differing only in axis, 14 `image_sample` operations each.

Both are order-dependent on the shared constant ABI in
[`firstwave-12.40-stage-contracts.md`](firstwave-12.40-stage-contracts.md),
which remains the source of truth for individual scalar loads.

## Reproducing this extraction

The dump is local-only and gitignored; nothing from it is committed.

```
python3 -c "d=open('Sce.Vsh.ShellUI.BGLayer.dll.sprx','rb').read(); \
open('BGLayer.dll','wb').write(d[0x54050:])"
dotnet tool install -g ilspycmd
ilspycmd -o decompiled BGLayer.dll
```
