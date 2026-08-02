<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 fidelity review of the launcher shell

A full audit of why the shell still does not read as a PS5, ranked by visual impact.
Reviewed at `rebrand` 9b628ab; commit 1547fd0 landed on the branch while this review was
in progress and is folded in where it matters (it is the clock fix, finding 9). Nothing
in `src/` was changed by this review.

Evidence, in order of authority, per `docs/ps5-reverse-engineering-index.md`:

1. The decrypted 3.00 RN bundles under `games\useful rnps\readable_js_3.00\`. Locators
   follow the house style: `HOME m25:3215` is bundle NPXS40002, haul module 25, line of
   the pretty printed file. New in this review: `DIALOG mNNN` is `NPXS40021.js`, the
   system dialog app (`dal-dialog_v2_ppr_releases_03.00`), mined with
   `tools/rn-layout/extract_styles.py` because no existing doc covered it.
2. `docs/ps5-rn-layout.md` (the layout contract), `docs/ps5-ui-gap-analysis.md`,
   `docs/ps5-options-menu-and-focus.md`, `docs/ps5-shell-theme.md`.
3. The community Figma file is used **nowhere** in this review as a source of numbers.
   Its geometry is refuted seven times over in the index; where it agrees with the
   bundles it adds nothing, and where it disagrees it loses. It stays useful only as a
   reminder of which surfaces exist.
4. Our code at `src/SharpEmu.GUI/`, cited file:line, plus two live captures of the
   running build (one supplied, one taken fresh for this review with the DPI-aware
   capture script; the fresh one also settles the clock question).

Markers: EXACT is a literal read from a StyleSheet or constant. INFERRED is arithmetic
or structure with the derivation shown. UNRESOLVED means the number is not recoverable
from what we hold, and inventing it would be worse than admitting it.

First, credit where it is due, so the real gaps stand out: the strand spring
(400/50/0.2 clamped), the 8/16 two-margin spacing, the 106 to 168 size-only focus
affordance, the radius-to-side ratio, the 60 ms boot stagger, the travelling warp focus
ring, the modal show/hide timings, and the 126 nav band with 84 insets and 104 icon
pitch are all implemented from the real numbers now. What follows is what still is not.

---

## The ranked findings

| # | Finding | Impact | Cost of the fix |
|---|---|---|---|
| 1 | Game covers sit on the wrong tier: the home's game row is the 106/168 experience switcher, not a 370 card strand | Decisive | Medium |
| 2 | The switcher band is spent on five utility buttons in invented dark plates | Decisive | Medium (falls out of 1) |
| 3 | Below the row is dead space where the console has a hub | High | Large |
| 4 | The bottom launch bar is a desktop toolbar the console has no counterpart for | High | Medium |
| 5 | Tile chrome is invented: violet fill, hairline border, drop shadow, cyan prism placeholders, title-id captions | High | Small |
| 6 | The per-tile options menu reads as a desktop context menu: emoji icons, a separator after every row, colored rows | High | Small to medium |
| 7 | Prompts are small desktop cards; the console's are 1312-wide DialogPS surfaces with 388 x 72 button rows over a 0.8 black scrim | Medium-high | Medium |
| 8 | The nav band is a painting: no control in it works, and the space switcher is static text | Medium | Medium |
| 9 | The clock: confirmed, root-caused, and fixed on the branch mid-review | Fixed | Done |
| 10 | Focus and hover: the ring is right in kind; a glanced state, icon labels on glance, and the overflow tail fade are missing | Medium | Small each |
| 11 | Anything past the 11th game is unreachable: there is no library surface behind the row | Functional hole | Medium |
| 12 | The frame around the shell (wordmark title bar, status bar, on-home search box, empty and loading states) is desktop-styled | Low each, additive | Small each |
| 13 | Type scale: every px value is a guess by necessity; do not tune them as if they were facts | Honesty item | n/a |

---

## 1. The game covers are on the wrong tier

**What the console does.** The home screen has exactly one row of tiles, the experience
switcher, and the games themselves are its tiles: 106 x 106 squares at rest, 168 x 168
focused, radius 16 at rest and 25.358 focused, resting pitch 114 (106 + itemMargin 8),
16 px of air either side of the focused tile, focused left edge pinned to x 172, at
most 11 tiles. All EXACT: HOME m25:3215-3224, m201:14577-14587, m47:4080, layout
contract `ps5-rn-layout.md` §2.3-2.5. The tile is an image and nothing else, and its
missing-art fallback is the app fallback icon at 168 (EXACT, HOME m47, m210). The
370-wide tile with the 32 gutter on a 402 pitch that our cards use is real firmware,
but it is the entry for a content tile inside a hub or media strand
(`HORIZONTAL_SPACING[1576][370]`, EXACT, HOME m28:3319-3352, and the SQUARE.MEDIUM
preset, HOME m721:51284). Those strands carry activities, media and store content for
a title. No bundle puts the installed-games list on that tier.

**What we do.** `MainWindow.axaml.cs:58-64` builds the game row from
`StrandTileWidth = 370`, `StrandTileGap = 32`, and `MainWindow.axaml.cs:71`
`StrandTileRestScale = 1.0`, so the game covers are a flat row of large equal cards
with no focus enlargement at all; the comment block at `MainWindow.axaml.cs:44-71`
argues game cards are content-strand tiles. `ShellTileRow` itself carries the correct
switcher model (106/168, springs, two margins) and is then configured away from it.

**Why it reads as not-PS5.** This is the single most recognisable composition on the
console and we invert it. On a PS5 the top strip is a dense ribbon of small square
game icons with exactly one blown up half again as large; the size jump is the whole
focus statement. Our screenshot shows four near-identical big cards where nothing is
clearly "the one", and the covers duplicate information the backdrop already gives.
The user's own note ("even after we set them to 370 wide... four across") is the tell:
those numbers are right for a different surface, so tuning them can never converge on
the home screen. Right table, wrong screen.

**Fix.** Feed the games into the switcher tier: one row, `ShellTileRow` at its own
defaults (106/168/8/16/114/172, rest scale 106/168), cover art as the tile image,
fallback icon instead of initials. Delete the 370 configuration from
`MainWindow.axaml.cs`. The control already implements everything needed; this is
mostly deleting the overrides. Medium, because finding 2 and 3 decide what happens to
the row that used to be there.

Confidence: high. Every number EXACT; the tier assignment (games live in the switcher)
is INFERRED from structure but corroborated by `ps5-ui-gap-analysis.md` §1 and by the
switcher's own app fallback icon and options-menu items (check for update, save data,
delete, close application; EXACT, HOME m525:37923), which only make sense on game
tiles.

## 2. The switcher band is spent on five utility buttons

**What the console does.** The 168 band holds the experiences themselves. System
destinations (search, settings, profile) are the 56 px icons in the 126 nav band
(EXACT, HOME m143:10653), reached as their own focus region (`home-system`, EXACT,
HOME m217). Nothing in the home bundle draws a plate, border or shadow behind a
switcher tile: the tile stylesheet is `imageWrapper { borderRadius: 16 }` and an image
(EXACT, HOME m210; EXACT absence of any background/border/shadow rule).

**What we do.** `MainWindow.axaml.cs:658-672` fills a `ShellFunctionRow` with Library,
Search, Add folder, Rescan, Options, and `ShellFunctionRow.cs:259-272` gives every
icon an invented `#17191E` plate (`#232833` focused), a `#1AFFFFFF` hairline border and
a `0 4 12 0 #40000000` drop shadow, with a glyph or dump pictogram inside. Search and
Settings therefore appear twice on one screen (function row and nav band), and the
band that defines the console's silhouette is occupied by tool buttons.

