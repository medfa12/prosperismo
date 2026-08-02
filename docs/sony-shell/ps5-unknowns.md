<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# What is still unknown

Every open gap across the PS5 reverse-engineering corpus, in one place.

**Read this before deriving a number that is not already in these documents.** The
answer is very often "this was looked for, and it is not recoverable from the material
we hold". That makes it a **measurement task**, not a design decision. A number invented
to fill one of these rows will be indistinguishable from an extracted one six months
later, which is exactly how `0.55` and `-14` got into our shell.

Each row names the document that owns the detail. Go there for the evidence; this page
is the roll-up, not the record.

## 1. The five that matter most

Ranked by how much is blocked behind them.

### Background image transition remainder

**Status: fade, HOME caller, opaque HOME slide, and opaque native ripple are
closed; optional gradation/alpha branches remain.** The 4.03 native duration formula and linear
`cross_fade_p` program execute directly. HOME RN modules 196/511 prove the
direction mapping and degree `Normal`; the original `slide_in_p` shader is
decoded and its spatial mask, authored parameter record, direction and opaque
texture-coordinate equations execute for title art. The original 657-instruction
`ripple_p` program now validates as SPIR-V and executes with its exact two-image,
two-buffer ABI, focus-derived origin, degree record, and progress curve. The
remaining transition work is the optional gradation/dither clock and transparent-
alpha paths for ripple/slide. Owner:
`ps5-background-native.md`, Transition node section.

### 1.1 The `base_dll` type scale

**Status: UNRESOLVED, and it blocks all typography.**

The React Native bundles name every text size symbolically and never state a pixel
value. The shared library bundle does not resolve them either; it forwards to a native
module:

```
Q5qt7gZH: function(e, t, n) {
    var r = n("3r/tBeou").FontSize;
    n("J3z8EzFS")(r, "FontSize native module is not installed correctly");
```

`games/useful rnps/readable_js_3.00/NPXS40141.base.js`, lines 11362-11364.

| Blocked | Detail |
|---|---|
| Pixel values behind `FontSizePS.Size3XLarge` .. `Size5XSmall` | 11 tokens, names EXACT, values unknown. Only `FontSizePS.Invalid = -1` is a literal. |
| Line heights | Same native source, `FontSize.lineSpacingWithEnhancedFontScale`, BASE:29765 |
| Font family and weights | No `fontFamily` appears anywhere in the home bundle; weights pass through as opaque props |
| Coefficients for `easeInOutPS`, `easeOutBreezePS`, `easeOutBlastPS` | Native curves, referenced by name only. These three drive modal show, control centre card enter, and the shell's shared in/out. |
| Options menu panel geometry | Built by native `OptionsMenuPS` / `RCTOptionsMenu`; JS supplies only the item list and the anchor |

An earlier revision of `ps5-rn-layout.md` claimed `base_dll` was simply missing from the
readable set. **That was wrong** and is corrected there: it is present at
`readable_js_3.00/NPXS40141.base.js`. Having it does not help, because it forwards. The
conclusion survived; the reason changed.

There is one live corroborating clue, from a completely different artefact. Every RN app
manifest logged in a crash dump carries
`enableAccessibility : [textToSpeech,fontEmboldening,fontScaling]`
(`ps5-core-dumps.md` §4.5). Font size is a **runtime-variable, user-controlled**
quantity, which is why it is not a constant anywhere in the bundles. Any fixed ladder we
adopt is a default, not the value.

**What would close it:** dump the native `FontSize` module constants at runtime, or read
them out of the shell native binary. Owner: `ps5-rn-layout.md` §11.

### 1.2 The focus ring width: 8 or 3?

**Status: UNRESOLVED, and the two sources have not been reconciled.** This is the one
place in the corpus where a tier-1 and a tier-2 source disagree and neither has been
retired.

| Source | Tier | Value | Locator |
|---|---|---|---|
| RN control centre bundle | 1 | `FOCUS_WIDTH = 8` | `CC:2640`, `I.FOCUS_WIDTH = 8;` |
| PUI / UI3 managed metadata | 2 | 3.0 px stroke at 3.0 px outside offset, 1.5 px AA, texture `image_focus_frame_2` | `ps5-shell-metadata.md`, `FocusRenderManager` |

