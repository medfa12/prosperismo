<!--
SPDX-FileCopyrightText: 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# KytyPS5 shell migration handoff

Snapshot date: 2026-08-02.

## Authoritative Git state

- Repository: `https://github.com/medfa12/sharpemu.git`
- Authoritative branch: `codex/ps5-shell-integration`
- Last implementation commit before this handoff: `687729af6ee946890d49ba36722e5a91ad7dadb1`
- Local worktree: `C:\sharpemu-integration`
- The branch contains all tracked work from `master`, `feat/ps5-home-shell`,
  `exp/shell-boot`, and `rebrand`. Each of those refs has zero commits not
  reachable from `codex/ps5-shell-integration`.
- Do not merge or cherry-pick those frontier branches again.

The two superseded worktrees were removed after their ignored evidence was
preserved:

| Removed worktree | Contained branch | Preserved ignored evidence |
|---|---|---|
| `C:\sharpemu-home` | `feat/ps5-home-shell` | `C:\sharpemu-ps5-shell-evidence` |
| `C:\sharpemu-shell` | `exp/shell-boot` | `C:\sharpemu-ps5-shell-execution-evidence` |

The branch refs were intentionally retained. Worktree removal did not delete
branches or commits.

## Worktrees that remain

### Shell handoff worktree

`C:\sharpemu-integration` is the clean, authoritative shell worktree. Do the
KytyPS5 extraction from this branch, not from `master`.

### Unrelated active master worktree — preserve

`C:\sharpemu` is still an active, dirty worktree. Its changes are not part of
the shell handoff and must not be reset, cleaned, or overwritten:

Modified:

- `C:\sharpemu\docs\minecraft-bringup.md`
- `C:\sharpemu\src\SharpEmu.GUI\MainWindow.axaml.cs`
- `C:\sharpemu\src\SharpEmu.Libs\Gpu\GuestGpuTypes.cs`
- `C:\sharpemu\src\SharpEmu.Libs\Kernel\KernelMemoryCompatExports.cs`
- `C:\sharpemu\src\SharpEmu.Libs\Pad\PadExports.cs`
- `C:\sharpemu\src\SharpEmu.Libs\VideoOut\VulkanVideoPresenter.cs`
- `C:\sharpemu\src\SharpEmu.ShaderCompiler\Gen5ShaderScalarEvaluator.cs`

Untracked:

