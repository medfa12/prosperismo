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

This `PS5UPDATE1` package carries exactly **two filesystems**. There is no `preinst`
partition in `PS5UPDATE1`: the
string `preinst` occurs in `/system` only as path *references* inside
`/sys/vsh_prefetch_list.dat` (font paths) and one XML comment. The preinstalled font
and app content is not in this package. An official update set can also include a
separate `PS5UPDATE2` package containing the `preinst` font volume; measurements from
that companion package are retained in section 7.

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

## 7. Companion-package and extracted-tree measurements

This section preserves measurements from the separately extracted
`games/PS5_4.03_reconstructed/PUP_dec/` tree. It complements the directly parsed
`PS5UPDATE1.PUP.dec` contract above by covering the companion `PS5UPDATE2` package and
its per-segment block-size exception. Two assumptions motivated the work: that an
update set carries the whole system image, and that every compressed segment uses a
fixed chunk size.

Nothing from a dump is committed. Every number below is either arithmetic over file
sizes in an extracted tree or a short structural quotation.

### 7.1 How to read this

| Marker | Meaning |
|---|---|
| EXACT | Read directly out of a file on disk. The file and the offset are given. |
| DERIVED | Arithmetic over EXACT values. The derivation is shown so it can be rechecked. |
| INFERRED | A structural reading that is consistent with everything measured but is not itself written down anywhere. |
| UNRESOLVED | Known to be undetermined. Listed so nobody re-derives it by guessing. |

### 7.2 Container-header evidence in the extracted tree

**Source tier 3, and the weakest section in this document.** The reference tree here
(`games/PS5_4.03_reconstructed/PUP_dec/`) is *already extracted*; no raw `.PUP` is on
disk, so the header fields below could not be re-read from a file. They are carried
over from the extraction pass that produced that tree, and are marked accordingly.
Everything from section 7.3 onward *was* re-measured and is much stronger.

| Field | Offset | Value | Marker |
|---|---|---|---|
| Magic | 0x00 | `0xEEF51454` | INFERRED, not re-read |
| Total container size | 0x10, u64 | file length | INFERRED, not re-read |
| Entry table | 0x20 | array of 32-byte entries | INFERRED, not re-read |
| Entry | +0x00, u64 | `type` (also carries flags in its high bits) | INFERRED, not re-read |
| Entry | +0x08, u64 | `offset` of the segment payload | INFERRED, not re-read |
| Entry | +0x10, u64 | `csize`, compressed length | INFERRED, not re-read |
| Entry | +0x18, u64 | `usize`, uncompressed length | INFERRED, not re-read |
| Block-table link | `(type >> 20) & 0xFFF` | index of the companion entry holding this segment's block table | INFERRED, corroborated by the companion-link measurements below |

If anyone re-runs the extraction from a raw `.PUP`, replace this table with EXACT rows
and delete this caveat. Until then treat section 1 as the least trustworthy part of
this file.

### 7.3 What is actually in the two-package update set

**EXACT.** Directory listing and file sizes of
`games/PS5_4.03_reconstructed/PUP_dec/`. A 4.03 update ships as **two** packages, and
between them they do **not** contain a whole console.

#### PS5UPDATE1: the system

| Entry | File | Size |
|---|---|---|
| 1 | `1_eula.xml` | 1 557 889 |
| 2 | `2_updatemode.elf` | 11 619 748 |
| 4 | `4_mbr.bin` | 512 |
| 5 | `5_kernel.bin` | 22 051 328 |
| 11 | `11_titania.bls` | 9 062 912 |
| 12 | `12_version_name.xml` | 134 |
| 14 | `14_eap_kbl.bin` | 276 480 |
| 15 | `15_bd_firm_info.json` | 293 |
| 16 | `16_emc_salina_c0.bls` | 454 144 |
| 17 | `17_floyd_salina_c0.bls` | 240 128 |
| 18 | `18_usb_pdc_salina_c0.bls` | 46 080 |
| 259 | `259_oberon_sec_ldr_c0.bin` | 401 008 |
| 260 | `260_oberon_sec_ldr_d0.bin` | 401 344 |
| 513 | `dev/513_wlanbt.bin` | 791 040 |
| **515** | `dev/515_ssd0.system_b` | **251 527 168** |
| **516** | `dev/516_ssd0.system_ex_b` | **996 409 344** |

