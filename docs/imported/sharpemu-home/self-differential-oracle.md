<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# The self-differential firmware oracle

We hold 565 cleartext PS5 4.03 modules. For any function we implement in HLE, Sony's real
implementation is on disk. This runs Sony's body inside our own emulator, runs our HLE export against
byte-identical guest state, and compares the two. The answer does not come from us.

```
SharpEmu.exe --fw-oracle --cases=oracle/cases/<library>/<nid>.json [--out=<dir>] [--isolate]
py -3 scripts/fw_oracle_gate.py            # all case files, rollup into oracle/runs/summary.json
py -3 scripts/fw_oracle_body.py --module <path.sprx> --nid <NID>   # author a new case file
```

Exit codes: `0` every case met its declared expectation, `1` an unexpected DIVERGENCE, `2` an
unexpected INCONCLUSIVE, `3` the NID is not gateable at all, `4` the harness itself faulted, `5` a
usage or case-file error. The whole thing is one file, `src/SharpEmu.CLI/FirmwareOracle.cs`, plus a
switch in `Program.cs`. It boots no title, needs no eboot and no Vulkan, and adds no instrumentation
to the CPU layer.

## What it proved on 2026-07-27

Five case files. 17 cases ran over four `libSceAgc` functions and a fifth module was refused whole,
all against 4.03 `libSceAgc.sprx`
(sha256 `3384E77AACE68C3DB16D9542FF9D74127089A76BE6C6A717E89E0326ECA69C4E`) except the last.

| NID | function | cases | result | exit |
|---|---|---:|---|---:|
| `V++UgBtQhn0` | `sceAgcGetDataPacketPayloadAddress` | 5 | 5 MATCH | 0 |
| `JOWmDrl+j20` | (unimplemented, negative control) | 2 | 2 declared INCONCLUSIVE | 0 |
| `Yw0jKSqop+E` | `sceAgcDcbDrawIndexAuto` | 6 | 5 DIVERGENCE, 1 declared INCONCLUSIVE | 1 |
| `dolOmWH+huQ` | `sceAgcDriverValidateDcbRange` | 4 | 4 DIVERGENCE | 1 |
| `j4ViWNHEgww` | `strlen` (libSceLibcInternal) | - | refused, module fails the syscall gate | 3 |

**`sceAgcGetDataPacketPayloadAddress` agrees, and that is the run that says the harness is not
broken.** Sony's body and `AgcExports.cs:1222` leave byte-identical arenas on all five vectors. Note
what this control does *not* test: both exit paths of the firmware body run `xor eax,eax` before
`ret`, so RAX is unconditionally 0 and the return channel has no discriminating power here. The real
output is the pointer stored to `[rdi]`, and each case line reports `wrote=HLE:8/LLE:8`, so eight
bytes really moved on both sides. A MATCH where both sides wrote nothing would prove nothing, which
is exactly why the byte counts are printed.

**`sceAgcDcbDrawIndexAuto` disagrees on every scored vector, and the mechanism is measured.** At
arena+0x100 Sony writes header `00 2D 01 C0` (PM4 type 3, count 1, opcode 0x2D `IT_DRAW_INDEX_AUTO`);
we write `10 10 05 C0`, a 7-dword `IT_NOP` carrying our private `RDrawIndexAuto` register tag. Sony
writes initiator `0x02` (`DI_SRC_SEL_AUTO_INDEX`) at +0x108; we write 0. Sony advances the DCB cursor
by 0x0C, we advance it by 0x1C. Three further findings, each new:

- With `modifier == 0` we return 0 and write nothing while Sony emits a full packet. Our
  `modifier != 0x4000_0000` guard rejects an input Sony accepts.
- With `modifier` bit 32 set Sony's `bt rdx,0x20` / `cmovae` path still yields initiator 2; we return 0.
- With a cursor at arena+0x102 Sony returns `0x60501104` because it does `add rcx,3; and rcx,~3`. We
  return `0x60501102` and emit the packet unaligned. `TryAllocateCommandDwords` never dword-aligns.

**`sceAgcDriverValidateDcbRange` disagrees 4 of 4, and kills a comment.** Sony dereferences `rsi` and
`rdx` as structure pointers, reading type tags at `+0x5a`. On the accepted pair 5/7 it writes
`[rdi]=0x80` and `[rdi+8]=4`; we write 24 zero bytes. On the rejected pairs it returns `0x8A6C0008`
and writes nothing while we return 0 and zero 24 bytes. The comment at `AgcExports.cs:3504` calling
`rsi`/`rdx` "command-range begin/end gpu-va" is wrong, and the differential proves it.

