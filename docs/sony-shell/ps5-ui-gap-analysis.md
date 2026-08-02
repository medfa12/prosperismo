# Where our shell UI disagrees with the real PlayStation 5 layout

A blunt diff between what we ship and what the console's own shell bundle says. Ranked by
visual impact: the things that make a screenshot read as "not PS5" at a glance come first,
and hex colours that are two shades off come last.

## Provenance of the "real" column

Everything in the "real" column below is read out of the shipped Home UI React Native
bundle:

- `NPXS40002.js`, release train `rnps-home_v2_ppr_releases_03.00`, package
  `packages/home-ui`. Beautified copy under the session scratchpad as `rn/NPXS40002.js`;
  module bodies are addressed as `[module N]` using the bundle's own
  `__haul_application.l(N, ...)` registration ids.
- The whole layer is expressed in RN `StyleSheet` objects at a fixed 1920x1080 design
  space. Absolute px, no responsive units.

Modules that carry the load here:

| Module | What it is | Key values |
|---|---|---|
| `[25]` | ExperienceSwitcher constants + stylesheet | `EXPERIENCE_SIZE 106`, `SCALED_EXP_SIZE 168`, `EXPERIENCE_SCALE 168/106`, `SCALED_EXP_MARGIN_LEFT 172`, `BORDER_RADIUS 16`, `VERTICAL_HEIGHT_CHANGE 40`, `MINIMIZED_EXP_SIZE 80`, `MINIMIZED_EXP_MARGIN_TOP/LEFT 48`; `container 1920x168`, `strandContainer 1500x168`, `strandStyle.marginLeft 172`, `focusContainer.borderRadius 168/106*16` |
| `[530]` | `packages/rnps-js-modules-strand/src/index.tsx`, the list the switcher actually uses | consumes `focusedMargin`, `itemMargin`, `selectedItemScale`, `springOptions`, `maxItems` |
| `[531]` | the strand's position solver (`calculate`, `updateState`) | the translateX formula quoted below |
| `[47]` | `MAX_TILES = 11` | pool size of the switcher |
| `[96]` | `System` | `SYSTEM_HEIGHT = 126`, `clockWrapper.marginLeft 88` |
| `[214]` | `TitleContainer` geometry | `TITLE_X = 172+168+16 = 356`, `TITLE_Y = 106`, `TITLE_MARGIN_TOP 10`, `TITLE_MARGIN_LEFT 16` |
| `[19]` | tile utility palette | `DARK_GREY #353535`, `GREY #292929`, `BLANK #FFFFFF0D`, `WHITE #FFFFFF`, `OBSCURE #0D0D0D99` |
| `[49]` | spring presets | `SLOW 130/25/1 clamped`, `SLOWER 100/20/1 clamped`, `FAST 200/100/0.2`, `FASTER 600/100/0.2` |
| `[573]` | `useMat` | per-tile darkening ramp, applied only at selection distance 8/9/10 |
| `[98]`, `[28]`, `[721]` | tile-template and hub-strand constants | **not** the home app row; see the "contradicted claims" section |

Our side is the worktree `C:\Users\sharpemu\sharpemu-workers\rnlayout`, files cited with
line numbers.

### The one derivation everything else leans on

`[531]`'s `calculate` places every icon in the switcher by translateX alone (the items are
`position: absolute` with no `left`, so translate is the whole story). With
`w = EXPERIENCE_SIZE = 106`, `s = EXPERIENCE_SCALE = 168/106`, `focusedMargin = 16`,
`itemMargin = 8`, and `offset = w*s/2 - w/2 = 84 - 53 = 31`:

```
selected               translateX = 31                    (scaled about centre, so the
                                                           rendered left edge lands on 0)
k slots right of it    translateX = 2*31 + k*114 + 16 - 8 = 70 + 114k
k slots left of it     translateX = 114k - 8
```

Read out in screen coordinates at 1920, with the strand's own `marginLeft: 172`:

| Slot | x range on screen |
|---|---|
| focused | **172 .. 340** (168 wide) |
| +1 | 356 .. 462 (106 wide) |
| +2 | 470 .. 576 |
| -1 | 50 .. 156 |

So: **16 px of air on each side of the focused icon, 8 px between every other pair, and
the focused icon's left edge pinned to exactly x = 172.** Neighbours to the left slide off
the left screen edge as you move right. That is the shape of the PS5 home row.

---

## 1. The game covers are in the wrong row, at the wrong size, in the wrong aspect

**Property:** what the top row is, and what the tiles are.

**Real (`[25]`, `[530]`, `[47]`, `Space/index.tsx`, `Tile/index.tsx`):** the home screen has
exactly one horizontal row of tiles, the *experience switcher*, and the games and apps
themselves live in it. Each tile is a **square** cover icon, `106 x 106` at rest and
`168 x 168` when focused, `borderRadius 16`, textured `bc7u` and resized to
`resize4k({width: SCALED_EXP_SIZE, height: SCALED_EXP_SIZE})` (i.e. 336 px source for a
168 px tile). At most `MAX_TILES = 11` are pooled. Below the row is the hub for the focused
title, not a second tile row.

**Ours:** two stacked rows.
`src/SharpEmu.GUI/MainWindow.axaml:139` puts a `ShellFunctionRow` of five utility icons
(Library / Search / Add folder / Rescan / Options, built at
`MainWindow.axaml.cs:515-529`) on top, and `MainWindow.axaml:215` puts the game covers in a
`ShellTileRow` underneath. The game tiles are `370 x 340` non-square rectangles
(`MainWindow.axaml.cs:47-48`), corner radius 16 (`:51`).

**Visual consequence:** this is the single biggest reason a screenshot does not read as
PS5. The console's signature top-row silhouette is a strip of *small square app icons* with
one blown up to 168; we render a strip of five abstract glyph buttons over a row of large
landscape rectangles. The eye reads "media-centre launcher", not "PS5 home". Aspect ratio
alone does it: PS5 app icons are square, ours are 1.088:1 rectangles, so every cover is
letterboxed or cropped differently from the console.

---

## 2. Tile spacing is 3x to 6x too loose, in both rows

**Property:** inter-tile gap / pitch.

**Real:** `[530]` is instantiated in `Space/index.tsx` with `itemMargin: 8` and
`focusedMargin: 16`. Unfocused pitch is `106 + 8 = 114`. Clearance around the focused icon
is 16.

**Ours:**
- `src/SharpEmu.GUI/Controls/ShellFunctionRow.cs:113` `private const double Gap = 46;`
  giving pitch 152, with the comment "106 + 46 = 152 keeps a focused 168 px icon clear of
  both neighbours". It does, but by leaving 46 px of dead air at rest where the console
  leaves 8, and 15 px around the focused icon (152 - 168/2 - 106/2 ... the focused icon
  overhangs 31 px each side into a 46 px gap) where the console leaves 16.
- `src/SharpEmu.GUI/MainWindow.axaml.cs:50` `StrandTileGap = 32` on 370-wide tiles, pitch
  402.

**Visual consequence:** density is the loudest single signal of the PS5 row. The console
packs 11 icons into 1500 px with 8 px seams so the row reads as one continuous ribbon that
the focused tile pushes apart. At 46 px pitch our icons read as separate buttons floating
on a background, and only five of them fit where the console shows eleven. The
"conveyor belt of covers" feel is gone entirely.

Side note: the 402 pitch is a real firmware number, but it is the entry for a **370-wide
content tile inside a hub strand** (`[28]` `HORIZONTAL_SPACING[1576][370]`), not the home
app row. Right table, wrong screen.

---

## 3. The focused tile is not anchored where the console anchors it

**Property:** horizontal anchor of the focused tile, and how the row scrolls.

