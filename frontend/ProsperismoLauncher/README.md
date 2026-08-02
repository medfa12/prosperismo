# Prosperismo frontend

Windows-only React Native migration target for Prosperismo. It will replace the
Qt launcher after feature parity and native Windows integration are complete,
while keeping two deliberately separate routes:

- **Desktop** is being converted to own the Qt launcher's practical responsibilities:
  game roots, recursive `eboot.bin` discovery, `param.json` metadata, global and
  per-game settings, patch-plan lookup, and launching the emulator.
- **Big Picture** is currently a scaffold for the recovered console-style
  Home/Settings shell boundary.
  It is not the old SharpEmu desktop UI and it does not expose the Desktop
  settings surface as console Settings.

## Pinned toolchain

- Node.js `>=22.11` (verified with `22.14.0`)
- React `19.2.3`
- React Native `0.84.1`
- React Native Windows `0.84.0`
- TypeScript `5.8.x`

`npm install`, `npm test`, `npm run typecheck`, and `npm run lint` work without
Visual Studio. A native app build additionally needs Visual Studio 18.6 or
newer with the v145 C++ toolset, UWP/Windows App SDK tooling, and a Windows
SDK. RNW 0.84 hardcodes that minimum and its generated project selects v145;
the verified machine has VS Build Tools 2022 17.14/v143, so packaging remains
blocked until the v145 toolchain is installed.

```powershell
npm install
npm test
npm run typecheck
npm run windows
```

RNW 0.84's CLI requires `pwsh.exe` even for project generation. The checked-in
`tools/rnw-powershell-shim.cjs` lets `init-windows` use Windows PowerShell when
PowerShell 7 is absent:

```powershell
$env:NODE_OPTIONS='--require ./tools/rnw-powershell-shim.cjs'
npx react-native init-windows --overwrite --template cpp-app --name Prosperismo --namespace Prosperismo --no-telemetry
```

The generated C++ app is present under `windows/`. The OS adapter described in
`src/native/README.md` remains the explicit build boundary.

## Branding and restricted assets

All visible app, package, and window branding is **Prosperismo**. Package icons
are derived from the user-supplied brand material in `../../assets/branding`.
Sony firmware images, fonts, icons, decompiled bundles, and native assemblies
are research inputs only and must never be copied into this frontend or its
distribution packages.

See `docs/architecture.md` for the port contract and provenance.
