# PS5 shell theme values

This is a clean-room reference of theme facts recovered from PS5 firmware 9.00 and
4.03. It contains values and data structure only.

Color notation:

- `0xAARRGGBB` is the packed value used by the shell theme parameter object.
- `#RRGGBB` is the same color without alpha.
- `UIColor(r, g, b, a)` channels are normalized floats in that order.
- An alpha of zero in a default or legacy focus color is intentional. The RGB is
  still the focus hue; the renderer supplies visibility separately.

## Provenance

| ID | Firmware | Firmware-relative file | SHA-256 | Relevant evidence |
|---|---:|---|---|---|
| `9-THEME` | 9.00 | `common_ex/lib/Sce.Vsh.ShellUI.Theme.dll.sprx` | `6F3932D4831EDA42AC1943550A47A5BC3F97BDD6762F250E7E066E2F3377CFE2` | Managed metadata plus the native AOT method bodies |
| `9-PUI` | 9.00 | `common_ex/lib/Sce.PlayStation.PUI.dll.sprx` | `5A78896BDE7718CF8A02ACB9FB5E9E5BDDF17942C2C8CAC523CCB1D9FAAB9212` | UI3 theme enums, token containers, and focus-render geometry |
| `4-THEME` | 4.03 | `common_ex/lib/Sce.Vsh.Theme.dll.sprx` | `C8D9D9E4D7620F962B0B6379A7D774227DB8D2306B4A5BD5DC609AB939A73D00` | Concrete managed IL for the legacy wave/panel skin |
| `4-PUI` | 4.03 | `common_ex/lib/Sce.PlayStation.PUI.dll.sprx` | `EC6DAD4C940EE89C78B1B27A859E0AAF8119B8DE476B684C7273A0413F6B55F7` | PUI color definitions and legacy skin dependencies |
| `4-UI3-RCO` | 4.03 | `vsh_asset/Sce.PlayStation.PUI_UI3.rco` | not required | 1,092 resources: graphics, sounds, and 31 locale JSON payloads; no theme token set |
| `4-BASE-RCO` | 4.03 | `app/NPXS40087/psm/Application/resource/Sce.Vsh.ShellUI.Base.rco` | not required | Shell application resources; no theme token set |

The managed PE inside `9-THEME` is a reference image, but the surrounding SPRX is
a Mono AOT implementation. The default values below come from its native method
table and native bodies, not from the empty managed getter stubs.

## 9.00 shell defaults

### Default theme parameters

| Property | `UIColor` channels | Packed ARGB | RGB | Provenance |
|---|---:|---:|---:|---|
| `DefaultThemeFontColor` | `(255, 255, 255, 255)` | `0xFFFFFFFF` | `#FFFFFF` | `9-THEME`, native getter |
| `DefaultThemeFontShadowColor` | `(0, 0, 0, 0)` | `0x00000000` | `#000000` | `9-THEME`, native getter |
| `DefaultThemeFocusColor` | `(0, 186, 255, 0)` | `0x0000BAFF` | `#00BAFF` | `9-THEME`, native getter |
| `DefaultThemeBGLightColor` | `(255, 255, 255, 255)` | `0xFFFFFFFF` | `#FFFFFF` | `9-THEME`, native getter returns the referenced PUI `UIColors.White` field |
| `DefaultThemeHomeScreenDimmer` | `(0, 0, 0, 0)` | `0x00000000` | `#000000` | `9-THEME`, native getter |
| `DefaultThemeFuncScreenDimmer` | `(0, 0, 0, 0)` | `0x00000000` | `#000000` | `9-THEME`, native getter |
| `DefaultThemeTitleNameDimmer` | `(0, 0, 0, 0)` | `0x00000000` | `#000000` | `9-THEME`, native getter |
| `DefaultWidgetColor` | `(255, 255, 255, 255)` | `0xFFFFFFFF` | `#FFFFFF` | `9-THEME`, native getter returns the same PUI `UIColors.White` field |

Other defaults:

