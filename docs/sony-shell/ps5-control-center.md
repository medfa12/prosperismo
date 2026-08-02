<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 Control Center — layout, controls, motion, focus

Clean-room reference for building the PS-button overlay ("Control Center") in
Prosperismo. Everything here is a numeric constant, a structural fact, or a
behavioural rule read out of the readable Control Center JavaScript bundle for
system app **NPXS40003**, firmware 3.00. No source is reproduced; identifiers and
bundle module ids appear only as locators.

Companion documents: [`ps5-shell-overlays.md`](ps5-shell-overlays.md) (the managed
`BGLayer`/`SystemModalDialog` model), [`ps5-shell-motion.md`](ps5-shell-motion.md),
[`ps5-home-structure.md`](ps5-home-structure.md), [`ps5-shell-theme.md`](ps5-shell-theme.md).

## Provenance and how to read the locators

The Control Center is a React Native application, not a managed scene like the
system modals in `ps5-shell-overlays.md`. Its bundle is a Haul/webpack bundle whose
module bodies are registered by numeric id. Locators below are written as
`mod NNN` and, where the bundle carries a build path, the originating package file.

The app root is `apps/control-center`. Two source trees matter:

| Tree | What lives there |
|---|---|
| `src/modules/function-control-bar/**` | the bottom icon row, its buttons, popup, badges, key guide, customize panel, and one folder per control under `controls/` |
| `packages/function-control*/**` | the *contents* of each control's panel (sound, power, network, device/accessories, transfers, notification list, ...) plus the shared panel chrome in `packages/function-control/` |

Other CC-level modules: `src/modules/control-center/containers` (the scene root),
`src/modules/action-carousel` (the card row above the bar),
`src/modules/deeplink-handler` (`ActionCardLayer`, `MultitaskCardLayer`,
`DeeplinkLayer`, `FocusCapture`, `StandaloneActionCard`), `src/modules/multitask`,
and `src/components/GradientBackground`.

All geometry is in a fixed **1920 × 1080** design space (`SYSTEM_WIDTH = 1920`;
the gradient backdrop View is literally 1920 × 1080).

---

## 1. Layout and geometry

### 1.1 Scene root

Source: `src/modules/control-center/containers/index.js` (mod 2204) and its style
module (mod 2225).

| Element | Value |
|---|---|
| Root container | `flex: 1` (full screen) |
| Action-card carousel | absolute; `left 84`, `right 0`, `bottom 190`, `height 520`; row, items bottom-aligned |
| Function-control bar | absolute; `left 0`, `bottom 0` |
| Clock strip | row, right-aligned, `height 126`, `marginRight 84` |
| Play-time (parental) indicator | `marginRight 88` from the clock, size `small`, `lightBase` |
| Clock text | font size token `SizeLarge` |
| Full-screen backdrop | `1920 × 1080` View with base-mat `{ type: "fullscreen-gradient" }`, ignores parent transform (`src/components/GradientBackground`, mod 1184) |

Child order inside the root focus layer: clocks → function-control bar → action
carousel. `left: 84` and `marginRight: 84` are the same value as `SYSTEM_MARGIN`.

### 1.2 System constants

Source: `packages/function-control-navigation/src/navigation` style module (mod 319).

| Constant | Value |
|---|---|
| `SYSTEM_WIDTH` | `1920` |
| `SYSTEM_MARGIN` | `84` |

Usable horizontal band for popups is therefore **84 … 1836**.

### 1.3 The function-control button row

Source: mod 319 (row) and mod 220 (button metrics).

| Constant / style | Value |
|---|---|
| `BUTTON_CONTAINER_WIDTH` | `112` |
| `BUTTON_CONTAINER_HEIGHT` | `147` |
| `BUTTON_CONTAINER_MARGIN` | `64` — exported but **not referenced anywhere in this bundle** |
| Row container | `width 1920`, `height 147`, `flexDirection: row`, `justifyContent: center` |
| Leading spacer | `flexGrow: 1`, rendered **before** the buttons |
| Button container | `112 × 147`, children centre-aligned |
| Icon box | `48 × 48`, `marginTop 54` (so the icon's top edge is 54 px below the row's top edge) |
| Icon image | `48 × 48` |
| Pressable (`Button`) hit area | `56 × 56`, background `visibleOnFocus`, inverted-icon filter colour `black` |
| Label container | absolute, `top 0`, `height 34`, `width 368`, content bottom-aligned and centred |
| Label text | font size token `SizeXSmall`, single line, `ellipsizeMode: "marquee"` |
| Badge container | absolute, `top 0`, `left 0` |

