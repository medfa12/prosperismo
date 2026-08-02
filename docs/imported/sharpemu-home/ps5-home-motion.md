<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 Home UI motion — the real animation values

A clean-room design reference for driving Prosperismo's recreated shell menus on
the modern PS5 Home shell's actual motion. Where `docs/ps5-shell-motion.md`
established that the cleartext `.rco` containers carry **no** visual timelines
(only the soundscript/event vocabulary), this document fills that gap: the shell
is React Native, and its animation durations, easings, and spring configs live in
the Home UI JavaScript bundle. Those values are extracted and tabulated here.

**Values/behaviour only.** Nothing below is Sony source. The decrypted bundle is
read from a gitignored location; only extracted numbers and short identifier/key
locators are recorded. No code blocks are reproduced.

## Provenance

- **Home bundle:** `NPXS40002.js` — internal build tag
  `rnps-home_v2_ppr_releases_03.00` (visible in an embedded source path for
  `js-modules-hub-sdk/src/components/SceneList`). This is the shell Home app.
- **Shared motion library:** `NPXS40141.base.js` — defines the custom `Easing`
  family (the `*PS` curves and `parametricCurve`).
- Locators cited below are the identifier or config key nearest the value
  (e.g. `SPRING_OPTIONS_FAST`, `modalAnimationType`, module id `l(49)`), not
  copied source. All durations are milliseconds; `useNativeDriver` is `true` on
  effectively every config.

## 1. Global animation constants

One `ANIMATION` constants object (Home bundle, default export near module id
`l(720)`) parameterises most timings:

| Constant | Value | Meaning |
|---|---|---|
| `ANIMATION.TIMING.DEFAULT` | **300 ms** | standard transition duration |
| `ANIMATION.TIMING.LOADING` | **750 ms** | loading-pulse half-cycle |
| `ANIMATION.GRADIENT_OFFSET` | −80 | background-gradient slide offset (px) |
| `ANIMATION.OPACITY.DEFAULT` | MIN 0 / MAX 1 | generic fade endpoints |
| `ANIMATION.OPACITY.LOADING` | MIN 0.05 / MAX 0.08 | loading shimmer opacity band |
| `ANIMATION.OPACITY.LOADING_GRID` | MIN 0 / MAX 0.03 | loading-grid shimmer band |
| `ANIMATION.OPACITY.ACTION_INDICATOR` | MIN 0.7 / MAX 1 | action/press indicator fade |
| `ANIMATION.OPACITY.GRADIENT` | MIN 0.01 / MAX 1 | gradient overlay fade |

## 2. The easing family (custom `*PS` curves)

The shell does **not** use cubic-bezier for its primary transitions. It uses a
custom parametric-curve family defined in `NPXS40141.base.js` via a two-parameter
generator `parametricCurve(back, flat)`:

- `flat` (t) sets the tail sharpness: effective power `r = 9·t + 1`, and a
  time-compression factor `i = 400 / (600·t + 200)`.
- `back` (e) sets anticipation: `0` = none, negative = pull-back/undershoot at
  the start, `≥1` = pure ease-in.
- The output for the ease-out case (back ≤ 0) is
  `y = 1 − (1 − min(x·i, 1))^r`.

Named instances (all defined in the shared lib; **Home actually references** the
ones flagged **[used]**):

| Curve name | `parametricCurve(back, flat)` | Derived power r / factor i | Character |
|---|---|---|---|
| `easeOutBreezePS` **[used]** | (0, 0.4) | r ≈ 4.6, i ≈ 0.909 | gentle ease-out — the shell's **default** curve |
| `easeOutBlastPS` **[used]** | (0, 1) | r = 10, i = 0.5 | aggressive front-loaded ease-out |
| `easeOutPS` | alias of `easeOutBlastPS` | — | — |
| `easeSmoothOutBreezePS` | (0.05, 0.4) | slight anticipation + breeze | — |
| `easeSmoothOutBlastPS` | (0.05, 1) | slight anticipation + blast | — |
| `easeFlyingOutBreezePS` | (−0.4, 0.4) | strong pull-back then breeze | — |
| `easeFlyingOutBlastPS` | (−0.4, 1) | strong pull-back then blast | — |
| `easeInPS` | (1, 1) | pure ease-in (r = 10) | — |
| `easeInOutPS` **[used]** | `inOut(quad)` | quadratic in-out | symmetric — used for the loading pulse |

