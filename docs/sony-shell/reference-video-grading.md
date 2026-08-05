<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Grading the background model against `live_background/default.mp4`

Measured 2026-08-05. This note is **authoritative for the file
`ps5oracle/shell_ui/live_background/default.mp4` and for every number measured
from it.** No other note in this repository may cite that clip as a "genuine
reference capture"; §0 below establishes that it is **not a firmware asset**,
and the other notes have been corrected to say so.

The exercise set out to grade the recovered FirstWave background model against
the clip. What it produced instead is (a) a provenance rejection and (b) a set
of measured presentation values that are **strictly better than the invented
harness constants** [`firstwave-decoded-passes.md`](firstwave-decoded-passes.md)
previously used, and **strictly weaker than anything ISA-recovered**. They
inherit the clip's unestablished provenance. "Replaces the guesses" is the
correct claim; "recovers the constants" is not.

**Two classes of evidence appear in this repository and this note keeps them
apart.**

| Class | Source | What it can settle |
|---|---|---|
| **ISA-recovered** | disassembly of `NPXS40087/eboot.bin` | mechanism, constant offsets, structure, tessellation factors |
| **Reference-measured** | pixel statistics of `default.mp4` | colour, size, density, geometry *of that clip* |

A reference-measured number can never overwrite an ISA-recovered one. It can
only agree with it, disagree with it, or fail to speak to it. Which of those
three happened is stated for every claim below.

---

## 0. Provenance: this clip is not a firmware asset

This has to come first, because it caps everything that follows.

The clip was approached as "the genuine PS5 reference". It is not, and the
repository's own catalogue line for it —
`live_background/ 1 default.mp4, Sony's animated home background`
in [`ps5oracle-README.md`](../../ps5oracle/ps5oracle-README.md) — is an
unsupported claim. Six independent pieces of evidence:

| # | Evidence | Value |
|---|---|---|
| 1 | container `encoder` tag | `https://clipchamp.com` |
| 2 | container `comment` tag | advertising copy for the Clipchamp free online video editor |
| 3 | audio stream | AAC-LC stereo, 44.1 kHz, 192 kbit/s, **audibly non-silent** (mean −37.1 dB, peak −24.0 dB over a 20 s window) |
| 4 | video profile | H.264 **Constrained Baseline**, `has_b_frames=0` — a web-export profile |
| 5 | colour metadata | **absent entirely** (`color_range`, `color_space`, `color_primaries`, `color_transfer` all unset) |
| 6 | loop seam | none: high-pass correlation between the first and last frame is **+0.033**, and no frame in the clip correlates with `t=0` above **+0.006** |

Contrast a video asset that *is* from the firmware —
`PS5_12.40/filesystems/preinst/vsh_asset/initial_boot_movie.mp4`:

| Property | `initial_boot_movie.mp4` (firmware) | `default.mp4` (this clip) |
|---|---|---|
| codec / profile | HEVC Main | H.264 Constrained Baseline |
| resolution / rate | 3840×2160 @ 59.94 | 1920×1080 @ 30 |
| `color_range` / `color_space` | `tv` / `bt709`, tagged | unset |
| audio stream | none | AAC 192 kbit/s, non-silent |
| `compatible_brands` | `isom` | `isomiso2avc1mp41` |
| `encoder` tag | none | `https://clipchamp.com` |
| `creation_time` | `2020-03-12T07:13:40Z` | absent |

And the shell does not play a video background at all. Searching the 12.40
shell eboot (`NPXS40087/eboot.bin`, 21,695,212 bytes) and the 3.20 React Native
image (`rnps_img_3.20/rnps.img`, 759,693,312 bytes):

| Needle | Hits in `eboot.bin` | Hits in `rnps.img` |
|---|---:|---:|
| `live_background` | 0 | 0 |
| `default.mp4` | 0 | 0 |
| `LiveBackground` / `liveBackground` | 0 | — |
| any `.mp4` / `.webm` literal | 0 | — |

