<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 reverse-engineering: index and evidence ranking

Start here. There are two dozen `ps5-*.md` documents and they were **not** built from
sources of equal quality. Several were written before better evidence existed and are
now partly wrong. This page says what each one covers, what it was built from, and how
far to trust it.

The single most important thing on this page: **`docs/ps5-figma-layout.md` is traced by
hand off screenshots by a member of the public. It is the weakest evidence in this
corpus and it must never win an argument against a bundle constant.**

For the KytyPS5 transfer, use **`docs/kytyps5-shell-migration-handoff.md`**. It pins the
authoritative branch/commit, retained evidence roots, source-porting map, verification
baseline, remaining work, and local worktree state at the 2026-08-02 handoff.

For the product-wide target and the current implementation ledger, start with
**`docs/ps5-reactive-shell.md`**. This index ranks evidence; the reactive-shell
document explains how layout, focus, settings, background, and boot form one
state-driven system.

## 1. The source tiers

Ranked best to worst. Every document below is tagged with the tier it was built from.

| Tier | Source | Why it ranks there | Where it lives |
|---|---|---|---|
| **1** | **Decrypted React Native bundles** | Sony's own `StyleSheet.create` literals and module constants. The actual numbers the actual shell lays out with. Nothing beats this for layout. | `games/useful rnps/readable_js_3.00/*.js` |
| **2** | **Managed assembly metadata** | Read reflection-only from `.dll.sprx`. Exact constants and exact enum members, and it cannot be argued with. But it proves *declarations*, not how a method uses them, and a name is not a behaviour. | 4.03 and 9.00 dumps |
| **3** | **Native decompilation** | Ghidra over `NPXS40087/eboot.bin` and friends. Strong for the renderer and for anything with no managed or JS surface. Weaker than 1 and 2 because it is a human reading disassembly. | 4.03 dump |
| **3=** | **Console artefacts: crash dumps, update packages** | Self-describing files measured directly. As reliable as tier 1 for what they contain, but they contain narration and structure rather than layout. Added after most of this corpus was written. | `games/coredumps/`, `games/PS5_4.03_reconstructed/PUP_dec/` |
| **3=** | **Direct measurement of shipped media** | Sweeping the real boot movie frame by frame. Ground truth for what the console *looks like*, and it has already refuted two firmware readings. Cannot tell you *why*. | `initial_boot_movie.mp4` in a dump |
| **4** | **A community Figma file** | One person's redraw, traced by eye over 4K captures. Left margins wander between 135 and 218 px on screens that share one inset; two frames are 1915 px wide instead of 1920. Useful for structure and for gaps. **Not a source of truth.** | not committed |

### The rule

Where a tier-1 value and any other value disagree, **tier 1 wins and the other document
gets a correction note**. Corrections are written in place, with the retired claim left
visible. We do not silently rewrite history: knowing that a number was once wrong, and
how it got that way, is what stops it being reintroduced.

## 2. The documents

### 2.1 Layout, the load-bearing set

| Document | Covers | Tier | Trust |
|---|---|---|---|
| **`ps5-rn-layout.md`** | The full shell layout contract: design tokens, home, hub, control centre, library, cards, motion, easing. 248 EXACT rows. | **1** | **Highest. This is the reference.** It is the arbiter for every layout dispute and it carries its own "Corrections to sibling docs" section. If you read one file, read this one. |
| `ps5-ui-gap-analysis.md` | Blunt diff between our shell and the console, ranked by visual impact. | 1 | High. Its 12 numbered gaps are all tier-1 backed. Also carries the "contradicted claims" list and a capture-methodology warning. |
| `ps5-home-structure.md` | Home screen structure, spaces, hub model, tile packing. | 1 | Good, **with two corrections applied in place**: the `TILE_SQUARE_*` mislabel and the `strandContainer` / `STRAND_WIDTH` conflation. See §3. |
| `ps5-control-center.md` | Control centre layout, controls, motion, focus, from NPXS40003. | 1 | Good. Not contradicted by anything found so far. |
| `ps5-hub-and-cards.md` | Game hub, activity cards, grid. | 1 + 2 | Good. |
| `ps5-home-theme.md`, `ps5-shell-theme.md` | Colour and theme values. | 1 + 2 | Good, but the colour system is incomplete by construction: there is no colour theme module in JS, only two literal palettes. |
| `ps5-toasts.md` | Toast and notification taxonomy and geometry. | 1 | Good. |
| `ps5-home-motion.md` | Animation durations, easings, spring configs. | 1 | Good. Superseded in coverage by `ps5-rn-layout.md` §9 but not contradicted by it. |
| **`ps5-figma-layout.md`** | A community Figma board, decoded. | **4** | **Lowest in the corpus.** Seven of its geometry claims are refuted. It keeps value only as a gap-filler for surfaces the bundles do not describe (home content area, media hubs, library chrome, user select) and as an independent eyeball confirmation of 106, 16, 11, two spaces and the 24 px gutter. See §3. |