| Property | Value | Provenance |
|---|---:|---|
| `DefaultThemeColor` | `0` | `9-THEME` metadata |
| `DefaultLabel` | `default` | `9-THEME` metadata |
| `DefaultHomeBgmEnable` | `true` | `9-THEME` metadata/native constructor |
| `DefaultWidgetBorderRadiusRatio` | `1.0` | `9-THEME`, native getter |
| `FormatVersion` default | empty string | `9-THEME` metadata/native constructor |
| Current theme package format | `4.0` | `9-THEME` metadata |
| Supported theme package formats | `1.0`, `2.0`, `3.0`, `4.0` | `9-THEME` metadata |

The native `ThemeParam` constructor assigns the focus color to both
`FocusColor` and `FocusSecondaryColor`, assigns the white widget color to both
widget color slots, and initializes `IsFocusColorEnabled` to `true`.

### Default focus-color indices

`DefaultThemeFocusColors(int index)` ignores `index` and returns
`DefaultThemeFocusColor`. Therefore every index resolves to:

| Index | Packed ARGB | RGB | Alpha |
|---|---:|---:|---:|
| any integer | `0x0000BAFF` | `#00BAFF` | `0` |

Provenance: `9-THEME`, native body of `DefaultThemeFocusColors`.

## 9.00 design-token contract

### Storage and types

| Item | Value | Provenance |
|---|---|---|
| Theme root component | `theme` | `9-THEME` |
| Color-theme component | `color` | `9-THEME` |
| Shape-theme component | `shape` | `9-THEME` |
| Token filename | `design_tokens.json` | `9-THEME` |
| Custom-package skin file | `/skin.json` | `9-THEME` |
| Color token set type | string key to unsigned 32-bit color | `9-PUI` |
| Shape token set type | string key to 32-bit float | `9-PUI` |
| Default PUI resource prefix | `cxml://ui3/` | `9-PUI` |
| Color literal length | 9 characters | `9-THEME` |

The mounter selects a theme by ID and searches under the logical `theme/color`
or `theme/shape` branch for `design_tokens.json`. The absolute mount root is
supplied at runtime. Custom theme packages use `/skin.json`.

The color parser accepts a nine-character hash-prefixed eight-digit hex value,
parses its eight hex digits, and forces alpha opaque with
`value | 0xFF000000`. Consequently, a color successfully loaded from these token
files is stored as `0xFFRRGGBB`.

### Shell token mappings

| Token | Shell destination | Built-in fallback | Concrete file value |
|---|---|---:|---|
| `background.enabled` | `BackgroundLightColor` | `0xFFFFFFFF` / `#FFFFFF` | Data-driven; token file not present in supplied dumps |
| `base-mat.overlay.enabled` | `HomeScreenDimmer` | `0x00000000` / transparent black | Data-driven; token file not present in supplied dumps |
| `focus.stroke.enabled` | `FocusColor` | `0x0000BAFF` / RGB `#00BAFF` | Data-driven; token file not present in supplied dumps |
| `focus.fill.enabled` | `FocusSecondaryColor` | `0x0000BAFF` / RGB `#00BAFF` | Data-driven; token file not present in supplied dumps |

The focus override flag is cleared before parsing the two focus tokens and is
set when either focus token is found. Missing focus tokens therefore retain the
cyan fallback while recording that no custom focus color was loaded.

### Theme selector values

| 9.00 `ColorTheme` | Numeric value | 9.00 `ShapeTheme` | Numeric value |
|---|---:|---|---:|
| `Default` | 0 | `Default` | 0 |
| `Red` | 1 | `Soft` | 1 |
| `Purple` | 2 | `Sharp` | 2 |
| `Black` | 3 | `Round` | 3 |
| `Pink` | 4 | `DEBUG` | 99 |
| `Blue` | 5 |  |  |
| `Gray` | 6 |  |  |
| `DEBUG` | 99 |  |  |

Provenance: `9-PUI`.

## 9.00 focus shape and geometry