**Why it reads as not-PS5.** The eye parses a strip of identical dark rounded plates
with line glyphs as a settings toolbar or a media-centre dock. The console's strip is
a row of full-bleed cover art. No amount of correct geometry survives the wrong
content: this row has the right pitch and the right springs and still reads as chrome,
not games.

**Fix.** Once the games move into this band (finding 1), the utility functions go
where the console keeps them: search and settings already exist in the nav band (make
them work, finding 8); add-folder and rescan belong in the options page or behind a
library tile. If a Library destination tile is kept in the row, that choice is ours
and should be marked as such, not attributed to the console. UNRESOLVED whether the
real switcher appends any non-game tile; nothing in HOME suggests one.

Confidence: high on the console side (EXACT absences are still absences from the real
stylesheet); the relocation is a design decision the bundle constrains but does not
dictate.

## 3. Below the row: dead space where the console has a hub

**What the console does.** Under the switcher sits the hub viewer for the focused
title, from y 128 (marginTop `SCALED_EXP_SIZE - VERTICAL_HEIGHT_CHANGE` = 168 - 40,
EXACT, HOME m490:34809), a full-width surface of content strands (1576 wide inside the
172 margins, EXACT, HOME m28) with section headers 58 tall (EXACT, HUB:356). The hub is
why the home feels alive: two thirds of the screen belongs to the focused game.

