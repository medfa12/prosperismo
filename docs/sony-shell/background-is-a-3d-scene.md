<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The background is a 3D scene, not a fullscreen shader

**This corrects the premise of every earlier background document.** The
recovery work so far treated the PS5 background as a chain of post-process
passes over a fullscreen quad — plate, wave mesh, OIT, blur, FXAA. That is a
*part* of it. The thing on screen is a **rendered 3D scene**: models with
image-based lighting, a camera, a light-shaft effect and a sequence player
that reacts to system state.

Evidence is Sony's own symbols and build paths in the 12.40
`NPXS40087/eboot.bin`.

## The source tree, from embedded build paths

The binary carries absolute build paths from Sony's build machine. Under
`vsh\shell\shell_ui\src\background_layer\` there are **283** of them,
including:

```
background_layer\cis_renderer\src\sequence\sequence_scene_builder.cpp
background_layer\cis_renderer\src\sequence\sequence_demo_player.cpp
```

`cis_renderer` is the background renderer, and it is organised around
**sequences** and a **scene builder** — a timeline driving a scene graph, not
a shader chain. (The remaining `background_layer` paths are the PSVR2 play-area
and camera-calibration renderers, which share the subsystem.)

## What the scene contains

Symbols clustered around `sequence_scene_builder.cpp`:

| Symbol | Meaning |
|---|---|
| `CreateBasicModel`, `DrawModelNative`, `StartModelSequenceNative` | model entities driven by sequences |
| `animations/skeleton/`, `ModelEntity`, `cameraController` | skeletal animation and a camera |
| `maya_lambert`, `SurfaceIncandescence`, `SurfaceSpecularColor`, `TextureAmbient` | Maya-authored materials |
| `TextureIBLDiffuse`, `IBLGlobalSpace`, `IBLReflectionMipLevel` | **image-based lighting** |
| `LightController`, `Direction`, `AmbientColor` | a light rig |
| `diffuseIntensity`, `ambientIntensity`, `specularIntensity`, `specularPowerK`, `diffuseDistOrCosineBeginFading` | a real lighting model with distance falloff |
| **`FwLsShader`**, `effect_light_shaft`, `CreateLightShaftModel` | **FirstWave Light Shaft** — the volumetric rays |
| `SamplerColorRamp`, `Sampler2dNoise`, `ColorScale`, `SurfaceTransparency` | ramp and noise inputs |
| `XfConstantBuffer`, `SetupVs`, `SetupScreenViewport`, `Xf::BeginningRender` | the `Xf` scene framework |

`effect_light_shaft` and `CreateLightShaftModel` are decisive: the rays are a
**model** in the scene with its own effect, not a screen-space post pass. That
matches the reference capture, where the shaft has real volume and enters from
outside the frame.

## Why the shader PoC produced a flat field

[`macos-background-poc.md`](macos-background-poc.md) proved a firmware shader
executes on macOS through MoltenVK, but its output was a uniform colour across
330 input configurations. This explains why: the ripple/plate shaders are
fragments of a larger renderer. Run in isolation, with no scene, no camera, no
IBL textures, no light-shaft model and no sequence state, they have nothing to
shade. Sweeping their constant buffers harder was never going to help.

## Sequence states

The states the user describes — booting, ambient, rest, shutdown — appear as
`coldboot/colorchange/colorchange`, `spread_expanded`, `Ambient`, `RestMode`,
matching the enums already recovered in
[`bglayer-managed-contract.md`](bglayer-managed-contract.md)
(`LightRenderModeIndex` 65–79, `GlobalBackgroundState` 0–13). Those are
**sequence selectors** for the scene player, which is why the same renderer
produces the boot animation, the ambient room and the shutdown fade.

`ps5oracle/shell_ui/live_background/default.mp4` is a capture of the **ambient**
sequence.

## What this means for the port

- Reproducing the background needs a **scene renderer**: model loading, skeletal
  animation, IBL, a light rig and a sequence player — not a shader port.
- Asset dependencies are correspondingly larger. The binary references real
  paths such as `/system_ex/vsh_asset/heightMark.gnf` and
  `/system_ex/vsh_asset/indicator_L.gnf`, confirming `.gnf` models/textures live
  in `vsh_asset` (still absent from every local dump).
- The procedural particle field in `Ps5ProceduralParticleFrameSource` remains
  valid — particles are genuinely one layer of this scene — but it is one
  element of several, and the light shaft is a bigger contributor to the look.

## The complete asset manifest

Every firmware asset the shell eboot names, extracted from its string table.
This is the exhaustive list — there is nothing else to look for.

**Textures (15 `.gnf`, all in `/system_ex/vsh_asset/`):**

| File | Role |
|---|---|
| `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf`, `…Particle1.gnf` | the particle light textures |
| `shutdown_ramp.gnf` | the shutdown sequence's colour ramp |
| `diffuse_default.gnf` | fallback material texture |
| `all_symbols_2x.gnf` | symbol atlas |
| `heightMark`, `heightMark_feet`, `hmdMark`, `indicator_L/R/arrow`, `Add_Mode_*`, `Erase_Mode_*` | PSVR2 play-area editor |

**Shaders (4 `.ags`, under `shaders/effects/`):** `effect_post_process_vv`,
`effect_post_process_copy_p`, `effect_post_process_fxaa_p`,
`effect_post_process_fxaa_ssaa_x4_p`.

So for the *background* specifically, only four files matter:
`Particle0.gnf`, `Particle1.gnf`, `shutdown_ramp.gnf`, `diffuse_default.gnf`.

**There are no model, scene or sequence asset files.** No `.mdl`, `.scn`,
`.seq` or equivalent appears anywhere in the binary. Combined with
`CreateBasicModel`, `CreateLightShaftModel` and `CreateLightShaftModel`'s
placement next to the scene builder, the strong reading is that the room
geometry and the shaft volume are **constructed in code**, with `.gnf` files
supplying only textures.

If that holds it is good news for a port: the geometry is recoverable by
reading the builder rather than by extracting proprietary model assets, and
the outstanding asset requirement shrinks to four texture files.

This has not been confirmed — `LoadFreeForm` also appears nearby and may load
geometry from somewhere not yet identified.

## Not yet established

The scene file format, how sequences are authored and stored, which `.gnf`
assets the ambient sequence loads, and whether `Xf` is a public Sony framework
or shell-private. None of that is settled here; this document only establishes
*that* the background is a scene and names its parts.