**Real:** the focused icon's rendered left edge sits at **x = 172** (`strandStyle.marginLeft
= 172`, plus the `+31` translate that cancels the centre-origin scale). The row translates
under a fixed focus position; the focused tile never moves.

**Ours, function row:** `ShellFunctionRow.cs:581`
`double left0 = Math.Max(0, (_viewWidth - total) / 2);` and `:588`
`Canvas.SetLeft(icon.Root, left0 + (i * pitch));`. The row is **centred** in its viewport
and does not scroll at all; the focused icon travels left to right across the screen as you
navigate.

**Ours, strand:** `ShellTileRow.cs:767`
`double dx = FocusAnchorX - (SelectedIndex * (TileWidth + TileGap));` is the right model,
but the anchor is double counted. `MainWindow.axaml.cs:653` already insets the whole home
block by `HomeContentMargin * scale` (172) on the left, and then `:665` sets
`GameStrand.FocusAnchorX = HomeContentMargin * scale` **inside** that already-inset
container. The focused cover therefore lands at 344 design px from the screen edge, twice
the console's inset.

**Visual consequence:** on the console, focus is a fixed point of the composition. Your eye
learns that the big tile is always at the same place, one-eleventh in from the left, and the
world slides behind it. Our function row instead slides the highlight across the screen and
recentres the whole strip, which reads like a desktop dock; and our strand puts the focused
cover a full extra 172 px in, so the left edge of the screen is a large empty band and the
row is visibly off-centre against the function row above it.

---

## 4. Unfocused tiles are dimmed; on the console they are not

**Property:** opacity of non-focused tiles.

**Real (`[573]` `useMat`):** the only darkening applied to switcher tiles is a background
mat interpolated over `rgba(2,4,8,0)` -> `0.05` -> `0.2` -> `0.4`, and the driving value is
set per tile by

```
distance = index - max(0, selectedIndex)
alpha input = {8: .05, 9: .2, 10: .4}[distance] ?? 0
```

Every tile within seven slots of the selection gets input `0`, i.e. **fully transparent
mat, full opacity, full colour**. Only the 8th, 9th and 10th tiles past the selection fade,
and they fade to a black wash, not to a global opacity.

**Ours:**
- `ShellTileRow.cs:98` `UnfocusedOpacity = 0.55` applied to every non-focused cover at
  `:743`.
- `ShellFunctionRow.cs:602-604`: focused `1.0` / `0.8`, unfocused `0.62` / `0.42`.

**Visual consequence:** the console's row is a bright, saturated wall of cover art with one
tile larger than the rest. Ours is one bright tile surrounded by ghosts. Cover art at 55 %
opacity over a dark background loses almost all its colour, so the row reads as monochrome
and the shell looks like it is in a disabled or modal state. This also destroys the tail
cue: the console uses the fade *only* to say "there is more content past here", and we have
spent that signal on ordinary neighbours.

---

## 5. The focused tile lifts; on the console it only scales

**Property:** transform applied on focus.

**Real (`[531]` `createAnimationStyles`):** the per-item transform list is exactly
`[{translateX}, {scale}]`. There is no `translateY`, no shadow change, no z-index change in
the switcher.

**Ours:** `ShellTileRow.cs:99` `FocusedLift = -14` px, applied at `:744-747` as
`translateY(-14px) scale(1)`, folded into the published focus rect at `:289`, plus
`ZIndex = 1000` at `:735` and a permanent contact shadow `0 6 16 0 #40000000` at
`:117-118` / `:596`.

**Visual consequence:** the lift plus drop shadow turns a flat, in-plane enlargement into a
card popping off a surface, which is a Netflix/Apple TV idiom, not a PS5 one. The PS5 row
stays perfectly flush; depth is expressed by the focus ring and by size, never by elevation.
The lift also means the focused tile's baseline no longer lines up with its neighbours,
which is immediately visible along the bottom edge of the row.

---

## 6. Resting scale is 0.92 where the console uses 0.63

**Property:** size ratio between an unfocused and a focused tile.

**Real:** `1 / EXPERIENCE_SCALE = 106/168 = 0.631`. The focused tile is **58 % larger** in
each dimension and 2.5x the area.