Possible reconciliations, **none confirmed**, listed so nobody re-derives them:

1. They measure different things. 8 may be a JS-side *layout reservation* (the space the
   ring is allowed to occupy: 3 px stroke + 3 px offset + antialiasing rounds to 8) while
   3 is the drawn stroke. This is the most economical reading and the arithmetic works,
   but nothing states it.
2. They describe different rings. The control centre and the home strand may not share a
   focus treatment.
3. One is stale relative to the other. The bundle is 3.00; the metadata is 4.03.

**Do not pick one and move on.** If a renderer needs a number today, draw 3 px and
reserve 8, and write down that you did.

**What would close it:** screenshot diffing against a console at a known zoom, or finding
the consumer of `FOCUS_WIDTH` in the bundle and seeing whether it feeds a layout box or a
stroke. Owner: `ps5-options-menu-and-focus.md` §1.8, `ps5-rn-layout.md` §11.

### 1.3 `wave0.fbxd` and `wave1.fbxd`

**Status: RETIRED as a shell-fidelity blocker.** The paths and dormant skinned
`Wave` code are real, and the factory assets remain absent, but shader namespace
and live-owner analysis proves the 4.03 moving PS5 background is the
`BackgroundLayer::` compute particle field over Plane2's full-screen
`wave_bg_p` plate. Plane2's animated noise phase, runtime uniform writer, and
steady Home/Settings record route are now recovered. Do not keep searching
update material for these files and do not block the live background on them.

### 1.4 The nested particle records in the seven pattern blobs

**Status: outer container and selector decoded; nested record schema unresolved.**

The native loader at module vaddr `0x00095920` bounds its selector to 0-6 and
passes three exact seven-entry tables through the wrapper at `0x00099280` and
matcher at `0x000992d0` to the blob parser at `0x00099ac0`:
names at `0xd60e10`, relocated blob pointers at `0xd60e50`, and byte lengths at
`0xbb0d80`. The mapping is:

| selector | embedded name | blob vaddr | file offset | bytes |
|---:|---|---:|---:|---:|
| 0 | `coldboot` | `0xbb0dc0` | `0xbb4dc0` | `0x1faa` |
| 1 | `spread_expanded` | `0xbb2d70` | `0xbb6d70` | `0x1df5` |
| 2 | `spread_expanded_fadeout` | `0xbb4b70` | `0xbb8b70` | `0x276c` |
| 3 | `bottom_camCal` | `0xbb72e0` | `0xbbb2e0` | `0x2856` |
| 4 | `bottom_fadeout` | `0xbb9b40` | `0xbbdb40` | `0x2707` |
| 5 | `initboot_to_spread_no_movie` | `0xbbc250` | `0xbc0250` | `0x2960` |
| 6 | `initboot_to_bottom_no_movie` | `0xbbebb0` | `0xbc2bb0` | `0x3208` |

The `R_X86_64_RELATIVE` entries prove the pointers. Every blob has a packed
length-prefixed ASCII name (length includes NUL), immediately followed by
serialization version `1`; the parser at `0x00099ac0` accepts versions 0 and 1.
`scripts/ps5_particle_patterns.py` reproduces and tests this result against the
audited 4.03 eboot hash.

The next boundary is also settled. Exactly 25 u32 vector cardinalities precede
all element payload. Counts 8-17 allocate ten 0x50-byte record vectors, 18-22
allocate five related 0x50-byte record-vector forms, count 23 allocates 0x30-byte
records, and count 24 allocates 0x98-byte records. `docs/ps5-background-native.md`
records every exact count array. Payload word 25 is `6.5f` for `coldboot`,
`0.1f` for the two fadeouts, and zero for the remaining patterns; its semantic
field name is not yet proven.

`BGLayerNative::BeginBootupSequenceNative` runs `coldboot`,
`spread_expanded`, then `spread_expanded_fadeout`, followed by
`EndWelcomeAnimation`; the renderer is `ParticleBoids`. The first two blobs
each carry one count-24 string record naming
`coldboot/colorchange/colorchange.`.

