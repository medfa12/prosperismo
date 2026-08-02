# PS5 reactive shell: canonical target and status

This is the coordinating document for the Sony-style SharpEmu shell. It does
not replace the evidence documents linked below; it defines how their recovered
contracts fit together and separates shipped behavior, our implementation, and
experiments.

## Product target

The target is one reactive shell, not a collection of visual demos:

1. Sony-derived home geometry, typography, icon placement, focus/card states,
   transitions, and effects.
2. SharpEmu's games and emulator controls, including SharpEmu Settings. Sony's
   console registry and settings content are research evidence, not product
   content.
3. A native-style background and boot presentation driven by shell state. The
   same state model must coordinate the plate, native particle pattern,
   basemat, selected-title artwork, focus geometry, transitions, and audio.
4. A separate conventional desktop shell remains available for desktop users.

"Sony-derived" means behavior reconstructed from the user's firmware, readable
bundles, native code, shaders, assets loaded in place, and direct captures. It
does not mean importing Sony source into this GPL repository. Where firmware
behavior is not yet known, the implementation and documentation must say so.

## Ground truth and precedence

Use these sources in order, choosing the source appropriate to the question:

| Question | Primary evidence | Location |
|---|---|---|
| Layout, cards, focus ownership, navigation, motion | readable Sony React Native bundles | `C:\sharpemu\games\useful rnps\readable_js_3.00\` |
| Declared background states, durations, modes | managed firmware metadata | 4.03/6.50 firmware material described in `ps5-background.md` |
| Background and focus rendering | native eboot and shader decompilation | `ps5-background-native.md`, `ps5-focus-highlight.md` |
| Appearance and timing | direct console captures and shipped media | user captures and `initial_boot_movie.mp4` in the firmware dump |
| Emulator behavior and product settings | current SharpEmu code and docs | `C:\sharpemu\`, this worktree |

The detailed evidence rules remain in `ps5-reverse-engineering-index.md`. A
community mock-up or our own procedural output never overrides firmware code or
a direct console capture.

## Reactive state contract

The global background state is the root input. It selects a native light mode
such as ColdBoot, WarmBoot, InitialBoot, Bottom, Spread, NoParticle, Black, or
None; coordinates the corresponding timeline, pause/readiness flags, basemat,
and audio; and controls when the interactive shell becomes visible.

Once home is interactive, two more inputs participate:

- the selected title selects and cross-fades title artwork, with the documented
  `pic1 -> pic0 -> pic2 -> bg_hub_default -> #020408` fallback chain;
- the focused element supplies geometry and focus phase to the line and area
  focus passes and, where the native background consumes it, the focus-rect
  buffer.

Settings is a route within this same shell. It retains the background and focus
system and swaps the content model to SharpEmu categories and controls. It must
not open the old desktop settings page in Sony mode and must not expose Sony's
console settings hierarchy.

The word reactive applies to the firmware's global background/light states,
title-artwork transitions, focus-driven effects, and motion clocks. It does not
mean inventing a Settings-only wave. Across every readable managed BGLayer
version, HomeScreen preset 4 is written once at startup and
`SetPresetColour` is empty; normal Home-to-Settings navigation stays on that
plate. The native System Area preset is known and also resolves to record 2 in
normal mode, but no recovered Settings caller selects it.

The name `NoParticle` proves that the managed state selects no explicit light
particle override. It does not make every native pass inert. Plane2 analysis
proves that `wave_bg_p` advances its noise phase by one on every draw, wrapping
after 255. The steady state-to-record selection for Home and Settings is now
proven too, so the translated record-2 plate is the untitled product default.
A fabricated ambient drift remains forbidden.

## Implementation ledger

| Surface | Present in SharpEmu Home | Fidelity status | Next proof needed |
|---|---|---|---|
| Home layout and title strand | 1920x1080 canvas, safety area, Sony bundle constants, title artwork fallback | firmware-derived structure; capture comparison still required | paired 1920x1080 console and host captures |
| Focus/card state | separate line band and translucent area wash, recovered SDF, seven-colour ramp, noise and shimmer, firmware curves; original AreaFocus/LineFocus fragment and vertex programs execute through persistent, separately sized Vulkan targets; fragment records are 128/160 bytes and vertex records are Area 112 bytes / Line 116 bytes (`u_InOutScale` at `0x70`) | original AreaFocus and LineFocus `ShowAlpha=0` are live and time-varying with Sony's `image_focus_noise`; the original vertex path preserves global `ClipPos` across cropped dynamic viewports, so native Area shimmer is enabled and clipped cleanly instead of falling back to the CPU wash | validate card/icon/display geometry and paper-white mode against paired console captures |
| Settings | Sony-style category/detail surfaces with SharpEmu controls | product boundary implemented | route/navigation and focus capture pass |
| Background composition | clear plate, translated Plane2/wave_bg_p renderer backed by the 37-record firmware table, primary/fallback title-art channel, recovered HOME fade/slide/ripple programs, firmware-derived additive frame layer, optional basemat | Plane2 routing, 60 Hz phase, steady Home/Settings records, HOME caller mapping, fade, opaque slide, and the original opaque ripple shader path are recovered; both large-particle passes execute under ColdBoot | execute separate particle bodies for particle themes; close optional gradation/dither/transparent-alpha branches |
| Native particle records and draw | decoder, event routing, complete rendezvous entry layout, field names, bank strides, byte-exact resource sampler, recovered two-buffer ID allocator, full `particle_c` dispatch, both original `large_particle` stages translated/validated/driver-linked, native GNF decode, and an off-screen raster probe | both groups use the firmware-proven primary zero-based ID permutation, bind the same post-compute property allocation, and obey recovered group end gating | preserve property/ping-pong state and Vulkan objects continuously across frames |
| Boot | shipped initial-boot movie path is known; inherited procedural `BootIntroField` exists | procedural output is an experiment, not Sony rendering | replace invented geometry/compositing with native record execution or a separately labelled fallback |
| Desktop shell | remains separate | supported product mode | no Sony-fidelity requirement |

