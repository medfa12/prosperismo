# PS5 UI recreation — current state of work

This is the short handover. The canonical target, provenance boundary, and
surface-by-surface ledger are in `ps5-reactive-shell.md`; evidence ranking is in
`ps5-reverse-engineering-index.md`.

## Worktrees

- `codex/ps5-shell-integration` at `C:\sharpemu-integration`: canonical merged
  shell line. It contains both frontier histories plus the reconciled focus,
  Home Hub, Settings, and native-background contracts.
- `feat/ps5-home-shell` at `C:\sharpemu-home`: Sony-style SharpEmu shell.
- `exp/shell-boot` at `C:\Users\sharpemu\sharpemu-workers\shell`: execution of
  Sony's own NPXS40087 under SharpEmu. These two frontier worktrees remain as
  preserved source lines; new integrated work belongs on the canonical line.

## Implemented foundation

- Sony-derived 1920x1080 home geometry, safety area, typography, title strand,
  icon pipeline, and title-art fallback.
- Separate Sony and conventional desktop presentation modes, with explicit
  `--big-picture`/`--sony-ui` and `--desktop-ui` launch forms.
- Sony-shaped Settings route containing SharpEmu settings only. The legacy
  desktop Options editor remains exclusive to desktop mode; D-pad/stick,
  Cross, Circle, and keyboard share the two-level category/detail route.
- NPXS40071-derived 5x3 Game Library surface behind the Home switcher's
  eleven-tile cap, with selection preservation and controller navigation.
- Home now owns a separate 1920x1080 Hub frame at the recovered y=128 module
  boundary. Once a pooled guest has called `focusReady`, Down hands the selected
  168 px experience tile to the 80 px header badge at (48,48), lifts Home by
  166 px, fades sibling tiles on the separate FASTER driver, and Up/Circle
  restores the remembered Home selection. Default runtime swallows Down while
  no guest is ready, exactly as HOME does. `SHARPEMU_PS5_HUB_PREVIEW=1` is an
  explicit developer-only bypass for inspecting the recovered transition. The
  minimized tile and moving title remain Home-owned in embedded mode; the Game
  Hub's separate header is standalone-only and is not duplicated.
- Focus line-band and translucent card-wash passes using the recovered rounded
  SDF, noise texture, rotating shimmer, seven-colour table, and firmware curves.
- Original `AreaFocus` and `LineFocus` shaders from `libScePsm.sprx` compile,
  validate, and execute live with their real texture/buffer ABI, dedicated
  original vertex programs, global `ClipPos`, and persistent separately sized
  Area/Line targets.
- Background state/mode mapping, plate, title image, and basemat composition.
- Native background decoder with particle event structure, exact resource-bank
  routing, field names, a byte-exact resource-state sampler, and a persistent
  one-pass Vulkan renderer for ColdBoot's two ordered large-particle groups.
- The steady `spread_expanded` body can now execute all eight firmware resource
  banks live at 60 Hz from an optional user-generated compute payload. Forward
  replay resumes the shared property allocation and draw-life latch instead of
  restarting it when the shell requests a later display frame.
- An inherited procedural boot visualizer, a separate native-shell boot
  experiment, and an off-screen native-particle draw probe. The probe executes
  Sony's compute stage and renders both large-particle banks through Sony's
  vertex/pixel pair with the two firmware GNF sprites. A generated animated
  sequence now reaches the managed shell through a state-gated additive cache
  layer.

## Honest fidelity status

The shell is not yet 1:1. Layout and many focus calculations are firmware-
derived, but the current focus output still needs paired capture validation,
especially line thickness, the translucent card shimmer, compact icon focus,
and display-mode colour conversion. The paper-white transform is opt-in through
`SHARPEMU_PS5_FOCUS_PAPER_WHITE`, not a validated default.

The Home-owned Hub shell frame and its vertical transition are live. The guest
Game Hub application is not. Local `GameEntry` records now reproduce AppBrowse's
`localConceptId` and `pshome:gamehub?titleId=` route from package-authored
concept/title ids using recovered Sony rules, but there is still no executing
topic channel, guest `focusReady` callback, or per-title app-module payload. The
default runtime therefore does not open the empty frame. It must not be filled
with guessed cards or console Settings.

The home background is owned by `ShellBackground`: recovered Plane2, title-art
transition, native particle layer, and basemat. ColdBoot's large groups execute
in-process in firmware order through one persistent Vulkan ONE/ONE/ADD pass;
cached frames remain fallback. Older procedural wave, particle, and ambient-drift
claims were retired. The steady Home/Settings Plane2 record and opaque original
ripple transition are proven. The eight-bank steady body is live for the exact
raw-state 1/2 `spread_expanded` route when its firmware compute payload is
present; snapshot properties remain fallback. Optional transition gradation and
the unrecovered pattern-turnover bodies are not live yet.

The procedural boot image is inherited authored geometry with selected decoded
firmware values injected into it. It is useful as a plumbing experiment, but it
is not Sony output and must not be shown as proof of fidelity. The
`exp/shell-boot` branch reaches graphics initialization and composite calls but
has not submitted a visible frame.

## Next implementation milestone

Continue the decoded renderer and single shell state contract:

1. extend the now-live eight-bank `spread_expanded` replay beyond raw states 1/2
   only when another serialized body and its call-site inheritance are proven;
2. extend the forward-only shared-allocation resume boundary across recovered
   pattern/group turnover; do not reset or loop the bounded resource capture;
3. feed selected-title and focus-rectangle changes into the native owner where
   the recovered renderer consumes them;
4. validate focus/card output and background states with paired 1920x1080
   console and host captures before claiming 1:1.

Running Sony's own shell remains the longer native milestone. It is valuable
ground truth, but it is not required before the decoded renderer path can be
validated in isolation.

## Evidence map

- Layout and motion: `ps5-rn-layout.md`, `ps5-home-structure.md`,
  `ps5-home-motion.md`.
- Focus: `ps5-focus-highlight.md`.
- Settings boundary and implementation: `ps5-settings-integration.md`.
- Background state and native renderer: `ps5-background.md`,
  `ps5-background-native.md`.
- Boot: `ps5-boot-animation.md`, `ps5-shell-boot-attempt.md`.
- Open questions: `ps5-unknowns.md`.