- `C:\sharpemu\Program.cs`
- `C:\sharpemu\VP_VULKANINFO_AMD_Radeon_Pro_V620_MxGPU_2_0_317.json`
- `C:\sharpemu\queues\isa-gaps.json`
- `C:\sharpemu\scratch-review-e.txt`
- `C:\sharpemu\scratch-review-o.txt`
- `C:\sharpemu\scripts\nid_resolve.py`
- `C:\sharpemu\scripts\printwindow_capture.ps1`
- `C:\sharpemu\src\SharpEmu.Tests\VulkanGuestSurfaceWriterOrderTests.cs`
- `C:\sharpemu\tmp\`
- `C:\sharpemu\tmp_eboot_syms.txt`
- `C:\sharpemu\tmp_shaderisa_disasm.txt`

## Ground-truth inputs

These inputs are local research material and are not ordinary redistributable
application assets.

### Readable Sony React Native bundles

Root: `C:\sharpemu\games\useful rnps\readable_js_3.00`

- `NPXS40002.js` — Home shell, AppBrowse use, focus graph, hub ownership.
- `NPXS40003.js` — notifications and InAppToast.
- `NPXS40008.js` — Settings application.
- `NPXS40033.js` — Game Hub app and identity encoder.
- `NPXS40141.base.js` — shared RN/PUI components and icon registry.

### Firmware

- Reconstructed 4.03 root:
  `C:\sharpemu\games\PS5_4.03_reconstructed`
- Decrypted 9.00 root:
  `C:\sharpemu\games\PS5_9.00_decrypted`
- Native PUI focus audit:
  `C:\sharpemu-ps5-shell-evidence\pui-focus-audit`
- ReactNative.PUI audit:
  `C:\sharpemu-ps5-shell-evidence\reactnative-pui-audit`

### Preserved rendering and execution evidence

- Visual captures, shader probes, native focus captures, native background
  replay data, and the small-particle draw cache:
  `C:\sharpemu-ps5-shell-evidence`
- Firmware shell-execution traces, carved registry assemblies, heap/queue
  experiments, and long-run logs:
  `C:\sharpemu-ps5-shell-execution-evidence`
- Native small-particle draw cache used by the current preview runtime:
  `C:\sharpemu-ps5-shell-evidence\native-small-spread\draw-cache`

The first evidence root contains 22,658 files / 2,180,394,391 bytes at handoff.
The execution-evidence root contains 84 files / 58,178,428 bytes.

## Documentation map

Start here:

1. `C:\sharpemu-integration\docs\ps5-ui-state-of-work.md`
2. `C:\sharpemu-integration\docs\ps5-reverse-engineering-index.md`
3. `C:\sharpemu-integration\docs\ps5-unknowns.md`
4. `C:\sharpemu-integration\docs\kytyps5-shell-migration-handoff.md`

Home layout, focus, motion, and theme:

- `C:\sharpemu-integration\docs\ps5-rn-layout.md`
- `C:\sharpemu-integration\docs\ps5-home-structure.md`
- `C:\sharpemu-integration\docs\ps5-home-motion.md`
- `C:\sharpemu-integration\docs\ps5-home-theme.md`
- `C:\sharpemu-integration\docs\ps5-focus-highlight.md`
- `C:\sharpemu-integration\docs\ps5-options-menu-and-focus.md`
- `C:\sharpemu-integration\docs\ps5-fidelity-review.md`
- `C:\sharpemu-integration\docs\ps5-ui-gap-analysis.md`
- `C:\sharpemu-integration\docs\ps5-figma-layout.md`

Background and boot:

- `C:\sharpemu-integration\docs\ps5-background.md`
- `C:\sharpemu-integration\docs\ps5-background-native.md`
- `C:\sharpemu-integration\docs\bglayer-background-spec.md`
- `C:\sharpemu-integration\docs\bglayer-shaders.md`
- `C:\sharpemu-integration\docs\ps5-boot-animation.md`
- `C:\sharpemu-integration\docs\ps5-shell-boot-attempt.md`
- `C:\sharpemu-integration\docs\shell-mono-boot-invocation.md`

Shell applications and overlays:

- `C:\sharpemu-integration\docs\ps5-hub-and-cards.md`
- `C:\sharpemu-integration\docs\ps5-settings-integration.md`
- `C:\sharpemu-integration\docs\ps5-control-center.md`
- `C:\sharpemu-integration\docs\ps5-toasts.md`
- `C:\sharpemu-integration\docs\ps5-shell-overlays.md`
- `C:\sharpemu-integration\docs\ps5-reactive-shell.md`

Assets, bundles, metadata, and methodology:

- `C:\sharpemu-integration\docs\ps5-icons.md`
- `C:\sharpemu-integration\docs\ps5-fonts.md`
- `C:\sharpemu-integration\docs\ps5-rn-bundle-map.md`
- `C:\sharpemu-integration\docs\rnps-shell.md`
- `C:\sharpemu-integration\docs\ps5-shell-metadata.md`
- `C:\sharpemu-integration\docs\ps5-shell-theme.md`
- `C:\sharpemu-integration\docs\ps5-shader-isa-audit.md`
- `C:\sharpemu-integration\docs\ps5-re-understanding.md`

## Source map for the KytyPS5 port

### Scene composition and runtime routing

- `src/SharpEmu.GUI/MainWindow.axaml`
- `src/SharpEmu.GUI/MainWindow.axaml.cs`
- `src/SharpEmu.GUI/GameEntry.cs`
- `src/SharpEmu.GUI/GuiSettings.cs`
- `src/SharpEmu.GUI/ShellMotion.cs`

`MainWindow` is integration glue, not a desirable Kyty architecture. Use it to
recover event flow and ownership, then split the Kyty port into shell state,
navigation, rendering, and emulator adapters.

### Mostly portable state/protocol code

These files have the highest clean-room reuse value. They contain constants,
state machines, timings, focus topology, and protocol models with little or no
Avalonia rendering:

- `src/SharpEmu.GUI/Controls/ShellFocusGraph.cs`
- `src/SharpEmu.GUI/Controls/ShellGlance.cs`
- `src/SharpEmu.GUI/Controls/ShellHubModuleContract.cs`
- `src/SharpEmu.GUI/Controls/ShellHubPoolStateMachine.cs`
- `src/SharpEmu.GUI/Controls/ShellHubRemoteProps.cs`
- `src/SharpEmu.GUI/Controls/ShellStartupChoreography.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5AnimationCurve.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5AppBrowseMetadata.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5DesignSpace.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5FontScale.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5HomeMetrics.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5Transitions.cs`

Port behavior and tests first; replace C# records/classes with Kyty's language
and ownership conventions without changing literals or transition ordering.

### Avalonia-specific UI to translate

- `src/SharpEmu.GUI/Controls/ShellNavBand.cs`
- `src/SharpEmu.GUI/Controls/ShellTileRow.cs`
- `src/SharpEmu.GUI/Controls/ShellFocusRing.cs`
- `src/SharpEmu.GUI/Controls/ShellFocusWash.cs`
- `src/SharpEmu.GUI/Controls/ShellSettingsCategoryList.cs`
- `src/SharpEmu.GUI/Controls/ShellSettingsDetailList.cs`
- `src/SharpEmu.GUI/Controls/ShellDialog.cs`
- `src/SharpEmu.GUI/Controls/ShellToast.cs`
- `src/SharpEmu.GUI/Controls/ShellHubViewer.cs`
- `src/SharpEmu.GUI/Controls/ShellAllGames.cs`
- `src/SharpEmu.GUI/Controls/ShellContextMenu.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5IconPresenter.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5VectorIcon.cs`

Do not port Avalonia control structure literally. Preserve scene geometry,
focus ownership, separate focus/wash layers, animation state, and data flow;
implement those against Kyty's own renderer and input system.

### Native background/focus renderer

GUI integration:

- `src/SharpEmu.GUI/SystemAssets/Shell/ShellBackground.cs`
- `src/SharpEmu.GUI/SystemAssets/Shell/Ps5NativeBackgroundLayer.cs`
- `src/SharpEmu.GUI/SystemAssets/Shell/Ps5NativeSmallParticleCacheFrameSource.cs`
- `src/SharpEmu.GUI/SystemAssets/Shell/Ps5NativeWavePlate.cs`
- `src/SharpEmu.GUI/SystemAssets/Shell/ShellLayerBackgroundTransition.cs`

Reusable rendering/runtime logic:

- `src/SharpEmu.Libs/Presentation/Ps5NativeFocusCompiler.cs`
- `src/SharpEmu.Libs/Presentation/Ps5NativeFocusRenderer.cs`
- `src/SharpEmu.Libs/Presentation/Ps5NativeFocusUniforms.cs`
- `src/SharpEmu.Libs/Presentation/Ps5NativeParticleGroupTimeline.cs`
- `src/SharpEmu.Libs/Presentation/Ps5NativeParticleRenderer.cs`
- `src/SharpEmu.Libs/Presentation/Ps5NativeSmallParticleReplay.cs`
- `src/SharpEmu.Libs/Presentation/Ps5ParticleVulkanBackend.cs`
- `src/SharpEmu.Libs/Presentation/Ps5ParticleVulkanSession.cs`
- `src/SharpEmu.Libs/Presentation/VulkanPs5NativeParticleRenderer.cs`

Kyty can reuse the recovered shader contracts, property layout, draw ordering,
timeline, and Vulkan concepts. Replace SharpEmu buffer/device abstractions and
do not assume its presentation ownership model.

### Asset and firmware readers

- `src/SharpEmu.GUI/SystemAssets/RnpsShellAssets.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5HomeSourceBundle.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5IconLibrary.cs`
- `src/SharpEmu.GUI/Ps5Home/Ps5ShellResourcePack.cs`
- `tools/rnps-re/`
- `tools/rco_extract.py`
- `tools/rco_icon_inventory.py`
- `tools/shell-metadata/`

## Behavior closed by Sony ground truth

- Home system icons are 56 x 56 native buttons with a 48 x 48 icon request.
- Native `ButtonBase.borderRadius` is `Height / 2`; the settled system-icon
  background and focus target are circles with radius 28.
- `focusStyle:"rectangle"` in Home m224 belongs only to the avatar `View`, not
  the stock icon-button branch.
- One-colour icon focus interpolation ends at `#333333`.
- Focus inversion uses 0.5 s in after a 0.1 s delay, 0.2 s out, and UI3
  `EaseOutBlast`.
