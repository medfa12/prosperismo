# Prosperismo migration status

Verified on 2026-08-02 against the imported native baseline at
`fb5ecec455cf6c67154134429485ffccbfc34203`.

> Status: incomplete. The Qt launcher remains the only runnable desktop
> launcher and therefore remains the behavioral oracle. It must not be removed
> until the React Native launcher reaches feature parity and ships with its
> native Windows host module.

## Product boundary

- The native emulator remains C++20, Vulkan, SDL2 and FFmpeg. Its installed
  executable is `prosperismo_emulator`.
- The target Windows frontend is React Native Windows 0.84 with TypeScript.
  Desktop mode will own launcher operations; Big Picture mode will own the
  console-style UI. The current executable frontend is still Qt.
- Product-facing window, package, logger, configuration, profiler and release
  names are Prosperismo. KytyPS5 and SharpEmu remain only in attribution,
  source-provenance notes and live upstream URLs.
- Sony SDK, firmware, symbols and dumps remain ignored oracle inputs under
  `ps5oracle`; none are redistributed in builds.

## Ground-truth ports accepted

- Sony SDK 10 texture descriptors preserve enabled non-depth DCC metadata
  addresses across render-target, storage and sampled aliases. The cache now
  separates identical pixel addresses with different DCC lifetimes. Astro's
  retained descriptor decodes to `0x570520000`.
- Named event flags accept the firmware-observed attribute bit `0x100` and
  Open/Close share the named object's lifetime. The registered NIDs are
  `1vDaenmJtyA` and `s9-RaxukuzQ`.
- PS5 UUID generation follows the SDK layout and firmware/FreeBSD version-1,
  RFC-variant contract instead of producing four unrelated random words.
- The HDR system-service fallback uses the measured firmware words
  `441F546A`, `44754958`, and `3DFFDDEC` rather than assumed display values.

Kyty's existing `getdirentries` and tile-mode-aware image identity were kept:
the proposed donor replacements were not more complete than the current code.

## Frontend verification

- Four supplied Prosperismo icon variants are bundled for light/dark and
  colour/monochrome surfaces; Windows package tiles and the native `.ico` are
  derived from the same set.
- Jest: 4 suites and 8 tests passed.
- TypeScript typecheck and ESLint passed.
- A production Windows Metro bundle passed and copied all eight density assets.
- Native RNW packaging is externally blocked: RNW 0.84 generates a v145 project
  and its CLI requires Visual Studio 18.6+, while this host has VS Build Tools
  2022 17.14/v143. Downgrading to an unsupported RNW line was rejected.

## React Native parity still required

The current TypeScript implementation has recursive `eboot.bin` discovery,
`param.json` metadata, settings serialization, exact launch-argument ordering,
patch-plan discovery and basic Desktop/Big Picture routes. It is not a complete
Qt conversion. Remaining required behavior includes:

- implement and register the `ProsperismoHost` Windows module for filesystem,
  folder selection, settings persistence, executable discovery and supervised
  process launch;
- port compatibility database status/comments and refresh behavior;
- port the complete global and per-game settings editor and custom-settings
  clear operation;
- port search/sort, PIC0 preview, open-game-folder and running-state behavior;
- port patch enable/disable persistence, trophy viewing and confirmed save-data
  removal;
- replace the current Big Picture scaffold with the recovered Sony-like shell
  contracts, then connect both routes to the same library/session state;
- build, package and run the RNW executable before disabling the Qt launcher.

The migration is complete only when those checkpoints pass and the Qt launcher
can be deleted without losing behavior.

## Native verification and known baseline failures

- A clean Release configure and full native build succeeded.
- Focused HLE/audio tests passed, and the DCC descriptor/clip-control selector
  passed.
- Full CTest ran 27 tests. Twenty-two passed. Two unchanged upstream assertions
  fail (`shader_cfg` cube identity and the default shader-compute image
  transition assertion). Three selectors fail earlier because this host Vulkan
  device does not support format 129 with the requested 2x-MSAA depth usage.
  A missing `audio_out2_port_tests` aggregate dependency was fixed; that test
  now builds and passes.

These failures are recorded rather than reclassified as migration successes.
No Astro boot or nonblack final frame is claimed by this migration checkpoint.