**None of these bugs are fixed.** `AgcExports.cs` is untouched by this work. Our submitted-DCB parser
at `AgcExports.cs:4247` is built around the private `IT_NOP` convention, so changing the emitter
without changing the parser breaks the emulator, and nobody has checked whether our GPU path can
consume a real `IT_DRAW_INDEX_AUTO`.

## Why you should believe the firmware side is real

Four independent results, three of them from the audit of this harness rather than from its author:

1. Flip one byte inside the body (`add rsi,8` to `add rsi,0x10`) and exactly the two cases that
   execute that instruction diverge, with the LLE side moving to `10` while the HLE side stays at
   `08`. The three cases that branch around it still MATCH.
2. Overwrite the body's first byte with `0xC3` and every case diverges: the LLE arena keeps its
   poison pattern while the HLE arena shows real writes. If the LLE side had ever been a mirror of
   our HLE, or a cache, the baseline MATCH could not have existed.
3. On `JOWmDrl+j20`, a NID with no HLE implementation at all, the firmware side returns `0x8` for
   `rdi=0` and `0x0` for `rdi=0x3FFFFFFE`. Those are exactly the values `lea eax,[rdi*4+8]` produces,
   predicted from the disassembly before the run.
4. Corrupt our HLE instead and 5/5 MATCH becomes 5/5 DIVERGENCE, with the return channel and the
   memory channel firing independently and only on the cases that reach the corrupted line.

The harness also refuses to execute bytes it cannot vouch for. Before any case runs it reads the
whole `st_size` body out of guest memory at the address it is about to call and compares it against
`body_hex` in the case file, which is EXTRACTED from the module by `scripts/fw_oracle_body.py`. A
mismatch is `I7_BODY_BYTES_MISMATCH`, exit 3.

## The four ways this comparison lied, or would have

The first version of this harness passed its own controls and was still wrong in four places. All
four were found by adversarial audit, not by running it more.

1. **It verified 24 bytes of a 44-byte body.** `scripts/fw_export_bodies.tsv` stores a 24-byte prefix
   in its `body_hex` column, and the case files copied that prefix. Patch byte 0x25 of
   `sceAgcGetDataPacketPayloadAddress`, past the prefix, and the harness printed "prefix verified"
   and then reported four MATCH verdicts against a module that was not Sony's, plus one divergence
   attributed to our HLE rather than to the tampering. Provenance was the entire value proposition
   and it covered 55% of the executed code. Now the whole body is compared, `body_size` and
   `body_hex` must agree, and a case file without them is a `5` exit rather than a weaker check. The
   same tamper now yields `I7_BODY_BYTES_MISMATCH`.
2. **"The firmware returned 0 and wrote nothing" was indistinguishable from "the firmware never
   ran".** The dispatch status was captured and never read; the result slot was pre-zeroed. Any
   dispatch that failed before executing left RAX 0 and an untouched arena, and against an HLE stub
   that also returns 0 without writing that scores MATCH. That is aimed precisely at the VERIFIED
   NO-OP bucket `scripts/stub_census.py` produces, which is the bucket where a false MATCH is most
   expensive. Now the result slot is poisoned with `0xFEEDFACEDEADC0DE`, the trampoline stamps a
   separate magic word only after the body returns, and the verdict is refused unless the dispatcher
   reported `ORBIS_GEN2_OK` and the marker is present. Demonstrated live: patch the body to
   `add rsp,8; ret` so it returns past the trampoline, and every case comes back
   `I6_LLE_DISPATCH_FAILED` with `LLE.rax=0xFEEDFACEDEADC0DE`.
3. **Case 17 destroyed the whole run.** Windows were laid out at `0x60000000 + index * 0x100000` with
   an exact mapping, and the host already owns `0x61000000`. A 17-case file died with a harness fault
   and, because artifacts were written once at the end, discarded the 16 verdicts already earned. The
   window cursor now steps until a mapping succeeds, and `cases.jsonl` is rewritten after every case.
   40 replicas of the control now run to completion: `cases=40 MATCH=40`, exit 0.
