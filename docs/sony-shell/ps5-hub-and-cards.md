# PS5 Game Hub, Activity Cards, and Grid — Values Reference

Clean-room behavioural reference for recreating the per-title **hub**, the **activity
(action) cards**, and the **grid/segment** layout in Prosperismo. Everything below is a
numeric constant, an enum value, or a structural rule extracted from the readable shell
JavaScript. No source is reproduced; identifiers and module ids are pointers only, so
that anyone can re-derive a value independently.

Companion documents: `ps5-home-structure.md` (home layout/focus), `ps5-home-motion.md`,
`ps5-shell-overlays.md` (native compositor / basemat), `ps5-home-theme.md`.

## SharpEmu integration status

The canonical integration branch now implements the Home-owned shell side of
this contract: a sibling 1920x1080 hub frame, the y=128 module boundary, the
-166 px Home lift, the selected-tile handoff into the 80x80 header badge at
(48,48), the independent sibling fade, and the Down / Up-or-Circle focus route.
The route is gated by the retained module state's one-shot `focusReady`, as in
HOME; default runtime swallows Down while no genuine guest is ready.

Embedded-mode ownership is now explicit in the implementation: HOME's selected
tile and `TitleContainer` animate into the 80 px badge/header pose and remain
visible. NPXS40033's `renderHeader` is conditioned on standalone mode, so the
embedded app-module frame does not draw a second icon/title. Switcher opacity is
also not tied to vertical progress; it is a separate FAST driver changed only
by the guest's `toggleHeader` callback while the hub is active.

This is deliberately not presented as a running Game Hub app. SharpEmu now
reproduces AppBrowse's per-title identity and normal-game `hubUri` from genuine
package metadata and the recovered Sony encoders, but it does not yet have an
executing topic provider, `focusReady` callback, or translated NPXS40033
app-module payload. Therefore the frame contains no guessed
activities, trophies, tabs, friends, news, metadata, or action buttons. The next
faithful step is the app-module/channel adapter, not placeholder card design.
`SHARPEMU_PS5_HUB_PREVIEW=1` only bypasses readiness for deterministic visual
inspection and is not evidence that the guest module runs.

The host contract descriptor/state scaffolding now preserves the exact split
used by the two bundles. It is not yet an executing NPXS40033 host. `hubUri` is
parsed into `scheme:path`; the native instance is keyed by
that path, named `app-module-<scheme:path>`, opened at
`<scheme:path>?isFromHubSDK=1`, and connected on `channel-<scheme:path>`. The
original query travels separately. A query-only change retains the native
module but remounts the guest's HubAppContext subtree using its serialized query
key. Per-experience readiness/background/music/offset state remains keyed by the
AppBrowse `localConceptId`/`experience.id` even when several titles share that
native module slot. Package scans reproduce Sony's `cid:scp:` key from the
package-authored numeric concept id (or its `cid:local:` title-key fallback);
the raw title id, concept id and filesystem path are not substituted. The five
no-response callback names and 4 / 260 ms / 60000 ms / 300 ms pool timings are
pinned in code and tests.

## Provenance

Three release trains are involved. Locators are written `[BUNDLE m<id>]`.

| Bundle | App | Release train | Packages that matter here |
|---|---|---|---|
| `NPXS40002` | Home UI | `rnps-home_v2_ppr_releases_03.00` | `packages/home-ui`, `packages/rnps-js-modules-strand`, `@rnps-ppr/js-modules-hub-sdk` |
| `NPXS40033` | Game Hub | `s-game-hub_v2_ppr_releases_03.00` | `packages/game-hub`, `@rnps-ppr/js-modules-hub-sdk`, `@rnps-ppr/action-card-sdk` |
| `NPXS40003` | Control Center | `rol-center_v2_ppr_releases_03.00` | `apps/control-center`, `apps/action-cards-host/packages-sdk/consumer-sdk` |

All layout is React Native `StyleSheet` at a fixed **1920x1080** design resolution;
every number below is a design pixel, not a responsive unit.

> Note for re-derivation: these bundles contain embedded NUL bytes, so ripgrep-style
> tools classify them as binary and silently report zero matches. Read them as text and
> match with a regex engine instead (or strip NULs into a scratch copy first).

---

## 1. The hub

### 1.1 What a hub actually is

A hub is **not a screen inside home**. Home hosts it as a *separate React Native
application* mounted in a native app-module view:

- `HubProvider` [NPXS40002 m351, `js-modules-hub-sdk/src/components/HubProvider`] renders
  a native component (`RCTVshNuiAppModule`) sized **1920x1080** [NPXS40002 m835], named
  `app-module-<appModulePath>`, whose URL is derived from the experience's `hubUri`.
- Props cross the boundary through a topic-keyed provider/consumer channel. The hub app
  reads them via `HubAppContext` [NPXS40033 m92].
- Five callbacks are declared *fire-and-forget* (`noResponseCallbacks`), i.e. the hub
  calls back into home without awaiting a reply: `focusReady`, `onTemplateChange`,
  `setBackgroundImage`, `setBackgroundMusic`, `toggleHeader` [NPXS40002 m351].
- The channel key is the topic; changing topic remounts the whole hub.

**Implication for an emulator:** the hub is a process/module boundary, not a component
tree. A faithful reimplementation needs a host surface plus that five-callback contract;
the visual constants below describe what the guest side draws inside it.

`HubAppContext` defaults [NPXS40033 m92]:

| Field | Default |
|---|---|
| `isStandaloneMode` | `false` |
| `queryParams` | `{}` |
| `refreshCounter` | `0` |
| `resetCounter` | `0` |
| `connected` | `"unknown"` |
| `signedIn` | `"unknown"` |
| `marginTop` | `0` |
| `isHubFocused` | `false` |

`isStandaloneMode` is the key branch: `true` when the hub app was launched directly
(not from home). It turns on the hub's own header and shifts content down.

### 1.2 Hub pooling (how many hubs are alive)

Home keeps a **pool** of hub app-modules so re-entry is instant
[NPXS40002 m503 `useHubViewer`, m505 `useHubPooling`, m512 `HubState`].

| Param | Value | Meaning |
|---|---|---|
| `hubPoolSize` | `4` | max simultaneously mounted hub app-modules |
| `hubShowDelay` | `260` ms | delay before a newly selected hub is restored/shown |
| `hubReclaimDelay` | `60000` ms | idle time after which the pool is trimmed to the current hub |
| `hubDebounceDelay` | `300` ms | debounce on rapid tile changes (feature flag `hubDebounce`) |
| `hubBlockList` | list | hub URIs never pooled (compilation, disc player, game store, library, media gallery, remote play, unsupported) |

The pool is also flushed to just the current hub when the app goes `background` or
`inactive`. Per-hub state is a `HubState` object [NPXS40002 m512]:
`{ backgroundImage, backgroundMusic, hubOffsets: [0,0], ready: false }`, with
`focusReady()` flipping `ready` to `true` and `unload()` resetting all four.

### 1.3 The vertical axis and the focus-ready gate

[NPXS40002 m130 `useVerticalAnimation`]

- `valueByVerticalPosition = { home: 0, hub: 1 }`
- `valueByVisibility = { visible: 1, hidden: 0 }`
- Recoil atom `verticalPosition`, default `"home"`.

