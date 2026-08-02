# PS5 Shell — Overlay / Modal + Animation Reference

Clean-room reference for reimplementing the PS5 home-shell overlay/modal presentation
model, background dimmer/scrim, focus movement, and background-layer motion. Values and
behavior only, recovered from the shell's managed (.NET) UI assemblies.

## Provenance and method

Two firmware dumps were inspected. The managed assemblies are wrapped in `.sprx`; the
embedded managed PE was carved and disassembled to read constants and control flow.

| Assembly | 9.00 copy | 4.03 copy | Real bodies? |
|---|---|---|---|
| `Sce.Vsh.ShellUI.SystemModalDialog` | reference facade (metadata only, empty IL) | full implementation | **4.03 only** |
| `Sce.Vsh.ShellUI.BGLayer` | partial (consts present, some bodies stripped) | full implementation | **4.03 primary**, 9.00 confirms names |
| `Sce.Vsh.ShellUI.BaseSystem` | bootstrap/run-manager only (no UI logic) | same | n/a for this topic |

Key point: the `common_ex/lib` copies in the 9.00 dump are largely **reference
assemblies** — type/field/enum metadata is intact but method IL is stripped, so literal
values are not inline. The 4.03 reconstructed dump carries **full method bodies**, so all
concrete numbers below are read from 4.03. The 9.00 metadata confirms the same type
surface and constant names exist, i.e. the presentation model is unchanged between 4.03
and 9.00. Where a value is quoted it is from 4.03 unless noted.

`BaseSystem` does **not** contain overlay/theme logic — its largest module is an
assembly-loader / boot run-manager. The theme tokens named in the brief
(`base-mat.overlay.enabled`, `DefaultThemeHomeScreenDimmer`, `DefaultThemeFuncScreenDimmer`,
`DefaultThemeTitleNameDimmer`) were **not found** in any of these three assemblies; they
live in the theme/resource assemblies (e.g. `Sce.Vsh.ShellUI.Theme`) and were out of scope.
See Gaps.

---

## 1. Modal / overlay presentation model

Source: `Sce.Vsh.ShellUI.SystemModalDialog` (4.03).

### Layering and registration
- System modals are hosted in a dedicated overlay layer. Scenes are named and placed under
  a container found by path `"SystemOverlay"` (`LayerManager.FindContainerSceneByPath("SystemOverlay")`),
  and the shutdown menu root is named `"MainSceneSystemOverlay"`.
- `DialogManager` holds a static registry (`dialogConfigList`) mapping each dialog name to a
  **menu bucket** and a `remainWhenMenuDispose` flag. Buckets group dialogs by severity/context:
  `RnpsAppMenu`, `NormalMenu`, `VrNormalMenu`, `ErrorMenu`, `ErrorDialogMenu`,
  `SystemAlertMenu`, `CriticalErrorMenu`, `DbCorruptionErrorMenu`, `ShutdownMenu`, etc.
  Only `ShutdownMenu` entries set `remainWhenMenuDispose: true`; all others `false`.
- Open modals are ref-counted by `DialogCounter` (`OpenedCount`). When `OpenedCount > 0`,
  `ButtonLocker` installs a PS-button lock (`Locker((LockType)7,(LockType)0)`) and a
  capture-menu lock; both are released when the count returns to 0. So: **any open system
  modal suppresses the PS button and the capture (Share) menu.**

### A modal scene itself
Concrete presentation flags set per dialog (framework base type `ModalScene`):
- `ModalScene.PresentationStyle = (ScenePresentationStyle)2` — the standard presentation
  style used by essentially every system modal observed (alert/confirm/VR/error scenes).
- `ModalScene.DimBackgroundScene = false` — set explicitly on **all** system-modal scenes
  observed (VR modals, `AlertDialog`, camera/how-to scenes, etc.).
- `ModalScene.HideOnBackButton = false` — system modals do not auto-dismiss on the Back/○
  button; dismissal is driven by `DialogManager.Instance.RequestToClose(openId, processId)`.
- A modal is attached with `Scene.AddModalScene(modalScene)`.

### How the background is dimmed (the important architectural finding)
PS5 system modals **do not** draw a translucent scrim rectangle over the live scene. They
set `DimBackgroundScene = false` and instead **darken the whole background layer** by
pushing a background transition + a flat "basemat" through `SystemBGMediator`:

```
BackgroundParam = {
    BackgroundTransitionParam { TransitionType = CustomImage(6), NextImageUri = <pic0/blur> },
    BackgroundBasematParam    { Type = Flat(1) }
}
scene.SetExtendedProperty(SystemBGMediator.BackgroundParam, param)
```

