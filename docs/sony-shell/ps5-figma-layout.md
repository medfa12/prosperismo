# PS5 UI — Community Figma Board, Decoded

A secondary layout reference decoded from a community-made Figma file that redraws the
PS5 system shell. It is useful for *proportion and structure*, and for a handful of
places where the firmware docs have gaps.

## Corrected

Source tier 4 (a hand trace). Superseded on all strand geometry by
`docs/ps5-rn-layout.md`, which reads the numbers out of Sony's own StyleSheets
(source tier 1). The rows below are kept as the record of what this board claimed, so
that anyone who finds one of these numbers quoted elsewhere can see where it came from
and that it was retired.

| Section | This board claimed | Bundle ground truth | Provenance of the correction |
|---|---|---|---|
| §3.2 | Resting pitch **116** (106 + gap 10) | **114** (106 + `itemMargin` 8) | `ps5-rn-layout.md` 2.4, HOME m531:38282-38367 |
| §3.2, §4 | Focused tile **181 x 179**, radius **32**; content box 171 x 169 radius 29 | **168 x 168**, radius **25.358490566** | HOME m25:3216 `n.SCALED_EXP_SIZE = 168;`, HOME m25:3236 `borderRadius: 168 / 106 * 16` |
| §3.2, §4 | Radius grows faster than the tile (16 to 29 or 32, about 1.8x on a 1.585x scale) | Radius is a constant **ratio 0.150943** of the side length: `16 / 106 = 25.3585 / 168` | `ps5-rn-layout.md` 1.5, arithmetic over the two EXACT rows above |
| §3.2 | Row origin **x 186, y 131** | Row origin **x 172, y 126**. 172 is `SCALED_EXP_MARGIN_LEFT` and is the *focused tile's left edge*, not a static row start; 126 is `SYSTEM_HEIGHT` | HOME m25:3218, HOME m96:7287 `t.SYSTEM_HEIGHT = 126;` |
| §3.1 | Right-hand system icon pitch **100** (1346 / 1446 / 1546) | **104**: `iconContainer { width: 56, marginLeft: 48 }` | HOME m143:10653 |
| §3.1 | Icons measured 31.3 (search), 34 (settings), 53 (avatar) | All three are **56 x 56** boxes; the avatar is clipped at radius 28 | HOME m143:10653, `ps5-home-theme.md` |
| §3.2 | Focus is a ring drawn *around* the tile, so the tile grows in place | The strand **scrolls** so the focused tile's left edge pins to x 172 | `ps5-rn-layout.md` 2.4, and §6.3 below already said this |

Two numbers this board is often credited with **were never on the console at all**, and
did reach our shell before being caught. They are recorded in `ps5-rn-layout.md` §10
rows 2 and 3: an unfocused-tile opacity of **0.55** and a focused vertical lift of
**-14** px. The RN `Tile` component carries no opacity rule and no `translateY` on
focus. The 106 to 168 size change is the entire focus affordance. Do not reintroduce
either value from this board or from a screenshot.

§6.2 below already reached most of these conclusions from the same direction. This
block exists so the corrections are visible before the tables that contain the wrong
numbers, rather than 250 lines after them.

## Ground truth caveat — read this first

**The decrypted firmware is ground truth. This Figma board is not.**

It is one community author's redraw, traced by eye over 4K screen captures of a real
console. It is not Sony's design source, it carries no token names, and its numbers
are measurements with visible hand-tracing error (left margins wander between 135 and
218 px on screens that should share one inset; two frames are 1915 px wide instead of
1920). Wherever a value here contradicts `ps5-home-structure.md`,
`ps5-options-menu-and-focus.md`, `ps5-control-center.md`, `ps5-hub-and-cards.md` or
`ps5-home-theme.md`, **the firmware-derived value wins and this file is wrong.**

What it is genuinely good for:

1. **Corroboration.** Several firmware constants (`EXPERIENCE_SIZE = 106`,
   `BORDER_RADIUS = 16`, `MAX_TILES = 11`, `EXPERIENCE_SCALE = 1.5849`, the two-space
   model, the 24 px five-column grid gutter, the 126 px chrome band) reappear here as
   independent eyeball measurements of a real console. That is a meaningful second
   source.