These are fixed UI3 renderer values, independent of the missing shape-token
files.

| Focus-render parameter | Value |
|---|---:|
| Stroke thickness | `3.0` |
| Stroke offset | `3.0` |
| Anti-alias extent | `1.5` px |
| Maximum in/out extension | `80.0` |
| List-item top margin | `3.0` |
| List-item bottom margin | `5.0` |
| Minimum edge-fade length | `10` |
| Area-rendering threshold | `0.4` |
| Stroke scale while hiding | `1.2` |
| Default area warp-fade distance | `80.0` px |
| Default area warp-fade threshold ratio | `0.1` |
| Area opacity decrease rate by size | `30.0` |
| Minimum area-opacity decrease by size | `0.5` |
| Default warp-gradient ratio intensity | `0.2` |
| Default warp-gradient toe value | `0.3` |

Related focus motion values:

| Parameter | Value |
|---|---:|
| In duration | `0.3` s |
| Out duration | `0.3` s |
| Press duration | `0.3` s |
| Move duration | `0.3` s |
| Warp duration | `0.25` s |
| Initial warp progress | `0.06666668` |
| Frame interval | `0.01666667` s |
| Noise move frequency | `0.25` |
| Shimmer speed | `1.0` |
| Shimmer frequency | `5.0` |

Provenance: `9-PUI`, UI3 focus renderer.

Absolute corner radii for `Default`, `Soft`, `Sharp`, and `Round` are
data-driven floats. Their `design_tokens.json` files were not present, so no
concrete per-theme radius map could be recovered. The only shell-level radius
default in the available implementation is the `1.0` widget radius ratio above.

## 4.03 legacy wave-theme palette

Firmware 4.03 has no `Sce.Vsh.ShellUI.Theme.dll.sprx` in the supplied filesystem.
It instead contains a concrete `Sce.Vsh.Theme.dll.sprx` wave/panel skin. This is
an authentic 4.03 shell palette, but it is not the later 9.00 UI3
`design_tokens.json` map.

### Focus hues

The base focus colors all carry alpha zero.

| 4.03 theme | Numeric value | Packed ARGB | RGB |
|---|---:|---:|---:|
| `Blue` | 0 | `0x0000BAFF` | `#00BAFF` |
| `Pink` | 1 | `0x00FF58BE` | `#FF58BE` |
| `Red` | 2 | `0x00FF214E` | `#FF214E` |
| `Navy` | 3 | `0x0021FFFD` | `#21FFFD` |
| `DarkGray` | 4 | `0x00D9E2FF` | `#D9E2FF` |
| `Gold` | 5 | `0x00FFB421` | `#FFB421` |
| `LightSteelBlue` | 6 | `0x00B7F7FF` | `#B7F7FF` |
| `Purple` | 7 | `0x00E573FF` | `#E573FF` |
| `DualColor0` | 16 | `0x0000BAFF` | `#00BAFF` |
| `DualColor1` | 17 | `0x0000BAFF` | `#00BAFF` |
| `DualColor2` | 18 | `0x0000BAFF` | `#00BAFF` |
| `DualColor3` | 19 | `0x0000BAFF` | `#00BAFF` |
| `Particle0` | 32 | `0x0000BAFF` | `#00BAFF` |
| `Particle1` | 33 | `0x0000BAFF` | `#00BAFF` |
| `Particle2` | 34 | `0x0000BAFF` | `#00BAFF` |
| `Particle3` | 35 | `0x0000BAFF` | `#00BAFF` |

Provenance: `4-THEME`, concrete `ThemeConstantsWaveColor` initializer.

The 4.03 blue focus RGB exactly matches the 9.00 built-in default focus RGB.

### Panel base RGB

The four source colors below are opaque before the component-specific opacity
mapping is applied.

