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