**Ours, strand:** `MainWindow.axaml.cs:49`
`StrandTileRestScale = 314.0 / 340.0` = **0.9235**, pushed at `:492`. That ratio is real
firmware (`TILE_SQUARE_HEIGHT_S / TILE_SQUARE_HEIGHT_L`, `[98]`) but it belongs to the
player-tile / content-tile family (`[701]`, the 370x340 profile tile with a 144 px avatar),
not to the app row.

**Ours, function row:** `ShellFunctionRow.cs:94` gets it right (`168/106`), so the two rows
disagree with each other as well as with the console.

**Visual consequence:** a 7.6 % size difference is below the threshold at which size reads
as focus. On the strand the selected cover is essentially the same size as its neighbours,
so all the focus weight has to be carried by the opacity dim from item 4, which is exactly
backwards from the console. On PS5 you can identify the selected app from across the room
by size alone.

---

## 7. The focused title and its metadata row are missing / mislocated

**Property:** placement and content of the caption for the focused item.

**Real (`[214]`, `[25]`):** the focused experience's name is drawn at
`experienceTitleContainer { position: absolute, left: 356, top: 106 }` inside the
1920x168 switcher band, where `356 = SCALED_EXP_MARGIN_LEFT + SCALED_EXP_SIZE + 16` and
`106 = EXPERIENCE_SIZE`. It sits **to the right of the enlarged icon**, near its lower
edge, at `FontSizePS.SizeNormal`. Directly with it is a metadata row,
`itemContainer { position: absolute, height: SCALED_EXP_SIZE - EXPERIENCE_SIZE = 62,
flexDirection: row, alignItems: center }`, carrying a 2 px `rgba(255,255,255,0.25)`
separator, a tag label at `rgba(255,255,255,0.7)` with `marginLeft 26`, and 42x42
entitlement/storage pictograms with `marginLeft 12`.

**Ours:** `ShellTileRow.cs:519-537` builds a `StackPanel` caption with
`Margin = 28,18,28,0`, `HorizontalAlignment.Center`, `VerticalAlignment.Top`, i.e. a
two-line block **centred above** the whole row. `ShellFunctionRow.cs:621-627` likewise
centres its caption under the focused icon.

**Visual consequence:** the console's asymmetric "big icon, then title and platform tags
running to its right" is one of the most recognisable pieces of the home screen; it is what
fills the otherwise empty right two-thirds of the top band. A centred caption floating
above or below the row instead makes the composition symmetric and generic, and leaves the
right side of the band empty. There is also no metadata row at all in our shell, so the
PS5/PS4 tag, storage pictogram and separator rule are simply absent.

---

## 8. There is no 126 px system chrome band

**Property:** the top band of the screen.

**Real:** `[96]` `SYSTEM_HEIGHT = 126`. `[216]` puts a `spaceSwitcherWrapper`
(`height 126, marginLeft 84`) on the left and a `systemWrapper` (`height 126,
marginRight 84`) on the right, `justifyContent: space-between`. `[815]`: the space labels
are `FontSizePS.SizeLarge` bold with `marginRight 64` and `padding 8`; the unselected label
is `fontWeight normal, opacity 0.6`. `[143]`: system icons are `56 x 56` with
`borderRadius 28`, `marginLeft 48`, label at `FontSizePS.SizeXSmall` `top 56, marginTop 4`.
`[96]`: the clock is right-aligned at `FontSizePS.SizeLarge` with `marginLeft 88`.

**Ours:** `MainWindow.axaml:69` a 44 px `TitleBar` with an icon, wordmark and version pill,
then `MainWindow.axaml:103-125` a `ContentToolbar` with L1/R1 chips, Library/Options
segment buttons, a 240 px search `TextBox` and three ghost buttons.

**Visual consequence:** the console's top band is two anchored clusters with a wide empty
middle. Ours is a dense left-aligned toolbar plus a right-aligned input field. The empty
middle is a big part of why the PS5 home feels spacious; filling it with controls collapses
that immediately, and a visible text input is something the console shell never shows on
home.

---

## 9. Motion is eased tweens where the console uses springs

