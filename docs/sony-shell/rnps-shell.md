<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 System Shell (rnps) Assets

The PS5 system software UI ("the shell") is a set of
[React Native](https://reactnative.dev/) applications. Sony's fork is called
**rnps** (React Native PlayStation). Each screen the console shows — the home
screen, control center, settings, the store, trophies, dialogs — is a separate
RN app shipped inside the firmware.

SharpEmu can *optionally* surface these assets when the user has a decrypted
firmware dump on disk. Everything under the dump is Sony proprietary content:
it is only ever read from the user's own disk at runtime and is **never**
redistributed with the emulator. Nothing from a dump is copied into this
repository.

The runtime loader is `SharpEmu.GUI.SystemAssets.RnpsShellAssets`; the offline
catalog tool is `scripts/rnps_catalog.py`.

## Directory layout

The shell lives under `filesystems/system_ex/rnps/` in a dump:

```
filesystems/system_ex/rnps/
  apps/
    NPXS40002/                     one directory per system app (title id)
      manifest.json                app metadata (plain JSON)
      application.ps.bundle         signed RN bundle (see below)
      license.txt                   OSS license text bundled with the app
      appdb/                        optional: shell tiles this app registers
        default/
          icon0.png                 512x512 icon
          param.json                localized title names + hub uri
        NPXS40053/                  a "pseudo app" tile (e.g. TV & Video)
          icon0.png
          param.json
      assets/                       optional: extracted PNG/JPG UI art
    NPXS40004/
      manifest.json
      main.jsbundle                 dialogs use main.jsbundle instead
      ...
  bgs/                             background service (note: trailing space
    NPXS40052/                     in the directory name in some dumps)
      manifest.json                 applicationName "ppr-bgs"
      main.jsbundle
      license.txt
```

Some `apps/NPXS400xx` directories are empty stubs (no manifest, no bundle) —
in the 4.03 dump these are `NPXS40059`, `NPXS40169`, `NPXS40185`. The loader
and the catalog script both skip them.

A separate sibling tree, `filesystems/system_ex/vsh_asset/`, holds the shared
non-RN media (hub backgrounds, boot sounds, PUI resource archives) described
under [Usable assets](#usable-assets) below.

## `manifest.json` schema

Plain UTF-8 JSON, one object. Observed keys across the 4.03 dump (frequency out
of 61 app manifests that have one):

| Key | Freq | Example | Notes |
| --- | --- | --- | --- |
| `applicationName` | 61 | `"rnps-home"` | Internal app name; stable identifier. |
| `applicationVersion` | 61 | `"4.1.0+12349"` | SemVer + build metadata (`+build`). |
| `titleId` | 61 | `"NPXS40002"` | Matches the directory name. |
| `commitHash` | 61 | `"1480d58e…"` | Source commit; not useful to us. |
| `reactNativePlaystationVersion` | 61 | `"0.59.6-683.1"` | rnps engine version (RN 0.59.6 base). |
| `applicationData` | 60 | `{ "branchType": "release" }` | Free-form object. |
| `repositoryUrl` | 56 | `https://github.sie.sony.com/…` | Internal Sony repo; ignore. |
| `twinTurbo` | 27 | `true` | rnps performance flag. |
| `bootAnimation` | 7 | `"default"` | Home/hubs only. |
| `enableAccessibility` | 5 | `["textToSpeech", …]` | Array of feature strings. |
| `enableHttpCache` | 3 | `true` | |
| `networkingBackend` | 1 | | Rare. |
| `updateType` | bgs | `"bgsservice"` | Only on the bgs manifest. |

The loader reads `applicationName`, `applicationVersion`, `titleId`, and
`reactNativePlaystationVersion`; the catalog script also records `bootAnimation`
and `twinTurbo`. Unknown keys are ignored, so newer firmware that adds keys
still parses.

### `appdb/*/param.json`

Where an app registers shell tiles, each `appdb/<id>/param.json` is a standard
PS `param.json` (same shape as a game's `sce_sys/param.json`): `titleId`,
`contentId`, `displayLocation`, an optional `hubAppUri`, and a
`localizedParameters` object mapping locale -> `{ "titleName": "…" }` with a
`defaultLanguage`. This is where human-readable names like "TV & Video" come
from; the RN app directories themselves only carry the internal
`applicationName`.

## Bundle format (`application.ps.bundle` / `main.jsbundle`)

The bundles are **not** raw JS or bare Hermes bytecode. They are a signed Sony
container. Header bytes (little-endian):

```
offset 0x00  8  magic  "RNPSHEDR"  (52 4E 50 53 48 45 44 52)
offset 0x08  4  container version   = 2
offset 0x0C  …  section offset/size table
```

Immediately after the header table the file contains an X.509 certificate
chain (DER; `30 82 …` SEQUENCE, subject `US / California / SIE LLC`), i.e. the
bundle is signed and its payload is encrypted/opaque. The actual RN program
(JS or Hermes) is inside the signed container and is not readable without
Sony's keys, so **SharpEmu treats bundles as opaque blobs**: it records the path
and size for provenance but does not attempt to execute or decompile them.
`main.jsbundle` (used by dialogs) and `application.ps.bundle` (used by
full-screen apps) share the same `RNPSHEDR` v2 container; the file name is the
only difference. Bundle sizes range from ~5 KB to ~7.4 MB.

## App catalog

Title id -> `applicationName` for the 4.03 dump (61 apps + 1 background
service; empty stubs omitted). Regenerate with
`python scripts/rnps_catalog.py <dump-root>`.

| Title id | applicationName | Title id | applicationName |
| --- | --- | --- | --- |
| NPXS40002 | rnps-home | NPXS40051 | rnps-web-launcher |
| NPXS40003 | rnps-control-center | NPXS40062 | universal-checkout |
| NPXS40004 | rnps-player-selection-dialog | NPXS40063 | rnps-explore-hub |
| NPXS40005 | rnps-game-custom-data-dialog | NPXS40064 | rnps-x-wing |
| NPXS40006 | rnps-invitation-dialog | NPXS40066 | rnps-disc-player |
| NPXS40007 | rnps-uam-fs | NPXS40070 | rnps-system-message-client |
| NPXS40008 | rnps-settings | NPXS40071 | rnps-library |
| NPXS40009 | millennium-falcon | NPXS40072 | rnps-cosmiccube |
| NPXS40011 | rnps-notification-overlay | NPXS40075 | rnps-media-gallery |
| NPXS40013 | rnps-profile | NPXS40080 | rnps-share-play |
| NPXS40015 | rnps-search | NPXS40081 | rnps-millenniumfalcon-dialog |
| NPXS40016 | monte-carlo | NPXS40089 | usbstoragedialog |
| NPXS40017 | rnps-content-information | NPXS40097 | rnps-game-hub-preview-launcher |
| NPXS40018 | gaming-lounge | NPXS40103 | rnps-netctlap-dialog |
| NPXS40021 | reactSystemModalDialog | NPXS40107 | rnps-g2p-dialog |
| NPXS40024 | rnps-capture-menu | NPXS40108 | rnps-player-review |
| NPXS40025 | rnps-trophy | NPXS40110 | rnps-disc-player-hub |
| NPXS40026 | rnps-lfps-bc | NPXS40138 | npparty-compatibility-app |
| NPXS40027 | igc-browse | NPXS40141 | apennine |
| NPXS40029 | rnps-playgo-dialog | NPXS40144 | rnps-unsupported-title-hub |
| NPXS40031 | rnps-bgft | NPXS40145 | rnps-compilation-disc-hub |
| NPXS40032 | rnps-service-hub-psnow | NPXS40147 | rnps-legal-docs |
| NPXS40033 | rnps-game-hub | NPXS40154 | rnps-remoteplay-hub |
| NPXS40034 | rnps-message-dialog | NPXS40161 | rnps-screen-share |
| NPXS40035 | rnps-savedata-dialog | NPXS40163 | rnps-onboard-download |
| NPXS40036 | rnps-action-cards | NPXS40167 | rnps-wishlist |
| NPXS40037 | rnps-service-hub-psplus | NPXS40182 | rnps-vr-onbarding |
| NPXS40040 | rnps-app-installer | | |
| NPXS40041 | titlestore-preview | | |
| NPXS40043 | rnps-psnow-player | | |
| NPXS40044 | rnps-broadcast | | |
| NPXS40046 | rnps-profile-dialog | | |
| NPXS40047 | elysion | | |
| NPXS40048 | rnps-agent-popupgui | | |
| **bgs** NPXS40052 | ppr-bgs | | |

Some `applicationName`s are internal project codenames rather than user-facing
labels — `millennium-falcon`/`monte-carlo`/`elysion`/`apennine`/`rnps-x-wing`.
The user-facing names for the shell tiles those apps host come from the
`appdb/*/param.json` `localizedParameters` instead (see above).

## Usable assets

### `appdb` shell icons

12 rnps apps register `appdb/<id>/icon0.png` tiles (512x512 PNG). These are
directly loadable PNGs — the most immediately usable art in the tree — and each
has a sibling `param.json` giving the localized tile name. `RnpsShellAssets.
EnumerateShellIcons` returns these as `RnpsShellIcon` records (host title id,
represented title id, icon path, param path). Hosts with an `appdb`:
NPXS40008, NPXS40016, NPXS40032, NPXS40037, NPXS40041, NPXS40047, NPXS40063,
NPXS40071, NPXS40075, NPXS40097, NPXS40144, NPXS40145.

### `apps/*/assets` PNG/JPG art

23 apps carry an extracted `assets/` tree; across the whole rnps tree there are
~646 `.png` and ~19 `.jpg` files (onboarding illustrations, payment-method
icons, rating badges, keyguide art, etc.). These are ordinary
PNG/JPEG and load without special handling. They are addressed by file path via
`RnpsShellAssets.OpenAsset`; the loader does not index every one (there is no
manifest for them), so a consumer walks `assets/` itself when it wants them.

### `vsh_asset` shared media

`filesystems/system_ex/vsh_asset/` holds the shell's shared media (30 files):

| File(s) | Format | Notes |
| --- | --- | --- |
| `bg_hub_default.dds`, `bg_NPXS400xx.dds` (15 total) | DDS, BC7 (`DXGI_FORMAT_BC7_UNORM`), 3840x2160 | Per-hub 4K backgrounds. Need a BC7 decode step before display. |
| `bgm_home.at9`, `bgm_onboarding.at9` | ATRAC9 | Background music. The GUI already contains an ATRAC9 decoder (`SharpEmu.GUI/Atrac9`). |
| `sfx_coldboot.at9`, `sfx_warmboot.at9` | ATRAC9 | Boot chimes. |
| `psbutton_press.mp4` | MP4/H.264 | PS-button press animation. |
| `Sce.PlayStation.PUI*.rco`, `ReactNative.*.rco`, `Sce.Vsh.*.rco` (9 total) | RCO archive | Packed PUI resource archives; would need an RCO unpacker to mine. |
| `Sce.Vsh.ShellUI.BGLayer.Particle{0,1}.gnf` | GNF | GPU textures (GNF, magic `GNF `); need GNF parsing. |

`RnpsShellAssets.GetHubBackgroundPath(titleId)` resolves `bg_<titleId>.dds`,
falling back to `bg_hub_default.dds`. Note the DDS files are BC7 and 4K, so a
consumer must decode BC7 before drawing them; the loader hands back the path
only.

## Shell audio

`SharpEmu.GUI.SystemAssets.ShellAudio` exposes the four `vsh_asset` audio
tracks as `ShellAudioTrack` values:

| Track | File | Content |
| --- | --- | --- |
| `BootChime` | `sfx_coldboot.at9` | Cold-boot chime. |
| `WarmBootChime` | `sfx_warmboot.at9` | Resume-from-rest chime. |
| `HomeBgm` | `bgm_home.at9` | Home-screen background music (~8.5 MB). |
| `OnboardingBgm` | `bgm_onboarding.at9` | First-boot onboarding music. |

All four are ATRAC9 streams in plain RIFF/WAVE containers — the same layout as
a game's `sce_sys/snd0.at9` — so `ShellAudio.TryDecodeToWav` reuses the exact
decoder the library preview player (`SndPreviewPlayer`) already runs on the
vendored LibAtrac9 (`SharpEmu.GUI/Atrac9`); no second decoder exists.

`ShellAudio.GetTrackPath(track)` resolves a track inside the dump located by
`RnpsShellAssets.LocateDumpRoot()` and returns null when the dump or the file
is absent. The play hooks — `PlayBootChime()` (one-shot) and
`PlayHomeBgm(loop: true)` — decode on a background task and play through
winmm's `PlaySound`, so they are Windows-only and safe no-ops elsewhere or
without a dump. winmm allows one active sound per process, so starting a shell
track replaces whatever is playing (including the snd0.at9 library preview).
As with every other dump asset, the audio is Sony proprietary content read
from the user's own disk at runtime; no audio ships in this repository.

## Fonts

Sony's shell fonts are **all proprietary** and must never be shipped in this
repository or loaded into Prosperismo's own UI. They live in the dump at:

- `filesystems/preinst/common/font/` — the SST family (`SST-Roman/Bold/Italic/
  Light/Medium` + Arabic/Thai/Vietnamese variants), `SSTJpPro-*`, `DFHEI5-SONY`,
  `SCEPS4Yoongd-*`, `YoonGothicProSIE*`, `SIE-RDC-*`, `SCE-RDC-*`,
  and CJK/ARIB faces.
- `filesystems/system_ex/common_ex/font2/PS4Icon.ttf` — the PS glyph/icon font
  (button glyphs), plus `SceWebKitSupplemental.otf`.

Prosperismo replaces all of these with the open-source **Inter** font, which is
already a dependency of the GUI (`Avalonia.Fonts.Inter` in
`SharpEmu.GUI.csproj`). The rnps loader deliberately exposes **no** font APIs so
that proprietary faces are never surfaced.

## Runtime discovery

`RnpsShellAssets.LocateDumpRoot()` finds a dump root in this order:

1. The `SHARPEMU_FW_DUMP` environment variable, if it points at an existing
   directory.
2. `games/PS5_4.03_reconstructed` relative to the current working directory.
3. `games/PS5_4.03_reconstructed` relative to the executable directory
   (`AppContext.BaseDirectory`).

If none exists, `IsAvailable` is `false` and every enumeration method returns an
empty result — nothing throws. This mirrors how the rest of the emulator treats
the dump as optional developer content.

### Home source and NPXS40087 resource references

The visible Home application is **NPXS40002**. Its 4.03
`application.ps.bundle` remains signed/opaque and belongs to the emulator boot
path. For layout comparison, `Ps5HomeSourceBundle` can reference an external
readable 3.00 `NPXS40002.js` with `SHARPEMU_PS5_HOME_SOURCE`; it records only
the path and length and never copies the source into this repository.

`Ps5ShellResourcePack` opens the original 4.03 NPXS40087 resource pair directly
from the user's dump:

```
filesystems/system_ex/app/NPXS40087/psm/Application/resource/
  Sce.Vsh.ShellUI.Base.rco
  Sce.Vsh.ShellUI.BGLayer.rco
```

Both files must be present. Payloads are decoded in memory and are never
extracted or written to a cache. Base's `tex_default_game` is used as the first
visible runtime-backed fallback for an installed title with no cover.
BGLayer.rco is indexed as part of the pack, but its 4.03 contents are VR/gaze
furniture and are not misrepresented as the normal Home background. Shared
Home UI art continues to come from `Sce.PlayStation.PUI_UI3.rco` in
`vsh_asset`. `SHARPEMU_PS5_SHELL_RESOURCE_DIR` can point directly at the
NPXS40087 resource directory for a nonstandard dump layout.

`SHARPEMU_UI_MODE=sony` is the default on this branch and gives the fixed
1920x1080 console surface the whole window. `SHARPEMU_UI_MODE=desktop` keeps
the ordinary Prosperismo title bar, launch controls, console, and status strip
visible. F10 remains the runtime chrome toggle in either mode.

## Offline catalog tool

`scripts/rnps_catalog.py` (Python stdlib only) walks a dump and emits a JSON
catalog:

```bash
# Print the catalog to stdout
python scripts/rnps_catalog.py <dump-root>

# Write it to a file
python scripts/rnps_catalog.py <dump-root> --output catalog.json
```

It accepts either a dump root (contains `filesystems/`) or a path pointing
directly at the `rnps` directory. For each app it records the manifest fields,
the bundle header (magic / container version / size), any `appdb` tiles with
their localized title names, and an `assets/` PNG count. Missing directories,
empty stubs, and malformed JSON are handled without failing.