**Conclusion.** `default.mp4` is a stock/edited motion-graphics clip that was
placed in the oracle tree and captioned as Sony's background. It is a
*stylistic* reference of unknown authorship, not a capture of the PS5 shell.
The statement already in
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md) — *"no genuine PS5
background capture exists in the local oracle, so there is nothing to diff
against"* — **survives this exercise unchanged, and is reinforced by it.**

Everything in sections 1–7 is therefore an honest measurement of *this clip*.
Section 8 states precisely what that does and does not license.

File pinned for reproducibility:

```
sha256  562aaa99911169ca06ad766c02f33fe4469a6b8dba2e28a6ad3f26a54d42e1b9
size    1,081,027,395 bytes
stream  h264 1920x1080 yuv420p 30/1, 12,847 frames, 428.233 s, 19,996,299 bit/s
```

---

## 1. Method

43 frames sampled 10 s apart (`fps=1/10`), plus two 60-frame consecutive bursts
at `t=100 s` and `t=250 s` for motion, plus a 41-frame 0.5 s-spaced sequence for
particle tracking. Five frames were additionally dumped as raw `yuv420p` planes
so that black level could be read as **code values**, before any range
conversion.

Layer separation throughout: the smooth field is a Gaussian of σ = 24–32 px on
luma; the particle layer is the residual. Luma is Rec.709
`0.2126R + 0.7152G + 0.0722B`. "Additive colour" means pixel minus the
co-located smooth field — the correct quantity for sprites composited additively
over a base, and the only one whose hue is meaningful.

---

## 2. Background colour — the `rgb(2,4,8)` family is the right hue and the wrong level

Raw luma codes, five frames spanning the clip:

| Frame `t` | Y min | Y p0.1 | Y p1 | Y p50 | Y max |
|---|---:|---:|---:|---:|---:|
| 5 s | 15 | 23 | 24 | 46 | 237 |
| 100 s | 15 | 23 | 24 | 47 | 237 |
| 200 s | 14 | 23 | 25 | 47 | 238 |
| 300 s | 14 | 23 | 25 | 47 | 236 |
| 420 s | 15 | 23 | 25 | 48 | 237 |

The Y floor of 14–16 is the limited-range (16–235) black point, i.e. encoder
noise around video black. The **darkest actual content** sits at Y ≈ 23–26,
comfortably above the floor, so it is not clipped and the measurement is real.

Chroma in those darkest regions is consistently **U ≈ 133.9, V ≈ 125.1** —
U above 128 and V below it, i.e. blue-positive and red-negative. The background
is a navy, not a neutral black. This is stable to ±0.1 code across all five
frames.

Converted (BT.709, limited→full; ffmpeg's own PNG conversion agrees to within
1 code):

| Region | Measured RGB |
|---|---|
| darkest 1 % of frame, mean over 43 frames | **rgb(4.0, 8.8, 20.5)** – **rgb(4.8, 9.8, 21.6)** |
| empty top-right corner box (x 1500–1920, y 0–200) | **rgb(6.3, 10.9, 23.0)** ± 0.2 |
| darkest 2 % of the 43-frame smoothed mean | **rgb(4.93, 9.80, 21.92)** |

**Verdict on the docs' assumed `rgb(2,4,8)` basemat: hue confirmed, level
denied.**

| | R:G:B ratio | hue | saturation |
|---|---|---|---|
| assumed `rgb(2,4,8)` | 1 : 2 : 4 | 220.0° | 0.750 |
| measured `rgb(5, 10, 22)` | 1 : 2.0 : 4.4 | **222.4°** | **0.773** |

The chromaticity is essentially identical — a 2.4° hue difference at this
brightness is one code value of chroma, below what the measurement can resolve.
The **level is ~2.5× too dark** (luma 3.86 vs 9.80): the clip's
basemat is `rgb(5,10,22)`, not `rgb(2,4,8)`. If a basemat constant is wanted for
presentation, `rgb(5,10,22)` is the measured one.

