# SharpEmu shell surface translation map

This map tracks the React Native translation of the firmware-backed Avalonia
shell at `C:\sharpemu-home\src\SharpEmu.GUI`. Measurements remain in the
1920x1080 authored coordinate space. Product content is Prosperismo content;
console Settings content is deliberately not copied.

## Implemented contracts and render containers

| SharpEmu source | React Native destination | State |
|---|---|---|
| `Controls/ShellLibraryGrid.cs` | `src/bigPicture/shellSurfaces.ts` | Exact 5x3 grid geometry, padding, scroll, focus movement, sort order, tile/empty metrics |
| `Controls/ShellAllGames.cs` | `src/bigPicture/ShellLibrarySurface.tsx` | Installed-game grid, cover/fallback, focus-only metadata, sort panel, empty action, keyboard/gamepad navigation |
| `Controls/ShellSettingsMetrics.cs` | `src/bigPicture/shellSurfaces.ts` | Exact NPXS40008 list, tabs, panel and furniture metrics |
| `Controls/ShellSettingsCategoryList.cs` | `src/bigPicture/ProsperismoSettingsSurface.tsx` | 102-unit category pitch and 304/186/1312/894 list frame with Prosperismo categories |
| `Controls/ShellSettingsDetailList.cs` | `src/bigPicture/ProsperismoSettingsSurface.tsx` | Vertical tab column and emulator-owned detail panel; left/right changes focus ownership |
| `Controls/ShellContextMenu.cs` | `src/bigPicture/ShellUtilitySurfaces.tsx` | 652-784 panel, 98 rows, icon gutter, headers/separators, one focus rectangle |
| `Controls/ShellDialog.cs` | `src/bigPicture/ShellUtilitySurfaces.tsx` | Popup/full-screen body stack, modal scrim, button geometry and affirmative-last initial focus |
| `Controls/ShellUtilityStrip.cs` | `src/bigPicture/ShellUtilitySurfaces.tsx` | 56 marks on 104 pitch, 416 cap, focus-only 336-wide label |
| `Controls/ShellFunctionPanel.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Fixed 1188/126 anchor, header, bounded panel and action rows |
| `Controls/ShellFunctionRow.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Resting/focused card sizes, margins, radius scaling and caption container |
| `Controls/ShellHubNavMetrics.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Horizontal and vertical nav frame variants |
| `Controls/ShellHubViewer.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Hub header, icon/title/tag furniture and body host |
| `Controls/ShellSceneList.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Vertically stacked scenes with horizontal tile rows and independent scene/item focus |
| `Controls/ShellMarqueeCycle.cs`, `ShellMarqueeText.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | 2s dwell, 60px/s reference velocity, fade-out/snap/fade-in cycle |
| `Controls/ShellSpaceHost.cs` | `src/bigPicture/ShellRecoveredContainers.tsx` | Immediate space replacement with arrival-only spring fade; no invented slide |
| `MainWindow.axaml.cs` route transition | `ShellSurfaceTransition` in `ShellUtilitySurfaces.tsx` | 300ms whole-scene transition container |

The reusable arithmetic is covered by
`frontend/ProsperismoLauncher/__tests__/shellSurfaces.test.ts`.

## Integration ownership

`BigPictureShell.tsx` now mounts the recovered Home renderer for every Home
state, including when Search, Profile, Options, or an error dialog is open.
The former modal-only fallback to the legacy Home renderer has been removed.
All Games, Settings, context-menu and dialog routes use the translated
containers above, with the persistent native background mounted below them.

Exactly one Home focus region renders the travelling focus line. The installed
strand and terminal Library shortcut are distinct named nodes, so controller
navigation reaches the shortcut without pretending it is an installed game.
Route code clears Home ownership before mounting All Games or Settings.

All route-local targets now use `ShellFocusOverlay`, the same translated UI3
timeline as Home (show delay, travel driver, exterior distance-field line,
area wash, and inherited radius). No Search, Library, Settings, context-menu,
profile, or dialog route has an independent static white focus border.

## Deliberately deferred

- `GameSurfaceHost`, `VulkanHostSurface`, process-session popups and launch
  teardown are emulator/native-host concerns, not shell presentation. Their RN
  bridge remains a separate host integration.
- Per-game settings data and desktop Options editor are not copied into the
  console surface. The Sony-shaped Settings route exposes Prosperismo settings
  only.
- Firmware icons and background resource lookup are wired by the asset and
  background lanes against `C:\prosperismo\ps5oracle`; this surface slice does
  not read, copy or modify the oracle.
- Hub application payloads are guest/app-owned in the original bundles. The RN
  `ShellSceneList` accepts Prosperismo scene data rather than fabricating Sony
  store, news, account, notification or console-settings content.