**Gate:** pressing Down on a tile (`onDownKeyDown`) or pressing X (`onPress`) only
descends into the hub if `HubState.isReady()` for that experience is `true`
[NPXS40002 m503]. `ready` is set by the hub app calling `focusReady` exactly once, which
`HubContainer` does the first time its content reports it is focusable
[NPXS40033 m355]. Until then the input is swallowed — this is the observable "hub won't
open yet" behaviour.

`HubContainer` uses the same flag internally to gate rendering: background, header and
utility rail are not rendered at all until `focusReady` has fired.

### 1.4 Hub transition table

All springs use the shared spring presets [NPXS40002 m49]:

| Preset | stiffness | damping | mass | overshootClamping |
|---|---|---|---|---|
| `SPRING_OPTIONS_SLOW` | 130 | 25 | 1 | true |
| `SPRING_OPTIONS_SLOWER` | 100 | 20 | 1 | true |
| `SPRING_OPTIONS_FAST` | 200 | 100 | 0.2 | false |
| `SPRING_OPTIONS_FASTER` | 600 | 100 | 0.2 | false |

(RN `Animated.spring` with `stiffness/damping/mass` is a critically-damped-ish analytic
spring; `SPRING_OPTIONS_FAST` at m=0.2, k=200, c=100 is heavily overdamped, so it reads
as a fast decelerating slide rather than a bounce. Prosperismo has no spring
integrator yet — see §6.)

| Element | Property | home (0) -> hub (1) | Driver | Locator |
|---|---|---|---|---|
| Home shell | `translateY` | `0` -> `-166` | spring `FAST` | m130; `-(SYSTEM_HEIGHT 126 + VERTICAL_HEIGHT_CHANGE 40)` |
| Hub layer *n* | `translateY` | `hubOffsets[0]` -> `hubOffsets[1]` (always `0`) | spring `FAST` | m130, m512 |
| Function row (all tiles) | `opacity` | `1` -> `0` for **non-selected** tiles; selected tile stays `1` | spring `FASTER` | m571 `useHeaderTransition` |
| Selected tile | `translateX` | `0` -> `-106` | spring `FAST` | m571 |
| Selected tile | `translateY` | `0` -> `+27.762` | spring `FAST` | m571 |
| Selected tile | `scale` | `1` -> `0.47619` (`MINIMIZED_EXP_SCALE = 80/168`) | spring `FAST` | m571 |
| Experience switcher wrapper | `opacity` | `1` -> `0`/`1` per `experienceSwitcherVisibility` | spring `FAST` | m130 |
| Download bar *n* | `opacity` | `1` at home for non-selected tiles, else `0` | `setValue` (instant) | m570 `useBarAnimation` |

The selected-tile transform is derived, not literal [NPXS40002 m25 + m96 + m571]:

```
EXPERIENCE_SIZE          = 106
SCALED_EXP_SIZE          = 168
EXPERIENCE_SCALE         = 168/106 = 1.5849057
SCALED_EXP_MARGIN_LEFT   = 172
MINIMIZED_EXP_SIZE       = 80
MINIMIZED_EXP_MARGIN_TOP = 48
MINIMIZED_EXP_MARGIN_LEFT= 48
MINIMIZED_EXP_SCALE      = 80/168 = 0.4761905
VERTICAL_HEIGHT_CHANGE   = 40
BORDER_RADIUS            = 16
SYSTEM_HEIGHT            = 126

dx = -(172 + 168/2 - (48 + 80/2))          = -168
dy = -(126 + 168/2 - (48 + 80/2))          = -122
translateX(hub) = dx / EXPERIENCE_SCALE                     = -106
translateY(hub) = (dy + (126 + 40)) / EXPERIENCE_SCALE      = +27.7619
```

The transform is expressed in the tile's own pre-scale coordinate space (hence the
`1/EXPERIENCE_SCALE` division). Net effect: the focused tile flies from the function row
to a **80x80 badge at (48, 48)** — which is exactly where `HubHeader` draws its icon
(§1.7), so the badge and the hub header icon are the same visual object handed off
across the app-module boundary.

**Entering/leaving home->hub is not what mounts the hub.** A separate one-shot
"hub appears" animation runs when a hub is *shown* from the pool
[NPXS40002 m507 `useHubAnimation`]:

| Step | Value |
|---|---|
| Pre-roll (stagger) | `16.67` ms (one 60 Hz frame) |
| Pre-roll sets | `opacity = 0`, `progress = 0.95` (i.e. `translateY = 42.5`) |
| Then, in parallel | `opacity 0->1` and `progress 0.95->1` |
| Duration | `300` ms each |
| Easing | `cubic-bezier(0.25, 0.1, 0.25, 0.8)` |
| translateY mapping | `progress 0..1` -> `850..0` px |
| `hide()` | stops the animation, sets `progress = 0` (translateY 850, offscreen) and `opacity = 1` |

### 1.5 Hub content offsets (`onTemplateChange`)

`HubContainer` [NPXS40033 m355] picks a pair of numbers from the active content template
and the presence of a horizontal nav or a utility rail, then (a) reports
`[n0 - n1, 0]` back to home as `hubOffsets`, and (b) sets its own content `translateY`
to `n1`.

| Template | horizontal nav **or** utility present | `hubOffsets[0]` (home rest position) | content `translateY` |
|---|---|---|---|
| `showcase`, `grid` | yes | `8` | `0` |
| `showcase`, `grid` | no | `48` | `88` |
| `poster`, `grid-with-section-header` | yes | `-50` | `0` |
| `poster`, `grid-with-section-header` | no | `-10` | `88` |
| `lightbox` | yes | `-38` | `0` |
| `lightbox` | no | `2` | `88` |
| `hubError`, `grid-loading` | yes | `-130` | `0` |
| `hubError`, `grid-loading` | no | `-98` | `108` |

`hubOffsets[1]` is `0` in every case, so at the hub position the hub layer always sits
flush; the offset only shifts where the hub layer rests while home is on screen.

### 1.6 `HubContainer` structure

[NPXS40033 m355, styles m751]

- Outer focus layer `"hubSDKContainerLayer"`, `height = 1080 - marginTop`,
  `focusCustomSettings: { canMoveLeft: false, canMoveRight: false }`.
- Content view (`testID: hubsdk-content-view`): `flex: 1`,
  `marginTop = 128` when `isStandaloneMode`, else `0`; plus the animated `translateY`
  from §1.5.
- Inner content focus layer `"hubSDKContentLayer"`: `flex: 1`, with an extra
  `marginTop: 128` when there is **no** nav but there **is** a utility rail.
- Render slots, in z-order: `renderBackground` -> `renderHeader` (standalone only) ->
  animated content view containing `renderUtility`, then either the nav (with the content
  injected as its panel) or the bare content.
- Back key: standalone -> exit the app; embedded -> `pshomeui:navigateToHome`.
  Overridable via `disableBackButtonBehavior`.
- Triangle key: focus the utility rail, playing `psfx_focus_move`.
- The default tab is the `defaultTab` prop of the nav, falling back to the `name` of the
  nav's first child.

### 1.7 `HubHeader`

[NPXS40033 m766, styles m359, constants m767]

