# PS5 Home theme values

This is a clean-room value reference recovered primarily from the readable
`ppr_releases_03.00` React Native bundles. One component-geometry row also uses the managed native
PUI assembly from the reconstructed 4.03 filesystem and is marked `PUI-NATIVE`; it must not be
mistaken for a JavaScript theme token.

## Provenance and scope

| ID | Bundle | SHA-256 | Use in this reference |
|---|---|---|---|
| `HOME` | `NPXS40002.js` | `FDEB5EB5D2505DD5052F46FBB5CAF98C98004B620CD3497307A6F6294E864957` | Home shell styles, tile utility palette, Home mat ramp, tag presets, and Home component radii |
| `PEER-03` | `NPXS40003.js` | `C8227D3D4D40D1F43CFE25D4E5F15B3F1E57D31439B76887719BE13164677737` | Card/action-card and shared error colors |
| `PEER-13` | `NPXS40013.js` | `2ABD5666F8DBE76E3197768C2C35CCB0900F2FA58DED3447AB97CEF7AF08AC59` | Shared toast and fallback-surface colors |
| `PEER-15` | `NPXS40015.js` | `34334B7519198989381365FCF4951E169568B956EF53C4CE6874A436EE15B3C3` | Explicit dark tile background and secondary metadata color |
| `PEER-18` | `NPXS40018.js` | `525D4B4C1C262E2C7394579602C311B71398DE2F5562D3FC13A593CF91AFA3EC` | Shared live/VOD status colors |
| `PUI-NATIVE` | `PS5_4.03_reconstructed\filesystems\system_ex\common_ex\lib\Sce.PlayStation.PUI.dll.sprx` | `EC6DAD4C940EE89C78B1B27A859E0AAF8119B8DE476B684C7273A0413F6B55F7` | Native button focus/background radius only |

All 28 `NPXS*.js` files in the same readable-bundle set were searched for
hex, RGB/RGBA, packed numeric colors, theme/token identifiers, and the known
color-theme and shape-theme names. Peer values below are included only when a
nearby identifier gives them a useful UI meaning.

## Core UI summary

| Requested role | Best recovered value | Confidence |
|---|---:|---|
| Background | `#020408` | High as Home dark-base RGB; not proven as `background.enabled` |
| Card/surface | `#080A0F`, `#11141A`, `#17191E` | High for the named components; no single global surface token |
| Text primary | `#FFFFFF` | High |
| Text secondary | `#FFFFFFB3` | High |
| Focus/highlight | unresolved | `#00BAFF` is not present in the JavaScript |
| Accent | `#FFD228` with `#A88644` border | High for gold-line tags only; global accent mapping unresolved |
| Dimmer/scrim | `#000000CC`; media obscure `#0D0D0D99` | High for the named components |
| Danger/error | `#320000BF` background with `#FFC8C8` text | High for the shared error component; global danger-token mapping unresolved |
| Success | unresolved | No defensible production success constant found |

## Recovered dark-shell palette

The table distinguishes direct names from functional interpretations. A
functional interpretation does not establish a mapping to a UI3
`design_tokens.json` key.

