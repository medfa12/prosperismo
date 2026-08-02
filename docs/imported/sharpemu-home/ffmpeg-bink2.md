<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# FFmpeg with Bink2 (BK2) decoding for SharpEmu

> **Current consumption model (2026-07-31):** AvPlayer and the host Bink bridge
> use FFmpeg libraries in-process through `FFmpeg.AutoGen` by default.
> `SHARPEMU_FFMPEG_PATH` explicitly selects the older external
> `ffmpeg`/`ffprobe` process path. The build and binary notes below remain
> useful for that override and for producing Bink2-capable native libraries,
> but they no longer describe the default Astro MP4 path.

PS5 titles frequently ship their intros, logos, and cutscenes as RAD Game Tools
**Bink Video 2** streams (`.bk2`). Upstream FFmpeg only decodes the original
Bink Video ("Bink video 1"); it has no Bink Video 2 decoder. SharpEmu's AvPlayer
HLE therefore relies on a small FFmpeg fork that adds one.

## What the `sharpemu/FFmpeg` fork adds

The fork lives at [`sharpemu/FFmpeg`](https://github.com/sharpemu/FFmpeg). On top
of upstream FFmpeg it adds an **open Bink Video 2 decoder** so that PS5
intro/movie assets can be turned into raw frames. After the change the codec
table exposes both generations:

```
 D.V.L. binkvideo            Bink video
 D.V.L. binkvideo2           Bink video 2
 D.AIL. binkaudio_dct        Bink Audio (DCT)
 D.AIL. binkaudio_rdft       Bink Audio (RDFT)
```

The `binkvideo2` line (`D` = decoding supported) is the new capability; the Bink
audio decoders already existed upstream.

The fork also carries `scripts/build-sharpemu-ffmpeg.sh`, vendored verbatim into
this repo at [`scripts/build-sharpemu-ffmpeg.sh`](../scripts/build-sharpemu-ffmpeg.sh).
It configures a static, network-disabled FFmpeg with only the `file` and `pipe`
protocols (enough for AvPlayer, which feeds files and reads frames over a pipe)
and runs `make` / `make install`. Run it from a checkout of the fork:

```bash
# inside a clone of sharpemu/FFmpeg
./scripts/build-sharpemu-ffmpeg.sh [install-dir]
```

## How SharpEmu consumes FFmpeg

SharpEmu does **not** link FFmpeg into the main executable. Its default AvPlayer
path loads native FFmpeg libraries in-process via `FfmpegNativeVideoDecoder` and
`FfmpegNativeAudioDecoder`; the Bink host bridge likewise tries
`FfmpegNativeBinkFrameSource` first. This is the path exercised by Astro's MP4
intro.

An external executable is used only when `SHARPEMU_FFMPEG_PATH` is explicitly
set. In that mode AvPlayer launches `ffmpeg`, locates adjacent `ffprobe`, and
reads decoded frames over pipes. The resolver in
`src/SharpEmu.Libs/AvPlayer/AvPlayerExports.cs` (`FindFfmpegTool`) searches, in
order:

1. The explicit override env var: **`SHARPEMU_FFMPEG_PATH`** (and
   `SHARPEMU_FFPROBE_PATH` for ffprobe). If set and the file exists it wins.
2. The directory of a configured `SHARPEMU_FFMPEG_PATH` (so `ffprobe` is found
   beside a hand-set `ffmpeg`).
3. `PATH`.
4. A built-in list of common install dirs (`FfmpegSearchDirs`), which includes
   **`C:\ffmpeg\bin`** on Windows (plus `C:\Program Files\ffmpeg\bin`,
   `C:\Program Files (x86)\ffmpeg\bin`, `C:\ProgramData\chocolatey\bin`, and the
   usual `/usr/bin`, `/usr/local/bin`, `/opt/homebrew/bin` on Unix).

On Windows the external resolver appends `.exe`. Merely installing
`ffmpeg.exe` no longer selects this path; set `SHARPEMU_FFMPEG_PATH` when the
process-backed override is intentionally required.

## How to obtain the binary (method that worked)

Building the fork from source is optional. A prebuilt Windows x64 `ffmpeg.exe`
is produced by the fork's GitHub Actions and is the fastest way to get a
BK2-capable binary:

1. In `sharpemu/FFmpeg`, open the latest successful `master` run of the build
   workflow and download the **`sharpemu-ffmpeg-win-x64`** artifact. The verified
   binary came from run `29737856163`, source commit
   `cdf3755f44aedb7276baa7b6ce2d68d1089e9fb7` (the Bink2 PR merge).
2. Unzip it. This gives `ffmpeg.exe`
   (SHA256 `211bf06eafad596b5ace5beac23921566537e0b7b7ff8527b37a97952c8c0596`,
   30,167,040 bytes for the verified build).

### Required runtime dependency (do not skip)

The CI `ffmpeg.exe` is built with the MSYS2 UCRT64 toolchain and is **not fully
self-contained**. Its import table references `libwinpthread-1.dll`, which is not
present on a vanilla Windows install. Without it the exe fails to start with
`STATUS_DLL_NOT_FOUND` (`0xC0000135`).

Fetch the matching `libwinpthread-1.dll` from the MSYS2 UCRT64 repo and place it
next to `ffmpeg.exe`. The `api-ms-win-crt-*` UCRT DLLs it also imports ship with
Windows 10+ and are already present.

### Install layout

Place both files on SharpEmu's default Windows search dir so no env var is
needed:

```
C:\ffmpeg\bin\ffmpeg.exe              (the BK2-capable binary)
C:\ffmpeg\bin\libwinpthread-1.dll     (required runtime dependency)
```

Alternatively, put `ffmpeg.exe` anywhere and point `SHARPEMU_FFMPEG_PATH` at it
(keep `libwinpthread-1.dll` beside it).

## How it was verified on the VM

The binary was installed to `C:\ffmpeg\bin\` on the Windows VM and confirmed with
the codec listing:

```
ffmpeg.exe -hide_banner -codecs
```

The command exited `0` and its output contained the `binkvideo2` / "Bink video 2"
decoder line shown above, proving both that the decoder is compiled in and that
the binary launches on the VM (i.e. the `libwinpthread-1.dll` dependency is
satisfied).

The historical binary check below proved that particular executable exposed a
Bink2 decoder. It did not prove end-to-end guest integration. Current included
tests cover the Bink bridge lifecycle/header behavior, while title-level Bink2
playback evidence belongs in the relevant title journal.
No end-to-end Bink2 decode smoke test was possible in that VM snapshot: the
Astro Bot dump ships its movies as `.mp4`, and no `.bk2`/`.bik` asset was
available. The `-codecs` listing definitively confirms Bink2 support in that
FFmpeg binary, not title-level SharpEmu playback.
