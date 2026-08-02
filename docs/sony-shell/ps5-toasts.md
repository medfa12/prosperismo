# PS5 toast / notification system — values reference

Behavioural and layout values recovered from the system software 3.00 React Native shell
(control-center bundle, title id NPXS40003). All source locators are given as
`m<id>` = module id inside that bundle plus the original package path embedded in the
bundle's debug metadata. No code is reproduced here, values and behaviour only.

Packages involved:

- `@rnps-ppr/notification-view-template` — the shared toast/notification renderer
- `@rnps-ppr/ui-shared-utilities-notification` — `InAppToast` / `PersistentToast` overlay pills
- `apps/control-center/packages/function-control-notification-list` — notification list UI

---

## 1. Taxonomy

Two rendering families, one template registry (m109, `getTemplate`/`getComponent`):

| Family | Base component | Marker | Behaviour |
|---|---|---|---|
| Informative | `InformativeToastBasic` (m361, `components/InformativeToastBasic/index.js`) | no `isInteractive` | header only (icon + text), never expands, no CTA |
| Interactive | `InteractiveToastBasic` (m41, `components/InteractiveToastBasic/index.js`) | `isInteractive = true` on the template | collapsed header that can expand in place to a panel with CTA buttons (state machine in section 3) |

View-template types registered in m109 (name → wraps):

| Template type | Renders | Notes |
|---|---|---|
| `Toast` | InformativeToastBasic, TemplateA | default informative |
| `ToastTemplateA` / `ToastTemplateB` | InformativeToastBasic, TemplateA / TemplateB | |
| `InteractiveToast`, `InteractiveToastTemplateA`, `InteractiveToastTemplateB` | InteractiveToastBasic, TemplateA / TemplateB | generic interactive |
| `InteractiveToastMessages` (m252) | message received / group joined / "you added" bodies, image+video attachment preview | |
| `InteractiveToastTrophy` (m1771) | trophy unlock. Two use cases: `NUC55` = single trophy unlock (trophy image 128x128 + grade icon 64x64 + name, description up to 5 lines), `NUC273` = trophy-set progress (trophies icon 64x64 + set name, message up to 7 lines, progress row with 64x64 set icon) | grade icon id comes from model (`trophy.gradeIcon`) |
| `InteractiveToastFriendRequest` (m1898) | incoming friend request | |
| `InteractiveToastSharePlay` (m1884) / `InteractiveToastScreenShare` (m1883) | Share Play / screen share invitations | |
| `InteractiveToastVoiceChat` (m1911) | voice chat invite, voice-message-full indication | |
| `InteractiveToastSaveDataMessage` (m1921) | save-data transfer message (own header + expanded body) | |
| `InteractiveToastPlayerSessionInvitation` (m1772) / `InteractiveToastLegacySessionInvitation` (m1880) / `InteractiveToastPlayerSessionRequestToJoin` (m1924) | PS5 session invite / PS4-era session invite / request-to-join | |
| `InteractiveToastActivityChallenges` (m1906) | activity challenge updates | |
| `InteractiveToastPSNowPlayer` (m1918) | PS Now player action | |
| `InteractiveToastSummary` (m1886) | "N notifications" summary toast with an embedded mini notification list | list height = interactive max height − 2 x 16 − text height |
| also registered | `InteractiveToastGamePreparation`, `InteractiveToastFriendAvailable`, `InteractiveToastGameToPlayer`, `InteractiveToastSystemMessage`, `InteractiveToastSuppressionOnboarding`, `IconOnlyToast`, `CTASample`, `MessageReplySample`, `UnmSample`, `HandoverIndicator`, `DeviceConnectionIndicator`, `VolumeIndicator`, `MixSliderIndicator` | indicators share the same template pipeline |

Components registry (same module): `ToastHeaderBasic` (m244), `NotificationListView` (m699),
`NotificationDetailCard` (m701), `ContentErrorMessage` (m133), `SystemMessageActions` (m705),
`SystemMessageScreenTitle` (m704).

### Header templates (`ToastHeaderBasic`, m244 dispatcher)

