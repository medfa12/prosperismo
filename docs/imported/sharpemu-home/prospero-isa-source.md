<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# The authoritative shader ISA is Sony's, not AMD's RDNA2 guide

## Use this source

```
games/gpu shit_forzen/
  GPU Shader Core ISA Instruction Reference - SDK 12.000/   48 PDFs
  GPU Shader Core ISA Specification - SDK 12.000/           80 PDFs
  _text/                                                    both, extracted, greppable
    GPU_Shader_Core_ISA_Instruction_Reference_-_SDK_12.000.txt   4.5 MB
    GPU_Shader_Core_ISA_Specification_-_SDK_12.000.txt           1.6 MB
```

These are Sony's own PS5 GPU Shader Core documents (SDK 12.000, marked SIE Confidential / AMD
Proprietary). Grep the `_text` dumps; all 48 + 80 source pages are represented in them, so an absence
in the text is an absence in the document.

**Do not treat `games/rdna2*op*code(...).txt` as ground truth.** Its own header says it was
"transcribed from AMD's public RDNA 2 Instruction Set Architecture Reference Guide (30-Nov-2020)",
and its filename already says to prefer the GPU folder. Prospero's shader core is not stock RDNA2 and
the two disagree in both directions.

## How the two disagree

Compared 1,121 entries in the RDNA2 table against 629 mnemonics extracted from the Sony reference.

### Prospero has instructions RDNA2 dropped

Confirmed present in the Sony reference with full usage entries, absent from the RDNA2 table:

| Instruction | Note |
|---|---|
| `v_mac_f32`, `v_mac_legacy_f32` | multiply-accumulate; RDNA replaced these with `v_fmac_*` |
| `v_mad_f32`, `v_mad_legacy_f32` | multiply-add, likewise superseded by FMA forms in RDNA |
| `v_madmk_f32`, `v_madak_f32` | MAD with literal constant |
| `s_dcache_wb`, `s_dcache_discard`, `s_dcache_discard_x2`, `s_atc_probe` | scalar cache control |

Our decoder already knows `MacF32`, `MadF32`, `MadmkF32` and `MadakF32`. It does **not** know
`MacLegacyF32`, `MadLegacyF32`, `DcacheWb`, `DcacheDiscard` or `AtcProbe` (zero hits across
`SharpEmu.ShaderCompiler*`).

### RDNA2 instructions with no trace in the Sony reference

These are the dangerous direction: if the opcode number exists on Prospero and means something else,
we decode it confidently and wrongly.

| Encoding | Opcode | RDNA2 name | Hits in Sony reference |
|---|---:|---|---:|
| VOP3P | 19-25 | `v_dot2_f32_f16`, `v_dot2_i32_i16`, `v_dot2_u32_u16`, `v_dot4_i32_i8`, `v_dot4_u32_u8`, `v_dot8_i32_i4`, `v_dot8_u32_u4` | 0 |
| VOP2 | 2, 13 | `v_dot2c_f32_f16`, `v_dot4c_i32_i8` | 0 |
| SMEM | 31, 36 | `s_gl1_inv`, `s_memtime` | 0 |
| VOP1 | 27, 65 | `v_pipeflush`, `v_clrexcp` | 0 |
| VOP2 / VOP3 | 6 / 320 | `v_fmac_legacy_f32`, `v_fma_legacy_f32` | 0 |
| VOP3 | 835 | `v_interp_p1lv_f16` | 0 |
| SOP1 | 34 | `s_rfe_b64` | 0 |
| SOPP | 11, 13, 18, 27, 35 | `s_setkill`, `s_sethalt`, `s_trap`, `s_endpgm_saved`, `s_waitcnt_depctr` | 0 |

