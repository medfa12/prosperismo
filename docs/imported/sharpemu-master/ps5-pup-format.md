<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 PUP container format

This describes the layout of a **decrypted** PS5 update package (`PS5UPDATEn.PUP.dec`),
enough to unpack it end to end with nothing but `zlib`. `scripts/pup_extract.py`
implements everything below.

Scope note: this document is about *decompression* of an already-plaintext file.
Nothing here concerns decryption, keys, or signature verification. A `.PUP` that has
not been decrypted first will not match this layout.

A decrypted PUP looks high-entropy (~8.0 bits/byte) because almost all of it is zlib
data. That is not encryption — do not be misled by an entropy measurement.

## 1. Header

All integers are little-endian.

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 4 | magic `0xEEF51454` (bytes `54 14 F5 EE`) |
| 0x04 | 4 | version / flags (`0x32010110` on 4.03) |
| 0x08 | 8 | unknown |
| 0x10 | 8 | total file size — **matches the file exactly, use it as a sanity check** |
| 0x18 | 2 | segment count (26 on the 4.03 retail update) |
| 0x1A | 2 | unknown (0x32) |
| 0x1C | 4 | padding |
| 0x20 | — | segment table |

## 2. Segment table

`segment_count` records of 32 bytes each, starting at 0x20:

```c
struct pup_segment {
    uint64_t flags;
    uint64_t offset;             // absolute file offset of the payload
    uint64_t compressed_size;    // bytes on disk
    uint64_t uncompressed_size;  // bytes after inflation
};
```

### Flag bits

| Bit / mask | Meaning |
| --- | --- |
| `0x1` | this segment **is a block table** describing another segment |
| `0x8` | payload is zlib-compressed |
| `0x800` | payload is **blocked** (split into fixed-size chunks) |
| `0x20000` | (block tables only) the table carries per-block offset/size pairs |
| `(flags >> 20) & 0xFFF` | (block tables only) index of the segment being described |

`compressed_size == uncompressed_size` also indicates a stored payload, but the flag
bits are authoritative — a *blocked* segment can still be stored (see segments 6 and 13).

Block tables always sit **immediately before** the segment they describe, so the
pairing is easy to sanity-check, but resolve it through the `flags >> 20` index rather
than by position.

## 3. Blocked zlib

This is the part that is easy to get wrong.

A blocked, compressed segment is a **sequence of independent zlib streams**, each of
which inflates to exactly `0x80000` bytes (512 KiB). The final block inflates to
`uncompressed_size % 0x80000` (or a full 512 KiB when it divides evenly).

Two traps:

1. **The streams use a 4 KiB window, so the two header bytes are `48 89`, not the
   familiar `78 9C`.** Any scanner that looks for `78 9C` finds nothing.
2. **Blocks are not laid out at a fixed stride and are not densely packed.** There is
   padding between them, and 0x100 alignment holds for the first couple of blocks and
   then breaks. Scanning forward for the next valid zlib header
   (`(cmf & 0x0F) == 8 && ((cmf << 8) | flg) % 31 == 0`) *appears* to work and then
   silently fails: that predicate matches random data roughly once every 500 bytes, so
   a forward scan lands on false positives and stalls. Do not scan.

**Use the block table instead.** It gives an exact offset and size for every block.

### Block table layout

For a segment with `n = ceil(uncompressed_size / 0x80000)` blocks, the table segment's
payload is two arrays back to back — *not* an array of interleaved records:

```
[ n × 32-byte digest ]            // integrity digests, not needed to unpack
[ n × { uint32 offset; uint32 size; } ]   // only when flag 0x20000 is set
```

so a compressed segment's table is `n * 40` bytes and a stored segment's table is
`n * 32` bytes. `offset` is relative to the *described* segment's `offset` field;
`size` is the number of bytes on disk for that block.

### The incompressible-block rule

**If `size == 0x80000`, the block is stored verbatim — do not try to inflate it.**
This is the single reason a naive extractor stalls partway through: the PS5 filesystem
images contain already-compressed payloads (PNG, DDS, a nested PUP, `.prx` blobs) whose
512 KiB chunks did not shrink, so they are written out raw. A raw block's first bytes
are arbitrary and will fail a zlib header check.

With this rule applied, segments 23 and 25 of the 4.03 update inflate to 443/443 and
1903/1903 blocks with zero errors.

Pseudocode:

```python
for k, (off, size) in enumerate(block_table):
    want = min(0x80000, seg.uncompressed_size - k * 0x80000)
    raw = read(seg.offset + off, size)
    out += raw[:want] if size == 0x80000 else zlib.decompress(raw)
```

## 4. Segment map (retail 4.03 update, 913,664,156 bytes)

