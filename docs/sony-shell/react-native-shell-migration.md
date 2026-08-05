# React Native shell migration

Status: **active implementation guide**. This supersedes the temporary
implementation handoff that accompanied the imported shell work.

## Product split

Prosperismo has two deliberate routes in one React Native Windows application:

- **Desktop** is the compact mouse-and-keyboard launcher. It owns scanning,
  library management, game launch, patches, trophies, and detailed emulator
  configuration.
- **Big Picture** is the controller-first shell. It presents the same library,
  session, and settings data in a fixed 1920x1080 logical scene. It is the
  fidelity-focused route and must not fall back into Desktop when navigating
  upward from Home.

The routes share host services and settings; neither keeps a second database.
The Windows executable accepts `--big-picture` (or `-bigpicture`) to enter the
controller shell directly, matching the launch-mode boundary used by desktop
clients such as Steam.

## Oracle boundary

Firmware, native PUI, readable React Native bundles, decoded assets, shaders,
and direct captures under `C:\prosperismo\ps5oracle` are the sole authority
for shell behavior. Imported source trees are historical implementation
references only. Do not copy proprietary application code or assets into this
repository; express recovered behavior through original TypeScript, C++, and
tests.

## Current implementation

The active shell source is:

- `frontend/ProsperismoLauncher/src/bigPicture/BigPictureShell.tsx`
- `frontend/ProsperismoLauncher/src/bigPicture/RecoveredHomeShell.tsx`
- `frontend/ProsperismoLauncher/src/bigPicture/shellMetrics.ts`
- `frontend/ProsperismoLauncher/src/bigPicture/shellState.ts`

It currently provides:

- fixed 1920x1080 design-space scaling. HOME multiplies every recovered metric
  into the viewport instead of applying a root transform, because the latter
  could detach the RNW visual tree and leave a white client area;
- independent remembered game selection and top-bar focus;
- the recovered 106→168 title-card scale, 8/16px strand gaps, scaled card
  corner geometry, spring movement, caption placement, and eleven-card cap.
  Both strand solvers (`homeTileLeft` and the `shellTileBaseX` metrics helper)
  now agree that tiles on either side of the selection clear it by the 16px
  focused margin, and a regression test pins them together;
- the traced FontSizePS tokens at every HOME call site: space labels are
  SizeLarge bold with the 0.6-opacity normal-weight blur state, system icon
  labels are SizeXSmall, the clock is SizeLarge tabular-nums right-aligned,
  and the selected title is SizeNormal inside the 62px metadata strip. All
  text routes through the native font resolver instead of hardcoded family
  strings, so the audited Fira Sans → Segoe fallback applies everywhere. The
  strip's separator/tag row remains structurally absent because every
  Prosperismo title is a PS5 (`PPR`) package, which the console shows untagged;
- the space-switcher focus region measures the live label container from
  layout (authored bounds remain a pre-layout fallback), and the invented
  focused-label underline has been removed — the focus ring is the only
  focus treatment, as on the console;
- the Home-owned side of the hub contract from
  [ps5-hub-and-cards](ps5-hub-and-cards.md) §1.3–1.5: the m130 vertical axis
  with the −166px home lift on SPRING_OPTIONS_FAST, the m571 handoff that
  flies the selected tile into the 80×80 hub-header badge at (48, 48) while
  non-selected tiles fade on FASTER and the TitleContainer parks beside the
  badge, the m507 hub-appears one-shot (16.67ms pre-roll, 300ms
  cubic-bezier(0.25, 0.1, 0.25, 0.8), 850px travel), and the m503 one-shot
  `focusReady` gate. No hub app module executes yet, so every experience is
  unready, Down on a tile is swallowed exactly as on a console whose hub has
  not booted, and the hub frame below the y=128 module boundary renders no
  invented content. The next faithful step remains the app-module/channel
  adapter, not placeholder card design;
- the recovered FirstWave **surface** (the ripples) in both layers:
  `src/bigPicture/shellWaveField.ts` and the native
  `windows/Prosperismo/FirstWaveSurface.{h,cpp}`, which sits beside the
  existing `FirstWaveBackground` plate. A 4x4 bicubic control lattice
  displaced by a single 3D simplex evaluation, one-sided (the firmware squares
  the scaled noise), under the recovered ten-second entrance envelope
  `0.4*e^3 + 0.16`. The native file is platform-header-free and its host test
  (`FirstWaveSurfaceHostTest.cpp`) builds and passes on macOS/clang, with the
  probe values matching the TypeScript reference to 1.33e-06. See
  [firstwave-decoded-passes](firstwave-decoded-passes.md);
