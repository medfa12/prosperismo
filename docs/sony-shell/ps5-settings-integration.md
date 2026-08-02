# PS5 Settings recovery and integration map

## Product-content boundary

The shipped Sony-mode Settings surface uses NPXS40008's recovered presentation
contract—1920x1080 frame, vertical tab geometry, list focus/back behavior and
firmware icon plumbing—but it does **not**
expose or imitate the console's registry settings. Its content is Prosperismo's:
General, Graphics, Audio and Interface, Emulation, Logging, Environment, and
About. The retail hierarchy below remains research evidence for understanding
the widgets and routes; it is not the product's menu model.

This is the evidence boundary for integrating Settings into the console-style
shell. It does not treat the desktop Options editor as PS5 UI, and it does not
copy Sony source or assets into the repository.

## Sources and version boundary

| Evidence | Local external path | What it proves |
|---|---|---|
| Readable RN bundle | `C:\sharpemu\games\useful rnps\readable_js_3.00\NPXS40008.js` | Settings app structure and values for firmware 3.00; 196,610 lines |
| 4.03 manifest | `C:\sharpemu\games\PS5_4.03_reconstructed\filesystems\system_ex\rnps\apps\NPXS40008\manifest.json` | `rnps-settings`, version `4.0.0+19589`, RNPS `0.59.6-683.36`, Twin Turbo |
| 4.03 signed bundle | sibling `application.ps.bundle` | The actual 4.03 program input, 4,734,384 bytes; still opaque to the host preview |
| 4.03 loose assets | sibling `assets/` | 78 PNG and 90 `.resource` files, kept external and loaded only in place |
| Shared UI3 art | `filesystems/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco` | Category icon sources such as `iconid_network`, `iconid_system`, and `iconid_storage` |

The important limitation is temporal: the complete readable program is 3.00,
while the installed asset set and signed application are 4.03. Values below are
therefore exact for 3.00 and only candidates for 4.03 until the latter bundle is
decrypted or captured. The 4.03 assets prove that the application and its
specialized imagery remained present, not that every 3.00 pixel value survived.

## Top-level hierarchy

The retail category model is a literal array in `NPXS40008.js` module 933,
lines 95630-95802. This is its exact order and routing:

| # | Item id | English label | Icon id | Target |
|---:|---|---|---|---|
| 1 | `id_users_guide_legal` | User's Guide, Health and Safety, and Other Information | `users_guide_health_and_safety` | `UserGuideScreen` |
| 2 | `id_accessibility` | Accessibility | `accessibility` | `AccessibilityScreen` |
| 3 | `id_network` | Network | `network` | `NetworkScreen` |
| 4 | `id_account_management` | Users and Accounts | native avatar, not an IconPS id | `pssettings:accounts?entry=settings` |
| 5 | `id_family_parental_controls` | Family and Parental Controls | `family` | `pssettings:accounts?entry=family` |
| 6 | `id_system` | System | `system` | `SystemScreen` |
| 7 | `id_storage` | Storage | `storage` | `StorageScreen` |
| 8 | `id_sound` | Sound | `sound_speaking` | `SoundScreen` |
| 9 | `id_screen_video_playback` | Screen and Video | `screen_and_video` | `ScreenVideoPlaybackScreen` |
| 10 | `id_accessories` | Accessories | `devices` | `DevicesScreen` |
| 11 | `id_general_games_apps` | Saved Data and Game/App Settings | `games_and_apps` | `GamesAppsScreen` |
| 12 | `id_notification` | Notifications | `notification` | `NotificationsScreen` |
| 13 | `id_share_broadcast` | Captures and Broadcasts | `captures_and_broadcasts` | `SharingBroadcastsScreen` |

The account row is patched after model creation with a 48x48 launched-user
avatar (`NPXS40008.js` lines 88063-88075). A hollow unresolved marker in the
host is intentional until a user/avatar provider exists.

The readable bundle also exposes representative child screens through its
route headers: Accessibility -> Closed Captions, Custom Button Assignments,
Color Correction (102485-102532); System -> System Information, System
Software Update Settings, Error History, Remote Play connection/linking,
Backup and Restore, Initialization, Language, Date and Time, Power Save
(118689-137282); Storage -> delete/manage game data (146951-172643); Games/App
Settings -> Game Presets, subtitle/audio, first-person and third-person view,
auto-update, spoiler warning, game status and followed games
(175484-175542); Screen and Video -> Video Output Information and Language
(186244-186301); Captures and Broadcasts -> Audio, Camera, Overlays and Chat to
Speech (191295-191350). This is a route hierarchy, not a claim that every child
screen is implemented.

## Layout and typography

