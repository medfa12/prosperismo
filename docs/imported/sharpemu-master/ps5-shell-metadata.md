<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell managed-metadata survey

This document records values read directly from the managed metadata embedded
in the PS5 4.03 shell `.sprx` wrappers. It is a cross-check of
`ps5-background.md`, `ps5-home-theme.md`, `ps5-home-structure.md`,
`ps5-options-menu-and-focus.md`, `ps5-control-center.md`, `ps5-toasts.md`, and
`ps5-hub-and-cards.md`.

The extraction tool is `tools/shell-metadata`. It carves each embedded managed
PE, loads it through `MetadataLoadContext`, and reads reflection metadata only.
Assembly code is never executed. Carved files and the full JSON result stayed
in an operating-system temporary directory and are not repository artifacts.

## Survey coverage

The selected corpus was:

- 118 `Sce.Vsh.*.dll.sprx` / `Sce.PlayStation.*.dll.sprx` wrappers from
  `system_ex/common_ex/lib`;
- 6 matching wrappers below
  `system_ex/app/NPXS40087/psm/Application`.

All 124 wrappers contained one managed PE and all 124 assemblies were read.
The unfiltered result contained 11,890 types, 2,131 enums, 37,888 literal
fields (including enum members), and 611 safely resolved static-readonly
fields. A separate native `libcairo.sprx` check returned `native-only` and exit
code zero, exercising the non-managed path.

## Contradictions

These are metadata disagreements, not reinterpretations of control flow.

| Existing claim | Exact metadata | Verdict |
|---|---|---|
| `ps5-background.md`: cold boot is `6000 ms (600,000,000 ticks / 100 ns)` | `BackgroundLayer.ColdBootDurationTick = 60,000,000`; at 100 ns per tick this is the documented 6000 ms | **CONTRADICTED:** the duration in milliseconds is right, but the printed tick count has one extra zero |
| `ps5-options-menu-and-focus.md`: `FocusRenderWidget.EdgeFadeMinLength = 6.5` | UI3 `FocusRenderWidget.EdgeFadeMinLength = 10` | **CONTRADICTED:** the exact literal is 10, not 6.5 |

No metadata contradiction was found in the checked claims from
`ps5-home-theme.md`, `ps5-home-structure.md`, `ps5-control-center.md`,
`ps5-toasts.md`, or `ps5-hub-and-cards.md`. This does not validate their
control-flow inferences: metadata proves declarations and values, not how a
method uses them.

## Confirmations

### Background and basemat

The BGLayer metadata independently confirms:

- `WaveColourPreset`: `InitialSetup=0`, `MiniApp=1`, `SystemArea=2`,
  `MusicUnlimited=3`, `HomeScreen=4`, `NoWave=5`, `Black=6`,
  `WhatsNew=7`, `MusicUnlimitedSplash=8`, `Login=9`,
  `LoginNoUserLogined=10`, `Boot=11`, `Store=12`, `PsVideo=13`, and
  `ThemeFlow0..7=14..21`.
- Texture slots: `BackgroundImage0=0`, `BackgroundImage1=1`,
  `TransitionOldImage=2`, `OverlayBackgroundImage=4`,
  `BackgroundBlurImage0=5`, and `BackgroundBlurImage1=6`.
- `LightRenderModeIndex`: `NoParticle=65`,
  `InitialWelcomeNoParticle=66`, `Bottom=67`, `Spread=68`, `ColdBoot=69`,
  `WarmBoot=70`, `InitialBoot=71`, `Black=78`, and `None=79`.
- `GlobalBackgroundState=0..13` with the documented aliases
  `ColdBootAnimation=BootAnimation=2`.
- Transition aliases and holes:
  `Invalid=-1`, `LaunchingGame=0`, `Hide=HideGameSplash=1`,
  `LaunchingGameBC=2`, `SystemDefault=5`,
  `CustomImageRipple=CustomImage=6`, slide-left/right `7/8`, fade `9`,
  and ripple-back `10`. Degrees are `CrossFade=0`, `Subtle=1`,
  `Normal=2`, and `Strong=3`.
- Basemat types `None=0`, `Flat=1`, `Linear=2`, and
  `EllipseWide=EllipseNarrow=3`. PUI's UI3 `FullScreenBasematType` has the
  same `None/Flat/Linear/EllipseNarrow = 0/1/2/3` sequence.
- `VisibleOpacityThreshold=0.0001`,
  `focusMoveThreshold=10`, `FocusCheckTimeout=1,000,000` ticks (100 ms),
  `BasematAnimationDuration=1000` ms,
  `BGTransition.Timeout=100,000,000` ticks (10 s),
  `WarmBootDurationTick=30,000,000` (3 s), and
  `BootAnimTimeout=15,000` ms.

This confirms the enum numbering and named constants in
`ps5-background.md` and the basemat mapping in `ps5-home-structure.md`.
Metadata alone does not prove the documents' state-to-state transitions or
render ordering.

### Focus

UI3 `FocusRenderManager` confirms the documented 3 px stroke, 3 px offset,
0.3 s press/in/out/move durations, 0.25 s warp, 60 Hz
`SecPerFrame=0.01666667`, 0.4 area-rendering threshold, and 1.2 hiding scale.

The focus-style names used by the React Native documents now have exact native
values:

| Focus style | Value |
|---|---:|
| `Rectangle` | 0 |
| `ListItem` | 1 |
| `None` | 2 |
| `Glow` | 3 |
| `Osk` | 4 |
| `Rectangle2` | 5 |
| `RoundedRectangle` | 6 |
| `Ignore` | 7 |
| `Exit` | 8 |

This confirms the member set in `ps5-options-menu-and-focus.md`; only its
previously stated 6.5 edge-fade value is contradicted.

### Theme semantics and typography

The native tag enum is exactly `Bright=0`, `Dark=1`, `Line=2`,
`LineGold=3`, confirming the four semantic tag variants catalogued in
`ps5-home-theme.md`. Metadata does not expose the constructed `UIColor`
payloads, so it does not resolve the focus, accent, success, or per-theme
colors left open by that document.

UI3 `UIFont` static-readonly fields resolve the font tokens used throughout
the other documents:

| Token | Pixels |
|---|---:|
| `Size3XLarge` | 72 |
| `Size2XLarge` | 54 |
| `SizeXLarge` | 45 |
| `SizeLarge` | 36 |
| `SizeNormal` | 30 |
| `SizeSmall` | 27 |
| `SizeXSmall` | 24 |
| `Size2XSmall` | 21 |
| `Size3XSmall` | 18 |
| `Size4XSmall` | 15 |

This fills the explicit font-token gaps in `ps5-control-center.md` and
`ps5-toasts.md`. It also confirms the toast document's use of 24 px when
reasoning about `SizeXSmall`; the JS-derived toast timings and layout behavior
have no conflicting managed constant.

### Control Center and action cards

The managed launcher side confirms that the Control Center is addressed as
`pscontrolcenter:main`, that resume uses
`pscontrolcenter:main?resume=true`, and that the double-tap route targets the
first action card:
`pscontrolcenter:main?target=action-card&acIndex=0`. This is consistent with
the Control Center/action-card handoff described in
`ps5-control-center.md` and `ps5-hub-and-cards.md`.

It also adds exact launcher budgets not present in those documents:
`ResumeTimeout=4000` and `ResumeNativeTimeout=6`. The metadata does not label
the latter's unit, so it should not be converted speculatively.

## New values

The most useful declarations not already captured by the seven documents are:

- UI3 popup geometry:
  `PopupMenuBase.DefaultMaxWidth=1180`;
  `PopupMenuCore.VerticalSafeAreaMargin=48`,
  `HorizontalSafeAreaMargin=48`, `DefaultOffset=16`, `SectionPadding=8`,
  `sectionSpacer=16`, `DefaultAlignmentOffset=8`,
  `DefaultSubMenuOffset=24`, `scrollBarLengthMargin=6`, and
  `scrollBarOffset=3`.
- UI3 separator geometry: `Separator.DefaultHeight=32` and
  `lineHeight=2`. This closes the `RCTSeparator.defaultHeight` gap in
  `ps5-options-menu-and-focus.md`; it also shows why Prosperismo's shipped
  1 px separator was too thin.
- UI3 popup scrolling:
  `DefaultScrollAnimationDuration=0.25` s and
  `focusScrollMarginCoeff=1.5`.
- Numeric collision behavior:
  `None=0`, `Fit=1`, `Flip=2`, `FlipFit=3`.
- Focus fade behavior:
  UI3 `DefaultFadeInTime=0`, `DefaultFadeOutTime=0.2`,
  `DefaultKeyRepeatFadeOutRate=2`; the older UI2 renderer uses rate 3.
- Menu spacing:
  `MenuListItem.siblingMargin=48`, `iconTextMargin=20`;
  `MenuListSelectable.checkBoxTextMargin=16`,
  `checkBoxIconMargin=16`, and selectable icon/text margin 8.
- Theme categories:
  `ThemeColor` has normal colors `Blue=0`, `Pink=1`, `Red=2`, `Navy=3`,
  `DarkGray=4`, `Gold=5`, `LightSteelBlue=6`, `Purple=7`, dual colors
  `16..19`, and particle colors `32..35`. These are native theme categories,
  not recovered RGB values and not the JavaScript selector table.
- Home option-menu storage modes:
  `InternalAndExternal=0`, `M2AndExternal=1`, `Internal=2`, `External=3`,
  `M2=4`, `Other=5`.
- Initial-boot media constants:
  light cue at 8000 ms, BGM cue at 14,721 ms, `TimeoutInit=20,000`,
  `TimeoutEnd=40,000`, and `AfterEndTime=1100`. These name exact player
  thresholds but do not by themselves disprove the separate 30 s state timer
  described in `ps5-background.md`.

## Tool use

The project is intentionally absent from `SharpEmu.slnx` and builds
standalone:

```powershell
$env:DOTNET_ROOT = 'C:\dotnet'
dotnet build tools\shell-metadata\shell-metadata.csproj
dotnet run --project tools\shell-metadata -- `
  C:\path\to\Sce.Vsh.ShellUI.BGLayer.dll.sprx `
  --json --grep 'BackgroundTransition|LightRenderMode'
```

Directory inputs scan `*.sprx`; add `--recursive` when needed. Text is the
default output and `--json` selects structured output. A missing dependency
can prevent reflection from formatting a signature, so the tool falls back to
decoding that signature directly from the assembly metadata. Both paths are
body-free and non-executing.
