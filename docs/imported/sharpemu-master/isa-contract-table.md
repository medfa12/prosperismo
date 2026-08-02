<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# The ISA contract table: Sony's own instruction semantics, machine-parsed and adversarially checked

`contracts/isa/instructions.tsv` is the ground-truth contract every shader ISA
work item is built against, and the input corpus for the T0.5 reference
interpreter. One row per instruction named in the table of contents of Sony's
**GPU Shader Core ISA Instruction Reference, SDK 12.000**; every populated cell
was mechanically parsed out of the 4.7 MB text dump of that document by
`scripts/isa_contract.py`, with a citation (`source_pdf`, `source_page`) and a
provenance tag. No cell was typed by hand and none was paraphrased. Where the
extraction destroyed a field beyond mechanical recovery, the cell is empty and
the loss is counted in the parser's anomaly report - an empty cell is a work
item, not an oversight. The companion `contracts/isa/README.md` covers columns
and regeneration; this document records what the table is, how it was checked,
and how it nearly lied before it was right.

## The numbers (measured, after the validation round)

- **Universe: 542 toc names, 542 rows.** 537 mnemonic-pattern names, 5
  bracketed `image_*[_mods]` templates, 2 `_SCE_BREAK()`/`_SCE_STOP()`
  debugger macros. An independent validator re-derived the universe from the
  toc pages alone and matched the row set exactly, both directions.
- **275 rows EXTRACTED, 267 TOC_ONLY.** The 48 captured PDFs contain only the
  Scalar ALU, Vector ALU, and Debug/Profiling chapters plus the toc. Every
  memory-family name (`ds_` 62, `buffer_` 11, `image_` 11, `tbuffer_` 4,
  `flat_`/`global_`/`scratch_` 14, `exp` 1), 43 program-flow `s_` names
  (`s_endpgm`, `s_branch`, `s_waitcnt`, ...) and 121 `v_` names (all f16
  arithmetic, `v_pk_*`, most `v_cvt_*`, `v_interp_*`) have zero body text
  anywhere in the dump. Their rows carry name and family only, per the
  never-guess rule. A validator grepped the full dump for body text of
  sampled TOC_ONLY names and found none, and confirmed zero EXTRACTED rows
  lack independent body evidence - the split is real in both directions.
- **Fill rates over the 275 EXTRACTED rows:** title, usage, category, modes,
  description and operation_summary 275 (100%); restrictions 274;
  encoding_family 273 (the two `_SCE_*()` macros genuinely have no Encodings
  section); rate 269 (six Debug entries print a lone space); implicit_rw 102
  (the label itself is absent for the rest in the source);
  operation_pseudocode 39 (only ~14% of entries carry an Operation Details
  block; four `s_*_saveexec`/`s_*_wrexec` rows have the label but no block -
  their semantics are the summary plus the variants table); variants_table
  23 of 23 templated entries with tables, all now carrying both the
  `<param>Operation` rows and the `Mnemonic Description` rows.
- **Variant expansion:** the 22 templated compare/mask rows expand via
  `variant_mnemonics` to 177 concrete mnemonics with per-variant
  expressions, giving roughly 429 concrete mnemonics with machine-derived
  semantics.
- 14 rows still carry cross-copy field disagreements resolved by majority
  vote across up to 20 duplicate captures (down from 29 before the fixes -
  half of the "disagreements" were one pagination window being corrupted).

## How it was extracted, in one paragraph

The source is a website text-dump that lies to adjacency-based tools: chapters
duplicated up to 20 times, two-stream pages whose section labels sit in the
main flow while content floats to the page bottom, entries and code blocks
split across page footers, variant tables printed with glued columns and
labels displaced a page from their rows, and page furniture interleaved with
content. The parser reconstructs association with a per-page main/float split,
a FIFO of pending content slots zipped positionally against float blocks,
brace-balance rejoining of split pseudocode, per-mnemonic majority voting
across duplicate captures, and exact-match furniture stripping. The docstring
of `scripts/isa_contract.py` enumerates all seven hazard classes with the
countermeasure for each.

## How it was validated

Three independent adversarial passes ran against the shipped table:

1. **Field fidelity** - a hostile sample (longest pseudocode blocks, first and
   last row of every source PDF, every entry the extractor admitted was hard,
   unusual characters, all 22 variant tables scanned programmatically, plus
   two whole-table furniture scans), each row diffed field-by-field against
   its cited page. Verdict: no fabrication anywhere - every wrong cell traced
   to real displaced source lines - but 10 defects, clustered in three
   mechanisms (below).
2. **Universe coverage** - a full-universe check with an independently derived
   toc scan and three body-evidence signals the extractor did not use.
   Verdict: zero defects; 542/542 both directions; no duplicate, furniture,
   or fabricated rows; every citation valid.
3. **Compilability (T0.5 feasibility)** - all pseudocode blocks and 55
   summaries pulled verbatim and fed to a prototype grammar and interpreter.
   Verdict: the language is a small C dialect and the approach works, with a
   list of cells that were not self-contained - all of which traced back to
   the same fidelity mechanisms.