Because a `flexGrow: 1` spacer precedes the button row inside a fixed 1920-wide
container, **the row is right-anchored**: the rightmost control (power) always ends
at x = 1920 and the row grows leftward as controls become visible. With all 16
controls shown the row starts at x = 128.

### 1.4 The expanded panel (popup)

Source: shared popup style module (mod 528) and `src/modules/function-control-bar/components/Popup` (mod 1213).

| Constant / style | Value |
|---|---|
| `DEFAULT_MENU_WIDTH` | `652` |
| Popup frame | `minWidth 652`, `maxWidth 784`, `minHeight 216`, `maxHeight 810`, absolute, `bottom 190`, `paddingBottom 8` |
| `PopupBaseMat` | `flex: 1`, `borderRadius 16`, background `rgba(8, 10, 15, 1.0)` = **`#080A0F`**, `lightMat: "auto"` |

Horizontal placement algorithm (mod 1213), given the button's measured X within the
bar and the control's `menuWidth`:

```
left = buttonX + BUTTON_CONTAINER_WIDTH/2 - menuWidth/2      // centre on the button
if (left < SYSTEM_MARGIN)                left = SYSTEM_MARGIN         //  84
if (left + menuWidth > SYSTEM_WIDTH - SYSTEM_MARGIN)                  // 1836
                                          left -= overflow
```

The popup bottom is pinned at 190 px — the same `bottom` as the action carousel, so
panels rise from just above the icon row.

### 1.5 Panel chrome (header / list / rows)

Source: `packages/function-control/src/components/FunctionControlHeader` (mod 347),
`.../FunctionControlListItem` style module (mod 240), `.../FunctionControl` (mod 346)
and its style + height calculator (mod 241).

| Constant / style | Value |
|---|---|
| `HEADER_HEIGHT` | `80` |
| Header row | `height 80`, row, centre-aligned, `padding 24`, `opacity 0.7` |
| Header text | font size token `SizeSmall` |
| Header icon | `48 × 48`, `borderRadius 8`; its container `48 × 48`, `marginRight 16` |
| `LIST_ITEM_HEIGHT` (panel rows) | `98` (used as `minHeight`) |
| `LEFT_ICON_SIZE` | `56` |
| `LEFT_ICON_MARGIN_LEFT` | `16` |
| `LEFT_ICON_MARGIN_RIGHT` | `20` |
| Separator inset (derived) | `left 92` ( = 56 + 20 + 16 ), `right 16` |
| Right-icon slot | `48 × 48`, `marginHorizontal 16` |
| Profile row variant | `height 90`, `marginBottom 2` |
| With-description variant | left icon top-aligned, `marginTop 21` |
| List body | `flex: 1`, `marginHorizontal 8`, `marginBottom 16` |
| First section header | `marginBottom 8`, `marginHorizontal 16` |
| Later section headers | `marginTop 24`, `marginBottom 8`, `marginHorizontal 16` |
| Section header text | font size token `SizeXSmall`, `opacity 0.7` |
| Loading indicator | `height 30`, `marginHorizontal 24`, `marginVertical 12` |
| In-panel key guide | absolute `bottom 0`, `width 100%`, `height 0`, `row-reverse`, top-aligned; guide `marginTop 16` |

The list is a `ListViewPS` with separators on, edge fading on (bottom fade padding 0),
rows themselves not focusable, initial focus at section 0 / item 0.

**Panel auto-height** (`calculateContainerHeight`, mod 241) — this is why
`minHeight 216` / `maxHeight 810` exist:

```
sectionHeaderUnit g = 34   (normal)       // 26 for "small", 50 for "large"/"veryLarge"
headerBlock = 0
if sections >= 1:  headerBlock += g + 8
if sections >= 2:  headerBlock += (sections - 1) * (g + 24 + 8)

height = HEADER_HEIGHT(80) + headerBlock + sum(rowHeights) + 16
height = clamp(height, 216, 810)
```

### 1.6 Per-control panel widths

Default is `DEFAULT_MENU_WIDTH` (652). Overrides observed:

| Control | Width | Note |
|---|---|---|
| sound | `784` | fixed |
| music | `652` | panel body forced to `height 810` |
| network | `652` | panel body `minHeight 100` |
| gaming lounge | `652` | suppresses its own base-mat when it has card content |
| notifications | `652`, `784` when large text | via the notification toast layout table (`toastMaxWidth 652` / `toastMaxWidthForLargeText 784`) |
| profile | `652`, `784` at font scaling `large`/`veryLarge` | |
| transfers | `652`, `784` at font scaling `large`/`veryLarge` | |
| all others | `652` | |

### 1.7 The "customize controls" panel

Source: `src/modules/function-control-bar/components/CustomPanel` (mod 2216) + style (mod 2217),
custom button style (mod 770).

| Constant / style | Value |
|---|---|
| `CONTAINER_HEIGHT` | `434` |
| Panel container | `1920 × 434`, absolute `bottom 0`, `left 0` |
| Base mat | `{ type: "overlay-panel", position: "bottom", length: 434 }` |
| Message block | `1640 × 178`, `paddingTop 8`, centred |
| Header text | font size token `SizeNormal`, max 2 lines |
| Edit-menu strip | `height 256`, row, centred |
| Custom button cell | `88 × 165`, `marginTop 42` |
| Its icon | `48 × 48`; label container `width 362`, `height 34` |
| Its toggle switch | `40 × 40` |
| Non-customisable item | `opacity 0.4`; its switch `opacity 0` |
| Icon bottom margin | `28` normally, `24` when focused |

`BUTTON_CONTAINER_WIDTH` is re-declared as `112` in mod 770 for the customize cells,
matching the live row.

### 1.8 Icon / badge geometry

Source: `packages/function-control/src/components/FunctionControlIcon` style (mod 1537),
`src/modules/function-control-bar/components/Badge` (mod 1529).

| Constant / style | Value |
|---|---|
| `CONTAINER_SIZE` | `64` |
| Icon container | `64 × 64`, centred |
| Battery badge container | `40 × 40`, centred |
| Badge anchor (normal) | absolute `top 31`, `left 31` |
| Secondary badge anchor | absolute `top -7`, `left 35` |
| Battery badge anchor | absolute `top 25`, `left 40` |
| Badge icon | `28 × 28`; secondary badge icon `20 × 20` |
| Battery icon / plate | `40 × 40` |
| Badge count text | font size token `Size3XSmall`, bold; counts above 99 render as `99+` |

Non-battery badges are scaled by `styleWidth / CONTAINER_SIZE` on both axes, i.e. 64
is the reference box a badge glyph is authored against.

Badge types and their icon ids: `new` → `new`; `status`/`online` → `lamp_online`;
`busy` → `lamp_standby`; `appearOffline` → `lamp_appear_offline`; `alert` →
`lamp_offline`; `battery` → looked up through `BATTERY_MAP`.

`BATTERY_STATUS_MAP` values: `empty`, `focusedEmpty`, `low`, `half`, `full`,
`chargingEmpty`, `chargingFocusedEmpty`, `chargingLow`, `chargingHalf`,
`chargingFull`, `unknown`. `BATTERY_MAP` maps each to an icon id; the "almost empty"
and "low/half/full charging" states use **animated** icon ids (`animation_battery_*`),
the rest are static (`battery_low`, `battery_half`, `battery_full`, `battery_unknown`).
When the button is focused, `empty` and `chargingEmpty` swap to the non-animated
`focusedEmpty` / `chargingFocusedEmpty` icons.

### 1.9 Transfers list rows

A separate row metric set (mod 112, consumed only by
`packages/function-control-transfers/**`):

| Constant | Value |
|---|---|
| `ICON_SIZE` | `96` |
| `LIST_ITEM_HEIGHT` | `146` (row incl. separator; body `minHeight 144` + `marginBottom 2`) |
| `LIST_ITEM_WIDTH` | `636` (derived: popup `minWidth 652` − 2 × list `marginHorizontal 8`) |
| `SEPARATOR_MARGIN` | `left 132`, `right 16` |
| Icon container | `marginLeft 16` |

---

## 2. The function-control inventory

`FC_TYPE` is **not** the control inventory — the brief's assumption was wrong. It is
the *list-row widget type* used inside a panel (mod 604):

