<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell layout specification

The layout contract the SharpEmu shell is rebuilt against. Bundle values were read out of a
`StyleSheet.create` literal or a module-level constant in Sony's decrypted React Native bundles.
Where a row explicitly names `RN-NATIVE` or `PUI-NATIVE`, it instead comes from the corresponding
managed assembly in the reconstructed 4.03 filesystem. No number in this document comes from a
screenshot or a Figma trace.

Organised by screen, home first, because that is the surface the user stares at. Design tokens lead,
because the per screen numbers hang off them.

## How to read this

| Marker | Meaning |
|---|---|
| EXACT | The value is a numeric literal in a StyleSheet or an exported constant. The quoted line is given. |
| INFERRED | Arithmetic on EXACT values, or a structural reading not written down anywhere. The derivation is shown in the row so it can be checked. |

The two are never blurred. A value that is neither is not in this document; it is in the "Still
unknown" section at the end.

Everything is at a fixed 1920 x 1080 design resolution. The bundles contain no responsive units for
shell chrome; the layout is absolute pixels.

### Source set

| Key | Bundle | Title id | Surface |
|---|---|---|---|
| HOME | `games\useful rnps\readable_js_3.00\NPXS40002.js` | NPXS40002 TITLEID_SIE_RNPS_HOMEUI | Home |
| CC | `...\readable_js_3.00\NPXS40003.js` | NPXS40003 TITLEID_SIE_RNPS_CONTROLCENTER | Control centre |
| AC | `...\readable_js_3.00\NPXS40036.js` | NPXS40036 TITLEID_SIE_RNPS_ACTION_CARDS_HOST_APP | Action cards host |
| HUB | `...\readable_js_3.00\NPXS40033.js` | NPXS40033 TITLEID_SIE_RNPS_GAMEHUB | Game hub |
| LIB | `...\readable_js_3.00\NPXS40071.js` | NPXS40071 TITLEID_SIE_RNPS_LIBRARY | Game library |
| SET | `...\readable_js_3.00\NPXS40008.js` | NPXS40008 TITLEID_SIE_RNPS_SETTINGS | Settings |
| BASE | `...\readable_js_3.00\NPXS40141.base.js` | NPXS40141 base_dll | Shared library bundle |
| RN-NATIVE | `games\PS5_4.03_reconstructed\filesystems\system_ex\common_ex\lib\ReactNative.PUI.dll.sprx` | n/a | React Native to PUI bridge |
| PUI-NATIVE | `games\PS5_4.03_reconstructed\filesystems\system_ex\common_ex\lib\Sce.PlayStation.PUI.dll.sprx` | n/a | Native PUI controls |

Locators are written `HOME m25:3215`, meaning bundle HOME, haul module 25, line 3215 of the pretty
printed bundle. Where the module id was not resolved the locator is just `HOME:3215`. Bundle id to
app mapping is cross checked against `games\psdevwiki_ps5\wikitext\RNPS.txt` and
`docs/ps5-rn-bundle-map.md`.

Build stamp inside the bundles, HOME m585:42266:

```
f = "/home/jenkins/jenkins_slave/workspace/rnps-home_v2_ppr_releases_03.00/packages/home-ui/src/components/HomeControls/index.tsx";
```

Reproduce any row with:

```
python tools/rn-layout/extract_styles.py <bundle>.js --sources
python tools/rn-layout/extract_styles.py <bundle>.js --index --hints
python tools/rn-layout/extract_styles.py <bundle>.js --module <id>
python tools/rn-layout/extract_styles.py <bundle>.js --rule focusContainer
```

### Firmware caveat

