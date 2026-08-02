# PS5 RCO ("RCOF" v0x110) resource-container format

The PS5 system shell stores its UI assets in `.rco` resource containers. Unlike
the encrypted React-Native bundles, these are **not encrypted**: they hold
thousands of readable ASCII names and standard image/sound payloads (PNG, DDS,
GNF, SVG, VAG). This document records what was determined by reading the real
bytes of the version `0x110` containers shipped in a 4.03 firmware dump, and it
distinguishes **measured fact** from **inference**.

A `.rco` file is a *compiled document tree* (conceptually a compiled XML) laid
over a trailing binary blob that concatenates every asset. The four regions are:
a fixed header, a node tree, two string tables (names and labels), and the data
blob.

## Header (measured)

All multi-byte values are little-endian. The header is `0x50` bytes.

| Offset | Size | Field | Notes |
|-------:|-----:|-------|-------|
| `0x00` | 4 | magic | `0x464F4352` = `"RCOF"` |
| `0x04` | 4 | version | `0x110` on every observed file; this reader accepts only `0x110` |
| `0x08` | 4 | tree offset | always `0x50` (the node tree begins right after the header) |
| `0x0C` | 4 | tree size | byte span of the node tree from `0x50`; the name table follows, 16-byte aligned (**inferred** meaning; the value consistently lands just under the name-table offset) |
| `0x10` | 8×8 | section table | eight `(offset, size)` dword pairs |

### Section table (measured)

Eight `(offset, size)` pairs at `0x10`. Three indices are used by this reader;
they were identical in role across every file examined:

| Index | Offset of pair | Role | Evidence |
|------:|---------------:|------|----------|
| 0 | `0x10` | **name table** | its slice holds the id/name records |
| 1 | `0x18` | unknown (size 8, sometimes 0) | not needed for enumeration |
| 2 | `0x20` | **label table** | its slice holds `resource`,`texture`,`src`,`texture/png`,... |
| 3–6 | `0x28`–`0x44` | usually empty (`size == 0`) | occasionally a small table (e.g. `0x54` bytes) |
| 7 | `0x48` | **data blob** | `offset + size == file length` on every file; holds the PNG/DDS/... payloads |

That `data.offset + data.size == fileLength` was confirmed exactly on all five
files measured, which anchors the whole layout.

## Label table (measured)

Section 2 is a run of NUL-terminated ASCII strings. These are the element tags,
attribute names and enumerated attribute values of the compiled document, e.g.:

```
resource  version  type  normal  id  texturetable  texture  src
texture/png  texture/dds  texture/svg  alpha8  off  src_4k  ninepatch
margin  sounddatatable  sounddata  sound/vag  soundgrouptable  soundscript ...
```

Throughout the tree, a label is referenced by its **byte offset within this
table** (not by index). Content types always contain a `/` (`texture/png`,
`texture/dds`, `texture/svg`, `sound/vag`).

## Name table (measured)

Section 0 is a run of records, each:

```
+0x00  u32   back-reference (a link to the owning node; value unimportant to a reader)
+0x04  char* NUL-terminated ASCII name (e.g. "tex_bg_gold", "image_button_base")
              followed by padding to the next 4-byte boundary
```

A name is referenced from the tree by the **byte offset of its record**; the
string itself begins four bytes into the record (skip the back-reference).

## Node tree (partly measured, partly inferred)

The tree at `0x50` is a stream of 32-bit words encoding nested elements and
their attributes. A full generic decode of the element/child linkage was **not**
completed; however, the parts needed to enumerate assets were decoded and
**verified against ground truth**:

- An **attribute** is introduced by a word equal to a label offset, followed by
  a type-code word, followed by the value.
- A **data reference** is the attribute pattern
  `[srcLabel][0x08][relOffset][length]`, where `srcLabel` is one of
  `src`, `src_4k`, `src_lv1`, `src_lv2`; type code `0x08` means "binary blob";
  and `relOffset`/`length` are relative to the data blob (section 7). This is
  **measured**: every such triple's `(relOffset, length)` lands on a real
  payload, and lengths are exact (they include the container's own trailing
  padding to the next blob slot).
- An **id** is the attribute `[idLabel][code][nameOffset]`, where `nameOffset`
  addresses a name record. **Measured.**
- **Name ↔ blob binding**: each data reference belongs to the element whose `id`
  is the *nearest id that follows it* in the word stream (the id terminates its
  element). This rule was **verified** against `Sce.Vsh.ShellUI.BGLayer.rco`,
  whose textures are named monotonically (`tex_2dvr_screen_location_00`,
  `_01`, ...): the "nearest following id" rule reproduces that order exactly.
  It is nonetheless treated as **inferred/heuristic** in the reader, because the
  full element boundaries were not decoded and an element may own several `src`
  blobs (e.g. `src` + `src_4k` + nine-patch pieces share one name).