The dimmer is therefore the **basemat** rendered by `BGLayer` (Section 4), not a per-scene
overlay. `BackgroundSceneOpacity` is held at `1f` for the dialog scenes (the dialog content
is fully opaque; darkening comes from the background swap underneath).

Note: the recurring literal `Opacity = 0.7f` seen throughout `SystemModalDialog` is applied
to **individual widgets** (secondary text labels, texture panels), not to a scrim.
`Opacity = 0.827451f`, `0.35686275f`, `0.050980393f`, `8f/85f`, `1f/15f`, `6f/85f` are
per-element opacities in specific light/flicker sequences (see 3.3), not global dimming.

`ScenePresentationStyle` and `ModalScene.DimBackgroundScene`'s **default** value live in the
UI-toolkit assembly, not in ShellUI — see Gaps.

---

## 2. Scene push/pop transition animations

Source: `SystemModalDialog` (4.03). Navigation between sub-scenes inside a modal wizard uses
`NavigationScene` with four transition slots:

```
DefaultPushAnimationForNextScene    = new PushWizardShowing()
DefaultPushAnimationForCurrentScene = new PushWizardHiding()
DefaultPopAnimationForNextScene     = new PopWizardShowing()
DefaultPopAnimationForCurrentScene  = new PopWizardHiding()
```

`PushWizardShowing/Hiding` and `PopWizardShowing/Hiding` are named framework transition
classes (their internal curves/durations are defined in the UI toolkit, not in ShellUI).

One explicit opacity push transition is built inline (HDR-calibration wizard):

```
DefaultPushAnimationForNextScene =
    new TransitionAnimation(
        new AnimationOption(1.5f, 0.2f, TransitionVariety.Linear),   // (duration=1.5, delay=0.2), Linear
        new PrepareAction(TransitionVariety.ShowingPrepareOpacity),
        new AnimationAction(TransitionVariety.ShowingStartOpacity))
```

`AnimationOption(a, b, curve)` — the two floats are the transition's time parameters
(observed `1.5f` and `0.2f`, in seconds; the constructor's exact field names are in the
toolkit). Curve here is `TransitionVariety.Linear`.

---

## 3. Explicit dialog animation timelines (durations + easing)

Source: `SystemModalDialog` (4.03). These are hand-authored keyframe timelines built via
`TransitionVariety.Animate` / `AnimationBlock.AddKeyFrame(startSec, endSec, curve, action)`.
Times are in **seconds**. The dominant easing is `AnimationCurve.EaseInOutCubic`; a few use
`AnimationCurve.Linear`. All these timelines set `CalculateWithActualTimer = true`
(wall-clock timed, frame-rate independent).

### 3.1 Health-warning dialog (auto-timed fade in / hold / fade out)
```
0.0 → 0.5s   EaseInOutCubic   bodyPanel.Opacity → 1   (fade in)
0.5 → 2.5s   EaseInOutCubic   hold (2.0s)
2.5 → 3.0s   EaseInOutCubic   bodyPanel.Opacity → 0   (fade out) → auto-close
```
So: **0.5s fade-in, 2.0s hold, 0.5s fade-out, total 3.0s.**

### 3.2 Welcome-to-PS text fade (named constants)
```
beforeTextFadeIn = 0.33f   // delay before text fades in (s)
textFadeIn       = 1f      // text fade-in duration (s)
textFadeOut      = 1f      // text fade-out duration (s)
```
Timeline: `AddKeyFrame(0.33, 1.33, EaseInOutCubic, opacity→1)` for fade-in; fade-out
`AddKeyFrame(0, 1.0, EaseInOutCubic, opacity→0)`. Fade-out is gated on both the screen
reader finishing and the fade-in completing.

### 3.3 Other fades in the same assembly
- Texture fade-in: `AddKeyFrame(0f, 1.8f, EaseInOutCubic)` — **1.8s**.
- Text fade-in: `AddKeyFrame(0f, 1f, EaseInOutCubic)` — **1.0s**.
- Generic dialog fade-in: `AddKeyFrame(0f, 1f, EaseInOutCubic)` — **1.0s**.
- A "light" flicker sequence (Linear), keyframe boundaries in seconds:
  `0, 0.44, 0.92, 1.24, 1.31, 1.39, 1.43, 1.59, 1.93` with per-step opacities
  `0.827451, ~0, 0.35686275, 0.0941(8/85), 0.0667(1/15), 0.050980393, 0.0706(6/85)` — an
  authored HDR/welcome light pulse, not a general modal effect.

### Easing available
`AnimationCurve.EaseInOutCubic` and `AnimationCurve.Linear` are the curve identities
observed. The curve math (cubic in/out, linear) is standard; no bezier control points or
spring constants are stored in these assemblies. Additional named presets exist as
`TransitionVariety.*` (Section 4).

