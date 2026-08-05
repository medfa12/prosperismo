<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Browser preview of the Big Picture shell

A `react-native-web` harness that renders the Big Picture shell on any machine,
with no Windows toolchain.

```
cd frontend/ProsperismoLauncher
npm run preview:web        # http://localhost:5273
```

> **This is a layout and motion preview, not a fidelity capture.** Never grade
> pixels against it. See "What is real here" below.

## Why this exists rather than react-native-macos

`react-native-macos` cannot be used on this project, for two independent
reasons, either sufficient on its own:

1. **There is no 0.83 line.** Across all 1,194 published `react-native-macos`
   versions there is no 0.80.x, 0.82.x, 0.83.x or 0.84.x. The newest is
   `0.81.9`, whose `peerDependencies` pin `react-native` to **exactly**
   `0.81.6`. This app is on `react-native` 0.83.4, so installation fails with
   `ERESOLVE`. This is not a version-range problem that a flag can bypass —
   the releases do not exist.
2. **The host cannot build one.** The development Mac has Command Line Tools
   only: no `xcodebuild`, no CocoaPods, and system Ruby 2.6.10.

`react-native-web` needs neither Xcode nor CocoaPods.

Historical note: `docs/migration-status.md` line 50 says "this host has VS
Build Tools 2022 17.14". That describes a **different machine from an earlier
session** (the same documents reference `C:\prosperismo\` and
`C:\Users\sharpemu\` paths). It must not be read as a statement about the
current development machine, which is a Mac with no Windows toolchain at all.

## What is real here, and what is not

The shell depends on five Windows natives. All five now degrade cleanly, so
the React tree renders anywhere:

| Native | On the web preview |
|---|---|
| `NativeBackgroundSurface` | **absent** — no FirstWave plate, no particle layer |
| `ProsperismoFocusRing` | **absent** — replaced by a plain outline stand-in |
| `ProsperismoLocalImage` | **absent** — falls back to React Native's own `Image` |
| `ProsperismoHost` | absent — the harness supplies placeholder titles |
| `ShellTypography` | absent — falls back to the system font, not the audited stack |

So what the preview *does* validate is geometry, layout, the strand packing,
spring motion, focus routing and route transitions. What it cannot validate is
anything the recovered background or the UI3 focus treatment contributes.

The focus outline in particular is a stand-in: three pixels of plain white
border, with **none** of the recovered wash, band, warp, shimmer or timing. It
exists so focus stays legible while navigating. It is labelled as such in the
source, and must never appear in a fidelity comparison.

## The guard fix this required

Before this harness existed, `FocusRingNativeComponent` and
`LocalImageNativeComponent` called `codegenNativeComponent` at **module top
level**, and `RecoveredHomeShell` / `ShellFocusOverlay` imported them
statically. Importing the home shell therefore evaluated them on every host,
so the tree was unrenderable anywhere but Windows —
`ShellBackgroundSurface` had always guarded its own surface correctly, but
these two were asymmetric.

Resolution now goes through `src/bigPicture/nativeShellComponents.ts`, which
applies the same pattern the background owner already used: check
`Platform.OS`, then `UIManager.hasViewManagerConfig`, then lazily `require`
the codegen module inside a `try`. Off Windows it returns `null` without ever
requiring the module. `__tests__/nativeShellComponents.test.ts` locks the
regression in, including that importing the home shell does not throw.

## Automation caveat

Driving this preview through browser automation will show a **frozen entrance
animation**. Chrome reports the automated tab as `visibilityState: "hidden"`
and suspends `requestAnimationFrame` entirely, so the shell's startup
choreography never advances past its first frames — the 126px system band
stays at `opacity: 0` because its spring is released at 1050ms of *simulated*
time that never elapses.

The page itself is fine. Open `http://localhost:5273` in an ordinary visible
window and the entrance runs normally. When checking layout under automation,
measure DOM geometry rather than waiting on the animation.