The two frontend modes also have explicit shortcut-friendly launch forms. Start
`SharpEmu --big-picture` (or `--sony-ui`) for the controller-first Sony surface,
and `SharpEmu --desktop-ui` for the conventional desktop launcher. With no
frontend argument the Sony surface remains the default; emulator CLI arguments
are not consumed by these frontend-only aliases.

## Focus acceptance contract

Focus is not "a ring." It is at least two coordinated passes with distinct
roles:

- a narrow line/band around the focused geometry;
- a translucent area wash over the card itself, including its five-second area
  sweep (the separate rotating-mask subpath is disabled by Sony's stock zero
  intensity);
- icon focus and game-card focus may use different geometry and thickness even
  when they share shader mathematics;
- movement, show/hide, and warp phases use the recovered focus curves.

Gold/pink/peach is permitted only where it comes from the recovered seven-entry
shader colour table and its intensity lookup. Arbitrary decorative gradients
are not. The recovered display-output conversion is currently opt-in through
`SHARPEMU_PS5_FOCUS_PAPER_WHITE`; until capture testing resolves the active
console display mode, it must not be described as the default or as visually
validated.

## Boot and background provenance

There are three different artifacts and they must remain clearly labelled:

1. Sony ground truth: the shipped initial-boot movie, firmware state machine,
   native renderer, shaders, particle records, textures, and audio.
2. The native-shell boot experiment on `exp/shell-boot`: Sony's NPXS40087 runs
   deeply enough to initialize graphics and reach composite calls, but has not
   submitted a visible frame.
3. The inherited procedural boot visualizer in this branch: authored geometry,
   particles, colours, and compositor with selected decoded firmware values
   injected into it. Its output is useful for plumbing and experimentation only.

The desired end state is a single renderer whose boot, login, home, title,
settings, shutdown, focus, and audio behavior respond to the recovered shell
state. A procedural screenshot cannot be used as evidence that this has been
achieved.

### Native-frame integration checkpoint

`ShellBackground` now owns a distinct additive native-particle layer between
the plate and basemat. Set `SHARPEMU_PS5_NATIVE_FRAME` to one PNG emitted by
`SharpEmu.Tools.Ps5ParticleDrawProbe`, or to a directory emitted by the sequence
script, and select `ColdBootAnimation`; the layer loads asynchronously and uses
Avalonia `Plus`, the compositor equivalent of the recovered `ONE/ONE/ADD`
blend. A directory is played in ordinal filename order at the rate recorded in
its `sequence.json` manifest (30 fps when absent). The layer is
hidden when motion is disabled, in every non-ColdBoot state, or when no proven
frame exists.

For a headless visual check, run `shell-shot --scene native-background` with
that environment variable set. This checkpoint proves that the two firmware
draw banks can enter the production compositor without bundling Sony data. A
directory cache is animated, but compute/draw still executes ahead of time.

The in-process boundary now exists in two layers:

- `IPs5NativeParticleRenderer` in `SharpEmu.Libs` owns the native five-buffer
  draw ABI and returns tightly packed RGBA without an Avalonia dependency.
  The Vulkan implementation now keeps its device, original shader modules,
  decoded GNF textures, pipeline, descriptors, target and readback allocation
  alive across frames. The two live cold-boot large groups are submitted in
  native group order inside one persistent `ONE/ONE/ADD` render pass; the old
  pair of standalone renders plus CPU clear-colour subtraction is no longer
  the production path.
- `IPs5NativeParticleFrameSource` converts the shell's global state and clock
  into those draws. `Ps5NativeBackgroundLayer` prefers this live source and
  uploads returned frames on the UI thread; the PNG sequence remains the
  bring-up fallback.

