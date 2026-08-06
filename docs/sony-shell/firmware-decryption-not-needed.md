<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Firmware decryption is not the blocker — and 3.0x is the wrong target

Two findings, both measured, that redirect the asset-recovery effort.

## 1. The shell binaries we hold are already decrypted

Entropy over every `NPXS*/eboot.bin` in `ps5oracle` runs **5.0–6.4 bits/byte**,
the signature of ordinary code and data. Encrypted payload sits at ~7.99 (the
`1060` PUP segments measured exactly that). Confirmation is stronger than
entropy alone: `CreateLightShaftModel` and `sequence_scene_builder` match as
**plaintext ASCII** inside `NPXS40087/eboot.bin`. You cannot string-match
ciphertext.

So the scene-renderer code — the thing that actually draws the room, the light
shaft and the sequences — is **already in the clear**, and no key is required
to read it.

## 2. The light-shaft scene does not exist in 3.0x

Symbol counts across the shell eboots we hold:

| Symbol | 3.00 | 12.40 | 13.00 | 13.20 |
|---|---|---|---|---|
| `sequence_scene_builder` | **0** | 1 | 1 | 1 |
| `CreateLightShaftModel` | **0** | 1 | 1 | 1 |
| `CreateBasicModel` | **0** | 1 | 1 | 1 |
| `effect_light_shaft` | **0** | 2 | 2 | 2 |
| `FwLsShader` | 1 | 1 | 1 | 1 |

3.00 carries the FirstWave light-shaft *shader* but none of the scene
architecture. The scene builder, the model constructors and the light-shaft
effect were introduced **after** 3.0x.

This matters directly: **obtaining a decrypted 3.02 would yield a firmware that
does not contain the feature being reverse engineered.** The 12.40 / 13.00 /
13.20 eboots already in hand are the correct targets, and they are already
readable.

## Why the published keys would not have helped anyway

[psdevwiki's PS5 Keys page](https://www.psdevwiki.com/ps5/Keys) publishes a
large amount of real key material, but **none of it decrypts a PUP**, and the
page has no PUP section at all. What it does cover:

- **ROM keyseeds** — inputs to `KMB_AES_CBC_MP0` / `KMB_AES_ECB_MP4`, the key
  management block on the MP0 security processor. These are *seeds into a
  hardware keyladder* rooted in per-SoC fused secrets. The derivation runs in
  silicon; the seeds alone compute nothing off-console.
- **PKG metadata RSA-3072** (private key included) — signs PKG *metadata*.
  PKG is not PUP, and metadata is not content.
- **EMC / EAP / communication-processor keys** — the auxiliary power and
  communication processors. The EMC IPL key is additionally noted as working
  only on EMC revision c0. None touch the main filesystem.
- **Portability EncDec, M.2, trophy keys** — console-side device operations and
  external storage.
- **RNPS keys** — an AES-128-CBC MAC key and an RSA public key. The only entry
  relevant to shell content, and moot: the rnps bundles are already decrypted
  locally (see `rnps_decrypted/`).

The general point is that PS5 content decryption is done **on a jailbroken
console**, by running a payload that asks the hardware to decrypt — which is
exactly what the `decrypt_rnps` `.elf` in `ps5oracle/rnps_decrypted/` is. It is
not an offline operation, and no published key list changes that.

## What is actually still missing

Four texture files: `Particle0.gnf`, `Particle1.gnf`, `shutdown_ramp.gnf`,
`diffuse_default.gnf`. They are absent because **`/system_ex/vsh_asset` was
never dumped**, not because it is encrypted. The fix is a filesystem dump from
a jailbroken console, not a decryption effort.

And per [`background-is-a-3d-scene.md`](background-is-a-3d-scene.md), if the
geometry is constructed in code, those four files are textures over geometry we
can already recover — which makes the outstanding gap cosmetic rather than
structural.

## Addendum: the 3.00 recovery PUP — CORRECTED

**An earlier version of this section claimed the recovery PUP payload was
encrypted. That was wrong.** The payload is *compressed*, and it decompresses
without any key.

The error was methodological and worth recording so it is not repeated:
entropy was measured (99.956% of 4 KB windows above 7.0 bits/byte) and read as
evidence of encryption. But **compressed data is incompressible too**, so
entropy cannot distinguish the two. The only sound test is to attempt
decompression, which was not done before drawing the conclusion.

Two details made the compression easy to miss, neither of which excuses
skipping the test:

- The chunks use CMF `0x48` — deflate with a 4 KB window — instead of the
  near-universal `0x78`. Scanning for `78 9c` / `78 da` zlib magic finds
  nothing.
- The segment table's field pairs read naturally as offset/size, but they are
  **compressed size / uncompressed size**: entry 2 is `0x85120 → 0x17C581`, a
  2.9× ratio, and entries where the two are equal are simply stored.

### What the payload actually is

`PS5UPDATE1.PUP.dec` is a run of independently zlib-compressed chunks, each
expanding to exactly 512 KB, laid end to end. Walking all 902 MB yields
**2,025 streams and 897 MB of output**, containing:

| Content | Evidence |
|---|---|
| exFAT filesystems | `eb 76 90 45 58 46 41 54` headers, x4 |
| An install manifest | full `/system_ex/...` path list |
| GNF textures | `GNF ` magic, `version=0xC0104` |
| XML and JSON metadata | e.g. `{"BdFirmIn…` |

`PS5UPDATE2.PUP.dec` yields 153 streams / 40 MB and two exFAT headers, but no
shell assets.

### A path correction this turned up

The manifest lists where the shell's resources actually live, and one is not
where this project assumed:

```
/system_ex/vsh_asset/Sce.PlayStation.PUI.rco
/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco
/system_ex/app/NPXS40087/psm/Application/resource/Sce.Vsh.ShellUI.BGLayer.rco
/system_ex/app/NPXS40087/psm/Application/resource/Sce.Vsh.ShellUI.Base.rco
```

`Sce.Vsh.ShellUI.BGLayer.rco` and `.Base.rco` are under the **application's
own `resource/` directory**, not `vsh_asset`. Any resolver probing only
`vsh_asset` for them will always miss.

### What still stands

The parts of this document above the addendum are unaffected: the shell eboots
on disk are already plaintext, 3.0x genuinely lacks the scene architecture
(`sequence_scene_builder` and `CreateLightShaftModel` appear only from 12.40),
and the published psdevwiki keys still do not decrypt anything. What changes is
that **the recovery PUP was never encrypted to begin with**, so its contents
were reachable all along.

Tools: [`pup_decompress.py`](../../tools/shell-recovery/pup_decompress.py) walks
the chunks and counts targets; [`pup_extract.py`](../../tools/shell-recovery/pup_extract.py)
carves files across chunk seams.