Also present and used in Home:

- `Easing.linear` — modal hide, and a couple of raw fades.
- `Easing.poly(5)` — quintic ease, used for a progress-bar fade (200 ms).
- `Easing.bezier(0.25, 0.1, 0.25, 0.8)` — list scroll (SceneList) and scroll-to-index.
- `Easing.bezier(0.2, 0.7, 0.6, 0.8)` — programmatic `willScroll({from,to})` list scroll.

## 3. Named spring presets

Four reusable spring configs (shared module id `l(49)`, exports
`SPRING_OPTIONS_*`). RN/Reanimated physical-spring form (`stiffness`, `damping`,
`mass`); `overshootClamping` where noted; `useNativeDriver: true` on all.

| Preset | stiffness | damping | mass | overshootClamp | Notes |
|---|---|---|---|---|---|
| `SPRING_OPTIONS_SLOW` | 130 | 25 | 1 | yes | heavy/soft settle |
| `SPRING_OPTIONS_SLOWER` | 100 | 20 | 1 | yes | slowest, softest |
| `SPRING_OPTIONS_FAST` | 200 | 100 | 0.2 | no | snappy, light mass |
| `SPRING_OPTIONS_FASTER` | 600 | 100 | 0.2 | no | very snappy |

Two component-local spring defaults (not from the shared table):

| Where | stiffness | damping | mass | overshootClamp |
|---|---|---|---|---|
| horizontal scroll view default (`p =`) | 600 | 100 | 0.2 | yes |
| virtualized list `springOptions` default | 400 | 50 | 0.2 | yes |

## 4. Animation by UI moment

### Focus / interaction (tiles, list items)

Interaction state enum (module `l(720)`): **`GLANCED` → `FOCUSED` → `ACTION`**
(glance / focus / press).

| Moment | Type | Duration | Curve / spring | Delay |
|---|---|---|---|---|
| Focus in (`GLANCED → FOCUSED`) | timing | 300 ms (`TIMING.DEFAULT`) | `easeOutBreezePS` | 0 |
| Focus out (`FOCUSED → GLANCED`) | timing | 300 ms | `easeOutBreezePS` | 0 |
| Press micro-blip (sequence to 0 and back) | timing×2 | 35 ms each | inherited default | 0 |
| Action indicator fade | timing/opacity | 300 ms | `easeOutBreezePS` | 0 |

### Tile / experience scale + view switch (home ↔ hub / experience switcher)

The experience switcher animates a `switcher` value plus a per-tile
`experienceScales` array and named position values (`hub` / `system`).

| Moment | Type | Duration / spring | Stagger | Notes |
|---|---|---|---|---|
| Switcher open/close | spring | `SPRING_OPTIONS_SLOWER` (100/20/1) | — | `toValue: 0` |
| Experience tiles animate in | spring, staggered | `SPRING_OPTIONS_SLOWER` | **60 ms** per tile | `experienceScales.slice(0,n)` |
| Vertical home↔hub position | spring | `SPRING_OPTIONS_FASTER` (600/100/0.2) | — | `valueByVerticalPosition.home/.hub` |
| Focus/index positioning | spring | `SPRING_OPTIONS_FAST` (200/100/0.2) | — | `toValue: I[A]` / `g[d]` |
| Icon pop-in | spring | 200/100/0.2 (FAST-equivalent) | — | `Animated.delay(300)` lead-in |

### Home reveal / boot choreography

An orchestrated `Animated.sequence`/`parallel` intro (all springs
`SPRING_OPTIONS_SLOW`, 130/25/1):

| Step | Delay before | Target | Spring |
|---|---|---|---|
| System + title fade | **1050 ms** | `systemOpacity`, `system` | SLOW |
| Title offset within that step | +**333 ms** | `titleOpacity` | SLOW |
| Hub reveal (then `onAnimationEnd`) | **1450 ms** | `hub` | SLOW |

### Dialog / overlay (modal)

Single config `modalAnimationType` (helper builds
`{toValue, duration, easing, delay, useNativeDriver}`):

| Moment | Duration | Easing | Delay |
|---|---|---|---|
| Modal **show** | **250 ms** | `easeOutBlastPS` | **50 ms** |
| Modal **hide** | **300 ms** | `linear` | 0 |

### Scene / page content fade (SceneList)

