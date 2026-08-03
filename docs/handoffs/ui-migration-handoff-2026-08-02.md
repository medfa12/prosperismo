# Prosperismo React Native UI migration handoff

Date: 2026-08-02

> Historical snapshot. The frontend changes described here were imported into
> `C:\prosperismo-ui` on branch `codex/react-native-shell` so concurrent native
> emulator work can continue on `main`. The active Big Picture state and
> validation gate are documented in `docs/sony-shell/react-native-shell-migration.md`.

## Repository state

- Repository: `C:\prosperismo`
- Branch: `main`
- Base commit: `a83ac2d389f900bde327b656dd4a78e54b6dbc01`
- Remote: none configured
- State at snapshot: intentionally dirty; the frontend migration described
  below had not yet been committed.
- Do not reset or overwrite the concurrent HLE changes under `src/libs/` and `tests/HleContractTests.cpp`.

## Product boundary

The product UI is React Native Windows. The old Qt launcher is only a temporary behavioral oracle. The migration is complete only after the React Native Desktop route and immersive route are verified and the Qt launcher is absent from shipped artifacts. The native emulator remains C++.

## Frontend stack

- React `19.2.3`
- React Native `0.83.4`
- React Native Windows `0.83.2`
- TypeScript `5.8`
- Jest `29`
- Metro `0.83.7`
- Native Windows host: C++/WinRT attributed module named `ProsperismoHost`
- Build toolset: Visual Studio Build Tools 2022 `17.14`, MSVC `v143`, Windows SDK `10.0.26100`

RNW was deliberately moved from 0.84 to the supported 0.83 line because the installed host has VS2022/v143. RNW 0.84 generated a v145 project and its CLI required VS2026.

## Implemented UI behavior

`frontend/ProsperismoLauncher/App.tsx` now provides the normal desktop launcher and routes into `src/bigPicture/BigPictureShell.tsx`. Both routes share the same library, settings and process state.

Desktop parity implemented:

- Recursive `eboot.bin` library scan with symlink avoidance and canonical-path deduplication
- Separate `sce_sys/icon0.png` tile art and `sce_sys/pic0.png` background art
- Search and sortable library columns
- Global and per-game settings, override creation and clearing
- Local compatibility status/comments
- Remote compatibility refresh with the Qt endpoint, initial request plus three retries, schema-safe parsing and local-edit-wins merge
- Patch-plan parsing, persistent selection and atomic materialization through the host
- Bounded UCP trophy parser and localized metadata display
- Open-game-folder action
- Exact-path, explicitly confirmed save-data removal
- Tracked emulator process lifecycle and failure reporting
- Prosperismo branding and supplied icon variants

Immersive shell implemented:

- React Native, not C#/Avalonia or Qt
- Sony-derived 1920x1080 layout metrics, 126px system band, 172px strand origin, 106/168px tile states, 8px packing margin and captured spring constants
- Games/Media spaces, game strand, library, settings and desktop return route
- Shared game launch and options behavior
- Responsive scale from the reference 1920x1080 canvas

The exact layout/state contracts live in:

- `frontend/ProsperismoLauncher/src/bigPicture/shellMetrics.ts`
- `frontend/ProsperismoLauncher/src/bigPicture/shellState.ts`
- `frontend/ProsperismoLauncher/src/bigPicture/BigPictureShell.tsx`
- `docs/sony-shell/`

## Native Windows bridge

The module is implemented and registered in:

- `frontend/ProsperismoLauncher/windows/Prosperismo/ProsperismoHost.{h,cpp}`
- `frontend/ProsperismoLauncher/windows/Prosperismo/ProsperismoHostSupport.{h,cpp}`
- `frontend/ProsperismoLauncher/src/native/ProsperismoHost.ts`

Capabilities:

- Directory enumeration with file/directory/symlink provenance
- UTF-8 text and binary reads
- Atomic UTF-8 text writes
- Canonicalization and existence queries
- Native multi-folder picker
- Launcher settings under `%LOCALAPPDATA%\Prosperismo`
- Emulator discovery and correctly quoted supervised launch
- Explorer open
- Process events
- Save-data removal guarded by exact `_SaveData/<title-id>` shape, confirmation and target/parent reparse-point rejection

Standalone host tests compile under `/W4 /WX` and pass.

## Startup root cause and fix

The first RNW executable compiled but fast-failed in `ucrtbase.dll` at `ReactNativeWin32App.AppWindow()`. The root cause was not the native module.