---

## 4. BGLayer — background dimmer, basemat, wave, particles, focus

Source: `Sce.Vsh.ShellUI.BGLayer` (4.03 for values; 9.00 confirms the same const names).

### 4.1 Basemat (the modal dimmer surface)
```
BasematAnimationDuration = 1000f            // ms — basemat fade-in/out duration (1.0s)
BasematDefaultColor      = Vector3(0.00784f, 0.01568f, 0.03137f)   // linear RGB
```
`BasematDefaultColor` is a near-black cool navy: linear (0.00784, 0.01568, 0.03137) ≈ 8-bit
(2, 4, 8) → about `#020408`. This is the tint the background is darkened to behind system
modals. Basemat shapes (`BackgroundBasematType`):
```
None = 0,  Flat = 1,  Linear = 2,  EllipseWide = 3 (== EllipseNarrow)
```
System modals use **Flat (1)** (full-frame flat dim). `BackgroundTransitionFlag` includes
`BasematAnimationInProgress = 0x80`, i.e. the basemat animates in/out as a first-class
background transition over 1000ms.

### 4.2 Background transitions
`BackgroundTransitionType`: `Hide=1`, `LaunchingGame=0`, `SystemDefault=5`,
`CustomImage/Ripple=6`, `CustomImageSlideInLeft=7`, `CustomImageSlideInRight=8`,
`CustomImageFade=9`, `CustomImageRippleBack=10`.
`BackgroundTransitionDegree`: `CrossFade, Subtle, Normal, Strong` — the transition param's
`Degree` **defaults to `Strong`**; specific paths downgrade to `CrossFade` or `Subtle`.
Misc background timeouts: `ShowIndicatorTimeOut = 1000f` ms, `BlankTimeOut = 500f` ms,
loading `Timeout = 100000000` ticks (10 s).

### 4.3 Background "wave" (the animated home backdrop)
Per-frame opacity ramp in the update loop:
```
if (!ShowWave)  waveOpacity *= 0.9f;                          // exponential fade-out (~10%/frame)
else            waveOpacity = min(1f, waveOpacity + 0.01f);   // linear fade-in (+0.01/frame)
```
At 60 fps: wave fades out to near-zero in roughly 0.5 s and fades in 0→1 in ~1.7 s. The
wave is native-rendered; `WaveOpacity` is passed to the native layer each frame. `MaskWave`
(with focus stop) can hard-mask the wave.

### 4.4 Particles
Particles are driven by the **native** BGLayer (`BGLayerNative`); counts and per-particle
motion (drift/parallax) are not in managed code. Managed side only sets flags/state:
- `GlobalBackgroundState`: `ParticleBottom = 9`, `ParticleSpread = 10`, `NoParticle = 11`
  (plus boot variants `NoParticle=65`, `InitialWelcomeNoParticle=66`).
- `LightParticleFlag`: `PauseParticle = 2` (bit 1). The managed code toggles bit 1 of
  `lightParticleFlag` to pause/resume, and sets `NextGlobalBGState` to switch particle mode.
- **Gap:** particle counts, spawn rates, drift/parallax speeds are in native code — not
  recoverable here (value not located).

### 4.5 VR dimmer (PSVR/Morpheus only)
Included for completeness; these are the only numeric *dimmer opacity* values in managed code:
```
_defaultVr3dDimmerOpacity = 0.95f
SetExtendedDimmerForVR3D(true, 400f, 0f, 400f, 20f, 50f)   // VR3D dimmer geometry params
```
Dimmer changes are animated with named linear presets:
```
MorpheusDimmer      → TransitionVariety.LinearPoint3Sec    (0.3 s linear)
MorpheusVr3dDimmer  → TransitionVariety.LinearPoint4Sec    (0.4 s linear)
VR background mask  → TransitionVariety.LinearPoint15Sec   (0.15 s linear)
```
These are VR-specific (`MorpheusParam.Dimmer` / `.Vr3dDimmer`) and do not apply to the flat
2D shell; they are useful mainly as evidence of the naming scheme:
`TransitionVariety.LinearPoint{N}Sec` = an N/10-second linear tween preset.