**What we do.** `MainWindow.axaml:231-334` stacks function row, strand, and nothing.
Both captures show the lower half of the window as empty backdrop between the cards
and the launch bar.

**Why it reads as not-PS5.** Emptiness at that scale reads as an unfinished launcher.
The console never shows bare wallpaper below the row while a game is focused.

**Fix.** A first hub pass needs only what we already have per title: a strand of
SQUARE.MEDIUM 370 tiles (the table finding 1 evicts from the game row is exactly right
here) carrying key art, screenshots from the dump, a Play tile, and the game's
metadata. Large, and worth ranking behind findings 1 and 2 because an empty band under
a correct switcher still reads more PS5 than the current inversion.

Confidence: high; geometry EXACT, content of a sensible minimal hub is our choice.

## 4. The bottom launch bar

**What the console does.** There is no persistent bottom bar on the home screen, and
no visible text of the game's install path anywhere in the shell. Launch is pressing
the focused tile. Title, tags and metadata live beside the focused tile
(TitleContainer at x 356, y 106, with the 62 px metadata strip, EXACT, HOME
m214:15401-15408 and §2.6 of the layout contract) and in the hub. Stopping a running
title is `MENU_ID_APPLICATION_CLOSE` in the options menu (EXACT, HOME m525:37923)
followed by a system confirm dialog (the DialogPS surface of finding 7). The nearest
thing to a bottom band is the keyguide hint row, which is glyph hints, not buttons
(`keyGuideContainer` pinned to the bottom, EXACT, HOME m278:21978; hint message ids,
EXACT, HOME m540).

**What we do.** `MainWindow.axaml:696-758`: a rounded desktop card with a 56 px cover
thumbnail, bold title, title-id/version/size pills, the filesystem path in 11 px grey,
a status dot, and three buttons (Console toggle, accent Launch, red Stop) with 8 px
corner radii and hover fills (`App.axaml:151-204`).

**Why it reads as not-PS5.** It is the strongest desktop signal on the screen: a
toolbar with buttons is Windows grammar, and it is present in every screenshot no
matter how good the row above gets. The path line and the pills say "file manager".

**Fix.** Remove the bar from the home surface. Launch stays on tile activation (it
already is: `MainWindow.axaml.cs:632`). Title and metadata move beside the focused
tile per HOME m214. Stop moves to the options menu plus a confirm modal (finding 7).
The Console toggle is developer tooling: put it behind a hotkey or the options page.
Keep a keyguide-style hint strip at the bottom if the affordances need surfacing; that
is the console's own pattern for exactly this. Medium.

Confidence: high. The absence of such a bar on the console is structural (nothing in
HOME renders a persistent bottom action bar); the replacement pattern is EXACT-backed.

## 5. Tile chrome is invented

**What the console does.** Tile surface palette is neutral: `DARK_GREY #353535`,
`GREY #292929`, `BLANK rgba(255,255,255,0.05)`, `OBSCURE rgba(13,13,13,0.6)` (all
EXACT, HOME m19:2858-2863). The tile draws no background behind art, no border, no
shadow (EXACT absence, HOME m210). Missing art shows the neutral app fallback icon
(EXACT, HOME m47). Sub-labels are `Size3XSmall` at opacity 0.7 (EXACT, HOME m19:2964),
and nowhere in the shell is a title id shown to the user.