The exact walk disproves the former claim that 30 and 300 are corresponding
leading acceleration fields. The 30 is `coldboot` field 21 / record 6 /
interpolated assignment 1's start value, targeting byte offset 232
(`+0x1f11`). That field routes to the large-particle draw table at node
`+0x678`, not to the compute simulation, independently ruling out an
acceleration interpretation. Reflection identifies offset 232 as `parMaxSize`;
the paired offset 228 is `parMinSize`, and the event fades them from 30/15 to
zero over seconds 6.5-6.9. The 300 is
`spread_expanded` field 23 / record 0 / fixed tail component 1 (`+0x1dcc`). A
shared 0.1 does not follow both. The desktop renderer has removed that guessed
30/300/0.1 force path. Routed resource events are now sampled byte-exactly, and
the second large-draw bank has a stateful event player with native windowing and
integer truncation. The complete 29,760-byte native `particle_c` ELF section
now decodes to 5,024 instructions and translates to SPIR-V after SharpEmu's
scalar evaluator is given the recovered 64x1x1 launch ABI, the byte-exact
coldboot `large_compute[1]` state and real ID/property buffer descriptors. The
6.0-second spawn-window state (`particleOptions=0x1101`) now executes on an AMD
Vulkan device and writes 40 native 0x44-byte property records, including
positions, velocities, bases and `1.983333/2.0` lifetimes. The older one-based
initialization remains only a visually accepted probe reference. Firmware
callback `0x978e0`, its two-group caller at `0x94ed0`, and the absence of a
coldboot opcode-11 descriptor replacement prove that both large groups bind the
primary zero-based permutation.
The allocator is now recovered from constructor `0x94020`: two zero-based
Fisher-Yates permutations of `0..5999`, generated continuously from the native
xorshift128+ state at `0xD60E88/0xD60E90`. Both coldboot large groups use the
first permutation; the second remains available to the generic resource-event
lookup table. The procedural visualizer's point sets remain ours and are not
credited by this result.

The original `large_particle_vv` and `large_particle_p` firmware ELFs now also
decode and emit validating Vulkan SPIR-V, and the pair creates a graphics
pipeline on the AMD host. The off-screen probe binds the evaluator's exact
SRT/resource/property/ID/corner buffers plus the decoded Particle0/Particle1
GNFs. Geometry-stage diagnostics prove that all 40 submitted t=6.0 triangles
reach the raster pipeline. The earlier degenerate/clear result was closed: the
host read the selector from property `+0x20` rather than the shader's `+0x28`,
while the backend dropped `v_interp_mov_f32` and failed to mark its integer-bit
parameter `Flat`. With both corrected, a continuous t=6.5 compute/draw run
submits 26 billboards and changes 1,449,449 pixels using the original shader
pair and textures.

`ParticleBoids`' own parameters remain unresolved. Both large-particle banks
now execute compute and draw in-process from the shell clock when the raw draw
cache carries their programs/constructor inputs. Cached properties and PNGs are
fallback. The primary-ID binding is now live; the shared property buffer,
including bank0-to-bank1 continuation and rebinding to both draw passes, is now
live too. The coldboot group interval is now live and test-pinned: group 1
starts at native time 6.0, group 0 retires at 13.0, and eligibility uses the
inclusive native end comparison. The managed light-mode dispatcher boundary is
also resolved (Bottom/Spread/ColdBoot/WarmBoot/InitialBoot → raw states
1/2/3/4/6). The eight-bank small-particle execution selected by the steady
states, persistent ping-pong state, and direct resource translation are not
live yet.

The large-particle sampler is now exact (`{0x92,0,0x02500000,0}`: linear
min/mag, nearest/base mip, clamp-to-edge). The former “native target-format
`0x11`” blocker was refuted: that immediate constructs a separate PSM DXT3
surface. The particle pass draws into PSM's RGBA UI context, mapped by the host
probe to `R8G8B8A8_UNORM`.

**What would close it:** preserve the particle allocation and ping-pong counter
continuously across frames, connect every shell event to the recovered numeric
state/pattern routing, host resource/shader evaluation directly rather than
feeding translated snapshots, and capture that full sequence for direct console
comparison.
Owner: `ps5-boot-animation.md`, `ps5-background-native.md`.

### 1.5 Customized wave hue and cold-boot plate selection

**Status: STEADY HOME/SETTINGS RESOLVED; CUSTOM THEME HUE AND COLD BOOT OPEN.**