**Property:** the curve and duration of focus / scroll motion.

**Real:** every layout motion in the switcher is `Animated.spring`.
`[530]`'s default when `springOptions` is unset is
`{stiffness: 400, damping: 50, mass: 0.2, overshootClamping: true}`; the
`springOptions` atom (`[128]`) defaults to `undefined` and is only written during the
startup animation. `[49]` supplies the named presets used elsewhere:
`FAST 200/100/0.2` and `FASTER 600/100/0.2` **without** `overshootClamping`, i.e. they
overshoot; `SLOW 130/25/1` and `SLOWER 100/20/1` clamp. Home entrance staggers the icons at
`Animated.stagger(60, ...)` with `SPRING_OPTIONS_SLOWER`.

**Ours:** `ShellTileRow.cs:100-105` fixed `300 ms` `EaseOutBreeze` transitions for focus,
scroll and caption; `:102` `StaggerMs = 16.67`; `:841` the reveal starts from
`translateY(16px) scale(0.6)`. `ShellFunctionRow.cs:120-122` the same 300 ms / 16.67 ms
pair.

**Visual consequence:** a spring with mass 0.2 and no overshoot clamp settles with a small
overshoot and a long low-amplitude tail; an ease-out tween arrives dead. Side by side the
console feels physical and ours feels like CSS. The stagger difference is starker: 60 ms
between icons is a visible cascade across the row on entry, 16.67 ms is one frame and reads
as everything appearing at once. Our reveal also starts at scale 0.6 and 16 px low, where
the shell springs the scale from 0 with no translate.

---

## 10. The focused corner radius on the strand is left unscaled

**Property:** corner radius of the focus highlight on the focused tile.

**Real:** `[25]` `focusContainer.borderRadius = 168/106 * 16 = 25.3585`. A 106 px tile with
a 16 px radius, scaled to 168, has a 25.36 px radius on screen, and the ring must match it.

**Ours:** `ShellFunctionRow.cs:363` does this correctly
(`ring.Radius = BorderRadius * ExperienceScale * LayoutScale`). `ShellTileRow.cs:423` does
`ring.Radius = TileCornerRadius`, i.e. the unscaled 16. Because our strand tile happens to
be at scale 1.0 when focused this is currently self-consistent, but it is only correct by
accident: the moment the strand adopts the real 106/168 model it will draw a 16 px ring
around a 25.36 px tile.

**Visual consequence:** a ring whose radius disagrees with the art behind it shows as four
visible corner slivers. Low impact today, guaranteed impact after fixing items 1 and 6.

---

## 11. The tile chrome is invented: purple fills, borders, gradients

**Property:** the tile's own fill, border and placeholder art.

**Real (`[19]`, `Tile/index.tsx`, `[210]`):** the switcher tile is an `Image` inside a
wrapper whose only style is `borderRadius: BORDER_RADIUS`. No background, no border, no
shadow in the RN layer. The fallback/blank treatments come from the tile utility palette:
`BLANK rgba(255,255,255,0.05)`, `DARK_GREY rgba(53,53,53,1)`, `GREY rgba(41,41,41,1)`,
`OBSCURE rgba(13,13,13,0.6)`. Every one is neutral grey.

**Ours (`ShellTileRow.cs:108-118`, `:587-598`, `:645-688`):**

| Element | Ours | Real counterpart |
|---|---|---|
| tile fill | `#241A3C` | none (image only); blank state `#FFFFFF0D` |
| tile border | `1.5 px #3A2A5C` | no border in the RN styles |
| tile shadow | `0 6 16 0 #40000000` | none in the RN styles |
| placeholder gradient | `#242C46` -> `#141A2C` | neutral `#292929` / `#353535` |
| placeholder wash | radial `#5500BAFF` | none |
| initials/prism | `#00BAFF` -> `#A669FF` | none |
| function-row icon fill | `#17191E` / `#232833` (`ShellFunctionRow.cs:125-126`) | none; `#17191E` is the action-card surface from a peer bundle, not a tile fill |