**What we do.** `ShellTileRow.cs:503-505` paints every cover on a violet `#241A3C`
fill with a 1.5 px `#3A2A5C` border (`:1117-1118`) and a permanent
`0 6 16 0 #40000000` shadow (`:511-512`, `:1119`). Placeholders (`:1171-1214`) are a
blue-violet gradient with a radial cyan wash and cyan-gradient initials. The caption
under the focused card prints the title id as its second line
(`MainWindow.axaml.cs:884` passes `game.TitleId` as the subtitle;
`ShellTileRow.cs:1036-1041` renders it at 13 px). The function row plates are the same
family of invention (finding 2).

**Why it reads as not-PS5.** The violet cast plus a visible frame around every cover
is third-party-skin grammar; the console's covers are frameless art on near-black.
The title id under the focused game is emulator-forum grammar; Sony never surfaces
`PPSA21564` on the home. The shadow question is softer: no shadow exists in the RN
styles, but a native layer could add one, so mark the shadow UNRESOLVED rather than
proven wrong (per `ps5-ui-gap-analysis.md` "Not determinable"). The fill and border
are proven wrong: the stylesheet is exhaustive about what a tile carries.

**Fix.** One small patch: fill to `#353535`/`#292929` neutrals or nothing, border
gone, placeholder to the neutral gradient with the dump's fallback icon when present,
drop the title id from the caption. Keep the shadow only if someone shows it on a
capture of the real console. Small.

Confidence: high, EXACT except the shadow, which is UNRESOLVED.

## 6. The per-tile options menu

**Current status (2026-08-02): fixed at the recovered managed boundary.** The
menu is now one monochrome list with vector pictograms, a single section break,
no desktop section-header row, and no destructive-red treatment. Its panel
geometry remains explicitly unresolved because `OptionsMenuPS` is native; the
652-wide analogue is not claimed as a measured options-menu value.

**What the console does.** The options menu is a native component: JS hands
`OptionsMenuPS` an item list and an anchor and draws nothing itself (EXACT, HOME m840,
m514; `ps5-rn-layout.md` §2.14). Its panel geometry is therefore UNRESOLVED; the
nearest real analogues are the function-control popover (652 wide, 216 to 810 tall,
radius 16, EXACT, HOME m143:10683) and the sort/filter panel rows (72 tall with a 72
leading icon gutter, EXACT, HOME m259:20683). The context items for a game tile are a
flat list (check for update, save data, delete, remove from home, information, close
application; EXACT, HOME m525:37923) plus globals pushed ahead (eject disc, EXACT,
HOME m514). The one separator idiom in the mined set is a single 2 px
`rgba(255,255,255,0.1)` line between sections, not between rows (EXACT, HOME
m679:48457, m278). Menu focus is the same travelling rectangle ring as everything
else, not a row fill (EXACT, `focusStyle: "rectangle"`, RN-BASE:8618 per
`ps5-options-menu-and-focus.md` §5).

**What we do.** `MainWindow.axaml:239-305`: an Avalonia `ContextMenu` whose icons are
emoji and dingbat TextBlocks at 32 px (▶ 📂 ⚙ ✕ ⧉), with a `Separator` between
every pair of rows, an explicit "System" section header, a red destructive row, and
`Placement="Right"`. The chrome in `App.axaml:239-335` is genuinely close (opaque
near-black card, radius 16, 652 to 784 width, 98 px rows, ring-based focus, no row
fill), and `ShellMotion.cs` gives it the right 250/50 show and 300 linear hide.

**Why it reads as not-PS5.** Emoji render in the Windows color-emoji font: one glance
at a full-color 📂 next to greyscale UI says desktop app. Separators after every row
turn a quiet list into a ruled table. A visible "System" header with different
styling, and a red row, are Windows menu grammar; the console's list is uniform white
rows. The width is also worth an honest label: 652 is the popover analogue, not a
measured options-menu number, and the code comments should say analogue, not fact.

