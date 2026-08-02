# Prosperismo Conformance Framework — "Build to Contract, Verify to Ground Truth"

Goal: run **most games**, using an unlimited but **hallucination-prone** LLM workforce
(Fable + codex), without the hallucination compounding into plausible-but-wrong code.

## 0. The core principle (the anti-hallucination rule)

> **LLMs propose; ground-truth oracles dispose.**
> No emulator code is trusted because a model — or another model reviewing it — says it is
> correct. Every unit of work has (1) a machine-extracted **contract** from ground truth, and
> (2) an automated **oracle** that mechanically verifies conformance. The model's only job is
> to make the oracle pass. Model-on-model review is a pre-filter, never the gate.

This directly reforms three failure modes visible in the repo history:
- NID codegen produced plausible stubs validated against *proxies* (name catalog, shadPS4). We now
  have the real modules → validate against **exact contracts**, not proxies.
- "Opus reviews Sonnet" caught some bugs but is itself hallucination-prone → replace the *gate* with
  deterministic oracles; keep model review only as cheap pre-filtering.
- Env-flag / assert-unwind whack-a-mole treats symptoms → **every divergence is root-caused against
  a contract**, never papered over.

### Provenance tagging (how we find the hallucinated debt already in the tree)
Every non-obvious value in the HLE (return code, struct offset, arg register, flag bit) carries a
provenance tag:
- `EXTRACTED` — from ground truth (SDK header, stub-lib symbol, module disassembly, FreeBSD source).
- `DIFFERENTIAL` — confirmed by co-execution against the real module / a reference emulator.
- `ASSUMED` — a model guess. **These are debt.** They are enumerable, and the framework's job is to
  drive `ASSUMED` → `EXTRACTED`/`DIFFERENTIAL` to zero on the surfaces games actually touch.

---

## 1. What "works on most games" actually requires (build spec by layer)

