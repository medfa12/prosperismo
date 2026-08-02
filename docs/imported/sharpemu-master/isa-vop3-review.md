<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# isa-vop3 review (external, REQUEST_CHANGES - all findings since addressed)

Recovered from an untracked file in the `isa-vop3` worktree before it was removed. Verdict was
**REQUEST_CHANGES**; all three MAJOR findings were fixed by `d45be64` and verified against the
merged tree:

- finding 5, promoted `v_readfirstlane_b32` decoded with a vector destination - fixed, the branch
  now treats `VReadfirstlaneB32` like `VReadlaneB32`.
- finding 7, Metal wave64 borrow-in read the wrong mask word for lanes 32-63 - fixed, the selector
  now picks `s[N+1]` above lane 32.
- finding 10, the new tests assembled `0x34 << 26` where GFX10 VOP3 is `0x35 << 26` - fixed, and
  the tests now carry a comment naming the canonical encoding.

Kept because it is the only record of why those changes exist.

---

1. **OK â€” [Gen5ShaderTranslator.cs:1523](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:1523)** â€” The range boundaries and switch ordering are correct. `< 0x100` handles VOPC; the exclusions precede `>= 0x100 and < 0x140`; VOP1 is limited to `>= 0x180 and < 0x200`. Therefore explicit cases in `0x140â€“0x17F` and `>= 0x200` remain reachable, while none is accidentally captured by a relational pattern. This agrees with AMDâ€™s documented opcode offsets. [AMD RDNA2 ISA](https://docs.amd.com/api/khub/documents/Et~wpu9g~Ffl7d9q0QZ~Og/content)

2. **OK â€” [Gen5ShaderTranslator.cs:1528](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:1528)** â€” The exclusions are correct for every nonempty entry currently in `Vop2Name`: `madmk/madak`, `fmamk/fmaak`, and `dot2c` must not be promoted. The full ISA has additional VOP2-only rows, including the FP16 literal forms and `dot4c`, but those base opcodes are not present in `Vop2Name`, so they already resolve to an empty name and fail safely rather than being misdecoded.

3. **OK â€” [Gen5ShaderTranslator.cs:1591](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:1591)** â€” The ten-entry VOP3B set is complete for GFX10: the three promoted carry/borrow VOP2 rows at `0x128â€“0x12A`, plus `div_scale_f32/f64`, `mad_u64_u32`, `mad_i64_i32`, and the three standalone carry-out rows at `0x30F/0x310/0x319`. None of the listed opcodes is VOP3A. [AMD RDNA2 ISA](https://docs.amd.com/api/khub/documents/Et~wpu9g~Ffl7d9q0QZ~Og/content)

4. **OK â€” [Gen5ShaderTranslator.cs:2341](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:2341)** â€” For VOP3-encoded VOPC, the low `vdst` byte is indeed repurposed as the scalar mask destination; it is not the VOP3B `sdst` field at bits 14:8. Treating VCMPX as writing EXEC only and ignoring the decoded destination at emission is also correct, although canonical assembly normally puts `EXEC_LO` in that field.

5. **MAJOR â€” [Gen5ShaderTranslator.cs:2349](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:2349)** â€” Promoted `VReadfirstlaneB32` (`0x180 + 0x02 = 0x182`) is decoded with a vector destination. The branch only special-cases `VReadlaneB32`, whereas the ordinary VOP1 path correctly makes `VReadfirstlaneB32` scalar at line 2208. Both backends require a scalar destination and consequently reject a legal VOP3-encoded `v_readfirstlane_b32` as â€œinvalid read-first-lane operands.â€ AMD specifies that VOP1 instructions may use the `+0x180` VOP3 form and that this instruction writes an SGPR. [AMD RDNA2 ISA](https://docs.amd.com/api/khub/documents/Et~wpu9g~Ffl7d9q0QZ~Og/content)

6. **OK â€” [Gen5SpirvTranslator.Alu.cs:1954](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.Alu.cs:1954)** â€” The compare-destination selection is sound in both backends. VOP3 cannot simultaneously carry SDWA control, and the singleton scalar-destination pattern is evaluated only inside compare emission, so it cannot accidentally capture a VOP3B carry instruction whose normal destination is a VGPR. Falling back to VCC for ordinary VOPC is correct.

7. **MAJOR â€” [Gen5MslTranslator.Alu.cs:356](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler.Metal/Gen5MslTranslator.Alu.cs:356)** â€” The subtraction operand order and borrow formulas are correct, but Metal reads an arbitrary VOP3B carry-in pair incorrectly in wave64. `MaskBitExpression` at line 1613 always evaluates `s[N] >> sharpemu_lane`; lanes 32â€“63 must instead read `s[N+1]` and shift by `lane-32`. Thus the newly wired `VSubCoCiU32` and `VSubrevCoCiU32` produce wrong upper-half results whenever `src2` is an ordinary SGPR pair. Vulkanâ€™s 64-bit pair load does not have this bug. AMD explicitly defines VOP3 carry-in as the SGPR pair named by `src2`. [AMD RDNA2 ISA](https://docs.amd.com/api/khub/documents/Et~wpu9g~Ffl7d9q0QZ~Og/content)

8. **OK â€” [Gen5ShaderTranslator.cs:1529](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs:1529)** â€” I found no opcode-row aliasing among the reused name-table entries. The GFX10 VOP3 rows in these ranges are defined by the base opcode plus `0x100` or `0x180`; names such as the translatorâ€™s `VAddI32` versus the ISAâ€™s `V_ADD_NC_U32` are naming differences with the same no-carry arithmetic. MAC/FMAC also retain their tied destination-as-accumulator semantics rather than becoming an unrelated three-source operation.

9. **OK â€” [Gen5ShaderIr.cs:419](/C:/sharpemu-workers/isa-vop3/src/SharpEmu.ShaderCompiler/Gen5ShaderIr.cs:419)** â€” The control-pattern order is correct: VOP3B and SDWA scalar destinations are obtained from control first, while VOP3A compares use their scalar `Destinations` entry. Marking `register+1` is appropriate for a possible wave64 mask and is bounds-checked. VCMPX causes conservative over-marking of its decoded-but-ignored field, but that only prevents scalar folding and does not change shader semantics.

10. **MAJOR â€” [Gen5Vop3PromotionTests.cs:58](/C:/sharpemu-workers/isa-vop3/tests/SharpEmu.Libs.Tests/ShaderCompiler/Gen5Vop3PromotionTests.cs:58)** â€” Both `Vop3` and `Vop3b` assemble `(0x34u << 26)`, but GFX10 VOP3A/B requires encoding bits `110101`, i.e. `0x35 << 26`. Every test therefore uses a non-GFX10 VOP3 first word and passes only because the production decoder already accepts both `0x34` and `0x35`; `AssertDecodes` checks the same decoder and cannot independently validate the encoding. The focused suite passes 10/10 despite this. Additionally, the â€œunknown opcodeâ€ at line 386 is actually valid opcode `0x14C`, `V_FMA_F64`; it is merely unimplemented by this decoder. [AMD RDNA2 encoding tables](https://docs.amd.com/api/khub/documents/Et~wpu9g~Ffl7d9q0QZ~Og/content), [LLVM GFX10 VOPC e64 encodings](https://raw.githubusercontent.com/llvm/llvm-project/main/llvm/test/MC/AMDGPU/gfx10_asm_vopc_e64.s)

**REQUEST_CHANGES** â€” Legal VOP3 `v_readfirstlane_b32` instructions fail translation, Metal wave64 borrow-in is incorrect, and the new regression tests do not use the actual GFX10 VOP3 major encoding.