- **Content type (`TypeLabel`)**: taken as the nearest preceding label
  containing `/`. This is **best-effort**; it is occasionally wrong (a DDS blob
  may report `texture/png`). The authoritative payload kind comes from sniffing
  the blob's magic bytes, which the reader exposes as `Kind`.

### What is authoritative vs heuristic in this reader

| Datum | Confidence |
|-------|-----------|
| header fields, section table | measured / high |
| label table contents | measured / high |
| name table contents | measured / high |
| entry `(DataOffset, DataLength)` | measured / high (from the tree `src`/`0x08` triple) |
| entry payload `Kind` (png/dds/gnf/svg/vag/json) | measured / high (magic sniff of the bytes) |
| entry `Name` | inferred / good (nearest-following-`id` binding, verified on BGLayer) |
| entry `TypeLabel` | inferred / fair (nearest preceding `/`-label) |

The reader also offers a pure magic-scan fallback (`RcoOffsetSource.MagicScan`)
that is only used when the tree yields no entries; it pairs each PNG/DDS/GNF hit
to the nearest preceding name and is clearly marked non-authoritative.

## Per-file summary (measured on the 4.03 dump)

Counts from `scripts/rco_dump.py`. "entries" are tree-decoded data references;
"magic-scan" is the independent count of PNG/DDS/GNF magics in the data blob
(the two agree closely; the excess entries are SVG/VAG/JSON payloads that have no
scanned magic, plus the container's leading template blob).

| File | Size | Labels | Names | Entries | Kinds | Magic-scan |
|------|-----:|-------:|------:|--------:|-------|-----------:|
| `Sce.Vsh.ShellUI.BGLayer.rco` | 489,456 | 10 | 8 | 8 | 7 png, 1 dds | 8 |
| `Sce.PlayStation.PUI_UI3.rco` | 56,652,240 | 81 | 1,031 | 1,092 | 341 png, 675 svg, 45 vag, 31 json | 386 |
| `Sce.PlayStation.PUI.rco` | 18,358,640 | 22 | 7,223 | 7,315 | 7,282 png, 2 dds, 30 bin (RCSF sound), 1 svg | 7,284 |
| `ReactNative.Components.CommonAssets.rco` | 18,315,744 | 15 | 446 | 446 | 88 png, 301 dds, 57 svg | 389 |

(`BGLayer.rco` lives under
`filesystems/system_ex/app/NPXS40087/psm/Application/resource/`, not
`vsh_asset/`.)

Notes on the "extra" payload kinds:

- `PUI_UI3` carries 45 `sound/vag` blobs and 31 JSON blobs (sound scripts,
  starting with `{"ms...`) alongside its textures — the container is a mixed
  texture + sound package.
- `PUI` has 30 blobs whose magic is `RCSF` (an RC sound container), reported as
  `bin` by the kind sniff.
- `CommonAssets` is DDS-heavy (BC-compressed textures) with SVG vector sources.

## The `sound/vag` payloads in `PUI_UI3`

The 45 `sound/vag` entries are the shell's UI interaction cues. Their entry `id`
is the soundscript event name that triggers them (`snd_focus_move`, `snd_enter`,
`snd_cancel`, ...), so the entry name *is* the binding documented in
docs/ps5-shell-motion.md; the name stored inside the payload is the same cue in
the Home bundle's `psfx_*` spelling (`snd_focus_move` holds `psfx_focus_move`).
One entry, `snd_undefined`, is a 44,100 Hz placeholder named `SE_UNDEFINED`; the
other 44 are the real cues.

Each payload is a complete VAG file, not a headerless stream: 0x30 bytes of
big-endian header (`VAGp`, version at 0x04, payload length at 0x0C, sample rate
at 0x10, channel count at 0x1E, a 16-byte ASCII name at 0x20) followed by 16-byte
ADPCM frames. Every cue is version `0x00020001`, 48,000 Hz, 2 channels.

Version `0x20001` selects HEVAG rather than classic PS-ADPCM. The frame layout is
unchanged — `[shift/filter][flags][14 bytes of nibbles]`, 28 samples per frame,
low nibble first — but the predictor is four-tap with a 128-entry coefficient
table, and the filter index is split across both header bytes (high nibble of
byte 0 OR'd with the high nibble of byte 1); the flag stays in the low nibble of
byte 1. Stereo is frame-interleaved: frame 0 is left, frame 1 is right, and so
on, each channel carrying its own predictor state. The first frame of each
channel is a zero frame.

`SharpEmu.GUI.SystemAssets.Audio.VagDecoder` decodes both flavours, and
`SharpEmu.GUI.SystemAssets.ShellUiSounds` maps the cue events onto these entry
names.

## Reference material

The PSP/PS3/PS4 RCO format is publicly documented and was used only as a
*shape* guide (magic, string tables, a compiled tree). Every field above was
re-derived from the real PS5 `0x110` bytes; older-generation offsets and the
zlib/RLE-compressed variants of earlier RCOs do **not** apply — the PS5 files
here store their tables and payloads uncompressed.
