<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# macOS preview of the Big Picture shell — blocked, 2026-08-05

> **Superseded in part, same day.** This note remains authoritative for **why a
> native `react-native-macos` preview is impossible** (§§1–2) — that has not
> changed. It is **out of date on what happened next**: option 5 of §4
> ("preview the layout without a native macOS host") was subsequently built as
> a `react-native-web` harness under `frontend/ProsperismoLauncher/web/`.
> [`web-preview.md`](web-preview.md) is authoritative for that harness, for the
> native-guard state, and for the current gate numbers. Read the two together
> and prefer `web-preview.md` wherever they differ.

**Result: a *native macOS* preview is not currently possible. Nothing was
installed and no application file was changed *by this investigation*.** This
document records exactly what was measured, why the native attempt stopped, and
what would have to change upstream or on this machine before it could be
retried. It is written to be re-checked: every claim below is a command you can
re-run.

The goal was a *layout and motion preview* on macOS — React layout, strand
geometry, spring motion and focus navigation running under `react-native-macos`,
with the five Windows natives falling back to their JS paths. Pixel parity was
never the goal. That goal is unreachable today for two independent reasons,
either one of which is sufficient on its own.

## 1. Blocker A — no `react-native-macos` exists for React Native 0.83

The application is pinned to `react-native@0.83.4` (`frontend/ProsperismoLauncher/package.json`),
which `react-native-windows@0.83.2` requires via peer `react-native@^0.83.0`.

`react-native-macos` has **1194 published versions on npm; none of them is
0.82, 0.83 or 0.84.** The highest published version in that whole range is
`0.81.9`:

```
npm view react-native-macos versions --json
  # → newest in range: 0.79.0 0.79.1 0.79.4 0.81.0 … 0.81.9   (no 0.80, 0.82, 0.83, 0.84)
```

| Fact | Value | Source |
|---|---|---|
| `react-native-macos` `latest` | `0.81.9`, published 2026-07-13 | `npm view react-native-macos dist-tags` / `time` |
| `react-native-macos` `next` | `0.81.0` (older than `latest`) | same |
| `react-native-macos` `nightly` | `0.78.4` | same |
| Highest `*-stable` dist-tag | `0.81-stable` → `0.81.9` | same |
| `react-native-macos@0.81.9` peer | `react-native: "0.81.6"` — an **exact pin**, not a range | `npm view react-native-macos@0.81.9 peerDependencies` |
| App's react-native | `0.83.4` | `package.json` |

The install therefore fails deterministically. Run in
`frontend/ProsperismoLauncher`:

```
npm install --dry-run react-native-macos@0.81.9
```

```
npm error code ERESOLVE
npm error Found: react-native@0.83.4
npm error Could not resolve dependency:
npm error peer react-native@"0.81.6" from react-native-macos@0.81.9
```

This is a two-minor-version gap (0.81 → 0.83), not a patch skew. It cannot be
closed with a version bump because there is no version to bump to.

**Upstream state.** `microsoft/react-native-macos` *does* carry a `0.83-stable`
branch (also `0.83/saadnajmi/merge-0.83.10`, `0.84-merge`, and 0.85–0.87
branches). Its `packages/react-native/package.json` on that branch reads
`"version": "1000.0.0"` — the React Native monorepo's placeholder for *not yet
versioned, not yet released*. So the port is in progress upstream but nothing
for 0.83 has been published to npm. Verified 2026-08-05 against
`raw.githubusercontent.com/microsoft/react-native-macos/0.83-stable/packages/react-native/package.json`.

**Note on package shape.** `react-native-macos` is a fork of the whole
`react-native` package (npm `main: ./index.js`, its own `codegenConfig`,
repository directory `packages/react-native`), not a plugin layered on top of
core RN. That is why a version mismatch is fatal rather than cosmetic: the
macOS build would be running RN 0.81 JS internals against this app's 0.83.4
Babel preset, 0.83.4 Metro config and 0.83-shaped codegen output.
Whether an `--legacy-peer-deps` install of that mixture would boot was **not
determined** — it was not attempted, because Blocker B makes it moot.

## 2. Blocker B — this Mac cannot build any React Native macOS app today