| # | Flags | On disk | Inflated | Contents |
| --- | --- | --- | --- | --- |
| 0 | `0xf0200000` | 4,096 | 4,096 | zero-filled |
| 1 | `0x230007` | 120 | 120 | block table for 2 |
| 2 | `0x107c0e` | 545,056 | 1,557,889 | XML |
| 3 | `0x430007` | 40 | 40 | block table for 4 |
| 4 | `0xc07c0e` | 96 | 134 | XML (`pup_meta`) |
| 5 | `0x610007` | 736 | 736 | block table for 6 |
| 6 | `0x207c06` | 11,619,748 | 11,619,748 | **nested PUP**, 6 stored segments, all opaque (EAP/bootloader). No filesystem. |
| 7 | `0xf0040e` | 142 | 293 | JSON (`BdFirmInfo`) |
| 8 | `0x1100006` | 240,128 | 240,128 | SLB2 |
| 9 | `0x100040e` | 454,079 | 454,144 | single zlib stream (not blocked) |
| 10 | `0x1200006` | 46,080 | 46,080 | SLB2 |
| 11 | `0x400006` | 512 | 512 | `SonyInteractive…` text blob |
| 12 | `0xd10007` | 1,344 | 1,344 | block table for 13 (stored, 32 B records) |
| 13 | `0x507806` | 21,750,272 | 21,750,272 | opaque, blocked but stored |
| 14 | `0xf30007` | 40 | 40 | block table for 15 |
| 15 | `0xe07c0e` | 268,080 | 276,480 | opaque |
| 16 | `0x1130007` | 720 | 720 | block table for 17 |
| 17 | `0xb07c0e` | 9,008,000 | 9,062,912 | SLB2 |
| 18 | `0x10300006` | 401,008 | 401,008 | opaque |
| 19 | `0x10400006` | 401,344 | 401,344 | opaque |
| 20 | `0x1530007` | 80 | 80 | block table for 21 |
| 21 | `0x120107c0e` | 543,232 | 791,040 | SLB2 |
| 22 | `0x1730007` | 17,720 | 17,720 | block table for 23 (443 blocks) |
| 23 | `0x120307c0e` | 169,416,704 | 231,931,904 | **exFAT — `/system`** |
| 24 | `0x1930007` | 76,120 | 76,120 | block table for 25 (1903 blocks) |
| 25 | `0x120407c0e` | 698,864,640 | 997,392,384 | **exFAT — `/system_ex`** |

The update carries exactly **two filesystems**. There is no `preinst` partition: the
string `preinst` occurs in `/system` only as path *references* inside
`/sys/vsh_prefetch_list.dat` (font paths) and one XML comment. The preinstalled font
and app content lives on a partition that is not shipped in an update package.

## 5. The exFAT images

Both images are ordinary exFAT with 4096-byte sectors and 64 KiB clusters, root
directory at cluster 5.

The images are **truncated**: the boot sector declares a volume larger than the shipped
bytes (segment 23 declares 640 MiB and ships 221 MiB; segment 25 declares 1.5 GiB and
ships 951 MiB). Only the trailing *free* clusters are omitted — every file's data lies
inside the shipped range, so extraction is lossless. Do not treat the short image as a
corrupt one.

Directory walking is standard exFAT: a `0x85` file entry, a `0xC0` stream-extension
entry, then `0xC1` name entries holding 15 UTF-16 code units each. Honour the
`NoFatChain` bit (`0x02`) in the stream extension's general-secondary-flags byte — when
set, the clusters are contiguous and the FAT must not be consulted.

Top-level trees:

* segment 23 (`/system`): `common/ eap/ priv/ sys/ vsh/` — 103 dirs, 563 files
* segment 25 (`/system_ex`): `app/ common_ex/ etc/ mbus/ priv_ex/ rnps/ vsh_asset/`
  — 557 dirs, 1719 files

Shell UI media (boot sounds, transition SFX, background DDS, `.rco` resource archives)
lives under `/vsh_asset` on `system_ex`.

## 6. Using the tool

```
scripts/pup_extract.py segments PS5UPDATE1.PUP.dec
scripts/pup_extract.py unpack   PS5UPDATE1.PUP.dec out/ --only 23,25
scripts/pup_extract.py ls       out/seg25.img
scripts/pup_extract.py find     out/seg25.img vsh_asset
scripts/pup_extract.py get      out/seg25.img /vsh_asset/sfx_transition.at9 -o media/
```

Note for searches: exFAT splits a long name across 15-character entries, so a raw
byte search of an image for a UTF-16 file name longer than 15 characters can miss it.
Search for a short prefix, or use `find`, which walks the directory tree properly.