**Fix.** Replace every emoji with monochrome vector marks or dump pictograms (the
settings row already does this, `MainWindow.axaml.cs:706`); keep at most one
section-break separator; drop the header row styling to a plain sub-label or remove
it; make the destructive row white (its confirm dialog is where the weight goes).
Small. Adopting the real item taxonomy (update check, information, close) where the
emulator has equivalents is a nice-to-have on top. The panel geometry stays flagged
UNRESOLVED until someone measures a console capture.

Confidence: high on the emoji/separator/header calls (grammar mismatches, EXACT idiom
on the console side); medium on exact panel numbers, which are and remain analogues.

## 7. Prompts and dialogs

**What the console does.** All system prompts ride one native primitive. NPXS40021
(`dal-dialog`) builds every dialog it owns on `DialogPS` with
`presentationStyle: "fullScreen" | "popup"`, `DialogPS.Title`, and a button factory
for positive/negative rows (EXACT, DIALOG m461 lines 30130-30323, m462 30324-30542,
m463 30543-30695; source paths
`@rnps-ppr/ui-shared-utilities-error-dialog/src/components/*.js`). What the JS pins
numerically: the suggestion dialog body is 1312 x 696 fullscreen and 1312 x 594 popup
(EXACT, DIALOG m463 `stylesFull`/`stylesPop`), body text is `SizeNormal`, error codes
`SizeXSmall` at opacity 0.7 in a 64-tall strip (EXACT, DIALOG m461). Cross-bundle
corroboration: buttons are 388 x 72 (EXACT, SET m24 `SELECTMODE_BUTTON_WIDTH/HEIGHT`;
AC:89741 text button width 388; button height 72 recurs in CC:36989, HUB:24702,
LIB:20830), the action-cards host's popup dialog is 764 x 440 with a 676 body (EXACT,
AC:32294-32295, AC:227005-227006), the modal scrim is `rgba(0,0,0,0.8)` (EXACT, HOME
m632:44435), and modal motion is show 250 ms after a 50 ms delay on easeOutBlast, hide
300 ms linear (EXACT, HOME m677:48013-48021). The 1312 body width equals the settings
list width (SET m24), so the console's prompt scale is roughly two thirds of the
screen, title left-aligned above a body, with a centred column of wide flat buttons.
The panel's own fill and radius are native and UNRESOLVED.

**What we do.** `MainWindow.axaml:817` shows the loading prompt as a 380-wide card
with a 16 px title, 12 px grey body and an indeterminate progress bar;
`MainWindow.axaml:773` the session bar popup is a 598 x 58 toolbar card;
`PerGameSettingsDialog.cs` and `ConsoleWindow.cs` are separate desktop windows. Our
buttons are 8 px radius padded rectangles with hover fills (`App.axaml:151-204`).
There is no scrim behind any of them. `Border.ps5Card` (`App.axaml:339-345`) has the
right surface idea but at a third of the console's scale.

**Why it reads as not-PS5.** Scale and posture. A PS5 prompt is an event: the screen
dims to 20 % and a wide, calm surface takes over, with big flat buttons a thumbstick
can walk. A 380 px card with a progress bar in the corner of the eye is a Windows
toast. The user's instinct that the launch bar "should be like ps5 modal/prompt" is
this finding plus finding 4: press the tile, and any confirmation that is needed
arrives as the big dialog, not as a persistent toolbar.

**Fix.** One `ShellDialog` control: 0.8 black scrim over the whole shell surface, body
1312 wide centred (594-class height for short prompts), title and `SizeNormal` body,
buttons 388 x 72 stacked centre with ring focus, shown and hidden on the ShellMotion
timings that already exist. Route launch-loading, stop-confirm and error text through
it. Medium; the motion and focus-ring pieces are already built.

Confidence: high on everything marked EXACT; panel fill/radius UNRESOLVED (native),
so style those from the existing `OptionSurfaceBrush` and say so in the comment.

## 8. The nav band is a painting