Measured on this machine, 2026-08-05:

| Requirement | State here |
|---|---|
| macOS | 27.0 (build 26A5388g) |
| Full Xcode | **Absent.** `xcode-select -p` → `/Library/Developer/CommandLineTools`; `xcodebuild -version` errors: *"requires Xcode, but active developer directory is a command line tools instance"* |
| CocoaPods | **Absent.** `pod --version` → `command not found` |
| Ruby | 2.6.10 (system Ruby; current CocoaPods requires a newer Ruby, so installing CocoaPods is itself a prerequisite chain, not a one-liner) |
| Node | v26.5.1 (satisfies the `>= 22.11.0` engine) |

`react-native-macos` builds an `.xcworkspace` through CocoaPods and `xcodebuild`.
Command Line Tools alone are not sufficient. Even if a 0.83-compatible
`react-native-macos` were published tomorrow, this host would still need a full
Xcode install (multi-GB) plus a CocoaPods-capable Ruby before `run-macos` could
produce a window.

**Correction to an earlier doc.** `docs/migration-status.md:50-51` states "this
host has VS Build Tools 2022 17.14/v143". That line describes a *different* machine
from a previous session. The machine this work ran on is the macOS host
described in the table above; it has no Windows toolchain, and the Windows
build was therefore not exercised here either.

## 3. What was verified instead — the JS side is ready, and where it is not

No application file was modified. The three gates were run from
`frontend/ProsperismoLauncher` as a baseline and were green:

| Command | Result **at the time of this investigation** |
|---|---|
| `npx jest --runInBand` | 22 suites, **122 tests, all passing** (0.9 s) |
| `npx tsc --noEmit` | exit 0, no diagnostics |
| `npx eslint src/bigPicture __tests__` | 0 errors, 1 pre-existing warning (`RecoveredHomeShell.tsx:552` inline style) |

**These are a historical snapshot.** The suite has grown since (23 suites / 126
tests as of the 2026-08-05 consistency pass) and the inline-style warning has
moved to line 564. Current numbers live in
[`react-native-shell-migration.md`](react-native-shell-migration.md)
§*Validation status*.

Since the port could not be run, the five native dependencies were audited by
reading the source rather than by observation. The result is **not uniform** —
three degrade cleanly, two are unguarded:

| Native | Guarded? | Behaviour off Windows |
|---|---|---|
| `ProsperismoHost` (module) | **Yes** | `src/native/ProsperismoHost.ts:48` reads `NativeModules.ProsperismoHost` into an optional and every gateway method uses `native?.x() ?? fallback`. Reads resolve empty/false, writes reject with an explanatory `Error`. No throw at import. |
| `ShellTypography` (module) | **Yes** | `src/bigPicture/shellTypography.ts` wraps the constants read in `try/catch` and funnels it through `resolveShellFontFamily`, which returns `'Segoe UI'` for any unrecognised value. Font sizes are the recovered `FontSizePS` tokens and are pure JS, so the type scale survives; only the family resolution is lost. |
| `NativeBackgroundSurface` (Fabric) | **Yes** | `src/bigPicture/ShellBackgroundSurface.tsx:30-56` returns `null` when `Platform.OS !== 'windows'`, *and* checks `UIManager.hasViewManagerConfig('ProsperismoNativeBackground')` before it will even `require()` the codegen module. The comment there is explicit that the ordinary React tree is a complete visible fallback. |
| `FocusRing` (Fabric) | **No** *(fixed since — see below)* | `src/bigPicture/ShellFocusOverlay.tsx:3` and `src/bigPicture/RecoveredHomeShell.tsx:26` import `./FocusRingNativeComponent` statically and render `<ProsperismoFocusRing>` unconditionally. There is no `Platform.OS` check and no `hasViewManagerConfig` probe. |
| `LocalImage` (Fabric) | **No** *(fixed since — see below)* | `src/bigPicture/RecoveredHomeShell.tsx:30` imports `./LocalImageNativeComponent` statically and renders it unconditionally. Same absence of a guard. |