- The travelling focus line, button's white `visibleOnFocus` wash, and glyph
  inversion are separate layers/state.
- AppBrowse identity encoding, normal game-hub URI, four-slot native module
  pool, readiness/show ordering, debounce, reclaim timing, and remote-props
  sequence handling are encoded in production models and tests.
- Sony-mode F10 routes directly between Home and the emulator's own Settings.
  It does not expose console Settings or summon the desktop settings surface.

## Verification baseline

At commit `687729af`:

- `dotnet vstest artifacts\bin\Debug\net10.0\SharpEmu.Libs.Tests.dll`
  passed 4,021 / 4,021 tests.
- `python -m unittest tests.test_ps5_particle_patterns -v`
  passed 11 / 11 tests.
- `dotnet build src\SharpEmu.CLI\SharpEmu.CLI.csproj --no-restore`
  completed with zero warnings and zero errors.
- `dotnet build tools\shell-shot\ShellShot.csproj --no-restore`
  completed with zero warnings and zero errors.

Headless visual harness:

`tools/shell-shot/Program.cs`

Useful scenes include `home`, `navband`, `focus`, `focus-idle`, `settings`,
`settings-detail`, `in-app-toast`, `hub-transition`, `native-background`,
`firmware-home`, and `boot`.

## Preview runtime