**Visual consequence:** `#241A3C` and `#3A2A5C` are violet. The shell's entire neutral
family is desaturated grey over `rgb(2,4,8)`. A violet cast across every tile plus a
violet hairline border reads as a third-party skin at a glance, before any geometry is
examined. The 1.5 px border in particular gives every cover a visible frame the console
does not have, which flattens the artwork and makes the row look like a table of buttons.

---

## 12. `#00BAFF` is used as a settled fact

**Property:** focus stroke and accent colour.

**Real:** not determinable from this extract. `#00BAFF`, `rgb(0,186,255)` and the common
packed forms do not appear in `NPXS40002.js` or its peer bundles (already recorded in
`docs/ps5-home-theme.md`). The focus colour is a native theme uniform. What the bundle does
show is that the shell has no JS-level accent at all: the only explicit accent-like pair is
the gold tag preset `#FFD228` on `#A88644`.

**Ours:** `src/SharpEmu.GUI/App.axaml:14` `SystemAccentColor #00BAFF`, `:36` `FocusBrush`,
`:58` `AccentBrush`, `:63` `InfoBrush`, plus
`src/SharpEmu.GUI/Controls/ShellFocusRing.cs:486,489` `DefaultStrokeColor` /
`DefaultFillColor`, and the prism gradients at `App.axaml:26-33`.

**Visual consequence:** low on its own. The cyan is a plausible placeholder and the ring
geometry (3 px stroke at 3 px offset, `ShellFocusRing.cs:94,97`) is from the native side and
is defensible. The problem is spread: cyan is now the accent for buttons, info text, the
wordmark and the tile placeholders, so it is doing far more work than a focus colour does on
the console, where nothing outside the focus ring is tinted. Ranked last because a colour
swap is a one-line fix and the items above are not.

---

## Claims in the existing docs that the real StyleSheets contradict

These four docs were written from weaker sources. Where the bundle disagrees, the bundle
wins.

### `docs/ps5-home-structure.md` §2.3 invents a second tile row and gives it the wrong constants

The doc says:

> ### 2.3 Content / game tile row below ("Strand")
> The scrolling row of game/content tiles under the function row. Source
> `packages/rnps-js-modules-strand` and constants in [module 28]:
> - Strand viewport: `STRAND_WIDTH = 1576`, `STRAND_HEIGHT = 864`, `CONTAINER_MARGIN = 172`.
>   (An inner `strandContainer` of `1500 x 168` and `strandStyle.marginLeft = 172` is used for
>   the compact/aligned variant [module 25].)

Three separate things are conflated here.

1. **`strandContainer 1500 x 168` and `strandStyle.marginLeft 172` are the experience
   switcher's own.** They live in `[25]`, the ExperienceSwitcher constants module, and
   `home-ui/src/components/Space/index.tsx` renders them directly:
   `Animated.View style={[styles.strandContainer, ...]} testID="strand-container"`, with
   `style: styles.strandStyle` handed to the list that renders the `TileItem` experiences.
   They are not a "compact variant" of a lower content row; they are the app row. A 168 px
   tall container can only ever hold `SCALED_EXP_SIZE` tiles.

2. **`packages/rnps-js-modules-strand` is not `[module 28]`.** The package path resolves to
   `[530]`, whose props are `focusedMargin` / `itemMargin` / `selectedItemScale` /
   `springOptions` and whose stylesheet is the two-rule `{container: {marginTop: -h/2},
   item: {marginTop: h/2}}` pair. `[28]` (`STRAND_WIDTH 1576`, `STRAND_HEIGHT 864`,
   `CONTAINER_MARGIN 172`, `HORIZONTAL_SPACING`, `VERTICAL_SPACING`) is consumed by a
   different Strand entirely, `[733]`/`[734]` and `[762]`/`[763]`, which are the hub SDK's
   `Strand` + `ListView` + `Label` stack. Those render inside a hub, below the switcher, not
   as a home row.

