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
  corner geometry, spring movement, caption placement, and eleven-card cap;
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

The React Native layer deliberately does **not** claim to execute a guest shell
application or the still-untranslated PUI focus shaders. It uses the settled
geometry, colors, timing, and state contracts exposed by the oracle. The native
surface now executes the translated FirstWave pixel pass, and the optional
out-of-process renderer executes the recovered particle shader/resources from
the user's oracle. The remaining FirstWave mesh/OIT/blur/FXAA passes are still a
recovery boundary and must not be replaced with invented ambient motion.

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