There are 21 wave presets (table at `0xbd1fd0`) and 37 plate records (table at
`0xbd0ed0`). The native owner's complete 30-entry preset-to-state table and
Plane2's complete 52-entry state-to-record table are recovered. For steady 4.03
Home, managed preset 4 maps through state 5 to record 2; System Area preset 2
maps through state 4 to the same record. High contrast adds 26 to those states,
so both map to record 13. Ordinary per-user theme colours are validated to
`0..6`, while `embedded:DUALCOLOR` packs to `0x10..0x12` and
`embedded:PARTICLE` to `0x20..0x22`. What remains open is how those packed
custom-theme families select their authored wave hue, plus cold-boot plate
selection.

Managed steady NoParticle value `65` is `0x41`, not literal theme `0x01`.
The high-nibble-4 selector branch changes effect control and returns to the
direct Home/System Area states, preserving record 2. Literal `0x01` alone maps
to state 10 / record 6. This distinction is resolved and test-pinned.

What is settled:

| Claim | Verdict |
|---|---|
| The 21-entry table **is** the managed `WaveColourPreset` enum | **Refuted.** Index 5 is cyan and 6 is rose; the enum has `NoWave` and `Black` there. It is a separate *wave colour* table with its own setter, `sceShellCoreUtilSetSystemBGWaveColor` at `0xb4365c`. |
| Wave preset 2 is the cold boot's blue | Holds up against the movie |
| Wave preset 9 is the cold boot's gold | **Holds up.** Its saturated yellow plus an additive mote and a desaturating tonemap lands as the movie's warm white at hue 25. |
| Plate record 21 is the blue plate | **Refuted by measurement.** Record 21 carries green at 0.28 of blue; the movie's plate carries it at 0.041. At the bloom the movie's per-row median is 8-bit `(2, 3, 41)` where record 21 at the same blue would be `(5, 21, 41)`. Seven times the green, far outside 8-bit noise. |
| Plate record 9 is the warm plate | **Refuted by measurement.** Record 9 is a yellow at hue 48 with no blue in any stop or in its light. The movie's warm plate is a copper at hue 21, linear ratio 1 : 0.57 : 0.39. Solving for record 9 plus any neutral fill drives the plate term negative. Its luminance run down the frame is consistent; its hue is not. |

So **the steady shell plate is record 2, while the plate the cold boot actually
selects is still unidentified and may not be in the 37-record table at all.**

**What would close the remaining part:** trace the customized hue and cold-boot
routes, or capture the live sequence with a known theme setting. Owner:
`ps5-boot-animation.md`, `ps5-background-native.md`.

## 2. Everything else, by area

### 2.1 Layout and typography

| Item | Status | Owner |
|---|---|---|
| The full colour system | Only two literal palettes plus a handful of one-offs exist in JS. There is no colour theme module. | `ps5-rn-layout.md` §11 |
| Focus ring colour, alpha and interior fill | Native theme uniforms (`uniform_ThemedFocusColor`, `ThemedFocusColor`, `DefaultFocusColor`), absent from JS. `#00BAFF` appears **nowhere** in any bundle and is a placeholder. | `ps5-options-menu-and-focus.md` §1.8 |
| `focusStyle` enum geometry | `rectangle`, `rectangle2`, `roundedRectangle` known by name only. `listItem` adds 3.0 / 5.0 top and bottom margins. | `ps5-options-menu-and-focus.md` §1.8 |
| `CalcWarpDistortionMatrix` form | Known to be a matrix uniform; affine vs per-corner deformation not recovered | `ps5-options-menu-and-focus.md` §1.8 |
| `NoiseChangeParam`, `ShimmerParam` | **Closed:** exact 4.03 IL, shared absolute UI clock, sine/cosine orbit, two-channel five-second shimmer, Linear + ClampToEdge firmware sampler | `ps5-focus-highlight.md` |
| Settings and control centre row chrome | `SettingsListPS`, `MenuListItemPS`, `ListViewPS`, `TabViewPS` are native | `ps5-rn-layout.md` §11 |
| Blur and backdrop parameters | No blur radius or backdrop constant surfaced in any mined bundle | `ps5-rn-layout.md` §11 |
| 4K layout variants | Only `LOGO_HEIGHT_4K` / `LOGO_WIDTH_4K` hint at a 4K path | `ps5-rn-layout.md` §11 |
| Which mini canvas action cards use | Two conflicting sizes in one bundle: 1116 x 812 and 928 x 810 | `ps5-rn-layout.md` §11 |
| `STACKED.LARGE.LABEL` height, 400 or 408 | Both present in the same preset | `ps5-rn-layout.md` §11 |
| Library grid pitch | 5 tiles across 1576 at margin 20 gives 299.2, not on the tile ladder | `ps5-rn-layout.md` §11 |
| Home focused title visibility rule | `textOpacity<i>` drivers not fully traced | `ps5-rn-layout.md` §11 |
| Whether `strandContainer` clips at 1500 | No `overflow` declared; RN's default differs by platform | `ps5-ui-gap-analysis.md` |
| Whether the console draws any tile shadow | None in the RN styles, but the native focus layer could add one. Our `0 6 16 0 #40000000` is "unsupported by the extract", not "proven wrong". | `ps5-ui-gap-analysis.md` |
| `springOptions` during the startup animation | Written by `setStartupAnimation`; the written value not traced | `ps5-ui-gap-analysis.md` |
| Eight unmined bundles | Explore, Profile, Store, PS Plus, Remote Play, Trophies, Gaming Lounge, Share Play. Readable, just outside the six-surface pass. | `ps5-rn-layout.md` §11 |

