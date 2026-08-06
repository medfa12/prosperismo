#!/usr/bin/env python3
# Copyright (C) 2026 Prosperismo Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""
Extract files from the exFAT volume inside a chunked-zlib PS5 .PUP.dec.

The firmware assets are not loose in the update - they sit in an exFAT
filesystem image, which is itself stored as a run of independently
zlib-compressed 512 KB chunks. Two consequences shaped this tool:

* Searching the update for a filename finds nothing. exFAT stores names as
  UTF-16LE, split across multiple 32-byte File Name records with a 0xC1 tag
  between fragments, so no contiguous ASCII or even UTF-16 copy of a long name
  exists anywhere on disk.
* Files are stored in cluster chains, so a file cannot be carved by locating a
  magic number and reading forward. The FAT has to be walked.

The volume is therefore reassembled from its chunks, then read as a filesystem.
"""

import argparse
import os
import struct
import sys
import zlib

MAX_STREAM_INPUT = 2 << 20
READ_WINDOW = 64 << 20
OVERLAP = 1 << 20

ENTRY_FILE = 0x85
ENTRY_STREAM = 0xC0
ENTRY_NAME = 0xC1
ATTR_DIRECTORY = 0x10


def is_zlib_header(cmf, flg):
    return (cmf & 0x0F) == 8 and (((cmf << 8) | flg) % 31) == 0


def inflate_volume(pup_path, out_path):
    """Reassemble the decompressed image in file order."""
    fh = open(pup_path, "rb")
    size = fh.seek(0, 2)
    fh.seek(0)
    written = 0
    streams = 0
    with open(out_path, "wb") as out:
        pos = 0
        while pos < size:
            fh.seek(pos)
            buf = fh.read(READ_WINDOW)
            if not buf:
                break
            view = memoryview(buf)
            o = 0
            while o < len(buf) - 2:
                if is_zlib_header(buf[o], buf[o + 1]):
                    window = view[o:o + MAX_STREAM_INPUT]
                    try:
                        dec = zlib.decompressobj()
                        data = dec.decompress(window)
                    except zlib.error:
                        o += 1
                        continue
                    if len(data) >= 4096:
                        out.write(data)
                        written += len(data)
                        streams += 1
                        if streams % 250 == 0:
                            print(f"  ...{streams} streams, {written / 1e6:.0f} MB", flush=True)
                        o += max(len(window) - len(dec.unused_data), 1)
                        continue
                o += 1
            if len(buf) < READ_WINDOW:
                break
            pos += max(len(buf) - OVERLAP, 1)
    return written, streams


class ExFat:
    """A read-only exFAT reader over an in-file volume at a known offset."""

    def __init__(self, fh, volume_offset):
        self.fh = fh
        self.base = volume_offset
        self.size = fh.seek(0, 2)
        fh.seek(volume_offset)
        boot = fh.read(512)
        if boot[3:11] != b"EXFAT   ":
            raise ValueError("not an exFAT boot sector")
        self.fat_offset, = struct.unpack("<I", boot[80:84])
        self.fat_length, = struct.unpack("<I", boot[84:88])
        self.heap_offset, = struct.unpack("<I", boot[88:92])
        self.cluster_count, = struct.unpack("<I", boot[92:96])
        self.root_cluster, = struct.unpack("<I", boot[96:100])
        self.sector_shift = boot[108]
        self.cluster_shift = boot[109]
        self.sector = 1 << self.sector_shift
        self.cluster = self.sector << self.cluster_shift

    def describe(self):
        return (f"exFAT @0x{self.base:X}  sector={self.sector} cluster={self.cluster} "
                f"heap@{self.heap_offset} root={self.root_cluster} "
                f"clusters={self.cluster_count:,}")

    def cluster_pos(self, cluster):
        return (self.base
                + ((self.heap_offset + ((cluster - 2) << self.cluster_shift)) * self.sector))

    def fat_next(self, cluster):
        pos = self.base + (self.fat_offset * self.sector) + (cluster * 4)
        if pos < 0 or pos + 4 > self.size:
            return None
        self.fh.seek(pos)
        raw = self.fh.read(4)
        if len(raw) < 4:
            return None
        return struct.unpack("<I", raw)[0]

    def read_chain(self, first, length, contiguous):
        """Read `length` bytes starting at `first`, following the FAT unless
        the entry is flagged contiguous (NoFatChain).

        A volume recovered from an update image is frequently truncated - its
        boot sector describes more clusters than the image actually holds - so
        a read can land past EOF and return nothing. Without a short-read
        break the loop would follow the chain forever while making no progress.
        """
        out = bytearray()
        cluster = first
        guard = 0
        max_clusters = (length // self.cluster) + 2
        while len(out) < length and guard <= max_clusters:
            if cluster < 2 or cluster >= 0xFFFFFFF7:
                break
            pos = self.cluster_pos(cluster)
            if pos < 0 or pos >= self.size:
                break
            self.fh.seek(pos)
            piece = self.fh.read(min(self.cluster, length - len(out)))
            if not piece:
                break
            out += piece
            guard += 1
            if contiguous:
                cluster += 1
            else:
                nxt = self.fat_next(cluster)
                if nxt is None or nxt == cluster:
                    break
                cluster = nxt
        return bytes(out[:length])

    def walk(self, cluster, contiguous=False, path="", depth=0, seen=None):
        """Yield (path, name, length, first_cluster, contiguous) for every file."""
        if seen is None:
            seen = set()
        if depth > 12 or cluster in seen:
            return
        seen.add(cluster)

        # A directory is read a bounded number of clusters deep; the guard
        # keeps a corrupt chain from pulling in the whole volume.
        data = self.read_chain(cluster, min(self.cluster * 16, 4 << 20), contiguous)
        i = 0
        while i + 32 <= len(data):
            tag = data[i]
            if tag == 0x00:
                break
            if tag != ENTRY_FILE:
                i += 32
                continue
            secondary = data[i + 1]
            attrs, = struct.unpack("<H", data[i + 4:i + 6])
            name = ""
            first = length = None
            flags = 0
            j = i + 32
            for _ in range(secondary):
                if j + 32 > len(data):
                    break
                t = data[j]
                if t == ENTRY_STREAM:
                    flags = data[j + 1]
                    length, = struct.unpack("<Q", data[j + 8:j + 16])
                    first, = struct.unpack("<I", data[j + 20:j + 24])
                elif t == ENTRY_NAME:
                    name += data[j + 2:j + 32].decode("utf-16-le", "ignore")
                j += 32
            name = name.rstrip("\x00")
            i = j
            if not name or first is None or length is None:
                continue

            # Reject entries that cannot be real. Reassembled volumes contain
            # stretches that parse as directory entries but are not, and
            # following them costs enormous reads at random offsets.
            if first < 2 or first >= self.cluster_count + 2:
                continue
            if length > (1 << 32):
                continue
            if any(ord(c) < 0x20 for c in name):
                continue
            no_fat_chain = bool(flags & 0x02)
            full = f"{path}/{name}"
            if attrs & ATTR_DIRECTORY:
                yield from self.walk(first, no_fat_chain, full, depth + 1, seen)
            else:
                yield full, name, length, first, no_fat_chain


def find_volumes(path):
    """Offsets of every exFAT boot sector in the reassembled image."""
    found = []
    with open(path, "rb") as fh:
        base = 0
        prev = b""
        while True:
            chunk = fh.read(1 << 25)
            if not chunk:
                break
            buf = prev + chunk
            s = 0
            while True:
                i = buf.find(b"EXFAT   ", s)
                if i < 0:
                    break
                s = i + 1
                if i >= 3:
                    found.append(base - len(prev) + i - 3)
            prev = chunk[-16:]
            base += len(chunk)
    return found


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pup")
    ap.add_argument("out_dir")
    ap.add_argument("--image", help="reuse/emit the reassembled volume here")
    ap.add_argument("--only", action="append",
                    help="extract only names containing this (repeatable)")
    args = ap.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)
    image = args.image or os.path.join(args.out_dir, "volume.img")

    if not os.path.exists(image):
        print("reassembling volume from zlib chunks ...")
        written, streams = inflate_volume(args.pup, image)
        print(f"  {written:,} bytes from {streams:,} streams -> {image}\n")
    else:
        print(f"reusing {image} ({os.path.getsize(image):,} bytes)\n")

    volumes = find_volumes(image)
    print(f"exFAT boot sectors: {len(volumes)}")

    fh = open(image, "rb")
    total = 0
    for off in volumes:
        try:
            fs = ExFat(fh, off)
        except (ValueError, struct.error):
            continue
        print(f"\n{fs.describe()}")
        try:
            entries = list(fs.walk(fs.root_cluster))
        except Exception as exc:
            print(f"  walk failed: {exc}")
            continue
        print(f"  {len(entries)} files")
        for full, name, length, first, contiguous in entries:
            if args.only and not any(k.lower() in name.lower() for k in args.only):
                continue
            if not length or length > (256 << 20):
                continue
            data = fs.read_chain(first, length, contiguous)
            if len(data) != length:
                print(f"  SHORT {name}: got {len(data):,}/{length:,}")
                continue
            dest = os.path.join(args.out_dir, name)
            with open(dest, "wb") as w:
                w.write(data)
            total += 1
            print(f"  extracted {name:52} {length:>12,} B", flush=True)
    print(f"\nextracted {total} files to {args.out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