#### PS5UPDATE2: preinst only

| Entry | File | Size |
|---|---|---|
| 1 | `1_eula.xml` | 1 557 889 |
| 2 | `2_updatemode.elf` | 11 619 748 |
| 12 | `12_version_name.xml` | 134 |
| **519** | `dev/519_ssd0.preinst` | **38 862 848** |

#### The consequence

**EXACT, and it settles a standing question.** `519_ssd0.preinst` is an exFAT volume
(`EXFAT   ` at offset 3, bytes-per-sector `2^12`, sectors-per-cluster `2^4`, root
cluster 5). Harvesting the UTF-16LE names out of its `0xC1` FileName directory entries
gives **four directories and 42 font files, and nothing else**:

| Directory | Contents |
|---|---|
| `font` | 42 files: `SST-{Roman,Light,Medium,Bold}` plus italics, `SSTArabic-*`, `SSTThai-*`, `SSTVietnamese-*`, `SSTJpPro-{Regular,Bold}`, `SSTTypewriter-{Roman,Bd}`, `SSTAribStdB24-Regular.ttf`, `SIE-RDC-Pr5N-{B,M}-JPN.otf`, `SCE-RDC-{B,R}-JPN.otf`, `SCEPS4Yoongd-{Bold,Light,Medium}.otf`, `YoonGothicProSIE{720,760,780}.otf`, `DFHEI5-SONY.ttf`, and eight `[a-z]0NNNNN[dmt]s.ttf` files |
| `common` | present, no filename entries |
| `vsh_asset` | present, **no filename entries** |
| `priv` | present, **no filename entries** |

So `preinst` is a **font package**. `/vsh_asset` and `/priv` are empty *in the official
update itself*, not merely empty in our dump.

Therefore, DERIVED: **`initial_boot_movie.mp4`, `wave0.fbxd` and `wave1.fbxd` are
factory content.** They live in `/vsh_asset` on a shipped console and are carried by no
update package. This is why `wave0/1.fbxd` could never be found by looking harder at
update material (see `docs/ps5-background-native.md`, "What is still unknown"), and it
retires that line of search: the only way to those files is a console-side dump of the
factory partition.

It also confirms `docs/ps5-fonts.md`'s font inventory from a second, independent
direction, and adds the families that page does not list (Arabic, Thai, Vietnamese,
ARIB, and the Korean Yoon Gothic set).

### 7.4 Block tables: the part that was wrong

The working assumption was a fixed stride: compressed segments chunked at a constant
512 KB spacing. **There is no fixed stride.** Each compressed segment has a companion
segment holding an explicit block index, and the blocks are packed at their own
compressed lengths.

#### The two table shapes

**EXACT**, measured over all 11 tables in the tree. A table is one of two things:

| Shape | Layout | Used for |
|---|---|---|
| digest-only | `[n x 32-byte digest]` | segments stored uncompressed |
| digest + index | `[n x 32-byte digest][n x (u32 offset, u32 size)]` | segments stored compressed |

The two arrays are back to back. The digest array comes first and the `(offset, size)`
array starts at byte `n * 32`. A table's shape is decidable from its length alone:
`len % 40 == 0` for the second shape, `len % 32 == 0` and `len % 40 == 16` for the
first.

#### The measurements

`n` is the block count. `ceil(usize / 512 KB)` is the predicted block count.