2. **Filling gaps.** The firmware bundles gave us no pixel font sizes and no geometry
   at all for the home content area under the strand (the Play button, the trophy
   summary card, media hub rows). This board has those — as *unverified* numbers.

Everything below is Figma-only unless the comparison tables say otherwise.

## Provenance and how to reproduce

- Source: `PS5 Interactive UI (Community).fig`, 256 MB, community-published Figma
  document titled "PS5 Interactive UI (Community)".
- Not committed. The `.fig` and everything extracted from it (raster fills up to
  18 MB each) stay out of the repo.
- Decoder: `scripts/fig_decode.py` (stdlib only). Regenerate any table below with:

  ```
  python scripts/fig_decode.py <file.fig> --frames --depth 1
  python scripts/fig_decode.py <file.fig> --layout "Home Page" --depth 4
  python scripts/fig_decode.py <file.fig> --tree tree.json
  ```

### Container format

A `.fig` is a ZIP. The node tree is `canvas.fig` inside it, framed as:

| Offset | Field |
|---|---|
| 0 | `"fig-kiwi"` magic, 8 bytes |
| 8 | `uint32` container version — **106** in this file |
| 12 | `uint32` length + compressed **Kiwi schema** block |
| … | `uint32` length + compressed **data** block |

Kiwi is a self-describing tagged binary format (varints; message/struct/enum
definitions), so the schema block tells the decoder every field name and type and the
tree can be read without knowing Figma's private field set in advance.

The two blocks are compressed differently in this container generation, which is the
one trap: the **schema block is raw deflate** (no zlib header, `windowBits = -15`) but
the **data block is Zstandard** (`28 b5 2f fd` magic). Python has no stdlib zstd
before 3.14, so `fig_decode.py` tries `compression.zstd`, then the `zstandard` /
`pyzstd` packages, then `ctypes` against a system `libzstd` — on Windows that is the
`libzstd.dll` Git for Windows already ships in `mingw64/bin`.

### Decode result

Fully successful, no partial parsing and no guesswork:

| Measure | Value |
|---|---|
| Container version | 106 |
| Schema block | 28 761 B deflated → 71 765 B, **627 definitions**, parsed to exactly the last byte |
| Data block | 180 836 B zstd → 866 630 B, decoded against the schema to exactly the last byte |
| Node changes | **1 453** |
| Canvases | 3 — `Page 1`, `Components`, `Internal Only Canvas` |
| Top-level frames | 32 on `Page 1`, 33 on `Components` |
| Node types | 383 INSTANCE, 354 ROUNDED\_RECTANGLE, 259 TEXT, 213 FRAME, 92 SYMBOL, 82 VECTOR, 45 ELLIPSE, 20 BOOLEAN\_OPERATION, 1 LINE |

Both blocks decoding with zero trailing bytes is the correctness proof: a wrong field
type or a wrong varint rule desynchronises the stream and fails long before the end.

## 1. Design resolution

**1920 x 1080.** Every coordinate in this document is a 1920x1080 design pixel.

The evidence is unusually clean: **all 189 image paints in the file have
`scale = 0.500` exactly**, and their `originalImage` dimensions are 4K
(3840x2160 for the full-screen captures). The author dropped native 4K console
captures onto the board at half size and traced on top. So the board's coordinate
space is the console's 1920x1080 design space, and native 4K pixels are 2x these
numbers.

This independently matches the firmware docs, which state a fixed 1920x1080 design
resolution in three separate places, with 4K appearing only as a 2x texture request.

One wrinkle to keep in mind when reading the tables: most frames are **1920x1075**,
not 1080. The author cropped 5 px. On `Home Page` the background sits at frame-relative
y = -5, so *design* y = *frame* y + 5 there; on other frames the background sits at
y = 0. Treat all y values below as +/- 5 px.

## 2. Frame inventory

`Page 1` holds the screens. Sizes are the frame's own size.