### 2.2 Background, boot and the native renderer

| Document | Covers | Tier | Trust |
|---|---|---|---|
| **`ps5-reactive-shell.md`** | Canonical product target, reactive state contract, provenance boundaries, and implementation ledger. | synthesis | **Start here for scope and current status; follow its links for evidence.** |
| `ps5-background-native.md` | The particle/wave renderer from Ghidra: curl noise, rendezvous points, ramp tables, plate records. | 3 | Good, and it self-corrects: it records that the 21-entry wave table is **not** the managed `WaveColourPreset` enum. |
| `ps5-boot-animation.md` | The cold boot sequence, its phases, its palettes, and our reproduction of it. | 3 + measured | **High, and unusually honest.** It measures its own failure. See §4. |
| `ps5-background.md` | Basemat, state machine, transition durations. | 2 + 3 | Good, **with one correction applied in place** (the cold-boot tick count). |
| `ps5-shell-overlays.md` | Overlay and modal presentation model from the managed assemblies. | 2 | Good. Note its own caveat: the 9.00 copies are reference assemblies with stripped IL; only 4.03 carries real bodies. |
| `ps5-shell-motion.md` | What the cleartext RCO containers hold. | 2 + 3 | Good, and valuable as a **negative** result: the RCO files carry no visual timelines at all. Read it before mining RCO for motion again. |
| `ps5-options-menu-and-focus.md` | The travelling focus highlight and the native option menu. | 1 + 2 | Good, **with one correction applied in place**: the radius scaling was documented backwards. See §3. |

### 2.3 Containers, packaging and artefacts

| Document | Covers | Tier | Trust |
|---|---|---|---|
| `ps5-rn-bundle-map.md` | Which bundle is which app. **This is where the `RNPSPACK` / `RNPSHEDR` format lives**, now measured across 137 files, with the certificate chain and the crypto boundary. | 1 + 2 + artefacts | High, **with three corrections applied in place**. Key result: the console-side crypto covers `[signature .. EOF]` only, both X.509 certificates are cleartext in every firmware, and the leaf subject is `CN=<application name>`, so the NPXS id to app name map is free at 4.x with no decryption. |
| **`ps5-pup-format.md`** | The PUP update container, what an update actually ships, and the block-table structure. New. | 3 + artefacts | Section 1 (the header) is carried over and **unverified**; sections 2 to 4 were re-measured and are strong. Read the markers. |
| **`ps5-core-dumps.md`** | `prosperocore` crash dumps: LZ4 over ELF `ET_CORE`, 28 Sony note types, and what the TTY notes reveal about the shell. New. | artefacts | High. Everything was parsed directly out of five dumps. |
| `rnps-shell.md` | The on-disk shell asset tree and the runtime loader. | 2 + 3 | Good. |
| `ps5-icons.md`, `ps5-fonts.md` | Icon art sourcing; the SST family and our open substitutes. | mixed | Good. `ps5-fonts.md`'s inventory is now independently confirmed and extended by `ps5-pup-format.md` §2. |

### 2.4 System, ISA and status