The project had `WindowsPackageType` empty. WinAppSDK therefore did not compile its bootstrap initializer; `WindowsAppSdkAutoInitialize=true` alone was a no-op for an unpackaged process. The project now sets:

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSdkBootstrapInitialize>true</WindowsAppSdkBootstrapInitialize>
<WindowsAppSdkAutoInitialize>true</WindowsAppSdkAutoInitialize>
```

The verified build compiles `MddBootstrapAutoInitializer.cpp` and `WindowsAppRuntimeAutoInitializer.cpp`, crosses `AppWindow()`, registers `ProsperismoHost`, enters the React event loop and displays a responsive window.

Initial window sizing uses `AppWindow.ResizeClient({1600, 900})`. At the host's 125% scale the external window probe reports 1280x720 logical pixels, as expected.

Current verified executable:

`C:\prosperismo\frontend\ProsperismoLauncher\windows\x64\Release\Prosperismo.exe`

At handoff time exact PID `20900` was running, responsive and titled `Prosperismo`. Stop only that PID if it is still the same process and the user no longer needs it visible.

## Verification already completed

From `frontend/ProsperismoLauncher`:

```powershell
npm test
npm run typecheck
npm run lint
npm run windows:bundle
```

Results:

- Jest: 11 suites, 25 tests passed
- TypeScript: clean
- ESLint: clean
- Production bundle: passed, 13 assets copied
- Offline Release native build: passed
- Runtime: visible responding React Native window with `ProsperismoHost` enabled

The shell state test includes the firmware-derived strand packing control: selected tile x=31, previous=-122, next=184.

## Correct native build invocation

RNW 0.83.2's public CLI health check still hardcodes a VS18.6 minimum even though this project and RNW property sheets build under v143. Use direct MSBuild and the repository shim:

```powershell
$env:NODE_OPTIONS='--require=C:\prosperismo\frontend\ProsperismoLauncher\tools\rnw-powershell-shim.cjs'
$msbuild='C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe'
& $msbuild windows\Prosperismo.sln /restore /m:1 /t:Prosperismo /p:Configuration=Release /p:Platform=x64 /p:WindowsTargetPlatformVersion=10.0.26100.0 /p:TargetPlatformVersion=10.0.26100.0 /v:minimal
```

Do not run concurrent Metro/MSBuild bundle writers against `windows/Prosperismo/Bundle`; one earlier overlapping build produced a transient file-open warning.

## Remaining UI gates

1. Visually capture the corrected 1600x900 Desktop route with `PrintWindow(..., PW_RENDERFULLCONTENT)`.
2. Enter Big Picture and visually capture the immersive route. Computer Use could not enumerate this raw RNW AppWindow in the current session, so do not claim this visual gate from process status alone.
3. Exercise folder selection, settings persistence, patch write, trophy read, Explorer open and exact confirmed save removal against the live native module.
4. Decide whether to ship unpackaged output or install DesktopBridge/UWP components and produce MSIX. The unpackaged executable is proven; the `.wapproj` is not built because DesktopBridge targets are absent.
5. Build the native emulator with the legacy Qt launcher option off, create one clean final artifact containing the RN launcher plus `prosperismo_emulator.exe`, then remove old Qt binaries from `artifacts/`.
6. Update `docs/migration-status.md`, `docs/frontend-architecture.md` and the frontend README from “in progress” only after the visual and artifact gates pass.
7. Review `npm audit`: current dependency tree reports 7 moderate and 4 high transitive findings. Do not use `npm audit fix --force` blindly.
8. Commit in logical chunks: frontend/core, native host/startup, native HLE imports, docs/artifact cleanup. There is no remote configured, so push requires adding the intended remote first.

## Dirty-file ownership

Frontend migration owns all modified/untracked paths under `frontend/ProsperismoLauncher/`.

The concurrent Sony/HLE audit owns:

- `src/libs/libKernel.cpp`
- `src/libs/libSaveData.cpp`
- `src/libs/libTextToSpeech2.cpp`
- `src/libs/saveDataMountSlots.h`
- `src/libs/saveDataCapacity.h`
- `src/libs/textToSpeech2.h`
- `src/libs/writeThrottling.h`
- `tests/HleContractTests.cpp`

Document preservation is importing complete SharpEmu and former sharpemu-home snapshots under `docs/imported/`; do not discard that work or retain its temporary extraction directory.
