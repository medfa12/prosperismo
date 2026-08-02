<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell icons

The launcher borrows the system shell's own icon art at runtime, from a
user-provided decrypted firmware dump. This is the same arrangement already used
for the hub wallpaper, the boot chime and the UI interaction cues: the art is
read from the user's own disk, it is never redistributed with the emulator, and
everything degrades to a hand-drawn glyph when no dump is present.

The provider is `src/SharpEmu.GUI/SystemAssets/ShellIcons.cs`. It locates the
container through `RnpsShellAssets.LocateDumpRoot()`, extracts entries with
`SystemAssets/Rco/RcoContainer.cs` (format notes in [rco-format.md](rco-format.md))
and hands out decoded Avalonia bitmaps. `ShellIcons.TryGet` returns null for
anything unavailable, so a caller always has a fallback path to take.

## Where the art lives

Everything used here comes from one container:

```
<dump>/filesystems/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco
```

1092 data-bearing entries: 341 PNG, 675 SVG, 45 VAG, 31 JSON. The VAG entries
are the interaction cues `ShellUiSounds` plays; the PNG entries are what this
file is about.

Entries appear under several source attributes. Where an icon has both, `src` is
the 1080p asset and `src_4k` is the 2160p one — the keyguide glyphs ship at
40x32 under `src` and 80x64 under `src_4k`. The loader takes `src_4k` and falls
back to `src`, because the launcher draws these larger than the shell's own
footer does.

Other containers in the dump were checked and add nothing: `Sce.PlayStation.PUI.rco`
(7315 entries) carries the PS4-era `emoji_*` set, `ReactNative.Components.CommonAssets.rco`
carries illustration and empty-state art, and the `Sce.Vsh.ShellUI.*.rco` set
under `system_ex/app/NPXS40087/psm/Application/resource/` carries per-scene
textures. None of them has a bitmap search, folder, copy, delete or play icon.

## The DualSense button glyphs

The priority set, and the one that made the launcher's button hints wrong rather
than merely plain: it lettered its shoulder-button chips **LB/RB**, which is Xbox
nomenclature. PlayStation calls those buttons **L1/R1**. That correction is
unconditional — the chips read L1/R1 with or without a dump — and with a dump the
shell's own keyguide art replaces the chip outright.

All fifteen decode to clean, non-blank images: light-grey button caps with a dark
mark, the same art the shell draws in its footer key guides.

| Logical icon | RCO entry | `src_4k` | `src` | What it looks like |
| --- | --- | --- | --- | --- |
| `L1` | `image_keyguide_l1` | 80x64 PNG | 40x32 | rounded shoulder cap lettered L1 |
| `R1` | `image_keyguide_r1` | 80x64 PNG | 40x32 | rounded shoulder cap lettered R1 |
| `L2` | `image_keyguide_l2` | 80x64 PNG | 40x32 | rounded cap lettered L2 |
| `R2` | `image_keyguide_r2` | 80x64 PNG | 40x32 | rounded cap lettered R2 |
| `L3` | `image_keyguide_l3` | 80x64 PNG | 40x32 | rounded cap lettered L3 |
| `R3` | `image_keyguide_r3` | 80x64 PNG | 40x32 | rounded cap lettered R3 |
| `Cross` | `image_keyguide_cross` | 64x64 PNG | 32x32 | round face button, cross |
| `Circle` | `image_keyguide_circle` | 64x64 PNG | 32x32 | round face button, circle |
| `Square` | `image_keyguide_square` | 64x64 PNG | 32x32 | round face button, square |
| `Triangle` | `image_keyguide_triangle` | 64x64 PNG | 32x32 | round face button, triangle |
| `OptionsButton` | `image_keyguide_options` | 80x64 PNG | 40x32 | three stacked bars |
| `CreateButton` | `image_keyguide_create` | 80x64 PNG | 40x32 | the create mark |
| `PsButton` | `image_keyguide_ps` | 64x64 PNG | 32x32 | round button, PS logo |
| `LeftStick` | `image_keyguide_left_stick` | 68x64 PNG | 34x32 | three-quarter stick, L |
| `RightStick` | `image_keyguide_right_stick` | 68x64 PNG | 34x32 | three-quarter stick, R |