Caveat: `rgb(2,4,8)` is not itself an ISA-recovered constant either — it is a
doc assumption. So this is a guess being replaced by a measurement of a clip of
unestablished provenance. It is better than the guess; it is not firmware truth.

---

## 3. Particles

### 3.1 Count and size

533 ± 21 detections per frame (min 495, max 583 across 43 frames), at a
threshold of 6 luma above the local smooth field with a ≥4 px footprint.
22,915 detections total.

Radius is measured as the **full-width-half-maximum** footprint radius,
`sqrt(area_at_half_peak / π)`:

| percentile | p10 | p25 | p50 | p75 | p90 | p99 | max |
|---|---:|---:|---:|---:|---:|---:|---:|
| FWHM radius (px) | 1.13 | 1.26 | 2.03 | 4.41 | 7.23 | 13.54 | 31.17 |

Peak luma increment over the local field:

| percentile | p10 | p50 | p90 | p99 | max |
|---|---:|---:|---:|---:|---:|
| peak increment | 7.2 | 26.6 | 82.6 | 210.2 | 237.7 |

The distribution is **very wide and heavily skewed small**: half the population
is under ~2 px, but the top 1 % reaches 13–31 px. Size and brightness are almost
uncorrelated (Pearson r = 0.087), so large discs are not simply near ones — they
are defocused ones.

### 3.2 Radial profile — a flat defocus disc, not a Gaussian

Averaged over 644 large sprites (FWHM radius 7–12 px), normalised to peak:

| r/R | 0.0–0.4 | 0.5 | 0.6 | 0.7 | 0.8 | 0.9 | 1.0 | 1.1 | 1.2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| intensity | 0.67 | 0.65 | 0.62 | 0.59 | 0.47 | 0.32 | 0.21 | 0.08 | 0.00 |

Flat to ~0.6 R, then a smooth roll-off reaching zero at ~1.2 R. **No bright
rim** — this is a plain circular-aperture defocus disc, not an annular bokeh and
not a Gaussian sprite. A `smoothstep((1.2 − r/R)/0.6)` reproduces it closely.

### 3.3 Colour — gold, with saturation rising with size

Measured on the additive increment, so the navy basemat is excluded. Hue is only
meaningful for sprites large enough to resolve; the sub-2 px population is
near-neutral and its hue is scattered by codec noise.

| FWHM radius | n | mean additive RGB | median hue | median sat | median peak |
|---|---:|---|---:|---:|---:|
| 0.5–1.5 | 8,407 | (33.9, 32.1, 30.5) | 98.9° *(unreliable)* | 0.136 | 21.8 |
| 1.5–2.5 | 4,654 | (29.1, 25.5, 20.7) | 44.4° | 0.275 | 16.7 |
| 2.5–4 | 3,431 | (36.1, 29.5, 20.3) | 36.6° | 0.436 | 29.0 |
| 4–6 | 2,953 | (38.6, 30.0, 18.1) | 35.2° | 0.539 | 31.9 |
| 6–10 | 2,683 | (42.5, 32.1, 18.1) | 34.1° | 0.600 | 33.9 |
| 10–40 | 787 | (41.8, 32.3, 16.6) | 33.9° | 0.630 | 34.0 |

The resolved population converges hard on **hue ≈ 34°** (orange-gold) with
saturation climbing from ~0.28 at 2 px to ~0.63 above 10 px. Normalised to
G = 1, the large sprites are **R:G:B = 1.29 : 1.00 : 0.51**.

Overall hue histogram (all detections with additive V > 3):

| hue band | 0–15° | 15–30° | 30–45° | 45–60° | 60–90° | 90–180° | 180–270° | 270–360° |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| share | 6.2 % | 22.6 % | **28.8 %** | 8.4 % | 5.7 % | 6.9 % | 10.3 % | 11.1 % |

