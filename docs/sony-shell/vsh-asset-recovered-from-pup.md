<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# `vsh_asset` was inside the update all along

The particle textures this project spent considerable effort declaring "absent
from every dump" were present in `300REC.7z` from the moment it was supplied.
They sit in an **exFAT filesystem inside the update**, which is why every
earlier search missed them.

## Why they were invisible

Two structural reasons, both of which defeated the searches used up to now:

- **exFAT stores filenames as UTF-16LE**, split across multiple 32-byte File
  Name records with a `0xC1` tag between fragments. So
  `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` does not exist as a contiguous string
  anywhere in the image - not in ASCII, and not even in UTF-16. Byte searches
  for the filename were structurally incapable of finding it.
- **Files live in cluster chains**, so locating a magic number and reading
  forward does not recover a file. The FAT has to be walked.

Compounding this, entropy was used early on to conclude the payload was
encrypted. It is zlib-compressed, and compressed data is equally incompressible,
so the measurement could never have distinguished the two.

## The directory

Walking the exFAT directory yields the real `vsh_asset` listing, with sizes:

| Size | Name |
|---|---|
| 167,936 | `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` |
| 167,936 | `Sce.Vsh.ShellUI.BGLayer.Particle1.gnf` |
| 53,365,888 | `Sce.PlayStation.PUI_UI3.rco` |
| 19,526,560 | `ReactNative.Components.CommonAssets.rco` |
| 18,358,640 | `Sce.PlayStation.PUI.rco` |
| 1,290,320 | `Sce.Vsh.VoiceAndAgent.rco` |
| 8,919,368 | `bgm_home.at9` |
| 6,000,968 | `bgm_onboarding.at9` |
| 332,100 | `sfx_coldboot.at9` |
| 784,100 | `sfx_initialboot.at9` |
| 306,500 | `sfx_transition.at9` |
| 202,500 | `sfx_warmboot.at9` |
| 8,294,548 | `bg_hub_default.dds`, plus 15 `bg_NPXS*.dds` |
| 695,089 | `initial_setup_controller.mp4` |
| 558,871 | `psbutton_press.mp4` |

`Sce.Vsh.VoiceAndAgent.rco` is the container the boot-screen ripple animations
came from, which independently confirms the `Agent_*` naming recovered in
[`ripple-animation-mapping.md`](ripple-animation-mapping.md).

## The particle textures

Both extracted and verified:

```
magic=GNF  version=0x4  numTextures=1  align=12  streamSize=0x29000 (167,936)
Particle0: payload entropy 6.947, 79.3% non-zero
Particle1: payload entropy 6.074, 72.2% non-zero
```

The size agrees three independent ways - the GNF header's own `streamSize`
field, the exFAT directory entry, and the extracted length. An earlier GNF
carved by magic alone was 4,096 bytes of pure zeros; these are not that.

Stored at `ps5oracle/shell_ui/vsh_asset/` (gitignored - Sony's assets do not
enter the repository).

**Not yet parsed:** the texture descriptor. The `T#` read at offset `0x20`
yields width=1 height=1, which is wrong, so the descriptor lives elsewhere in
the 0xFF8-byte header block. Dimensions and format are therefore **not**
established here.

## What this does not solve

`cis_ac_model.wad` - the 3D scene archive holding the room, light shafts,
skeletons and shaders - is **not** in this update, and cannot be: it postdates
3.0x, verified absent from the 3.00 eboot and present in 12.40. See
[`required-assets.md`](required-assets.md).

So the position is now:

| Need | Status |
|---|---|
| Particle textures | **recovered** |
| Shell audio (bgm, sfx) | **available in this update** |
| Wallpapers, UI containers | **available in this update** |
| The animated room and light shafts | still needs a 12.40+ `vsh_asset` dump |

## Tooling

[`pup_exfat_extract.py`](../../tools/shell-recovery/pup_exfat_extract.py)
reassembles the volume from its zlib chunks and reads it as a filesystem.

A caveat worth recording: concatenating *every* zlib stream in the update
produces an image that is **not** a single coherent volume - the update holds 27
segments, so streams from different volumes end up spliced together and cluster
arithmetic no longer resolves. The extraction above therefore carved by magic at
the directory-declared size rather than following cluster chains. Extracting the
remaining files properly requires reassembling each segment separately.