4. **The canary was one-sided and `mem_equal` lied on refused cases.** An HLE export calling a
   caller-supplied callback where Sony does not is a serious divergence, and the HLE canary result
   was computed and thrown away. Refused cases also reported `mem_equal=true` for arenas that were
   never compared. Both fixed; a refused case now prints `mem_equal=not-compared`.

## Refusal states

INCONCLUSIVE is a failure, never a pass, and every one of these runs **before** any comparison.

| code | meaning | fired live |
|---|---|---|
| `I1_UNRESOLVED_IMPORT` / `I1_IMPORT_DISPATCHED` | the body left the module | no |
| `I2_FAULT` | a catchable exception on either side | no |
| `I2_HOST_KILL` | an isolation child died without a verdict | yes |
| `I3_HLE_SELF_REPLAY` / `I3_LLE_SELF_REPLAY` | a side did not reproduce itself on identical state | no |
| `I4_OUT_OF_WINDOW_WRITE` | a write landed in a guard band | no |
| `I5_GUEST_CALLBACK_TAKEN` | either side called the caller-supplied callback | yes |
| `I6_LLE_DISPATCH_FAILED` / `I6_LLE_DID_NOT_RETURN` | the firmware body may not have run | yes |
| `I7_*` | admission: module missing, syscall bytes present, NID absent, body bytes wrong | yes |
| `I8_NO_HLE_EXPORT` | we have not implemented this NID | yes |

Each case runs four passes in HLE / LLE / HLE / LLE order over one never-reused, poison-filled,
guard-banded arena. Both sides of a case share the arena base, because these functions return and
store guest pointers and differing bases would manufacture divergence out of address noise.

## Honest limits

- **Six integer arguments and nothing else.** The trampoline emits six `movabs` into RDI..R9. There
  is no float or SSE argument path, no 7th argument, no struct by value, no varargs, and RAX is the
  only return channel compared. A function returning in XMM0, or clobbering RBX, is scored MATCH
  today. A case file declaring more than six arguments is now rejected rather than silently truncated.
- **The observation window is the arena and its guards.** Module `.data` and `.bss` are never
  snapshotted or compared. A same-module callee that writes a global is invisible.
- **Leaf-ness is not statically proven.** The harness does not disassemble. It proves the module
  contains no syscall byte pair and it detects import dispatches and indirect calls through the
  canary, but "this body does not call another function in the same module" is established by a human
  with a disassembler.
- **The syscall gate is a raw byte scan** over the PF_X PT_LOADs. It over-approximates, so a zero is
  a sound proof of absence and a non-zero may be a false reject. `libSceAgc` measures 0 sites over
  74210 executable bytes and is admitted; `libSceLibcInternal` measures 4 over 896210 and is refused.
  Lane A's `strlen`/`strchr` agreement came from a scratchpad app with no such gate and is **not**
  reproduced here. Under this harness `libSceLibcInternal` is out of scope, and that stays true until
  either the scan is replaced by a disassembly or the backend grows a guest syscall handler.
- **The import journal is log-scraping.** It sets `SHARPEMU_LOG_ALL_IMPORTS=1` and counts stderr
  lines containing `[LOADER][TRACE] Import#` / `[LOADER][WARN] Import#`. The literals exist at
  `DirectExecutionBackend.Imports.cs:417/422/430/435/609/632`, so it aims at real strings, but a
  dispatch path that does not print would silently report zero. A thread-static counter at the
  dispatch site would be sound; this is not.
- **A guest fault is a host kill, not an exception.** In-process, a null or unmapped pointer argument
  kills the process with `Fatal error.` and no catchable exception; `I2_FAULT` cannot fire for it.
  `--isolate` spawns one child per case and turns that into `I2_HOST_KILL` for the one case, with the
  rest of the file still reporting. **Any case ladder that exercises null, unmapped or overflowing
  arguments must use `--isolate`.**
- **`0x32964` was not controlled.** `sceAgcDcbDrawIndexAuto` ORs its draw initiator with a dword that
  lives past the PT_LOAD `filesz` and therefore reads 0 as `.bss`. Initiator 2 is established only
  for the zero-initialised case.
- **No determinism seam.** Nothing pins clock, RNG, TSC, pid or thread id. The functions chosen touch
  none of them; one that does will be scored DIVERGENCE for a reason that is not conformance.
