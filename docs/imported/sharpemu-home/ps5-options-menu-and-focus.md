# PS5 shell: the moving focus highlight and the option menu

Two shell surfaces that Prosperismo currently approximates from the wrong
source. This note records what the shell actually does, with a locator for
every value.

Sources used here, in decreasing order of confidence:

| Tag | What it is |
|---|---|
| `RN-BASE` | `NPXS40141.base.js` — the shared React Native component library (`react-native-playstation`, branch `rnps__ppr_releases_03.00`). Build path prefix `.../apennine/node_modules/react-native-playstation/Libraries/...`. |
| `RN-HOME` | `NPXS40002.js` — the home-ui application (`rnps-home_v2_ppr_releases_03.00`). |
| `RN-CC` | `NPXS40003.js` — the control-center application (`rol-center_v2_ppr_releases_03.00`). |
| `PUI` | `Sce.PlayStation.PUI.dll.sprx` (9.00 tree) — the native UI3 widget toolkit. Symbol names are read directly out of the module; numeric values in the tables marked `PUI` were mined earlier and are recorded in `ps5-shell-theme.md`. |

Locators of the form `RN-BASE:8489` mean "that file, that line". Module ids
of the form `IirGKmQ9` are the bundle's own module keys; a module body is
addressed as `__haul_application.l(<n>,` in the app bundles and as
`"<id>": function(e, t, n)` in the base library.

---

## 1. The moving focus highlight

### 1.1 What it is, architecturally

The focus highlight is **not** a per-widget decoration. It is a single
renderer per focus scene (`focusRenderManager` / `FocusRenderWidget`, `PUI`)
that owns one rectangle and draws it as a shader quad on its own compositing
plane. Widgets do not draw it; they only publish a rect to it.

Three consequences that Prosperismo's model does not have:

1. **One highlight, one rect.** When focus moves the renderer does not
   cross-fade two highlights. It *retargets its single rect* and animates the
   geometry from the old rect to the new one. Symbols: `warpStartPositionRect`,
   `warpStartRadius`, `warpStartOpacity`, `warpStartAreaOpacity`,
   `CalcCurrentPositionRectByWarp`, `StartWarp`, `isInWarp`, `isFirstWarpFrame`
   (`PUI`).
2. **The highlight sits between layers, not on top.** A view can opt to render
   *in front of* the highlight with `renderFrontOfFocus` (`RN-BASE:16100`,
   used e.g. at `RN-HOME:17055`). So the plane is composited under selected
   children — an avatar can poke through the ring.
3. **The highlight rect is decoupled from the widget rect.** Three overrides
   exist on `focusCustomSettings` (`RN-BASE:16101`-`16120`):
   - `focusImageRectangle {x,y,width,height}` — draw the highlight on a
     different rect than the view's own bounds.
   - `searchHintRectangle {x,y,width,height}` — use a different rect for
     directional focus search scoring than for drawing.
   - `focusTarget` — delegate the highlight to another node entirely.
   - `borderRadius` — highlight corner radius, independent of the view's.

### 1.2 How it travels ("warp")

Focus movement runs two animations at once:

- a **move** of the rect (`MovingDuration` / `MovingAnimationCurve`), and
- a **warp**, which is a *deformation* applied on top of the move
  (`AnimatablePropertyWarpProgress`, `WarpAnimationDuration`,
  `WarpAnimationCurve`).

The warp is what produces the "it stretches toward the new tile and snaps
back" look. It is implemented in the shader, not in layout: the uniform
`uniform_WarpDistortionMatrix` (fed by `CalcWarpDistortionMatrix`) deforms
the quad, and the trailing part of the deformed quad is faded out over a
fixed pixel distance (`uniform_AreaWarpFadePixel`, `CalcAreaWarpFadeLength`)
using a gradient shaped by `uniform_WarpGradientCurveByRatioIntensity` and
`uniform_WarpGradientCurveToeValue` (`CalcWarpGradientCurveByRatio`).

So: **it stretches/smears, it does not leave a discrete trail sprite, and it
does not fade one ring out while fading another in.**

The full uniform set of the focus shader, which is effectively the spec of
what the highlight can do (`PUI`, `uniform_*` symbols):

| Uniform | Role |
|---|---|
| `uniform_borderTargetRect` | destination rect of the ring |
| `uniform_radius` | corner radius |
| `uniform_thickness` | stroke thickness |
| `uniform_strokePosition` | stroke inset/outset position |
| `uniform_borderColor`, `uniform_ThemedFocusColor` | ring colour; theme colour |
| `uniform_borderParam` | packed border params |
| `uniform_ShowAlpha` | show/hide opacity |
| `uniform_inOutScale` | the in/out scale pulse |
| `uniform_moving` | movement amount fed to the shader |
| `uniform_pressing` | press amount fed to the shader |
| `uniform_WarpDistortionMatrix` | the warp deformation |
| `uniform_AreaWarpFadePixel` | length over which the warped area fades |
| `uniform_WarpGradientCurveByRatioIntensity`, `uniform_WarpGradientCurveToeValue` | shape of the warp gradient |
| `uniform_AreaOpacityDecreaseRateBySize` | interior fill fades as the rect grows |
| `uniform_NoiseChangeParam` | scroll/animation of the noise texture |
| `uniform_ShimmerParam` | shimmer phase/speed |
| `uniform_offset`, `uniform_pixel`, `uniform_bufferAspect`, `uniform_angle`, `uniform_ratio` | framing/aspect |

The noise and shimmer are separate from the warp and run continuously while
the highlight is visible: `CalcNoiseChangeParam`, `CalcShimmerParam`,
`NoiseTexture` / `NoiseImage` / `FocusNoise` (`PUI`). The RCO name table
carries the actual textures — `image_focus_frame_2`, `image_focus_list_item`,
`image_focus_noise` (see `ps5-shell-motion.md`). `FocusFrame2` /
`FocusThickness2` in `PUI` are the same "frame 2" asset, i.e. the ring is a
**textured nine-patch frame plus a scrolling noise texture**, not a solid
stroke.

### 1.3 Concrete numbers