- **TemplateA** (m618): icon left, then up to 3 stacked texts — primary (font `SizeNormal`,
  min-height = icon 64 + 2 x (24 − 16)), secondary and tertiary (font `SizeXSmall`, opacity 0.7,
  4 px above-gap). Supports a dual-icon row (second icon 8 px left margin).
- **TemplateB** (m620): sub-message line drawn *above* the main message
  (sub `SizeXSmall` at 0.7, main `SizeNormal`), tertiary line below; always 1 line for sub.
- **VrTemplateA–E** (m1741 region, `ToastHeaderBasic/VrTemplateX.js`): VR variants; a template
  name starting with `Vr` is VR3D (m129). Selected when `model.platformViews.virtualReality3D`
  exists and focus mode is `VR` (m130). VR icon size 72x72.

### Text line budget (m354)

Total lines = 3 in list mode, else 5 (6 with large text). TemplateB: secondary fixed at 1.
Tertiary = 1 only when an extra message exists. TemplateA computes secondary =
clamp(1..2, budget − ceil(primaryHeight / textHeight) − tertiary).

### Display surfaces

`displayType`: `"Toast"` (popup surface, the default), `"List"` (notification list rows,
`ToastHeaderForList` m~2010 adds timestamp + "new" dot), `"ListOnSummaryToast"` (rows inside a
summary toast). Same templates render on all three surfaces.

### Model-driven behaviour

- Channel type → toast icon id (new-notification pill, `InAppToastNewNotification.js`):
  `WhenFriendsGoOnline→friend_online`, `GameContentAnnouncement→game_alerts`,
  `GameInvites→send_invitation`, `Trophies→trophies`, `ActivityChallenges→challenges`,
  `FriendRequest→add_friend`, `Messages→messages`, `Party→party`, `Accolades→accolades`,
  `MusicTrackChange→music`, `Downloads→download`, `Uploads→upload`, `WishlistItems→favorite`,
  `FromPlayStation/PlayStationSeasonPass→from_ps`, `PlayStationPlus→ps_plus`,
  `PlayStationStore→ps_store`, `PlayStationNow→ps_now`, `PlayStationMusic→ps_music`,
  `EAAccess/EaPlay→3rd_party_ea`, `FamilyActivity→family`, `PlayStationSafety→security`,
  `SystemError/ServiceError→error_message_caution`.
- Toast replacement (m130 `checkToastReplaceable`): a new toast replaces the one on screen only
  for the same user, when ids match, or when its `toastOverwriteType` is `"Always"` (or matches
  the current context) and `bundleName` (or `useCaseId` when no bundle name) matches.
- `platformViews.previewDisabled`: forces TemplateA with the preview-disabled view data
  (content hidden until expanded).
- Expansion is disabled when `settings.iduMode === 1` (kiosk/demo units) (m~1645).

### Overlay pills (`ui-shared-utilities-notification`)

- **InAppToast** (m1934): transient pill; icon and/or 1–2-line message; auto-dismisses
  (section 3); positions `TOP` / `CENTER` / `BOTTOM` / custom; used e.g. for
  "new notification" while the list is open (background `rgba(255,255,255,0.04)`, text
  `msgid_new_notification`).
- **PersistentToast** (m1936): static status chip, `persistent` — no timeout, background
  `#11141A`, up to 2 icons + text.

---

## 2. Geometry

### LAYOUT_VALUES (m17, `notification-view-template/src/utils/layoutUtil.js`)