| Document | Covers | Tier | Trust |
|---|---|---|---|
| `ps5-shell-metadata.md` | Managed-metadata survey, and the **contradiction ledger** against other docs. | 2 | High for what it asserts. Deliberately narrow: it proves declarations only. |
| `ps5-shell-boot-attempt.md` | Running the console's own vsh modules under the emulator. | 3 + measured | Good, and an honest negative: nothing renders a frame; the wall is the C++ runtime, not the compositor. |
| `ps5-re-understanding.md` | What running a PS5 game on Windows actually needs. | mixed | Broad overview. Predates much of the above; treat specific numbers in it as needing a check against the tier-1 docs. |
| **`ps5-unknowns.md`** | Every open gap in the corpus, in one place, with the owning document and what would close it. New. | n/a | **Read before deriving any number that is not already written down.** |
| `ps5-shader-isa-audit.md`, `prospero-isa-source.md`, `prospero-isa-gaps.md`, `isa-contract-table.md` | Shader ISA. | separate corpus | Governed by Sony's own ISA documents in `games/gpu shit_forzen`, **not** by the AMD RDNA2 table. Out of scope for this index. |

## 3. Corrections applied

Every correction is written into the affected document, next to the claim it replaces,
with the old wording preserved. This table is the roll-up.

| Document | Retired claim | Replacement | Provenance of the correction |
|---|---|---|---|
| `ps5-figma-layout.md` | Resting pitch **116** | **114** | HOME m531:38282-38367, `106 + itemMargin 8` |
| `ps5-figma-layout.md` | Focused tile **181 x 179**, radius **32** | **168 x 168**, radius **25.358490566** | HOME m25:3216, HOME m25:3236 |
| `ps5-figma-layout.md` | Row origin **(186, 131)** | **(172, 126)**, and 172 is the focused tile's pinned left edge, not a static row start | HOME m25:3218, HOME m96:7287 |
| `ps5-figma-layout.md` | System icon pitch **100** | **104**, from `iconContainer { width: 56, marginLeft: 48 }` | HOME m143:10653 |
| `ps5-figma-layout.md` | Icons are 31.3 / 34 / 53 px | all three are 56 x 56 boxes | HOME m143:10653 |
| `ps5-home-structure.md` | `TILE_SQUARE_*` = 370 / 340 / 314 / 360 are content tiles | they are **player and friend tiles**, HOME m98, consumed by `ui-shared-utilities-player-tile/PlayerTileSquare` | `ps5-rn-layout.md` §2.9 |
| `ps5-home-structure.md` | `strandContainer 1500 x 168` and `STRAND_WIDTH 1576` are variants of one viewport | two different viewports sharing a 172 margin: the experience switcher clip (m25) and the content strand (m28) | `ps5-rn-layout.md` §2.3, §3.1 |
| `ps5-options-menu-and-focus.md` | The shell **pre-divides** the radius so the on-screen focused radius is 16 | it **multiplies**. On-screen focused radius is **25.3585**. The invariant is a constant radius-to-side **ratio of 0.150943** | HOME m25:3224, HOME m25:3236 |
| `ps5-background.md` | Cold boot is `6000 ms (600,000,000 ticks)` | `60,000,000` ticks. The millisecond figure was always right; the tick count had an extra zero and contradicted it | `ps5-shell-metadata.md`, `BackgroundLayer.ColdBootDurationTick` |
| `ps5-background-native.md` | (already self-corrected) the 21-entry wave table is `WaveColourPreset` | it is **not**. Index 5 is cyan and 6 is rose, where the managed enum has `NoWave` and `Black`. Separate table, separate setter (`sceShellCoreUtilSetSystemBGWaveColor` at `0xb4365c`) | `ps5-background-native.md` |
| `ps5-boot-animation.md` | (already self-corrected) plate record 21 is the blue plate, record 9 the warm one | **both refuted by measuring the movie.** Record 21 carries 7x too much green; record 9 cannot reach the movie's copper hue at any mix. Wave preset 9 does hold up | `ps5-boot-animation.md` |
| `ps5-rn-bundle-map.md` | `RNPSPACK` `0x10` is "header size 0x280" | it is a **section offset**; `0x0C` is a section count. `rnps-settings.epkg` has two sections and a real second `RNPSHEDR` at `0xFFC400` | survey of all 19 `.epkg` |
| `ps5-rn-bundle-map.md` | `0x64`..`0x70` are version major, minor, patch, build | **UNRESOLVED.** `0x64 = 4` in all 19 packages, so it is not a major version; two different apps share `(0x68,0x6C,0x70)` at different sizes | survey of all 19 `.epkg` |
| `ps5-rn-bundle-map.md` | "an `RNPSHEDR` bundle begins at offset 640" | true **only for `.epkg`**. `RNPSPACK` is an OTA envelope; the 62 installed 4.03 bundles and all 56 3.00 bins begin directly with `RNPSHEDR` at offset 0 | 137-file survey |
| `ps5-core-dumps.md` | (self-corrected on the same pass) the 4.02 action-cards package is "version 4.2.0+45353" | **withdrawn.** A pattern that fitted one sample, tested against the other eighteen and failed | see the row above |

