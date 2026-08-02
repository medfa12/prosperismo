# PS5 Home UI — Layout, Focus, and Overlay Structure

Clean-room behavioral reference for recreating the PS5 home menu in Prosperismo.
Values here are numeric constants and structural facts extracted from the readable
Home UI JavaScript; no source is reproduced. Use it to build a faithful layout and
navigation model without copying Sony code.

## Provenance

- Bundle: `NPXS40002` (the Home UI React Native app), release train
  `rnps-home_v2_ppr_releases_03.00`, package `packages/home-ui`.
- Locators below are given as `[module N]` (the bundle's internal module id) plus the
  originating source path embedded in the bundle (e.g. `home-ui/src/components/System`).
  These are pointers only, for anyone re-deriving the values — not copied code.
- The shell is React Native. Layout is expressed in RN StyleSheet objects at a fixed
  1920x1080 design resolution (absolute pixel constants, not responsive units).
- Cross-checked against the native compositor notes in `ps5-shell-overlays.md`; where the
  JS and the native layer describe the same thing (the "basemat"), both are cited.

---

## 1. Top-level screen and space structure

- The home app renders a single top-level screen, `HomeScreen`
  (`home-ui/src/screens/HomeScreen`), which mounts a `HomeContainer`
  (`home-ui/src/components/HomeContainer`).
- There are exactly two **spaces** (the horizontal content universes):
  `spaceIds = ["game", "media"]`, default space `"game"` [module ~117 / ExperienceDataStore].
  "game" is the top space, "media" the bottom space.
- Switching spaces is an explicit directional/shoulder action, not a wrap:
  - From `game`, a **Down** key event switches to `media`.
  - From `media`, a **Down** key event switches to `game` (toggle).
  - L1/R1 shoulder keys also switch space. Each switch plays `psfx_change_space`.
    [module 808, `useSpaceFocus`]
- A separate "hub" concept overlays this: selecting an experience opens its **hub**
  (a per-title/per-app React Native mini-app rendered inside `HubViewer`). Vertical
  position is a two-state axis: `valueByVerticalPosition = { home: 0, hub: 1 }`
  [module 130]. The whole home shell animates between the "home" position (function row +
  tile row visible) and the "hub" position (shell slides up, hub content occupies the
  frame). `isHubFocused` is true exactly when the current vertical position is `"hub"`
  (`isHubFocused: "hub" === position`) [module ~511, HubViewer wiring].

---

## 2. Home layout (the two rows + system chrome)

The visible home is composed top-to-bottom. `HomeContainer`'s outer style uses
`flexDirection: "column-reverse"`, so declaration order is bottom-up; described here
top-down as seen on screen.

### 2.1 System chrome row (top)
- Height `SYSTEM_HEIGHT = 126` [module 96].
- Left cluster: the **space switcher** (Game / Media selector), in a wrapper of height
  126, `marginLeft: 84`. Focus layer name `"space-switcher"` [module 813].
- Right cluster: the **system icon row**, wrapper height 126, `marginRight: 84`. Focus
  layer name `"home-system"`. `systemIconsCount = 3` [module ~System/index]:
  - Search  -> deeplink `pssearch:main`, iconId `"search"`.
  - Profile (avatar) -> opens the profile modal.
  - Settings -> deeplink `pssettings:play?mode=settings`, iconId `"settings"`.
    [module ~SystemIcon/index]
  - Layout: `systemContainer` is `flexDirection: row, justifyContent: space-between`;
    on error state it collapses to `justifyContent: flex-end`.

### 2.2 Function row — the horizontal experience/icon row ("ExperienceSwitcher")
This is the row of Game/Media/app icons. Source `home-ui/src/components/ExperienceSwitcher`
and the shared constants in [module 25]:
- Row container: `width: 1920, height: 168`, `flexDirection: row`.
- Each experience icon (function-row tile):
  - `EXPERIENCE_SIZE = 106` (resting square size).
  - `SCALED_EXP_SIZE = 168` (focused/enlarged square size).
  - `EXPERIENCE_SCALE = 168 / 106` (~1.585) — the focus enlarge factor.
  - `SCALED_EXP_MARGIN_LEFT = 172` — left inset of the enlarged/selected icon.
  - `BORDER_RADIUS = 16`; the focused container radius is `168/106 * 16` (scaled with the
    tile) so the corner radius stays visually constant during enlarge.
  - `VERTICAL_HEIGHT_CHANGE = 40` — vertical shift applied when the row transitions.
- The selected experience is the enlarged one; unselected siblings sit at `EXPERIENCE_SIZE`.
- Maximum experiences shown in the function row: `MAX_TILES = 11` [module 47]. Content
  fetch is capped at `contentLimit: MAX_TILES`.
- **Minimized state** (when a hub is open, the function row shrinks to a corner badge):
  - `MINIMIZED_EXP_SIZE = 80`, `MINIMIZED_EXP_SCALE = 80/168`,
    `MINIMIZED_EXP_MARGIN_TOP = 48`, `MINIMIZED_EXP_MARGIN_LEFT = 48`.
- Focus layer names are per-experience: `"experience-switcher-<conceptId>"`. The row's
  focus container just applies `borderRadius`; enlarge is done via an Animated scale, not
  layout reflow.

### 2.3 Content / game tile row below ("Strand")
The scrolling row of game/content tiles under the function row. Source
`packages/rnps-js-modules-strand` and constants in [module 28]:
- Strand viewport: `STRAND_WIDTH = 1576`, `STRAND_HEIGHT = 864`, `CONTAINER_MARGIN = 172`.
  (An inner `strandContainer` of `1500 x 168` and `strandStyle.marginLeft = 172` is used for
  the compact/aligned variant [module 25].)

  > **Corrected.** The parenthesis above reads the two as variants of one viewport.
  > They are two different viewports that happen to share the 172 px margin.
  > `strandContainer 1500 x 168` (HOME m25) is the **experience switcher** clip: 168 is
  > `SCALED_EXP_SIZE`, so it is the focused-tile band, not a content area.
  > `STRAND_WIDTH 1576 x STRAND_HEIGHT 864` (HOME m28) is the **content strand**
  > viewport below it. Provenance: `docs/ps5-rn-layout.md` 2.3 and 3.1.

- Tile heights [module 98]:
  - `TILE_HEIGHT_L = 130`, `TILE_HEIGHT_S = 98` (list-style tiles).
  - Square tiles: `TILE_SQUARE_WIDTH = 370`, `TILE_SQUARE_HEIGHT_L = 340`,
    `TILE_SQUARE_HEIGHT_S = 314`, `TILE_SQUARE_HEIGHT_L_VL = 360`.

  > **Corrected: these are not content tiles.** This document files
  > `TILE_HEIGHT_L`, `TILE_HEIGHT_S` and the four `TILE_SQUARE_*` constants
  > (370 / 340 / 314 / 360) under the home content strand. They are HOME module 98,
  > and their only consumer is
  > `ui-shared-utilities-player-tile/PlayerTileSquare`. They size **player and friend
  > tiles**, which is a different strand element entirely. The content tile sizes are
  > the presentation-style matrix in `docs/ps5-rn-layout.md` 2.9, not these.
  >
  > The 370 figure survives elsewhere for an unrelated reason: 370 is also a content
  > tile width in the packing table immediately below, and `1576 = 370*4 + 32*3`. That
  > coincidence is probably how the mislabel happened. Do not read it as confirmation.
- Horizontal tile packing is a lookup keyed by strand width then tile width [module 28,
  `HORIZONTAL_SPACING`]. For strand width `1576`:

  | tile width | tiles per row (`howManyCanFit`) | inter-tile `margin` | `tileSizingWithMargin` |
  |-----------:|:-------------------------------:|:-------------------:|:----------------------:|
  | 236        | 6                               | 32                  | 268 |
  | 296        | 5                               | 24                  | 320 |
  | 360        | 4                               | 32                  | 392 |
  | 370        | 4                               | 32                  | 402 |
  | 504        | 3                               | 32                  | 536 |
  | 772        | 2                               | 32                  | 804 |

- Vertical packing [module 28, `VERTICAL_SPACING`]: for container height `864` and tile
  height `192`, `howManyCanFit = 4`, `margin = 5`, `tileSizingWithMargin = 197`.
- Grid/section constants [module ~20884]: `SEGMENT_HEADER_HEIGHT = 34`, plus
  `SEGMENT_HEADER_BOTTOM_MARGIN`, `GRID_ITEM_MARGIN`, `DEFAULT_MARGIN_UNDER_BOTTOM_ITEM`,
  `GridPadding` (numeric values present in that module for grid hubs).

### 2.4 Vertical relationship (row-to-row)
- The function row sits directly above the tile row; they share the `home` vertical
  position. Focus moving **down** off the system row / function row targets the tile area;
  focus moving **up** off a tile targets the space switcher above it (see Section 3).
- `_ = SYSTEM_HEIGHT + VERTICAL_HEIGHT_CHANGE` (126 + 40 = 166) is the composite offset used
  when the top chrome + function row translate together during the home<->hub transition
  [module 130].

---

## 3. Focus / navigation model

Focus is a **named-region graph**, not pure geometric spatial nav. Each focusable region is
a `FocusLayerPS` with a `name` and a `focusCustomSettings` object describing its edges.

### 3.1 focusCustomSettings — the edge contract
Per region, any of:
- `canMoveLeft`, `canMoveRight`, `canMoveUp`, `canMoveDown` (booleans) — when `false`, that
  edge is **clamped** (focus cannot leave that way). Tiles commonly set all four `false` so
  the enclosing list, not the tile, owns movement.
- `leftCandidate`, `rightCandidate`, `upCandidate`, `downCandidate` (strings) — the **named
  region** focus jumps to when leaving that edge. Examples observed:
  - System row: `leftCandidate: "space-switcher"`, `canMoveRight: false`,
    `downCandidate: "experience-switcher-<id>"`.
  - Function row region: `downCandidate: "experience-switcher-<id>"`.
  - Top nav (`"home-top-nav"`): `downCandidate: "experience-switcher-<id>"`.
  - Experience switcher item: `upCandidate: "experience-switcher-<id>"` /
    `upCandidate: "space-switcher-<id>"`.
  - Tile item (`"tile-item-focus-layer"`): `upCandidate: "space-switcher-<id>"` (unless in
    hub, where it is suppressed).
  - Space switcher: `canMoveLeft: false`, `rightCandidate: "home-system"`.
  - Hub tab list region: `leftCandidate: "hubSDKtabList"`, `canMoveRight: false`.
- `focusTarget` — an explicit initial-focus id for a region (menus).
- This is a **clamp + named-neighbor** model. There is no global focus wrap; horizontal
  edges are clamped and vertical/space movement is by explicit candidate or shoulder key.

### 3.2 focusInBehavior — focus memory
- Regions declare `focusInBehavior: { type: "lastFocusedItem" }` (seen on the tile focus
  layer). Re-entering a region restores the previously focused item rather than the first.

### 3.3 Directional key handlers
- Down from the system icon area invokes `focusSystem` and plays `psfx_focus_move`
  [module 812, `useSystemFocus`, onTriangleKeyDown].
- Space change (game<->media) is L1/R1 or Down toggles, playing `psfx_change_space`
  [module 808].
- SharpEmu's Sony presentation now keeps that shoulder contract at the host
  boundary: L1/R1 clamp across the exact `game`/`media` pair and play the
  recovered change-space sound. The desktop presentation retains its separate
  Library/Options shoulder shortcut; it is not allowed to leak into Sony Home.
- Entering an experience plays `psfx_enter`; the space switcher confirm plays `psfx_enter`
  then focuses the space default position [module 813].
- Back key handling is centralized (`useKeyHandlers().onBackKeyDown`); regions delegate
  back to it. Home's own back (from a hub) issues `pshomeui:navigateToHome` or, at the root,
  `ExitHandler.exitApp()` [module ~HubContainer].

### 3.4 Focused-tile enlarge
- Function-row focus enlarge is the `EXPERIENCE_SCALE` (168/106) Animated scale described in
  2.2, applied via `SPRING_OPTIONS_FAST`/`FASTER` springs, not a layout change.
- Tiles use an Animated highlight; edges clamped so the list scrolls the focused tile into
  view rather than the tile itself moving focus.

### 3.5 focusReady / latency
- `focusReady` is a readiness gate for hubs. A `HubState` object holds a `ready` flag,
  initially `false`; the hub calls its `focusReady()` callback (surfaced as `onFocusReady`)
  when its content has mounted and can accept focus, setting `ready = true` [module 512].
- The host waits on this before routing focus into a freshly opened hub — i.e. `focusReady`
  is the "hub content is live, safe to hand it focus now" signal, preventing focus from
  landing on a not-yet-rendered mini-app. `HubState.unload()` resets `ready` to false.
- The related `isHubFocused` (Section 1) tells child components whether the hub currently
  owns focus vs. the home shell.

### 3.6 Focus sound hooks (event ids)
Played through `SystemSoundPS.playByID(...)`:
- `psfx_focus_move` — focus moved between items / into the system row.
- `psfx_enter` — confirm / enter an experience or space.
- `psfx_cancel` — back / cancel.
- `psfx_change_space` — game<->media space switch.
- `psfx_open_home` — played once at the end of the home startup animation.
- Some paths also use the enum form `SystemSoundPS.SoundTypes.enter`.

---

## 4. Overlay / dialog / control-center model

There are **two distinct dimming mechanisms**; the earlier `.NET`-side note conflated them.
Both are real and operate on different layers.

### 4.1 Foreground modals (Control Center / dialogs) — RN Modal
- Overlays such as Control Center (Function Control) and profile/dialogs are presented as a
  React Native `Modal` with `visible` toggled and `animationType: modalAnimationType`
  [module ~HomeControls, `home-ui/src/components/HomeControls`].
- The modal wraps its own `FocusLayerPS` (so it captures focus) and a `View` with
  `lightMat: "auto"`. A `FocusTrap` region (`name: "focus-trap"`, all four `canMove*` false,
  `focusable: true`) is used to prevent focus escaping the overlay.
- Modal container animation constants [module ~677]:
  - `modalAnimationType.show = { duration: 250, delay: 50, type: "easeOutBlastPS" }`
  - `modalAnimationType.hide = { duration: 300, delay: 0, type: "linear" }`
  - The modal **contents** cross-fade separately via timing anims: show `duration 150`
    linear, hide `duration 100` linear.
- z-order: the RN Modal renders above the home shell; within it, FocusLayer + FocusTrap own
  input until dismissed (`onRequestClose`).

### 4.2 Background darkening — the "basemat" (native BG layer, driven from JS)
- The full-frame dim behind system modals is **not** an RN scrim drawn by the Home UI. The
  Home UI only selects a **basemat shape**; the actual dim surface is rendered by the native
  background compositor (`SystemBGMediator` / `BGLayer`; see `ps5-shell-overlays.md`).
- In the JS, `basemat` is a named shape string carried on the background descriptor:
  - `DEFAULT_BASEMAT = "EllipseNarrow"` [modules 9197, 61541]. This is the resting home
    background treatment.
  - When a scaled/secondary experience is active the code selects `"Flat"`
    (`basemat = a?.basemat || (index > 0 ? "Flat" : "EllipseNarrow")`) [module ~513]. `"Flat"`
    is the full-frame flat dim used behind system modals.
  - These map to the native `BackgroundBasematType` enum (`EllipseNarrow`, `Flat(1)`, ...).
- The dim **color and timing** live natively, not in this bundle: per `ps5-shell-overlays.md`
  the basemat default color is linear (0.00784, 0.01568, 0.03137) ≈ 8-bit (2,4,8) = `#020408`,
  animated over `BasematAnimationDuration = 1000f` ms. So: modal **content** animates at
  250/300 ms (RN, this bundle) while the **background** darkens to `#020408` over 1000 ms
  (native basemat) — a two-layer effect, not a single 1000 ms flat swap.

### 4.3 Per-tile darkening mat (independent, same color)
- Separately, the function-row experiences have a JS-level darkening mat: `useMat` builds an
  interpolated `backgroundColor` per tile from `rgba(2,4,8,0)` -> `rgba(2,4,8,0.4)` across
  input `[0, .05, .2, .4]` [module ~586]. Off-selection tiles (indices 8/9/10 past the
  selection) get alpha `.05 / .2 / .4` — a progressive fade of overflow experiences.
- This independently confirms Sony's `#020408` (rgb 2,4,8) as the canonical dark tint used
  both for the native basemat and for in-app tile darkening.

---

## 5. Startup / transition motion (structural timings)

Home entrance animation [module 843, reducer `start`]:
- Space switcher springs in with `SPRING_OPTIONS_SLOWER`.
- Experience icons stagger in at `Animated.stagger(60, ...)` (60 ms between icons),
  `SPRING_OPTIONS_SLOWER`.
- Sequenced reveal: `delay 1050` -> system opacity/translate out (`SPRING_OPTIONS_SLOW`),
  with the title fading after an inner `delay 333`.
- `delay 1450` -> hub position resolves; `onAnimationEnd` fires.
- `SystemSoundPS.playByID("psfx_open_home")` plays at the start of that sequence.

Spring presets [module 49]:
- `SPRING_OPTIONS_SLOW = { stiffness 130, damping 25, mass 1, overshootClamping true }`
- `SPRING_OPTIONS_SLOWER = { stiffness 100, damping 20, mass 1, overshootClamping true }`
- `SPRING_OPTIONS_FAST = { stiffness 200, damping 100, mass 0.2 }`
- `SPRING_OPTIONS_FASTER = { stiffness 600, damping 100, mass 0.2 }`
All use the native driver.

home<->hub transition [module ~585 / 41xx]: each experience tile springs its vertical value
to `valueByVerticalPosition[position]` with `SPRING_OPTIONS_FAST`, and its mat value with
`SPRING_OPTIONS_FASTER`; the selected tile goes to the `hub` value while siblings drop to the
`home` value. The system chrome fades opacity 1->0 and translates `translateY 0 -> -20` on the
same axis [module 843, styles].

Other structural constants: `HEADER_HEIGHT = 80` [module ~HubHeader].

---

## 6. Region / focus-layer name catalog (for wiring)

Named `FocusLayerPS` regions observed (useful as a state-machine node list):
- `home-system` — system icon cluster (search / profile / settings).
- `space-switcher` and `space-switcher-<spaceId>` — Game/Media selector.
- `home-top-nav` — the top nav region.
- `experience-switcher-<conceptId>` — each function-row experience icon.
- `home-experience-switchers` — the function-row container (all four `canMove*` false).
- `tile-item-focus-layer` — a content/game tile (remembers last focus).
- `focus-trap` — modal focus containment.
- `hubSDKtabList` / hub utility regions — tabs inside an opened hub.
- `space-switcher` right-links to `home-system`; system left-links back to `space-switcher`,
  forming the top-row loop; both down-link into the function row.

---

## 7. Honest gaps / not determined from this bundle

- **Exact on-screen X/Y of the two rows** is composed at runtime from flex layout +
  Animated offsets; only the piece constants (heights 126/168, margins 84/172, the
  166 composite) are literal. A pixel-perfect vertical stack was not fully resolved to
  absolute top coordinates.
- **Basemat color and 1000 ms duration are native**, not in `NPXS40002`. Confirmed only via
  the separate native-side analysis (`ps5-shell-overlays.md`) and the corroborating in-app
  `rgba(2,4,8)` tile mat. The JS here selects only the *shape* (`EllipseNarrow`/`Flat`).
- **Grid-hub packing** (`GRID_ITEM_MARGIN`, `GridPadding`, etc. numeric values) exist in
  module ~20884 but belong to in-hub grids rather than the root strand; not fully tabulated
  here.
- **Space-switcher visual geometry** (icon sizes/labels for the Game/Media pills) beyond the
  126-high wrapper and 84 margin was not extracted.
- Behavior for **>2 spaces** does not exist — the model is hard-limited to `["game","media"]`.
- The `easeOutBlastPS` easing curve is a native easing referenced by name only; its formula
  is not in this bundle.