- the recovered FirstWave **blur** in `windows/Prosperismo/FirstWaveBlur.{h,cpp}`:
  the exact 13-tap separable Gaussian (weights summing to 1.0 at a fitted
  sigma of 3.8462 texels, offsets exactly +/-k/3840) and its radial mask,
  including the threshold below which the firmware takes a single unblurred
  sample. `FirstWaveBlurHostTest.cpp` asserts normalization, symmetry,
  monotonicity, the fitted sigma, the offset scaling and the mask's
  plateau/decay/threshold behaviour;
- the recovered background **particle** maths in
  `windows/Prosperismo/FirstWaveParticle.{h,cpp}`: the six-vertex billboard
  expansion and inline corner table, the size lottery (with the firmware's own
  magic-number reductions, checked against plain modulo), the minimum-screen-size
  clamp, the folded projection constants, the small points' anisotropic radius /
  flat-top / power falloff / life fade / seven-entry palette selection, and the
  large discs' computed defocus width, life fade, alpha and HSV pair.
  `FirstWaveParticleHostTest.cpp` carries a verified negative control, so the
  assertions are load-bearing. Deliberately **not** ported: the lighting loop,
  the gradient projections and anything touching textures, because those are
  driven by runtime data that is not recovered. See
  [particle-system](particle-system.md) and [particle-draw](particle-draw.md).

  All three native modules carry no platform headers (`pch.h` is behind
  `#ifdef _WIN32`), so they build and their host tests pass on macOS/clang. The
  implementation files are in `Prosperismo.vcxproj`; the `*HostTest.cpp`
  programs deliberately are not. **The mesh tessellation, OIT resolve, blur and
  particle passes are recovered as arithmetic but are not yet executed by a
  renderer** — that remains native-renderer work;
- the background transition contract (`shellBackgroundTransition.ts`) from
  [bglayer-managed-contract](bglayer-managed-contract.md): the packed
  `type | (degree << 16)` word, the degree table that derives HOME's 633.333ms
  Normal transition, the plate-flip rule, the aliased basemat values, and the
  normalized ripple origin. The origin is the **focused tile's centre**
  (`screenX/1920`, `screenY/1080`), with the screen centre used only when
  nothing is focused — a centre-only ripple is visibly wrong on the console.
  This is wired, not merely defined: `BigPictureShell` hands
  `ShellBackgroundSurface` the focused strand card's design-space bounds, the
  owner takes its cross-fade duration from the degree table instead of a
  hard-coded constant, and it maintains the double-buffered plate id using the
  firmware's flip rule. The native pass that actually draws the ripple is not
  reproduced, so the owner cross-fades in the meantime;
- the host data plane of that adapter (`shellHubModuleHost.ts`), translated
  from the integration branch's `ShellHubModuleContract`/`Ps5AppBrowseMetadata`:
  hubUri → `scheme:path` module/channel identity with the query travelling
  separately, query-only changes retaining the native slot while remounting
  the guest context key, per-experience m512 state (one-shot focusReady,
  validated `onTemplateChange` offsets, opaque background payloads), the
  4 / 260 ms / 60 000 ms / 300 ms pool constants, the five no-response
  callback names, and Sony's `cid:scp:`/`cid:local:` AppBrowse key encoders.
  Home's descend gate is keyed by the encoded experience id, never the raw
  title id. What remains for a live hub is the executing guest module itself;
- the native card-focus geometry: a 3px line offset 3px outside the card, with
  the observed cool-to-warm edge treatment, plus a separate translucent card
  shimmer pass. The area remains clear for the first three seconds of Sony's
  five-second cycle and pulses only during the final two seconds;
- a 56px circular system-icon focus surface with delayed glyph inversion;
- real React Native Windows focus transfers between strand, spaces, and system
  controls; Arrow Up/Down no longer leaves an old desktop target active. The
  remembered game selection stays enlarged when focus visits the top band, but
  its card focus passes are hidden so card and system highlights cannot appear
  simultaneously. RNW 0.83 focuses the host ref directly; `UIManager.focus`
  is guarded because that function is absent in this runtime and was the
  concrete cause of the formerly responsive white Big Picture window;