**Correction (see the cross-check section below).** An earlier version of this file called the
`v_dot*` family "genuinely absent from Prospero". That was an over-claim. All the evidence shows is
that Sony's *document* never names them - documentation silence, not architectural absence. KytyPS5,
which targets PS5, maps VOP2 `0x02` to `v_dot2c_f32_f16` exactly as the RDNA2 table does, and so do
we. Treat the whole table above as **"undocumented by Sony"**, not "absent from the hardware", and do
not remove a mapping on the strength of it.

The scalar utility ops (`s_trap`, `s_setkill`, `s_sethalt`) are more equivocal: they are standard
parts, and Sony may simply have omitted debug/trap facilities from a developer document rather than
removed them from silicon. Treat those as UNVERIFIED rather than proven absent.

## The comparison trap - read this before repeating the diff

A naive name diff says **505 of our 1,121 entries are "missing"** from the Sony reference. That number
is almost entirely an artefact and must not be quoted.

The Sony document uses **templated mnemonics** where the RDNA2 table enumerates every variant:

```
Sony:   buffer_store_format_<dataType>      ds_read_<dataType>      s_load_<dataType>
        s_cbranch_<cond>                    image_<bvhPtrSize>_intersect_ray
RDNA2:  buffer_store_format_xyzw            ds_read_b32             s_load_dwordx4
        s_cbranch_scc0 ...
```

So `buffer_store_format_xyzw` scores 0 hits while being an instruction Astro demonstrably executes.
The inflated categories are exactly the templated families - DS (92), MIMG (88), MUBUF (73),
GLOBAL (57), VOPC (56), FLAT (54), SCRATCH (22), MTBUF (16), and the templated parts of SMEM and
SOPP. **Only plainly-named instructions give a meaningful absence signal**, which is why the table
above is restricted to those.

## Does any of this affect Astro today?

**No, not directly.** A traced Astro run contains zero `Vop3Raw*`, `Vop3bRaw*` or other
undecoded-opcode markers, so nothing in its shaders is currently hitting these gaps. Do not file this
against the black screen.

It is still worth fixing, because a wrong decode is silent by construction: an opcode number that we
map to the wrong instruction produces plausible SPIR-V and wrong pixels, with no diagnostic anywhere.

## Suggested use

1. When adding or changing any opcode mapping, cite the Sony reference, not the RDNA2 guide.
2. Before trusting an entry in `docs/ps5-shader-isa-audit.md`, check whether its claim came from the
   RDNA2 table. That audit predates this source.
3. If a decode gap is suspected at runtime, look for raw-opcode markers first - their absence is
   strong evidence the gap is not being exercised.
4. The encoding **bit-field diagrams** did not survive text extraction, but they can be **rendered and
   read**: `python -m pip install pymupdf`, then `python scripts/isa_page.py spec 25 5` writes a PNG.
   Use that for any field-width question; the text dumps answer name and semantics questions only.

## Field widths, read off the rendered diagrams

Verified against `Specification/25.pdf` pages 4-5. **Every one matches our decoder** - these were
checked, not assumed:

| Encoding | Sony diagram | `Gen5ShaderTranslator.cs` | Verdict |
|---|---|---|---|
| MIMG | `OP7` at [24:18] | `(word >> 18) & 0x7F` | correct |
| MIMG length | `NSA2`: 0/1/2/3 -> 64/96/128/160-bit | `sizeDwords = 2 + ((word >> 1) & 0x3)` | correct |
| MUBUF | `OP7` (plus an `OPM` bit) | `(word >> 18) & 0x7F` | correct |
| MTBUF | `OP3` at [18:16] | `(word >> 16) & 0x7` | correct |
| SMEM | `OP8` at [25:18], `OFFSET21` | `(word >> 18) & 0xFF` | correct |

**This refutes ISA-audit finding 7.** That finding claimed MIMG's opcode is 8 bits and that our 7-bit
decode aliases G16 samples to the wrong operation. Sony's own encoding diagram shows `OP7` - seven
bits. Our decode is right; the finding was derived from the AMD RDNA2 guide and is wrong for
Prospero. It has been removed from the open-items list in `astro-bot-boot.md`.