3. **`[98]`'s `TILE_SQUARE_*` are not the home row's tiles.** `TILE_SQUARE_WIDTH 370`,
   `TILE_SQUARE_HEIGHT_L 340`, `TILE_SQUARE_HEIGHT_S 314` are consumed by a tile-shape enum
   (`SLIM`/`STACKED`) and by the player-tile stylesheet at `[701]`, whose siblings are
   `AVATAR_SIZE = 144`, `nameTextStyle.width 322`, `avatar {marginTop 32, marginRight 113}`.
   That is a profile card, not a game cover in the app row.

This one paragraph is the direct cause of items 1, 2 and 6 above: `MainWindow.axaml.cs:44-51`
cites exactly these constants ("Square content tiles: `TILE_SQUARE_WIDTH x
TILE_SQUARE_HEIGHT_L`, with the small-tile height giving the resting scale") and builds our
home around them.

### `docs/ps5-home-structure.md` §7 understates what is now resolvable

The doc says:

> **Exact on-screen X/Y of the two rows** is composed at runtime from flex layout +
> Animated offsets; only the piece constants (heights 126/168, margins 84/172, the
> 166 composite) are literal. A pixel-perfect vertical stack was not fully resolved to
> absolute top coordinates.

Horizontally this is no longer true. `[531]`'s `calculate` gives closed-form x for every
tile (`70 + 114k` right of the selection, `114k - 8` left of it, `+31` on the selection
itself), so the focused icon's left edge is exactly 172 and every neighbour is exactly
placed. The vertical caveat still stands.

### `docs/ps5-options-menu-and-focus.md` §1.6 and §1.7 get the corner-radius arithmetic backwards

§1.6 says:

> the switcher's `focusContainer` uses `borderRadius: 168/106 * 16 ≈ 25.36` ... i.e. **the
> focus container's corner radius is pre-multiplied by the tile's scale factor** so that
> after the scale transform the ring's visual radius lands back on `16`.

§1.7 item 10 says the opposite of itself:

> The shell **pre-divides** the radius by the scale factor (`168/106 * 16`) so the
> post-transform radius is `16`.

Both conclusions are wrong, and they disagree with each other. `168/106 * 16 = 25.36`, which
is larger than 16, so it cannot be a pre-division and it cannot land back on 16. The correct
statement: the tile art carries a 16 px radius at 106 px; scaled to 168 that radius is
25.36 px on screen; the focus shape is therefore declared at 25.36 so it matches what the
viewer sees. A reimplementation must give the ring **the scaled radius**, not the unscaled
one. `ShellFunctionRow.cs:363` already does the right thing numerically, so the code is fine
and the doc's explanation is what needs replacing.

### `docs/ps5-figma-layout.md` holds up on the strand, and should be promoted

§6.2 and §6.3 already state `itemMargin = 8`, base pitch 114, the 172 anchor, the 1500 clip
at a 248 px right gutter, and the `(i-sel)*114 - 8` / `+70` formula. All of that is
confirmed exactly by `[530]`/`[531]`. Those rows are no longer "firmware says X, Figma says
Y" adjudications; they are settled firmware values and belong in `ps5-home-structure.md`
§2.2 rather than buried in a comparison table in the weakest of the four documents. The
board's own numbers (gap 10, pitch 116, outer 181x179, radius 29/32, row origin x 186) stay
wrong.

### `docs/ps5-home-theme.md` is not contradicted

Checked against `[19]`, `[721]`, `[143]`, `[156]`, `[737]`: the palette table, the
`rgb(2,4,8)` mat ramp with its four alphas, the `#FFFFFFB3` secondary, the radii table
(header icon 8, hub image 12, experience tile 16 at 106, focus container 25.3585, FC
container 16, action indicator 18 on 36, system icon 28 on 56) and the "`#00BAFF` is not in
the JavaScript" finding all match the bundle. Nothing to correct.

One refinement rather than a correction: the doc records the mat ramp as "per-tile mats" in
general. `[573]` shows the ramp is keyed on **distance past the selection**, applying only
at distances 8, 9 and 10. It is a tail-fade for overflow items, not a general unfocused
treatment, which is what item 4 above turns on.

---

## A measurement that lied: DPI-virtualised screen captures

Recorded because it was briefly written down and passed on as a real defect, and because
it is the exact failure mode this document is otherwise designed to prevent.

**The claim.** A capture of our shell was measured against the 1920 x 1080 design space
and appeared to be laid out about **25 % too large**, with the right and bottom edges of
the UI running off the window. On a 125 % display that is a suspiciously round number,
and it was reported as a layout bug.

**It is not a layout bug.** It is the capture script. `powershell.exe` 5.1 runs
**DPI-unaware**, so:

| Step | What happens on a 125 % display |
|---|---|
| `GetWindowRect` | returns **virtualised** logical coordinates, i.e. the physical size divided by 1.25 |
| bitmap allocated from that rect | allocated at `1/1.25` of the window's real pixel size |
| `BitBlt` into it | copies real pixels one for one into a bitmap 20 % too small |
| result | the right and bottom edges are **cropped**, and everything that survives looks oversized relative to the frame |

A UI that is 25 % too large in a correctly sized frame and a correctly sized UI in a
frame cropped to 80 % are pixel-identical over the region that survives. Nothing in the
image distinguishes them. The scale factor recovered from the capture was a property of
the capture tool, not of the thing captured.

**The rule this produces.** Before any pixel measurement against this document:

1. Make the capture process DPI-aware, or capture at 100 % scaling, or capture the whole
   screen and crop by content rather than by `GetWindowRect`.
2. Sanity-check the bitmap dimensions against the window's **physical** size before
   measuring anything out of it. If they disagree, the capture is wrong and every number
   derived from it is wrong by the same factor.
3. Treat a suspiciously round error ratio (1.25, 1.5, 2.0) as evidence of a tooling
   artefact until proven otherwise. Real layout bugs are rarely exactly the display
   scale factor.

No number anywhere in `docs/` was derived from those captures, so nothing needed
retracting. This section exists so the conclusion does not get rediscovered as a bug.

---

## Not determinable from this extract

Called out so nothing here gets guessed at later.

- **Absolute pixel values for the type scale.** Every text style names a token
  (`FontSizePS.SizeLarge`, `SizeNormal`, `SizeSmall`, `SizeXSmall`, `Size2XSmall`,
  `Size3XSmall`, `Size4XSmall`). `FontSizePS` resolves through `[5]`
  (`module.exports = require(45)("COfhp+aU")`), a hash-keyed load from a shared library that
  is not in this bundle. The *relationships* are usable (switcher title `SizeNormal`, space
  label and clock `SizeLarge`, system icon label `SizeXSmall`, tile sublabel
  `Size3XSmall` at opacity 0.7); the px are not. Our hardcoded `FontSize="22"` /
  `FontSize="13"` (`ShellTileRow.cs:505,514`) and `15 * scale`
  (`ShellFunctionRow.cs:624`) cannot be judged right or wrong against this source.
- **Focus ring colour, fill colour and alpha.** Native theme uniforms; absent from the JS.
- **Whether the 1500 px `strandContainer` clips.** No `overflow` is declared on it and RN's
  default differs by platform. The left-of-selection tiles translate to negative x, so
  whether they are cut at x = 172 or fade off the screen edge is unresolved.
- **The vertical origin of the switcher band.** The band is 168 tall and sits under a 126 px
  system band, but `HomeContainer` uses `flexDirection: "column-reverse"` with animated
  offsets, so the absolute screen `top` of the row was not resolved.
- **Whether the console draws any shadow under a tile.** There is none in the RN styles, but
  the native focus/light layer could add one. Our `0 6 16 0 #40000000` is therefore
  "unsupported by the extract" rather than "proven wrong".
- **What `springOptions` is set to during the startup animation.** The atom is written by
  `setStartupAnimation`; the written value was not traced.
- **The hub content area below the switcher.** Out of scope for this pass, and the reason
  the "second row" question in item 1 needs a design decision rather than a constant: the
  console puts a per-title hub there, and we have no hub.
