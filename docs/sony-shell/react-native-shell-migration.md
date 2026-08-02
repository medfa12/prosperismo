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
  wash/shimmer pass;
- a 56px circular system-icon focus surface with delayed glyph inversion;
- real React Native Windows focus transfers between strand, spaces, and system
  controls; Arrow Up/Down no longer leaves an old desktop target active;
- selected-title `pic0` composition with the firmware default 1000ms
  cross-fade, while Settings returns to the shell plate;
- Prosperismo-owned settings categories, an undimmed dark options popup using
  the recovered 652px/16px/190px control-menu geometry, and a transient
  in-app toast with the recovered 40px-icon and 300ms/3500ms/200ms lifecycle;
- keyboard/controller capture that keeps Home navigation inside Big Picture.

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