Current SharpEmu preview command environment:

```text
SHARPEMU_FW_DUMP=C:\sharpemu\games\PS5_4.03_reconstructed
SHARPEMU_PS5_NATIVE_SMALL_DRAW_CACHE=C:\sharpemu-ps5-shell-evidence\native-small-spread\draw-cache
SHARPEMU_PS5_HUB_PREVIEW=1
C:\sharpemu-integration\artifacts\bin\Debug\net10.0\win-x64\SharpEmu.exe --big-picture
```

`SHARPEMU_PS5_HUB_PREVIEW=1` fabricates readiness only for visual validation.
It is not evidence that NPXS40033 executes.

## Remaining work and honest boundaries

1. There is no actual NPXS40033 execution host yet. Metadata, native-slot pool,
   remote-props protocol, and readiness boundary exist, but real `focusReady`
   still requires a translated/executing guest module.
2. Exact focus renderer stroke/glow/compositing and theme-dependent appearance
   remain partially unresolved even though system-icon geometry/timing/color
   are closed.
3. `ShellDialog` exists but has no production consumer, and controller modal
   routing/suppression is unfinished.
4. Per-game settings still use the standalone `PerGameSettingsDialog` rather
   than the shell-owned modal system.
5. Empty/loading library states still use desktop-looking visuals.
6. `ShellToast` is an evidence-bounded primitive with no invented notification
   producer. Add a host only when a real emulator event exists.
7. The native background replay is state-reactive, but the cache-backed preview
   is evidence infrastructure, not a replacement for general guest shader
   execution.
8. No KytyPS5 checkout was present on this machine at handoff, so no target-side
   files were changed.

## Recommended KytyPS5 migration order

1. Create a Kyty shell module with 1920 x 1080 design-space transforms and port
   the pure metrics/state/protocol tests.
2. Port focus graph and one-owner focus-plane routing before individual views.
3. Port Home switcher, nav band, and card/wash states without the background.
4. Integrate the native background replay behind a renderer-neutral interface.
5. Port the emulator-owned Settings data model into the Sony-layout surface.
6. Add dialogs, prompts, and toast host using real emulator events.
7. Add the Game Hub execution/channel adapter; keep preview readiness disabled
   in production.
8. Run screenshot checkpoints from `shell-shot` and Kyty at the same timestamps
   and diff them before changing constants.

## Legal/clean-room boundary

SharpEmu source is GPL-2.0-or-later. Check KytyPS5 license compatibility before
copying implementation. Sony firmware, decompiled bundles, icons, fonts, RCOs,
and native assemblies are ground-truth inputs supplied locally; do not add them
to a distributable Kyty repository. Prefer porting documented facts, protocols,
geometry, timing, and independently written implementation. Preserve source
provenance in Kyty's documentation.