- selected-title `pic0` composition with the recovered 633.333ms Normal HOME
  transition timing, while Settings returns to the shell plate;
- a retained local-only native-frame bridge that can dynamically enumerate
  timestamped renderer output instead of baking a frame count into the app.
  The preferred oracle sequence is `rnw-native-bottom-shared-51-v2`: 51 unique
  1920x1080 frames at 10fps, produced by the persistent original-shader player
  for raw Bottom state 1 using the setter-proven shared `spread_expanded`
  particle body. It runs 0→5s and wraps forward to frame zero; reversing the
  recovered motion was an invented presentation and has been removed.
  The six-frame `shell-shot-small-persistent` sequence and the later
  `shell-shot-bottom-shared-native-5` proof remain fallbacks; two of the latter
  proof's three samples are byte-identical.
  The frames are not copied into source control or the application package.
  The recovery HOME slice no longer plays those PNGs in product. They remain
  oracle evidence and a fallback diagnostic for the live producer;
- a code-generated React Native Windows Fabric/Composition surface and a
  versioned two-slot BGRA shared-memory protocol connect the live renderer
  helper. `ShellBackgroundSurface` is mounted once, below the React shell, and
  is retained while Home, Library, Settings, search, profile, options, and
  dialogs change. It evaluates the translated 12.40 `fw_background_p` plate at
  30fps. The producer contributes only the recovered additive particle layer;
  no oracle image, shader, texture, or draw cache is copied into the package;
- a local-only firmware-icon bridge for the exact `emoji_settings`,
  `emoji_game_and_apps`, `emoji_system`, `emoji_game`, and `iconid_search`
  payloads extracted from the
  user's `Sce.PlayStation.PUI_UI3.rco`. The files remain under
  `ps5oracle/evidence/shell-icons-runtime`, are never bundled or committed, and
  retain the white-to-dark glyph inversion used by system focus. The native
  host resolves these paths and the related RCO/GNF/Home-source inputs from a
  configurable `PROSPERISMO_PS5_ORACLE`; no absolute developer path is kept in
  TypeScript;
- Prosperismo-owned settings categories, an undimmed dark options popup using
  the recovered 652px/16px/190px control-menu geometry, and a transient
  in-app toast with the recovered 40px-icon and 300ms/3500ms/200ms lifecycle;
- Settings category focus is attached to the actual focusable rail row rather
  than a non-interactive preview value, and route entry transfers native focus
  to that row;
- keyboard/controller capture that keeps Home navigation inside Big Picture.
- a native Big Picture presenter transition marshalled through React Native's
  UI dispatcher; changing the AppWindow presenter on the module thread can
  detach the Win32 React island and leave a responsive white client area.

- **all five Windows natives now degrade cleanly off Windows.**
  `ProsperismoFocusRing` and `ProsperismoLocalImage` previously called
  `codegenNativeComponent` at module top level and were imported statically, so
  importing the home shell was Windows-only. Resolution now goes through
  `src/bigPicture/nativeShellComponents.ts` using the same
  `Platform.OS` → `UIManager.hasViewManagerConfig` → lazy `require`-in-`try`
  pattern `ShellBackgroundSurface` already had, and
  `__tests__/nativeShellComponents.test.ts` pins it;
- a **browser preview harness** at `web/` (`npm run preview:web`, Vite +
  `react-native-web`, port 5273). It exists because there is no
  `react-native-macos` for the 0.83 line and this host has no Xcode — see
  [macos-preview](macos-preview.md) for that finding and
  [web-preview](web-preview.md) for the harness. It is confined to `web/` and
  devDependencies, is excluded from the app's `tsconfig.json`, and participates
  in no Windows build. **It is a layout and motion preview only**: the
  background, focus ring, firmware icons and font resolver are all absent, and
  its focus outline is a plain-white stand-in that must never appear in a
  fidelity comparison.

The React Native layer deliberately does **not** claim to execute a guest shell
application or the still-untranslated PUI focus shaders. It uses the settled
geometry, colors, timing, and state contracts exposed by the oracle. The native
surface now executes the translated FirstWave pixel pass, and the optional
out-of-process renderer executes the recovered particle shader/resources from
the user's oracle. The FirstWave mesh/OIT/blur/FXAA and particle passes are
**decoded but not rendered**: their arithmetic is ported and host-tested, and no
renderer executes them yet. That gap must be closed with the recovered maths,
never with invented ambient motion.