> **Both gaps are now closed.** The recommendation at the end of this section
> was acted on: resolution goes through
> `src/bigPicture/nativeShellComponents.ts`, which applies the same
> `Platform.OS` → `hasViewManagerConfig` → lazy `require`-in-`try` pattern
> `ShellBackgroundSurface` already had, and
> `__tests__/nativeShellComponents.test.ts` pins the regression. All five
> natives now degrade cleanly. See [`web-preview.md`](web-preview.md)
> §*The guard fix this required*, which is authoritative on their current
> state; the table above is the pre-fix audit, kept for the record.

Because these files use the `*NativeComponent.ts` suffix, `@react-native/babel-preset`
rewrites the `codegenNativeComponent()` call at build time into a static view
config registration, so the *import* is unlikely to throw. What the renderer
then does when asked to mount an unregistered host component on macOS —
silently drop it, render an "Unimplemented component" placeholder, or raise —
is **not determined**; it was not observed, and it is not safe to assert from
reading `node_modules/react-native/Libraries/Utilities/codegenNativeComponent.js`
alone. This asymmetry is the first thing to fix in any retry: `ShellFocusOverlay`
and the `RecoveredHomeShell` icon path need the same two-stage guard
`ShellBackgroundSurface` already has.

**One further behavioural consequence, independent of the natives:**
`getStartupRoute()` resolves `'desktop'` when the host module is absent
(`src/native/ProsperismoHost.ts`), and `App.tsx:360-365` only switches to
`'big-picture'` when that promise yields `'big-picture'`. A macOS preview would
therefore boot into `DesktopLauncher`, and Big Picture would have to be entered
through the in-app control (`App.tsx:428`, `onBigPicture`). That is not a bug —
it is the documented fallback — but it means "boots" and "shows the Big Picture
home surface" are two different milestones.

## 4. Options, in order of cost

None of these were started. They are recorded so the choice stays with the
maintainer.

1. **Wait for upstream.** `react-native-macos` `0.83-stable` exists but is
   unpublished. If and when a 0.83.x is released to npm, this task becomes the
   originally-scoped job again — but Blocker B still has to be cleared first.
   Re-check with `npm view react-native-macos dist-tags`.
2. **Install the macOS toolchain.** Full Xcode + a CocoaPods-capable Ruby.
   This is required by *every* option that ends in a native macOS window, and
   it is worth doing independently of which RN version lands. It does not on
   its own unblock anything.
3. **Build `react-native-macos` from the `0.83-stable` branch.** Source-build a
   React Native fork at a placeholder version and link it in. Highest cost,
   highest breakage risk, and it would put an unreleased fork in the dependency
   graph alongside `react-native-windows@0.83.2`.
4. **Downgrade the app to the 0.81 line.** `react-native@0.81.6` +
   `react-native-macos@0.81.9` + `react-native-windows@0.81.32` (which does
   exist). This would satisfy every peer range, but it moves the *Windows*
   target backwards two minors and would require re-validating the vcxproj,
   the codegen output under `windows/Prosperismo/codegen`, and the native
   FirstWave sources. Explicitly out of scope for this task, which required
   not downgrading `react-native-windows`.
5. **Preview the layout without a native macOS host** (for example a
   `react-native-web` or storybook-style harness). This exercises layout,
   strand geometry and spring motion without Xcode or a macOS RN fork, but it
   is a *different* preview target than the one requested, with its own
   fidelity caveats — it is a change of plan, not a fix.

   > **This is the option that was taken**, later the same day and outside the
   > scope of this investigation. The harness lives at
   > `frontend/ProsperismoLauncher/web/` and runs with `npm run preview:web`.
   > See [`web-preview.md`](web-preview.md). Options 1–4 remain untaken and
   > their costs above still stand.

## 5. Standing caveat

Whichever option is taken, the resulting macOS preview would be a **layout and
motion preview only**. On macOS the FirstWave background plate, the UI3 focus
ring, the firmware icon rasters and the DirectWrite font resolution are all
Windows-native and would render as their JS fallbacks. The recovered
constants those surfaces are built from — see
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md),
[`bglayer-managed-contract.md`](bglayer-managed-contract.md) and
[`ps5-focus-highlight.md`](ps5-focus-highlight.md) — remain Windows-only in
this application. Nothing in this document changes any recovered firmware
claim.