The accepted `t=6.416667` two-bank output remains byte-identical after this
split (`SHA-256 2A27428D9DB861D57D5BFC7A240DB6F3C391BCBC03648AC7618C87E62013CC40`).
The existing Vulkan proof body now runs through
`IPs5NativeParticleRenderer` in `SharpEmu.Libs`; the full two-bank oracle
remains byte-identical. The sequence generator also stores each bank's
translated programs and five native vertex buffers under `draw-cache/`.
When `SHARPEMU_PS5_NATIVE_DRAW_CACHE` names that directory (or the directory
named by `SHARPEMU_PS5_NATIVE_FRAME` contains it), the shell renders those
snapshots in-process from the user's GNF textures. First renders are cached as
exact RGBA frames for subsequent loops; the PNG sequence remains fallback.

Two PNG-free headless runs through the production `ShellBackground` produced
different capture hashes, proving the shell clock selects and renders changing
native snapshots rather than one static frame.

The draw cache can also carry `compute/bank0/{particle.spv,resources.bin,ids.bin}`.
When present, the shell replays Sony's bank-0 compute shader in-process at the
selected native time and replaces the cached property binding before drawing.
At `t=6.416667` that runtime path reproduces the locked property SHA-256
`67A18FD30E68E1CD9E9E20237C92E31A61526179A8E22FE4B0D67D4ED27A697A`.

The second bank follows the same in-process route. The former visually accepted
one-based placeholder produced
`AFA477C45E5048E941E0C5B9DDA5E5B35D4A146057684749110864EB30A484A8`,
but firmware disassembly now supersedes that binding. Initializer `0x978e0`
copies owner descriptor `+0x72c` into `ResourcesCs.particleIds1` for both
large-compute groups, and coldboot contains no opcode-11 descriptor replacement.
The live shell therefore uses the primary zero-based permutation
(`428B84F2...F5AB3`), producing `1F5A9938...91181D` and 40 populated records at
`t=6.416667`. Bank0 first produces `67A18FD3...A697A`; bank1 continues from
those exact bytes and produces the shared-allocation hash
`E7439935...FD226B`. That one resulting buffer is bound to both draw passes, as
the native resource callbacks require. Cached properties are fallback only.

This is not yet the whole background state machine. ID choice, shared-buffer
composition, and the coldboot group interval are resolved. The live source now
models the native `+0x6d8/+0x6e0` start/end pairs: group 1 activates at native
time 6.0, group 0 remains eligible for the firmware's seven-second retirement
interval, and the renderer's inclusive `time + step <= groupEnd` test removes
it after native time 13.0. The raw state-3/state-4 delays (3.5/0.5 seconds),
2/3 ramp, and strict 1.5 completion threshold are encoded and test-pinned.
Routing every non-coldboot shell state into those numeric firmware states is
now resolved at the dispatcher boundary: Bottom/Spread/ColdBoot/WarmBoot/
InitialBoot map to raw states 1/2/3/4/6, while home NoParticle makes no setter
call. Executing the eight-bank small-particle bodies selected by the steady
states is still open; direct evaluation of every serialized resource body also
remains.

For an interactive development preview, also set
`SHARPEMU_PS5_NATIVE_PREVIEW=1` before launching SharpEmu Home. This forces the
background owner into `ColdBootAnimation` only for that process. It is an
explicit inspection switch, not the product's steady-home state routing.

All three probe stages accept `SHARPEMU_PS5_PROBE_TIME` in the decoded
6.0-8.5-second window. `scripts/render_ps5_native_particle_sequence.ps1` drives
them together to create a correctly stateful, time-sampled two-bank cache from
the user's shader ELFs and GNF files. Bank 0 starts at native t=0 with the
recovered first shuffled ID buffer; bank 1 retains the accepted visual
baseline. The
compute probe requires and verifies a 32-lane Vulkan **host** subgroup. Sony's
compute shader is a 64-lane guest wave; the translator emulates it with two
pinned host wave32 subgroups and an LDS bridge for cross-half operations.
Unsupported hosts fail rather than generate a misleading cache.

The currently generated frames were directly reviewed and accepted by the user
as visually correct. They are therefore the locked baseline for this native
pass. The two-bank generator adds the missing bank without restyling that
baseline. Integration work may extend the native timeline and react to shell
state; it must not procedurally replace the accepted frames.

## Authoritative supporting documents

- `ps5-rn-layout.md`, `ps5-home-structure.md`, `ps5-home-motion.md`: layout and
  interaction contracts.
- `ps5-focus-highlight.md`: recovered focus shaders and implementation gap.
- `ps5-settings-integration.md`: Sony presentation / SharpEmu content boundary.
- `ps5-background-native.md`: native particle renderer and decoded resources.
- `ps5-background.md`: managed state machine and current runtime correction.
- `ps5-boot-animation.md`, `ps5-shell-boot-attempt.md`: boot evidence and the two
  experiments.
- `ps5-unknowns.md`: unresolved facts and the evidence required to close them.
