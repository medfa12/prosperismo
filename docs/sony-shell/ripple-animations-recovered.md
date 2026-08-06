<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Ripple animations recovered from the 3.00 recovery PUP

Sony's own ripple animations are now extracted — as animated PNGs, at 60 fps,
straight out of `300REC.7z`. This was reachable all along; the earlier
conclusion that the PUP was encrypted was wrong (see
[`firmware-decryption-not-needed.md`](firmware-decryption-not-needed.md)).

## The chain

```
300REC.7z
  -> PS5UPDATE1.PUP.dec            902 MB, chunked zlib (CMF 0x48, 512 KB chunks)
     -> 2,025 streams / 897 MB     tools/shell-recovery/pup_decompress.py
        -> 40 RCOF containers      tools/shell-recovery/pup_extract.py
           -> 16 PNGs, 6 animated
```

## The state names

One carved container holds the ripple state table verbatim:

```
Ripple_Base      Ripple_Entry      Ripple_Ambient           Agent_Core
Ripple_Agent     Ripple_Loud       Ripple_Ambient_simple    Agent_Glow
                 Ripple_Medium                              Agent_Gold
```

`Ripple_Ambient` is the state captured in
`ps5oracle/shell_ui/live_background/default.mp4`. `Loud` / `Medium` and the
`Agent_*` entries indicate these react to audio level and to the voice agent.

## The animations

Six APNGs, all authored with `APNG Assembler 2.91`, all 8-bit RGBA at 60 fps:

| File | Size | Frames | Duration |
|---|---|---|---|
| `148x148_da989b03.png` | 148x148 | 185 | 3.08 s |
| `148x148_6a3813b1.png` | 148x148 | 120 | 2.00 s |
| `168x168_c87f32a3.png` | 168x168 | 84 | 1.40 s |
| `168x168_679481bb.png` | 168x168 | 59 | 0.98 s |
| `140x140_0e671e2c.png` | 140x140 | 50 | 0.83 s |
| `168x168_7cb70859.png` | 168x168 | 40 | 0.67 s |

Content is concentric rings expanding outward and fading — a ripple in the
literal sense.

Stored at `ps5oracle/shell_ui/ripple_apng/` (gitignored; these are Sony's
assets and do not enter the repository).

## What these are NOT

**These are not the fullscreen background.** At 140–168 px they are a UI
element, and the `Agent_*` neighbours point at the voice-agent indicator rather
than the ambient room. The 3D scene described in
[`background-is-a-3d-scene.md`](background-is-a-3d-scene.md) — the room, the
light shaft, the IBL rig — is a separate thing and is still recovered by
reading the 12.40 eboot.

**The state-to-file mapping is not established.** Seven `Ripple_*` names and
six APNGs were found in the same container, but nothing yet ties a specific
name to a specific file. Assigning them by frame count or by proximity would
be a guess, and this project has already had to correct one such guess.

## Still missing

The four `.gnf` textures. The only GNF carved from the PUP is 4,096 bytes with
a body entropy of exactly 0.00 — an all-zero placeholder, not a texture. The
`Particle0.gnf` / `Particle1.gnf` / `shutdown_ramp.gnf` / `diffuse_default.gnf`
set remains absent.