| Property | Value |
|---|---|
| Container | `position: absolute`, `marginTop: 48`, `marginLeft: 48`, `marginRight: 172`, row, centre-aligned |
| Icon | `80 x 80`, `borderRadius: 12`, `marginRight: 44` |
| Icon source | 4K-resized to `2 x SCALED_EXP_SIZE` = `336 x 336`, `bc7u`, `keepFitSize`, `mitchell` filter, GPU DXT compress |
| Icon fallback | `cxml://CommonAssets/iconid_game` |
| Title | single line, `ellipsizeMode: "marquee"` |
| Title max width | `1132 - 54*(has entitlement icon) - 54*(has storage icon) - 76*(platform tag) - 260*(package tag)` |
| Tag separator | `2` px wide, `top: 6`, `bottom: 6`, `left: 12`, `rgba(255,255,255,0.25)` |
| Tag text | `marginLeft: 26`, `rgba(255,255,255,0.7)` |
| Metadata icons | `42 x 42`, container `marginLeft: 12`, row |

Platform/package tag suppression uses `STRING_ID` [NPXS40033 m767]:
`PPR: "msgid_pfn"`, `PS4: "msgid_ps4"`, `PS3: "msgid_ps3"`,
`FULL: "msgid_full_version"`, `BETA: "msgid_beta"`, `TRIAL: "msgid_trial"`,
`DEMO: "msgid_demo"`, `COMPILATION: "msgid_compilation"`.
The platform tag is hidden when the platform is `PPR` (i.e. native PS5); the package tag
is hidden for `FULL` and `DEMO`.

### 1.8 `HubNav` (the tab model)

[NPXS40033 m752, styles m753]

