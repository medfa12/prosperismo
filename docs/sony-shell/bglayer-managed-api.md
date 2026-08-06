<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The BGLayer managed API is fully readable

`BGLayer.dll.sprx` — the background layer's own library — sits **decrypted** in
`ps5oracle/fwdb/{3.00,12.40,13.00,13.20}/`. It is a plain ELF (entropy 5.97)
carrying embedded CLI metadata, so the entire managed API surface of the PS5
background is readable with no key, no console and no firmware unpacking.

This is a better oracle for the background than the update PUPs, which yield
only resources — their executables are SELF-encrypted (see
[`firmware-decryption-not-needed.md`](firmware-decryption-not-needed.md)).

## Reading it

```
BSJB metadata at 0xA7AE4, runtime v4.0.30319
  #~        53,044 B   tables
  #Strings  46,772 B   2,558 names   <- the API surface
  #Blob     12,912 B   signatures
  #US       29,772 B   user strings
```

Extracted name lists per version live at
`ps5oracle/shell_ui/bglayer_api/<version>.txt` (gitignored). The four builds
differ — distinct MD5s — so the API can be diffed across firmware versions.

## The scene and sequence API

This is the layer the eboot's `*Native` symbols bind to, and it confirms the
scene-graph reading of the background from the managed side:

| Group | Members |
|---|---|
| Scene graph | `AddScene`, `PushScene`, `PopScene`, `ContainerScene`, `FindContainerSceneByPath`, `GetTopScene`, `LayerScene`, `SceneBase`, `SceneParameters`, `BgSceneVisibility`, `RemoveFromSuperScene`, `RenderSceneFrontNative` |
| Model lifecycle | `InitializeModel`, `LoadModel`, `AnimateModel`, `DrawModel`, `FinalizeModel` (each with a `*Native` counterpart) |
| **Sequences** | `GetModelSequenceCount`, `GetModelSequenceName`, `GetModelSequenceDuration`, `GetModelSequencePlayTime`, `StartModelSequenceNative`, `CheckProgressSequence` |
| Boot | `BeginBootupSequence`, `ColdBoot`, `ColdBootAnimation`, `ColdBootDurationTick`, `ColdBootWaitCount` |
| Lighting | `LightRenderModeIndex`, `LightStartPosition`, `LightPoint`, `LightMat`, `LightFlag`, `OnLightStart`, `SetThemedLightColor` |
| Wave | `SetMaskWave`, `SetMaskWaveNative`, `SetMaskWaveAndStopFocus`, `WaveColourPreset`, `WaveOpacity`, `WaveGpuTime`, `WavePostGpuTime`, `NoWave` |
| Ripple | `CustomImageRipple`, `CustomImageRippleBack` |
| Particles | `ParticleBottom`, `ParticleSpread`, `PauseParticle`, `LightParticleFlag`, `InitialWelcomeNoParticle` |
| Transition | `SetBGTransition`, `BackgroundTransitionType/Degree/Flag`, `TransitionVariety`, `executeBackgroundTransition`, `cancelBackgroundTransition`, `fallbackBackgroundTransition`, `updateBasematWithTransition` |
| Basemat / imagery | `BackgroundImage0/1`, `BackgroundBlurImage0/1`, `BGBasematParam`, `BackgroundBasematType`, `AsyncLoadAndMapImage`, `AsyncLoadAndMapBlurImage` |

`GetModelSequenceCount` / `Name` / `Duration` is decisive: the background plays
**named model sequences with durations**, queried at runtime. That is a timeline
driving a scene graph, exactly as
[`background-is-a-3d-scene.md`](background-is-a-3d-scene.md) concluded from the
eboot's build paths — now confirmed independently from the managed side.

## Why this matters for the port

The reimplementation needs to satisfy this contract, and the contract is now
fully enumerated rather than guessed. It also supersedes the update-PUP route
for the background: the PUPs give resources, but this gives the **behaviour**.

## Not established here

Enum *values*. `GlobalBackgroundState`, `LightRenderModeIndex`,
`BackgroundTransitionType` and `WaveColourPreset` are named, but their numeric
members are in the `#~` tables and `#Blob` heap and have not been decoded. The
partial values recorded in
[`bglayer-managed-contract.md`](bglayer-managed-contract.md) came from a
different route and are not re-derived here.