**Do not treat the background's visual balance as settled.** The only candidate
capture in the local oracle failed provenance
([reference-video-grading](reference-video-grading.md) §0), so how prominent the
wave is against the particles on a real console is **not recovered** — see the
"Recovered background pipeline" map in
[firstwave-decoded-passes](firstwave-decoded-passes.md). Presentation values
taken from that clip are better than guesses and are not firmware constants.

## Recovery checkpoint (2026-08-02)

The earlier migration at `8711d9a` was a useful host/behavior checkpoint, not
a visually proven port of the Avalonia shell. The complete prior control tree
remains on `C:\sharpemu-home`, branch `feat/ps5-home-shell`. HOME is now being
translated from that source into `RecoveredHomeShell.tsx` in bounded slices.
The first slice includes the 126px top band, space switcher, system controls,
clock, 106/168px title strand, selected caption, one exclusive focus owner,
library shortcut, runtime oracle icons, and asset-free fallbacks.

The Release executable builds and starts the Big Picture component without a
React error after replacing the unsupported focus call. Native React logging
is persisted to `%LOCALAPPDATA%\Prosperismo\launcher-startup.log`, and the root
error boundary prevents future module/render faults from degrading to an
unexplained white surface. The native background is guarded twice: JavaScript
loads its code-generated component only when RNW reports the Fabric registration,
and the C++ view uses non-throwing composition-interface discovery. Missing or
incompatible native support therefore leaves the ordinary React tree visible
over the neutral basemat. A screenshot comparison is still required before
calling this slice visually accepted.

## Next validation gate

1. Produce paired 1920x1080 Big Picture captures for icon, card, settings,
   modal, and toast states.
2. Compare focus line thickness, card-wash opacity, and glyph inversion timing
   to the oracle captures before changing the visual constants.
3. Validate Home layer mask `3` and Settings/Library/modal layer mask `1`
   while the same native surface remains mounted across the transition.
4. Route the settings category detail pages to Prosperismo data; never expose
   a console settings hierarchy in the product shell.

Run `npm run typecheck`, `npm run lint`, and `npm test -- --runInBand` from
`frontend/ProsperismoLauncher` after shell changes.

`npm run windows:bundle` is also part of the gate. The Metro configuration
explicitly supports a worktree that shares a dependency cache through a Windows
junction, so bundle resolution remains reproducible without duplicating the
large dependency tree.

## Validation status (2026-08-05)

Measured on the macOS development host. This section is the current-numbers
anchor; other notes referencing gate results defer to it.

| Gate | Command | Result |
|---|---|---|
| Tests | `npx jest --runInBand` | **23 suites, 126 tests, all passing** |
| Types | `npx tsc --noEmit` | **exit 0**, no diagnostics |
| Lint (shell) | `npx eslint src/bigPicture __tests__` | **0 errors**, 1 warning |
| Lint (project) | `npx eslint .` | **0 errors**, 1 warning |
| Native surface | `clang++ -std=c++20 -O2 -Wall -Wextra` + run | **passes**, warning-free |
| Native blur | same | **passes**, warning-free |
| Native particle | same | **passes**, warning-free |

The single lint warning is `react-native/no-inline-styles` at
`src/bigPicture/RecoveredHomeShell.tsx:564`
(`{ backgroundColor: 'transparent' }`). It is pre-existing and is a warning, not
an error.

The three native host tests are built and run standalone — they are **not** in
`Prosperismo.vcxproj`, by design:

```
cd frontend/ProsperismoLauncher
clang++ -std=c++20 -O2 -Wall -Wextra -I windows/Prosperismo \
    windows/Prosperismo/FirstWave<Surface|Blur|Particle>HostTest.cpp \
    windows/Prosperismo/FirstWave<Surface|Blur|Particle>.cpp -o /tmp/fw && /tmp/fw
```

**Not exercised on this host:** the Windows build (`npm run windows`),
`npm run windows:bundle`, and any on-screen fidelity comparison. This machine
has no Windows toolchain and no Xcode — see [macos-preview](macos-preview.md)
§2. The Windows gates and the paired-capture gate above remain outstanding; a
green table here is **not** a claim of visual acceptance.