Most keyguide glyphs are 8-bit palette PNGs (colour type 3); a few are RGBA
(colour type 6). Both decode through Avalonia's PNG decoder unchanged.

Two related sets exist and are deliberately not used:

- `image_keyguide_*_onbutton` (52x48 / 26x24) — the flatter variant the shell
  draws when a glyph sits inside a button rather than in a footer. The launcher's
  hints are footer-style, so the plain variant is the right one.
- `emoji_key_l1`, `emoji_key_r1`, `emoji_key_options`, `emoji_key_ps`,
  `emoji_key_face_*`, `emoji_key_analog_stick*`, `emoji_key_arrow_*` (~82x64
  RGBA) — the inline-text versions the shell splices into strings. Same subject,
  looser optical sizing; the keyguide set is cleaner at chip size.

## The pictograms

| Logical icon | RCO entry | Size | Replaces |
| --- | --- | --- | --- |
| `Settings` | `emoji_settings` | 72x64 RGBA PNG | the `⚙` on the Options function tile and on the "Game settings…" menu row |
| `Library` | `emoji_game_and_apps` | 80x64 RGBA PNG | the `🎮` on the Library function tile |
| `Controller` | `emoji_game` | 76x64 RGBA PNG | nothing yet — see below |
| `Storage` | `emoji_storage` | 69x64 RGBA PNG | nothing yet |
| `System` | `emoji_system` | 69x64 RGBA PNG | nothing yet |

All five are white silhouettes on transparency, which suits the launcher's dark
surfaces directly.

## What kept its glyph, and why

The shell keeps most of its pictogram set as SVG. Avalonia has no built-in SVG
renderer, and rasterising these would mean either a new package dependency or a
hand-rolled partial SVG parser; neither is worth it for five icons. So these stay
as our own marks, and `ShellIcons.VectorOnlyEntryNames` records the entry each
one would come from if that ever changes:

| Logical icon | Vector entry | Our mark | Used by |
| --- | --- | --- | --- |
| `Search` | `iconid_search` | `🔍` | Search function tile |
| `AddFolder` | `iconid_add_folder` | `＋` | Add folder function tile |
| `Rescan` | `iconid_update` | `⟳` | Rescan function tile |
| `Launch` | `iconid_control_play` | `▶` | "Launch" menu row |
| `Folder` | `iconid_folder` | `📂` | "Open game folder" menu row |
| `Copy` | `iconid_copy` | `⧉` | "Copy path", "Copy title ID" menu rows |
| `Remove` | `iconid_delete` | `✕` | "Remove from library" menu row |

Forcing a bitmap match for these would have meant a worse icon, not a more
authentic one: the nearest bitmap candidates were `emoji_no_operation_allowed`
(a prohibition sign) for remove and `emoji_download` for rescan, and neither
means what the row does.

`image_button_base`, `image_emphasisbutton_base` and `image_keyguide_base` are
also PNG but are not icons — they are 11x11 and 22x22 nine-slice chrome blobs the
shell stretches behind a control. Nothing here uses them.

## A controller illustration for later

There is no full DualSense product render in the dump's bitmap sets. The closest
is `emoji_game` (76x64), a clean front-on DualSense silhouette, exposed as
`ShellIcon.Controller`; it would work as the header mark on a future
controller-settings screen. The richer controller and peripheral art —
`iconid_game_controller_1` through `_4`, `iconid_ds4_connected_via_usb`,
`iconid_ps5_media_remote`, `iconid_psvr2_motion_controller*`, and the
`iconid_controller_key_*` family covering the touchpad, drag, pinch, swipe and
tap gestures — is all SVG, so it is subject to the same limitation as above.

## Runtime and licensing

Nothing from a firmware dump is committed to this repository. The icons are
copyrighted Sony assets; they are read from the user's own dump at runtime, cached
in memory, and never written back out. `ShellIcons.IsAvailable()` reports whether
a dump was found, and every icon has a fallback glyph so the launcher looks
complete without one.

The provider is cheap when the dump is absent (one `File.Exists`), extracts on a
background thread, and raises `ShellIcons.Loaded` when it finishes so the
launcher can swap its glyphs for the real art in place. Nothing it does throws.