```
FC_TYPE = { BUTTON: "Button", BUTTON_PROFILE: "ButtonProfile",
            DROPDOWN: "Dropdown", SWITCH: "Switch", SLIDER: "Slider" }
```

The real inventory is the control registry (mod 527) plus the bar's initial redux
state (mod 216). Sixteen controls exist; the array order below is the **left-to-right
order in the bar**.

| # | id | Icon id | Telemetry CTA | Customisable? | Hidden by default? | Feature flag |
|---|---|---|---|---|---|---|
| 1 | `home` | `home` | home | no | no | — |
| 2 | `apps` | `switcher` | app switcher | no | no | — |
| 3 | `notifications` | `notification` | notification | no | no | `enableNotificationsFC` |
| 4 | `gaming-lounge` | `game_base` | gaming lounge | yes | no | `enableGameBaseFC` |
| 5 | `music` | `music` | music | yes | no | `enableMusicFC` |
| 6 | `voice-agent` | `voice_command` | voice and agent | yes | no | `enableVoiceAgentFC` |
| 7 | `broadcast` | `broadcast` | broadcast | yes | **yes** | `enableBroadcastFC` |
| 8 | `accessibility` | `accessibility` | accessibility | yes | **yes** | — |
| 9 | `downloads` | `download` | transfer | no | no | — |
| 10 | `network` | `network` | network | yes | **yes** | — |
| 11 | `vr` | `psvr` | ps vr | no | no | — |
| 12 | `sound` | `sound_speaking` | sound | yes | no | — |
| 13 | `mic` | `mic` | microphone | yes | no | — |
| 14 | `controller` | `game` | accessories | no | no | — |
| 15 | `profile` | `ps_user` | profile | no | no | — |
| 16 | `power` | `power` | power | no | no | — |

Notes:
- "Customisable" = the user may hide it from the customize panel. The eight
  non-customisable entries are permanent.
- "Hidden by default" = `isDefaultUnavailable`, i.e. broadcast, accessibility and
  network are off until the user enables them.
- Package id ≠ control id in two places: the transfers control's id is `downloads`
  (package `function-control-transfers`), and the accessories control's id is
  `controller` (package `function-control-device`).
- Some controls swap their own icon by state: sound uses `sound_mute` when universal
  mic mute is on, notifications uses `notification_off` in Do-Not-Disturb, network
  and transfers compute theirs from live status.
- Telemetry scene names are `"function control:<name>"` (e.g. `function control:power`),
  and the bar as a whole reports `"function control"`; the customize panel reports
  `"function control:customization panel"`.

Per-control panel packages present in the bundle: `function-control-apps`,
`-accessibility`, `-broadcast`, `-device` (accessories: `device-screen.js`,
`controller-icon.js`, `list.js`, `listitem-wrapper.js`), `-gaming-lounge`, `-mic`,
`-music`, `-navigation`, `-network`, `-notification-list`, `-power`, `-profile`,
`-sound` (`component.js`, `component-voice-chat.js`), `-transfers`, `-voice-agent`,
`-vr`, plus `all-mute`.

---

## 3. Animation

### 3.1 The named config set

Source: `src/modules/function-control-bar/controls/home` constants module (mod 1212).
These drive the **icon / label / badge** of every bar button.

| Constant | Duration | Delay | Easing | Property |
|---|---|---|---|---|
| `ANIM_CONFIG_DEFAULT` | 300 ms | — | `easeOutBlastPS` | opacity (native driver) |
| `ANIM_CONFIG_FADE_IN_DELAY` | 250 ms | 50 ms | `easeOutBlastPS` | opacity (native driver) |
| `ANIM_CONFIG_FADE_OUT` | 100 ms | — | `linear` | opacity (native driver) |
| `ANIM_ON_VISIBLE_FADE_IN_DELAY` | — | 100 ms | — | extra delay added to icon + badge when a control becomes visible |
| `LAYOUT_ANIM_FADE_OUT` | 100 ms | — | `linear` | layout-animation `delete` on opacity |
| `LAYOUT_ANIM_POSITION_CHANGE` | 300 ms | — | `easeOutBlastPS` | layout-animation `update` on scaleXY |