**What the console does.** The 126-band is interactive on both ends. The space
switcher is a real control with exactly two spaces, bold selected label, normal-weight
0.6-opacity unselected label, 64 gutters and 8 padding (EXACT, HOME m815:60085, m513),
and switching spaces slides the whole home 1920 px per index (EXACT, HOME:42216). The
system cluster is a focus region (`home-system`) wired left to the space switcher and
down to the switcher row (EXACT, HOME m217), each icon a 56 box that deep-links
(`pssearch:main`, `pssettings:play?mode=settings`, EXACT, HOME m624), with a label
strip under the icons on glance (`labelContainer` at top 56, width 368, `SizeXSmall`,
EXACT, HOME m143:10653) and a profile popover hanging at 652 x 216-810 under the band
(EXACT, HOME m143:10683).

**What we do.** `MainWindow.axaml:173-181`: two static TextBlocks ("Games", "Media")
with no handlers anywhere in the code-behind (no reference to `SpaceGameLabel` or
`SpaceMediaLabel` outside the markup). `MainWindow.axaml:192-212`: three 56 px icon
panels with no pointer handlers, no keyboard focus, and no region in the focus graph
(`SetUpHomeLayout`, `MainWindow.axaml.cs:615-651`, wires exactly two regions, function
row and strand). The working search and settings entries are the duplicates down in
the function row.

**Why it reads as not-PS5.** Statically it passes; the geometry is right. It fails the
moment the user moves: on the console, up from the row lands on the system icons and
the labels fade in, and clicking Media changes worlds. Here the pointer sweeps through
dead pixels, which is exactly the "didn't check the hovering" doubt the user recorded.
A control that does nothing also forces the duplication in finding 2.

**Fix.** Add a `home-system` region to `ShellFocusGraph` (up from the switcher),
wire the search icon to the search strip, settings to the options page, profile to a
placeholder popover; show `SizeXSmall` labels on glance/hover under the icons; make
Media at minimum a disabled-but-honest state or an empty media space. Medium.

Confidence: high; all console-side rows EXACT.

## 9. The clock (confirmed, root-caused, fixed)

**Confirmed.** At 9b628ab the nav clock renders a static `00:00`, as in the supplied
capture.

**Cause.** `MainWindow.axaml:217` declares
`<TextBlock x:Name="SystemClockText" Text="00:00" ...>` as a resting value, and at
9b628ab nothing in the code-behind ever writes `SystemClockText.Text`: its only
reference was the layout-diagnostics dump (`MainWindow.axaml.cs`, the
`StartLayoutDiagnosticsIfRequested` table). The clock was markup with no driver. Not a
format bug, not a timezone bug, not a binding failure.

**Fix, already landed.** Commit 1547fd0 (on `rebrand`, after 9b628ab) adds
`StartSystemClock()` (`MainWindow.axaml.cs:2439-2462`), called from the `Loaded`
handler (`:229`): sets `HH:mm` immediately, then a 1 s `DispatcherTimer` that rewrites
the text only when the minute changes, stopped on window close. Verified live for this
review: a fresh build shows the real time (12:29 during capture) in the band.

**Residual nits, small.** The console's clock is `fontVariant: ["tabular-nums"]`
(EXACT, HOME m623:43946); the TextBlock relies on the font's default figures, so the
band can shuffle a pixel when digits change. And `FontSize="28"` stands in for
`SizeLarge`, which is UNRESOLVED (finding 13). Both cosmetic.

## 10. Focus and hover

**What is right.** The ring is right in kind: one travelling renderer per scene, 3 px
stroke at 3 px outside the rect with 1.5 px AA, 0.30 s move with a 0.25 s warp seeded
one frame in, 80 px stretch budget (`ShellFocusRing.cs:45-140`), which matches the PUI
metadata in `docs/ps5-options-menu-and-focus.md`. Hover on tiles moves the selection
(`ShellTileRow.cs:1141`, `ShellFunctionRow.cs:681` on `PointerEntered`), which is the
right call for a pointer on this UI: no separate hover highlight competes with the
ring. Verified by code read; not exercised frame-by-frame in a capture.

**Gaps.**

- **No glanced state.** The console distinguishes GLANCED, FOCUSED, ACTION (EXACT,
  HOME m720), and drives real differences off it: the action indicator sits at opacity
  0.7 glanced and 1.0 focused (EXACT, HOME m737:53635), nav icon labels appear on
  glance (finding 8). We have a binary selected/not. Small, and mostly matters once
  the nav band and hub exist.