- **140 of our NIDs are defined by more than one 4.03 module.** The case file names its module
  explicitly, so nothing goes wrong silently, but nothing refuses a carelessly chosen one. Both AGC
  NIDs here also exist in `libSceAgcVsh` and were not cross-run.
- **Guest code runs in the parent process with default Windows mitigations.** The `--fw-oracle`
  branch sits before `TryRunMitigatedChild`. It held for four small leaf bodies; the normal boot path
  relaunches with mitigations off for a reason.
- **Nothing in CI runs this.** `scripts/fw_oracle_gate.py` exists and rolls up, but `premerge.py` does
  not call it.

## Cost, and what it means for the queue

Measured on this machine, three runs each, spread under 0.05 s, in-process:

| shape | time |
|---|---|
| 1 case | 4.35 s |
| 40 cases | 20.56 s |
| fixed process cost | 3.93 s |
| marginal cost per case | 0.42 s |
| isolated (`--isolate`) | about 4.9 s per case |

A 5-case NID is about 6.0 s in one process. 4,108 NIDs at 5 cases each is roughly 6.9 hours serial,
about 26 minutes across 16 cores. Isolated it is roughly 28 hours serial, about 1.8 hours across 16
cores. **Runtime is not the blocker.**

The blocker is authoring. Every input is hand-produced: module path, full body hex, arena layout,
argument vectors, fixture wiring, and a disassembly to establish leaf-ness. One session produced five
case files covering four functions. Until a generator exists, "this turns the 4,108-NID queue into
parallelizable work" should be read as "this turns the subset of leaf, syscall-free, integer-ABI NIDs
whose bodies someone has disassembled into work".

An earlier cost model put the marginal cost at 0.16 s per case. That measurement was taken on a
40-case file against the version that crashed at case 17, so it timed 16 cases and called it 40. The
conclusion (runtime is cheap) survives; the number did not.

## How a worker would cheat it, and what stops them

The oracle takes **no expected values** from the case file. It cannot be made to pass by writing down
the answer. What it does take is the input vector and the fixture wiring, and that is the attack
surface.

- **Shrink the fixture until every case is refused.** Set the DCB `end` field from `arena+0x800` to
  `arena+0x108` and Sony's body takes its refill callback, the containment canary fires, and all six
  cases become INCONCLUSIVE. Five real HLE bugs disappear from the report. *Stopped by:* every case
  declares an `expect` verdict, defaulting to MATCH. The cheat now prints five
  `UNEXPECTED (declared MATCH)` lines and exits 2 instead of 0. The `case_file_sha256` is in the
  summary line and in every record in `cases.jsonl`, so the edit is visible in the golden record.
- **Delete the awkward case.** *Stopped by:* not fully. `fw_oracle_gate.py` records `scored_cases`
  per file and flags any file whose cases are all declared refusals as `is_self_test_only`, so a file
  cannot silently become coverage-free. There is still no per-NID minimum-case floor.
- **Relax the syscall gate to make `libSceLibcInternal` green.** *Stopped by:* the case file says in
  writing not to, and the gate refusing is the documented result. This is a social control, not a
  mechanical one.
- **Typo a key so a constraint is dropped.** JSON deserialisation silently ignores unknown members,
  and `_expect` used to be inert prose that nothing read. *Stopped by:* unknown keys are now a hard
  error unless they start with an underscore, which marks them as comments by convention.
- **Point the case file at a different module.** *Stopped by:* the module sha256 is recorded in every
  record, and the full body of the function is verified byte for byte before execution.

The self-test cases are the load-bearing part of the suite and they are designed to fail loudly if
they stop testing. `Yw0jKSqop+E` declares its callback case `INCONCLUSIVE I5_GUEST_CALLBACK_TAKEN`;
if the canary ever stops firing, that case becomes an unexpected MATCH and the file goes red.
`JOWmDrl+j20` declares both its cases `INCONCLUSIVE I8_NO_HLE_EXPORT`, and its first case is
engineered so that both sides return 0 and neither writes a byte, meaning every channel the oracle
compares agrees. A comparator without a refusal state would certify a function nobody has written.

## Next

The oracle is usable as a per-NID gate today and it is not wired into anything. The next concrete
step is a null-and-unmapped case ladder for the three AGC functions under `--isolate`, which is the
first thing this harness could not do until today and the shape of test that finds the bugs games
actually hit.