- `navType` is `"vertical"` (default) or `"horizontal"`.
- Tabs come from the nav's children; each contributes `{ tabId: name, type: "tab",
  collapsible: true }`.
- Tab list geometry:

| | vertical | horizontal |
|---|---|---|
| Nav container | `width: 2152`, `marginTop: 86`, `marginLeft: 40` | `marginLeft: 148`, `marginRight: 172` |
| Tab list width | `540` | `1576 - (48 + hubUtilityWidth)` |
| Panel wrapper | `flex: 1`, `marginLeft: -12` | `flex: 1`, `paddingTop: 40`, `marginLeft: -148`, `marginRight: -172` |
| Panel focus | `canMoveLeft: true`, `canMoveRight: false` | `canMoveLeft: false`, `canMoveRight: false` |

- The **1576** here is the same constant Prosperismo already calls `STRAND_WIDTH`.
- Tab list `opacity`: starts at `1` in standalone mode, `0` when embedded. On
  `isHubFocused` the nav calls `setFocusOnPanel()` and then sets opacity to `1` after a
  **300 ms** timeout; on losing focus it drops to `0` immediately (no animation — a
  discrete state change).
- `TabViewPS` options: `centerGapWidth: "narrow"`, `focusOutBehavior: "normal"`,
  `enableShorcutKeysToSwitchTab: true` (L1/R1 switch tabs),
  `tabListFocusCustomSettings: { canMoveLeft: false }`.
- Bumping `resetCounter` on the context returns the nav to its default tab.
- If the tab set changes and the previously active tab disappears, the nav falls back to
  the first tab and fires `onChange({ oldTabId, newTabId })`.

### 1.9 `HubBackground`

[NPXS40033 m764, styles m765]

`position: absolute`, `top: 0`, `left: 0`, `width: 1920`, `height: 1080`,
`zIndex: -1`, plus `marginTop: -marginTop` so the background always covers the full
frame regardless of the container's top inset.

The background *image* is not drawn by the hub — the hub calls `setBackgroundImage` back
into home, which forwards to the native `SceneControl.setBackground` [NPXS40002 m504].
Default hub wallpaper: `bg_hub_default.dds`, transition `{ type: "Ripple", degree:
"CrossFade" }` [NPXS40033 m755]. `basemat` is `"EllipseNarrow"` for scene 0 and `"Flat"`
for any later scene. A special-cased unsupported-title background exists:
`/system_ex/vsh_asset/bg_NPXS40144.dds` [NPXS40002 m503].

### 1.10 `HubUtility` / `UtilityContainer`

[NPXS40033 m769, m771, m772, styles m770/m360]

| Property | Value |
|---|---|
| Utility container | `maxWidth: 416`, `marginTop: 8`, row |
| Icon slot | `width: 56`, `marginLeft: 48`, centred |
| Icon | `56 x 56` (inner `IconButton` icon `48 x 48`) |
| Label | `position: absolute`, `top: 56`, `width: 336`, `marginTop: 16`, font `SizeXSmall`, marquee |
| Button background | `visibleOnFocus` |
| Label reveal | spring to `opacity 1` on focus, `0` on blur — `stiffness 200, damping 100, mass 0.2` (= `SPRING_OPTIONS_FAST`) |
| Rail reveal | `Animated.delay(300)` then the same spring to `opacity 1`; instant `0` on hub blur |
| Focus | `leftCandidate: "hubSDKtabList"`, `canMoveRight: false`, triangle key consumed |

### 1.11 `SceneList` (hub page content)

[NPXS40033 m755, m358, styles m763]

The hub's tab panel is a vertical list of *scenes* (sections), each independently fetched
and rendered.

| Property | Value |
|---|---|
| Scene stride | `getSceneLayout(scene) + 64` (i.e. **64 px** gap between scenes; zero-height scenes contribute nothing) |
| List container | `flex: 1`, `marginTop: -40` |
| Edge fade padding | `{ top: 40 }`, `edgeFading: true` |
| Container enter | `translateY` mapped `-1,0,1 -> 1080,-20,0`; `opacity` mapped `-1,0,1 -> 1,0,1` |
| Standard easing | `cubic-bezier(0.25, 0.1, 0.25, 0.8)` |
| Body fade in (hub focused) | `opacity -> 1`, `500` ms |
| Blur, when scrolled past scene 0 | `container -> 0` over `150` ms; jump-scroll to index 0; `body opacity` to `0` instantly; `container -> 1` over `500` ms |
| Blur, at scene 0 | `body opacity` set to `0` instantly |
| Scene scroll-out blink | outgoing scene `opacity -> 0` over `35` ms with `cubic-bezier(0.2, 0.7, 0.6, 0.8)`, then back to `1` after a `5` ms delay |
| Scene focus layer | `canMoveLeft: false`, `canMoveRight: false` |

Only scene 0 is fetched initially; scenes 1..n are fetched once scene 0 has loaded.
`sceneRenderLimit` caps how far ahead scenes are materialised before the hub is focused.
`focusReady` (§1.3) fires from the first scene's load callback.

### 1.12 Game Hub screens

The game hub app itself [NPXS40033, `packages/game-hub/src/screens`] is organised as
cover-plus-modules screens, useful as a checklist of what a hub page contains:

- `PrePurchaseScreen` — cover, info panel (friends row, content rating, description +
  price, CTA), publisher media (with dynamic background), about section.
- `PostPurchaseScreen` — cover (poster data, game CTA, game-version dialog), highlight
  section (product tile / trophy tile on cover), game plan, UGC strand, news, add-ons,
  merchandising, PVU.
- `MediaDetails`, `AboutScreen`, `AddOnPdpScreen`, `AddOnsViewAllScreen`,
  `LoadingScreen`, `ErrorScreen` (dialog / panel / BC3 panel variants).
- Shared components: `Hud`, `ContentDetail`, `ProductTile`, `PublisherMediaStrand`,
  `SectionTitle`, `Divider`, `Tags`, `ReleaseDate`, `CompatibilityNotices`,
  `ApplicationVersionTag`, `CoverPublisherLogo`.

---

## 2. Activity (action) cards

Package: `@rnps-ppr/action-card-sdk` (rendered inside the hub) and
`apps/action-cards-host/packages-sdk/consumer-sdk` (rendered inside Control Center).

### 2.1 `AC_MODE` and `MULTITASK_AC_MODE`

[NPXS40033 m86 / NPXS40003 m19 — identical]

`AC_MODE` is `MULTITASK_AC_MODE` merged with four base modes:

| Key | Value |
|---|---|
| `GLANCE` | `"glance"` |
| `FOCUSED` | `"focused"` |
| `SELECTED_LOADING` | `"selected-loading"` |
| `SELECTED` | `"selected"` |

`MULTITASK_AC_MODE`:

| Key | Value |
|---|---|
| `PINP` | `"pinp"` |
| `PINP_POSITIONING_TOP_LEFT` | `"pinp-positioning-top-left"` |
| `PINP_POSITIONING_TOP_CENTER` | `"pinp-positioning-top-center"` |
| `PINP_POSITIONING_TOP_RIGHT` | `"pinp-positioning-top-right"` |
| `PINP_POSITIONING_CENTER_LEFT` | `"pinp-positioning-center-left"` |
| `PINP_POSITIONING_CENTER_RIGHT` | `"pinp-positioning-center-right"` |
| `PINP_POSITIONING_BOTTOM_LEFT` | `"pinp-positioning-bottom-left"` |
| `PINP_POSITIONING_BOTTOM_CENTER` | `"pinp-positioning-bottom-center"` |
| `PINP_POSITIONING_BOTTOM_RIGHT` | `"pinp-positioning-bottom-right"` |
| `SPLIT_SCREEN` | `"split-screen"` |
| `SPLIT_SCREEN_POSITIONING_LEFT` | `"split-screen-positioning-left"` |
| `SPLIT_SCREEN_POSITIONING_RIGHT` | `"split-screen-positioning-right"` |
| `BACKGROUND` | `"background"` |

Defaults and predicates:

- `DEFAULT_PINP_POSITIONING_MODE = PINP_POSITIONING_CENTER_LEFT`
- `DEFAULT_SPLIT_SCREEN_POSITIONING_MODE = SPLIT_SCREEN_POSITIONING_LEFT`
- `isPinPPositioningMode(m)` = `m` contains `"pinp-positioning"`
- `isSplitScreenPositioningMode(m)` = `m` contains `"split-screen-positioning"`
- `isMultitaskPositioningMode(m)` = either of the above
- `normalizeContainerMode(m, pinpDefault, ssDefault)`: `SELECTED -> SELECTED_LOADING`,
  `PINP -> pinpDefault`, `SPLIT_SCREEN -> ssDefault`, otherwise unchanged.
- `deriveAppModuleMode(m)` (what the card's own app module is told):
  `SELECTED_LOADING -> SELECTED`, any pinp-positioning -> `PINP`, any
  split-screen-positioning -> `SPLIT_SCREEN`, else unchanged [NPXS40003 m1083].

### 2.2 `AC_DIMENSIONS`

[NPXS40033 m55 / NPXS40003 m43]

| Mode | width | height |
|---|---|---|
| `GLANCE` | `360` | `400` |
| `FOCUSED` | `432` | `520` |
| `SELECTED` | `464` | `810` |
| `PINP` | `464` | `261` |
| `SPLIT_SCREEN` | `464` | `810` |

Extended size presets, only in the Control Center host [NPXS40003 m203, `AC_SIZES`]:

| Preset (SELECTED) | width x height |
|---|---|
| `DEFAULT` | `464 x 810` |
| `SUPP_1` | `520 x 810` |
| `SUPP_2` | `652 x 810` |
| `SUPP_3` | `784 x 810` |
| `SUPP_4` | `928 x 810` |

| Preset (PINP) | width x height |
|---|---|
| `DEFAULT` / `16_9_DEFAULT` | `464 x 261` |
| `16_9_SUPP_1` | `360 x 203` |
| `16_9_SUPP_2` | `652 x 367` |
| `4_3_DEFAULT` | `464 x 348` |
| `4_3_SUPP_1` | `360 x 270` |
| `4_3_SUPP_2` | `652 x 489` |
| `1_1_DEFAULT` | `300 x 300` |
| `1_1_SUPP_1` | `212 x 212` |
| `1_1_SUPP_2` | `388 x 388` |
| `1_1_SUPP_3` | `476 x 476` |

`SPLIT_SCREEN` has no presets (`464 x 810`).

Free-form selected sizes are clamped, with a console warning on each clamp:

| Axis | min | max |
|---|---|---|
| height | `600` (= `FOCUSED.height 520 + 80`) | `810` (= `SELECTED.height`) |
| width | `432` (= `FOCUSED.width`) | `1752` |

A **foreign** card (one owned by another title) is forced to `SUPP_1` (`520 x 810`) when
selected [NPXS40003 m880].

### 2.3 `AC_ANCHORS`, `AC_OFFSETS`, `NO_OFFSET`

[NPXS40033 m55]

Anchor axis values: vertical `"top" | "center" | "bottom"`, horizontal
`"left" | "center" | "right"`. `AC_ANCHORS` is the 3x3 product:
`ANCHOR_TOP_LEFT`, `ANCHOR_TOP_CENTER`, `ANCHOR_TOP_RIGHT`, `ANCHOR_CENTER_LEFT`,
`ANCHOR_CENTER_CENTER`, `ANCHOR_CENTER_RIGHT`, `ANCHOR_BOTTOM_LEFT`,
`ANCHOR_BOTTOM_CENTER`, `ANCHOR_BOTTOM_RIGHT`, plus two aliases:

- `ANCHOR_CENTER = ANCHOR_CENTER_CENTER`
- **`DEFAULT_ANCHOR = ANCHOR_BOTTOM_LEFT`**

`NO_OFFSET = { x: 0, y: 0 }`. `AC_OFFSETS` maps `GLANCE`, `FOCUSED` and `SELECTED` all to
`NO_OFFSET` — i.e. in stock configuration a card grows purely from its anchor with no
nudge. `PINP_BORDER_INOUT = 2`.

### 2.4 The over-growth model (how a card grows without moving its slot)

This is the single most important structural rule [NPXS40003 m880]. A card **always
occupies a `GLANCE`-sized box in layout** (`360 x 400`). Larger modes are achieved with
an absolutely positioned inner container whose insets go negative:

```
inner container: position absolute, top/left/bottom/right computed, maxWidth 1920
dh = size.height - glanceSize.height
dw = size.width  - glanceSize.width

vertical anchor "top":     top = off.y             bottom = -(dh + off.y)
vertical anchor "center":  top = -(dh/2 - off.y)   bottom = -(dh/2 + off.y)
vertical anchor "bottom":  top = -(dh - off.y)     bottom = -off.y