Selection rule (mod 56): a target opacity **> 0** uses `ANIM_CONFIG_DEFAULT` for the
icon and `ANIM_CONFIG_FADE_IN_DELAY` for the label and the badge; a target of **0**
always uses `ANIM_CONFIG_FADE_OUT`. Showing/hiding a control also fires
`LAYOUT_ANIM_POSITION_CHANGE` so the neighbouring buttons slide/scale into their new
positions over 300 ms.

### 3.2 Button opacity state table (mod 1212)

| State | Icon | Label | Badge |
|---|---|---|---|
| inactive (bar not active) | `0.48` | `0` | `1` |
| active (bar active, not focused) | `0.96` | `0` | `1` |
| focused | `1` | `1` | `1` |
| selected (its panel is open) | `1` | `0` | `1` |
| sibling selected (another panel open) | `0.16` | `0` | `0.16` |
| hidden | `0` | `0` | `0` |
| battery badge, control idle | — | — | `0.48` |

A badge is forced to `0` whenever its icon resolves to `0`. This 0.16 "sibling"
value *is* the Control Center's dimming — there is no scrim over the bar.

### 3.3 Overlay enter / exit

Source: shared animation hooks in mod 724 (`useFunctionControlAnimation`,
`useClockAnimation`, `CLOSE_ANIMATION_DURATION`), mod 1191 (`useBackgroundAnimation`),
mod 2220 (key-guide), mod 1214 / mod 2218 (modal).

| Surface | Enter | Exit |
|---|---|---|
| Function-control bar | opacity `0 → 1` **and** translateY `20 → 0`, 250 ms `easeOutBlastPS`, delay **0 if app visibility just flipped, else 50 ms** | opacity `→ 0`, translateY `→ 20` (or stay 0 if the app is still visible), 100 ms `linear` |
| Clock strip | opacity `→ 1`, 250 ms `easeOutBlastPS`, same 0/50 ms delay rule | opacity `→ 0`, 100 ms `linear` |
| Gradient backdrop | opacity `→ 1`, 250 ms `easeOutBlastPS`, same 0/50 ms delay rule | opacity `→ 0`, 100 ms `linear` |
| Bar key guide | opacity `→ 1`, 250 ms `easeOutBlastPS`, no delay | opacity `→ 0`, 100 ms `linear` |
| Popup / customize modal (`animationType`) | 250 ms, delay 50 ms, `easeOutBlastPS` | 300 ms, delay 0, `linear` |
| Popup wrapper on app hide | — (reset instantly on show) | opacity `→ 0` and translateY `→ 20`, both 100 ms `linear` |
| Popup contents | opacity `→ 1`, 150 ms `linear` | opacity `→ 0`, 100 ms `linear` |
| `CLOSE_ANIMATION_DURATION` | — | `100` ms — the canonical teardown budget; the transfers control keeps its icon mounted for exactly this long after hide |

The **250 ms + 50 ms delay in / 300 ms linear out** modal pair is byte-identical to
the value already recorded in `ps5-shell-motion.md`, and `easeOutBlastPS` is the same
named curve as Prosperismo's `ShellMotion.EaseOutBlast`. The 0-vs-50 ms delay rule is
new: when the overlay is being *opened* (app visibility just changed) everything
starts immediately; when a sub-state changes while the overlay is already up, the
in-animation is staggered 50 ms.

Multitask positioning mode hides the clock and the backdrop (both fall to the 100 ms
linear exit) while the bar stays put.

### 3.4 Navigator transitions

Source: `packages/function-control-navigation/src/navigation/index.js` (mod 321).

Outer stack (`FunctionControl` ⇄ `ActionCard`):

| Property | Value |
|---|---|
| Duration / easing | 500 ms, `linear` |
| Opacity ramp | at scene offsets `[-1, -0.3, 0, +0.3, +1]` → `[0, 0.011, 1, 0.011, 0]` |
| Scale ramp | at offsets `[-0.3, 0, +0.3]` → `[0.95, 1, 0.95]` |
| Header | none; cards transparent |

Inner (per-control) stack:

| Property | Value |
|---|---|
| Duration / easing | 250 ms, `linear` |
| Container | `backgroundColor #080a0f`, `borderRadius 16`, `containerLightMat: "auto"` |
| Opacity ramp | offsets `[-1, -0.5, 0, +0.5, +1]` → `[0, 0.011, 1, 0.011, 0]` |
| TranslateX ramp | offsets `[-0.5, 0, +0.5]` → `[8, 0, -8]` |