Every confirmed parser defect was fixed in `scripts/isa_contract.py` (never in
the TSV - the table is generated output) and the table regenerated. Exactly 22
rows changed, all of them defect sites, and each changed cell was re-verified
against its source page.

## The four ways the extraction nearly lied

This is the section worth reading. Like the compliance comparison before it
(`prospero-isa-gaps.md`, "the three ways it lied first"), the extractor's
first shipped output was structurally sound and wrong in specific,
systematic ways. All four mechanisms produced cells that LOOKED parsed -
which is exactly the failure mode this table exists to prevent.

1. **Operation Summaries are blocks, not lines.** The FIFO filler assumed one
   float line per summary slot. `v_div_scale_f32`'s summary wraps over three
   lines; the filler took line one, misfiled the rest into the pseudocode
   slot, and `v_div_scale_f64` lost its middle line (`768) & EXEC ;`)
   entirely - the only outright content loss found. The same mechanism
   truncated `v_mbcnt_hi/lo_u32_b32` (leaving a dangling expression fragment
   at the head of their pseudocode), `v_sub_co_ci_u32` (whose pseudocode cell
   shipped as just `vdst.u = result & 0xffffffff`, reading a `result` that
   was never defined), and the whole `v_add_co*`/`v_sub_co*`/`v_subrev_co*`
   family. Fix: a summary line ending mid-statement (dangling binary
   operator, `?`, a `;` separator promising another statement, or open
   parens) keeps ownership of the next float line, across page boundaries,
   even when a details slot is queued behind it. Postfix `++`/`--`
   (`s_incperflevel`) are complete and excluded.
2. **Emulation-sequence idioms are shaped like usage lines.** Sony documents
   compiler macros inline (`v_mul_f32 vrcpf, vrcpf, #h4f7fffff`). Displaced
   into the float stream, these asm example lines filled whatever slot was
   open: `v_rsq_f32`'s summary shipped with three lines of
   `v_rcp_iflag_f32`'s integer-reciprocal macro in front of the real
   `vdst.f = Rsqrt(vsrc.f)` - a compiler consuming that cell would have
   executed another instruction's usage idiom as `v_rsq_f32` semantics. The
   same drift knocked out `v_rsq_f32`'s and `v_trig_preop_f64`'s encoding
   lists (the misfiled line popped the pending encodings slot, orphaning the
   ruler that followed) and polluted `v_add_co*`/`v_sub_co*` pseudocode with
   asm lines and a bare `...`. Fix: mnemonic-shaped operand lines without
   `=` and bare `...` in the float stream are dropped and counted
   (`emulation_seq_dropped`, 19 unique lines), never allowed to fill or
   continue a slot. They are description examples the two-stream format
   makes unanchorable; dropping them with a count is the honest option.
3. **`<param>Operation` table rows pass for pseudocode.** The variant tables
   of `s_pack_<select>_b32_b16` and the four `s_*_saveexec`/`s_*_wrexec`
   entries have rows (`ll (ssrc1[15:0] << 16 | ssrc0[15:0])`,
   `and EXEC = (ssrc.du & EXEC)`) that match no glued-row regex, so they fell
   through to the pseudocode slot: five rows shipped with table rows as
   leading pseudocode lines and variant tables holding an orphaned header.
   Fix: the variant token set is derived mechanically from the entry's own
   usage lines (diffing `s_pack_ll_b32_b16` against the template yields
   `ll`), and a float line whose first token is in that set is a table row.
   The four saveexec/wrexec pseudocode cells are now correctly EMPTY - the
   source has an `Operation Details:` label with no block behind it.
4. **Prose that belongs to nobody.** Chapter-end "In This Chapter" nav boxes
   leaked 18 lines of section names into `v_cvt_f64_i32`'s description (the
   last entry of its chapter, so every duplicate capture agreed and majority
   vote could not save it). Section-intro paragraphs after a category header
   leaked into the previous entry's description: `v_perm_b32` absorbed the
   Conversion Operations preamble, and `v_cmpx_<compareOp>_f64` absorbed two
   paragraphs of the 32-bit Integer Arithmetic intro - a contamination none
   of the three validators caught; the fix's mechanism found it. Fix: runs
   of 4+ category-shaped lines are nav furniture (real content never has
   them; labels, encoding classes and headings break any such run), and
   prose after a category header until the next heading is section intro.
   Both are dropped and counted, because the table has no row they belong
   to.

One repair cascade is worth recording as method: mechanism 2's fix alone
repaired `v_trig_preop_f64` completely (summary, encoding family, dword
count) because the original corruption was a chain - an asm line popped a
pending slot, which orphaned a ruler, which made encoding classes fill
summary slots two entries downstream. Displacement bugs in FIFO
reconstruction are never local.

## Reported defects rejected, with reasons

These were flagged by validators and deliberately NOT fixed, because the
table's contract is byte-fidelity to the source and the source itself is
wrong or idiosyncratic. A consumer (the T0.5 compiler) must normalize these;
the oracle must not.