The 15–45° band holds 51.4 % of all detections; the non-warm tail is dominated
by the unreliable sub-2 px population.

The description "warm gold/white" is confirmed, with the refinement that
**white-ness is a size effect, not a second population** — small sprites read
near-neutral because they are unresolved, and every resolved sprite is gold.

### 3.4 Spatial density

Per frame, in tenths of the screen:

| y band | 0–108 | 108–216 | 216–324 | 324–432 | 432–540 | 540–648 | 648–756 | 756–864 | 864–972 | 972–1080 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| count | 46.8 | 23.5 | 8.9 | 21.6 | 81.5 | **144.1** | 110.0 | 55.8 | 33.0 | 7.6 |
| median r | 1.60 | 1.60 | 1.60 | 2.76 | 2.59 | 2.65 | 3.66 | 3.19 | 3.19 | 5.97 |

| x band | 0–192 | 192–384 | 384–576 | 576–768 | 768–960 | 960–1152 | 1152–1344 | 1344–1536 | 1536–1728 | 1728–1920 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| count | 24.4 | **81.1** | 63.8 | 55.3 | 45.4 | 38.3 | 47.8 | **76.8** | 54.0 | 46.0 |
| median r | 5.01 | 1.95 | 2.26 | 2.33 | 2.59 | 2.73 | 2.03 | 2.33 | 3.78 | 4.11 |

Two corrections to the informal description:

- **"denser toward the lower half" is only half right.** Density peaks in a band
  at **y 432–756** (46 % of all particles in 30 % of the height) and then *falls
  off sharply* below y = 864. The bottom tenth is the emptiest band in the
  frame (7.6/frame). The field is a mid-lower **band**, not a bottom-weighted
  gradient.
- Median radius grows monotonically toward the bottom (1.6 px at the top,
  6.0 px at the bottom) and toward both left and right edges — the depth-of-field
  cue is a genuine, measurable gradient.

The horizontal distribution is **bimodal**, with peaks at x ≈ 290 and x ≈ 1440
and a trough at x ≈ 1050.

### 3.5 Motion — no coherent drift

Two independent methods, and they do **not** agree in sign, which is itself the
result.

Phase correlation on the high-pass layer (mean over each burst):

| gap | 1 frame (0.033 s) | 5 frames (0.167 s) | 10 frames (0.333 s) | 30 frames (1.0 s) |
|---|---|---|---|---|
| `t=100` burst, dx, dy (px) | −0.003, −0.008 | +0.004, −0.057 | +0.006, −0.031 | *lock lost* |
| `t=250` burst, dx, dy (px) | +0.002, −0.015 | +0.001, −0.160 | +0.160, −0.544 | *lock lost* |
| correlation peak | 0.65 | 0.27–0.28 | 0.11–0.12 | 0.03–0.04 |

Extrapolated: dy ≈ **−0.24 to −0.96 px/s** (upward). But the correlation peak
collapses from 0.65 to 0.11 within a third of a second, so the field is not
translating coherently — it is being *replaced*.

Nearest-neighbour tracking of 5,597 links between large blobs at 0.5 s spacing:

```
per 0.5 s : dx median +0.059 (mean +0.269, sd 4.219)
            dy median +0.305 (mean +0.264, sd 4.024)
per second: dx +0.118 px/s, dy +0.611 px/s, speed 0.622 px/s
```

Sign of dy is **opposite** to phase correlation, and the per-link scatter
(sd ≈ 4.2 px) is fourteen times the median displacement. The regional field is
incoherent: the mid-left tile moves (−0.63, +1.34) while the mid-right tile
moves (+1.51, +0.44) over the same interval.

**Verdict: there is no measurable global drift.** Per-particle motion is of
order **0.5–1 px/s** (≈ 0.05 % of screen height per second) and spatially
incoherent — consistent with independent per-particle velocities or a
turbulence/curl field, not a uniform scroll. Any direction quoted below
~1 px/s is below the noise floor of both methods and should not be used.