The `0.011` shoulder is deliberate: it keeps the outgoing screen barely non-zero so it
is not unmounted mid-transition.

### 3.5 How the scene behind is dimmed — and how it differs from system modals

**Every** `Modal` the Control Center creates sets `dimBackground: false`
(popup, customize panel, `FocusCapture`). There is no translucent scrim anywhere in
this app. Darkening is achieved with three separate mechanisms:

1. **Full-screen base mat.** The whole CC scene is drawn over a 1920 × 1080 View with
   `baseMat: { type: "fullscreen-gradient" }`, faded in over 250 ms `easeOutBlastPS`
   and out over 100 ms `linear`. This is the CC's own darkening of the game/home
   behind it.
2. **Panel base mat.** Each popup paints its own opaque `PopupBaseMat` — `#080A0F`,
   `borderRadius 16`, `lightMat: "auto"`. A control can opt out with `hideBaseMat`
   (the gaming-lounge control does when it renders its own surface). The customize
   panel instead uses `baseMat: { type: "overlay-panel", position: "bottom", length: 434 }`.
3. **Sibling fade.** Opening a panel drops every other button's icon and badge to
   `0.16` (§3.2) rather than covering them.

Relationship to `ps5-shell-overlays.md`: that document describes the *managed* shell,
where a modal swaps the background layer to a flat basemat tinted `#020408` over
1000 ms. That is the system-wide mechanism used by `SystemModalDialog` scenes. The
Control Center does **not** use it — it is a full-screen RN app that paints its own
gradient base mat with a 250 ms / 100 ms fade, and its panel surfaces are `#080A0F`
(linear-ish 8/10/15), a slightly lighter, bluer cousin of the `#020408` system
basemat. Two different surfaces, two different timings; do not conflate them.

---

## 4. Focus and sound

### 4.1 Focus tree

| Layer | Settings |
|---|---|
| CC root (`FocusLayerPS`) | `focusInBehavior: { type: "lastFocusedItem", initialFocusItem: "action-carousel" }`; owns the Back key |
| Action carousel | named `action-carousel` |
| Function-control bar layer | `focusCustomSettings: { canMoveUp: hasActionCards }` — up is only reachable when at least one action card exists |
| Function-control container | named `function-controls`; `focusInBehavior: { type: "lastFocusedItem", initialFocusItem: "fc-button-<restored id>" }`; `focusCustomSettings: { canMoveLeft: false, canMoveRight: false }`; owns the Options key |
| Each button | named `fc-button-<id>`; `ignoreDimByModal: "childrenAndSelf"` |

**Horizontal wrap.** The leftmost button declares the rightmost as its
`leftCandidate` and vice versa, so a single press wraps around the row. But
`canMoveLeftWithKeyRepeat` is false on the leftmost and `canMoveRightWithKeyRepeat`
is false on the rightmost, so **held** D-pad stops at the ends instead of spinning.

**`ignoreDimByModal: "childrenAndSelf"`** keeps the owning button (and its badge)
undimmed while its own popup modal is up — the framework would otherwise dim
everything under a modal.

**Focus restore.** `lastFocusedId` is written to persistent storage under
`LAST_FOCUSED_FUNCTION_CONTROL_ID` on unmount and read back at startup. On restore,
if the stored control is hidden the first non-hidden control at or after it is used;
if there is none, focus falls back to `home`.

**`FocusCapture`** (`src/modules/deeplink-handler/components/FocusCapture`, mod 2203)
is a transparent, non-dimming `Modal` named `FocusCapture` that is mounted while a
deeplink is being resolved. Its only job is to hold focus so no real widget grabs it
first; `releaseFocus()` unmounts it once the destination is known, and it re-arms
itself on the app's `willDeactivate` event.

**Other focus behaviour:**
- Showing a popup returns focus to the owning button (`onShow` → `ref.focus()`).
- A button's label is only mounted while focused (marquee state), so labels appear
  and disappear with focus rather than merely fading.
- Home and power labels are edge-clamped: the home label never crosses x = 40, and
  the power label never crosses x = `SYSTEM_WIDTH − 40` = 1880.
- On activation, if the action-card strand is empty (or times out after 500 ms) focus
  is forced onto the function-control bar.
- The bar's key guide ("Customize", Options button) is shown only while the control
  area is active **and** `isCustomFCTouched === false` — a one-time discovery hint.
  Its container is `height 44`, `marginLeft 24`, `marginBottom 30`, `marginRight 32`,
  right-aligned.