| Role | Value | Hex equivalent | Status and provenance |
|---|---:|---:|---|
| Shell dark base | `rgb(2, 4, 8)` | `#020408` | Direct RGB in `HOME`, `useMat` output range; independently explicit in `PEER-15`, `VIEW_ALL_TILE_BACKGROUND` |
| Card background | `rgb(8, 10, 15)` | `#080A0F` | Direct in `PEER-03`, `CARD_BACKGROUND_COLOR` |
| Toast surface | `rgb(17, 20, 26)` | `#11141A` | Direct in `PEER-13`, `PersistentToast.container` |
| Action-card surface | `rgb(23, 25, 30)` | `#17191E` | Direct in `PEER-03`, `actionCardBackgroundColor` |
| Opaque fallback surface | `rgba(48, 48, 48, 0.9)` | `#303030E6` | Direct in `PEER-13`, `PreviewFallbackIcon.fallbackContainer` |
| Tile fallback, dark | `rgba(53, 53, 53, 1)` | `#353535` | Direct in `HOME`, tile utility `COLOR.DARK_GREY` |
| Tile fallback, gray | `rgba(41, 41, 41, 1)` | `#292929` | Direct in `HOME`, tile utility `COLOR.GREY` |
| Primary text/detail | `rgba(255, 255, 255, 1)` | `#FFFFFF` | Direct in `HOME`, tile utility `COLOR.WHITE` and tag `DETAIL` |
| Secondary text/detail | `rgba(255, 255, 255, 0.7)` | `#FFFFFFB3` | Direct in `HOME`, `tagText`; also `PEER-15`, `METADATA_TITLE_COLOR` |
| Disabled/quiet detail | `rgba(255, 255, 255, 0.6)` | `#FFFFFF99` | Direct in shared peers, timeline `NORMAL`; Home also uses opacity `0.7` for `subLabelText` |
| Faint neutral fill | `rgba(255, 255, 255, 0.05)` | `#FFFFFF0D` | Direct in `HOME`, tile utility `COLOR.BLANK` |
| Divider, weak | `rgba(255, 255, 255, 0.1)` | `#FFFFFF1A` | Direct in `HOME`, `lineSeperator` |
| Divider, strong | `rgba(255, 255, 255, 0.25)` | `#FFFFFF40` | Direct in `HOME`, `separatorText` |
| Media obscure layer | `rgba(13, 13, 13, 0.6)` | `#0D0D0D99` | Direct in `HOME`, tile utility `COLOR.OBSCURE` and `mediaOverlay` |
| Modal/tag dark base | `rgba(0, 0, 0, 0.8)` | `#000000CC` | Direct in `HOME`, dark tag `BASEPLANE` and centered modal container |

### Home dark-base mat ramp

`HOME` builds per-tile mats by interpolating the same `rgb(2, 4, 8)` base at
four alpha levels.

| Step | RGBA | Hex equivalent | Provenance |
|---:|---:|---:|---|
| 0 | `rgba(2, 4, 8, 0)` | `#02040800` | `HOME`, `useMat` output range |
| 1 | `rgba(2, 4, 8, 0.05)` | `#0204080D` | `HOME`, `useMat` output range |
| 2 | `rgba(2, 4, 8, 0.2)` | `#02040833` | `HOME`, `useMat` output range |
| 3 | `rgba(2, 4, 8, 0.4)` | `#02040866` | `HOME`, `useMat` output range |

This is the strongest JavaScript evidence for the Home shell's underlying dark
chroma. It is a component mat ramp, not an explicit
`base-mat.overlay.enabled` assignment.

## Shared tag and badge colors

`HOME` contains four explicit tag presets. These are stable component values,
not color-theme variants.

| Preset | Base/detail | Border | Provenance |
|---|---:|---:|---|
| Dark | `#000000CC` / `#FFFFFF` | none | `HOME`, tag palette `DARK` |
| Bright | `#EBEBEB` / `#141414` | none | `HOME`, tag palette `BRIGHT` |
| Line | transparent / `#FFFFFF` | `#C3C3C3` | `HOME`, tag palette `LINE` |
| Gold line | transparent / `#FFD228` | `#A88644` | `HOME`, tag palette `LINE_GOLD` |

The gold pair is the only explicit accent-like pair in the Home bundle. The
bundle does not identify it as the global accent or focus color.

## Error and status colors

These values come from shared peer components and are useful fallbacks. They
are not proven UI3 danger/success tokens.

| Role | Value | Hex equivalent | Provenance |
|---|---:|---:|---|
| Error background | `rgba(50, 0, 0, 0.75)` | `#320000BF` | `PEER-03`, shared `COLOR.ERROR_BG`; repeated across the peer set |
| Error text | `rgba(255, 200, 200, 1)` | `#FFC8C8` | `PEER-03`, shared `COLOR.ERROR_TEXT`; repeated across the peer set |
| Error placeholder | `rgba(0, 0, 0, 0.25)` | `#00000040` | `PEER-03`, shared `COLOR.PLACEHOLDER` |
| Live status | `rgba(208, 2, 27, 1)` | `#D0021B` | `PEER-18`, timeline `LIVE` |
| VOD/inactive track | `rgba(255, 255, 255, 0.15)` | `#FFFFFF26` | `PEER-18`, timeline `VOD` |
| Success | unresolved | unresolved | No production UI constant with a defensible success meaning was found |