- **`s_lshl<num>_add_u32` has unbalanced parens** in its pseudocode
  (`if ( (ssrc0.u << instruction_bitshift) + ssrc1.u) > 0xffffffff)`). All
  duplicate captures agree: it is Sony's typo. Kept verbatim.
- **`v_perm_b32` contains `felse`** (for `else`). Sony's typo, in every copy.
  Kept verbatim.
- **En dash (U+2013) as minus** and **`2^x` as power** in several cells:
  Sony's notation. Kept byte-for-byte; the prototype tokenizer normalizes
  both on its side.
- **`v_cmp_<compareOp>_u32`'s variant table lists signed-comparison
  expressions** - a genuine Sony copy-paste bug on the page. The table
  reproduces it, because inventing the "obviously intended" unsigned forms
  is precisely the hallucination this table exists to catch.
- **Templated summaries pass operands as `<compareOp>(vsrc1, vsrc2)` while
  variant expressions use `vsrc0/vsrc1`**: real, but a consumer-side
  positional-remapping concern, demonstrated working in the prototype.
- **Roughly 10 intrinsics are approximation functions** (`Rcp`, `Rsqrt`,
  `Sin`, `trig_preop_scale_f64`, ...) that the document names but never
  defines bit-exactly. That is the semantic ceiling of the source itself;
  differential tests against these need ULP tolerance bands from the ISA
  Specification volume, not bit equality.

## What the table deliberately omits

- **Opcode numbers.** The document contains none (encodings survive only as
  class names and dword-width rulers). Numbers must come from a separate
  numeric source and must never be written into this table
  (`docs/prospero-isa-source.md`).
- **Semantics for the 267 TOC_ONLY rows.** Not a parser failure; the
  chapters were never captured. This includes every f16 arithmetic op - the
  gap `prospero-isa-gaps.md` ranks highest - so no amount of parser work on
  this dump can produce an f16 oracle. The table contributes the f16
  conversion formulas (`v_cvt_norm_*`/`v_cvt_pknorm_*`) and nothing else in
  that tier.
- **The `vftypemask` bit-class tables** of the four `v_cmp*_class_*` entries
  (`[0:] Signaling NaN (sNaN)` ...): displaced between a heading and its
  Usage label where no mechanical anchor exists. Dropped and counted rather
  than guessed - the one known in-chapter loss that survives the fixes.

## T0.5 verdict: build it, but re-aim it

The reference interpreter is real and cheap for the half of the ISA this
table covers, and impossible from this table for exactly the tier T0.5 was
motivated by. The pseudocode is a small C dialect (assignments, unbraced
`if`/`else`, C `for`, ternary, bit slices `reg[hi:lo]`, type-suffix
accessors, `{a,b}` concatenation); after the fixes a ~200-line prototype
grammar parses 221 of 275 semantic rows verbatim (was 214/274 before the
fixes - seven cells became self-contained), and the stdlib-only prototype
interpreter (tokenizer, Pratt parser, typed-register evaluator, reading
cells LIVE from the TSV with zero hand-transcribed semantics) executed
`s_add_u32`, `s_addc_u32`, `s_and_b32`, `v_bfe_u32` and `v_add_f32` over
seven operand vectors, all PASS, plus four mechanically harvested
`s_cmp_<compareOp>_i32` variants, all PASS. Realistic production ceiling:
roughly 250 of 275 semantic rows, expanding to ~429 concrete mnemonics, of
which ~40 depend on tolerance-band approximation intrinsics; 57 intrinsics
need hand-writing once each (about 45 exactly definable, the rest
approximate), plus a small wave-state model (EXEC/VCC/SCC, `thread_id`,
lane-indexed access). The highest-value first slice is NOT f16 (absent from
the source): it is SALU integer/bitwise/SCC (~100 mnemonics, near-100%
parseable, the carry/borrow/overflow semantics all shader control flow
depends on) plus the 16 compare families expanded to 177 concrete
`s_cmp`/`v_cmp`/`v_cmpx` mnemonics with EXEC masking - which is where SPIR-V
lowering bugs actually hide, and covers roughly a third of the decoder's
opcode keys with bit-exact oracles. For f16 arithmetic the tracker item
should say: encoding and usage contracts from this table, semantics from a
different source (the missing VALU-f16 chapters, or the ISA Specification's
mode rules plus IEEE-754 binary16).

## Drift note

`scripts/isa_compliance.py` output moves as the decoder grows: on 2026-07-31
it printed 227 not-decodable of 540 against 668 decoder keys. That is decoder progress,
not a property of this table. The 542 vs 540 universe difference is the two
`_SCE_*()` macros this table admits and the compliance prefix list excludes.

## Regenerating and re-verifying

```
python scripts/isa_contract.py            # rewrites contracts/isa/instructions.tsv
python scripts/isa_contract.py --stats    # + per-anomaly detail (every dropped line is listed)
```

The parser is deterministic and stdlib-only; regeneration is byte-stable.
Never hand-edit the TSV: fix the parser and regenerate, then diff the TSV
row-by-row and re-verify every changed cell against its cited source page -
that discipline is what caught the four mechanisms above.