| Property | Value |
|---|---|
| toastMaxWidth | 652 (784 for large text) |
| toastMinWidth / toastMinHeight | 80 / 74 |
| informativeToastMaxHeight | 242 (416 large text) |
| interactiveToastMaxHeight | 690 (596 large text; large-text multi-login: 556 − 40 = 516) |
| toastContentMarginHorizontal | 24 |
| iconWidth x iconHeight | 64 x 64 (landscape art width 114; VR 72 x 72) |
| iconMarginVertical | 24 |
| iconToTextMarginHorizontal | 20 |
| dualIconMarginHorizontal | 8 |
| textMarginVertical | 16 |
| textToTextMarginVertical | 4 |
| textHeight / largeTextHeight | 42 / 62 (primary line height 42) |
| expandedBodyDefaultMarginTop / MarginBottom | 8 / 32 |
| ctaDefaultHeight | 72 |
| ctaMarginHorizontal | 32 |
| userHeaderHeight | 48 (marginTopDiffForUserHeader 8 → additional height 40) |
| psButtonWidth x psButtonHeight | 48 x 48 (marginLeft 18, marginRight 26, marginTop 32) |
| listMarginHorizontal / listItemMargin | 8 / 0 |
| listTextMarginVertical | 24 (list rows get extra top pad 24 − 16 = 8, bottom 8 + 2) |
| listItemSeparatorHeight | 2 |
| summaryToastListMarginVertical | 16 |
| font scaling clamp | min "small", max "large" |
| collapsed header min height | icon 64 + 2 x 24 margins = 112 (scroll view minHeight, m41) |

Derived: list width = toast max width − 2 x 8; summary list height = interactive max height −
2 x 16 − text height.

### List surface extras (`ToastHeaderForList.js`)

Timestamp: font `Size3XSmall`, opacity 0.7, top margin 24, left margin 24, min/max width
48/80 (64/128 large text). "New" indicator icon 24 x 24, margins 4 left / 10 right.

### Trophy expanded panel (m1771)

Panel width = toast max width; body margins 32 top / 48 bottom / 24 horizontal; CTA block
margins 32 bottom / 32 horizontal. Trophy image 128x128; unlocked-grade icon 64x64
(16 left margin); trophy name `SizeNormal` (12 left margin); description `SizeXSmall`;
progress row: 64x64 set icon, progress text `SizeNormal` 20 left margin.

### InAppToast pill (m1934/m1935)

- Text+icon layout: icon 40x40, padding left 20 / right 24 / vertical 16, icon-to-text 16,
  text `SizeSmall`, max 2 lines (clip + 1 line in sized presets).
- Size presets: SMALL wrapper radius 20, inner 40x40, icon 26x26; MEDIUM radius 48,
  inner 96x96, icon 64 (44 with label, label `Size3XSmall` bold, max width 70); LARGE radius 72,
  inner 144x144, icon 96 (64 with label, label `Size2XSmall` bold, max width 126).
- Container `alignSelf: center`, `position: absolute`; POSITION.TOP `top: 0`,
  BOTTOM `bottom: 0`, CENTER vertically centered.

### PersistentToast chip (m1936)

min 80 wide / 64 tall, max 784 x 308; background `#11141A`; icons 40x40 (8 gap, vertical
margin 12 single-line / 16 multi-line); padding: icons-only 20 h; icons+text 20 left 24 right;
text-only 24 h; text `SizeXSmall` `#FFFFFF` (minimal variant: `Size2XSmall` bold tabular);
text max width 692 (one icon) / 644 (two icons).

### Anchor constants — clarification

`AC_ANCHORS` / `AC_OFFSETS` / `AC_DIMENSIONS` / `NO_OFFSET` / `ANCHOR_*` /
`PINP_BORDER_INOUT` (m69) and `MULTITASK_AC_MODE` / `AC_MODE` (m96) belong to the
**activity-card / multitask (PinP, split-screen) system, not to toasts**:
anchors are the 9 vertical x horizontal combos (default `ANCHOR_BOTTOM_LEFT`), offsets all
`{x:0, y:0}`, PinP border 2, card dimensions GLANCE 360x400, FOCUSED 432x520,
SELECTED 464x810, PINP 464x261, SPLIT_SCREEN 464x810. Recorded here to close the loop;
do not use them for toast placement. Where the shell pins the popup toast on screen is decided
by the native host, not this bundle (see gaps).

---

## 3. Lifecycle and animation

### States

`TOAST_STATE` (m251, interactive toast): `collapsed 1, collapsedToExpanded 2, expanded 3,
expandedToDetailView 4, detailView 5, detailViewToExpanded 6, collapsedSingle 7,
collapsedToDetailViewSingle 8, detailViewSingle 9`. Transitions: collapsed → expanded →
detailView and back; a single-notification summary goes collapsedSingle → detailViewSingle
directly. On the popup surface (`displayType === "Toast"`) the expand transition is animated;
on the list surface the toast jumps straight to expanded.