- Pressing a control emits a performance mark `fc/<id> launchStart`.

### 4.2 Sound cues

| Cue | Fires when |
|---|---|
| `psfx_open_control_center` | CC activated by deeplink **without** a `resume` parameter (i.e. a fresh open, not a resume) |
| `psfx_close_control_center` | app `willDeactivate` with reason `"PSButton"`; also on Back at the CC root, immediately before `exitApp()` |
| `psfx_open_option_menu` | Options pressed to open a control's options menu; also on mount of the customize panel |
| `psfx_close_option_menu` | Options pressed again to close that menu |
| `psfx_cancel` | Options/Back dismissing the customize panel; Back out of a nested view (music USB folder, track list); Back from a selected or positioning action card |
| `psfx_enter` | opening a deeplinked action card by index; the sound panel's controller-speaker enable/disable rows; the "more/overflow" button |
| `psfx_error` | deeplink target missing or cancelled; selecting an unavailable accessory; a voice-chat action that is currently blocked |
| `psfx_focus_move` | D-pad move while positioning a multitask card; focusing a transfers row |
| `psfx_tts_cannot_move_focus` | screen reader on and a D-pad direction in PinP positioning mode has nowhere to go |
| `psfx_button_for_negative_in_dialog` | Enter on a multitask-positioning card that is pending removal |
| `psfx_start_p_in_p_split_screen` | confirming a PinP / split-screen placement |

Note the asymmetry: the **open** cue is only played for a cold deeplink open, while
the **close** cue plays on both the PS-button path and the Back-at-root path.

### 4.3 Action-card event vocabulary

`AC_EVENT` (mod 210) — the input events the card layer consumes:
`focus`, `blur`, `enter`, `back`, `left`, `right`, `up`, `down`, `pinp`,
`split-screen`, `background`, `move-multitask-card`, `retrieve-multitask-card`,
`revert-active-multitask-card`.

`AC_MODE` (mod 96) = `glance`, `focused`, `selected-loading`, `selected`, plus the multitask
modes: `pinp-positioning-{top,center,bottom}-{left,center,right}` (no `center-center`),
`pinp`, `split-screen-positioning-{left,right}`, `split-screen`, `background`.
Defaults: `pinp-positioning-center-left` and `split-screen-positioning-left`.

---

## 5. `function-control-notification-list/src/components/OptionsMenu.js`

The brief expected `DEFAULT_MENU_WIDTH` / `LIST_ITEM_HEIGHT` metrics here. **They are
not there.** This file (mod 710) is a thin, geometry-free wrapper:

- Its only style is a container with `height: "100%"`.
- It renders a native `OptionsMenuPS` beside its children and opens it on an Options
  key event whose `keyEventType` is `"Down"`; it closes on the menu's own close request.