| Frame | Size | PS5 screen | Vector content? |
|---|---|---|---|
| `Home Page` | 1920x1075 | Home, PlayStation Store tile focused | yes — 5 text, 22 instances |
| `Explore Page` | 1920x1075 | Home, Explore tile focused (news feed) | yes — 12 text, 14 rects |
| `Astro Page` | 1915x1075 | Home, game focused (Astro's Playroom) | yes — 14 text, 20 instances |
| `Apex Page` | 1915x1075 | Home, game focused | yes — 14 text |
| `Ratchet Page` | 1915x1075 | Home, game focused | yes — 15 text |
| `Rocket league Page` | 1915x1075 | Home, game focused | yes — 13 text |
| `Ms.PacMan page` | 1915x1075 | Home, game focused | yes — 14 text |
| `fortnite page` | 1915x1075 | Home, game focused | yes — 17 text |
| `2k22 Page` | 1920x1075 | Home, game focused | yes — 14 text |
| `PS collection page` | 1915x1075 | Home, PS Plus Collection focused | partial — 4 text |
| `Game Library page` | 1915x**1822** | Game Library grid (tall, scrolls) | yes — 30 rects |
| `App Library page` | 1920x**2049** | App Library grid (tall, scrolls) | yes — 25 rects |
| `netflix page` | 1920x1075 | Media space, TV & Video hub | yes — 7 text |
| `disney page` | 1920x1075 | Media space, streaming app hub | yes — 7 text |
| `hulu page` / `prime page` / `peacock page` / `Apple page` | 1920x1075 | Media space, streaming app hubs | yes — 7 text each |
| `apple page` | 1920x1075 | Media, Apple Music | yes — 9 text |
| `Spotify page` | 1920x1075 | Media, Spotify | yes — 9 text |
| `crossing swords page` | 1920x1075 | Media title detail ("Watch on" providers) | yes — 10 text, 10 rects |
| `Login page` | 1920x1075 | User select / "Who's using this controller?" | yes — 10 text, 12 vectors |
| `Profile Overlay` (SYMBOL) | 1920x1075 | Profile card overlay over a dimmed screen | scrim only, card is a bitmap |
| `Discover splash`, `Moving out`, `Vanguard Splash`, `Warzone Splash`, `PS4 Games splash`, `Editor splash` | 1920x1075 | PS Store promo/detail pages | partial |
| `settings page` | 1920x1080 | Settings | **no** — bare screenshot + 1 hotspot rect |
| `search page` | 1920x1080 | Search | **no** — bare screenshot + 1 hotspot rect |
| `Frame 11` | 1920x1080 | unlabelled | **no** — bare screenshot + 1 hotspot |

`Components` holds the reusable pieces: the `Search` / `settings` / `More` / `Avatar`
icons, one component per game or app tile, `Text` (the space-switcher label with
selected/unselected/box variants), `psStoreBlock` (store promo card, 6 artworks x
2 focus states), `PsStore Nav bar`, `Profile overlay`, `Gold`/`Silver`/`Bronze PS Trophy`,
and `ps5 label` / `ps4 label` platform tags.

### What the board does *not* contain

Important negative result, because these are the parts Prosperismo most needs:

- **No control centre.** Not one frame, not one component. The 16-control function bar,
  the 652/784-wide popup panels, the card carousel — absent.
- **No options / context menu.** Nothing anywhere in the tree; the only hit for
  "Options" is a controller button-hint label on `Login page`.
- **No toasts** and **no persistent-toast** surface.
- **No action cards.** No 360x456 card, no glance/focused/selected states, no
  multitask/PinP placements.
- **No hub state.** The "game pages" are all *home with a tile focused*; the board never
  shows the tile collapsing to the 80x80 header badge, so it says nothing about the hub.
- **Settings and Search are bitmaps only** — zero recoverable geometry.

So on the four surfaces the task hoped to cross-check — control centre, option menu,
toasts, cards — this board contributes **nothing**, and the firmware docs remain the
only source. It contributes on home, the libraries, and the media hubs.

## 3. Home screen

Coordinates are frame-relative on `Home Page` / `Astro Page` / `Explore Page`, which
agree with each other to within ~2 px on the shared chrome.

### 3.1 System chrome (top band)

| Element | Size | Position | Style |
|---|---|---|---|
| Space label "Games" (selected) | 113x49 | 91, 33 | Open Sans SemiBold 36 px, `#FBFBFB`, ls 0.5 % |
| Space label "Media" (unselected) | 94x49 | 282, 33 | Open Sans Light 36 px, `#BFBFBF`, ls 0.5 % |
| Search icon | 31.3x31.3 | 1346, 41 | 24 px circle, 3 px `#FFFFFF` inside stroke, 5 px tail |
| Settings icon | 34x34 | 1446, 39 | `#FFFFFF`, drop shadow r1 |
| Avatar | 53x53 | 1546, 35 | circle, 47 px image + two 20 %-white rings; 14 px `#1AFF84` presence dot |
| Clock | 160x49 | 1694, 33 | Open Sans Light 36 px `#FFFFFF`, HH : MM + AM/PM, 10 px gap |

- Right-hand icon pitch is exactly **100 px** (1346 → 1446 → 1546).
  **WRONG, see "Corrected" above.** The bundle pitch is **104**, from
  `iconContainer { width: 56, marginLeft: 48 }` (HOME m143:10653). The three icon
  sizes in the table above (31.3 / 34 / 53) are all one 56 x 56 box.
- Icons are vertically centred around y ≈ 56–62, i.e. inside a ~126 px band.
- Optional focus box variant on the space label: 143x74 (Media) / 149x74 (Games),
  radius **9**, transparent fill, **4 px inside stroke**, linear gradient
  `#E5D0D0 → #837777`.

### 3.2 Tile strand (function row / content launcher)

The board's most valuable table, and the one that matches firmware best. Five of its
rows are nonetheless wrong; the "Correction" column carries the bundle value and
"Corrected" at the top of this file carries the provenance.

| Property | Value | Correction |
|---|---|---|
| Row origin | x **186**, y **131** | **x 172, y 126** |
| Row layout | horizontal auto-layout, **gap 10** | gap **8** (`itemMargin`) |
| Resting tile | **106 x 106**, corner radius **16** | correct |
| Resting pitch | **116** (106 + 10) | **114** |
| Focused tile, outer | **181 x 179**, corner radius **32** | **168 x 168**, radius **25.3585** |
| Focused tile, content | **171 x 169** at +5, +5, corner radius **29** | no separate content box exists |
| Row height with a focus | **179** | **168** (`SCALED_EXP_SIZE`) |
| Tile count | **11** on `Home Page` | correct, `MAX_TILES = 11` |
| Utility tile background | `#000016` at **70 % opacity** (PS Store, Explore, PS Plus, Library) | `#000016` appears in no bundle, see §6.2 |
| Utility glyph box | 46 x 46, centred, **30 px padding** inside the 106 px tile | unverified, no bundle counterpart |
| Game tile fill | cover art, `imageScaleMode = FILL` | correct in kind |

The focused tile is drawn as two stacked rectangles plus a scaled glyph:

| Layer | Size | Offset | Style |
|---|---|---|---|
| ring | 181 x 179 | 0, 0 | radius 32, no fill, **3 px inside stroke**, linear gradient `#CAA7D1 → #B18FB5` @ 0.38 |
| content | 171 x 169 | +5, +5 | radius 29, fill `#000016` @ 0.85, layer opacity 0.66 |
| glyph | 72 x 72 | +55, +54 | resting glyph is 46 x 46 at +30, +30 |

So the **content box grows 106 → 171 (x1.613)** and the **glyph grows 46 → 72 (x1.565)**,
while the 181 px figure is the outer edge of a 5 px ring. That bracket around 1.585 is
the single most useful corroboration on the board — see §6.1.

Tile order on `Home Page`: PS Store, Explore, NBA 2K22, Apex, Astro, Fortnite,
Ratchet, Ms. Pac-Man, Rocket League, PS Plus, Library.

Two structural notes:

- On the **Game Library** and **App Library** frames the strand is truncated to just
  2 tiles (`psplus` + focused `library`) starting at x = 19 / 101 — the author did not
  model strand scrolling, so those left insets are meaningless.
- On every frame the focused tile grows **in place**. The real console scrolls the
  strand so the focused tile lands at a fixed left inset; see §6.

### 3.3 Content area, game focused (`Astro Page`)

Not covered by any firmware doc — Figma-only.

| Element | Size | Position | Style |
|---|---|---|---|
| Game logo art | 625x152 | 204, 538 | image |
| Description | 609x66 | 204, 693 | Open Sans Light 24 px `#FFFFFF`, 2 lines |
| Play button pill | 371x78 | 204, 817 | radius **61** (fully round), fill `#000000` @ 0.14 |
| Play button label | 186x49 | 297, 831 | Open Sans SemiBold 36 px `#FFFFFF` |
| "More" (…) button | 84x78 ellipse | 590, 817 | fill `#000000` @ 0.14 |
| "More" glyph | 39x11 | 612, 856 | three dots |
| Trophy card | 370x145 | 1373, 751 | fill `#000000` @ **0.43**, no corner radius |
| ├ "Progress" label | 72x25 | 1399, 761 | Open Sans Light 18 px |
| ├ Progress value "15 %" | 61x41 | 1400, 786 | Open Sans Light 30 px |
| ├ "Earned" label | 59x25 | 1549, 761 | Open Sans Light 18 px |
| ├ Earned value "11/46" | 72x41 | 1549, 786 | Open Sans SemiBold 30 px |
| └ Trophy icons | 33x43 | 1398 / 1494 / 1590, 835 | pitch **96**; counts 24 px Light at y 847 |

The Play pill and the More circle share a 78 px height and sit 15 px apart
(371 + 15 = 386 → ellipse at 590).

### 3.4 Content area, Explore focused (`Explore Page`)

| Element | Size | Position | Style |
|---|---|---|---|
| Full-frame scrim | 1920x1074 | 0, 1 | `#000000` @ 0.33 |
| Source line "Official news from …" | 284x33 | 186, 447 | Open Sans Light 24 px |
| Separator rule | 32x0 | 478, 448 | 1 px `#FFFFFF` centre stroke |
| Timestamp | 129x33 | 493, 447 | Open Sans Light 24 px |
| Headline | 671x142 | 184, 487 | Open Sans Light **52 px**, 2 lines |
| Body | 890x98 | 186, 661 | Open Sans Light 36 px, 2 lines |
| News cards | 380x220 | 176 / 588 / 1000 / 1412, 821 | pitch **412** (gap 32); each with a `#000000` @ 0.33 scrim |
| Card badge pill | 127x36 | card + 10, card + 14 | fill `#000000` @ 0.50; 24 px icon at +12,+6; label Open Sans SemiBold **14 px** at +45,+8 |

### 3.5 Store row (`Home Page`, PS Store focused)

| Element | Size | Position |
|---|---|---|
| Featured art | 379x332 | 181, 442 |
| Row header "Must see" | 141x49 | 186, 808 — Open Sans Regular 36 px, ls -4.5 % |
| Promo card row | 1662x128 | 182, 881 |
| Promo card | 257x128 | pitch **281** (gap 24) — 6 visible |

## 4. Focus treatment

The board renders focus two different ways, and the sizes are informative.

**Tiles** (strand): a 5 px gradient ring (radius 32, 3 px inside stroke) wrapped around a
171x169 content box, total 181x179 — see the table in §3.2. The ring is *not* the same
construction as the card ring below: it is thicker (5 px vs 4 px), and the radius grows
with the tile (16 → 29 on the content, 32 on the ring) rather than staying fixed.

**Cards** (`psStoreBlock` component, both focus states are drawn side by side):

| State | Size | Notes |
|---|---|---|
| default | 257x128 | artwork only, drop shadow r10 |
| selected | **265x136** | artwork stays 257x128 at +4,+4 inside a ring frame |
| ring | — | **3 px inside stroke**, linear gradient `#CAA7D1 → #B18FB5` @ 0.38, drop shadow r10 |

So the card focus ring adds exactly **+4 px per side** and is a 3 px gradient stroke.
The firmware's own ring is 3 px thick at a 3 px outside offset, i.e. **+6 px per side** —
the same construction, a slightly different measurement.

The space-label focus box (§3.1) is a third variant: radius 9, 4 px gradient stroke,
no fill.

## 5. Other screens

### 5.1 Library grids

| | Game Library | App Library |
|---|---|---|
| Frame height | 1822 | 2049 |
| Title | "Game Library" 36 px at 329, 254 | "App Library" 36 px at 412, 255 |
| Sub-line | "Console storage: 20" at 135, 471 | same text at 218, 472 |
| Sort control | "Sort by: Name (A - Z)" at 1400, 471 | — |
| Tile | **300x300** | **300x300** |
| Columns | 5 — x = 135, 459, 783, 1107, 1431 | 5 — x = 218, 542, 866, 1190, 1514 |
| Column pitch | **324** (gap **24**) | **324** (gap **24**) |
| Row y | 538, 869, 1186, 1510 | 572, 936, 1300, 1664 |
| Row pitch | ~324 (drifts 331/317/324) | **364** |
| Placeholder fill | `#C4C4C4` | — |

The 24 px gutter is exact and shared; the left insets (135 vs 218) are not, and the
row pitch differs between the two grids. Treat the gutter as real and the insets as
tracing noise.

### 5.2 Media hubs (`netflix page`, `crossing swords page`)

| Element | Size | Position | Style |
|---|---|---|---|
| Top-down scrim | 1920x1075 | 0, 0 | linear gradient `#000000` 1.0 → 0.88 → 0.80 → 0.00 → 0.00 |
| App / title name | 168x76 | 183, 499 | Open Sans Regular **56 px**, ls -1 % |
| Description | 764x88 | 186, 591 | Open Sans Light **32 px**, 2 lines |
| Section header "Featured" | 140x49 | 175, 785 | Open Sans Regular 36 px, ls -4.5 % |
| App card row | 1582x202 | 175, 845 | horizontal auto-layout, **gap 32** |
| App card | 237x202 | pitch **269** | app logo art |
| Media strand | 645x179 | 186, 131 | 5 tiles, same 106/116/181x179 geometry as §3.2 |

`crossing swords page` (title detail) adds a metadata row at y 525 (rating / year /
genre, Open Sans Light 30 px at x 138, 247, 347) and a "Watch on" provider row:
4 cards of **334x178** at x 138 / 524 / 910 / 1296 (pitch **386**), fill `#000000`
@ **0.75**, price label Open Sans Light 24 px at card + ~36, + ~96.

### 5.3 User select (`Login page`)

Frame background `#030712` plus a `#000000` @ 0.30 scrim.

| Element | Size | Position | Style |
|---|---|---|---|
| Title | 823x87 | 549, 169 | Open Sans Light **64 px** |
| Prompt | 441x49 | 740, 280 | Open Sans Light 36 px |
| Controller glyph + index | 56x66 | 944, 393 | white, drop shadow r4; "1" 24 px above |
| User avatar | 257x257 ellipse | 844, 508 | 2 px `#FFFFFF` inside stroke |
| "Add user" circle | 187x187 ellipse | 530, 543 | fill `#D4CECE` @ 0.28, 66 px plus glyph centred |
| Labels | 24 px Regular | 571, 794 and 894, 792 | "Add User" / "New User" |
| Button hint | 101x25 | 894, 838 | 30x24 button glyph radius 4 + "Options" Open Sans SemiBold 18 px |

### 5.4 PS Store nav bar (component, 1857x153)

| Element | Position (in component) | Style |
|---|---|---|
| Store icon | 16, 16 — 86x86 | |
| "PlayStation Store" | 127, 43 | Open Sans Regular 24 px, ls -3 % |
| Tabs: Latest / Deals / Collections / Subscriptions / Browse | 127, 260, 387, 575, 792 — all y 113 | Open Sans Regular 24 px, ls 0.5 % |
| Search / Heart / Cart / More | 1493, 1582, 1680, 1767 — y ≈ 112 | 31 / 40x35 / 40x40 / 39x11 |

Icon pitch on the right cluster is ~89–98 px.

### 5.5 Profile overlay (component, 1920x1075)

Full-screen scrim `#000000` @ **0.65**, with the profile card (259x232) anchored at
**1443, 93** — top-right, under the avatar. The card itself is a bitmap, so only the
scrim alpha and the anchor are recoverable.

## 6. Cross-check against the firmware docs

Ground truth column is the firmware-derived docs. "Figma" is this board.

### 6.1 Agreements — Figma corroborates the firmware

| Thing | Firmware (ground truth) | Figma | Verdict |
|---|---|---|---|
| Design resolution | 1920x1080 design px (`ps5-home-structure.md`, `ps5-control-center.md`, `ps5-hub-and-cards.md`) | frames 1920x1075/1080, all 189 image paints at scale 0.500 over 4K sources | **agrees**, and independently confirms 4K = 2x design |
| Resting strand tile | `EXPERIENCE_SIZE = 106` | 106 x 106 | **exact** |
| Tile corner radius | `BORDER_RADIUS = 16` | r = 16 on every tile | **exact** |
| Max strand tiles | `MAX_TILES = 11` | 11 tiles on `Home Page` | **exact** |
| Spaces | exactly 2, `["game","media"]` | exactly two labels, Games + Media, with selected/unselected variants | **exact** |
| System icon count | `systemIconsCount = 3` (search, profile, settings) | exactly 3 — Search, Settings, Avatar | **agrees on count**, order differs (Figma is search → settings → profile) |
| Top chrome band | `SYSTEM_HEIGHT = 126` | chrome occupies y 31–82; strand starts at y **131** | **agrees** — the strand begins immediately below a ~126 px band |
| Space label left inset | space-switcher `marginLeft = 84`, `SYSTEM_MARGIN = 84` | "Games" at x = **91** | **agrees within 7 px** |
| System icon focus/background circle | 28 px radius on a 56x56 native button (`PUI-NATIVE` `UI3.ButtonBase.borderRadius = Height / 2`; HOME m143 supplies the 56 px size) | avatar circle 53x53 | **agrees within 3 px**, but the Figma evidence is the separate avatar branch |
| 5-column grid gutter | `GridPadding` 5 col → paddingHorizontal **24** | both library grids: gap **24** | **exact** |
| 5-column tile width | 296 wide, pitch 320 (`ps5-home-structure.md` packing table) | 300 wide, pitch 324 | **agrees within 4 px (1.3 %)** |
| Focus enlarge factor | `EXPERIENCE_SCALE = 168/106 = **1.5849**` | content box 171/106 = **1.613**, glyph 72/46 = **1.565** | **agrees** — the two independent measurements bracket 1.5849 |
| Secondary text colour | `#FFFFFFB3` (70 % white) | unselected space label `#BFBFBF` (75 % white) | **agrees in kind** |
| Primary text colour | `#FFFFFF` | `#FFFFFF` dominates every text fill (354 solid-white fills board-wide); selected label `#FBFBFB` | **agrees** |
| Focus ring construction | 3 px stroke, offset outside the rect | card focus ring: 3 px inside stroke on a frame 4 px larger per side | **agrees in kind** — same "stroke sitting outside the content box" |

Five exact hits (106, 16, 11, two spaces, the 24 px gutter) plus the focus scale
bracketed from two directions is a strong signal that the author traced a real console
and that our firmware extraction is reading the right numbers.

### 6.2 Disagreements — firmware wins

| Thing | Firmware (ground truth) | Figma | Note |
|---|---|---|---|
| Focused strand tile | `SCALED_EXP_SIZE = **168**` | outer **181 x 179**, content **171 x 169** | **Use 168.** The 181 is the outer edge of a 5 px ring, and the content box is 171 — 3 px over 168. Reconciliation: the firmware ring is 3 px thick at a 3 px outside offset, so a 168 px tile's ring reaches 168 + 12 = **180**, within 1 px of Figma's 181. The author traced the ring, and separately traced the tile 3 px large. Offered as a hypothesis, not a fact. |
| Focused tile corner radius | `168/106 x 16 = **25.358**` | 29 on the content box, 32 on the ring | **Use 25.358.** The author did scale the radius, but by ~1.8x rather than 1.585x. |
| Strand item gap / pitch | `itemMargin = 8`, base pitch **114** | gap 10, pitch **116** | **Use 8 / 114.** 2 px tracing error. |
| Strand left inset | `strandStyle marginLeft = **172**`; focused tile's left edge lands at exactly 172 | row origin x = **186**; focused tile wherever it falls in a static row | **Use 172.** And see the structural note below. |
| Strand clip width | `strandContainer 1500 x 168` at x 172, 248 px right gutter | `Frame 10` is 1274 wide (11 tiles) and unclipped | **Use 1500 @ 172.** Figma models no clipping and no scrolling. |
| Function row height | container 1920 x **168** | row height 179 when focused | **Use 168** (same 168-vs-181 ring issue). |
| Content width | canonical **1576** (`370*4 + 32*3`, hub nav, grids) | library grids span 1596; content rows start at 176–204 | **Use 1576.** |
| Left content margin | `SYSTEM_MARGIN = 84`; `CONTAINER_MARGIN = 172`; hub nav `marginLeft 148` | wanders: 91, 135, 138, 175, 176, 181, 182, 184, 186, 204, 218 | **Use the firmware values.** This spread is the clearest evidence of hand tracing. |
| Section header height | `SEGMENT_HEADER_HEIGHT = 34` | "Must see" / "Featured" headers are 36 px type in 49 px boxes | **Not comparable / suspicious.** 36 px type cannot fit a 34 px header, so either Figma's type is oversized or these are row titles rather than grid segment headers. Unresolved; do not use Figma here. |
| Dark surface | `#020408` basemat; `#080A0F` popup panel; `#17191E` action card — explicitly three distinct surfaces | strand utility tiles `#000016` @ 0.70 | **Use the firmware triple.** `#000016` is a blue-tinted near-black that appears in no bundle. |
| Scrims | `#000000CC` (0.8) dimmer, `#0D0D0D99` (0.6) media obscure | 0.30 / 0.33 / 0.43 / 0.50 / 0.65 / 0.75 black, plus multi-stop gradients | **Use the firmware values** for named surfaces. Figma's gradients (§5.2) are the only description we have of the *shape* of the top-down scrim, so keep those as structure only. |
| Font family | **SST** (proprietary); Prosperismo substitutes Fira Sans (`ps5-fonts.md`) | **Open Sans** throughout | **Irrelevant.** Open Sans is the author's own free substitute, exactly as Fira Sans is ours. |
| Font sizes | tokens only — `Size3XSmall` … `SizeLarge`, pixel values live in the platform theme and were **not** recovered | concrete ladder: 64, 56, 52, 48, 36, 32, 30, 24, 18, 14 px | **Not verifiable either way.** See §6.4. |
| Frame width | 1920 | two frames are 1915 | trivially wrong; ignore |

### 6.3 Structural disagreement worth calling out

On the real console, focusing a strand tile **scrolls the strand** so the focused
tile's left edge pins to x = 172, with the neighbours redistributing at pitch 114
(`translateX` = `(i-sel)*114 - 8` before the selection, `+70` after). On this board the
row is static and the focused tile simply grows in place at whatever x it occupies —
x = 650 on `Astro Page`, x = 302 on `Explore Page`. **Do not take the board's focus
model.** It is a mock-up of a still frame, not of the behaviour.

Likewise, the board never renders the hub state, so it cannot corroborate the
tile → 80x80 badge at (48, 48) collapse, the `translateY 0 → -166` shell shift, or any
of the hub geometry.

### 6.4 Where Figma fills a firmware gap (unverified)

These have **no firmware counterpart at all**. Use them as proportion hints, mark
anything built from them as unverified, and replace them the moment the firmware
yields real values.

| Gap | Figma offers |
|---|---|
| Type size ladder | 14 (badge) / 18 (card label) / 24 (metadata, body) / 30 (stat value) / 32 (hub description) / 36 (primary, nav, row header) / 52–56 (hub title) / 64 (full-screen title) px. The firmware's five size tokens plausibly map onto a subset of this, but nothing confirms the mapping — and the 36 px header vs 34 px `SEGMENT_HEADER_HEIGHT` clash suggests the whole ladder may be inflated. |
| Home content area, game focused | Play pill 371x78 radius 61; More circle 84x78; both at y 817; trophy summary card 370x145 top-right at 1373, 751 with a 96 px trophy pitch (§3.3) |
| Home content area, Explore focused | headline/body/meta stack and the 380x220 news-card row at pitch 412 (§3.4) |
| Store promo row | 257x128 cards at pitch 281 (§3.5) |
| Media hub | 237x202 app cards at gap 32; 334x178 provider cards at pitch 386; the multi-stop top-down scrim gradient (§5.2) |
| Library chrome | title / storage / sort-by placement; 300x300 tiles at gutter 24 (§5.1) |
| User select | the whole layout (§5.3) |
| PS Store nav bar | tab and icon positions in a 1857x153 bar (§5.4) |
| Profile overlay | scrim at `#000000` @ 0.65, card anchored top-right at 1443, 93 (§5.5) |
| Presence indicator | 14 px dot, `#1AFF84`, bottom-right of a 53 px avatar |

### 6.5 Net assessment

The board is a **weak-positive second source on home strand geometry and grid
gutters**, a **useful gap-filler for the home content area, media hubs and libraries**,
and **worthless for the control centre, the option menu, toasts and action cards** —
it simply does not contain them. It should never be cited as authority against an
extracted firmware constant.
