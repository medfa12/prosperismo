<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 fonts and their open equivalents

The PS5 shell is set in Sony's corporate **SST** family, designed by Akira
Kobayashi (Monotype) with Sony's design team. SST and its siblings are
proprietary Monotype/Sony fonts and must never ship with Prosperismo, in any
form. This page records which open-license typefaces we use in their place,
and why.

## The Sony family

| Sony font | Role on the PS5 | Character |
|---|---|---|
| SST (Roman/Light/Medium/Bold + italics) | Shell UI, system text | Humanist sans; Kobayashi describes it as a hybrid of Helvetica's neutral geometry and Frutiger's open, legible humanism. Single-storey `g`, double-storey `a`, wide apertures, understated ("it has the notable feature of having no notable features"). |
| SSTJpPro | Japanese/CJK UI text | CJK companion to SST |
| SSTTypewriter | Monospaced contexts | Typewriter/mono companion |

## Our mapping

| Sony font | Open equivalent | License | Status |
|---|---|---|---|
| SST | **Fira Sans** (Regular/Medium/SemiBold/Bold) | SIL OFL 1.1 | Embedded in `assets/fonts/FiraSans/`, default UI face |
| SSTJpPro | Noto Sans JP (or Source Han Sans) | SIL OFL 1.1 | Not embedded; the UI is Latin-first and CJK falls back to system fonts. Wire it if we ever ship a CJK-complete UI. |
| SSTTypewriter | Cascadia Mono / Consolas (system) | n/a (not shipped) | The console view keeps the system mono stack. Fira Mono (OFL 1.1) is the matching companion if we ever need to embed one. |

## Why Fira Sans

SST sits halfway between Helvetica and Frutiger. The UI previously used Inter,
which covers only the Helvetica half. Candidates were compared one by one
against SST's traits (humanist skeleton, single-storey `g`, open apertures,
neutral voice, screen-tuned, Medium/SemiBold weights, Latin+Greek+Cyrillic —
the GUI ships `ru.json`):

- **Inter** (OFL 1.1) — a neo-grotesque in the Helvetica/Roboto mould: tall
  x-height, double-storey `g`, tighter apertures, mechanical rhythm. Excellent
  UI font, but it reads technical rather than humanist; it matches SST's
  neutral half and misses the Frutiger half entirely.
- **IBM Plex Sans** (OFL 1.1) — grotesque with deliberate IBM quirks (sheared
  terminals, distinctive `a`/`g`/`l`). Too much brand personality for a face
  whose brief was "no notable features".
- **Rubik** (OFL 1.1) — geometric with rounded corners; wrong genre.
- **Hind** (OFL 1.1) — the closest pure-Frutiger revival of the bunch
  (single-storey `g`, wide apertures, UI-oriented weights). Rejected on
  coverage: Latin + Devanagari only — no Cyrillic or Greek — plus the short
  descenders it inherits from its Devanagari companion, and no italics.
- **Archivo** (OFL 1.1) — American grotesque heritage; too squarish and
  ink-trappy for SST's clean look.
- **Encode Sans** (OFL 1.1) — capable superfamily but its default width feels
  slightly condensed and its voice is more grotesque than humanist.
- **Mulish** (OFL 1.1) — minimalist geometric-humanist; rounder and more
  geometric than SST.
- **Source Sans 3** (OFL 1.1) — genuinely humanist with open apertures and a
  full weight axis, a close second. Its American gothic flavor (News
  Gothic/Franklin roots) and narrower body drift from SST's
  European Frutiger/Helvetica lineage.
- **Fira Sans** (OFL 1.1) — chosen. Commissioned by Mozilla for Firefox OS
  screens, drawn by Carrois with Erik Spiekermann: a humanist sans that was
  deliberately widened, straightened and neutralized for UI use. It matches
  SST point for point: single-storey `g`, double-storey `a`, wide open
  apertures, moderate contrast, understated voice, manual TrueType hinting,
  nine weights including the Medium/SemiBold the shell theme uses, and full
  Latin/Greek/Cyrillic coverage. Same design brief as SST (one face for a
  whole device UI), same genre, open license.

## What ships and where

- Files: `assets/fonts/FiraSans/FiraSans-{Regular,Medium,SemiBold,Bold}.ttf`
  plus `OFL.txt` (upstream: the Mozilla Fira project, via the google/fonts
  repository). All four weights carry the typographic family name
  `Fira Sans`, so they resolve as one family.
- Embedding: `src/SharpEmu.GUI/SharpEmu.GUI.csproj` includes them as
  `AvaloniaResource` under `Assets/Fonts/`.
- Default: `GuiLauncher.BuildAvaloniaApp()` sets
  `FontManagerOptions.DefaultFamilyName = "avares://SharpEmu.GUI/Assets/Fonts#Fira Sans"`,
  and the `Window` style in `App.axaml` uses the same family with
  `Segoe UI, sans-serif` fallbacks for glyphs outside its coverage
  (Arabic, CJK, Korean locales fall back to system fonts).
- The console/log view keeps `Cascadia Mono, Consolas` (system fonts, nothing
  shipped).

## Hard rule

Sony's SST, SSTJpPro, SSTTypewriter, DFHEI5-SONY and PS4Icon fonts are
proprietary. Do not commit them, embed them, or fetch them at runtime — not
even "for testing". Only OFL-1.1 or Apache-2.0 fonts may enter the repository,
and always together with their license file (see `LICENSES/OFL-1.1.txt` and
`REUSE.toml`).