The same diagrams give the one real gap here a precise address: **MTBUF `FORMAT7` sits at bits
[25:19]** and `DecodeMtbuf` never reads it (`Gen5ShaderTranslator.cs:1686`). That is `(word >> 19) &
0x7F`.

DPP8/DPP16 (Data Parallel Primitives) are also documented encodings; we do handle them
(`Dpp8`, `Dpp16`, `RowMask`, `BankMask` all appear in the translator).

## Sony does not publish opcode numbers

Rendered `Instruction Reference/0003.pdf` (`s_add_i32`) to check. Each instruction page lists Usage,
**Encodings as a class name only** (`SOP2`, `SOP2 + literal constant`), Operation Summary,
Restrictions, Implicit R/W, Rate and Modes. There is no numeric opcode anywhere, and no opcode
appendix in the 48-page reference. Sony expects you to use their assembler.

So a wrong opcode *number* cannot be checked against this source at all - only field widths, names
and semantics can. The `v_dot*` risk below stays open by construction.

## Numeric cross-check against KytyPS5

Sony publishes no opcode numbers, so the only PS5-targeted numeric source is **KytyPS5**
(`inspiration/KytyPS5/src/graphics/shader/recompiler/`). PS4 emulators are deliberately **not** used
here - PS4 is GCN, a different architecture, and its numbering does not transfer. The decrypted
firmware in `games/PS5_4.03_reconstructed` is also not a source: a scan of 400 modules found no
shader mnemonics at all, which is expected since retail firmware ships no disassembler.

Reproduce with `python scripts/isa_kyty_crosscheck.py`.

| Encoding | ours | kyty | shared | disagree |
|---|---:|---:|---:|---:|
| SOP1 | 35 | 21 | 17 | **0** |
| SOP2 | 51 | 46 | 46 | **0** |
| SOPP | 37 | 15 | 15 | **0** |
| VOP1 | 35 | 48 | 34 | 1 |
| VOP2 | 42 | 48 | 37 | 6 |

**78 scalar entries agree exactly.** Every disagreement was then resolved against the RDNA2 table,
and in every case **our table is right and Kyty is wrong**:

| Slot | ours | kyty | resolution |
|---|---|---|---|
| VOP1 `0x2b` | `VRcpIflagF32` | `VRcpF32` | RDNA2: 42 = `V_RCP_F32`, 43 = `V_RCP_IFLAG_F32`. Kyty is off by one. |
| VOP2 `0x2b-0x2d` | `VFmac/VFmaMk/VFmaAk` | `VMac/VMadmk/VMadak` | RDNA2: 43-45 = `V_FMAC_F32`, `V_FMAMK_F32`, `V_FMAAK_F32`. Kyty maps MAC here **and** at `0x1f-0x21`, duplicating its own entries - a patched-in GCN name that never displaced the original. |
| VOP2 `0x25-0x27` | `VAddI32`, `VSubI32`, `VSubrevI32` | `VAddNcU32`, `VSubNcU32`, `VSubrevNcU32` | Same instructions, GCN-era vs RDNA naming. Cosmetic only. |

**Our VOP2 table is more correct than either single source.** It carries the legacy MAC block at
`0x1f-0x21` that Prospero retains and the RDNA2 guide lacks, *and* the FMAC block at `0x2b-0x2d` that
Kyty gets wrong. Sony's reference documents both `v_mac_f32` and `v_fmac_f32`, which is exactly what
that layout predicts.

So the numeric tables are in better shape than the name-level gap count suggests. The 227 missing
instructions in `prospero-isa-gaps.md` are *unimplemented*, not *mis-numbered* - a much safer failure
mode, because an unimplemented opcode refuses loudly while a mis-numbered one is silent.

**Kyty is a cross-check, not an authority.** It has two demonstrated errors above. Use it to flag
slots for review, then resolve each against the RDNA2 table plus Sony's instruction names and
semantics.