horizontal "left":         left = off.x            right = -(dw + off.x)
horizontal "center":       left = -(dw/2 - off.x)  right = -(dw/2 + off.x)
horizontal "right":        left = -(dw - off.x)    right = -off.x
```

With the stock `ANCHOR_BOTTOM_LEFT` + `NO_OFFSET`, a FOCUSED card therefore keeps its
bottom-left corner pinned and grows `+120` up and `+72` right; a SELECTED card grows
`+410` up and `+104` right. **This is why the strand does not reflow when a card is
focused.**

Any mode other than `GLANCE` gets `zIndex: 1` (`foregroundMode`), so a grown card draws
over its neighbours. The card root is `flex: 1`.

Resize (a card asking for a different selected size at runtime) uses
`LayoutAnimation.create(duration, easeOut, opacity)` with a default duration of
**300 ms** (`animate: false` -> `0`, or an explicit number).

### 2.5 Multitask placement geometry

[NPXS40033 m55]

**PinP** — `calculatePinPOffsets` places the card against a **12 px** screen margin:

| Slot | left | top |
|---|---|---|
| topLeft / centerLeft / bottomLeft | `12` | — |
| topCenter / bottomCenter | `(1920 - w) / 2` | — |
| topRight / centerRight / bottomRight | `1920 - w - 12` | — |
| top* | — | `12` |
| center* | — | `(1080 - h) / 2` |
| bottom* | — | `1080 - h - 12` |

**Split screen** — `calculateSplitScreenOffsets`, only two slots:

| Slot | left | top |
|---|---|---|
| topLeft | `8` | `1080 - h - 135` |
| topRight | `1920 - w - 8` | `1080 - h - 135` |

**Selected (in-hub)** — `calculateSelectedModePosition` uses an **84 px** safe margin:

| Anchor axis | value |
|---|---|
| vertical `top` | `84` |
| vertical `center` | `540 - h/2` |
| vertical `bottom` | `1080 - h - 84` |
| horizontal `left` | `84` |
| horizontal `center` | `960 - w/2` |
| horizontal `right` | `1920 - w - 84` |

All three return *offsets relative to the card's current on-screen position*
(`pageX/pageY` are measured, then the anchor delta is applied), so they feed the
over-growth insets of §2.4 rather than absolute positioning.

### 2.6 The mode state machine

Events [NPXS40003 m210, `AC_EVENT`]: `focus`, `blur`, `enter`, `back`, `left`, `right`,
`up`, `down`, `pinp`, `split-screen`, `background`, `move-multitask-card`,
`retrieve-multitask-card`, `revert-active-multitask-card`.

`AC_MODE_MAP` [NPXS40003 m1083]:

| From | Event | To |
|---|---|---|
| `GLANCE` | `focus` | `FOCUSED` |
| `FOCUSED` | `focus` | `FOCUSED` (re-entrant) |
| `FOCUSED` | `blur` | `GLANCE` |
| `FOCUSED` | `enter` | `SELECTED_LOADING` |
| `FOCUSED` | `pinp` / `split-screen` / `move-multitask-card` | resolved multitask mode |
| `SELECTED_LOADING` | `back` | `FOCUSED` |
| `SELECTED_LOADING` | `left` / `right` | ignored |
| `SELECTED_LOADING` | `pinp` / `split-screen` / `move-multitask-card` | resolved multitask mode |
| any pinp-positioning | `back` | latest of `FOCUSED`/`SELECTED_LOADING` in history, default `FOCUSED` |
| any pinp-positioning | `retrieve-multitask-card` | `FOCUSED` |
| any pinp/split positioning | `revert-active-multitask-card` | resolved multitask mode |
| split-screen-left | `right` | split-screen-right |
| split-screen-right | `left` | split-screen-left |

PinP positioning is a 3x3 grid walk with holes (there is no centre-centre slot):

| From | up | down | left | right |
|---|---|---|---|---|
| top-left | — | center-left | — | top-center |
| top-center | — | bottom-center | top-left | top-right |
| top-right | — | center-right | top-center | — |
| center-left | top-left | bottom-left | — | center-right |
| center-right | top-right | bottom-right | center-left | — |
| bottom-left | center-left | — | — | bottom-center |
| bottom-center | top-center | — | bottom-left | bottom-right |
| bottom-right | center-right | — | bottom-center | — |

Mode resolution for a multitask request: an explicit `pinp`/`split-screen` event uses the
respective default positioning mode; otherwise the card's `initialMultitaskMode`;
otherwise the **earliest** multitask mode in the card's mode history; otherwise `null`
(no transition).

Sound: a d-pad move that lands on a new positioning slot plays `psfx_focus_move`; a
blocked move (screen-reader on) plays `psfx_tts_cannot_move_focus`.

### 2.7 Card visual spec

Root card box [NPXS40033 m1348 — the hub's standalone `ActionCard`]:

| Property | Value |
|---|---|
| width | `360` |
| height | `456` |
| `borderRadius` | `16` |
| background | `#17191E` (`actionCardBackgroundColor`) [m279] |
| focus style | `roundedRectangle` |

Note the **456** vs `AC_DIMENSIONS.GLANCE.height` **400**: the hub's `ActionCard`
reserves 56 px under the card body for the key guide row.

`CommonUITemplate` [NPXS40033 m499, styles m1346] composes, bottom-of-stack first:

1. `backgroundContainer` (absolute, fills) holding the cover image and `FaceBg`.
   Cover image is requested at `864 x 1040`, `keepFitSize`, `linear` filter, `cover`.
   Bottom overlay gradient: `{ type: "overlay-gradient", position: "bottom", length: L }`
   where **L = 64** out-of-control-centre, **12** in `GLANCE`, **88** otherwise.
2. `Header`.
3. `Face` — `faceG` (`100% x 188`) in glance / out-of-CC, `faceF` (`100% x 232`,
   `marginBottom: 8`) otherwise. `SELECTED_FACE_HEIGHT = 232`.
4. `Message` (absolute, bottom).

Light-mat (the native surface lighting hint) is `"forceOn"` for `SELECTED_LOADING`,
`SPLIT_SCREEN`, `PINP` and any multitask positioning mode; `"auto"` when there is no
cover image; otherwise unset.

`Header` [NPXS40033 m492, styles m493]:

| Style | Value |
|---|---|
| `containerCompact` (glance/focused) | row, `height: 72`, `paddingTop: 16`, `paddingBottom: 8`, `paddingHorizontal: 16` |
| `containerFull` (selected / split-screen) | row, centred, `height: 80`, `paddingHorizontal: 24`, `paddingTop: 24`, `marginBottom: 16` |
| `SELECTED_HEADER_HEIGHT` | `80` |
| `SELECTED_HEADER_BOTTOM_MARGIN` | `16` |
| title | `SizeSmall`, `opacity: 0.7` |
| left identifier icon | `marginRight: 8` (compact) / `16` (full) |
| multitask indicator | `marginLeft: 2` (compact) / `8` (full) |

Custom selected-face animation: if the card supplies `face.customAnimation.selected
.centerY`, the face translates by
`centerY - SELECTED_HEADER_HEIGHT(80) - SELECTED_HEADER_BOTTOM_MARGIN(16) -
SELECTED_FACE_HEIGHT/2(116)`.

