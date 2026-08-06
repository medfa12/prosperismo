<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Exactly which assets the background needs

Recovered from the absolute paths the 12.40 shell eboot names. This replaces
earlier guesses — including this project's own assumption that the room geometry
might be built in code.

**Everything lives in `/system_ex/vsh_asset/`**, a directory absent from every
dump on hand. See [`firmware-decryption-not-needed.md`](firmware-decryption-not-needed.md):
it is missing because no dumping tool walks it, not because it is encrypted.

## The one file that matters most

```
/system_ex/vsh_asset/cis_ac_model.wad
```

`cis_renderer` is the background renderer (its build paths are under
`vsh\shell\shell_ui\src\background_layer\cis_renderer\`), so **`cis_ac_model.wad`
is the background scene archive**. The relative paths the eboot names alongside
it are its contents:

| Inside the archive | What it is |
|---|---|
| `scenes/scene.sbs` | the scene graph |
| `scenes/scene_leonardo.sbs` | a second scene |
| `animations/skeleton/` | skeletal rigs |
| `animations/clipset/` | animation clips |
| `shaders/effect_light_shaft.spk` | **the volumetric light rays** |
| `shaders/effect_light_ring.spk` | the light ring |
| `shaders/maya_lambert.spk`, `layered_lambert.spk`, `maya_ramp_shader.spk` | materials |
| `shaders/model_render_basic.spk`, `model_render_depth.spk` | model passes |
| `shaders/device_shader.spk` | device pass |

This single archive is the room, its lighting, its animation and its shaders.
Nothing else recovered so far substitutes for it.

## Complete requirement

**Essential to the animated background:**

| Path | Role |
|---|---|
| `/system_ex/vsh_asset/cis_ac_model.wad` | the scene: geometry, animation, shaders |
| `/system_ex/vsh_asset/Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` | particle sprite |
| `/system_ex/vsh_asset/Sce.Vsh.ShellUI.BGLayer.Particle1.gnf` | particle sprite |
| `/system_ex/vsh_asset/shutdown_ramp.gnf` | shutdown colour ramp |
| `/system_ex/vsh_asset/shutdown_lightbar.fbxd` | shutdown light-bar model |

**Shell resources, under the app rather than `vsh_asset`:**

```
/system_ex/app/NPXS40087/psm/Application/resource/Sce.Vsh.ShellUI.BGLayer.rco
/system_ex/app/NPXS40087/psm/Application/resource/Sce.Vsh.ShellUI.Base.rco
```

**Not needed for the background** (PSVR2 play-area editor, listed so nobody
chases them): `heightMark.gnf`, `heightMark_feet.gnf`, `hmdMark.gnf`,
`indicator_L/R/arrow.gnf`, `Add_Mode_Active/Idle.gnf`,
`Erase_Mode_Active/Idle.gnf`, `all_symbols_2x.gnf`, `cis_vr_model.wad`,
`bglayer_haptics.pak`.

`diffuse_default.gnf` is a fallback material texture and is substitutable.

## What this changes

The four `.gnf` files this project has been treating as the outstanding gap were
never the whole story. They are **textures**; `cis_ac_model.wad` is the
**scene**. A dump lacking the `.wad` cannot produce the room and light shafts no
matter how faithfully the shader maths is recovered.

Conversely, the `.wad` plus the two particle textures would be sufficient —
the state machine, transitions and light-mode dispatch are already decoded
(see [`background-state-machine.md`](background-state-machine.md)), and the
shader toolchain is proven to run on macOS
(see [`macos-background-poc.md`](macos-background-poc.md)).

## Where to get them

A filesystem dump of `/system_ex/vsh_asset` from a jailbroken console on
**12.40 or later** — 3.0x predates the scene architecture entirely. The update
PUPs will not serve: their executables are SELF-encrypted, and their resources
do not include `vsh_asset`.

## Version note

These paths are read from 12.40. `cis_ac_model.wad` does **not** appear in the
3.00 eboot, consistent with the scene renderer being introduced after 3.0x.