### 4.6 Focus behavior (background light follows focus)
`BGLayer` tracks the currently focused widget rectangle to drive the background light that
follows selection. It does **not** render the visible focus ring (that is a widget-toolkit
concern — see Gaps). Mechanics:
```
struct FocusRect { int X, Y; uint Width, Height; }
struct FocusItem { FocusRect FocusRect; long FocusedTime; }
focusedItemQueue = new FocusItem[3];        // 3-slot ring buffer of recent focus positions
focusMoveThreshold = 10;                     // px
```
Each frame the current focus rect is compared to the last recorded one; a new focus event is
committed only when the **Manhattan distance moves > 10 px**
(`|dx| + |dy| > 10f`), timestamped with `FrameTickBasedTime.Ticks`, and pushed into the
3-entry ring buffer. `FocusCheckTimeout = 1000000` ticks (0.1 s). The background light
easing/interpolation between the buffered focus positions is performed natively; the managed
layer only supplies the target rects and timestamps.

---

## 5. Values summary (for reimplementation)

| Concern | Value | Curve | Source (4.03) |
|---|---|---|---|
| Modal presentation style | `ScenePresentationStyle = 2` | — | SystemModalDialog |
| Modal dims via scrim? | No — background basemat swap | — | SystemModalDialog |
| Modal auto-close on Back | No (`HideOnBackButton=false`) | — | SystemModalDialog |
| Open-modal side effect | PS button + capture menu locked | — | SystemModalDialog |
| Basemat (dimmer) shape | Flat (type 1) | — | SystemModalDialog/BGLayer |
| Basemat fade duration | 1000 ms | — | BGLayer |
| Basemat/dimmer color | linear (0.00784, 0.01568, 0.03137) ≈ `#020408` | — | BGLayer |
| Health-warning dialog | 0.5 in / 2.0 hold / 0.5 out (3.0 s) | EaseInOutCubic | SystemModalDialog |
| Text fade-in delay / dur | 0.33 s delay, 1.0 s | EaseInOutCubic | SystemModalDialog |
| Text fade-out | 1.0 s | EaseInOutCubic | SystemModalDialog |
| Texture fade-in | 1.8 s | EaseInOutCubic | SystemModalDialog |
| Wizard opacity push | dur 1.5 s, delay 0.2 s | Linear | SystemModalDialog |
| Wave fade-out | `*= 0.9`/frame (~0.5 s) | exp decay | BGLayer |
| Wave fade-in | `+= 0.01`/frame (~1.7 s) | linear | BGLayer |
| Focus move threshold | 10 px (Manhattan) | — | BGLayer |
| Focus history buffer | 3 slots | — | BGLayer |
| Focus check timeout | 0.1 s | — | BGLayer |
| Transition degree default | Strong | — | BGLayer |
| VR dimmer default opacity | 0.95 | — | BGLayer |
| VR dimmer tween | 0.3 s / 0.4 s / 0.15 s | Linear | BGLayer |
| Bg indicator / blank timeout | 1000 ms / 500 ms | — | BGLayer |

Easing identities present: `EaseInOutCubic`, `Linear`. Named tween presets:
`TransitionVariety.LinearPoint{15,3,4}Sec`, `TransitionVariety.Linear`. No spring or explicit
bezier control points are stored in these assemblies.

---

## 6. Gaps / not located

- **Theme dimmer tokens** — `base-mat.overlay.enabled`, `DefaultThemeHomeScreenDimmer`,
  `DefaultThemeFuncScreenDimmer`, `DefaultThemeTitleNameDimmer`: not present in
  SystemModalDialog / BGLayer / BaseSystem. They belong to the theme/resource assemblies
  (`Sce.Vsh.ShellUI.Theme` and theme resource blobs) — out of scope here.
- **Default `ModalScene.DimBackgroundScene` and `ScenePresentationStyle` semantics** — the
  base `ModalScene`, `TransitionAnimation`, `AnimationOption`, `AnimationCurve`,
  `PushWizardShowing/Hiding` types live in the UI-toolkit assembly (not ShellUI). Every
  ShellUI modal sets `DimBackgroundScene=false` explicitly, so the framework default value is
  not observable from ShellUI. Value not located.
- **Push/Pop wizard curve+duration** — `PushWizardShowing/Hiding`, `PopWizardShowing/Hiding`
  internals are toolkit-side; only their use is visible here.
- **Particle counts / drift / parallax** — native (`BGLayerNative`); managed code only sets
  mode flags. Not recoverable from managed IL.
- **Focus-ring geometry/color and scale-on-focus** — the visible selection highlight is a
  widget-toolkit feature; BGLayer only consumes the focused rect to steer the background
  light. Not present here.
- **9.00 literal values** — the 9.00 `common_ex/lib` SystemModalDialog (and parts of BGLayer)
  are reference facades; concrete numbers above are from the 4.03 full bodies. The 9.00
  metadata confirms identical type/constant names, so the model is treated as unchanged
  4.03 → 9.00, but 9.00-specific literals could not be independently confirmed where IL was
  stripped.