- **Focus ring width conflict, unresolved.** The RN bundle says `FOCUS_WIDTH = 8`
  (EXACT, CC:2640); the PUI metadata says 3 (EXACT there). We ship 3
  (`ShellFocusRing.cs:102`). The index lists this as an open conflict; only a console
  capture settles it. Flagging so nobody "fixes" it to 8 on bundle authority alone,
  or defends 3 as settled.
- **Ring colour.** `#00BAFF` (`ShellFocusRing.cs:494-497`) has tier-2 support as
  `DefaultThemeFocusColor` (`docs/ps5-shell-theme.md:37`), but note
  `docs/ps5-unknowns.md:168` still calls it a placeholder absent from all JS. The two
  docs disagree; the theme doc is the stronger source. Reasonable to keep, wrong to
  call settled. What is not supported is cyan spread beyond the ring (finding 12).
- **The overflow tail fade is missing.** The only dimming the console applies to the
  row is a black mat on the 8th, 9th and 10th tiles past the selection at 0.05, 0.2,
  0.4 (EXACT, HOME m573, HOME:41609), a "more content past here" cue. With 11 tiles in
  the row the case is reachable. `ShellTileRow` has no mat. Small.

## 11. Past eleven games, nothing

**What the console does.** The switcher caps at 11 (EXACT, HOME m47:4080) and
everything else lives in the Game Library app (NPXS40071): a 5 x 3 grid, item margin
20, strand 1576 inside 172 margins (EXACT, LIB:8922-8923, LIB:12698, LIB:2830-2832).

**What we do.** `ShellTileRow.cs:1001` takes the first `MaxTiles = 11` of the visible
games, and there is no other browsing surface: the old grid is gone (the
`ListBox.tileGrid` styles at `App.axaml:358-397` are now orphaned; `GameList` at
`MainWindow.axaml:340` is invisible and classless). A 12th game can be reached only by
typing into search until it filters into the first eleven. The function row's
"Library" tile (`MainWindow.axaml.cs:794-797`) just refocuses the same capped row.

**Fix.** A library page on the LIB numbers: 5 across, margin 20, inside the 172
margins, ring focus, opened from the Library destination. Medium. Until it exists, the
11 cap is a data-loss bug wearing a fidelity costume; alternatively lift the cap on
our row as an explicit divergence and say so in the comment.

Confidence: high; the hole is directly observable in code, the target numbers EXACT.

## 12. The desktop frame and the leftovers

Each small; together they keep the shell reading as an app that contains a PS5 rather
than a PS5.

- **Title bar and status bar.** `MainWindow.axaml:69-82`: gradient "PROSPERISMO"
  wordmark and version pill; `:828-835`: a status strip showing the emulator exe path
  and scan counts. The console shows no filesystem path anywhere, ever. Move both
  lines into the console window or the options page; keep the OS chrome minimal.
- **A visible TextBox on home.** `MainWindow.axaml:130`, unfolded over the home by the
  band's search tile. The console never shows an inline text input on home; search is
  its own surface (`pssearch:main` deep link, EXACT, HOME m624). Cheap improvement:
  restyle the strip as an overlay panel; real fix arrives with a search surface.
- **Empty state.** `MainWindow.axaml:312-321`: a 🎮 emoji, "Your library is empty",
  and an accent button. Console grammar would be the neutral fallback tile plus a
  plain sentence and a keyguide hint; the emoji and the cyan button are the loud
  parts. UNRESOLVED what the console's exact first-run home looks like (not in the
  mined set); staying neutral is safe either way.
- **Loading state.** `MainWindow.axaml:326-331` an indeterminate bar; the console's
  loading grammar is shimmer placeholders at opacity 0.05 to 0.08 over 750 ms
  (EXACT, HOME m719:51151, HOME:53876). Tile-shaped shimmer placeholders during a
  scan would read natively.
- **Cyan spread.** `App.axaml:14,26-33,36,58-63`: the focus colour is also the accent
  for buttons, info text, the wordmark gradient and placeholder initials. On the
  console nothing but the focus treatment is tinted; the only JS accent is the gold
  tag pair (EXACT, HOME:48546-48559). Keeping cyan strictly on the ring (and the
  boot-era prism art) would quiet the whole shell.
