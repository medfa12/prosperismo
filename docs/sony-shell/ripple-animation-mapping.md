<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Ripple animations: name mapping, and what they are not

The six recovered ripple animations are now mapped to the state names the
container itself declares. This supersedes the "mapping not established" note in
[`ripple-animations-recovered.md`](ripple-animations-recovered.md), and corrects
that document's implication that these belong to the home shell.

## How the mapping was derived

The carved RCOF container holds a **name table** immediately followed by the
resource records:

```
[u32 record_offset][cstring name][pad to 4]   repeated
```

Ten names at an exact `0x4C` stride:

| Record offset | Name |
|---|---|
| `0x0068` | `Agent_Core` |
| `0x00B4` | `Agent_Glow` |
| `0x0100` | `Agent_Gold` |
| `0x014C` | `Ripple_Base` |
| `0x0198` | `Ripple_Agent` |
| `0x01E4` | `Ripple_Ambient` |
| `0x0230` | `Ripple_Ambient_simple` |
| `0x027C` | `Ripple_Entry` |
| `0x02C8` | `Ripple_Loud` |
| `0x0314` | `Ripple_Medium` |

Each record carries a `[u32 data_pointer][u32 byte_size]` pair, with the owning
name's record offset **20 bytes before** the data pointer.

The mapping was not read off that structure alone. Each asset's byte size was
located as a `u32` in the container, and the data pointer beside it was resolved
against a base derived independently: the offset that reconciles pointers with
actual PNG signature positions in the file. **One base (`0x904D0`) satisfies nine
records simultaneously**, and `base + pointer` lands on a valid
`89 50 4E 47 0D 0A 1A 0A` signature for every one of them. A wrong base would
satisfy at most a coincidence or two.

## The mapping

| Name | File | Frames | Duration | Kind |
|---|---|---|---|---|
| `Ripple_Ambient_simple` | `148x148_da989b03` | 185 | 3.10 s | animated |
| `Ripple_Entry` | `148x148_6a3813b1` | 120 | 2.00 s | animated |
| `Ripple_Ambient` | `168x168_c87f32a3` | 84 | 1.40 s | animated |
| `Ripple_Loud` | `168x168_679481bb` | 59 | 1.00 s | animated |
| `Ripple_Medium` | `168x168_7cb70859` | 40 | 0.67 s | animated |
| `Ripple_Base` | `116x116_b2dbb419` | 1 | — | still |
| `Ripple_Agent` | `60x60_f3e5c6c0` | 1 | — | still |
| `Agent_Gold` | `112x112_fc5e1160` | 1 | — | still |

`Ripple_Base` and `Ripple_Agent` being **stills** is informative: the ripple is
composed — a static base and agent sprite with an animated ring layered over
them — rather than each state being a self-contained clip.

### Not resolved

`140x140_0e671e2c` (50 frames, 0.83 s) resolves to an owner **24 bytes** before
its data pointer rather than the consistent 20, and that owner (`Ripple_Loud`)
is already claimed by a record with the correct stride. Its true owner is
therefore one of the two unassigned names — `Agent_Core` or `Agent_Glow` — and
this document does not choose between them.

### A guess that was wrong

Frame count alone suggested the longest animation (3.10 s) would be
`Ripple_Ambient`. It is `Ripple_Ambient_simple`; the real `Ripple_Ambient` is
the 1.40 s clip, and `Ripple_Entry` is longer than both at 2.00 s. Worth
recording as a concrete case where the plausible-looking inference was wrong.

## What these are NOT

**They are not confirmed to be home-shell assets.** Three points against it:

- None of the ten names appears in `NPXS40087/eboot.bin`, `BGLayer.dll.sprx` or
  `Sce.PlayStation.PUI_UI3.rco` at any firmware version on disk.
- The container's other strings are a localisation set (each repeated ~30 times,
  one per language) covering certificates, serial numbers, passwords and USB
  storage — `msg_certificate`, `msg_serial`, `msg_pw`, `msg_issued`,
  `msg_expires`, `msg_error_usb_storage_*`. None of that is shell vocabulary.
- The carve that produced this container used a fixed 64 MB window rather than
  parsing the RCOF length, so it may span **more than one** container. The
  ripple block and the certificate strings could belong to different resources
  that the carve merged.

Calling these "PS5 shell ripple animations" was an over-claim. What is
established is that they are genuine firmware assets with a self-declared state
machine; **which subsystem drives them is not.**

`Agent_*` alongside `Ripple_Agent`, `Ripple_Loud` and `Ripple_Medium` suggests
something audio-reactive with an "agent" concept, but that is inference from
naming and is not evidence.

## Before building on this

Fix the RCO carver to parse the RCOF length field instead of using a fixed
window. That would establish the true container boundary and settle whether the
ripple block and the certificate localisation are one resource or two — which in
turn identifies the owning application.
