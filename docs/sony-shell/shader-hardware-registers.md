<!--
Copyright (C) 2026 Prosperismo Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# The hardware register set, per shader, from the firmware

Every background shader in the 12.40 eboot has a descriptor —
`{name, header, code, reflection}`, 0x20 stride, first entry at vaddr
`0x113EFB0` — and the header ends in a flat `(register, value)` pair list.
Those are the registers the driver programs for that shader.

The descriptor fields are pointers filled by `R_X86_64_RELATIVE` relocations,
so they read as **zero** in the file. Searching for the code addresses as data
finds nothing; they have to be resolved from the RELA table. That is why this
table went unnoticed until now.

```
python3 tools/dump_shader_registers.py --eboot ps5oracle/fwdb/12.40/NPXS40087-eboot.bin
```

Every `code` pointer in the table matches `FirstWaveProbe.Stages` exactly, which
is the check that the descriptor layout was read correctly.

## Why this matters

Host state that had to be inferred from the instruction stream — or guessed —
is now readable. Four values this repository had already derived are confirmed
**exactly**, and one was wrong.

| value | derived from | firmware says |
|---|---|---|
| `fw_background_p` pixel inputs `0x302` | the shader reads `FragCoord` from `v2`/`v3` | `SPI_PS_INPUT_ENA/ADDR = 0x302` ✅ |
| `particle_p` needs 6 interpolants | `particle_vv` exports `param0..param5` | `SPI_PS_IN_CONTROL.NUM_INTERP = 6` ✅ |
| `particle_p` user data is 4 words at `s0` | its `s_buffer_load` uses `s[0:3]` | `RSRC2.USER_SGPR = 4` ✅ |
| `particle_vv` user data starts at `s8` | `s3` is NGG merged wave info; `s[0:3]` is overwritten from the resource block | `RSRC2.USER_SGPR = 4`, and the GS-stage merged wave reserves `s0`–`s7` for system values, so user data is `s[8:11]` ✅ |
| `particle_p` pixel inputs | not derived — left at 0 | `SPI_PS_INPUT_ENA/ADDR = 0x2` ⚠️ now corrected in the probe |

The `0x302` agreement is the strongest of these: it was reasoned out of the
shader's VGPR reads before this table was found, and the firmware programs that
exact word.

## Pixel stages

| shader | `PS_INPUT_ENA`/`ADDR` | `NUM_INTERP` | `USER_SGPR` | `DB_SHADER_CONTROL` |
|---|---|---:|---:|---|
| `fw_background_p` | `0x302` | 0 | 4 | `0x00000010` |
| `fw_oit_p` | `0x302` | 5 | 12 | `0x00020600` |
| `fw_comp_oit_p` | `0x302` | 0 | 12 | `0x00020600` |
| `fw_fxaa_p` | `0x002` | 1 | 12 | `0x00000010` |
| `light_p` | `0x002` | 1 | — | `0x00000010` |
| `particle_p` | `0x002` | 6 | 4 | `0x00000050` |
| `large_particle_p` | `0x002` | 5 | 4 | `0x00000050` |
| `shutdown_correction_p` | `0x002` | 1 | 12 | `0x00000010` |

All eight use `SPI_SHADER_COL_FORMAT = 4`, `CB_SHADER_MASK = 0xF`,
`SPI_BARYC_CNTL = 0x01000000` and `SPI_SHADER_Z_FORMAT = 0`.

`large_particle_p`'s five interpolants match
[`particle-draw.md`](particle-draw.md)'s "five parameter records" for
`large_particle_vv`, from a completely different source.

## The tessellated wave surface

The three flow stages carry what the surface needs, and one pair settles the
subdivision from the host side:

| register | `fw_flow_h` value |
|---|---|
| `0x286` | `0x41400000` = **12.0f** |
| `0x287` | `0x3F800000` = **1.0f** |

Those are the tessellation level clamps — max 12.0, min 1.0. The hull shader
writes six factors of `12.0` from an inline constant
([`firstwave-plate-executed.md`](firstwave-plate-executed.md)), and the driver
independently programs a maximum of 12.0. **Two unrelated sources, one answer.**
The uniform 12×12 subdivision is now settled from both sides.

Also present for the flow stages:

- `fw_flow_h`: `RSRC1_HS = 0x312C0082`, `RSRC2_HS = 0x003C0084`, plus
  `0x2DB = 0x00040042` and `0x2D6 = 0x0004100F`.
- `fw_flow_vl`: `RSRC1_HS = 0x3000000A`, `RSRC2_HS = 0x0000000C`
  (`USER_SGPR = 6`), and `0x14A = 0x0300000A`.
- `fw_flow_dv`: `RSRC1 = 0x622C01E4`, `RSRC2 = 0x0007000C` (`USER_SGPR = 6`),
  `SPI_VS_OUT_CONFIG = 8`, `SPI_SHADER_IDX_FORMAT = 4`,
  `VGT_ESGS_RING_ITEMSIZE = 4`, `0x291 = 0x10020040`.

`particle_vv` and `large_particle_vv` are the same family:
`SPI_VS_OUT_CONFIG` `0x0A` and `0x08`, `RSRC1` `0x622C00C7` and `0x622C00C3`,
`RSRC2` `0x00030008` — four user SGPRs, which lands the SRT at `s[8:11]`.

## What is still not settled

Field-level decoding of `RSRC1`/`RSRC2`/`0x2DB` beyond `USER_SGPR` is not
attempted here — the values are recorded exactly, but naming their bitfields
would need the GFX10 register reference rather than inference, and this file
does not guess. `0x14A`, `0x291`, `0x2D5`, `0x2D6` and `0x25B` are likewise
recorded and unnamed.