### 2.2 Firmware versions

| Item | Status | Owner |
|---|---|---|
| Any 4.02 or 4.03 layout number | The 4.x containers still carry an encrypted `RNPSHEDR` body. Only the 3.00 set is readable. | `ps5-rn-layout.md` §11, `ps5-rn-bundle-map.md` |
| Whether 3.00 geometry still holds in 4.x | Not verifiable without the row above | `ps5-rn-layout.md` §11 |

Note the shape of this gap. It is narrower than it looks:

- The `RNPSHEDR` header **and both X.509 certificates are cleartext in every version**.
  The console-side crypto covers `[signature .. EOF]` only, proven by diffing the 3.00
  encrypted and decrypted pairs: the first differing byte is at `align16(end of leaf
  certificate)` and everything before it is byte-identical.
- The leaf certificate's subject is literally `CN=<application name>`, so **the NPXS id
  to app name map is free at 4.x** with no decryption.
- Only the **payload** is closed. A printable-byte census past offset 4096 gives 0.370
  for the 4.02 and 4.03 payloads, which is 95/256, exactly the uniform random
  expectation.

Open sub-items in the container itself:

| Item | Status | Owner |
|---|---|---|
| Meaning of `RNPSPACK` `0x64`, `0x68`, `0x6C`, `0x70` | **UNRESOLVED.** Previously read as version major/minor/patch/build; refuted, because `0x64 = 4` in all 19 packages and two different apps share `(0x68,0x6C,0x70)` at different sizes. | `ps5-rn-bundle-map.md` |
| The 16 bytes at `RNPSPACK + 0x54` | UNRESOLVED | `ps5-rn-bundle-map.md` |
| The `0x50`-byte blob at `RNPSHEDR + 0x180` | UNRESOLVED. Could be a 64-byte digest plus 16, or a key/IV blob. | `ps5-rn-bundle-map.md` |
| Whether `RNPSHEDR + 0x08 = 2` is a version field | UNRESOLVED. It is 2 in all 137 files across FW 3.00, 4.02 and 4.03, so it cannot be confirmed as a version from this corpus. | `ps5-rn-bundle-map.md` |
| The payload cipher | AES-CBC is the best fit and is **not proven**. ECB and a fixed reused keystream are ruled out by the 3.00 pair corpus. | `ps5-rn-bundle-map.md` |

### 2.3 Containers and artefacts