NID *count* is not a completion metric (we have 4,108; games don't run). Decompose into layers,
each with a **definition-of-done tied to an oracle**. Evidence (Astro bring-up) says the gating
layers are **L5 (graphics — nothing visible without it), L2 (kernel semantics — deadlocks), and
L4 (are the contracts games call actually correct)**. L0/L1/L3 are largely done.

| L | Layer | Definition-of-done | Oracle |
|---|---|---|---|
| L0 | CPU / ISA | every instruction *form* in the game corpus decodes+executes or has fault-recovery | decode histogram over corpus eboots; any unhandled form = known gap |
| L1 | Memory | flexible/direct dmem, fixed maps, >2GB | SDK memory API contracts + syscall semantics |
| L2 | **Kernel ABI + object semantics** | evf/osem/umtx/equeue/aio + errno + sysvec behave FreeBSD-faithfully | FreeBSD 11.0 source + differential syscall trace |
| L3 | Loader | SELF/ELF/PRX, dynamic-link, TLS | load every corpus module, resolve all imports, 0 unresolved |
| L4 | **HLE libraries** (~180 libSce) | each fn matches its contract (sig+errno+struct+behavior) | contract DB (Part 2) + conformance tests + differential |
| L5 | **Graphics** (AGC→SPIR-V, NGG, tiling) | corpus frames match reference within tolerance | golden SPIR-V per shader + differential frame hash vs reference emu |
| L6 | Filesystem / sandbox | app0 + nullfs mounts + savedata resolve as guest expects | wiki mount model + real path traces |
| L7 | Media / audio / input | AvPlayer, AudioOut/Ngs2, Pad match contract | contract DB + differential |

---

## 2. The Ground-Truth Oracle — a machine-readable contract DB (`contracts/`)

Built by **deterministic extractors only (no LLM)**. This is the single source of truth; a worker
may not treat anything else as authoritative, and if a value isn't in the DB it must be *extracted*
(deterministically) before it may be implemented.

| Table | Source (all in hand) | Extractor |
|---|---|---|
| `nid_map` (NID→symbol→module) | stub `.a` libs (`inspiration/ps5-sdk-4.00`) + cleartext modules | `scripts/nid_firmware_audit.py` (built) |
| `signatures` (argc, types, ret) | 906 SDK-4.00 headers | libclang AST walk |
| `structs` (field offset/size/enum) | SDK headers | libclang |
| `constants` (every `SCE_*_ERROR_*`, flags) | SDK headers | libclang / grep |
| `behavior` (arg-validation → errno, out-struct writes, syscalls issued) | cleartext module disassembly | capstone, semi-automated decision-tree extractor |
| `syscall_abi` (number → args → errno) | psdevwiki `Syscalls.txt` + FreeBSD 11 | parse + xref |
| `kernel_semantics` (waiter order, edge/level, EINTR/ETIMEDOUT) | FreeBSD 11.0 source (evf/umtx/kqueue) | reference read |

The `behavior` table is where targeted disassembly lives (the `sceAudioOut2PortGetState` analysis in
`handoff-2026-07-24.md` is the template: recover the **upward contract**, ignore the downward
FreeBSD-syscall path — per the Prospero/FreeBSD constraint).

---

## 3. Verification oracles (strongest first)

1. **Reference co-execution / differential testing — the gold oracle.** Two variants by asset:
   - *Console available:* capture real call / syscall / GPU traces on a jailbroken PS5; replay and
     assert-equal.
   - *Static-only (works today):* **(a) self-differential** — load the CLEARTEXT module LLE inside our
     own emulator and compare its output to our HLE for identical inputs (we HAVE the modules);
     **(b) reference-emulator diff** — compare against shadPS4 / KytyPS5 on shared surfaces.
2. **Auto-generated contract-conformance tests** — emitted from the DB, not written by a model:
   arg-arity, errno-on-null/out-of-range, struct field offsets, return-enum domains. Mechanically
   catches the exact bug classes the Opus pass found by hand (Rsi/Rdi swaps, unchecked throws across
   the native boundary, wrong out-param width).
3. **Golden GPU/shader conformance** — per-shader: our SPIR-V vs golden; per-frame: image hash vs a
   reference emulator over the corpus. This is how L5 gets verified instead of eyeballed.
4. **Game-milestone corpus** — N titles, each with **objective, automatable** progress markers
   (imports-resolved → boots-to-menu = asset/frame signature → gameplay = pad-poll + scene draws →
   frame-hash match). Runs headless, scored automatically. This is the top-level "does it work" number.
5. **Assert-as-oracle** — every guest assert is a conformance failure carrying a message; root-cause
   it against the DB. **Never unwind an assert as the fix** (unwind stays only as a diagnostic net).
6. **Invariant / fuzz** — bad inputs must yield correct errno and never crash the host (native-boundary
   robustness).

---

## 4. Workforce protocol (unlimited LLMs, non-compounding error)

Per work unit (one HLE fn / one shader op / one kernel primitive):
1. **Extractor** (deterministic) fills the contract-DB entry.
2. **Test-gen** (deterministic) emits the conformance suite from that entry.
3. **LLM worker** (Fable/codex, parallel, worktree-isolated) implements to pass the suite AND match the
   disassembly behavior. It is handed the contract + tests + disassembly and is **forbidden from
   inventing any value not in the DB** (must tag `ASSUMED` and flag if it has no source).
4. **Gate** (deterministic): 0-warn build + full test + generated conformance suite + differential
   check. Red on any → reject. **No LLM sign-off.**
5. Merge sequentially; the milestone corpus runs on merge to catch regressions.

The single rule that kills hallucination: **no model is ever the authority on the correctness of its
own or another model's output — only the extracted oracle is.**

---

## 5. Sequencing to "most games"

- **Phase 0 — infra (do first, or all new code is more hallucinated debt):** build the extractors +
  contract DB + test-gen + differential harness + milestone corpus + a stable CI substrate (the
  spot-preempted T4 cannot sustain conformance CI).
- **Phase 1 — L5 graphics:** AGC command stream / NGG amplification / shader translation against
  golden + differential. This is the proven blocker to *visible output*. Highest single-title payoff.
- **Phase 2 — L2 + L4 sweep:** make FreeBSD kernel-object semantics faithful (kills the deadlocks), and
  re-verify all 4,108 NIDs against the real DB (not proxies), burning `ASSUMED` → verified on the
  surfaces the corpus touches.
- **Phase 3 — corpus expansion:** add titles; each stuck title's assert/divergence auto-becomes a
  work unit. Drive the milestone score up.

**Definition of done for "most games":** a representative corpus (20–50 titles) reaches *interactive
gameplay* for the majority, measured automatically by the milestone harness — not by NID count, not by
a model's opinion.

---

## Resolved decisions (2026-07-24)
1. **No console — static-only.** The **gold oracle is self-differential**: load a cleartext 4.03
   module LLE inside our own emulator and diff its output against our HLE for identical inputs, plus
   reference-emulator diff (shadPS4/KytyPS5) on shared surfaces. No hardware-trace oracle.
2. **Broad AGC-native PS5.** Optimize for the general native-PS5 population. **L5 AGC/NGG/shader is the
   long pole and the highest-risk layer** (see risk register).
3. **Stable GPU CI: yes, but later.** This is a **planning pass only** — do NOT start executing. A
   parallel agent is actively working Astro on the shared tree; standing up infra/workers now would
   collide. CPU CI first when execution begins; GPU CI when L5 starts.

---

## ★ EMPIRICAL VALIDATION (2026-07-24, from the parallel Astro session)
The thesis stopped being theoretical. Commits `fc6a2d0` and `7b98f36` found that **a refactor had
silently replaced real SDK struct layouts with invented blobs**:
- `sceAudioOut2GetSpeakerInfo`: 0x50-byte `SceAudioOut2SpeakerInfo` → an unrelated **0x20-byte blob**
- `sceAudioOutGetPortState`: full state → a truncated **0x10 bytes**
- Ngs2 buffer-size queries: 0x40-byte context buffer-info → a **0x18-byte record with a tiny size**

**All 1038 tests stayed green the entire time.** Nothing caught it. Callers were reading wrong bytes
for an unknown number of commits, and it took a human-directed deep debug session to notice. That is
precisely the hallucinated-debt failure mode this framework exists to kill — and it proves the debt is
not hypothetical, it is *already shipping* in the tree.

**Consequence — priority change:** `E1` (SDK header extractor) + `E5` (test-gen) move to **FIRST**,
ahead of E7. Rationale: days not weeks; no GPU, no console, no VM needed; and it retroactively
protects every struct-writing HLE function across all ~4,108 NIDs. A generated struct-layout/size
conformance suite makes this entire bug class *impossible to reintroduce*. Add **golden struct-layout
snapshots** so any future refactor that changes a size/offset fails the build instead of silently
corrupting callers.

Revised order: **E1+E5 (fast, protects everything) → E7 (unblocks L5) → E6 → rest.**

## Phase-0 backlog (the infra to build WHEN execution starts — discrete work units)
E1. **Header extractor** — libclang AST over the 906 SDK-4.00 headers → `signatures`, `structs`,
    `constants` tables. Deterministic.
E2. **Stub-lib NID extractor** — symbol tables of the 179 stub `.a` libs → authoritative
    `nid_map` (NID→symbol→module). Cross-check with `scripts/nid_firmware_audit.py` (built).
E3. **Behavior extractor** — capstone over cleartext modules → per-function decision tree
    (arg-validation → errno, out-struct writes, syscalls issued). Semi-automated; the
    `sceAudioOut2PortGetState` disasm is the worked example.
E4. **Contract DB** — schema + storage for E1–E3 + `syscall_abi` + `kernel_semantics`; the ONLY
    source workers may treat as truth.
E5. **Test-gen** — DB entry → xUnit conformance suite (arg-arity, errno-on-null/range, struct
    offsets, return-enum domains). No model authors tests.
E6. **Self-differential harness** — LLE-load a cleartext module, drive an export with generated
    inputs, capture regs+out-memory, diff vs our HLE. *Caveat:* the LLE path runs the module's
    downward FreeBSD syscalls into our kernel HLE — so for kernel-heavy fns it co-tests L2+L4 (a
    feature: divergence localizes our kernel bug); for leaf/marshaling fns it's a clean L4 oracle.
E7. **Reference-interpreter shader oracle** (the L5 answer under static-only) — a slow-but-correct
    RDNA2/Gen5 ISA interpreter as reference; diff our fast SPIR-V/NGG output against it on random
    inputs. Buildable with no console/reference-emu; the ONLY strong NGG oracle we can have.
E8. **Milestone corpus harness** — headless boot + automatable markers (imports-resolved →
    boots-to-menu = frame/asset signature → gameplay = pad-poll + scene draws → frame-hash). Scored.
E9. **Provenance retrofit** — scan `src/`, tag every non-obvious HLE value EXTRACTED/DIFFERENTIAL/
    ASSUMED, produce the `ASSUMED`-debt count on corpus-touched surfaces. The burn-down worklist.

---

## Risk register (honest)
- **R1 — L5 NGG has the weakest oracle (highest risk).** Broad AGC-native + no console + no reference
  emulator that does NGG amplification (KytyPS5 aborts on it) means golden-SPIR-V-vs-reference does
  NOT exist for the hardest piece. Mitigation = **E7 reference ISA interpreter** (compare our
  translation to a direct ISA interpretation, not to another emulator). This is the make-or-break
  build item; without it, L5 is hallucination-driven with no check.
- **R2 — Self-differential co-tests the kernel.** E6 running LLE modules exercises our kernel HLE; a
  kernel bug shows up as an L4 divergence. Manage by ordering: stabilize L2 primitives (via FreeBSD
  reference) before trusting E6 verdicts on kernel-heavy libraries.
- **R3 — Extraction ≠ full behavior.** Disassembly recovers the *upward contract* but a model can
  still mis-implement control flow the contract underspecifies. Differential (E6/E7) is the backstop;
  contract tests (E5) alone are necessary-not-sufficient.
- **R4 — Corpus bias.** Optimizing to a fixed corpus can overfit; rotate/expand titles so the
  milestone score reflects the broad population, not a memorized few.

## Coordination note — the lane split needs to be STRUCTURAL, not an agreement
Observed 2026-07-24: the handoff's proposed division of labor ("Astro agent owns the CPU backend, I
own audio HLE") was **violated within hours** — the Astro session edited `AudioOut2Exports.cs` /
`AudioOutExports.cs` / Ngs2 (my lane), and swept my untracked docs into two of its unrelated commits
(`5b3a457`, then `fc6a2d0`/`7598836`) via `git add -A`. Neither is misconduct; it's the predictable
result of two agents sharing one working tree with no enforcement.

**Fix when execution starts:** give the framework/Phase-0 work its **own clone or worktree**, leaving
`master` in the shared checkout to the Astro session. Agreements between agents don't hold; separate
trees do. Also note: I cannot message that session (SendMessage only reaches subagents I spawn) — the
**user is the only router**, and `docs/handoff-*.md` is the only channel.