The JS measurements are firmware 3.00. The 4.03 bundles under
`games\PS5_4.03_reconstructed\filesystems\system_ex\rnps\apps\` and the 4.02 packages under
`games\rnps_4.02\*.epkg` still carry an encrypted `RNPSHEDR` payload, so they can neither confirm
nor contradict those JS measurements. The managed 4.03 `ReactNative.PUI` and
`Sce.PlayStation.PUI` assemblies are independently inspectable; rows sourced from them are marked
`RN-NATIVE` or `PUI-NATIVE` rather than silently treating them as 3.00 bundle evidence.

---

## 1. Design tokens

One place where numbers are defined. Screen sections reference these names. Where a screen hard
codes something off the scale, the row says so.

### 1.1 Spacing scale, `SIZE.PADDING` and `SIZE.ELEMENT`

| Token | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| `PADDING.SMALL` | 8 | EXACT | HOME m721:51235 | `SMALL: 8,` |
| `PADDING.MEDIUM` | 16 | EXACT | HOME m721:51234 | `MEDIUM: 16,` |
| `PADDING.LARGE` | 24 | EXACT | HOME m721:51233 | `LARGE: 24,` |
| `PADDING.SEARCH_SOURCE` | 25 | EXACT | HOME m721:51232 | `SEARCH_SOURCE: 25,` |
| `PADDING.SEARCH` | 30 | EXACT | HOME m721:51231 | `SEARCH: 30,` |
| `PADDING.PROFILE.LARGE` | top 40, bottom 40 | EXACT | HOME m721:51223-51224 | `TOP: 40,` |
| `PADDING.PROFILE.SMALL` | top 32, bottom 16 | EXACT | HOME m721:51227-51228 | `TOP: 32,` |
| `PADDING.AUTO` / `PADDING.NONE` | `"auto"` / `"none"` | EXACT | HOME m721:51236-51237 | `AUTO: "auto",` |
| `ELEMENT.ATTRIBUTE` | 32 | EXACT | HOME m721:51241 | `ATTRIBUTE: 32,` |
| `ELEMENT.LABEL_PADDING` | 10 | EXACT | HOME m721:51242 | `LABEL_PADDING: 10,` |
| `ELEMENT.BOTTOM` | 26 | EXACT | HOME m721:51243 | `BOTTOM: 26` |
| Generic list spacing | 16 | EXACT | AC:133267, HUB:26477, LIB:103307 | `i.SPACING = 16` |
| Grid item margin | 20 | EXACT | HOME:20899, CC:71437, HUB:29179, LIB:12698 | `E.GRID_ITEM_MARGIN = 20;` |
| Base step | 8 | INFERRED | 8, 16, 24, 32, 40 are all multiples of 8 | rows above |
| Off scale one offs | 10, 20, 25, 26, 30 | INFERRED | these break the 8 grid, so they stay per component constants | rows above |

### 1.2 Type scale

The scale is symbolic in JS. Names are exact. The pixel values are not in any bundle: BASE reads them
from a native module.

```
Q5qt7gZH: function(e, t, n) {
    var r = n("3r/tBeou").FontSize;
    n("J3z8EzFS")(r, "FontSize native module is not installed correctly");
```

BASE:11362-11364. Full token list, BASE:11365-11377.

| Token | Value | Marker | Provenance |
|---|---|---|---|
| `FontSizePS.Size3XLarge` | native | EXACT name, value unknown | BASE:11366 |
| `FontSizePS.Size2XLarge` | native | EXACT name, value unknown | BASE:11367 |
| `FontSizePS.SizeXLarge` | native | EXACT name, value unknown | BASE:11368 |
| `FontSizePS.SizeLarge` | native | EXACT name, value unknown | BASE:11369 |
| `FontSizePS.SizeNormal` | native | EXACT name, value unknown | BASE:11370 |
| `FontSizePS.SizeSmall` | native | EXACT name, value unknown | BASE:11371 |
| `FontSizePS.SizeXSmall` | native | EXACT name, value unknown | BASE:11372 |
| `FontSizePS.Size2XSmall` | native | EXACT name, value unknown | BASE:11373 |
| `FontSizePS.Size3XSmall` | native | EXACT name, value unknown | BASE:11374 |
| `FontSizePS.Size4XSmall` | native | EXACT name, value unknown | BASE:11375 |
| `FontSizePS.Size5XSmall` | native | EXACT name, value unknown | BASE:11376 |
| `FontSizePS.Invalid` | -1 | EXACT | BASE:11377 |

Generation gating, which limits which steps a rebuilt shell may use, BASE:976:

```
t.fontSize === R.Invalid && (console.warn("[Text] Unsupported fontSize. Size5XSmall are not supported on UI3. Size3XLarge, SizeXLarge and Size3XSmall are not supported on UI2."), t.fontSize = R.SizeXSmall);
```

| Rule | Marker | Provenance |
|---|---|---|
| UI3 does not support `Size5XSmall` | EXACT | BASE:976 |
| UI2 does not support `Size3XLarge`, `SizeXLarge`, `Size3XSmall` | EXACT | BASE:976 |
| Invalid size falls back to `SizeXSmall` | EXACT | BASE:976 |

Line spacing is a second native table keyed by font size,
`FontSize.lineSpacingWithEnhancedFontScale`, resolved through `forFontSize` (BASE:29765, BASE:29770).
No numeric line heights exist in JS.

### 1.3 Colour tokens

Tile surface palette, HOME m19:2858-2863. All EXACT.

| Token | Value | Hex |
|---|---|---|
| `COLOR.DARK_GREY` | `rgba(53, 53, 53, 1.0)` | `#353535` |
| `COLOR.GREY` | `rgba(41, 41, 41, 1.0)` | `#292929` |
| `COLOR.BLANK` | `rgba(255, 255, 255, 0.05)` | `#FFFFFF0D` |
| `COLOR.WHITE` | `rgba(255, 255, 255, 1.0)` | `#FFFFFF` |
| `COLOR.OBSCURE` | `rgba(13, 13, 13, 0.6)` | `#0D0D0D99` |

Tag and badge palette, HOME:48546-48559. All EXACT.

| Token | Value |
|---|---|
| `COLOR.DARK.BASEPLANE` | `rgba(0, 0, 0, 0.8)` |
| `COLOR.DARK.DETAIL` | `rgba(255, 255, 255, 1.0)` |
| `COLOR.BRIGHT.BASEPLANE` | `rgba(235, 235, 235, 1.0)` |
| `COLOR.BRIGHT.DETAIL` | `rgba(20, 20, 20, 1.0)` |
| `COLOR.LINE.DETAIL` | `rgba(255, 255, 255, 1.0)` |
| `COLOR.LINE.BORDER` | `rgba(195, 195, 195, 1.0)` |
| `COLOR.LINE_GOLD.DETAIL` | `rgba(255, 210, 40, 1.0)` |
| `COLOR.LINE_GOLD.BORDER` | `rgba(168, 134, 68, 1.0)` |

One off colours that belong to neither group:

| Usage | Value | Marker | Provenance |
|---|---|---|---|
| Strong divider, `separatorText` | `rgba(255, 255, 255, 0.25)` | EXACT | HOME m214:13220, HOME:15427 |
| Secondary text, `tagText` | `rgba(255, 255, 255, 0.7)` | EXACT | HOME m214:13224, HOME:15431 |
| Weak divider, `lineSeperator` | `rgba(255,255,255,0.1)` | EXACT | HOME m679:48457 |
| Modal scrim | `rgba(0,0,0,0.8)` | EXACT | HOME m632:44435 |
| Popup hint text on a light plate | `rgba(0,0,0,1)` | EXACT | HOME:56232 |
| Settings preview basemat | `#020408` | EXACT | SET m410 |
| Background gradient ramp | `rgba(2, 4, 8, 0)`, `0.05`, `0.2`, `0.4` over input `[0, .05, .2, .4]` | EXACT | HOME:41609 |

There is no colour theme module in the home bundle. Components hard code rgba strings, so the two
palettes above are the only groupings that exist.

### 1.4 Opacity tokens, `HOME m719`

| Token | MIN | MAX | Marker | Provenance |
|---|---|---|---|---|
| `OPACITY.LOADING` | 0.05 | 0.08 | EXACT | HOME m719:51151-51153 |
| `OPACITY.LOADING_GRID` | 0 | 0.03 | EXACT | HOME m719:51155-51157 |
| `OPACITY.ACTION_INDICATOR` | 0.7 | 1 | EXACT | HOME m719:51159-51161 |
| `OPACITY.DEFAULT` | 0 | 1 | EXACT | HOME m719:51163-51165 |
| `OPACITY.GRADIENT` | 0.010000001 | 1 | EXACT | HOME m719:51167-51169 |
| `GRADIENT_OFFSET` | -80 | n/a | EXACT | HOME m719:51176 |
| Sub label de-emphasis | 0.7 | n/a | EXACT | HOME m19:2964 |
| Unselected space switcher label | 0.6 | n/a | EXACT | HOME m815:60085 |

Action indicator opacity is token driven: MAX when focused, MIN when glanced (EXACT,
HOME m737:53635).

### 1.5 Radius, focus and icon tokens

| Token | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| Experience tile radius | 16 on a 106 box | EXACT | HOME m25:3224, HOME m210:15195 | `n.BORDER_RADIUS = 16;` |
| Focused experience tile radius | `168 / 106 * 16` = 25.358490566 | EXACT expression | HOME m25:3236 | `borderRadius: 168 / 106 * 16` |
| Radius to side ratio | `16 / 106` = `25.3585 / 168` = 0.150943 | INFERRED | arithmetic over the two rows above | n/a |
| Action indicator radius | 18 on a 36 box, fully round | EXACT | HOME m19:2969 | `borderRadius: 18,` |
| Avatar clip radius | 28 on a 56 box | EXACT | HOME m143:10653 | `borderRadius: 28` |
| Native button focus/background radius | `Height / 2`; 28 on the 56 x 56 system-icon control | EXACT | PUI-NATIVE `UI3.ButtonBase.borderRadius`, `OnLayout`; HOME m143 | `FocusCustomSettings.RoundedCornerRadius = borderRadius` |
| Function control panel radius | 16 | EXACT | HOME m143:10683 | `borderRadius: 16` |
| Hub header image radius | 12 on an 80 box | EXACT | HOME m170:13199 | `borderRadius: 12` |
| Control centre header icon radius | 8 on a 48 box | EXACT | CC m347:34446 | `borderRadius: 8` |
| Progress bar | height 4, radius 2 | EXACT | CC:2752-2753 | `I.PROGRESS_BAR_RADIUS = 2;` |
| Focus ring width | 8 | EXACT | CC:2640 | `I.FOCUS_WIDTH = 8;` |
| Focus inset used by settings | 3 | EXACT | SET m24 | `FOCUS_MARGIN` |
| Focus scale, hub tiles | 1.06 | EXACT | HUB:144592 | `n.FOCUS_SCALE = 1.06;` |
| Picture in picture border | 2 | EXACT | HOME, CC, AC, HUB, LIB | `e.PINP_BORDER_INOUT = 2;` |
| Fallback icon large | 92 | EXACT | HOME m721:51217 | `o = 92,` |
| Fallback icon medium | 72 | EXACT | HOME m721:51218 | `r = 72,` |
| Fallback icon small | 64 | EXACT | HOME m721:51219 | `g = 64,` |

---

## 2. Home screen

### 2.1 Vertical stack

`HomeScreen` mounts `HomeContainer`, which renders `HomeControls`. Wrapper stylesheet, HOME m216:

```
container:                 { flexDirection: "column-reverse" }
spaceSwitcherWrapper:      { height: SYSTEM_HEIGHT, marginLeft: 84 }
systemWrapper:             { height: SYSTEM_HEIGHT, marginRight: 84 }
systemContainer:           { flexDirection: "row", justifyContent: "space-between" }
experienceSwitcherWrapper: { height: SCALED_EXP_SIZE, width: "100%" }
```

`column-reverse` puts the experience switcher above the top nav row visually.

| Band | Height | Screen y | Marker | Provenance |
|---|---|---|---|---|
| Top nav, `home-top-nav` | 126 | 0 to 126 | EXACT | HOME m96:7287 `t.SYSTEM_HEIGHT = 126;` |
| Experience switcher | 168 | 126 to 294 | EXACT | HOME m25:3216 `n.SCALED_EXP_SIZE = 168;` |
| Hub viewer | remainder | from 128 | INFERRED | `marginTop: SCALED_EXP_SIZE - VERTICAL_HEIGHT_CHANGE` = 168 - 40 = 128, HOME m490:34809 |
| Hub viewer overlap over the icon row | 40 | n/a | INFERRED | 168 - 128 = 40 |
| Background plate | 1920 x 1080 at `zIndex: -1` | absolute | EXACT | HOME m401:30686 |
| Native app module host | 1920 x 1080 | absolute | EXACT | HOME m835:60650 |
| Screen canvas | 1920 x 1080 | n/a | EXACT | AC:20677-20678 `I.SCREEN_WIDTH = 1920;` |

### 2.2 Top nav band, y 0 to 126

`systemContainer` is row plus space-between, left cluster inset 84, right cluster inset 84 (EXACT,
HOME m216:15512 and m216:15516). When the profile fails to load the row switches to `flex-end`
(EXACT, HOME m216:15524).

Space switcher, left, HOME m815:60085:

| Rule | Value | Marker |
|---|---|---|
| `spaceSwitcher` | `alignItems:"center", flex:1, flexDirection:"row", justifyContent:"flex-start"` | EXACT |
| `spaceSwitcherItem` | `marginRight: 64, padding: 8` | EXACT |
| `spaceSwitcherItemText` | `fontWeight:"bold", fontSize: FontSizePS.SizeLarge` | EXACT, size token native |
| `spaceSwitcherItemBlur` | `fontWeight:"normal", opacity: 0.6` | EXACT |
| Space count | exactly two, `"game"` and `"media"` | EXACT, HOME m513 |

System icons and clock, right:

| Property | Value | Marker | Provenance |
|---|---|---|---|
| Icon count | 3 | EXACT | HOME m217:15588 `n.systemIconsCount = 3;` |
| Declared icon list | `["Fps", "Search", "Settings", "Profile"]` | EXACT | HOME m490:34578 |
| Shipped order | Search, Settings, Profile, Clock | INFERRED | `Fps` is gated behind an app config flag |
| Container | `alignItems:"center", flex:1, flexDirection:"row", justifyContent:"flex-end"` | EXACT | HOME m96:7289 |
| `iconContainer` | `width: 56, marginLeft: 48` | EXACT | HOME m143:10653 |
| `iconImage` | 56 x 56 | EXACT | HOME m143:10653 |
| Stock icon glyph request | 48 x 48 inside the 56 x 56 control | EXACT | HOME m224:65 |
| Icon pitch | 56 + 48 = 104 | INFERRED | arithmetic on the two rows above |
| Icon size constant | 56 | EXACT | HOME:10651, CC:78386 `e.SYSTEM_ICON_SIZE = 56;` |
| Icon size, no glance | 48 | EXACT | HOME:10652, CC:78385 `e.SYSTEM_ICON_SIZE_NO_GLANCE = 48;` |
| Icon label strip | `position:"absolute", top: 56, width: 368, marginTop: 4` | EXACT | HOME m143:10653 |
| Icon label size | `FontSizePS.SizeXSmall` | EXACT | HOME m143:10653 |
| Wide label variant | `textContainer.width: 336, marginTop: 16`, `container { maxWidth: 416, marginTop: 8 }` | EXACT | HOME m171:13258 |
| `clockWrapper` | `marginLeft: 88` | EXACT | HOME m96:7295 |
| `clock` | `fontSize: FontSizePS.SizeLarge, textAlign: "right"` | EXACT | HOME m96:7299 |
| `time` | `fontVariant: ["tabular-nums"]` | EXACT | HOME m623:43946 |
| Deep links | `pssearch:main`, `pssettings:play?mode=settings` | EXACT | HOME m624 |

The stock `iconId` and profile-avatar paths are different controls (EXACT, HOME m224). A stock
system icon is a `Button` with `legacy:false`, `backgroundVisibility:"visibleOnFocus"`, a 48 x 48
glyph request and the 56 x 56 `iconImage` style. The common-assets `IconButton` wrapper selects its
native icon-button path when `legacy` is false and no title or progress is supplied (HOME m396,
`IconButton.ps.js`:122). Native `UI3.IconButton` inherits `UI3.Button` without changing its focus
geometry, and `ButtonBase.OnLayout` assigns both the background and
`FocusCustomSettings.RoundedCornerRadius` to `Height / 2` (PUI-NATIVE). The result is a circular
28 px radius at this size, not a rectangular avatar-style focus target.

The native focus colour transition is also concrete. `ButtonBase` animates `CurrentColorLerp` on
focus over 0.5 s after a 0.1 s delay (the delay is removed during four-way key repeat), and animates
out over 0.2 s using PUI's default `EaseOutBlast` curve (PUI-NATIVE). `ReactButtonShadowNode`
enables icon inversion when an inverted URI or filter colour is supplied (RN-NATIVE). HOME m396
supplies the shared `Icon.iconEmphasisColors.inverted` value for the normal one-colour icon branch;
BASE:3326-3331 defines that value as `#333333`. PUI's `#292929` native default is only the fallback
when the bridge supplies no inverted filter colour.

Profile and function control popover, anchored under the system icons, HOME m143:10683:

```
FCFocusLayer: { marginTop: 126, marginLeft: 1188 }
FCContainer:  { position:"absolute", width: 652, minHeight: 216, maxHeight: 810, borderRadius: 16 }
```

| Property | Value | Marker | Note |
|---|---|---|---|
| Panel width | 652 | EXACT | same as the control centre card width |
| Panel min / max height | 216 / 810 | EXACT | n/a |
| Panel top | 126 | EXACT | equals `SYSTEM_HEIGHT`, so it hangs off the bottom of the top band |
| Trophy summary right margin | 24 | EXACT | HOME:56222 |
| Function list item right padding | 12 | EXACT | HOME:56225 |
| Popup hint | width 600, `paddingBottom: 20`, `SizeSmall` | EXACT | HOME:56228-56233 |

### 2.3 Experience switcher, y 126 to 294

Constants, HOME m25. All EXACT.

| Name | Value | Line |
|---|---|---|
| `EXPERIENCE_SIZE` | 106 | 3215 |
| `SCALED_EXP_SIZE` | 168 | 3216 |
| `EXPERIENCE_SCALE` | `168 / 106` = 1.584906 | 3217 |
| `SCALED_EXP_MARGIN_LEFT` | 172 | 3218 |
| `MINIMIZED_EXP_MARGIN_TOP` | 48 | 3219 |
| `MINIMIZED_EXP_MARGIN_LEFT` | 48 | 3220 |
| `MINIMIZED_EXP_SIZE` | 80 | 3221 |
| `MINIMIZED_EXP_SCALE` | `80 / 168` = 0.476190 | 3222 |
| `VERTICAL_HEIGHT_CHANGE` | 40 | 3223 |
| `BORDER_RADIUS` | 16 | 3224 |

Stylesheet, HOME m25:3225. All EXACT.

```
container:            { flexDirection:"row", width: 1920, height: 168 }
experienceTitle:      { fontSize: FontSizePS.SizeNormal, textAlign:"center" }
focusContainer:       { borderRadius: 168 / 106 * 16 }
optionsMenuStyle:     { height: 106, width: 106 }
strandStyle:          { marginLeft: 172 }
strandContainer:      { width: 1500, height: 168 }
downloadbarContainer: { marginTop: 2, alignItems: "center" }
downloadbar:          { width: 90 }
```

| Property | Value | Marker | Provenance |
|---|---|---|---|
| Max tiles in the row | 11 | EXACT | HOME m47:4080, plus `items.slice(0, 11)` at HOME m47:4067 |
| Space switch travel per page | 1920 per index | EXACT | HOME:42216 `toValue: 1920 * -u,` |

Strand construction, HOME m201:14577-14587. All EXACT.

| Parameter | Value |
|---|---|
| `getItemLayout` | 106 x 106 |
| `selectedItemScale` | `EXPERIENCE_SCALE` |
| `focusedMargin` | 16 |
| `itemMargin` | 8 |
| `maxItems` | 11 |
| `style` | `strandStyle`, `marginLeft: 172` |

### 2.4 Experience switcher positioning math

`updateState` and `calculate`, HOME m531:38282-38367. With `w` = 106, `s` = 168/106, `fm` = 16,
`im` = 8, `sel` = selected index:

```
offset  = w*s/2 - w/2                    = 84 - 53 = 31
if sel < 0:  x_i = i*(w + im) - w*s/2    = 114*i - 84
else:        x_i = offset + (i - sel)*(w + im)
             x_i -= offset + fm - im      when i < sel     // 31 + 16 - 8 = 39
             x_i += offset + fm - im      when i > sel
```

Each item is `position: "absolute"` in a 106 x 106 box and gets
`transform: [{translateX: x_i}, {scale: 1 to s}]`, scaled about the box centre.

| Item | translateX | Box on screen, x from 172 | Size | Marker |
|---|---|---|---|---|
| `sel - 1` | `31 - 114 - 39` = -122 | 50 to 156 | 106 | INFERRED, arithmetic shown |
| `sel` | 31 | 203 to 309, scaled about centre 256, so 172 to 340 | 168 | INFERRED, arithmetic shown |
| `sel + 1` | `31 + 114 + 39` = 184 | 356 to 462 | 106 | INFERRED, arithmetic shown |
| `sel + 2` | 298 | 470 to 576 | 106 | INFERRED, arithmetic shown |

| Consequence | Value | Marker | Basis |
|---|---|---|---|
| Resting pitch | 114, that is `106 + itemMargin 8` | INFERRED | arithmetic |
| Gap either side of the focused tile | exactly 16 | INFERRED | 340 to 356, and 156 to 172 |
| Focused tile left edge | 172, equals `SCALED_EXP_MARGIN_LEFT` | INFERRED | arithmetic |
| Strand vertical centring | `container { marginTop: -53 }`, `item { marginTop: +53 }` from `h/2` with h = 106 | EXACT | HOME m530 |
| Resting tile band | y 157 to 263 | INFERRED | 126 + 31, centring a 106 box in a 168 band |
| Focused tile band | y 126 to 294 | INFERRED | same |
| Corroboration | focus trap is a 106 x 106 element at `top: 157, left: 172 + 32`, `borderRadius: 16, opacity: 0` | EXACT | HOME m806:59568 |

### 2.5 Experience tile

HOME m210. All EXACT.

```
imageWrapper:      { borderRadius: BORDER_RADIUS }                        // 16
image:             { width: EXPERIENCE_SIZE, height: EXPERIENCE_SIZE }    // 106 x 106
primaryTitleId:    { fontSize: FontSizePS.Size4XSmall, fontWeight:"bold", textAlign:"center" }
telemetryContainer:{ position: "relative" }
telemetryTile:     { position:"absolute", transform:[{ translateY: -1080 }] }
```

| Finding | Marker | Note |
|---|---|---|
| There is no opacity rule on an unfocused tile | EXACT absence, HOME m210 | resting tiles draw at full opacity; the only tile opacity animations are the launch and minimize transition (HOME m571) and the download bar (HOME m570) |
| Missing icon fallback | EXACT | `cxml://CommonAssets/iconid_texture_app_fallback` at 168 x 168, HOME m47 |
| Broken icon | EXACT | `cxml://CommonAssets/iconid_texture_app_broken`, HOME m47 |

### 2.6 Focused title and metadata strip

HOME m214, `TitleContainer`.

| Name | Value | Marker | Provenance |
|---|---|---|---|
| `TITLE_MARGIN_TOP` | 10 | EXACT | HOME m214:15401 |
| `TITLE_MARGIN_LEFT` | 16 | EXACT | HOME m214:15402 |
| `TITLE_X` | `172 + 168 + 16` = 356 | INFERRED | arithmetic on EXACT constants |
| `TITLE_Y` | 106, equals `EXPERIENCE_SIZE` | EXACT | HOME m214 |
| `MINIMIZED_TITLE_MARGIN_LEFT` | 44 | EXACT | HOME m214:15407 |
| `MINIMIZED_TITLE_MARGIN_TOP` | 9 | EXACT | HOME m214:15408 |
| Metadata strip height | `SCALED_EXP_SIZE - EXPERIENCE_SIZE` = 62 | INFERRED | arithmetic, `itemContainer` uses the expression |
| `separatorText` | `top:6, bottom:6, left:12, width:2, rgba(255, 255, 255, 0.25)` | EXACT | HOME m214:13217-13220 |
| `tagText` | `marginLeft: 26, rgba(255, 255, 255, 0.7)` | EXACT | HOME m214:13223-13224 |
| `matadataIconContainer` | `marginLeft: 12, flexDirection: "row"` | EXACT | HOME m214, Sony's spelling preserved |
| `entitlementIconId` / `storageIconId` | 42 x 42 | EXACT | HOME m214 |
| Tag area width | 48 | EXACT | HOME:48697 |

Coordinates are inside the 1920 x 168 row container, so `top: 106` is screen y 232 and the 62 tall
strip is bottom aligned with the focused tile.

Honest caveat: per item text opacity is driven by `textOpacity<i>` values that were not fully traced,
so the states in which the title is visible at (356, 106) rather than only during the launch
transition are not established. The coordinates and the transition deltas in section 9 are EXACT; the
visibility rule is not.

### 2.7 Focus wiring

| Item | Value | Marker | Provenance |
|---|---|---|---|
| Focus layer names | `home-experience-switchers`, `experience-switcher-<spaceId>`, `focus-layer-<spaceId>`, `tile-item-focus-layer`, `focused-item-<key>`, `experience-switcher-<key>`, `space-switcher`, `space-switcher-<spaceId>`, `home-system`, `home-top-nav`, `experience-switcher-focus-layer` | EXACT | HOME m540, m513, m217, m585, m490 |
| Re-entry | `focusInBehavior: { type: "lastFocusedItem" }` | EXACT | HOME m540 |
| Focused tile directional moves | `canMoveLeft:false, canMoveRight:false`, left and right delegated to the strand key handlers | EXACT | HOME m540 |
| `home-system` wiring | `leftCandidate: "space-switcher"`, `canMoveRight: false`, `downCandidate: "experience-switcher-<space>"` | EXACT | HOME m217 |
| Interaction state enum | `GLANCED`, `FOCUSED`, `ACTION` | EXACT | HOME m720 |
| Stock system-icon branch | Native `Button`; `backgroundVisibility:"visibleOnFocus"`, `legacy:false`, no explicit `focusStyle` | EXACT | HOME m224:65 |
| Profile-avatar branch | `View`; `focusStyle:"rectangle"`, `style:round`, no `backgroundVisibility` prop | EXACT | HOME m224:83 |
| System-icon label scaling | `allowFontScaling:{maxLevel:"large"}` belongs to the label `Text`, not either focus control | EXACT | HOME m224:111 |

### 2.8 Content strand geometry

HOME m28. All EXACT.

```
STRAND_WIDTH     = 1576
STRAND_HEIGHT    = 864
CONTAINER_MARGIN = 172
TEST_IDS = { ITEM_LIST: "strand-item-list", ITEM: "strand-item", LABEL: "strand-label" }
```

| Relation | Value | Marker |
|---|---|---|
| Strand width equals canvas minus both margins | `1920 - (2 x 172)` = 1576 | INFERRED |

`HORIZONTAL_SPACING[1576]`, HOME m28:3319-3352. All EXACT.

| Tile width | howManyCanFit | margin | tileSizingWithMargin |
|---|---|---|---|
| 236 | 6 | 32 | 268 |
| 296 | 5 | 24 | 320 |
| 360 | 4 | 32 | 392 |
| 370 | 4 | 32 | 402 |
| 504 | 3 | 32 | 536 |
| 772 | 2 | 32 | 804 |

`VERTICAL_SPACING[864][192]`, HOME m28:3354-3361, EXACT:
`{ howManyCanFit: 4, margin: 5, tileSizingWithMargin: 197 }`.

| Relation | Marker | Note |
|---|---|---|
| `tileSizingWithMargin = tileWidth + margin` | INFERRED | holds for all seven rows |
| Default gutter is 32, with 24 reserved for the 296 tile | INFERRED | five of six horizontal rows use 32 |
| Only 1576 and these six widths are blessed | EXACT | out of table widths fall back to `round(containerWidth / (tileWidth + margin))` and log `"SWAT STRAND ERROR: ..."`, HOME m735:53549 |

### 2.9 Tile preset catalogue, `SIZE.PLAIN` / `.OVERLAY` / `.STACKED`

HOME m721:51245-52356. Templates are `PLAIN`, `SLIM`, `STACKED`, `OVERLAY` (HOME m722). Every row
EXACT. Padding columns reference section 1.1 tokens.

| Family | Variant | Size | Padding primary / secondary | Label size / lines | Line |
|---|---|---|---|---|---|
| SQUARE | LARGE | 504 x 504 | LARGE / MEDIUM | XSmall / 1 | 51248 |
| SQUARE | MEDIUM | 370 x 370 | LARGE / MEDIUM | XSmall / 1 | 51284 |
| SQUARE | SMALL | 296 x 296 | MEDIUM / SMALL | 2XSmall / 1 | 51316 |
| SQUARE | XSMALL | 236 x 236 | MEDIUM / SMALL | 2XSmall / 1 | 51352 |
| WIDE | LARGE | 504 x 284 | LARGE / MEDIUM | XSmall / 1 | 51390 |
| WIDE | MEDIUM | 370 x 208 | MEDIUM / SMALL | 2XSmall / 1 | 51426 |
| WIDE | SMALL | 236 x 133 | MEDIUM / SMALL | 2XSmall / 1 | 51462 |
| TALL | MEDIUM | 370 x 555 | MEDIUM / SMALL | 2XSmall / 1 | 51500 |
| FULL | LARGE | 772 x 579 | LARGE / MEDIUM | XSmall / 1 | 51538 |
| OVERLAY | SEARCH | 370 x 370 | LARGE / MEDIUM plus `paddingBottom: 30` | 2XSmall / 2 | 51579 |

Stacked family, where `overlayContainer` is the art box and `meta` is the text box under it. All
EXACT.

| Variant | Root size | Art | Meta | Label size / lines | Line |
|---|---|---|---|---|---|
| LARGE FULL | 504 x 456 | 504 x 284 | 172 | XSmall / 1 | 51622 |
| LARGE DESCRIPTION | 504 x 448 | 504 x 284 | 164 | XSmall / 2 | 51661 |
| LARGE DUAL_LABEL | 504 x 442 | 504 x 284 | 158 | XSmall / 2 | 51698 |
| LARGE LABEL | 504 x 400 declared, 408 in its own root style | 504 x 284 | 116 | XSmall / 2 | 51738, 51749 |
| MEDIUM FULL | 370 x 344 | 370 x 208 | 136 | 2XSmall / 1 | 51777 |
| MEDIUM DESCRIPTION | 370 x 340 | 370 x 208 | 136 | 2XSmall / 2 | 51816 |
| MEDIUM DUAL_LABEL | 370 x 334 | 370 x 208 | 126 | 2XSmall / 2 | 51853 |
| MEDIUM LABEL | 370 x 300 | 370 x 208 | 92 | 2XSmall / 2 | 51893 |
| MEDIUM SQUARE_DUAL_LABEL | 370 x 498 | 370 x 370 | 128 | 2XSmall / 2 | 51930 |
| SMALL LABEL | 236 x 201 | 236 x 133 | 68 | 2XSmall / 2 | 51972 |
| SEARCH | 370 x 370 | 370 x 208 | 162 | 2XSmall / 2 | 52009 |

The `LARGE LABEL` row carries a genuine inconsistency in the source: `height: 400` at HOME m721:51738
and `height: 408` in its own `root` style at HOME m721:51749. Both are EXACT. The shell must pick one
and record which.

Profile family, all EXACT:

| Variant | Size | Label size | Line |
|---|---|---|---|
| PROFILE LARGE | 504 x 442 | XSmall | 52054 |
| PROFILE SMALL | 370 x 334 | 2XSmall | 52109 |
| PROFILE FRIEND | 370 x 344 | 2XSmall | 52168 |
| PROFILE SEARCH | 370 x 370 | 2XSmall | 52227 |

Slim family, full width rows, all EXACT:

| Variant | Size | Media box | Label size / lines | Line |
|---|---|---|---|---|
| SLIM SQUARE | 100% x 192 | 144 x 144 | XSmall / 2 | 52293 |
| SLIM WIDE | 100% x 192 | 144 x 81 | XSmall / 2 | 52327 |

| Relation | Marker | Note |
|---|---|---|
| Tile widths come from one ladder: 236, 296, 370, 504, 772 | INFERRED | the same ladder appears in the packing table in 2.8 |
| `SLIM.WIDE` media box is 16:9 | INFERRED | 144 / 81 = 1.7778 |
| The matrix is fully token driven | EXACT | every `container` padding, `attribute.marginTop`, `subLabel.marginTop` and `selectionIndicator.marginRight` references `PADDING.*`, not a literal |

### 2.10 Content card chrome

HOME m19:2866. All EXACT.

```
statusIcon:               { height: 32, width: 32, marginLeft: 8 }
sourceIcon:               { height: 36, width: 36, position: "absolute" }
attributeContainer:       { flexDirection:"row", height: 32, flexWrap:"wrap", overflow:"hidden" }
attributeTag:             { marginRight: 8 }
subLabelText:             { fontSize: Size3XSmall, opacity: 0.7 }
actionIndicatorContainer: { width: 36, height: 36, borderRadius: 18, backgroundColor: WHITE, opacity: 1 }
slimStatusIndicator:      { position:"absolute", top:0, right:0, padding: 8 }
overlayContainer:         { height:"100%", width:"100%", flexDirection:"column", justifyContent:"space-between", position:"absolute" }
blank:                    { backgroundColor: COLOR.BLANK }
```

### 2.11 Player and friend tiles

Separate from the content matrix. All EXACT.

| Name | Value | Provenance |
|---|---|---|
| `TILE_HEIGHT_L` | 130 | HOME m98:7323 |
| `TILE_HEIGHT_S` | 98 | HOME m98:7324 |
| `TILE_SQUARE_HEIGHT_L` | 340 | HOME m98:7325 |
| `TILE_SQUARE_HEIGHT_S` | 314 | HOME m98:7326 |
| `TILE_SQUARE_HEIGHT_L_VL` | 360 | HOME m98:7327 |
| `TILE_SQUARE_WIDTH` | 370 | HOME m98:7328 |
| `avatar` | 144 x 144 | HOME m701 |
| `nameTextStyle` | width 322 | HOME m701 |
| `AVATAR_SIZE_SMALL` / `_LARGE` | 48 / 64 | HOME:49418-49419 |
| `AVATAR_SIZE` | 144 | HOME:49771 |
| `AVATAR_MARGIN_LEFT` | 16 | HOME:19895 |

These are consumed by `ui-shared-utilities-player-tile/PlayerTileSquare`, not by the content strand.

### 2.12 Grid, list and panel frames used across home bodies

Grid frame, HOME m255:20142 and m257:20283. All EXACT.

```
container:        { width: 1576, flex: 1, marginHorizontal: 172 }
gridContainer:    { alignItems: "center", flex: 1 }
optionContainer:  { position:"absolute", left: -120, top: 40, flexDirection:"column" }
optionItem:       { marginBottom: 32 }
headerContainer:  { flexDirection:"row", width: 1576, height: 34 }
gridTitle:        { fontSize: SizeXSmall, height: 34 }
```

`172 + 1576 + 172 = 1920` exactly (INFERRED, arithmetic).

| Name | Value | Marker | Provenance |
|---|---|---|---|
| `GRID_ITEM_MARGIN` | 20 | EXACT | HOME:20899 |
| `DEFAULT_MARGIN_UNDER_BOTTOM_ITEM` | 90 | EXACT | HOME:20900 |
| `SEGMENT_HEADER_HEIGHT` | 34 | EXACT | HOME:20901 |
| `SEGMENT_HEADER_BOTTOM_MARGIN` | 24 | EXACT | HOME:20902 |
| `LIST_ITEM_HEIGHT` | 98 | EXACT | HOME:11884, CC:24229 |
| `LEFT_ICON_SIZE` | 56 | EXACT | HOME:11885 |
| `LEFT_ICON_MARGIN_LEFT` / `_RIGHT` | 16 / 20 | EXACT | HOME:11886-11887 |
| `HEADER_HEIGHT` | 80 | EXACT | HOME:11947, CC:34445, AC:21159 |
| `CHECKBOX_WIDTH` | 62 | EXACT | HOME:19539 |
| `CENTERPANEL_MARGIN_LEFT` | 20 | EXACT | HOME:19864 |

Sort and filter panel, HOME m259:20683, identical in CC m648 and SET m479. All EXACT.

```
headerContainer:       { height: 40, marginTop: 16, marginBottom: 5, paddingLeft: 24, minWidth: 384 }
filterHeaderContainer: { height: 40, marginTop: 24, marginBottom: 5, paddingLeft: 24, minWidth: 384 }
header:                { color: COLOR_MAP.GRAY, fontSize: SizeSmall }
separator:             { height: 2, width: "100%", backgroundColor: COLOR_MAP.GRAY }
separatorContainer:    { marginTop: 16, marginHorizontal: 16 }
sortOption:            { height: 72, paddingLeft: 72, flexDirection:"row", alignItems:"center", minWidth: 384 }
sortOptionSelected:    { height: 72, flexDirection:"row", alignItems:"center", minWidth: 384 }
sortIconContainer:     { height: 72, width: 72 }
filterSubItem:         { height: 72, paddingLeft: 16, flexDirection:"row", alignItems:"center" }
filterSubItemText:     { paddingLeft: 16, paddingRight: 48 }
checkmarkContainer:    { marginHorizontal: 16, width: 40 }
tooltip:               { maxHeight: 64 }
resetButtonContainer:  { marginHorizontal: 20, marginVertical: 16 }
```

A menu row is 72 tall with a 72 leading icon gutter. That is the one menu metric that is in the JS.

List and notification frame, HOME m278:21978 and CC m241. All EXACT.

```
list:                     { flex: 1, marginHorizontal: 8, marginBottom: 16 }
sectionHeaderContainerTop:{ marginBottom: 8, marginHorizontal: 16 }
sectionHeaderContainer:   { marginTop: 24, marginBottom: 8, marginHorizontal: 16 }
sectionHeaderText:        { fontSize: SizeXSmall, opacity: 0.7 }
loadingIndicator:         { height: 30 }
keyGuideContainer:        { position:"absolute", bottom:0, width:"100%", height:0, flexDirection:"row-reverse", alignItems:"flex-start" }
keyguide:                 { marginTop: 16 }
lineSeperator:            { width: "100%", height: 2, backgroundColor: "rgba(255,255,255,0.1)" }
```

### 2.13 Hub header, minimized state

HOME m170:13199. Rendered once a game is running and the icon has minimized. All EXACT.

```
container:  { position:"absolute", marginTop: 48, marginLeft: 48, marginRight: 172, alignItems:"center", flexDirection:"row" }
image:      { width: 80, height: 80, borderRadius: 12, marginRight: 44 }
tagText:    { marginLeft: 26, color: "rgba(255, 255, 255, 0.7)" }
entitlementIconId / storageIconId: { height: 42, width: 42 }
```

48 / 48 / 80 are exactly `MINIMIZED_EXP_MARGIN_TOP`, `_LEFT` and `_SIZE`, and 44 is
`MINIMIZED_TITLE_MARGIN_LEFT` (INFERRED, matching against section 2.3), so the hub SDK and home-ui
agree.

Hub nav, HOME m389:28975, all EXACT: `horizontalNav { marginRight: 172, marginLeft: 148 }`,
`verticalNav { width: 2152, marginTop: 86, marginLeft: 40 }`,
`horizontalWrapper { paddingTop: 40, marginRight: -172, marginLeft: -148 }`,
`hubUtility { position:"absolute", right: 172 }` (HOME m406).

### 2.14 Options menu on a tile

The panel geometry is not in these bundles. `home-ui/src/components/TileOptionsMenu` (HOME m840) and
`rnps-js-modules-experience-options-menu/src/optionsMenu.tsx` (HOME m514) build the item list and
hand it to a native component:

```
React.createElement(OptionsMenuPS, { contextItems, globalItems, onRequestClose, ref, targetComponent: findNodeHandle(target) })
```

What is recoverable:

| Item | Value | Marker | Provenance |
|---|---|---|---|
| Anchor | tile `shim` view, `position:"absolute", top:0, left:0, right:0, bottom:0, transform:[{translateX:-3},{translateY:3}]` | EXACT | HOME m558:40147 |
| Anchor box | 106 x 106 `optionsMenuStyle` | EXACT | HOME m25:3238 |
| Menu ids in declaration order | `MENU_ID_CHECK_PATCH`, `MENU_ID_SAVE_DATA_MANAGEMENT`, `MENU_ID_GAME_DATA_MANAGEMENT`, `MENU_ID_APPLICATION_DELETE`, `MENU_ID_APPLICATION_MULTI_DELETE`, `MENU_ID_APPLICATION_REMOVE_FROM_HOME`, `MENU_ID_UPDATE_HISTORY`, `MENU_ID_MOVE_TO_EXTERNAL_STORAGE`, `MENU_ID_MOVE_TO_INTERNAL_STORAGE`, `MENU_ID_APPLICATION_INFORMATION`, `MENU_ID_INTELLECTUAL_PROPERTY_NOTICES`, `MENU_ID_APPLICATION_CLOSE`, `ACTION_ID_CLOSE_APPLICATION` | EXACT | HOME m525:37923 |
| Globals only item | `eject-disc`, `msgid_remove_disc`, pushed ahead of the context items | EXACT | HOME m514 |
| Button hints while a tile is focused | `msgid_sr_l1_button`, `msgid_sr_slash_or`, `msgid_sr_r1_button`, `msgid_sr_switch_homes`, `msgid_sr_options_button` plus `msgid_options`, `msgid_sr_more_content_down_btn` | EXACT | HOME m540 |

The nearest panel geometry that does exist is the function control popover in 2.2, 652 wide, 216 to
810 tall, radius 16, and the sort and filter panel in 2.12.

---

## 3. Control centre

### 3.1 Shell geometry

| Property | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| System bar width | 1920 | EXACT | CC:31410 | `t.SYSTEM_WIDTH = 1920;` |
| System bar side margin | 84 | EXACT | CC:31409 | `t.SYSTEM_MARGIN = 84;` |
| Strand left margin | 84 | EXACT | CC:85324 | `e.STRAND_MARGIN_LEFT = 84;` |
| Strand bottom margin | 214 | EXACT | CC:85325 | `e.STRAND_MARGIN_BOTTOM = 214;` |
| Container height | 434 | EXACT | CC:85439, CC:246020 | `n.CONTAINER_HEIGHT = 434;` |
| Selected face height | 232 | EXACT | CC:104563 | `e.SELECTED_FACE_HEIGHT = 232;` |
| Selected header height | 80 | EXACT | CC:45245 | `i.SELECTED_HEADER_HEIGHT = 80;` |
| Selected header bottom margin | 16 | EXACT | CC:45246 | `i.SELECTED_HEADER_BOTTOM_MARGIN = 16;` |
| Quick control button height / margin | 72 / 16 | EXACT | CC:36989, CC:36988 | `O.BUTTON_HEIGHT = 72` |
| List button height | 72 | EXACT | CC:82091 | `e.LIST_BUTTON_HEIGHT = 72;` |
| Nested strip tile heights | 72 and 60 | EXACT | CC:231282, CC:234562 | `e.TILE_HEIGHT = 72;` |
| Icon container size | 64 | EXACT | CC:150719 | `e.CONTAINER_SIZE = 64;` |

### 3.2 Control centre bar icon cell

CC m220:22058. All EXACT.

```
container:      { height: 147, width: 112, alignItems: "center" }
iconContainer:  { width: 48, height: 48, marginTop: 54, justifyContent:"center", alignItems:"center" }
icon:           { width: 48, height: 48 }
iconButton:     { width: 56, height: 56 }
labelContainer: { position:"absolute", height: 34, top: 0, width: 368, justifyContent:"flex-end", alignItems:"center" }
label:          { fontSize: SizeXSmall }
badgeContainer: { position:"absolute", top: 0, left: 0 }
```

Customisable variant, CC m770:85380, all EXACT: `container { width: 112 }`,
`iconContainer { marginBottom: 28 }` and 24 when focused, `labelContainer { height: 34, top: 0,
width: 362 }`, `switch { 40 x 40 }`, `customButton { width: 88, height: 165, marginTop: 42 }`,
`notCustomizable { opacity: 0.4 }`.

| Consequence | Value | Marker |
|---|---|---|
| Icon pitch | 112 | INFERRED, cell width |
| Icon inset from cell top | 54 | EXACT |
| Label strip pinned to cell top | 34 tall | EXACT |
| Button container width / height / margin | 112 / 147 / 64 | EXACT, CC:22055-22057 |

### 3.3 Card system

| Property | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| Card width | 652 | EXACT | CC:2634 | `I.CARD_WIDTH = 652;` |
| Card height | 810 | EXACT | CC:2635 | `I.CARD_HEIGHT = 810;` |
| Card left / right margin | 24 / 24 | EXACT | CC:2636-2637 | `I.CARD_LEFT_MARGIN = 24;` |
| Card margin | 24 | EXACT | CC:2638 | `I.CARD_MARGIN = 24;` |
| Card body width | 604 | EXACT | CC:2639 | `I.CARD_BODY_WIDTH = 604;` |
| Body equals card minus both margins | `652 - 48` = 604 | INFERRED | arithmetic on rows above | n/a |
| Default menu width | 652 | EXACT | CC:53462 | `t.DEFAULT_MENU_WIDTH = 652;` |
| Focus debounce | 100 ms | EXACT | CC:2641 | `I.FOCUS_DEBOUNCE_DELAY = 100;` |
| List bottom margin | 4 | EXACT | CC:2642 | `I.LIST_BOTTOM_MARGIN = 4;` |

Panel header, CC m347:34446, all EXACT: `header { height: 80, padding: 24, opacity: 0.7 }`,
`headerIcon { 48 x 48, borderRadius: 8 }`, `headerIconContainer { 48 x 48, marginRight: 16 }`,
`headerText { SizeSmall }`.

Menu row, CC m240:24233, all EXACT: `listItem { minHeight: 98, flexDirection:"row",
alignItems:"center" }`, `rightIconContainer { 48 x 48, marginHorizontal: 16 }`,
`menuListItemButtonProfileContainer { height: 90, marginBottom: 2 }`,
`leftIcon { alignSelf:"flex-start", marginTop: 21 }`.

Transfers row, CC m112:10989, all EXACT: `listItem { height: 146 }`,
`listItemExcludingSeparator { minHeight: 144, marginBottom: 2 }`, `icon { 96 x 96 }`,
`iconContainer { marginLeft: 16 }`,
`defaultIconContainer { 96 x 96, backgroundColor: rgba(255,255,255,0.05) }`,
`defaultIcon { 64 x 64, opacity: 0.7 }`, `lineBoxContainer { marginLeft: 20, marginRight: 16 }`.

### 3.4 Card section headers, lists and scroll bar

All EXACT.

| Property | Value | Provenance |
|---|---|---|
| Header height | 80 | CC:2643 |
| Section header height | 48 | CC:2644 |
| Section header bottom margin | 8 | CC:2645 |
| Section header top margin | 24 | CC:2646 |
| Scroll bar top margin | 48 | CC:2649 |
| Scroll bar right margin | 8 | CC:2650 |
| List top edge fade padding | 40 | CC:2678 |
| List row width | 636 | CC:2675 |
| List row height | 130 | CC:2674 |
| List left / right margin | 16 / 16 | CC:2676-2677 |
| Info text margin | 20 | CC:2680 |
| Separator height | 2 | CC:2757 |
| Mini player list icon size | 96 | CC:10976 |
| Mini player list item height | 146 | CC:10979 |

### 3.5 Music card

All EXACT.

| Property | Value | Provenance |
|---|---|---|
| Thumbnail | 96 x 96 | CC:2654-2655 |
| Now playing thumbnail | 286 x 286 | CC:2661-2662 |
| Playing animation icon | 96 x 96 | CC:2663-2664 |
| Album list row height | 98 | CC:2667 |
| Album index column width | 70 | CC:2665 |
| Compilation index width / height | 70 / 96 | CC:2668-2669 |
| Podcast index width / height | 70 / 96 | CC:2670-2671 |
| Podcast list row height | 130 | CC:2672 |
| USB list row height | 130 | CC:2673 |
| Track list text height | 40 | CC:2647 |
| Track list header height | 40 | CC:2648 |
| Mini player container height | 130 | CC:2740 |
| Mini player section header height | 44 | CC:2739 |
| Mini player header plus player | 174 | CC:2741 |
| Mini player width | 636 | CC:2742 |
| Mini player parts width | 604 | CC:2744 |
| Mini player thumbnail span | 96 | CC:2743 |
| Branding spans, trackline / playlist / mini player | 30 / 32 / 34 | CC:2651-2653 |
| Branding attribution image | 133 x 40, total block 80 | CC:2776-2778 |
| Explicit icon span | 32 | CC:2745 |
| Playback state icon span | 32 | CC:2657 |
| Podcast progress bar | left 400, top 50, width 100 | CC:2658-2660 |
| Podcast progress bar in list | height 4, radius 2, width 80 | CC:2754-2756 |
| Podcast margin width | 96 | CC:2656 |

### 3.6 Behaviour constants

All EXACT.

| Property | Value | Provenance |
|---|---|---|
| Podcast skip back / forward | -15 s / 15 s | CC:2766-2767 |
| Previous track double press window | 400 ms | CC:2770 |
| Play start watchdog | 10000 ms | CC:2771 |
| Track list page size | 30 | CC:24731 |
| Playlists page size | 15 | CC:24732 |
| Max tracklist / playlist pages | 10 / 10 | CC:2764-2765 |
| USB tracklist page size / max pages | 100 / 6 | CC:158542-158543 |
| Tooltip debounce | 15000 ms | CC:20092, HOME:6410 |
| Headphone volume caps, limited / unlimited | 22 / 25 | CC:162379-162380 |
| Volume throttle | 33 ms | CC:45745 |
| Volume timeout | 300000 ms | CC:45746 |

---

## 4. Action cards

### 4.1 Host layout, two size classes

All EXACT.

| Property | Full screen | Mini | Provenance |
|---|---|---|---|
| Total size | 1920 x 1080 | 1116 x 812 | AC:19773-19774, AC:6494-6495 |
| Head area height | 162 | 88 | AC:19775, AC:6496 |
| Selection area width | 984 | 660 | AC:19776, AC:6497 |
| Navigation area width | 528 | 456 | AC:19777, AC:6498 |
| Tab item height | 72 | 72 | AC:19778, AC:6499 |
| Tab item gap with panel | 32 | 34 | AC:19779, AC:6500 |
| Tab item left margin | 148 | not present | AC:19780 |
| Tab panel left margin | 24 | not present | AC:19781 |
| Player list height | 806 | not present | AC:19782 |
| Player list end offset | 90 | not present | AC:19783 |

A second mini canvas is declared separately as 928 x 810 (EXACT, AC:20679-20680,
`I.MINI_SCREEN_WIDTH = 928;`). Two different mini widths exist in the same bundle, so the shell must
pick per component rather than assume one mini canvas. See "Still unknown".

### 4.2 Card geometry

| Property | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| Card width | 520 | EXACT | AC:16054 | `A.CARD_WIDTH = 520;` |
| Card height | 810 | EXACT | AC:16055 | `A.CARD_HEIGHT = 810;` |
| Card left / right margin | 24 / 24 | EXACT | AC:16056-16057 | `A.CARD_LEFT_MARGIN = 24;` |
| Card body width | 472 | EXACT | AC:16058 | `A.CARD_BODY_WIDTH = 472;` |
| Body equals card minus both margins | `520 - 48` = 472 | INFERRED | arithmetic on rows above | n/a |
| Card margin in the strip | 16 | EXACT | AC:38556 | `D.CARD_MARGIN = 16` |
| Screen bottom margin | 16 | EXACT | AC:4366 | `e.SCREEN_BOTTOM_MARGIN = 16;` |
| Header height | 80 | EXACT | AC:21159 | `_.HEADER_HEIGHT = 80;` |
| Quick controls height | 72 | EXACT | AC:21160 | `_.QUICK_CONTROLS_HEIGHT = 72;` |
| Posts per card, max / min | 10 / 1 | EXACT | AC:21152-21153 | `_.MAX_POSTS = 10;` |

### 4.3 Card media

| Property | Value | Marker | Provenance |
|---|---|---|---|
| Tile | 472 x 267 | EXACT | AC:15299-15300 |
| Source image | 944 x 534 | EXACT | AC:15301-15302 |
| Image is exactly 2x the tile | `944 = 2 x 472`, `534 = 2 x 267` | INFERRED | arithmetic |
| Media height | 265.5 | EXACT | AC:39310 |
| Media aspect ratio | `16 / 9` | EXACT | AC:39313 |
| Map preview | 472 x 265.5 | EXACT | AC:85854-85855 |
| Expand overlay | 96 | EXACT | AC:39311 |
| Full screen icon | 52 | EXACT | AC:39312 |
| Default fallback icon | 92 | EXACT | AC:15303 |
| Media y offset | 152 | EXACT | AC:21161 |
| Media padding | 24 | EXACT | AC:21162 |

### 4.4 Capture and gallery grids inside the host

All EXACT.

| Grid | Item size | Margins | Provenance |
|---|---|---|---|
| Generic grid item | 388 x 218 | item 32, h 16, v 16 | AC:20684-20688 |
| Browse content | 504 x 284, thumbnail 504 x 284 | h 32, v 32 | AC:20694-20699 |
| Browse group | 370 x 484, thumbnail 370 x 370, footer 114 | n/a | AC:20706-20710 |
| Select content | 370 x 208, thumbnail 370 x 208 | h 32, v 32 | AC:20714-20719 |
| Browse USB | 370 x 208, text area 50, parts margin 16 | n/a | AC:20724-20731 |
| Browse content grid canvas | 1576 x 832 | n/a | AC:20704-20705 |
| Browse group grid canvas | 1576 wide | n/a | AC:20713 |
| List top without tab | 214 | n/a | AC:20682 |
| Grid list parts height | 32 | n/a | AC:20689 |
| Symbol icon in grid items | 124 x 124 | n/a | AC:20700-20701, 20711-20712, 20720-20721, 20728-20729 |
| Small icon in grid items | 32 x 32 | n/a | AC:20690-20691, 20702-20703, 20722-20723 |
| Duration label width | 88 | n/a | AC:20693 |
| Grid list icon horizontal margin | 8 | n/a | AC:20692 |

### 4.5 Dialogs and editors in the host

| Property | Value | Marker | Provenance |
|---|---|---|---|
| Popup dialog | 764 x 440 | EXACT | AC:32294-32295, AC:220072-220073, AC:227003-227004 |
| Popup dialog body | width 676, margin 44 | EXACT | AC:227005-227006 |
| Body equals dialog minus both margins | `764 - 88` = 676 | INFERRED | arithmetic |
| Video edit canvas | 1440 x 810 | EXACT | AC:229645-229646 |
| Image edit canvas | 1440 x 810 | EXACT | AC:229647-229648 |
| Side edge margin | 24 | EXACT | AC:229611 |
| Search input | 1130 x 72 | EXACT | AC:184516-184517 |
| Search close button offset | 312 | EXACT | AC:184518 |
| Message icon size | 40 | EXACT | AC:89740 |
| Text button width | 388 | EXACT | AC:89741 |
| Image button | 72 x 72 | EXACT | AC:89743-89744 |
| Dialog horizontal margin | 24 | EXACT | AC:89742 |
| Header text bottom margin | 24 | EXACT | AC:192419 |
| Leaderboard row height | 72 | EXACT | AC:201282 |
| Host list icon size | 64 | EXACT | AC:188153 |
| Max controllers | 4 | EXACT | AC:46669 |

---

## 5. Game hub

| Property | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| Strand margin | 172 | EXACT | HUB:332 | `E.STRAND_MARGIN = 172;` |
| Strand width / height | 1576 / 864 | EXACT | HUB:5695-5696 | `n.STRAND_WIDTH = 1576;` |
| Section header height | 58 | EXACT | HUB:356 | `E.SECTION_HEADER_HEIGHT = 58;` |
| Section bottom margin | 64 | EXACT | HUB:357 | `E.SECTION_BOTTOM_MARGIN = 64;` |
| News background image width | 1920 | EXACT | HUB:333 | `E.NEWS_BACKGROUND_IMAGE_WIDTH = 1920;` |
| Hub logo, 4K variant | 1440 x 296 | EXACT | HUB:394-395 | `E.LOGO_WIDTH_4K = 1440;` |
| Tile size | 370 | EXACT | HUB:59249 | `t.TILE_SIZE = 370;` |
| Focus scale | 1.06 | EXACT | HUB:144592 | `n.FOCUS_SCALE = 1.06;` |
| Control bar height | 72 | EXACT | HUB:7065 | `O.CONTROL_HEIGHT = 72;` |
| Control bar horizontal margin | 84 | EXACT | HUB:7066 | `O.CONTROL_H_MARGIN = 84;` |
| Tooltip max text width in safe area | 176 | EXACT | HUB:7067 | `O.TOOLTIP_MAX_TEXT_WIDTH_FOR_SAFE_AREA = 176;` |
| Button height / margin | 72 / 16 | EXACT | HUB:24702, HUB:24701 | `O.BUTTON_HEIGHT = 72` |
| Description column width | 504 | EXACT | HUB:60803 | `e.DESCRIPTION_WIDTH = 504;` |
| Edition art height | 283.5 | EXACT | HUB:60804 | `e.EDITION_ART_HEIGHT = 283.5;` |
| Side panel | 504 x 670 | EXACT | HUB:163201-163202 | `i.PANEL_WIDTH = 504;` |
| Side panel, short variant | 504 x 550 | EXACT | HUB:163203 | `i.SHORT_PANEL_HEIGHT = 550;` |
| Rating image max | 100 x 72 | EXACT | HUB:59379-59380 | `R.MAX_RATING_IMAGE_HEIGHT = 72;` |
| Rating divider max height | 66 | EXACT | HUB:59381 | `R.MAX_DIVIDER_HEIGHT = 66` |
| Add-on tiles shown | 10 | EXACT | HUB:330 | `E.MAX_ADD_ON_TILES_COUNT = 10;` |
| View all add-ons page size | 16 | EXACT | HUB:331 | `E.VIEW_ALL_ADD_ONS_PAGE_LIMIT = 16;` |
| Entities on screen cap | 24 | EXACT | HUB:34603 | `E.MAX_ENTITIES_ON_SCREEN = 24;` |
| Data / record cache | 96 / 960 | EXACT | HUB:34604-34605 | `E.DATA_CACHE_SIZE = 96;` |
| Hub scene render limit | 1 | EXACT | HUB:401 | `E.HUB_SCENE_RENDER_LIMIT = 1;` |
| Hub video target bitrate | 3000 | EXACT | HUB:407 | `E.VIDEO_TARGET_BITRATE = 3e3;` |
| Poll interval | 15000 ms | EXACT | HUB:172155 | `t.DEFAULT_POLL_INTERVAL = 15e3;` |
| Description column equals `SQUARE.LARGE` tile width | 504 | INFERRED | matches HOME m721:51248 | n/a |
| Hub tile equals `SQUARE.MEDIUM` | 370 | INFERRED | matches HOME m721:51284 | n/a |

---

## 6. Library

| Property | Value | Marker | Provenance | Snippet |
|---|---|---|---|---|
| Tiles per row | 5 | EXACT | LIB:8922 | `E.NUM_ROW_TILES = 5;` |
| Visible rows | 3 | EXACT | LIB:8923 | `E.NUM_VISIBLE_ROWS = 3;` |
| Grid item margin | 20 | EXACT | LIB:12698 | `E.GRID_ITEM_MARGIN = 20;` |
| Margin under last row | 90 | EXACT | LIB:12699 | `E.DEFAULT_MARGIN_UNDER_BOTTOM_ITEM = 90;` |
| Segment header height | 34 | EXACT | LIB:12700 | `E.SEGMENT_HEADER_HEIGHT = 34;` |
| Segment header bottom margin | 24 | EXACT | LIB:12701 | `E.SEGMENT_HEADER_BOTTOM_MARGIN = 24` |
| Strand width / height | 1576 / 864 | EXACT | LIB:2830-2831 | `n.STRAND_WIDTH = 1576;` |
| Container margin | 172 | EXACT | LIB:2832 | `n.CONTAINER_MARGIN = 172;` |
| Generic spacing | 16 | EXACT | LIB:103307 | `i.SPACING = 16` |
| Button height / margin | 72 / 16 | EXACT | LIB:20830, LIB:20829 | `O.BUTTON_HEIGHT = 72` |
| Entities on screen cap | 24 | EXACT | LIB:26837 | `E.MAX_ENTITIES_ON_SCREEN = 24;` |
| Data / record cache | 96 / 960 | EXACT | LIB:26838-26839 | `E.DATA_CACHE_SIZE = 96;` |
| Grid cell pitch, 5 across | `(1576 - 4 x 20) / 5` = 299.2 | INFERRED | does not land on any tile in the width ladder, so this is a hypothesis, not a build target | n/a |

The library grid arithmetic does not close cleanly against the tile ladder in 2.8. Treat the pitch
row as something to verify against a capture.

---

## 7. Settings

### 7.1 List frame, `StyleValues`

SET m24:1801 supplies the shared/plain-list frame. SET m76:8775 belongs to the
saved-data/game-title list family. Both export `StyleValues`, but their values
must not be collapsed into one native `MenuListItemPS` contract. Values are
EXACT for the named source family.

| Name | Value |
|---|---|
| `DEFAULT_LISTVIEW_TOP` | 186 |
| `DEFAULT_LISTVIEW_LEFT` | 304 |
| `DEFAULT_LISTVIEW_WIDTH` | 1312 |
| `DEFAULT_LISTVIEW_HEIGHT` | 894 |
| saved-data `DEFAULT_LISTITEM_HEIGHT` | 152 |
| `DEFAULT_LISTITEM_MARGIN` | 0 |
| `DEFAULT_LISTVIEW_BOTTOM_MARGIN_UNDER_BOTTOM_ITEM` | 90, variant 86 |
| `SELECTMODE_LISTVIEW_TOP` | 186 |
| `SELECTMODE_LISTVIEW_LEFT` | 172 |
| `SELECTMODE_LISTVIEW_WIDTH` | 1092 |
| `SELECTMODE_LISTVIEW_HEIGHT` | 894, tabbed 806 |
| `SELECTMODE_BUTTON_WIDTH` x `HEIGHT` | 388 x 72 |
| `SELECTMODE_BUTTON_LEFT` | 96 |
| `SELECTMODE_BUTTON_DESELECT_ALL_TOP` | 96 |
| `SELECTMODE_BUTTON_SELECT_DECIDEDL_TOP` | 736 |
| `SECTION_TAIL_ITEM_MARGIN` | 96, variant 50 |
| `SECTION_ITEM_MARGIN` | 14 |
| `DEFAULT_INDENT_WIDTH` | 64, variant 94 |
| `TOP_LABEL_MARGIN_BOTTOM` | 36, 48 and 60 across three call sites |
| `FOCUS_MARGIN` | 3 |
| `SEPARATOR_MARGIN` | 16 |
| `IMAGE_MARGIN_RIGHT` | 20 |
| `DEFAULT_TAB_PANEL_WIDTH` | 1092 |

```
settingsList: { position:"absolute", top: 186, left: 304, width: 1312, height: 894 }
```

| Consequence | Value | Marker |
|---|---|---|
| The settings list is not centred | `304 + 1312` = 1616, leaving a 304 lead-in and a 304 gutter | INFERRED, arithmetic |
| `FOCUS_MARGIN: 3` matches the native focus inset | 3 | EXACT value, INFERRED correspondence |

Row text, SET m190 `LongTextListItem`, all EXACT: title `marginTop/Bottom 27`, `marginRight 48`,
`marginLeft 16`, `SizeNormal`; value `marginTop 31`, `marginBottom 27`, `marginRight 16`,
`SizeXSmall`, `opacity 0.7`, `alignSelf: "flex-end"`. Popup scroll `maxHeight: 504,
marginBottom: 48` (SET m257). Preview plate `borderRadius: 16, backgroundColor: "#020408"`
(SET m410).

### 7.2 Settings screens

All EXACT unless noted.

| Property | Value | Provenance |
|---|---|---|
| Section header margin | 56 | SET:29174 |
| Top label bottom margin | 60 | SET:36221 |
| Top area height | 72 | SET:162649 |
| Top area bottom margin | 8 | SET:162650 |
| Indicator view width | 476 | SET:12718 |
| Restore exec view height | 696 | SET:13413 |
| Restore prompt bottom | 90 | SET:13414 |
| Progress circle | 500 | SET:73247 |
| Scroll view height with button | 696 | SET:154740 |
| Scroll view height without button | 828 | SET:154741 |
| Storage select width, normal / large | 784 / 1312 | SET:154737-154738 |
| Storage info width | 560 | SET:154739 |
| Storage info height, normal, 1 / 2 / 3 lines | 160 / 202 / 244 | SET:154742-154744 |
| Storage info height, large, 1 / 2 / 3 lines | 180 / 244 / 308 | SET:154745-154747 |
| Progress view body width | 1048 | SET:154748 |
| Progress label margin | 48 | SET:154749 |
| Second label margin, normal / large | 4 / 0 | SET:154733-154734 |
| Detail margin, normal / large | 21 / 8 | SET:154735-154736 |
| Storage select-all minimum width | 344 | SET:152866, SET:165832 |
| Line step, normal / large, INFERRED | 42 / 64, from `202-160=42`, `244-202=42`, `244-180=64`, `308-244=64` | derived from the two height rows |

The 42 and 64 line steps are the only place in the mined set where a text row grows by a non token
amount, so encode them as their own constants rather than deriving them from the spacing scale.

---

## 8. Other surfaces in the mined set

| Surface | Property | Value | Marker | Provenance |
|---|---|---|---|---|
| Search input, action cards host | Input | 1130 x 72 | EXACT | AC:184516-184517 |
| Search tile preset | Tile | 370 x 370, 2 label lines | EXACT | HOME m721:51579-51589 |
| Notifications and system message | Screen width | 1920 | EXACT | AC:89739 |
| Options menu | Default menu width | 652 | EXACT | CC:53462 |
| Modal | Scrim | `rgba(0,0,0,0.8)` | EXACT | HOME m632:44435 |
| Trophy summary, small | Height | 48 | EXACT | HOME:58771 |
| Trophy summary, portrait | Size | 370 x 456 | EXACT | HOME:58985-58986 |
| Trophy summary, wide | Size | 1576 x 256, `paddingHorizontal: 96`, row `marginLeft: 64` | EXACT | HOME:59210-59219 |
| Trophy summary, error image | Size | 306 x 236, `marginTop: 32` | EXACT | HOME:59016-59020 |

---

## 9. Motion

### 9.1 Shared timing tokens, `HOME m719`

| Token | Value | Marker | Provenance |
|---|---|---|---|
| `ANIMATION.TIMING.DEFAULT` | 300 ms | EXACT | HOME m719:51173 |
| `ANIMATION.TIMING.LOADING` | 750 ms | EXACT | HOME m719:51174 |

### 9.2 Spring presets, `HOME m49:4161`

All EXACT. All four set `useNativeDriver: true`.

| Preset | stiffness | damping | mass | overshootClamping |
|---|---|---|---|---|
| `SPRING_OPTIONS_SLOW` | 130 | 25 | 1 | true |
| `SPRING_OPTIONS_SLOWER` | 100 | 20 | 1 | true |
| `SPRING_OPTIONS_FAST` | 200 | 100 | 0.2 | absent, so false |
| `SPRING_OPTIONS_FASTER` | 600 | 100 | 0.2 | absent, so false |

Springs used outside the preset table:

| Usage | stiffness | damping | mass | Marker | Provenance |
|---|---|---|---|---|---|
| Strand focus move, the strand's own default | 400 | 50 | 0.2, `overshootClamping: true` | EXACT | HOME m530:38151 |
| Home list settle | 600 | 100 | n/a | EXACT | HOME:29592-29593 |
| Home hub transition | 200 | 100 | n/a | EXACT | HOME:30992-30993, HOME:31149-31150 |
| Control centre panel | 1000 | 500 | n/a | EXACT | CC:30613-30614 |
| Control centre fast snap | 6000 | 100 | n/a | EXACT | CC:47885-47886 |
| Control centre faster snap | 7000 | 300 | n/a | EXACT | CC:47918-47919 |

The strand focus move runs one `Animated.spring` per tile for both `scale` (0 to 1, interpolated to
1 to 1.5849) and `translateX`, started together via `Animated.parallel` (EXACT, HOME m530). There is
no tween and no duration on the focus move. The `springOptions` Recoil atom defaults to `undefined`
(EXACT, HOME m128), so the strand default above is what actually runs.

### 9.3 Launch and minimize transition, `HOME m571` `useHeaderTransition`

Per tile, `translateX`, `translateY` and `scale` interpolate `[0,1]` to the following. The formulae
are EXACT from the bundle; the evaluated numbers are INFERRED arithmetic, shown in full.

```
tx = -(SCALED_EXP_MARGIN_LEFT + SCALED_EXP_SIZE/2
       - (MINIMIZED_EXP_MARGIN_LEFT + MINIMIZED_EXP_SIZE/2)) * (1/EXPERIENCE_SCALE)
   = -(172 + 84 - (48 + 40)) * 106/168 = -168 * 0.630952 = -106.0
ty = ( -(SYSTEM_HEIGHT + SCALED_EXP_SIZE/2
         - (MINIMIZED_EXP_MARGIN_TOP + MINIMIZED_EXP_SIZE/2))
       + (SYSTEM_HEIGHT + VERTICAL_HEIGHT_CHANGE) ) * (1/EXPERIENCE_SCALE)
   = (-(126 + 84 - (48 + 40)) + 166) * 0.630952 = 44 * 0.630952 = 27.76
scale: 1 to MINIMIZED_EXP_SCALE = 80/168 = 0.476190
```

The `1/EXPERIENCE_SCALE` factor is present because the transform sits inside the already scaled node.
The title moves with it, HOME m565:

```
translateX: 0 to -(TITLE_X - (MINIMIZED_EXP_MARGIN_LEFT + MINIMIZED_EXP_SIZE + MINIMIZED_TITLE_MARGIN_LEFT))
          = -(356 - (48 + 80 + 44)) = -184
translateY: 0 to -(TITLE_Y - (MINIMIZED_EXP_MARGIN_TOP + MINIMIZED_TITLE_MARGIN_TOP)) + VERTICAL_HEIGHT_CHANGE
          = -(106 - 57) + 40 = -9
```

Springs used: `FAST` for the header transition values, `FASTER` for the experience opacity (EXACT,
HOME m571).

### 9.4 Boot and startup reveal, `HOME m843` `useStartupAnimation`

One `Animated.parallel` of four branches plus a sound. All EXACT.

| Branch | Timing | Spring |
|---|---|---|
| Switcher slide in | immediate | `SLOWER` |
| Per tile scale up | `Animated.stagger(60, ...)` over `min(11, n)` tiles, HOME m843:61237 | `SLOWER` |
| System opacity and translate | after `Animated.delay(1050)` | `SLOW` |
| Title opacity | after `delay(1050)` then `delay(333)` | `SLOW` |
| Hub viewer reveal | after `Animated.delay(1450)` | `SLOW` |

Reveal interpolations, all `[0,1]` driven from 1 to 0, EXACT, HOME m843:

```
experience<i>:    scale        [1, 0]
strandContainer:  opacity      [1, 0]
                  translateX   [0, 1920]
                  translateY   [0, (168 - 106)/2] = [0, 31]
titleContainer:   opacity      [1, 0]
system:           opacity      [1, 0], translateY [0, -20]
hubViewer:        opacity      [1, 0], translateY [0,  20]
```

Sound `SystemSoundPS.playByID("psfx_open_home")` (EXACT, HOME m843:61267). On completion the boot
timestamp `SCE_BOOT_ENTRY_SHELLUI_END` is written and `Installer.notifyFocusAnimationFinished()`
fires.

Other startup timings: the hidden startup chain waits 600 ms between the two navigations (EXACT,
HOME:44065), and a generic long fade of 1000 ms exists at HOME:44103.

### 9.5 Durations by interaction

All EXACT.

| Interaction | Duration | Easing | Provenance |
|---|---|---|---|
| Tile glance to focus opacity | 300 ms, `TIMING.DEFAULT` | `easeOutBreezePS` | HOME m747:54249-54250 |
| Tile focus to glance opacity | 300 ms | `easeOutBreezePS` | HOME m747:54254-54255 |
| Tile show and hide pair | 300 ms | `easeOutBreezePS` | HOME m748:54298-54299 |
| Gradient offset transition | 300 ms | `easeOutBreezePS` | HOME m750:54380 |
| Loading shimmer half cycle | 750 ms | `easeInOutPS` | HOME:53876-53877 |
| Hub scene enter | 300 ms | `bezier(.25, .1, .25, .8)` | HOME m391:36322-36330 |
| Hub scene stagger | 16.67 ms per item | n/a | HOME m391:36332 |
| List scroll nudge out | 35 ms | `bezier(.2, .7, .6, .8)` | HOME:29372-29378 |
| List scroll nudge back | 0 ms after a 5 ms delay | `bezier(.2, .7, .6, .8)` | HOME:29381-29383 |
| Return to top, fade out | 150 ms | inherited | HOME:29450 |
| Return to top, fade in | 500 ms | inherited | HOME:29461, HOME:29470 |
| Text marquee in and out | 200 ms each | `Easing.poly(5)` | HOME:40225-40228 |
| Focus highlight in / out | 150 ms / 100 ms | `Easing.linear` | HOME:47996 |
| Modal show | 250 ms after a 50 ms delay | `easeOutBlastPS` | HOME m677:48013-48016 |
| Modal hide | 300 ms, no delay | `linear` | HOME m677:48018-48021 |
| Control centre open and close | 300 ms | n/a | CC:15289 |
| Control centre close, fast path | 100 ms | n/a | CC:78867 |
| Control centre card enter | 200 ms | `easeOutBreezePS` | CC:25246-25247 |
| Control centre card cross fades | 150 ms each | `Easing.linear()` | CC:25251-25263 |
| Control centre panel slide | 500 ms | `bezier(.2833, .99, .31833, .99)` | CC:30620-30621 |
| Control centre panel settle | 350 ms | `Easing.out(Easing.poly(5))` | CC:30638-30639 |
| Control centre panel dismiss | 150 ms | `Easing.in(Easing.linear)` | CC:30646-30647 |
| Control centre background fade in | 500 ms | `Easing.linear` | CC:31541-31542, CC:34953-34954 |
| Control centre background fade out | 250 ms | `Easing.linear` | CC:31587-31588, CC:34999-35000 |
| Root navigator transition | 400 ms | n/a | CC:46886, AC:54476 |
| Selected item fade in | 150 ms | n/a | CC:52731, AC:60461 |
| Dialog fade | 250 ms | `Easing.inOut(Easing.ease)` | CC:49776-49777 |
| On visible fade in delay | 100 ms | n/a | CC:115812 |
| Action card add nudge | 400 ms, travel 32 | n/a | AC:38550-38551 |
| Action card add fade in | 200 ms | n/a | AC:38552 |
| Action card placeholder fade in | 750 ms after an 800 ms delay | n/a | AC:38553-38554 |
| Action card placeholder hide | 1500 ms | n/a | AC:38555 |
| Action card media shrink / grow | 300 ms / 300 ms | n/a | AC:21165-21166 |
| Expand icon timeout | 1000 ms | n/a | AC:21157 |

### 9.6 Easing curves

| Curve | Definition | Marker | Provenance |
|---|---|---|---|
| `Easing.bezier(.25, .1, .25, .8)` | literal cubic bezier | EXACT | HOME:29096, HOME m391:36322 |
| `Easing.bezier(.2, .7, .6, .8)` | literal cubic bezier | EXACT | HOME:29372 |
| `Easing.bezier(.2833, .99, .31833, .99)` | literal cubic bezier | EXACT | CC:30621 |
| `Easing.poly(5)` | standard React Native polynomial | EXACT | HOME:40225, CC:30639 |
| `Easing.linear` | standard | EXACT | HOME:47996, CC:31542 |
| `Easing.inOut(Easing.ease)` | standard | EXACT | CC:47776, CC:49777 |
| `Easing.out(Easing.quad)` | standard | EXACT | CC:31212 |
| `easeInOutPS` | native curve | EXACT name only | HOME:53877, HOME:53882 |
| `easeOutBreezePS` | native curve | EXACT name only | HOME m747:54250, CC:25247 |
| `easeOutBlastPS` | native curve | EXACT name only | HOME m677:48016, CC:28287 |

Every timing that carries a driver flag sets `useNativeDriver: true`, so a rebuilt shell should
assume transform and opacity only for these, never layout animation.

### 9.7 Input and throttle timings

All EXACT.

| Property | Value | Provenance |
|---|---|---|
| Display event throttle | 16 ms | HOME, CC, HUB, LIB `l.displayEventThrottle = 16` |
| Focus debounce | 100 ms | CC:2641 |
| Tooltip debounce | 15000 ms | HOME:6410, CC:20092 |
| Skip increment throttle | 500 ms | HOME, CC, HUB, LIB `SKIP_INCREMENT_THROTTLE_MS = 500` |
| Animation lookahead | 5 s | AC:131771, HUB:108128, LIB:101828 |
| Cache timeout | 100 ms | HOME:33020 |

---

## 10. Conformance gaps against the current shell

Read from `src/SharpEmu.GUI/Controls/ShellTileRow.cs`, `ShellFunctionRow.cs`, `ShellFocusRing.cs`,
`MainWindow.axaml` and `App.axaml`.

| # | Current shell | This spec | Fix |
|---|---|---|---|
| 1 | `DefaultGap = 22` uniform between every tile, `ShellTileRow.cs:93` | Two gaps: 8 between resting tiles, 16 either side of the focused tile. Resting pitch 114. See 2.4. | Implement the `calculate` formulae verbatim; they are six lines |
| 2 | `UnfocusedOpacity = 0.55`, `ShellTileRow.cs:98` | No opacity dimming on resting tiles at all. See 2.5. | Delete the dim. The size difference is the affordance |
| 3 | `FocusedLift = -14` translateY, `ShellTileRow.cs:99` | No lift. The tile scales about its own centre in a 168 band; vertical centring is `marginTop: -53 / +53`. See 2.4. | Remove the lift, centre the 106 box in a 168 band |
| 4 | `FocusMs = 300` and `ScrollMs = 300` tweens for scale and re-centre, `ShellTileRow.cs:100-101` | Scale and translateX are springs, `{stiffness:400, damping:50, mass:0.2, overshootClamping:true}`, one per tile in parallel. 300 ms tweens are for opacity and gradient only. See 9.2. | Add a spring integrator, keep 300 ms tweens for opacity |
| 5 | `StaggerMs = 16.67` on every re-layout, `ShellTileRow.cs:102` | The 60 ms stagger belongs to the boot reveal only. Focus moves are not staggered. The 16.67 ms stagger is the hub scene enter. See 9.4 and 9.5. | Move the stagger into a one shot startup animation at 60 ms |
| 6 | `TileCornerRadius = 16` at focused size, scaled down to about 10.1 at rest, `ShellTileRow.cs:94` | Radius tracks size at a constant ratio 0.150943: 16 at 106, 25.36 at 168. See 1.5. | Drive radius from side length, not from a fixed value plus scale |
| 7 | `ShellFunctionRow.Gap = 46`, `ShellFunctionRow.cs:113` | System icons are 56 boxes with `marginLeft: 48`, pitch 104, and the clock sits 88 after the last icon. See 2.2. | Set icon 56, gap 48, clock gap 88 |
| 8 | No fixed 1920 anchors; the shell reflows inside a resizable window with `Margin="32,24,32,20"`, `MainWindow.axaml:85` | Every anchor is absolute at 1920 x 1080: top band 126, icon row 126 to 294, focused tile at exactly x 172 y 126, nav insets 84, content strand inset 172. | Lay out on a 1920 x 1080 logical surface and scale the surface rather than reflowing |
| 9 | Tile count is whatever the library holds | `MAX_TILES = 11`, enforced twice. See 2.3. | Cap the row at 11 and put the rest behind the library |
| 10 | No focused title or metadata strip beside the tile | `TITLE_X = 356`, `TITLE_Y = 106`, strip 62 tall, entitlement and storage icons 42 x 42, separator 2 at `rgba(255,255,255,0.25)`. See 2.6. | Add the strip, honouring the visibility caveat in 2.6 |

### Corrections to sibling docs

- `docs/ps5-figma-layout.md` is a hand trace and is wrong wherever it disagrees. Resting pitch is 114
  and not 116. The focused tile is 168 x 168 with radius 25.36, not 181 x 179 with radius 32. The
  strand origin is x 172 and the band top is y 126, not x 186 y 131. Right hand icon pitch is 104 and
  not 100. Search, settings and avatar are all 56 x 56 boxes with the avatar clipped at radius 28,
  not 31.3, 34 and 53. The Figma file remains useful for artwork, not geometry.
- `docs/ps5-home-structure.md` files `TILE_HEIGHT_L`, `TILE_HEIGHT_S` and `TILE_SQUARE_*` under the
  home content area. Those are HOME m98 and they are consumed by
  `ui-shared-utilities-player-tile/PlayerTileSquare`, so they are player and friend tiles. Content
  tiles are the matrix in 2.9.
- `docs/ps5-home-structure.md` treats `strandContainer 1500 x 168` and `STRAND_WIDTH 1576` as
  variants of one thing. They are different: 1500 x 168 is the experience switcher viewport
  (HOME m25), 1576 x 864 is the content strand viewport (HOME m28). Both share the 172 margin.
- `docs/ps5-options-menu-and-focus.md` row 10 says the shell pre-divides the radius by the scale so
  the post transform radius is 16. It multiplies: `168/106 * 16 = 25.36`. The invariant is a constant
  radius to side ratio, and the on screen focused radius is 25.36.
- Earlier revisions of this document stated that `base_dll` was absent from the readable set. It is
  present at `readable_js_3.00\NPXS40141.base.js`. Having it does not recover the type scale, because
  `base_dll` itself reads the sizes from a native module (BASE:11363). The conclusion stands, the
  reason changes.

---

## 11. Still unknown

Read this before inventing numbers. A missing number is a measurement task, not a design decision.

| Item | Why it could not be recovered | What would close it |
|---|---|---|
| Pixel values behind `FontSizePS.*` | The scale is a native module. `base_dll` only forwards names, BASE:11363 `var r = n("3r/tBeou").FontSize;` | Dump the native `FontSize` module constants at runtime, or read the shell native binary |
| Line heights | Same native source, `FontSize.lineSpacingWithEnhancedFontScale`, BASE:29765 | Same as above |
| Font family and weights | No `fontFamily` appears anywhere in the home bundle; weights pass through as opaque props | Inspect the native text renderer or the installed system font set |
| Coefficients for `easeInOutPS`, `easeOutBreezePS`, `easeOutBlastPS` | Native curves referenced by name only | Native dump, or curve fit from a high frame rate capture |
| The full colour system | Only two literal palettes plus a handful of one offs exist in JS; there is no colour theme module | Capture the theme provider output, or read the native theme tables |
| Focus ring stroke, glow and compositing beyond width 8 and per-control radius | The native `ButtonBase` establishes the system-icon radius and colour-inversion timing, but not the complete focus renderer appearance | Inspect the remaining `FocusElement` renderer paths and screenshot-diff against a console; see `docs/ps5-options-menu-and-focus.md` |
| Options menu panel geometry | Built by native `OptionsMenuPS`; JS supplies only the item list and the anchor | Measure a capture; nearest analogues are the 652 wide popover and the 72 tall menu row |
| Settings and control centre row chrome | `SettingsListPS`, `MenuListItemPS`, `ListViewPS`, `TabViewPS` are native. The apps supply contents and the outer frame | Native inspection |
| Blur and backdrop parameters | No blur radius or backdrop constant surfaced anywhere in the mined bundles | Native compositor inspection |
| 4K layout variants | Only `LOGO_HEIGHT_4K` and `LOGO_WIDTH_4K`, HUB:394-395, hint at a 4K path | Grep the remaining bundles for a `_4K` suffix and mine those modules |
| Which mini canvas action cards use | Two conflicting mini sizes in one bundle: 1116 x 812 (AC:6494-6495) and 928 x 810 (AC:20679-20680) | Runtime capture of the mini surface |
| `STACKED.LARGE.LABEL` height, 400 or 408 | Both are present in the same preset, HOME m721:51738 and 51749 | Measure the rendered tile |
| Library grid pitch | 5 tiles across 1576 with margin 20 gives 299.2, which is not on the tile ladder | Measure a library screenshot |
| Home focused title visibility rule | `textOpacity<i>` drivers were not fully traced | Trace HOME m565 and m571 consumers, or capture the states |
| Explore, Profile, Store, PS Plus, Remote Play, Trophies, Gaming Lounge, Share Play | Readable bundles exist (NPXS40063, NPXS40013, NPXS40047, NPXS40037, NPXS40154, NPXS40025, NPXS40018, NPXS40080) but were outside the six surfaces this pass covered | Run the same extraction over those bundles |
| Firmware 4.02 and 4.03 numbers | Those containers still carry an encrypted `RNPSHEDR` payload; only the 3.00 set is readable | Decrypt the 4.x containers, re-run the extraction, diff against these tables |
| Whether 3.00 geometry still holds in 4.x | Not verifiable without the row above | Same as above |