- **L1/R1 chips.** `MainWindow.axaml:114-125` letter chips; with a dump the real
  keyguide glyphs already replace them (`MainWindow.axaml.cs:701-702`), which is the
  right pattern; the chips are only visible dumpless. Fine as is.

## 13. The type scale is a stack of guesses; treat it as one

Every text size in the shell is a symbolic token (`FontSizePS.SizeLarge` for space
labels and clock, `SizeNormal` for the switcher title and dialog bodies, `SizeXSmall`
for icon labels, `Size3XSmall` for sub-labels) whose pixel values resolve through a
native module and are recoverable from nothing we hold (EXACT names, UNRESOLVED
values; BASE:11362-11377, `ps5-rn-layout.md` §1.2 and §11). Our 28 (`MainWindow.axaml:176,217`),
22/13 (`ShellTileRow.cs:1030,1039`), 15 (`ShellFunctionRow.cs:857`) are placeholders
that cannot be validated or falsified against the bundles. Two consequences: do not
tune them against screenshots of our own shell and call it conformance, and keep the
token names in comments so a future native dump can land in one pass. The relative
ordering we ship (28 > 22 > 15 > 13) at least respects the token ladder; SizeNormal
(dialog body, switcher title) should not render smaller than SizeXSmall surfaces if
sizes get shuffled later.

Also filed here for honesty: the font itself. The console uses the SST family
(`docs/ps5-fonts.md`); we ship Fira Sans plus a display face for the wordmark
(`App.axaml:40,99`). Fira is a reasonable open stand-in; it is still a visible
difference in every string on screen, and nothing further can be done without a
licensed match.

---

## What was checked and found already right

So the next pass does not re-litigate them: strand focus spring 400/50/0.2 clamped
(`ShellTileRow.cs:138` vs HOME m530:38151); two-margin spacing 8/16 and pitch 114
(`ShellFunctionRow.cs:203-213`); no invented dim or lift on tiles (removed; asserted
in `ShellTileRow.cs:405-416`); radius-to-side ratio 0.150943 held at every size
(`ShellTileRow.cs:453`, `ShellFunctionRow.cs:175-184`); 60 ms boot stagger with the
1050+333 ms caption delay (`ShellTileRow.cs:476-480`); MAX_TILES 11 enforced; 126 nav
band, 84 insets, 56 icons at pitch 104, clock gutter 88 (`MainWindow.axaml:161-219`);
modal show 250/50 blast and hide 300 linear (`ShellMotion.cs:78-91`); the parametric
ease-out family instead of bezier stand-ins (`ShellMotion.cs:28-49`); menu surface,
98 px rows, ring-based row focus (`App.axaml:239-301`); UI sounds and ambient bed from
the dump; 1920 x 1080 fixed surface scaled as a plate (`MainWindow.axaml:148-157`).
The bones are now genuinely console-shaped; findings 1 to 4 are about what is hung on
them.

## Verification notes

- Fresh capture taken for this review after building at 1547fd0-era tree state:
  clock live, ring travel and tile growth on hover observed on the strand,
  Superliminal focused at 370 with the caption and title id beneath it, lower half of
  the window empty, launch bar present. The capture tool's DPI awareness was
  confirmed in its own output (`awareness=2`, 1938 x 1038 at dpi 120).
- NPXS40021 was mined for this review with
  `python tools/rn-layout/extract_styles.py "...\NPXS40021.js" --sources` and
  `--module 461/462/463`. It should get a proper section in `ps5-rn-layout.md` when
  the dialog work starts; the numbers quoted in finding 7 are the load-bearing ones.
- Open questions this review could not settle, so they are not settled: options menu
  panel geometry (native), dialog panel fill and radius (native), focus ring width 8
  vs 3, focus colour spread beyond tier-2 metadata, `FontSizePS` pixel values, whether
  the console draws any tile shadow, the switcher's non-game tail tile if any, and the
  console's first-run empty home. Each is marked UNRESOLVED at its finding.