`Message` [NPXS40033 m495, styles m1333]:

| Property | Value |
|---|---|
| root | `height: 140`, `width: 100%` |
| container | absolute, `bottom: 16`, `left: 24`, `right: 24`, `overflow: hidden` |
| glance text width | `312` (= `GLANCE.width - 48`) |
| focused text width | `384` (= `FOCUSED.width - 48`) |
| primary message | `SizeNormal`, bold, `marginTop: 4`, 1 line |
| secondary message | `SizeXSmall`, `opacity: 0.7`, `marginLeft: 2`, 1 line |
| secondary emoji | `SizeXSmall`, `opacity: 1` |
| meta description | `SizeXSmall`, `opacity: 0.7`, 1 line |
| meta container margin-top | `8` (focused / out-of-CC), `16` (glance) |
| glance meta lift, 1 line | `bottom: -34` (normal font scale) / `-26` (small) |
| glance meta lift, 2 lines | `bottom: -68` (normal) / `-52` (small) |
| width swap delay | `300` ms after the mode change |
| ellipsize | `"tail"`, becoming `"marquee"` on the primary message when focused |

`KeyGuide` [NPXS40033 m498, styles m1338]:

| Property | Value |
|---|---|
| container | absolute, `bottom: 0`, `width: 100%`, `height: 0`, `row-reverse`, top-aligned |
| key guide | `marginTop: 16` |
| visible in | `FOCUSED` and `SELECTED_LOADING` only |
| fade in | delay `400` ms (skipped after a shortcut swap) -> jump to `opacity 0.1` -> `1` over `150` ms |
| fade out | `-> 0` over `100` ms (**0 ms** for `PINP` / `SPLIT_SCREEN` / any multitask positioning) |
| shortcut swap hold | `400` ms at `opacity 0` between old and new guides |
| background | disabled (`enableBackground: false`) |

Shortcut filtering: in `GLANCE` and `FOCUSED` only the **Square** shortcut is shown; all
other modes show the full shortcut set. Foreign cards show none [NPXS40003 m880].

### 2.8 Mode-change animation

[NPXS40033 m275 `useModeAnimation`]

A single driver value per card maps modes to a scalar:

| Mode | value |
|---|---|
| `PINP`, any pinp-positioning | `-1` |
| `GLANCE`, `FOCUSED` | `0` |
| `SELECTED`, `SELECTED_LOADING`, `SPLIT_SCREEN`, any split-positioning | `+1` |

| Track | Duration | Easing |
|---|---|---|
| transform | `300` ms | `easeOutBlastPS` |
| fade (to `0`) | `250` ms | linear |
| fade (to `+/-1`) | `300` ms | linear |
| either, when the previous mode was `null`/`PINP`/`SPLIT_SCREEN` | `0` ms (snap) | — |

**GLANCE <-> FOCUSED transitions are explicitly excluded** — no animation runs; only the
container over-growth (a layout change) and the discrete style swaps do the work.

Derived style helpers (input range is always `[-1, 0, 1]`):

- `createFadeAnimationStyle(range)` -> `opacity`.
  Used as `[0,1,0]` for the cover/`FaceBg` layer, the face, and the message (visible only
  at glance/focused); `[0,1,1]` for the header identifier icon (hidden only in PinP).
- `createTranslateAnimationStyle(n)` -> `translateY` over `[0, 0, n]`.
- `createScaleAnimationStyle(n)` -> `scale` over `[1/n, 1/n, 1]`.

`easeOutBlastPS` is the `EaseOutBlast` curve Prosperismo already models (r = 10).

### 2.9 `AC_SR_ORDER` (screen-reader speech order)

[NPXS40033 m276]

| Key | Order |
|---|---|
| `SECTION_NAME` | `0` |
| `IDENTIFIER_ICON` | `1` |
| `SECONDARY_MESSAGE` | `2` |
| `PRIMARY_MESSAGE` | `3` |
| `INDICATOR_ICON` | `4` |
| `META_DESCRIPTION` | `5` |
| `FACE_GENERAL` | `10` |
| `FACE_ORDER_1` .. `FACE_ORDER_9` | `11` .. `19` |

### 2.10 `ActionCard` vs `ActionCardContainer` vs `StandaloneActionCard` vs `MultitaskCardLayer`

| Component | Where | Role |
|---|---|---|
| `ActionCard` [NPXS40033 m1347] | game hub | A **local, non-interactive-host** card. Owns its own mode state (`GLANCE` at rest, `FOCUSED` on highlight, `SELECTED` on Enter). On Enter it plays `psfx_enter`, stamps `mode: SELECTED` onto the deeplink and opens it via `LinkingAC.open` — i.e. it hands off to Control Center rather than expanding in place. Card box `360 x 456 r16`. |
| `ActionCardContainer` [NPXS40003 m880] | action-cards-host consumer SDK | The **real** card host. Runs the `AC_MODE_MAP` state machine, computes over-growth insets, hosts the card's own app module (`card-content`), owns the options menu and key guide, handles resize/multitask/PinP/split-screen. |
| `StandaloneActionCard` [NPXS40003 m764] | control-center deeplink handler | Wraps `ActionCardContainer` for a card opened by deeplink with no strand around it. Adds `LayoutAnimation` open/close at `OPEN_CLOSE_ANIMATION_DURATION = 300` ms with `easeOutBlastPS` on `opacity`, an initial split-screen offset computed against `ANCHOR_CENTER` and the fullscreen size, and size normalisation via `normalizeSelectedSize` / `normalizePinPSize`. |
| `MultitaskCardLayer` [NPXS40003 m2202] | control-center deeplink handler | The layer that keeps a *multitasked* (PinP / split-screen) card alive while the user is elsewhere. Reactivates the card with `animationDuration: 0` and transfers its app module to a portal (`app-module-client-<id>`). Renders `StandaloneActionCard` inside a `DeeplinkLayer` that supplies a scale animation style. |

Options menu placement for both card hosts:
`{ menuPosition: "right", verticalAlign: "bottom", collision: "fit" }`.

---

## 3. Grid and segments

### 3.1 Constants

[NPXS40033 m300]

| Constant | Value |
|---|---|
| `GRID_ITEM_MARGIN` | `20` |
| `DEFAULT_MARGIN_UNDER_BOTTOM_ITEM` | `90` |
| `SEGMENT_HEADER_HEIGHT` | `34` |
| `SEGMENT_HEADER_BOTTOM_MARGIN` | `24` |

`GridPadding` is keyed by **column count**:

| Columns | `paddingVertical` | `paddingHorizontal` |
|---|---|---|
| `3` | `32` | `32` |
| `4` | `32` | `32` |
| `5` | `24` | `24` |

(Only 3, 4 and 5 columns exist. `paddingHorizontal` doubles as the inter-column gap —
see §3.3.)

### 3.2 `GridTypes` and `GridListViewPS`

[NPXS40033 m299 `GridListViewPSWrapper`]

`GridTypes`: `SCANNABLE = 0`, `PAGINATED = 1`, `SEGMENTED = 2`.