`SettingsList/StyleValues` in modules 24 and 76 gives the fixed 1920x1080
frame. These values are exact for the readable bundle:

| Property | Value | Source |
|---|---:|---|
| list top / left | 186 / 304 | lines 1801-1804 |
| list width / height | 1312 / 894 | lines 1804-1805 |
| main-list item margin | 0 | lines 1809-1810 |
| focus margin | 3 | lines 1817, 8798 |
| list-item focus top / bottom expansion | 3 / 5 | native PUI constants already represented by `ShellFocusRingTimeline.ApplyListItemStyle` |
| image-to-text margin | 20 | line 8800 |
| title margins top / bottom / right / left | 27 / 27 / 48 / 16 | `LongTextListItem`, module 190 |
| title type token | `SizeNormal` | module 190 |
| value type token / opacity | `SizeXSmall` / 0.7 | module 190 |
| popup maximum height / bottom margin | 504 / 48 | module 257 |

The `152` row height in module 76 belongs to the saved-data/game-title list
family, not the initial category screen. The initial screen delegates row
height, internal padding, font binding and rendered icon size to native
`MenuListItemPS`; those values are not recoverable from this JavaScript alone.
The current host visualization therefore treats its row pitch and tab packing
as diagnostic scaffolding, not recovered Sony metrics. Its title/value type
tokens and margins come specifically from `LongTextListItem` module 190; they
are Sony-widget reuse, not proof that native `MenuListItemPS` shares them.

The only directly supported dark plate literal in this Settings bundle is
`#020408` with radius 16 for a preview plate (module 410). The full native
Settings background/theme, row fill, separator treatment and blur remain
unknown. The integration therefore keeps the firmware-backed shell plate
without a guessed Settings-only opacity overlay and draws no invented row cards
or separators. Focus uses PUI's recovered global 3 px line and 3 px outside
offset at unity scale; NPXS40008 exposes no thinner Settings/icon override.

## Navigation and focus behavior

- The categories container passes `initialScrollIndex: 0` and initial focus
  `id_network` into `SettingsList` (`NPXS40008.js` lines 86216-86325).
- A tabbed route does not leave focus parked in the left tab column. Module 73
  sets `_isSetFocusOnPanel` when `initialFocusTab` is present and, after the
  panel mounts, calls `TabViewPS.setFocusOnPanel()`. The host now follows that
  route-entry behavior: the selected tab remains visible on the left while the
  first emulator-setting row owns the travelling focus highlight.
- Focus is restored by item id through `setFocusTo`; when system language
  changes, the forced return target is `id_system` (86235-86243).
- Up/down are owned by native `ListViewPS`; the host currently mirrors them as
  clamped one-row moves. Its viewport packing remains diagnostic until the
  native row metric is measured.
- Activate dispatches the row target. The Screen and Video route first checks
  Remote Play and Share Play and may show `0x80EC0405`/`0x80EC0406`
  (`86225-86234`). The host does not reproduce those service checks yet.
- Accounts and Family are deep links into the account Settings application,
  not children rendered by NPXS40008.
- Select mode moves the list from x304/1312 wide to x172/1092 wide over 250 ms
  with exponential easing, then fades the 388x72 action buttons over 200 ms
  (SettingsList module 18). That mode is outside the first slice.