Independently: the particle layer **fully decorrelates within 10 s**
(autocorrelation r = −0.023 at lag 10 s, and flat at −0.014 to −0.027 out to
lag 420 s). Whatever the per-particle lifetime is, it is under 10 s.

---

## 4. The light shaft

Fitted on the 43-frame temporal mean, σ = 10 px smoothing, additive over the
measured basemat.

**Apex** — from the intersection of the two half-maximum edge lines of the
bright core (fitted over y ≤ 440, x < 700):

```
left  edge   x = −0.3055 y + 154.5   (−17.0° from vertical)
right edge   x = +0.5157 y + 535.0   (+27.3°)
ridge        x = +0.3596 y + 242.5   (+19.8°)
apex         (x = 296, y = −463)   — 463 px above the frame top,
                                     at 15.4 % of frame width
```

**Angular profile** — measured as median radius-normalised intensity
`I · (d/600)^1.4` in 4° bins, over d = 460–1000 px, y < 700. θ is measured from
straight-down at the apex, positive toward the right:

| θ | −30° | −22° | −14° | −6° | +2° | +10° | +18° | +26° | +34° | +42° | +50° | +58° |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| I | 14.5 | 34.2 | 55.8 | 83.3 | **87.6** | 86.8 | 74.4 | 62.8 | 49.8 | 31.5 | 21.3 | 17.0 |

Asymmetric-Gaussian fit:

| parameter | value |
|---|---|
| amplitude A | 87.4 |
| axis μ | **+1.30°** from vertical |
| σ left | 16.06° |
| σ right | 28.70° |
| **FWHM** | **52.7°** (18.9° left of axis, 33.8° right) |

The shaft is **markedly asymmetric** — 1.8× broader on its right flank. Its
*intensity* axis (+1.3°, near-vertical) is not the same as its *ridge* line
(+19.8°), because the ridge tracks the brightness maximum of a profile whose
right flank carries most of the energy.

**Falloff.** Along the ridge, intensity above the basemat drops from 128 luma at
y = 0 to 47 at y = 520. Against distance from the apex (465–893 px):

```
I ∝ d^−1.405
```

Close to inverse-square in the near field but distinctly shallower — consistent
with a volumetric shaft rather than a point source.

**Colour** — additive over the basemat, along the ridge:

| y | additive RGB | normalised (G = 1) | hue | sat |
|---|---|---|---:|---:|
| 0 | (149.0, 120.6, 101.7) | 1.235 : 1.00 : 0.843 | 24.0° | 0.317 |
| 120 | (106.5, 85.9, 70.4) | 1.240 : 1.00 : 0.820 | 25.7° | 0.339 |
| 300 | (71.4, 59.6, 46.8) | 1.198 : 1.00 : 0.785 | 31.2° | 0.345 |
| 400 | (61.4, 51.9, 41.4) | 1.182 : 1.00 : 0.797 | 31.7° | 0.326 |

Warm white at **R:G:B ≈ 1.24 : 1.00 : 0.84**, saturation a near-constant ~0.33,
warming slightly with distance (24° → 32°). This is *warmer and less saturated*
than the gold particles (hue 34°, sat 0.6) — the shaft and the sprites are not
the same colour.

---

## 5. The bottom-left glow band

Centred at **y ≈ 940**, Gaussian-ish with σ ≈ 110 px, peaking at **x ≈ 400** and
decaying toward the right:

| x band (at y = 940) | 0–160 | 160–320 | 320–480 | 480–640 | 640–800 | 960–1120 | 1280–1440 | 1760–1920 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| additive luma | 60.6 | 71.2 | **73.9** | 67.2 | 55.7 | 33.2 | 19.4 | 6.8 |

**Its colour is not cyan.** Additive over the basemat:

| y | additive RGB | normalised (G = 1) | hue | sat |
|---|---|---|---:|---:|
| 880 | (60.3, 59.4, 49.6) | 1.015 : 1.00 : 0.835 | 55.0° | 0.177 |
| 940 | (68.1, 68.3, 58.3) | 0.998 : 1.00 : 0.854 | 61.0° | 0.146 |
| 1000 | (61.7, 58.4, 49.8) | 1.055 : 1.00 : 0.852 | 43.7° | 0.193 |

The emitted light is **near-neutral, very slightly warm** (R:G:B ≈ 1.00 : 1.00 :
0.85, saturation 0.15). It only *reads* as pale cyan in the composited frame
because it sits on a navy basemat that lifts the blue channel. Anyone
reproducing this must add a neutral glow over a navy base, not a cyan glow —
adding cyan double-counts the base.

---

## 6. Global temporal statistics

43 samples, 10 s apart:

| statistic | mean | sd | trend over the full 428 s |
|---|---:|---:|---|
| luma mean | 38.440 | 1.355 | −0.24 % |
| luma sd | 22.512 | 1.220 | −0.21 % |
| luma p1 | 9.357 | 0.461 | +0.27 % |
| luma p99 | 104.274 | 6.119 | −0.08 % |
| % pixels > 128 | 0.272 | 0.119 | +1.49 % |

The clip is **statistically stationary**: every global statistic drifts by well
under 2 % across seven minutes, and the per-sample scatter (3.5 % on the mean)
dominates the trend. There is no ramp, no fade, no build. Particle density does
not drift over time (533 ± 21, no trend).

It also **does not loop**: no frame correlates with `t = 0` above +0.006.

---

## 7. Is there a wave/ripple surface? No.