| Theme | Normal | Bright | Dark | Menu dark |
|---|---:|---:|---:|---:|
| `Blue` | `#04298D` | `#3F7FDF` | `#031D61` | `#031D61` |
| `Pink` | `#BF4A57` | `#FFB1B9` | `#C94755` | `#A03643` |
| `Red` | `#701414` | `#C5272D` | `#4B080E` | `#420500` |
| `Navy` | `#00042B` | `#2144A6` | `#00042B` | `#000220` |
| `DarkGray` | `#363545` | `#A0A0B2` | `#282736` | `#282736` |
| `Gold` | `#B28600` | `#E4CF70` | `#AE8300` | `#A87700` |
| `LightSteelBlue` | `#659FB3` | `#85D8EB` | `#287A97` | `#20687F` |
| `Purple` | `#522E89` | `#A669FF` | `#381173` | `#3D1E6C` |
| `DualColor0` | `#2E9F9B` | `#5DDCDA` | `#00807B` | `#229490` |
| `DualColor1` | `#EAB33C` | `#FFFFFF` | `#E59D01` | `#D0A132` |
| `DualColor2` | `#AA3634` | `#FA4F55` | `#881B1C` | `#A33332` |
| `DualColor3` | `#00091D` | `#FFFFFF` | `#000005` | `#000A1D` |
| `Particle0` | `#091B46` | `#3A7DCB` | `#091B46` | `#0B1E48` |
| `Particle1` | `#BF4A57` | `#FFB1B9` | `#C94755` | `#AC3F4C` |
| `Particle2` | `#795307` | `#FFCD41` | `#533904` | `#AA7704` |
| `Particle3` | `#06070A` | `#FFFFFF` | `#000000` | `#080D13` |

The default black panel source is transparent black for all four entries.
Provenance: `4-THEME`, concrete panel color-set initializers.

### Panel component selection and opacity

| Component | Source RGB column | Standard theme alpha |
|---|---|---:|
| Tile | Bright | `76/255` (`0.29803923`) |
| Popup | Normal | `250/255` (`0.98039216`) |
| Black box | Dark | `153/255` (`0.6`) |
| Guide panel | Normal | `242/255` (`0.9490196`) |
| Overlay menu panel | Menu dark | `250/255` (`0.98039216`) |

Tile-alpha exceptions:

| Theme | Tile alpha |
|---|---:|
| `DualColor1` | `38/255` (`0.14901961`) |
| `DualColor3` | `20/255` (`0.07843137`) |
| `Particle3` | `20/255` (`0.07843137`) |

The default black skin uses:

| Tile | Popup | Black box | Guide panel | Overlay menu |
|---:|---:|---:|---:|---:|
| `178/255` | `242/255` | `204/255` | `216/255` | `247/255` |

The 4.03 skin also specifies a high-contrast minimum background opacity of
`0.85` and a background lightness reduction rate of `0.3`.

Provenance: `4-THEME`, concrete skin-creation and opacity-map initializers.

## Unresolved data

| Data | Status |
|---|---|
| 9.00 per-color-theme token maps | Data-driven; `design_tokens.json` payloads not present in the supplied 9.00 filesystem |
| 9.00 per-shape-theme float maps | Data-driven; `design_tokens.json` payloads not present in the supplied 9.00 filesystem |
| Concrete `background.enabled` value selected by each 9.00 color theme | Not located |
| Concrete `base-mat.overlay.enabled` value selected by each 9.00 color theme | Not located |
| Concrete `focus.stroke.enabled` and `focus.fill.enabled` values selected by each 9.00 color theme | Not located |
| Absolute corner radii for 9.00 `Default`, `Soft`, `Sharp`, and `Round` | Not located |
| Direct 4.03-to-9.00 comparison of the UI3 token maps | Impossible from these images: the 4.03 ShellUI theme assembly is absent and the 9.00 token payloads are absent |

The 4.03 `Sce.PlayStation.PUI_UI3.rco` was parsed rather than inferred from its
filename. Its JSON payloads are locale bundles named `default`, `en-US`,
`ja-JP`, and other locales; none is a color or shape token set. No RCO exists in
the supplied 9.00 tree.