- The target is either the whole child (`targetWholeChild` → the child's node handle)
  or `{ nodeHandle, childTargetName }` for a named sub-widget.
- It supports two item sets: `contextItems` (target-specific) and `globalItems`.

So all menu sizing is delegated to the platform widget. The values named in the brief
live elsewhere: `DEFAULT_MENU_WIDTH = 652` in the shared popup style module (mod 528),
panel `LIST_ITEM_HEIGHT = 98` in the function-control list-item module (mod 240), and
`LIST_ITEM_HEIGHT = 146` in the transfers row module (mod 112).

The notification-list package does have two *other* options-menu users —
`NotificationListContent/OptionsMenuForList.js` and
`NotificationDetailCard/OptionsMenuForDetailCard.js` — both of which are also item-list
providers, not layout owners.

---

## 6. Values summary (for reimplementation)

| Concern | Value | Source |
|---|---|---|
| Design space | 1920 × 1080 | mod 1184, mod 319 |
| Side margin | 84 px | `SYSTEM_MARGIN`, mod 319 |
| Bar height | 147 px, right-anchored, at `bottom: 0` | mod 220 / 319 / 2225 |
| Button cell | 112 × 147; icon 48 × 48 at `marginTop 54`; hit area 56 × 56 | mod 220 |
| Button label | absolute band 368 × 34 at top of cell, bottom-aligned, marquee | mod 220 |
| Card carousel | `left 84`, `bottom 190`, `height 520` | mod 2225 |
| Panel width | 652 default, 784 wide variant | mod 528 |
| Panel height | auto, clamped 216 … 810 | mod 241 |
| Panel bottom | 190 px, `paddingBottom 8` | mod 528 |
| Panel surface | `#080A0F`, `borderRadius 16`, `lightMat auto` | mod 528 / 1213 |
| Panel header | 80 px, `padding 24`, `opacity 0.7` | mod 347 |
| Panel row | `minHeight 98`; left icon 56 @ 16/20 margins; separator inset 92/16 | mod 240 |
| Customize panel | 1920 × 434 at `bottom 0`, overlay-panel base mat | mod 2216/2217 |
| Overlay in | 250 ms `easeOutBlastPS`, +50 ms delay if already visible, opacity + translateY 20 → 0 | mod 724 |
| Overlay out | 100 ms `linear`, opacity + translateY 0 → 20 | mod 724 |
| Modal show / hide | 250 ms (+50 ms) `easeOutBlastPS` / 300 ms `linear` | mod 1214, mod 2218 |
| Popup contents in / out | 150 ms / 100 ms `linear` | mod 1214 |
| Icon state change | 300 ms `easeOutBlastPS` in, 100 ms `linear` out | mod 1212 |
| Label / badge state change | 250 ms + 50 ms `easeOutBlastPS` in, 100 ms `linear` out | mod 1212 |
| Reflow after show/hide | 300 ms `easeOutBlastPS` on scaleXY | mod 1212 |
| Dim of unselected controls | icon + badge opacity 0.16 | mod 1212 |
| Idle / active / focused icon | 0.48 / 0.96 / 1.0 | mod 1212 |
| Outer navigator | 500 ms `linear`, scale 0.95 ↔ 1 | mod 321 |
| Inner navigator | 250 ms `linear`, translateX ±8 | mod 321 |
| Teardown budget | 100 ms (`CLOSE_ANIMATION_DURATION`) | mod 724 |
| Controls | 16, right-anchored, power rightmost, home leftmost | mod 527 / 216 |

Easing identities used: **`easeOutBlastPS`** and **`linear`** only. No spring
constants, no bezier control points anywhere in this bundle.

---

## 7. Gaps / not located

- **`easeOutBlastPS` curve definition.** The Control Center only *names* the curve;
  its implementation lives in the RN platform layer (`Easing`), not in this bundle.
  Prosperismo's existing `ShellMotion.EaseOutBlast` (r = 10) is the working stand-in,
  but the exact coefficients are not confirmed from this source.
- **`easeOutBreezePS`.** Not referenced anywhere in the Control Center bundle. If the
  home shell uses it, it is not part of CC motion.
- **`BUTTON_CONTAINER_MARGIN = 64`.** Exported by the button style module but never
  read in this bundle. Its intended role (probably inter-button gap in an earlier
  layout, or a value consumed by another bundle) is unresolved.
- **Base-mat rendering.** `baseMat`, `lightMat` and `containerLightMat` are props
  consumed by the native RN-PS view layer. The gradient stops of
  `fullscreen-gradient`, the geometry of `overlay-panel`, and what `lightMat: "auto"`
  resolves to are **not** in JavaScript. Value not located.
- **Focus ring visuals.** As in `ps5-shell-overlays.md`, the selection highlight is a
  widget-toolkit feature (`FocusLayerPS`, `Button backgroundVisibility`); its colour,
  radius and glow are not in this bundle.
- **Font size tokens.** `SizeLarge` / `SizeNormal` / `SizeSmall` / `SizeXSmall` /
  `Size3XSmall` are theme tokens; their pixel values are in the platform theme, not
  here. Font-scaling levels observed: `small`, `normal`, `large`, `veryLarge`.
- **Bar left edge.** Derived (128 px with all 16 controls) rather than authored; the
  row is right-anchored, so the left edge moves with the visible-control count. No
  explicit left-edge constant exists.
- **Action-card geometry.** Only the carousel's outer frame (`left 84`, `bottom 190`,
  `height 520`) was extracted. Individual card sizes, PinP/split-screen target rects
  and the multitask positioning grid were out of scope for this pass.
- **Firmware drift.** All values are from firmware 3.00 (build string
  `releases_03.00`). They were not cross-checked against 4.03 or 9.00 bundles.
