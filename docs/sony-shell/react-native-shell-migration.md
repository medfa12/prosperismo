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
- `frontend/ProsperismoLauncher/src/bigPicture/shellMetrics.ts`
- `frontend/ProsperismoLauncher/src/bigPicture/shellState.ts`

It currently provides:

- fixed 1920x1080 design-space scaling;
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
  simultaneously;
- selected-title `pic0` composition with the recovered 633.333ms Normal HOME
  transition timing, while Settings returns to the shell plate;
- a local-only native-frame bridge: Big Picture dynamically enumerates
  timestamped renderer output instead of baking a frame count into the app.
  The preferred oracle sequence is `rnw-native-bottom-shared-51-v2`: 51 unique
  1920x1080 frames at 10fps, produced by the persistent original-shader player
  for raw Bottom state 1 using the setter-proven shared `spread_expanded`
  particle body. It runs 0→5s and wraps forward to frame zero; reversing the
  recovered motion was an invented presentation and has been removed.
  The six-frame `shell-shot-small-persistent` sequence and the later
  `shell-shot-bottom-shared-native-5` proof remain fallbacks; two of the latter
  proof's three samples are byte-identical.
  The frames are not copied into source control or the application package;
  absence is a clean fallback, not an error. When the sequence is present the
  generic brand-art fallback is suppressed, so it cannot obscure the native
  particles; selected-title `pic0` artwork still crossfades above them. The
  background owner remains mounted across Home, Library, and Prosperismo
  Settings, matching the firmware evidence that Settings retains HomeScreen
  preset 4 rather than selecting a Settings-only palette or static fallback;
- a code-generated React Native Windows Fabric/Composition surface and a
  versioned two-slot BGRA shared-memory protocol are prepared for the live
  renderer helper. The surface is deliberately not mounted in the product tree
  until that producer is connected and its lifecycle handshake passes. The
  currently visible animation is the recovered 51-frame renderer sequence
  above, not a claim of continuous shader execution;
- a local-only firmware-icon bridge for the exact `emoji_settings`,
  `emoji_game_and_apps`, and `emoji_system` PNG payloads extracted from the
  user's `Sce.PlayStation.PUI_UI3.rco`. The files remain under
  `ps5oracle/evidence/shell-icons-runtime`, are never bundled or committed, and
  retain the animated white-to-dark glyph inversion used by system focus;
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

The React Native layer deliberately does **not** claim to execute proprietary
PUI focus shaders, native particle programs, or a guest shell application. It
uses only the settled geometry, colors, timing, and state contracts that the
oracle exposes. The title/background composition is a state-responsive
crossfade; native background execution remains a separate emulator-renderer
integration task and must not be replaced with invented ambient motion.

## Next validation gate

1. Produce paired 1920x1080 Big Picture captures for icon, card, settings,
   modal, and toast states.
2. Compare focus line thickness, card-wash opacity, and glyph inversion timing
   to the oracle captures before changing the visual constants.
3. Connect the native background owner through a bounded host surface only
   after its existing emulator renderer exposes a stable frame contract.
4. Route the settings category detail pages to Prosperismo data; never expose
   a console settings hierarchy in the product shell.

Run `npm run typecheck`, `npm run lint`, and `npm test -- --runInBand` from
`frontend/ProsperismoLauncher` after shell changes.

`npm run windows:bundle` is also part of the gate. The Metro configuration
explicitly supports a worktree that shares a dependency cache through a Windows
junction, so bundle resolution remains reproducible without duplicating the
large dependency tree.