| Package | Table | Table size | Target | n | `ceil(target / 512 KB)` | Match | Shape | Blocks stored verbatim |
|---|---|---|---|---|---|---|---|---|
| PS5UPDATE1 | `tables/5_for_12.img` | 40 | `12_version_name.xml`, 134 | 1 | 1 | yes | digest+index | 0 |
| PS5UPDATE1 | `tables/7_for_2.img` | 736 | `2_updatemode.elf`, 11 619 748 | 23 | 23 | yes | digest-only | n/a |
| PS5UPDATE1 | `tables/14_for_5.img` | 1 376 | `5_kernel.bin`, 22 051 328 | 43 | 43 | yes | digest-only | n/a |
| PS5UPDATE1 | `tables/16_for_14.img` | 40 | `14_eap_kbl.bin`, 276 480 | 1 | 1 | yes | digest+index | 0 |
| PS5UPDATE1 | `tables/18_for_11.img` | 720 | `11_titania.bls`, 9 062 912 | 18 | 18 | yes | digest+index | 10 of 18 |
| PS5UPDATE1 | `tables/22_for_513.img` | 80 | `513_wlanbt.bin`, 791 040 | 2 | 2 | yes | digest+index | 0 |
| PS5UPDATE1 | `tables/24_for_515.img` | 19 200 | `515_ssd0.system_b`, 251 527 168 | 480 | 480 | yes | digest+index | 76 of 480 |
| PS5UPDATE1 | `tables/26_for_516.img` | 76 040 | `516_ssd0.system_ex_b`, 996 409 344 | 1 901 | 1 901 | yes | digest+index | 618 of 1 901 |
| PS5UPDATE2 | `tables/5_for_12.img` | 40 | `12_version_name.xml`, 134 | 1 | 1 | yes | digest+index | 0 |
| PS5UPDATE2 | `tables/7_for_2.img` | 736 | `2_updatemode.elf`, 11 619 748 | 23 | 23 | yes | digest-only | n/a |
| PS5UPDATE2 | `tables/9_for_519.img` | 5 960 | `519_ssd0.preinst`, 38 862 848 | 149 | 75 | **no** | digest+index | 0 |

Ten of eleven confirm `n = ceil(usize / 512 KB)` exactly, including the 1 901-block
case where an off-by-one in the block size would be visible immediately.

#### Block size is per segment, not global

**DERIVED.** `519_ssd0.preinst` is the exception and it is a clean one:

```
ceil(38 862 848 / 512 KB) = ceil(74.125) = 75    does not match n = 149
ceil(38 862 848 / 256 KB) = ceil(148.25) = 149   matches n = 149 exactly
```

Corroborated independently by the block sizes themselves. In the 512 KB tables the
most common compressed size is exactly **524 288** (618 times out of 1 901 for
`system_ex_b`), which is the incompressible case stored verbatim. In
`9_for_519.img` the **largest** compressed size over all 149 blocks is **242 596**,
which never reaches 262 144 and never comes near 524 288. A 512 KB-blocked segment with
149 blocks would have to contain at least one block larger than 256 KB somewhere in 38
MB of compressed data; it contains none.

So: **512 KB for every segment in the tree except `preinst`, which is 256 KB.** Do not
hardcode 512 KB. Read the block count from the table and divide.

#### Incompressible blocks are stored verbatim

**EXACT.** A block whose compressed size equals the uncompressed block size is stored
raw, not deflated. Counts are in the measurements table above: 618 of 1 901 in `system_ex_b`, 76 of
480 in `system_b`, 10 of 18 in `titania.bls`. A reader that assumes every block is a
zlib stream will fail on a third of `system_ex`. Test the block size against the segment
block size first, and only then look for a zlib header.

#### Block offsets

**UNRESOLVED.** The `offset` fields are monotonically increasing and are close to the
running sum of the preceding sizes, but they do not reconcile under any single rule
tried. Two examples, both EXACT:

```
tables/9_for_519.img   (0, 519) (512, 5461) (5632, 77683) (82944, 167132) ...
   0 + 519    =    519, floor to 512-byte boundary =    512  = offset[1]   consistent
 512 + 5461   =   5973, floor to 512-byte boundary =   5632  = offset[2]   consistent
5632 + 77683  =  83315, floor to 512-byte boundary =  82944  = offset[3]   consistent

tables/18_for_11.img   (0, 516096) (516096, 524288) (1040384, 516520) (1556896, 524288) ...
1040384 + 516520 = 1556904, but offset[3] = 1556896, which is 8 less
                             and is not a multiple of 512 at all
```

A floor-to-512 rule fits `9_for_519` for every block and fails on `18_for_11`. The
"minus 8" gap suggests a per-block trailer counted inside `size` but not inside the
stride, which would fit the first case too, but that was not confirmed. **Do not build
a reader on the offset arithmetic. Use the offsets as given.**

#### The companion-segment link

**INFERRED.** The extraction tree names content files `<entry type>_<name>` and tables
`<n>_for_<target type>.img`. The left-hand number of a table name is not the table's
entry type, because it collides with content types in the same package
(`tables/16_for_14.img` alongside `16_emc_salina_c0.bls`). It is the table entry's
**index**, which is what the `(type >> 20) & 0xFFF` link in section 1 is said to store.

The observed index-to-target pairs are stable across the two packages where they
overlap:

| Package | Table index to target type |
|---|---|
| PS5UPDATE1 | 5→12, 7→2, 14→5, 16→14, 18→11, 22→513, 24→515, 26→516 |
| PS5UPDATE2 | 5→12, 7→2, 9→519 |

`5→12` and `7→2` are identical in both, which is the corroboration. The exact index
base is **UNRESOLVED**: PS5UPDATE2 holds 4 content files and 3 tables, 7 entries in
all, yet its largest table index is 9. Either some entries produce no file, or the
index is not a dense 0-based position.

### 7.5 Compression

**INFERRED, carried over from the extraction pass, not re-measured.** A compressed
segment is not one deflate stream. It is a sequence of independent zlib streams, one
per block, each inflating to the segment's block size.

The streams use a **4 KB window**, so a block header reads `48 89` rather than the
`78 9C` a default-settings zlib emits. This is arithmetically self-consistent and can
be checked without a file:

| Header | `CMF >> 4` (CINFO) | Window `2^(CINFO+8)` | `(CMF<<8 \| FLG) % 31` |
|---|---|---|---|
| `48 89` | 4 | 4 096 | 0, valid |
| `78 9C` | 7 | 32 768 | 0, valid |

Both are well-formed zlib headers; they differ only in the declared window. A reader
that pattern-matches on `78 9C` to find stream starts will find nothing.

Blocks that did not compress carry no header at all; see "Incompressible blocks are
stored verbatim" above.

### 7.6 Still unknown

| Item | Why | What would close it |
|---|---|---|
| Every field in section 7.2 | No raw `.PUP` in that evidence set, only an extracted tree | Re-run the extraction from a raw package and read the header back |
| The block-offset rule | Two tables need two different rules | Extract with the offsets honoured and diff the result against `PUP_dec` |
| The table index base | Indices exceed the entry count | Same as above |
| Whether the 4 KB window is uniform | Not re-measured | Inflate one block from a raw package |
| What is in `common/` inside `preinst` | Directory entry present, no filename entries harvested; either genuinely empty or the harvest missed a fragmented directory | Mount the exFAT volume properly rather than scanning `0xC1` entries |

### 7.7 What this file does not cover

The `RNPSPACK` / `RNPSHEDR` package format used for individual React Native shell apps
is a different container and is documented in `docs/ps5-rn-bundle-map.md`, which reads
its cleartext header including the per-application X.509 leaf certificate. Crash dumps
are in `docs/ps5-core-dumps.md`.