- The product's bounded two-level route stack is recreated: Back/Circle from a
  detail panel restores its category list and Back/Circle from that root route
  restores Home (including Home's remembered focus region/item). Deeper native
  RN route-stack restoration remains outside this Prosperismo-only slice.

## Legacy Options migration map

| Existing Prosperismo control/group | Sony destination | Migration decision |
|---|---|---|
| Internal render resolution | Screen and Video | Conceptual match only; remains desktop-only until a native-shaped detail screen exists |
| Title/menu music and UI sounds | Sound | Conceptual match; remains desktop-only until a native-shaped detail screen exists |
| Language | System -> Language | Future native-shaped detail row; remains in legacy General now |
| Boot sequence / background motion | System or Accessibility has no exact matching setting | Keep desktop-only until an exact route is found |
| CPU engine, strict dynlib, import tracing, logging | none; emulator developer controls | Permanently desktop-only |
| Environment-variable switches | none | Permanently desktop-only Environment tab |
| Discord, updater, project links | none | Permanently desktop-only |
| Per-game emulator overrides | Saved Data and Game/App Settings is only a conceptual parent | Do not label them Sony settings; keep the existing game context dialog |

The integrated seam follows that table. In Sony mode the gear opens the new
category list, and category activation never reveals the legacy editor. F10 is
also ignored on this surface. Native-shaped detail screens will reuse the
emulator settings model later without exposing desktop chrome. `SHARPEMU_UI_MODE=
desktop` continues to expose the complete ordinary desktop Options editor.

Settings is not a separate visual application in the product architecture. It
is a route inside the reactive shell defined by `ps5-reactive-shell.md`: the
same background owner, global state, focus line/area passes, navigation model,
and transition system remain active while only the content model changes to
SharpEmu controls.

This shared plate is not merely a product simplification. In every decompiled
managed BGLayer version with IL (1.12, 2.00, 3.00, 4.03, 5.00, 6.00 and 6.50),
`PresetColourIndex` is assigned HomeScreen `4` once in `Start()` and never
changes, while `BGLayerPlugin.SetPresetColour` has an empty body. Opening
Settings therefore does not justify selecting a Settings-only palette or
particle state. The native selector does contain a System Area preset (`2`),
but in normal mode it maps through state `4` to the same Plane2 record `2` as
Home (`4 -> 5 -> 2`). It is evidence about the renderer's state space, not
evidence that NPXS40008 switches to it on route entry.

The separate ShellCore `SystemBGState` is not such a switch either.
`ApplicationContainerScene::UpdateSystemBg` treats OFF (`0`), ON (`1`) and
DEFAULT (`3`) solely as background visibility/default gating, then notifies
`SystemBGMediator`. The subscribed `PLayerTransition` initializes its background
request with HomeScreen preset `4`. This closes the known Legacy Settings path
at the same Home record rather than leaving a plausible but unproven palette
change.

The product compositor now encodes that distinction directly. Its
`ShellLayerBackgroundTransition` model preserves the 4.03 ABI values for
transition type and degree, the primary/fallback/blur/overlay image slots, and
the optional basemat request. Applying `SystemDefault`, `CustomImageFade`, or
the HOME slide routes changes only the image/basemat channel; the translated
native selector remains HomeScreen preset 4 -> state 5 -> Plane2 record 2 and
continues rendering underneath. Ripple remains rejected at the render boundary
instead of being replaced with host motion.

The explicit `CustomImageFade` route now also uses the recovered native 4.03
clock: `300 + degree * 166.6666717529297` milliseconds (300, 466.6667,
633.3333, or 800 ms after native tick rounding). `cross_fade_p` samples the old
and new textures at the same UV and linearly interpolates them with native
`progress = min(elapsed / duration, 1)`; no host easing is applied. HOME's
ordinary caller is now identified independently: NPXS40002 module 196 maps
strand direction to `SlideInLeft`, `SlideInRight`, or `Fade`, and module 511
always supplies degree `Normal`. The opaque title-art path executes the
recovered slide mask and UV equations; optional gradation/transparent-alpha
behavior remains outside that proof.

## Implemented slice and remaining gaps

Implemented in `ShellSettingsCategoryList` and `ShellSettingsDetailList`:

- exact recovered frame with a 102-unit capture-measured category-row pitch
  that remains separate from recovered-code metrics;
- the module-24 two-unit row separator and the retail-frame title/icon inset;
- a 110-unit capture-measured vertical-tab pitch, plus module-73's initial
  focus transfer into the mounted content panel;
- Prosperismo category order and settings content, using firmware icon ids only
  as presentation assets;
- runtime UI3 vectors from the user's firmware dump;
- keyboard and pointer selection, clamped scrolling, back routing and shared
  single focus renderer;
- controller D-pad/stick traversal, Cross activation and Circle back on both
  the category and detail focus layers, using the same clamped list semantics;
- functional Prosperismo controls for resolution, language cycling, audio/UI
  options, launch behavior, logging, diagnostics and environment switches;
- hard-separated Sony and desktop presentation paths in `MainWindow`;
- the shared `ShellBackground` composite on both Home and Settings, so title
  art, motion enablement, global-state routing and the native particle renderer
  have one owner instead of the UI bypassing them through a plate-only control;
- a translated Plane2 `wave_bg_p` record-2 path whose noise phase advances once
  per rendered frame; the steady Home/Settings route is proven as HomeScreen
  preset 4 -> native node state 5 -> Plane2 record 2, so the earlier capture
  mismatch belongs to composition/layout rather than record selection;
- metric/order regression tests and a headless `shell-shot --scene settings`
  capture target.

Not yet implemented: the native `SettingsListPS` implementation itself,
select-mode, native popup/dropdown overlays, exact 4.03-vs-3.00 geometry,
accessibility speech ordering, and the remaining emitting particle states
beyond the firmware's steady `NoParticle` route. The
desktop Options editor remains available only in desktop presentation mode as
the conventional host UI; Sony mode never exposes it.