| Prop | Default / rule |
|---|---|
| `activeAreaOffset` | `1620` |
| `sectionTailItemMargin` | `GRID_ITEM_MARGIN` (`20`) |
| `marginUnderBottomItem` | `DEFAULT_MARGIN_UNDER_BOTTOM_ITEM` (`90`) |
| `edgeFadePadding` | `{ top: 24, bottom: 0 }` |
| `showPlaceholder` | `true` |
| `rowItemFocusable` | `false` |
| `poolingListItem` | `false` |
| `focusInBehavior` | `"lastFocusedItem"` |
| vertical scroll indicator | shown |

Type-dependent options:

| Option | `SEGMENTED` | other |
|---|---|---|
| `renderSectionHeader` | supplied | — |
| `snapScrollMode` | `!stickyHeader` | — |
| `focusScrollOverlappedMargin` | `58` (= `24 + 34`) when sticky header, else unset | `24` |
| `topMarginOverTopItem` | — | `24` |
| `sectionItemMargin` | `24` (= `SEGMENT_HEADER_BOTTOM_MARGIN`) | — |
| `sectionTailItemMargin` | `20` | — |
| scroll indicator | `{ type: "normal", marginStart: 58, marginEnd: 0 }` | — |

When empty, `showPlaceholder` paints the list background with `COLOR.BLANK`.
Loading and error states swap the whole list for a loading component or an error/no-results
component respectively.

### 3.3 `GridWrapper` layout rule

[NPXS40033 m570]

```
{ paddingVertical, paddingHorizontal } = GridPadding[numColumns]
containerWidth = itemWidth * numColumns + paddingHorizontal * (numColumns - 1)
contentContainerStyle = { itemWidth, itemHeight, paddingVertical, paddingHorizontal }
optionContainerStyle  = { top: paddingVertical }
```

So for a 4-column grid of 370-wide tiles: `370*4 + 32*3 = 1576` — the same 1576 that
recurs as the hub nav width and Prosperismo's `STRAND_WIDTH`. **1576 is the shell's
canonical content width**, and the grid, the strand and the horizontal hub nav all land
on it.

`GridWrapper` also exposes `scrollToItem({ itemIndex, animated })` which scrolls so the
item is the first visible row, offset by `paddingVertical`, and `setFocusedItem(index)`.
`initialFocusItem` accepts either a bare integer (interpreted as `itemIndex` in section 0)
or `{ itemIndex, sectionIndex }`.

---

## 4. Tile states and `CLICK_TYPES`

### 4.1 The four states are *card* modes, not tile modes

`GLANCE` / `FOCUSED` / `SELECTED` / `SELECTED_LOADING` are `AC_MODE` values — they belong
to activity cards, not to home tiles. Their visual deltas:

| Property | `GLANCE` | `FOCUSED` | `SELECTED_LOADING` | `SELECTED` |
|---|---|---|---|---|
| Box | `360 x 400` | `432 x 520` | `464 x 810` | `464 x 810` |
| Layout footprint | `360 x 400` | `360 x 400` (grows via insets) | same | same |
| Growth from bottom-left | — | `+120` up, `+72` right | `+410` up, `+104` right | same |
| `zIndex` | `0` | `1` | `1` | `1` |
| Header style | compact (`h 72`, pad `16/8/16`) | compact | **full** (`h 80`, pad `24/24`) | **full** |
| Face height | `188` | `232` (+`8` bottom) | `232` | `232` |
| Bottom overlay gradient | `12` | `88` | `88` | `88` |
| Message text width | `312` | `384` | `384` | `384` |
| Message ellipsize (primary) | tail | **marquee** | tail | tail |
| Key guide | hidden | **visible** | **visible** | hidden |
| Shortcuts shown | Square only | Square only | all | all |
| Light mat | `auto` if no cover | `auto` if no cover | **`forceOn`** | unset |
| Driver value | `0` | `0` | `+1` | `+1` |
| Transition into it | — | **no animation** from glance | `300` ms `easeOutBlastPS` + `300` ms linear fade | as loading |

`SELECTED_LOADING` is what `enter` actually produces; `SELECTED` is only reachable as a
normalised container mode (`normalizeContainerMode` maps `SELECTED -> SELECTED_LOADING`)
or as the app-module-facing alias (`deriveAppModuleMode` maps it back).
`SELECTED_LOADING` is distinguishable on screen only by the forced light mat and the key
guide staying up while the card's content loads.

### 4.2 Home tile (`TileItem`) states, for contrast

[NPXS40002 m540, m551, m25]

The home tile has no glance/focused/selected enum. Its state is:

| State | Visual |
|---|---|
| Rest | icon at `EXPERIENCE_SIZE` = `106`, drawn in a `168`-tall row |
| Focused (selected in strand) | `scale -> EXPERIENCE_SCALE` (`1.58490566`), spring `stiffness 400, damping 50, mass 0.2, overshootClamping` |
| Focus ring radius | `BORDER_RADIUS * EXPERIENCE_SCALE` = `16 * 168/106` = `25.358` |
| Hub-open (minimised) | see §1.4 |
| Options menu target | `106 x 106` |
| Download bar | `width 90`, container `marginTop 2`, centred |