Shared config `S = { easing: Easing.bezier(0.25, 0.1, 0.25, 0.8) }`:

| Moment | Duration | Easing | toValue |
|---|---|---|---|
| Scene fade-in | **500 ms** | bezier(0.25,0.1,0.25,0.8) | 1 |
| Scene fade-out | **150 ms** | bezier(0.25,0.1,0.25,0.8) | 0 |

### List scrolling

| Moment | Easing | Spring alt |
|---|---|---|
| SceneList / scroll-to-index | `Easing.bezier(0.25, 0.1, 0.25, 0.8)` | — |
| Programmatic `willScroll` | `Easing.bezier(0.2, 0.7, 0.6, 0.8)` | — |
| Horizontal scroll view momentum | — | 600/100/0.2 (clamped) |
| Virtualized list scroll-to | — | 400/50/0.2 (clamped) |

### Grid / tile appear

| Moment | Type | Duration | Stagger |
|---|---|---|---|
| Grid tiles fade in (opacity `y[r]`,`g[r]`) | timing (parallel) | **300 ms** | **16.67 ms** (= 1 frame @ 60 fps) |

### Loading / placeholder

| Moment | Type | Duration | Curve | Endpoints |
|---|---|---|---|---|
| Loading shimmer pulse | `loop(sequence[timing,timing])` | **750 ms** each way | `easeInOutPS` (quad in-out) | opacity 0.05 ↔ 0.08 |
| Loading-grid shimmer | opacity band | — | — | 0 ↔ 0.03 |

### Progress bar

| Moment | Type | Duration | Easing |
|---|---|---|---|
| Bar fade (transferring/promoting/playable) | timing | 200 ms | `Easing.poly(5)` |

## 5. Reconciliation with the .NET-derived values

| .NET decompile said | JS bundle shows | Verdict |
|---|---|---|
| Standard transition **0.3 s** | `TIMING.DEFAULT = 300 ms`; focus in/out = 300 ms | **Confirmed** |
| Focus in/out/press = 0.3 s | Focus in/out = 300 ms; press = short 35 ms blip + FAST springs | Confirmed (focus); press refined |
| Standard curve **EaseInOutCubic** | Default curve is **`easeOutBreezePS`** (parametric ease-**out**, power ≈ 4.6). The only in-out curve is `easeInOutPS` = **quadratic** in-out, used only for the loading pulse | **Differs / refined** — it is an ease-out, and where in-out is used it is quad, not cubic |
| **60 fps** | Tile stagger = **16.67 ms** = exactly one 60 fps frame | **Confirmed** |
| Warp **0.25 s** | Modal **show = 250 ms** (`easeOutBlastPS`, +50 ms delay) is the strongest 250 ms match | Plausible correspondence |
| Modal dimmer flat over **1000 ms** | No standalone 1000 ms dimmer fade found; modal show/hide are 250/300 ms. Reveal choreography has 1050 ms / 1450 ms delays | **Differs / gap** — see §6 |
| Focus **move** = 0.3 s | List/focus movement is spring- or bezier-driven, not a fixed 300 ms timing | Refined |

## 6. Honest gaps

- **Modal backdrop/dimmer opacity ramp:** the modal *content* transition is
  250/300 ms, but a dedicated dimmer-fade duration (the .NET's 1000 ms flat
  `#020408`) was not isolated in this bundle. The 1050 ms figure here is a reveal
  *delay*, not a dimmer fade. Dimmer timing remains unconfirmed from the JS.
- **Focus scale magnitude:** the springs that drive `experienceScales` are
  captured, but the numeric scale endpoints (e.g. 1.0 → 1.08) live in
  per-component style/interpolation config not resolved here.
- **Curve → bezier approximation:** `easeOutBreezePS`/`easeOutBlastPS` are
  parametric, not bezier. For engines that only take cubic-bezier, approximate
  `easeOutBreezePS` with a strong ease-out (roughly `cubic-bezier(0.1, 0.9,
  0.2, 1)`); prefer the exact power/factor form in §2 when possible.
- **Cross-app scope:** only the Home bundle (`NPXS40002.js`) and the shared lib
  were mined. Peer app bundles (settings, store, etc.) may define their own
  timings; not surveyed here.
- **Spring → duration:** RN native springs have no fixed duration; the presets in
  §3 are physical configs, so on-screen settle time depends on distance/velocity.