| Item | Status | Owner |
|---|---|---|
| Every PUP container header field | Carried over from an extraction pass; **no raw `.PUP` is on disk** to re-read | `ps5-pup-format.md` §1 |
| PUP block-offset rule | Two tables need two mutually inconsistent rules. Floor-to-512 fits one exactly and fails the other by 8 bytes. | `ps5-pup-format.md` §3.5 |
| PUP table index base | PS5UPDATE2 holds 7 entries yet its largest table index is 9 | `ps5-pup-format.md` §3.6 |
| Whether the 4 KB zlib window is uniform | Not re-measured. `48 89` and `78 9C` are both valid zlib headers differing only in declared window. | `ps5-pup-format.md` §4 |
| Contents of `common/` inside `preinst` | Directory entry present, no filename entries harvested | `ps5-pup-format.md` §5 |
| 26 of 28 core dump note descriptor layouts | Counted and sized, not parsed. Only the two TTY notes and `MONOVM_LOG` were read. | `ps5-core-dumps.md` §6 |
| `prosperocore-systemcrash.prosperostate` | 11 960 bytes, format not identified | `ps5-core-dumps.md` §6 |
| `COREFILE_INFO`, `SUMMARY_INFO` | No printable strings, layout unknown | `ps5-core-dumps.md` §6 |
| The two app-id spaces in a crash dump | Filename says `0x00000055` for NPXS40087; the log says `appId=0x0000a007` for the same title | `ps5-core-dumps.md` §6 |
| Whether the 28-note set is fixed | All five dumps agree, but all five are the same process on one console | `ps5-core-dumps.md` §6 |

### 2.4 Boot and background

| Item | Status | Owner |
|---|---|---|
| Whether the boot movie and the live sequence share a time base | The linear normalisation used throughout is an **assumption**, not a measurement. It is at least self-consistent: the blue phase is 63 % of the movie and 63 % of the firmware's 6000 ms run. | `ps5-boot-animation.md` |
| The basemat mat shapes | The native renderer owns the geometry; ours are APPROXIMATED | `ps5-background.md` |
| Why the motes turn gold before the plate does | Observed in the reference from about 0.38 of the run; neither implementation models it | `ps5-boot-animation.md` |
| The late-phase violet cast | Reference drifts to hue 322-353; ours stays warm at 24-30. A 50 to 65 degree error at low saturation. | `ps5-boot-animation.md` |

## 3. The reproduction failure, and why it is on this page

Our procedural boot renderer is the clearest demonstration in the corpus of a gap that is
**structural, not chromatic**, and it belongs here because the missing input is §1.4.

Rendered in the console's own palette and measured against the shipped movie:

| Measure | Result |
|---|---|
| Beat timing | every beat within 52 ms of 6000 ms, worst case 3 frames |
| Whole-run mean hue, chroma weighted | within 8.7 degrees |
| Unfitted blue-phase mean luminance | within 22 % rms |
| **Mean absolute Laplacian of luminance, normalised by frame mean** | **22 % of reference through the blue phase, 34 % through the warm** |
| Peak p99 | **0.146** against the reference's **0.257** |
| Fraction of frame lit | **33 %** against the reference's **20 %** |

The clock is right, the exposure is right, and the image is wrong. The same light is
spread over **1.7x the area at 0.57x the peak**. The reference puts its light into a
compact knot of resolved ribbons over a dark frame; ours is a fog. The ribbons, the
rendezvous line and the bokeh discs are all present as an average and absent as features.

Root cause: our emission counts (260 trail heads, 14 000 motes, 96 bokeh discs) are
**ours**, chosen to keep a Python frame under a second, and they are roughly an order of
magnitude short of what the choreography needs. Recolouring a fog produces a coloured
fog, which is why the shipping path recolours the console's own frames instead.

**The lesson, stated plainly because it generalises: concentration beats count.** Matching
an average is not matching an image. Any conformance check that only compares means,
hues and timings will pass a reproduction that looks nothing like the original. Add a
detail metric, and a peak metric, and a lit-area metric, before believing a match.

Owner: `ps5-boot-animation.md`.

## 4. Two things that are known and keep getting re-questioned

Recorded here so the roll-up is not read as "everything is open".

- **The strand focus affordance is size, and nothing else.** No opacity change, no
  vertical lift, no shadow, no z-order change. The transform list is exactly
  `[{translateX}, {scale}]`. `ps5-ui-gap-analysis.md` §4 and §5.
- **The corner radius is a constant ratio, not a constant value and not a
  pre-compensation.** `0.150943` of the side length: 16 at 106, 25.358490566 at 168.
  `ps5-rn-layout.md` §1.5.