Tile focus settings: `canMoveLeft: false`, `canMoveRight: false` (horizontal movement is
handled by the strand's own key handlers), `canMoveUp` / `canMoveDown` disabled while a
startup animation is running, `upCandidate: "space-switcher-<spaceId>"`.
Icon textures are requested at `2 x SCALED_EXP_SIZE` = `336 x 336`, `bc7u`,
`keepFitSize`, `mitchell`.

### 4.3 `CLICK_TYPES`

[NPXS40002 m38] — telemetry interaction identifiers; useful as an authoritative list of
what a tile / options menu can actually do.

| Key | Value |
|---|---|
| `APP_INFO` | `"app information"` |
| `CHECK_SYNC_STATUS_OF_SAVED_DATA` | `"check sync status of saved data"` |
| `CHECK_UPDATE` | `"check for update"` |
| `CLICK_ANI` | `"click ani"` |
| `CLICK_PROFILE` | `"click profile"` |
| `CLICK_SEARCH` | `"click search"` |
| `CLICK_SETTING` | `"click settings"` |
| `CLOSE_APP` | `"close game"` |
| `CONTINUE_TO_PS4` | `"continue to ps4 version"` |
| `DELETE_APP` | `"delete app"` |
| `DELETE_FROM_HOME` | `"delete from home"` |
| `EJECT_DISC` | `"eject disc"` |
| `GAME_VERSION` | `"game version"` |
| `IP_NOTICE` | `"intellectual property notices"` |
| `LAUNCH_APP` | `"launch app"` |
| `MANAGE_CONTENT` | `"manage game content"` |
| `MOVE_TO_INTERNAL_STORAGE` | `"move to internal storage"` |
| `MOVE_TO_USB_EXTERNAL_DRIVE` | `"move to usb external drive"` |
| `OPEN_HUB` | `"open hub"` |
| `OPEN_OPTIONS_MENU` | `"open options menu"` |
| `SELECT_ITEMS_TO_DELETE` | `"select items to delete"` |
| `SWITCH_TO_PS5` | `"switch to ps5 version"` |
| `UPDATE_HISTORY` | `"update history"` |
| `UPLOAD_DOWNLOAD_SAVED_DATA` | `"upload/download saved data"` |

`OPEN_HUB` is emitted by `animateVerticalToHub` [NPXS40002 m130] with the title id and
the space-appropriate title name (`experienceName` for the game space, `conceptName` for
media) — confirming that "open hub" is the single canonical transition, not a per-screen
navigation.

---

## 5. Strand packing (beyond what Prosperismo already has)

`packages/rnps-js-modules-strand` [NPXS40002 m530, math in m531].

### 5.1 Parameters home passes

[NPXS40002 m201 `Space`]

| Prop | Value |
|---|---|
| `focusedMargin` | `16` |
| `itemMargin` | `8` |
| `selectedItemScale` | `EXPERIENCE_SCALE` = `168/106` |
| `maxItems` | `MAX_TILES` = `11` |
| `springOptions` | from a Recoil atom; the strand's own default is `stiffness 400, damping 50, mass 0.2, overshootClamping: true` |
| container style | `strandStyle: { marginLeft: 172 }`, `strandContainer: { width: 1500, height: 168 }` |
| row container | `flexDirection: row`, `width: 1920`, `height: 168` |

Note **1500**, not 1576: the strand's clip box is 1500 wide starting at x = 172
(172 + 1500 = 1672, leaving a 248 px right gutter). 1576 is the *content* width used by
grids and hub nav; the strand uses 1500.

### 5.2 The packing formula

`updateState` precomputes a per-slot growth offset:

```
offsets[i] = (itemWidth[i] * selectedItemScale - itemWidth[i]) / 2
           = itemWidth[i] * (selectedItemScale - 1) / 2
```

`calculate(scale, layout, focusedMargin, itemMargin, offsets, i, poolIndex, sel)` returns
the tile's `translateX`:

```
w = layout(i).width

# nothing selected
if sel == -1:
    x = i * (w + itemMargin) - w * scale / 2

# something selected
x = offsets[i] + (i - sel) * (w + itemMargin)
if i < sel:  x -= offsets[i] + focusedMargin - itemMargin
if i > sel:  x += offsets[i] + focusedMargin - itemMargin
```

Simplified with the home values (`w = 106`, `itemMargin = 8`, `focusedMargin = 16`,
`scale = 168/106`, so `offsets[i] = 31`):

| Case | translateX |
|---|---|
| `i == sel` | `+31` |
| `i < sel` | `(i - sel) * 114 - 8` |
| `i > sel` | `(i - sel) * 114 + 70` |
| no selection | `i * 114 - 84.19` |

i.e. the base pitch is **114 px** (`106 + 8`); the selected tile inserts an extra
`focusedMargin - itemMargin = 8` px of clearance on the left side and
`2 * offsets + 8 = 70` px on the right, which is the room the 1.585x scale needs.

### 5.3 Per-item styles

- Each slot is `{ ...getItemLayout(n), position: "absolute" }` — absolutely positioned by
  pool slot, translated by the value above.
- Container gets `marginTop: -h/2`, each item `marginTop: +h/2`
  (`h = getItemLayout(0).height`), so the strand is vertically centred on its own
  baseline regardless of item height.
- Item transform is `[{ translateX }, { scale }]` where `scale` interpolates
  `0..1 -> 1..selectedItemScale`.
- The whole set animates in parallel; `onEnd` fires only when *all* springs finish.
- Layout state is recomputed from scratch (`updateState` + `setValue`, no animation) when
  `selectedItemScale`, `getItemLayout` or `maxItems` change; only a change of
  `selectedItem` or `data` animates.
- Pool slots come from `useDataPool({ data, keyExtractor, maxItems })` — a tile keeps its
  slot across data updates, which is what makes the reorder animation stable.

---

## 6. Gaps and honest caveats

1. **No spring integrator in Prosperismo.** Every hub and strand transition above is a
   spring, not a curve. `SPRING_OPTIONS_FAST` (k=200, c=100, m=0.2) has damping ratio
   `c / (2*sqrt(k*m)) = 100 / (2*sqrt(40)) ~= 7.9` — heavily overdamped, no overshoot,
   settles in roughly 150-250 ms. `SPRING_OPTIONS_FASTER` (k=600) is ~4.6, also
   overdamped. The strand's own default (k=400, c=50, m=0.2) is ratio ~0.88 —
   **underdamped**, but `overshootClamping: true` truncates it at the target, so it reads
   as a fast decelerating slide. Until a spring integrator exists, approximating these
   with `EaseOutBlast` (r=10) over ~250 ms is the closest available substitute; that is an
   approximation, not the shipped behaviour.
2. **`easeOutBlastPS` and `Easing.liner`** are native easing identifiers exposed to JS;
   their exact coefficients are not in the bundles. `EaseOutBlast` (r=10) is
   Prosperismo's existing model of the former; `liner` is presumably linear (the spelling
   is Sony's).
3. **Native placement is absent from every bundle.** The card's actual on-screen
   compositing when PinP'd or split-screened goes through `SceneControl` /
   `MultitaskService` / `AppModuleManager.transfer` — native calls. The JS computes offsets
   and hands them over; where the surface ends up, how it stacks against the running game,
   and the slide-in of the Control Center itself are native and not recoverable here.
   Same for `SceneControl.setBackground` (the hub wallpaper and its Ripple/CrossFade
   transition) and `setSystemBgm`.
4. **`GridPadding` is only defined for 3/4/5 columns.** Any other column count yields
   `undefined` padding and would crash the real shell; treat 3-5 as the supported range.
5. **`getSceneLayout` is supplied by each hub template**, so per-scene heights are
   title-specific; only the `+64` inter-scene stride is universal.
6. **`Face` / `FaceBg` variants not enumerated here.** The SDK ships 11 face types
   (`Broadcast`, `ChallengePeople`, `ChallengeScore`, `Icon`, `Image`,
   `ImagePeopleCount`, `People`, `Progress`, `Score1`, `Score2`, `Trophy`) and 3 face
   backgrounds (`ChallengeBg`, `ScreenShareBg`, `VoiceChatBg`) [NPXS40033
   `action-card-sdk/src/components/Face*`]. Sampled metrics: screen-share background is
   `360 x 188` in glance and `432 x 232` in focused, at `top: 70` / `top: 77`; challenge
   background uses `top: 72` with heights `188`/`232`, bars `16 x 40`, stripes
   `height 42, marginHorizontal 4`. Full per-face specs are unextracted.
7. **A latent bug in `CommonUITemplate`'s face-style selection** [NPXS40033 m499]: the
   ternary chain tests the truthiness of the `FOCUSED` enum *constant* rather than
   comparing it to the current mode, so the `faceS` branch is unreachable and every
   non-glance mode uses `faceF`. Reproduce this if matching behaviour exactly matters;
   `faceS` (`100% x 232`, no bottom margin) is dead code on retail 3.00.
8. **`hubStartTime` / `HubUtility.convertHubStartTimeToPerformanceTimestamp`** exist for
   perf marking; the hub also emits `launchStart` marks in namespace `ac/<type>` on the
   `FOCUSED -> SELECTED_LOADING` edge. Not needed for rendering, listed for completeness.
9. **`experienceOpacity` at home.** `useHeaderTransition` springs non-selected tiles to
   opacity `0` at the hub position and `1` at home; `useBarAnimation` sets download-bar
   opacity to `1` only when at home **and** the tile is not the selected one. The latter
   asymmetry is taken literally from the source and has not been confirmed against a
   running console.