This was the question that motivated the grading, because
[`firstwave-decoded-passes.md`](firstwave-decoded-passes.md) then treated a
tessellated wave mesh as a main visual element. (It no longer does: that note
now labels the wave's prominence an **assumption**, and this exercise is why.)
Four tests, all negative.

**7.1 Where the low-frequency variability lives.** Temporal sd of the σ = 24
smoothed field, as a fraction of the local level, in a 3 × 4 grid:

| | x 0–480 | 480–960 | 960–1440 | 1440–1920 |
|---|---|---|---|---|
| **y 0–360** | 6.9 % | **12.3 %** | 3.6 % | 2.5 % |
| **y 360–720** | 4.0 % | 8.4 % | 10.1 % | 12.4 % |
| **y 720–1080** | 3.2 % | 3.5 % | 7.0 % | 13.4 % |

Variability is concentrated at the **top** (the shaft) and in the dark
low-signal right, where it is percentage noise on a near-zero level. It is
*lowest* (3.2–3.5 %) precisely in the lower-left, where a wave surface would
have to be.

**7.2 The dominant varying mode is the shaft, not a surface.** SVD of the
low-frequency deviation maps: mode 1 carries **59.8 %** of the variance and
peaks at **(x = 408, y = 0)** — the top of the light shaft. Mean pairwise
correlation between deviation maps is **−0.010**, i.e. the smooth field is not
pulsing in a fixed shape either; it is flickering.

**7.3 No horizontal banding.** A wave surface viewed at a grazing angle bands
horizontally, so `|∂/∂y|` should greatly exceed `|∂/∂x|`. Measured on the
temporal mean:

```
mean |∂I/∂y| = 0.1685      mean |∂I/∂x| = 0.1929      ratio y/x = 0.87
```

The field is very slightly *more* structured horizontally than vertically. In
the 60–200 px spectral band, power in horizontal-band orientations exceeds
vertical by only 1.25× — essentially isotropic.

**7.4 No 12×12 mesh signature.** A 12×12 patch across 1920×1080 puts cell
features at 160 px and 90 px. Radial power of the band-passed (20–200 px)
temporal mean over the region a wave would occupy:

| period (px) | 300–600 | 200–300 | 140–200 | 100–140 | 70–100 | 50–70 | 35–50 | 25–35 | 15–25 |
|---|---|---|---|---|---|---|---|---|---|
| power | 1.85e9 | 8.76e8 | 2.31e8 | 1.60e8 | 1.63e8 | 9.16e7 | 4.96e7 | 2.04e7 | 6.47e6 |

Monotonically decreasing, a clean power law. **No peak at 160 px, no peak at
90 px, no peak anywhere.**

**7.5 Direct inspection.** A γ = 0.30 boost of a single frame — which lifts the
basemat to mid-grey and would make a surface of any amplitude obvious — shows
a smooth gradient, the shaft, the glow band and the sprites. Nothing else. There
is no surface, subtle or otherwise.

**Finding: the clip contains no wave or ripple surface at any amplitude this
measurement can detect.** Its visual vocabulary is exactly four elements — navy
basemat, volumetric shaft, neutral bottom glow, gold defocus sprites.

### What this does *not* prove

The clip is not the PS5 shell (§0). Its silence on waves is therefore **not
evidence that the shell has no wave.** The competing evidence is not close:

| Claim | Evidence | Class | Verdict |
|---|---|---|---|
| the mesh is tessellated 12×12 | `fw_flow_*` writes six tessellation factors, every one literally `12.0` | ISA-recovered from the shipping eboot | **stands** |
| the ripple is a bicubic patch displaced by 3D simplex noise driven by `time` | opcode census and constant offsets in `fw_flow_dv` / `fw_flow_h` / `fw_flow_vl` | ISA-recovered | **stands** |
| the ambient background shows no wave | pixel statistics of `default.mp4` | reference-measured, provenance failed | **carries no weight against the above** |

**The ISA evidence wins, and it is not a close call.** A constant `12.0` read
out of a program that the console actually executes cannot be overturned by a
Clipchamp export that the console never loads and whose filename appears nowhere
in the firmware.

The genuine open question is unchanged and unanswered: *how prominent is the
wave in the shipped ambient state?* `waveOpacity` at constant-buffer offset
`0x188` is set by native code at runtime, and its value is still not recovered.
A wave that is mechanically present but driven at low opacity would satisfy both
the ISA evidence and a subdued on-screen appearance — but nothing here measures
that, and this note does not claim it.

---

## 8. Corrected presentation values

`firstwave-decoded-passes.md` states, correctly, that *"colour, contrast,
spatial frequency and the world-space mapping in the render above are
presentation choices of the test harness, not recovered values."* That render
(`render_wave.mjs`, producing `recovered_t0.png`) used a blue/cyan palette
(`base·(0.10, 0.34, 0.85)`, specular `(0.55, 0.72, 0.95)`), an invented spatial
frequency (`FREQ = 0.85` over a `[−3,3] × [−1.7,1.7]` domain), an invented drift
(`DRIFT = 0.2`, `TIMESCALE = 0.12`) and an invented contrast curve
(`base = 0.04 + 0.30·band`). None of those came from anywhere.

The table below replaces each guess with a measured value **where the evidence
supports it**, and says so where it does not.

Every "New" value below is **reference-measured from a clip whose provenance
failed**. None of them is a firmware constant, none may be cited as one, and
any of them can be overturned the moment a genuine capture or a host-side
`.rodata` read appears.

| Presentation value | Old (invented) | New | Class |
|---|---|---|---|
| basemat colour | `(0.04)·(0.10,0.34,0.85)` ≈ `rgb(1,3,9)` | **`rgb(5, 10, 22)`** | measured — non-firmware clip |
| basemat hue | ~225° implied | **222.4°** | measured — non-firmware clip |
| specular / highlight tint | `(0.55, 0.72, 0.95)` — cyan-blue | **shaft `1.24 : 1.00 : 0.84`; sprites `1.29 : 1.00 : 0.51`** — both warm | measured — non-firmware clip |
| global contrast | `base = 0.04 + 0.30·band` | luma mean **38.44**, sd **22.51**, p1 **9.36**, p99 **104.27**, **0.272 %** above 128 | measured — non-firmware clip |
| spatial frequency | `FREQ = 0.85`, arbitrary domain | sprite FWHM radii **p50 2.03 px, p90 7.23 px**; smooth field is a **power-law spectrum with no characteristic scale** | measured — non-firmware clip |
| world mapping | `u ∈ [−3,3]`, `v ∈ [−1.7,1.7]` | not applicable — the clip has no surface to map | **not recovered** |
| drift | `DRIFT = 0.2`, `TIMESCALE = 0.12` | **no coherent drift**; per-particle speed **0.5–1 px/s**, incoherent; layer decorrelates in **< 10 s** | measured — non-firmware clip |
| light direction | `(0.35, −0.5, 0.79)` | apex **(296, −463)**, axis **+1.3°**, FWHM **52.7°**, falloff **d^−1.405** | measured — non-firmware clip |
| wave amplitude | dominant element | **zero in this clip**; the shipped value (`waveOpacity`, offset `0x188`) remains **not recovered** | see §7 |

**Where the ISA has something to say, it outranks this whole table.** The
particle notes are the worked example: `particle_p` carries a **seven-entry
embedded palette** read straight out of the program image
([`particle-draw.md`](particle-draw.md) §*Colour*), and the sprite radial
profile, size lottery and defocus width are all recovered arithmetic. Those are
firmware truth. The hue-34° / flat-disc measurements in §3 above are consistent
with them, which is mildly reassuring and evidentially worth nothing.

### Validation render

[`evidence/render_reference_model.py`](evidence/render_reference_model.py)
builds a frame from *only* the constants in this note — basemat, the asymmetric-Gaussian shaft, the neutral glow band, and 533
sprites drawn from the measured radius, brightness, colour-vs-size and 2-D
density distributions with the measured flat-disc radial profile. Against the
reference:

| statistic | reference | render | error |
|---|---:|---:|---:|
| luma mean | 38.44 | 40.03 | +4.1 % |
| luma p1 | 9.36 | 10.73 | +14.6 % |
| luma p99 | 104.27 | 110.94 | +6.4 % |
| % > 128 | 0.272 | 0.337 | +23.9 % |

Agreeing to a few percent on the bulk statistics from parameters alone is a real
check that the measurements are self-consistent — the parameters were fitted
per-feature and never tuned to match these aggregates.

**Known shortfall.** The rendered shaft is visibly softer than the reference's,
which has a narrow bright core and faint sub-beam striations. The single
asymmetric Gaussian of §4 was fitted to the radius-normalised median over a wide
radial range and under-represents that core. The shaft is **not** a single
Gaussian lobe; a two-component or striated profile is needed and has not been
fitted.

---

## 9. Not recovered

- **What `default.mp4` actually is.** Established only that it is not a firmware
  asset and not referenced by the shell. Its origin, author and licence are
  unknown, and no attempt was made to identify it as a specific stock clip.
- **Why it is in the oracle tree** captioned as Sony's background.
- **The shipped value of `waveOpacity`** (offset `0x188`) — the number that
  would actually settle how prominent the wave is on a real console.
- **Whether the PS5 ambient background resembles this clip at all.** Nothing
  here bears on that in either direction.
- **The shaft's fine structure** — the narrow core and sub-beam striations are
  visible but unfitted (§8).
- **Sprite lifetime and spawn/despawn rule.** Bounded above at 10 s by
  decorrelation; not otherwise measured.
- **Per-particle velocity law.** Established only that motion is ~0.5–1 px/s and
  spatially incoherent; whether it is per-particle random, curl-noise or
  something else is not determined.
- **Colour of sub-2 px sprites.** Below the resolution at which hue survives
  chroma subsampling and codec noise; the near-neutral reading for that
  population is an artifact and is not evidence of a second, white population.