### Two numbers that were never on the console

Our shell shipped an **unfocused-tile opacity of 0.55** and a **focused vertical lift of
-14 px**. Neither exists. The RN `Tile` carries no opacity rule and its transform list is
exactly `[{translateX}, {scale}]` with no `translateY`. **The 106 to 168 size change is
the entire focus affordance.** Documented at length in `ps5-ui-gap-analysis.md` §4 and
§5 and listed as shell defects in `ps5-rn-layout.md` §10 rows 2 and 3. Any document that
implies otherwise is wrong.

The only dimming the console applies is a background mat on the **8th, 9th and 10th**
tiles past the selection, at alpha 0.05 / 0.2 / 0.4. It is a "there is more content past
here" cue, not a focus cue. Spending it on ordinary neighbours destroys the signal.

## 4. Findings worth carrying forward

Two results from measurement rather than extraction, both worth more than the numbers
they produced.

**Concentration beats count.** Our procedural boot renderer matched Sony's *timing*
(worst beat 3 frames out of 6000 ms, whole-run mean hue error 8.7 degrees) and failed
completely on *image*. Mean absolute Laplacian of luminance, normalised by frame mean,
came to **22 %** of the reference through the blue phase and **34 %** through the warm.
Peak p99 was 0.146 against the reference's 0.257, with 33 % of the frame lit against
20 %. The energy was right and its distribution was wrong: **1.7x the area at 0.57x the
peak**, which is a fog where the reference has resolved ribbons. The root cause was
particle count about an order of magnitude short of what the choreography needs. Matching
an average is not matching an image, and no amount of recolouring fixes a structural
deficit. Full write-up in `ps5-boot-animation.md`.

**Measure the measuring tool first.** A capture of our shell appeared to show the UI laid
out 25 % too large. It was not a layout bug; `powershell.exe` 5.1 is DPI-unaware, so on a
125 % display `GetWindowRect` returned virtualised coordinates and the capture bitmap was
allocated at 1/1.25 of the window, cropping the right and bottom edges. A cropped frame
and an oversized UI are pixel-identical over the region that survives. A suspiciously
round error ratio is evidence of a tooling artefact until proven otherwise. Written up in
`ps5-ui-gap-analysis.md`.

## 5. What is still unknown

Consolidated in **`docs/ps5-unknowns.md`**. Read it before deriving a number that is not
in these documents, because the answer is frequently "this was looked for and is not
recoverable from the material we hold", which is a measurement task and not a design
decision.

The headline gaps: the `base_dll` type scale (font pixel sizes, line heights and the
named easing curves all resolve through a native module), the absent `wave0/1.fbxd`,
the still-unexecuted native particle records, an unreconciled focus-ring width conflict
(the RN bundle says 8, the PUI metadata says 3), and which particle pattern, wave preset,
and plate record the steady home screen actually selects. The pattern blobs at
`0xbb0dc4` are no longer wholly undecoded: event structure, resource routing, bank
strides, field names, and several exact values are recorded in
`ps5-background-native.md`. Their full semantics and rendering path remain open.

## 6. House rules for editing these documents

1. **Tables over prose.** Every number keeps its provenance: file, module, line or
   address.
2. **Mark every value.** `EXACT` for a literal that was read, `DERIVED` for arithmetic
   over EXACT values with the derivation shown, `INFERRED` for a structural reading that
   is consistent but unstated, `UNRESOLVED` for a known gap. A value that is none of
   these does not belong in a table; it belongs in `ps5-unknowns.md`.
3. **Never tidy a number.** If a document states something you cannot verify, mark it
   unverified. Do not delete it and do not promote it.
4. **Correct in place, and leave the old claim visible.** The record of having been wrong
   is what stops the error coming back.
5. **Nothing from `games/` is committed.** Short structural quotations as evidence only.