`ITEM_STATE` (list rows, `NotificationListItem.js`): `resting 1, focused 2, selected 3`.

### Transition animation (m251 `startAnimation`)

Each transition sets the intermediate `*To*` state, runs the group below, then lands on the
target state. All parts run in parallel:

| Part | Duration | Easing | Notes |
|---|---|---|---|
| container resize (width+height interpolate old→new) | 200 ms | `easeOutBreezePS` | optional start delay: call sites use 0 / 50 / 100 ms |
| outgoing UI opacity 1→0 | 150 ms | linear | starts at delay |
| incoming UI opacity 0→1 | 150 ms | linear | starts at delay + 150 ms (cross-fade is sequential) |
| additional UI (timestamp etc.) | 150 ms | linear | when present |

`easeOutBreezePS` / `easeOutBlastPS` are the shell's standard eased-out curves (previously
recovered: EaseOutBreeze r≈4.6, EaseOutBlast r=10; shell default timing 300 ms).

### InAppToast pill lifecycle (m1934)

enter: opacity 0→1, 300 ms linear → dwell: `timeout` prop, **default 3500 ms** (timer skipped
when `persistent`) → exit: opacity 1→0, 200 ms linear, then `onClose`. Re-showing new content
resets the timer. No translation/slide is applied in the RN layer.

### Severity

`ERROR_SEVERITY` = `critical / major / minor / normal / info` (default for reported errors:
major). In this bundle it classifies telemetry error events and the EMS error-code table; it
does **not** restyle toast chrome. Error presentation constants that do exist (EMS module):
error background `rgba(50,0,0,0.75)`, error text `rgba(255,200,200,1)`; error-channel
notifications (`SystemError`/`ServiceError`) use icon `error_message_caution`.

---

## 4. Sound mapping

An exhaustive scan of all 28 decrypted 3.00 bundles found **no `snd_*` references and no
toast-related `psfx_*` calls** — the RN layer never plays toast audio; the native shell plays
the cue when it posts the toast. The mapping below is therefore inferred from the cue names
and the taxonomy above (confidence: name-based, not code-proven):

| Toast type | Sound cue |
|---|---|
| informative toast (`Toast`/`ToastTemplateA`/`ToastTemplateB`) | `snd_informative_toasts_something_to_read` |
| interactive toast (any `InteractiveToast*`) | `snd_interactive_toasts_something_to_do` |
| error toast (SystemError / ServiceError channel) | `snd_error_toasts_something_is_broken` |
| trophy unlock, bronze/silver/gold (`InteractiveToastTrophy`, NUC55) | `snd_trophy_toast` |
| trophy unlock, platinum | `snd_platinum_trophy_toast` |

UI sounds the notification/control-center UI *does* play itself (`SystemSoundPS.playByID`):
`psfx_open_control_center` / `psfx_close_control_center` on open/close,
`psfx_open_option_menu` / `psfx_close_option_menu` for the notification options menu,
`psfx_error` when an option menu fails to open, `psfx_focus_move`, `psfx_enter`, `psfx_cancel`.

---

## 5. Gaps

- **On-screen placement and entry motion of the popup toast**: the popup host (which corner it
  docks to, slide/scale entry, stacking) lives in the native shell, not in any of the 28
  decrypted RN bundles. Only the fade+resize behaviour of the RN content itself is recoverable.
- **Dwell time of the system popup toast**: the only in-code dwell is the InAppToast pill's
  3500 ms default. The dwell of the shell-level popup (and any per-type overrides, e.g. trophy
  vs informative) is native-side and unconfirmed.
- **Sound trigger point**: inferred (section 4); the native caller could not be inspected here.
- **Per-severity presentation**: no evidence that ERROR_SEVERITY changes toast visuals; treat
  error toasts as normal informative toasts with the caution icon unless native evidence says
  otherwise.
- Font size tokens (`FontSizePS.SizeNormal`, `SizeXSmall`, ...) are referenced by name; their
  pixel values are defined in the shared UI toolkit, not in this bundle.