Values from `PUI` (already recorded in `ps5-shell-theme.md` §"9.00 focus
shape and geometry"; repeated here so this document stands alone) plus the
derivations they support:

| Property | Value | Source locator |
|---|---|---|
| Stroke thickness | `3.0` | `PUI` `FocusThickness` / `uniform_thickness` |
| Stroke offset (position) | `3.0` | `PUI` `StrokePosition` / `uniform_strokePosition` |
| Anti-alias extent | `1.5` px | `PUI` focus renderer |
| Maximum in/out extension | `80.0` | `PUI` focus renderer |
| List-item focus top margin | `3.0` | `PUI` `FocusStyleListItemTopMargin` |
| List-item focus bottom margin | `5.0` | `PUI` `FocusStyleListItemBottomMargin` |
| Minimum edge-fade length | `10` | `PUI` `EdgeFadeMinLength` (exact, read from assembly metadata) |
| Area (fill) rendering threshold | `0.4` | `PUI` focus renderer |
| Stroke scale while hiding | `1.2` | `PUI` `FocusHide` |
| Default area warp-fade distance | `80.0` px | `PUI` `DefaultAreaWarpFadePixel` |
| Default area warp-fade threshold ratio | `0.1` | `PUI` `DefaultAreaWarpFadeThreasholdRatio` (sic) |
| Area opacity decrease rate by size | `30.0` | `PUI` `DefaultAreaOpacityDecreaseRateBySize` |
| Minimum area-opacity decrease by size | `0.5` | `PUI` `DefaultAreaOpacityMinimumDecreaseValueBySize` |
| Warp-gradient ratio intensity | `0.2` | `PUI` `DefaultWarpGradientCurveByRatioIntensity` |
| Warp-gradient toe value | `0.3` | `PUI` `DefaultWarpGradientCurveToeValue` |
| Focus-in duration | `0.3` s | `PUI` `InMotionDuration` |
| Focus-out duration | `0.3` s | `PUI` `OutMotionDuration` |
| Press duration | `0.3` s | `PUI` `PressingDuration` |
| Move duration | `0.3` s | `PUI` `MovingDuration` |
| Warp duration | `0.25` s | `PUI` `WarpAnimationDuration` |
| Initial warp progress | `0.06666668` | `PUI` `InitialWarpProgress` |
| Frame interval | `0.01666667` s | `PUI` focus renderer |
| Noise move frequency | `0.25` | `PUI` `NoiseMoveFrequency` |
| Shimmer speed | `1.0` | `PUI` `ShimmerSpeed` |
| Shimmer frequency | `5.0` | `PUI` `ShimmerFrequency` |

Derivations that fall out of those numbers and that a reimplementation needs:

- **The warp is exactly 15 frames at 60 Hz.** `0.25 / 0.01666667 = 15.0`, and
  `InitialWarpProgress = 0.06666668 = 1/15`. The renderer seeds the warp at
  progress `1/15`, i.e. it starts already one frame in and never renders a
  zero-warp frame. `isFirstWarpFrame` (`PUI`) exists precisely to special-case
  that seeding.
- **Warp is shorter than the move.** Move `0.3` s vs warp `0.25` s: the
  deformation has fully relaxed `0.05` s (3 frames) before the rect finishes
  settling. So the highlight arrives *un-stretched* and then finishes easing
  into place — it does not snap.
- **`80.0` appears twice** (max in/out extension and the area warp-fade
  distance). The stretch can extend up to 80 px past the rect, and the
  extension is exactly what fades over 80 px. They are the same budget.
- **The interior fill is size-dependent**, not constant: rate `30.0` with a
  floor of `0.5`, gated by an area-render threshold of `0.4`. Big focused
  rects (a hero tile) get a much weaker interior wash than small ones (a list
  row).
- **Hiding blows the stroke out**, scale `1.2`, while `ShowAlpha` falls —
  the ring expands slightly as it disappears rather than shrinking.

### 1.4 Focus scoping and travel rules (JS side)

`FocusLayerPS` (`RN-BASE:7791`, native `RCTFocusLayerPS`, source
`Libraries/Components/FocusLayerPS/FocusLayerPS.ps.js`) is the scope inside
which the highlight lives.

| Prop / command | Values | Source locator |
|---|---|---|
| `focusInBehavior` | `{type:"nearestItem"}` / `{type:"lastFocusedItem", initialFocusItem}` / `{type:"consistent", consistentFocusItem (required), strict}` | `RN-BASE:7804`-`7813` |
| `lightMat` | `"auto"` \| `"forceOn"` \| `"forceOff"` \| `"none"` | `RN-BASE:7816`, also on every view at `RN-BASE:16093` |
| `captureFocusIfNoFocusableChild` | bool | `RN-BASE:7817` |
| `onHighlight` / `onDehighlight` | callbacks fired when the layer gains/loses the highlight | `RN-BASE:7814`-`7815` |
| command `resetLastFocusedItem` | clears the layer's remembered item | `RN-BASE:7820` |
| `ListViewPS.focusInBehavior` | narrower: only `"nearestItem"` \| `"lastFocusedItem"` | `RN-BASE:1081` |

Per-view focus control lives on `focusCustomSettings` (`RN-BASE:16101`):

| Field | Values | Source locator |
|---|---|---|
| `focusStyle` | `rectangle`, `listItem`, `none`, `glow`, `osk`, `rectangle2`, `roundedRectangle`, `ignore`, `exit` | `RN-BASE:16102` |
| `focusLineColor` | colour (number) | `RN-BASE:16103` |
| `focusImageFilterColor` | colour (number) | `RN-BASE:16104` |
| `canMove{Left,Right,Up,Down}` | bool — hard-gate a direction | `RN-BASE:16105`-`16108` |
| `canMove{...}WithKeyRepeat` | bool — gate only the auto-repeat | `RN-BASE:16109`-`16112` |
| `{left,right,up,down}Candidate` | string — name of the widget to jump to | `RN-BASE:16113`-`16116` |
| `searchHintRectangle` | rect used for directional search | `RN-BASE:16117` |
| `focusImageRectangle` | rect the highlight is drawn on | `RN-BASE:16118` |
| `focusTarget` | node the highlight is delegated to | `RN-BASE:16119` |
| `borderRadius` | number | `RN-BASE:16120` |

Related view props (`RN-BASE:16091`-`16140`): `renderFrontOfFocus`,
`ignoreBorderRadius`, `ignoreParentTransform`, `ignoreDimByModal`
(`none`/`onlySelf`/`childrenAndSelf`), `soundEffectsEnabled`,
`cursorEndSFXEnabledDirection` (`none`/`left`/`up`/`right`/`down`/`all` — the
"you hit the end of the list" thud, per direction), and `shortcutKeyGuide`.

Directional search on the native side is a scored search, not a grid walk:
`calcFocusScore`, `focusSearchSlopeCoef`, `focusSearchSlopeRate`,
`focusSearchSlopeRate2`, `beginFocusSearch` / `endFocusSearch`,
`FindFocusableWidgetRecursively`, `FocusSearchCandidate` (`PUI`). Named
candidate overrides (`leftCandidate`, …) short-circuit it.

### 1.5 `lightMat` / `baseMat` — the surface under the focus

`lightMat` and `baseMat` are two different things and both are often confused
with the highlight.

- **`baseMat`** is the darkening/gradient mat drawn *behind* content so text
  stays legible. `RN-BASE:16129`-`16136`: `baseMat {type, position, length}`
  plus flattened `baseMatType` / `baseMatPosition` / `baseMatLength`.
  `position` is `top`\|`bottom`\|`left`\|`right`. Types (`RN-BASE:27718`,
  module `mGJPYwof`): `overlay-gradient`, `overlay-gradient-tile`,
  `overlay-solid`, `overlay-transparent`, `overlay-solid-transparent`,
  `overlay-panel`, `fullscreen-gradient`, `fullscreen-gradient-uc`,
  `overlay-nofocus`. Home tiles default to `overlay-solid`
  (`RN-HOME:54792`) and the stacked tile applies it at `position:"bottom"`
  with `length` = tile height (`RN-HOME:54809`); the tile grid uses
  `overlay-gradient-tile` (`RN-HOME:54210`).
- **Fullscreen basemat** is a separate component, `FullScreenBasematPS` →
  `RCTFullScreenBasematPS`, with its own much smaller enum:
  `none`\|`flat`\|`linear`\|`ellipseNarrow` (`RN-BASE:7200`). home-ui's
  `DEFAULT_BASEMAT` is `"EllipseNarrow"` (`RN-HOME:9198`, again
  `RN-HOME:61545`) — i.e. the home screen background is an elliptical vignette.
- **`lightMat`** is an animated lighting layer with its own opacity channel:
  `LightMatState`, `LightMatOpacity`, `AnimatablePropertyLightMatOpacity`,
  `LightMatParam` (`PUI`). In JS it is only a four-way switch
  (`auto`/`forceOn`/`forceOff`/`none`) on both `FocusLayerPS` and every view.
  This is the same "background light steered by the focused rect" that
  `ps5-shell-overlays.md` describes on the 4.03 `BGLayer` side
  (`focusMoveThreshold` 10 px, `FocusCheckTimeout` 0.1 s).

### 1.6 The shell's motion is spring-driven, ours is not

The focus ring itself is native, but the layout motion around it is JS
`Animated.spring`, with four named parameter sets (`RN-HOME:4162`-`4187`,
module 49):

| Preset | stiffness | damping | mass | overshootClamping | Source locator |
|---|---:|---:|---:|---|---|
| `SPRING_OPTIONS_SLOW` | `130` | `25` | `1` | `true` | `RN-HOME:4162` |
| `SPRING_OPTIONS_SLOWER` | `100` | `20` | `1` | `true` | `RN-HOME:4169` |
| `SPRING_OPTIONS_FAST` | `200` | `100` | `0.2` | *(absent → false)* | `RN-HOME:4176` |
| `SPRING_OPTIONS_FASTER` | `600` | `100` | `0.2` | *(absent → false)* | `RN-HOME:4182` |

All four set `useNativeDriver: true`. Note the split: the two `SLOW*` presets
have mass `1` and clamp overshoot; the two `FAST*` presets have mass `0.2`,
damping `100` and **allow overshoot**.

Confirmed uses (none of them is the focus ring — checked):

| Motion | Preset | Source locator |
|---|---|---|
| Home ↔ hub vertical translate | `FAST` | `RN-HOME:9803`-`9805`, `:9823`-`9827` |
| Experience-title opacity / title-strip translate | `FAST` | `RN-HOME:40759`-`40767` |
| Experience header transition | `FAST` + `FASTER` (opacity) | `RN-HOME:41433`-`41437` |
| Space switching (per-space opacity) | `SLOW`, with `animated:false` on the outgoing space | `RN-HOME:42209`-`42213` |

So the answer to "is focus/enlarge spring-driven?" is: **the enlarge is, the
ring is not.** The experience switcher scales tiles between
`EXPERIENCE_SIZE = 106` and `SCALED_EXP_SIZE = 168`
(`EXPERIENCE_SCALE = 168/106 ≈ 1.5849`), and down to
`MINIMIZED_EXP_SIZE = 80` (`MINIMIZED_EXP_SCALE = 80/168`), with margins
`SCALED_EXP_MARGIN_LEFT = 172`, `MINIMIZED_EXP_MARGIN_TOP/LEFT = 48`,
`VERTICAL_HEIGHT_CHANGE = 40`, `BORDER_RADIUS = 16`, in a `1920 × 168`
container (`RN-HOME:3215`-`3229`). That geometry is animated with springs;
the ring then warps to follow it.

One focus-specific detail falls out of the same stylesheet: the switcher's
`focusContainer` uses `borderRadius: 168/106 * 16 ≈ 25.36`
(`RN-HOME:3235`-`3236`) — i.e. **the focus container's corner radius is
pre-multiplied by the tile's scale factor** so that after the scale
transform the ring's visual radius lands back on `16`. A reimplementation
that scales a control and leaves the highlight radius alone will look wrong
at exactly this point.

> **Corrected: the direction is backwards.** The paragraph above says the radius is
> pre-compensated so that the on-screen radius "lands back on 16". It does not.
> `borderRadius: 168 / 106 * 16` is a *multiply*, and the style is applied to the
> already-enlarged 168 px box, so the on-screen focused radius is **25.358490566**,
> not 16.
>
> The invariant is not a constant radius. It is a constant **radius-to-side ratio**:
>
> | Side | Radius | Ratio |
> |---|---|---|
> | 106 (resting) | 16 | 0.150943 |
> | 168 (focused) | 25.358490566 | 0.150943 |
> | 80 (minimised badge) | 12.075 | 0.150943, INFERRED by applying the ratio |
>
> Provenance: HOME m25:3224 `n.BORDER_RADIUS = 16;` and HOME m25:3236
> `borderRadius: 168 / 106 * 16`, both EXACT; see `docs/ps5-rn-layout.md` 1.5.
> The practical instruction is the opposite of the one above: **drive the radius
> from the current side length, do not pre-divide it and do not hold it fixed.**
> A control that scales its own geometry (rather than applying a render transform)
> must recompute the radius as `0.150943 * side` on every frame of the enlarge.

Prosperismo has no spring integrator at all — `ShellMotion` is ease-out
curves plus a linear hide. That is a real, separate gap from the focus-ring
gap.

### 1.7 What Prosperismo currently gets wrong

| # | Current behaviour | Reality | Fix |
|---|---|---|---|
| 1 | Focus is a **static cyan `BoxShadow`** on the selected tile: `0 0 22 3 #4400BAFF` layered over `0 6 14 0 #55000000` (`src/SharpEmu.GUI/App.axaml:341`-`344`). | The shell draws a **3.0 px textured stroke** (`image_focus_frame_2`) offset `3.0` px outside the rect, with `1.5` px AA, plus a size-dependent interior wash. It is a ring, not a glow. | Draw a stroke/ring, keep the drop shadow separate. |
| 2 | The glow **snaps** on/off when selection changes — the `BoxShadow` setter is a plain style setter with no transition, and the only `Transitions` block on the tile grid (`App.axaml:316`-`320`) covers `RenderTransform` only. | A single highlight **travels**: 0.3 s move, with a 0.25 s / 15-frame warp deformation on top, seeded at progress 1/15. | This is the headline gap. Nothing short of a travelling single highlight reads as PS5. |
| 3 | No stretch, no trail. | During travel the ring extends up to **80 px** in the direction of motion and the extension fades over **80 px** with a gradient (`intensity 0.2`, `toe 0.3`). | Implement the extension + fade before worrying about noise/shimmer. |
| 4 | Highlight is a constant appearance. | The interior fill **fades as the focused rect grows** (rate `30.0`, floor `0.5`, area threshold `0.4`), and there is a continuously animated **noise** (`move frequency 0.25`) and **shimmer** (`speed 1.0`, `frequency 5.0`). | At minimum, vary interior alpha with rect size. |
| 5 | Focus disappears instantly on blur. | Focus-out is **0.3 s** and the stroke **scales up by 1.2** while alpha falls. | |
| 6 | Highlight always drawn on top of the tile. | The highlight is a **plane**; children can opt in front of it (`renderFrontOfFocus`). | Only matters once the ring is real. |
| 7 | Highlight rect == control bounds. | The shell routinely draws the highlight on a **different rect** than the widget (`focusImageRectangle`, `focusTarget`) and searches on a **third** rect (`searchHintRectangle`). | Needed for hero tiles / irregular rows. |
| 8 | `#00BAFF` is used as *the* focus colour. | The focus colour is a **theme uniform** (`uniform_ThemedFocusColor`, `ThemedFocusColor`, `DefaultFocusColor` in `PUI`), and `ps5-home-theme.md` already records that `#00BAFF` does **not** appear anywhere in the JS. Treat it as a placeholder, not ground truth. | |

| 9 | No spring anywhere in `ShellMotion.cs`. | The shell's own navigation/scale motion is `Animated.spring` with four named presets (§1.6). | Add a spring integrator; use `SLOW` (130/25/1, clamped) for layout, `FAST` (200/100/0.2) for opacity/translate. |
| 10 | Tile scale-on-focus, if added, would keep a fixed corner radius. | ~~The shell **pre-divides** the radius by the scale factor (`168/106 * 16`) so the post-transform radius is `16`.~~ **Corrected:** it **multiplies**. The on-screen focused radius is `168/106 * 16 = 25.358490566`. The invariant is a constant radius-to-side ratio of **0.150943**. See §1.6. | Drive the radius from the side length: `radius = 0.150943 * side`. |

### 1.8 Gaps

- `WarpAnimationCurve`, `MovingAnimationCurve`, `InOutAnimationCurve`,
  `PressingAnimationCurve` exist as symbols in `PUI` but their curve
  parameters were not recovered. `PUI` has both `AnimationCurveCubicBezier`
  and `ParametricAnimationCurve` implementations, so the focus curves may or
  may not be the same parametric family as the JS easings in §2.2.
- The exact form of `CalcWarpDistortionMatrix` (is the stretch affine, or a
  per-corner deformation?) is not recovered — only that it is a matrix
  uniform.
- `NoiseChangeParam` and `ShimmerParam` are packed vectors; only the three
  scalar frequencies/speeds above are known.
- The focus colour, its alpha, and the interior-fill colour are not recovered
  from either source.
- `focusStyle` enum members are known by name only; the geometric difference
  between `rectangle`, `rectangle2` and `roundedRectangle` is not recovered.
  `listItem` at least is known to add `3.0`/`5.0` top/bottom margins.

---

## 2. The option menu

### 2.1 What it is

The home-screen tile option menu is `OptionsMenuPS` (`RN-BASE:16219`, module
`SH5Dy6ls`), a subclass of `PopupMenuBase`
(`Libraries/PopupMenu/PopupMenuBase.ps.js`, `RN-BASE:8449`, module
`IirGKmQ9`). It renders the native view `RCTOptionsMenu` (`RN-BASE:16281`);
the generic popup menu renders `RCTPopupMenu` (`RN-BASE:8489`).

The JS wrapper for home is
`packages/rnps-js-modules-experience-options-menu/src/optionsMenu.tsx`
(`RN-HOME:37279`), consumed by `useExperienceOptionsMenu` (`RN-HOME:36978`)
→ `useTileOptionsMenu` (`RN-HOME:40309`) → `TilesOptionsMenu`
(`packages/home-ui/src/components/TileOptionsMenu/index.tsx`,
`RN-HOME:60796`-`60853`).

**The headline structural fact: the menu has two sections, not one.**
`contextItems` (section 0 — actions for the focused title) and `globalItems`
(section 1 — system actions) are separate data-source sections
(`RN-BASE:16242`-`16256`; wired at `RN-HOME:37255`-`37256`). On the home
screen `globalItems` currently holds exactly one conditional row, the
eject-disc button, added only when an `onEjectDisc` handler exists
(`RN-HOME:37037`-`37048`).

### 2.2 Values recoverable from the JS

| Property | Value | Source locator |
|---|---|---|
| Container layout | `flexDirection:"row"`, `position:"absolute"` | `RN-BASE:8684`-`8687` (`O.menu`, applied `RN-BASE:8593`) |
| List height | native constant `popupMenuListViewHeight` | `RN-BASE:8682` |
| Row max width | native constant `popupMenuMaxWidth`; overridable by `style.maxWidth` | `RN-BASE:8632` |
| Row min width | pass-through of `style.minWidth`, stripped off the container and re-applied per row | `RN-BASE:8580`-`8582`, `RN-BASE:8631` |
| Row padding L/R | native constants `itemPaddingLeft` / `itemPaddingRight`; overridable via same-named props | `RN-BASE:8633`-`8634` |
| Rows are single-line | `singleLine: true`, forced on every row | `RN-BASE:8629` |
| Row focus style | `focusCustomSettings.focusStyle = "rectangle"` (row may override) | `RN-BASE:8618`-`8620` |
| First row extra top padding | `12` | `RN-BASE:8613`-`8614`, `:8625` |
| Separator rule | shown on every row **except** the first (`showSeparator: !isFirst`); the list itself has `showSeparator:false` | `RN-BASE:8623`-`8624`, `:8652` |
| Rows are not individually focusable | `rowItemFocusable: false` (the list owns focus) | `RN-BASE:8653` |
| Dropdown rows | forced `menuPosition:"right"` | `RN-BASE:8621`-`8622` |
| Section header style attr | `styleAttr:"listOnPopup"`, `isTopHeader` on index 0 | `RN-BASE:8626`-`8627` |
| Section header padding | `paddingLeft:16`, `paddingRight:48`, `paddingBottom:8`, `paddingTop:4`, `marginTop:0` | `RN-BASE:31528`-`31534` |
| Section header font | `FontSizePS.SizeXSmall` (native token), `fontWeight:"normal"` | `RN-BASE:31548`-`31549` |
| Section header opacity on a popup | `0.7` (vs `1.0` in an in-page list) | `RN-BASE:31554`-`31556` |
| Section header separator inset | `marginHorizontal: 16` on a popup (component default is `24`) | `RN-BASE:31512`-`31515`; default `RN-BASE:18884`-`18886` |
| Separator height | native constant `RCTSeparator.defaultHeight` | `RN-BASE:33212`-`33213` |
| Placement enums | `menuPosition` `top\|bottom\|left\|right`; `horizontalAlignment` `left\|right\|center`; `verticalAlignment` `top\|bottom\|center`; `collision` `flip\|fit\|flipfit\|none` | `RN-BASE:23007`-`23014` |
| Default alignment **only when there is no anchor** | `menuPosition/horizontalAlign/verticalAlign = "center"` | `RN-BASE:8584` |
| Home tile menu anchoring | `targetComponent` = `findNodeHandle` of the focused tile; **no `menuStyle` passed at all**, so placement is the native default | `RN-HOME:37259`, call site `RN-HOME:37253`-`37265` |
| Submenu placement | `{collision:"flipfit", menuPosition:"right", horizontalAlign:"left", subMenu:true}` | `RN-BASE:4625`-`4630`, used `RN-BASE:4634` |
| Control-center menus | `{menuPosition:"right", verticalAlign:"bottom", collision:"fit"}` | `RN-CC:67886`-`67888`, `RN-CC:92393`, `RN-CC:104708`, `RN-CC:113292` |
| `dimBackground` prop | exists on `PopupMenuBase`, **never set anywhere in any bundle** | `RN-BASE:8674` |
| Row description reveal animation | preset `easeOutPS` (300 ms / `easeOutBlastPS` / opacity), duration+delay overridden by native `descriptionAnimDur` / `descriptionAnimDelay` (seconds ×1000) | `RN-BASE:4462`-`4470`, again `RN-BASE:16009`-`16012` |
| `initialFocusItem` `{sectionIndex,itemIndex}` | supported, **not set** by the home tile menu | `RN-BASE:16276`-`16279` |
| `optionsMenuItemsCount` | `contextItems.length`, used only to decide whether to show the Options hint | `RN-HOME:37266`-`37268` |

The shell's easing library, which is the ground truth behind our
`EaseOutBlast` (`RN-BASE:18065`-`18153`, module `XqLWDmeE`):

```
parametricCurve(back, flat):
    r = 9*flat + 1
    i = 800 / (600*flat + 200) * 0.5
    o = min(x*i + (back > 0 ? (1-i)*back : 0), 1)
    back <= 0 :  1 - (1-o)^r * (1 + back)
    back >= 1 :  o^r
    o <  back :  (o/back)^r * back
    else      :  (1 - (1 - (o-back)/(1-back))^r) * (1-back) + back
```

| Named curve | `parametricCurve(back, flat)` | Source locator |
|---|---|---|
| `easeOutBlastPS` (= `easeOutPS`, = `Easing.ease`) | `(0, 1)` → r=10, i=0.5 | `RN-BASE:18135`, `:18149`, `:18074` |
| `easeOutBreezePS` | `(0, 0.4)` → r=4.6, i≈0.909 | `RN-BASE:18137` |
| `easeSmoothOutBlastPS` | `(0.05, 1)` | `RN-BASE:18139` |
| `easeSmoothOutBreezePS` | `(0.05, 0.4)` | `RN-BASE:18141` |
| `easeFlyingOutBlastPS` | `(-0.4, 1)` | `RN-BASE:18143` |
| `easeFlyingOutBreezePS` | `(-0.4, 0.4)` | `RN-BASE:18145` |
| `easeInPS` | `(1, 1)` | `RN-BASE:18147` |
| `easeInOutPS` | `inOut(quad)` | `RN-BASE:18151` |

Note the negative-`back` curves start at a non-zero output (`easeFlyingOutBlastPS(0) = 0.4`)
— they are deliberate "already in flight" curves.

`LayoutAnimation` presets, the closest thing in JS to a transient-surface
show/hide spec (`RN-BASE:28244`-`28286`, module `nouIyNcp`):

| Preset | create | update | delete |
|---|---|---|---|
| `fadeInOutPS` | `200 ms`, delay `50 ms`, `easeSmoothOutBreezePS`, opacity | `200 ms`, delay `0`, `easeInEaseOut`, opacity | `100 ms`, delay `0`, `easeSmoothOutBreezePS`, opacity |
| `easeOutPS` | `300 ms`, `easeOutBlastPS`, opacity | — | — |
| `easeInEaseOut` | `300 ms` | — | — |
| `linear` | `500 ms` | — | — |
| `spring` | as `fadeInOutPS` | `700 ms`, spring, damping `0.4` | as `fadeInOutPS` |

### 2.2b A fully JS-implemented shell popup, with real numbers

`OptionsMenuPS` hides its geometry in native code, but the control-center
**function popup** is built in JS and is the closest thing in the shell to a
menu card whose numbers we can actually read. Source
`apps/control-center/src/modules/function-control-bar/components/Popup/index.js`
(`RN-CC:115899`), stylesheet module 528 (`RN-CC:53452`).

| Property | Value | Source locator |
|---|---|---|
| `DEFAULT_MENU_WIDTH` | `652` | `RN-CC:53462` |
| Popup `minWidth` / `maxWidth` | `652` / `784` | `RN-CC:53468`-`53469` |
| Popup `minHeight` / `maxHeight` | `216` / `810` | `RN-CC:53470`-`53471` |
| Popup position | `absolute`, `bottom: 190`, `paddingBottom: 8` | `RN-CC:53472`-`53474` |
| Card corner radius | `16` | `RN-CC:53478` |
| Card background | `rgba(8, 10, 15, 1.0)` (= `#080A0F`, fully opaque) | `RN-CC:53479` |
| Backdrop | `dimBackground: false` — **no scrim** | `RN-CC:115985` |
| Modal transparency | `transparent: false` | `RN-CC:115977` |
| Horizontal anchoring | `left = anchorX − width/2 + BUTTON_CONTAINER_WIDTH/2`, clamped to `left ≥ SYSTEM_MARGIN` and `left + width ≤ SYSTEM_WIDTH − SYSTEM_MARGIN` | `RN-CC:115943`-`115953` |
| `BUTTON_CONTAINER_WIDTH` | `112` | `RN-CC:22056`, again `RN-CC:85379` |
| `SYSTEM_MARGIN` / `SYSTEM_WIDTH` | `84` / `1920` | `RN-CC:31409`-`31410` |

That is: **centre the card on the anchor button, then slide it inward until
it clears an 84 px screen margin** — a real "fit" collision implementation,
matching `collision:"fit"` in the prop enum.

Its show/hide transition (`RN-CC:116134`-`116145`, duplicated verbatim at
`RN-CC:246114`-`246125`) is passed to the native `Modal` as `animationType`:

| Phase | Duration | Delay | Curve |
|---|---:|---:|---|
| show | `250` ms | `50` ms | `easeOutBlastPS` |
| hide | `300` ms | `0` | `linear` |

Two additional JS animations sit on top of it:

| Animation | Values | Source locator |
|---|---|---|
| `useModalAnimation` — plays when the whole app loses visibility | parallel: opacity `→0` and `translateY → 20`, both `100` ms, `Easing.linear`, native driver | `RN-CC:116098` |
| `useModalContentsAnimation` — the card's contents | in: opacity `→1`, `150` ms linear; out: opacity `→0`, `100` ms linear | `RN-CC:116118` |

And a JS-implemented menu **row** (control-center menu list item, module 240,
`RN-CC:24229`-`24272`):

| Property | Value | Source locator |
|---|---|---|
| `LIST_ITEM_HEIGHT` / row `minHeight` | `98` | `RN-CC:24229`, `:24267` |
| Row layout | `flexDirection:"row"`, `alignItems:"center"` | `RN-CC:24266`-`24268` |
| `LEFT_ICON_SIZE` | `56` | `RN-CC:24230` |
| `LEFT_ICON_MARGIN_LEFT` / `_RIGHT` | `16` / `20` | `RN-CC:24231`-`24232` |
| Right icon box | `48 × 48`, `marginHorizontal: 16` | `RN-CC:24234`-`24240`, `:24248` |
| Left icon in the with-description variant | `alignSelf:"flex-start"`, `marginTop: 21` | `RN-CC:24269`-`24271` |
| Profile row variant height | `90`, `marginBottom: 2` | `RN-CC:24252`-`24256` |
| Enclosing list | `marginHorizontal: 8`, `marginBottom: 16` | `RN-CC:24297`-`24300` |

These are control-center numbers, not home-tile-option-menu numbers, so they
are an *analogue*, not the value. But they are the right order of magnitude
and the right proportions, and they are the best evidence available for what
a shell menu row looks like: **~98 px tall, 56 px leading icon at 16 px
inset, 48 px trailing icon, 8 px list inset.**

`function-control-notification-list`'s own `OptionsMenu.js`
(`RN-CC:77629`) turns out to be only a thin wrapper: it renders
`OptionsMenuPS` with `contextItems` + `globalItems`, `targetComponent` set to
its own container's node handle, opened from `onOptionsKeyDown`, container
style `{height: "100%"}` (`RN-CC:77607`-`77634`). No geometry of its own.
`function-control-transfers` has the same pattern in
`components/OptionsMenu/useOptionsMenu.js` (`RN-CC:215855`).

The canonical home-tile option-menu item set (`OPTIONS_MENU_IDS`,
`RN-HOME:37927`-`37940`, module 525) is 12 entries:
`MENU_ID_CHECK_PATCH`, `MENU_ID_SAVE_DATA_MANAGEMENT`,
`MENU_ID_GAME_DATA_MANAGEMENT`, `MENU_ID_APPLICATION_DELETE`,
`MENU_ID_APPLICATION_MULTI_DELETE`, `MENU_ID_APPLICATION_REMOVE_FROM_HOME`,
`MENU_ID_UPDATE_HISTORY`, `MENU_ID_MOVE_TO_EXTERNAL_STORAGE`,
`MENU_ID_MOVE_TO_INTERNAL_STORAGE`, `MENU_ID_APPLICATION_INFORMATION`,
`MENU_ID_INTELLECTUAL_PROPERTY_NOTICES`, `MENU_ID_APPLICATION_CLOSE`, plus
`ACTION_IDS.ACTION_ID_CLOSE_APPLICATION` (`RN-HOME:37941`-`37943`). The
telemetry vocabulary `CLICK_TYPES` (`RN-HOME:3632`) carries the matching
`OPEN_OPTIONS_MENU`, `CLOSE_APP`, `CHECK_UPDATE`, `MANAGE_CONTENT`,
`DELETE_APP`, `UPDATE_HISTORY`, `APP_INFO`, `IP_NOTICE`, `GAME_VERSION`,
`CHECK_SYNC_STATUS_OF_SAVED_DATA`, `DELETE_FROM_HOME`,
`MOVE_TO_INTERNAL_STORAGE`, `MOVE_TO_USB_EXTERNAL_DRIVE`,
`SELECT_ITEMS_TO_DELETE`, `UPLOAD_DOWNLOAD_SAVED_DATA`, `EJECT_DISC`
(`RN-HOME:38833`-`40331`).

### 2.3 What the native side adds (`PUI` symbols)

The JS defers all panel geometry and the open/close transition to native.
The `PUI` symbol table tells us what that native implementation does, even
where the numbers were not recovered:

| Symbol (`PUI`) | What it tells us |
|---|---|
| `OptionMenuSlideAnimation` | the panel **slides**; it is not a pure fade or scale |
| `MeasureOptionMenuPanelWidth`, `GetOptionMenuItemWidth`, `OptionMenuItemWidth`, `OptionMenuPanelWidthCache`, `IsOptionMenuPanelWidthDirty`, `DirtyOptionMenuPanelWidth` | panel width is **measured from the widest item and cached**, invalidated on content change — it is not a fixed width |
| `PopupMenuParameter.MaxWidth` | the measured width is clamped by a max, matching JS `popupMenuMaxWidth` |
| `PopupMenuBaseTailTop`, `PopupMenuBaseTailLeft`, `PopupMenuBaseTailBottom`, `PopupMenuBaseTailTriangle` | the panel has a **tail/pointer** aimed at the anchor, with per-edge variants |
| `OptionMenuBackground`, RCO asset `image_optionmenu_background` vs `image_popupmenu_base` (see `ps5-shell-motion.md`) | the option menu and the generic popup menu use **different** background art |
| `PushOptionMenuScene`, `PopOptionMenuScene`, `PopAllOptionMenu`, `IsOptionMenuScene`, `ExtentionOptionMenuScene`, `FlexionOptionMenuScene` | option menus are a **scene stack**, so submenus push rather than overlay |
| `SubOptionMenuWidth`, `subOptionMenuOffset` | submenus get their own width and an offset from the parent row |
| `OptionMenuCheckIcon`, `PopupMenuCheckIcon`, `SubMenuOptionMenuCheckIcon`, `OptionMenuCheckSelected` | checkable rows have three distinct check-icon slots and their own event |
| `OptionMenuItemFocuseIndex` (sic), `OptionMenuSelectionChanged`, `OptionMenuClosedEventArgs`, `OptionMenuSelectedEventArgs` | focus index and selection are tracked natively |
| `FindOptionMenuContainerScene`, `FindOptionMenuRootScene` | the menu is hosted in a container scene found by walking up, not parented to the tile |

Sound cues are also native and are **specific to the option menu**
(`PUI` `PSFX_*` string table, and the event names
`OptionMenuOpen` / `OptionMenuClose` / `OptionMenuCursorMove` /
`OptionMenuEnter` / `OptionMenuCancel` / `OptionMenuCheckSelected`, which sit
alongside the generic `UICursorMove` / `ListCursorMove`):

| Event | Sound id |
|---|---|
| Menu opens | `PSFX_OPEN_OPTION_MENU` |
| Menu closes | `PSFX_CLOSE_OPTION_MENU` |
| Row confirmed | `PSFX_ENTER_IN_OPTION_MENU` |
| Menu cancelled | `PSFX_CANCEL_IN_OPTION_MENU` |
| Cursor moves **down** a list | `PSFX_MOVE_FOCUS_DOWN_IN_THE_LIST` |
| Cursor moves **up** a list | `PSFX_MOVE_FOCUS_UP_IN_THE_LIST` |
| Cursor hits the end of the list | `PSFX_CANNOT_MOVE_FOCUS` (gated per direction by `cursorEndSFXEnabledDirection`, `RN-BASE:16099`) |
| Generic focus move outside lists | `PSFX_FOCUS_MOVE` |
| Checkbox rows | `PSFX_CHECKBOX_ON` / `PSFX_CHECKBOX_OFF` |
| Switch rows | `PSFX_SWITCH_ON` / `PSFX_SWITCH_OFF` |

Home-ui also plays these directly by id via `SystemSoundPS.playByID`:
`psfx_cancel` (`RN-HOME:10575`), `psfx_focus_move` (`RN-HOME:12717`,
`:59915`), `psfx_enter` (`RN-HOME:41093`, `:59976`), `psfx_change_space`
(`RN-HOME:59691`), `psfx_open_home` (`RN-HOME:61267`) — i.e. the `PSFX_*`
constants are addressable from JS as lowercase ids.

The JS-level semantic enum is smaller (`SystemSoundPS.SoundTypes`,
`RN-BASE:4342`-`4368`): `cursor`, `enter`, `cancel`, `enter2`, `cancel2`,
`dialogOpen`, `dialogOpenNegatively`, `disabled`, `cursorEnd`.

### 2.4 What Prosperismo currently gets wrong

Our implementation lives in `src/SharpEmu.GUI/ShellMotion.cs` (timings),
`src/SharpEmu.GUI/App.axaml:228`-`285` (appearance) and
`src/SharpEmu.GUI/MainWindow.axaml:113`-`156` (structure). `ShellMotion` is
literally the same static fields used by `PerGameSettingsDialog`, so the
"borrowed from the modal constants" claim is confirmed by construction — the
option menu and the modal share one set of constants.

| # | Shipped value | Verdict | Reality |
|---|---|---|---|
| 1 | `ShowDuration = 250 ms`, `ShowDelay = 50 ms`, `EaseOutBlast` | **Right numbers, wrong surface.** `{show: 250 ms / 50 ms delay / easeOutBlastPS}` is a genuine shell constant — found verbatim twice, at `RN-CC:116134` and `RN-CC:246114` — but it is `Modal.animationType` for the **control-center popup card**, not the option menu. `OptionsMenuPS` never sees it; its transition is native `OptionMenuSlideAnimation`. | Keep the constant for our modal. Do not claim it for the menu. |
| 2 | `HideDuration = 300 ms`, **linear** | Same verdict: `{hide: 300 ms / 0 delay / linear}` is real (`RN-CC:116140`), and it really is linear — but again it is the modal card, not the menu. For comparison the generic `fadeInOutPS.delete` preset is `100 ms, easeSmoothOutBreezePS` (`RN-BASE:28257`-`28262`), so 300 ms linear is a *long* hide even by shell standards. | |
| 3 | `ItemStagger = 16.6667 ms` per row | **Wrong in kind.** There is no per-row stagger in the menu path. Rows are a native `ListViewPS` (`RN-BASE:8605`) with `rowItemFocusable:false` and no animation config. The 16.67 ms came from the *grid-tile appear* stagger in `ps5-home-motion.md` §4. | Remove the stagger. |
| 4 | Focused row = `#2600BAFF` fill + a **3 px cyan left bar** (`App.axaml:264`-`267`, `BorderThickness="3,0,0,0"`) | **Wrong in kind.** Menu rows use `focusStyle:"rectangle"` (`RN-BASE:8618`) — the same global travelling focus renderer described in §1. There is no left bar and no tinted row fill anywhere in the shell's menu. | The menu's focus is the ring, not a bar. |
| 5 | `MinWidth = 288` fixed | **Wrong in kind, and far too narrow.** Panel width is measured from the widest item and cached (`MeasureOptionMenuPanelWidth`, `OptionMenuPanelWidthCache`), clamped by `popupMenuMaxWidth`. Where the shell *does* pin a menu width in JS it is `652` min / `784` max (`RN-CC:53462`, `:53468`-`53469`) at 1920-wide layout — more than twice ours. | Measure from content; if a floor is needed, it belongs in the 600s, not 288. |
| 6 | `Placement="Pointer"` with `HorizontalOffset=-8`, `VerticalOffset=-6` (`MainWindow.axaml:114`, `App.axaml:242`-`243`) | **Wrong.** The shell anchors to the **focused tile** (`targetComponent` = the tile's node handle, `RN-HOME:37259`), never to a pointer. Where a `menuStyle` is given at all it is `menuPosition:"right"` — submenus `{right, horizontalAlign:left, collision:"flipfit"}` (`RN-BASE:4625`), control-center `{right, verticalAlign:bottom, collision:"fit"}` (`RN-CC:67886`). | Anchor to the focused item; collision `flipfit`. |
| 7 | Flat 6-item menu (`MainWindow.axaml:113`-`156`) | **Structurally wrong.** The real menu has two sections — `contextItems` then `globalItems` — with a separator on every row but the first and a `listOnPopup` section header (padding `16/48/8/4`, opacity `0.7`, `SizeXSmall`, separator inset `16`). | |
| 8 | Separator `Height 1`, `Margin 14,6` (`App.axaml:281`-`285`) | Inset is wrong: the shell uses `marginHorizontal: 16` on popups (`RN-BASE:31512`), not 14/6. Height is a native constant we do not have. | |
| 9 | `MinHeight = 40`, `Padding = 14,11`, `CornerRadius = 10` per row (`App.axaml:253`-`263`) | **Almost certainly far too small.** The only readable shell menu row is `LIST_ITEM_HEIGHT = 98` with a `56` px leading icon at `16` px inset and a `48` px trailing icon (`RN-CC:24229`-`24248`) at 1920-wide layout. A 40 px row is under half that. Rows are also forced `singleLine` (`RN-BASE:8629`), which we do not enforce. | |
| 10 | Card `#17191E`, `CornerRadius 16`, `Padding 8`, border `#1AFFFFFF`, shadow `0 8 20 0 #99000000` | **Radius confirmed, colour wrong, shape probably wrong.** `borderRadius: 16` is confirmed on a real shell popup card (`RN-CC:53478`) — good. But that card's background is `rgba(8,10,15,1.0)` = `#080A0F`, **fully opaque and much darker** than our `#17191E`. And the native option menu is a nine-patch (`image_optionmenu_background`) **with a tail** (`PopupMenuBaseTailTop/Left/Bottom/Triangle`); a plain rounded rect has no tail. | Change the surface to `#080A0F`; keep radius 16. |
| 13 | No backdrop treatment either way. | **Confirmed correct.** The shell popup sets `dimBackground: false` and `transparent: false` explicitly (`RN-CC:115977`, `:115985`), and `dimBackground` is never set to `true` anywhere in any bundle. There is no scrim behind a shell menu. | Do not add one. |
| 14 | Menu content is a flat 6-item list of our own invention. | The shell's canonical tile menu is a 12-entry `OPTIONS_MENU_IDS` set (`RN-HOME:37927`) plus a separate `globalItems` eject-disc row. | Naming/ordering reference if we ever grow the menu. |
| 11 | Two sounds only: `snd_open_option_menu` / `snd_close_option_menu` on open/close (`MainWindow.axaml.cs:270`-`271`) | **Incomplete.** The shell also has `PSFX_ENTER_IN_OPTION_MENU`, `PSFX_CANCEL_IN_OPTION_MENU`, **separate up and down** list-move cues (`PSFX_MOVE_FOCUS_UP_IN_THE_LIST` / `..._DOWN_...`) and an end-of-list `PSFX_CANNOT_MOVE_FOCUS`. | Distinct up/down cues are the surprising one. |
| 12 | `RiseDistance = 10.0` px translate on show | **Unverified.** The native animation is named `OptionMenuSlideAnimation`, so a translate is right in kind, but 10 px is our own number and the direction/axis is unknown. | |

### 2.5 Gaps

- Every pixel value of the panel itself — width bounds, padding, corner
  radius, background colour, border, shadow, blur — is a native
  `getViewManagerConfig("RCTPopupMenu").Constants` token
  (`popupMenuMaxWidth`, `popupMenuListViewHeight`, `itemPaddingLeft`,
  `itemPaddingRight`) with **no numeric definition in any of the 28 JS
  bundles**. They must come from the native shell module.
- Same for row height, font size/family/weight, and normal/focused/disabled
  text colours — `MenuListItemPS` renders `RCTMenuListButton` /
  `RCTMenuListSubmenu` / `RCTMenuListSelectable` / `RCTMenuListDropdown` /
  `RCTMenuListSwitch` / `RCTMenuListContinuousSlider` (`RN-BASE:4661`-`4666`)
  and passes no styling.
- The open/close duration and curve of the panel are native
  (`OptionMenuSlideAnimation`); the `fadeInOutPS` numbers above are the best
  available proxy, not proof.
- `descriptionAnimDur` / `descriptionAnimDelay` are native constants; only
  the curve (`easeOutBlastPS`) is known.
- No scroll/overflow threshold exists in JS; the list is simply
  `popupMenuListViewHeight` tall.
- `dimBackground` is declared on `PopupMenuBase` but never set there. The
  one place the shell sets it explicitly it is `false` (`RN-CC:115985`), so
  the working assumption is "no scrim", but that is inference from the
  control-center card, not proof for the tile option menu.
- Icon size and icon-to-label gap for menu rows: not present in JS.
- False lead worth recording: `optionsMenuStyle` at `RN-HOME:3238` is
  `{height:106, width:106}` in the **ExperienceSwitcher space-tile**
  stylesheet (`EXPERIENCE_SIZE = 106` at `RN-HOME:3215`) and is never read.
  It has nothing to do with the option menu.

---

## 3. Control center — stub

`NPXS40003.js` (9.14 MB) is the control-center app, built from workspace
`rol-center_v2_ppr_releases_03.00`. It is organised as one package per
control-center "function control" tile:

`function-control` (core), `function-control-bar` (the bar itself),
`function-control-accessibility`, `-apps`, `-broadcast`, `-device`,
`-gaming-lounge`, `-mic`, `-music`, `-navigation`, `-network`,
`-notification-list`, `-power`, `-profile`, `-sound`, `-transfers`,
`-voice-agent`, `-vr`.

Two of these are by far the largest and carry most of the sub-UI:
`function-control-broadcast` (a full go-live flow: privacy dropdown
public/unlisted/private, video-quality dropdown 1280x720@30/60 and
1920x1080@30/60, overlay-position dropdown with six corner/edge positions,
title/description/tags text inputs each with a tooltip, twitch/youtube
targets, error and prohibit states) and `function-control-gaming-lounge`.

Its option menus use the same `OptionsMenuPS` as home-ui, e.g.
`RN-CC:68017` (music mini/full player) with
`menuStyle {menuPosition:"right", verticalAlign:"bottom", collision:"fit"}`
(`RN-CC:67886`-`67888`). `menuPosition:"right"` is overwhelmingly the
default across the app; `collision:"flipfit"` is used where the anchor can be
near an edge (`RN-CC:27052`, `RN-CC:188303`).

The bar itself lives outside `packages/` at
`apps/control-center/src/modules/function-control-bar/`, and its `Popup`
component is the card documented in §2.2b — that is the single most useful
piece of control-center geometry recovered so far. Layout constants found
alongside it:

| Constant | Value | Source locator |
|---|---:|---|
| `SYSTEM_WIDTH` | `1920` | `RN-CC:31410` |
| `SYSTEM_MARGIN` | `84` | `RN-CC:31409` |
| `BUTTON_CONTAINER_WIDTH` | `112` | `RN-CC:22056`, `:85379` |
| `BUTTON_CONTAINER_HEIGHT`, `BUTTON_CONTAINER_MARGIN` | present, not read | `RN-CC:22054` |
| `LIST_ITEM_HEIGHT` (menu rows) | `98` | `RN-CC:24229` |

Not yet mined: the bar's own tile metrics and icon states, the music
player's large constant block (`RN-CC:2633` exports ~70 named sizes:
`CARD_WIDTH`, `CARD_HEIGHT`, `MINI_PLAYER_*`, `TRACK_LIST_*`,
`PROGRESS_BAR_*`, `FOCUS_WIDTH`, `FOCUS_DEBOUNCE_DELAY`, …), and the
transfers/notification-list layouts.