Development-library colors and the quick-image-editor color picker were
excluded. Their nearby identifiers show that they are not shell theme values.

## UI3 token mapping result

The expected token-key strings and token payloads are absent from this bundle
set. The closest JavaScript evidence is therefore recorded without promoting
it to a proven token value.

| UI3 token | JavaScript result | Closest recovered value |
|---|---|---|
| `background.enabled` | Key not present; no resolver or token object found | `#020408` is the strongest dark Home base candidate, but the mapping is unproven |
| `base-mat.overlay.enabled` | Key not present | Home uses the `#020408` alpha ramp above, plus semantic native basemat types |
| `focus.stroke.enabled` | Key not present | Unresolved |
| `focus.fill.enabled` | Key not present | Unresolved |

The JavaScript chooses semantic native rendering modes such as
`overlay-gradient-tile`, `overlay-solid`, `overlay-solid-transparent`, and
`overlay-transparent`. Focus is delegated to `FocusLayerPS`. Neither path
contains the resolved token colors. This indicates that the React Native
bundle requests themed native treatments while the concrete UI3 token
resolution occurs outside the readable JavaScript.

## Focus-color check

`#00BAFF` does not occur in `NPXS40002.js` or any of the 27 peer bundles.
Equivalent `rgb(0, 186, 255)` text and common RGBA/ARGB packed forms were also
not found.

The JavaScript therefore neither confirms nor corrects the separately
recovered `#00BAFF` shell fallback. It only shows that Home focus is normally
rendered by native `FocusLayerPS` without a JavaScript color override.

## Per-color-theme result

No JavaScript token maps were found for the seven UI3 color-theme selectors.

| Color theme | Recovered JavaScript token values |
|---|---|
| `Default` | Not present |
| `Red` | Not present |
| `Purple` | Not present |
| `Black` | Not present |
| `Pink` | Not present |
| `Blue` | Not present |
| `Gray` | Not present |

The explicit Home palette above is shared/invariant in JavaScript. Any
differences among these seven selectors must be supplied by data or native
theme state not embedded in the readable bundles.

## Shape values

The identifiers `ShapeTheme`, `Default`, `Soft`, `Sharp`, and `Round` do not
occur as a shape-token map. No per-shape-theme radii can be assigned from the
JavaScript.

The Home bundle does contain these fixed component radii:

| Component | Radius | Provenance |
|---|---:|---|
| Header icon | `8` px | `HOME`, `headerIcon` |
| Hub/background image | `12` px | `HOME`, `image` in the Hub background styles |
| Experience tile | `16` px at `106 × 106` | `HOME`, `BORDER_RADIUS` |
| Scaled experience focus container | `25.358490566…` px | `HOME`, `focusContainer` calculation `(168 / 106) × 16` |
| Function-control container | `16` px | `HOME`, `FCContainer` |
| Action indicator circle | `18` px on `36 × 36` | `HOME`, `actionIndicatorContainer` |
| System icon focus/background circle | `28` px on `56 × 56` | `PUI-NATIVE`, `UI3.ButtonBase.borderRadius = Height / 2`, assigned to `FocusCustomSettings.RoundedCornerRadius` in `OnLayout`; `HOME` m143 supplies the 56 px control size |
| Avatar | half of rendered size | `HOME`, `PureAvatar` dynamic radius |

These are component geometry, not recovered `Default`/`Soft`/`Sharp`/`Round`
shape-token values.

## Unresolved

| Item | Status |
|---|---|
| Seven per-color-theme UI3 token maps | Not embedded in the readable JavaScript |
| Four per-shape-theme radius maps | Not embedded in the readable JavaScript |
| Concrete JavaScript value for either focus token | Not found |
| JavaScript confirmation of `#00BAFF` | Not found |
| A single global accent token | Not found; only the component-local gold tag pair is explicit |
| A production success token | Not found |
| Exact ownership of token resolution | Not named in JavaScript; native semantic components are the observable boundary |
