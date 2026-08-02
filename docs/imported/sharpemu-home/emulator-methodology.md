# Building an emulator right: methodology derived from shadPS4's history

Derived 2026-07-27 from shadPS4's complete 4,125-commit history (2022-10-24 to
2026-07-26, `inspiration/shadPS4`, full commit metadata), read in six
chronological slices plus quantitative statistics (revert rates, subsystem
birth order, churn hotspots, contributor concentration) and their stated
process docs, then synthesized against `docs/mission.md` and
`docs/conformance-framework.md`. Every principle carries shadPS4 commit hashes
and a stress-test note on whether the evidence really supports it.

---

## 1. Load-bearing principles

### P1. On same-ISA emulation, the loader/linker IS the emulator - front-load its fidelity

Evidence: three months of exhaustive DT_* parsing (672e2b2d..0682830e), NID
resolution (8acfc3d5), relocations (81906c27) meant first execution "just
worked" (d641f7b6) with only an ABI fix the next day (28aad0a5). No CPU
interpreter was ever built. Subsystem birth order confirms: loader/main
2023-04, core 2023-10, video_core only 2024-02.

Stress test: not survivorship - the causal chain is explicit in the commit
sequence, and the inverse (six months of frontend-first work, all deleted at
71d14aca) is the control group inside the same repo.

**Confidence: HIGH.** SharpEmu is same-ISA (x86-64 native) and already holds
this.

### P2. Wide shallow stub surface + logging turns every boot into a work-queue generator - but permissive scaffolding must be deliberately retired

Evidence: 11k auto-generated aerolib stubs (f1ce6fe6), unknown-NID logging
(470881dc), caller return-address logging (fb4f7b79) - the era's
highest-leverage tooling commit. The bookend: four years later they *stopped*
auto-stubbing unresolved symbols (d9f68b53) because silent stubs had hidden
real missing-implementation bugs, and replaced asserts with accurate errors
(6f568eae).

Stress test: solid - both halves are documented, and the cost of not retiring
scaffolding is documented too (stub misbehavior needing behavioral fixes,
34a5f231).

**Confidence: HIGH.** The principle is a lifecycle: stubs are a *discovery
instrument*, and there must be a planned commit where they start failing
loudly. SharpEmu's 4,108 NIDs are exactly this phase; the `ASSUMED` provenance
tag is the retirement mechanism shadPS4 never had.

### P3. A ladder of concrete named targets orders the backlog; fixes stay generic, the game name lives in the commit message

Evidence: every HLE commit cites its pulling title (0ac4032d videoout_basic ->
9337e859 pong -> 189d1776 Sonic Mania -> 674bd4a2 branch literally named
"sonic" -> c79b10ed "Bloodborne stabilization pt1"). Per-game code is nearly
absent from the whole history; the norm is "fix generically, cite the game as
evidence" (2f022a46 st_mtim fixes RB4, 1f407a41 GCN thread-mask accuracy
labeled "GR2 Grass Fix"). Engine-cluster targeting converted one investigation
into many-game wins ("The way to Unity" pt.1-3: 98f0cb65, 7ffa581d, fea2593a).

Stress test: partial survivorship risk - this worked because a vast
homebrew/2D-commercial ladder existed below the hard titles. PS5 has almost no
such ladder; SharpEmu jumped straight to Astro Bot and paid with the
assert-skip/env-flag wall its own M4 documents. The transferable half is the
*generic-fix norm*, which mission.md already states more strongly than shadPS4
ever wrote down.

**Confidence: HIGH** for the generic-fix norm; **MEDIUM** for the ladder
(needs adaptation - SharpEmu's ladder must be milestone markers *within*
titles plus a second title, not easier titles).

### P4. Diagnostics before, or at latest with, the feature they will debug - and as permanent facilities, not one-off probes

Evidence: logger and ELF viewer before the linker existed (4d218d7a,
dda7020e), disassembler before code ran (b124bac0); Tracy/null-GPU/PM4-dumps/
RenderDoc markers within days of the first triangle (e89b2d1c, d752aa53,
863d80c1, b8916787); "list all missing instructions during translation"
(0c32ea24). Counter-case inside the same history: September 2024's hotfix
storm was fought blind until PM4 Explorer landed (af398e36), after which the
video_core hot-fix rate visibly dropped.

Stress test: strong - the repo contains both the treatment and the control.
The one refinement: shadPS4 sometimes let tooling lag pain by weeks; the lag
was pure cost.

**Confidence: HIGH.** This is SharpEmu's M0, and mission.md's "instrumentation
is a first-class subsystem" is the correct hardening of it.

### P5. Transplant proven architectures; grow them incrementally afterward

Evidence: the single biggest capability jump was a 17.8k-line import of a
yuzu/Citra-lineage shader recompiler (87309683) followed by hundreds of tiny
per-instruction commits; Citra's logger transplanted wholesale (79d6c8a3);
fpPS4's PlayGo credited (516a3e71). Cost: a regression tail as the borrowed
architecture met GCN reality (bfe33229, d0d7ef06, ca25333a).

Stress test: genuine, but note the precondition - the transplant landed *into*
a working frame/flip loop built the week before (133acdc1, 9df1a8d1), so games
drove it immediately. Transplanting into a vacuum would not have worked.

**Confidence: HIGH.** SharpEmu already treats shadPS4/Kyty as
inspiration-not-authority, which is the right constraint: transplant
*architecture* (IR design, pass structure), never *values* - PS4 GCN semantics
are not PS5 RDNA2/AGC ground truth.

### P6. When a subsystem accumulates ~5 point fixes in weeks, the spec is wrong, not the code - rewrite against the accumulated evidence

Evidence: five "another missed case in saveDataMount" commits in one week
(6545b09b..ebfed28f) resolved only by the 4,898-line rewrite (0f4bcd8c);
months of deadlock band-aids (dcab06ff, c8d0d563) preceding the 104-file
pthread rewrite (c4506da0); memory manager rework at three months old
(fc887bf3). The failure mode when the rewrite never comes is also on record:
core/memory cycled through fix-of-fix storms for years (46a7c4e1 -> c8b45e5e
-> 42f2697b, 4ba0e626 "Attempt to address race conditions") because VMM
concurrency was never designed once - and the eventual test suite never
targeted it.

Stress test: the pattern is real but shadPS4 *chose* the timing badly at least
once (three months of threading whack-a-mole before c4506da0). The honest
reading: point fixes are cheap evidence-gathering, but institute a hard
trigger (N narrow fixes in M weeks => mandatory redesign review), which
shadPS4 never had.

**Confidence: MEDIUM-HIGH.** The fix-storm counter is measurable per-file from
git; wire it into the workflow. Their data suggests N~5, M~3 weeks.

### P7. External deterministic oracles beat human judgment - use every free one, aggressively

Evidence: Vulkan validation layers treated as a test suite for years before
any unit tests existed - burn-down sweeps (75adf7c8, 01e8606f), commits named
after VUIDs (ae1acfa9), fixes justified purely by "it makes validation happy"
(4a554535), per-game validation enabling (23d0fc64). Also assert calibration
as deliberate practice: upgrade silent errors to asserts (394b7fa6), downgrade
only with evidence (65bd62e9), remove hacks only after verification (f1becb25
"assert removed (verified)").

Stress test: solid; validation-layer discipline demonstrably shrank the future
search space at near-zero cost.

**Confidence: HIGH.** This is the germ of SharpEmu's entire conformance
framework - shadPS4 found one free oracle by accident; SharpEmu's plan is to
manufacture them systematically (E1-E9). Same principle, industrialized.

### P8. Main stays shippable: revert fast, re-land as "take 2" carrying the lesson

Evidence: lifetime revert rate 0.80%, regressions evicted same-day rather than
forward-fixed (dd61c2a0, cd4f48cb, e9ede8d6), then reapplied corrected
(f6d71646 -> 9241ebd4 -> e1ecd8e9 "take 2"; a52b4c0d -> 221efa40 -> 9fb948cb).

Stress test: **this principle has a hidden dependency SharpEmu lacks.** It
worked because auto-updating nightly builds (ad9f1370, f79da986) turned
thousands of users into a same-day regression oracle. The revert *speed* is
transferable; the regression *detection* is not - SharpEmu must substitute the
milestone-corpus harness for the user fleet, or regressions will simply go
unnoticed (shadPS4's own "missing since october and somehow no one noticed"
3381f5d7 shows what happens on surfaces the fleet didn't cover).

**Confidence: HIGH** for revert-fast; the detection substitute is mandatory,
not optional.

### P9. Big changes land as numbered, individually-reviewable part-series, never megapatches

Evidence: "Surface management rework (1/3)..(3/3)" (64459f1a, 30198d5f,
28feb779), Http HLE parts 1-7 (08fe66a9..a2c4e68d), Matching2 P1-P11
(b564846b..48f4bf88), macOS port as a ~25-commit reviewed series
(66fa2905..70708fc6). Counter-case: the KBM megapatch merged the day of the
0.6.0 tag (c4bfaa60) cost release users a week of breakage; the Crowdin
one-shot swap cost two weeks of production debugging (1e7f651b -> 290e127a).

Stress test: both directions documented in-repo.

**Confidence: HIGH.** For agent workflows this is doubly load-bearing: small
verified units are also the anti-hallucination containment boundary.

### P10. HLE-vs-LLE is a reversible per-module toggle, and loading/decryption infrastructure is what makes it reversible

Evidence: HLE libc grind -> LLE libc.prx behind a config flag (02dcf4d4,
540c21d3); LLE deleted entirely to commit to pure HLE (4e0757ed); then
key_manager (c898071b, 240 lines) made LLE a 7-line-per-module change again
(910f7370) and whole Sony libraries were swapped in one at a time ("More safe
LLE modules" 7da7a8e1). The infra commit, not the library work, was always the
enabler.

Stress test: solid - three reversals, each cheap *because* the loader could
run real modules.

**Confidence: HIGH.** SharpEmu already runs game `.prx` LLE and holds 565
cleartext firmware modules; the self-differential oracle (LLE module vs our
HLE) is the strongest possible use of this - an oracle shadPS4 never built
despite having the ingredients.

### P11. Track every hack to its origin and delete it when the general mechanism lands; the record is the debt register

Evidence: early era used honest commit messages as the register ("not probably
correct behaviour" 9337e859, "ugly workaround :D" b62c44c9 - but no tracking
issues, and several hacks survived until subsystem rewrites swept them).
Mature era did it properly: "Remove hack from #2726" (9e7df6ae), "reverted
cmp_u64 workaround" once the real fix existed (f8c38ba7), "Require robustness2
and remove null buffer/image workarounds" (3d03375a).

Stress test: the early-era failure (untracked hacks lingering) and late-era
success are both visible; the delta is exactly the tracking.

**Confidence: HIGH.** SharpEmu's zero-`SHARPEMU_ASTRO_*`-flags milestone and
the deviation-record rule in mission.md are this principle, stated better -
the flags are at least greppable, which is more than shadPS4's early hacks
were.

### P12. Tests aimed at the expansion frontier pay off immediately - so put them *before* the frontier, not four years after

Evidence: gtest arrived at commit ~4050 (31b2d9cc), the GCN shader test
framework three weeks later (963d10f2), and the boldest recompiler work of the
era (Neo ISA campaign, dcdbd174, 07a0475d, tess+geometry pipelines 09c20d46)
began days after and clustered on top of it - one contributor sustained a
per-instruction PR stream without destabilizing main. The cost of the
four-year delay is equally documented: "did really no one ever test this"
(81098da5), five-month latent breakage (3381f5d7).

Stress test: the *late arrival* is not evidence lateness works - it's evidence
a 143-contributor human fleet can partially substitute for tests. That
substitute does not exist for SharpEmu, and SharpEmu has already reproduced
the failure at smaller scale: invented struct blobs shipped while all 1,038
tests stayed green (conformance framework).

**Confidence: HIGH** that harness-before-frontier is right; shadPS4 proves the
payoff, SharpEmu's own incident proves the cost of inversion.

---

## 2. Sequencing: the dependency order that worked

The validated ladder (subsystem birth order + milestone chain):

1. **Logging/viewer tooling -> disassembler -> loader -> linker -> relocation
   -> execution** (2023-04..07). Cheapest path to first executed instruction
   on same-ISA.
2. **OS-side display contract before any rendering**: equeue/flip semantics
   (f1b1eacb, 0c39b808) -> present-only blit path -> v0.0.1 (1395fd49). The
   GPU command processor came 7 months later (b94efcba) and landed *cleanly*
   because the OS contract was already stable.
3. **Kernel eventing before the recompiler** (9ad74956, 133acdc1, 9df1a8d1 the
   week before 87309683) so the first triangle rendered inside real game
   submissions.
4. **Recompiler transplant -> per-instruction growth**, with dump/profiling
   tooling in the same weeks.
5. **Threading/TLS as early as you can stomach** - this is where shadPS4 paid
   most: the two-month TLS wall (af184539 -> 724c56d8) blocked *all*
   commercial-game progress, and TLS was reworked three more times (728249f5,
   1b9bf924, 9e504794). Deferral here was the single most expensive ordering
   mistake of the bootstrap era, repeated in miniature by the pthread rewrite
   arriving only after months of deadlock triage (c4506da0).
6. **Multimedia (AJM/videodec) after boot+render** (f068f13e, Nov 2024) -
   correct call; cost was months of titles hanging on FMVs, absorbed
   knowingly.
7. **UX/i18n/perf polish strictly after correctness** (Crowdin, FSR f663176a
   only post-0.6.0); **online last** (ShadNet 2026, layered
   HTTP -> Np -> server -> matchmaking with a two-month "preparations" lead,
   95ba5918 -> aff387e8).

Where wrong order was paid for: the 6-month frontend-first dead-end (deleted
at 71d14aca - though it taught PKG/PSF formats); tests four years late (P12);
GPU introspection tooling arriving *after* the September 2024 blind hotfix
storm; the Crowdin and spdlog (854b291c) one-shot infra swaps, each costing
weeks of production debugging because the old behavior was never characterized
first.

For SharpEmu: the conformance framework's Phase 0 -> L5 -> L2+L4 ordering is
consistent with this ladder, with one shadPS4-derived caution - L2 kernel
semantics (their TLS/pthread analogue) is exactly the layer shadPS4
under-prioritized twice and paid for twice. Do not let L5's visibility appeal
starve L2; the deadlocks are the PS5 equivalent of the TLS wall.

---

## 3. What SharpEmu should NOT copy

1. **Community-as-regression-oracle.** shadPS4's entire quality system - 0.8%
   revert rate, nightly auto-update fleet (f79da986), mandatory-log bug
   reports (8f33dfe4), 143 authors/year - substitutes users for tests.
   SharpEmu has zero users and agents whose failure mode is *plausible*
   wrongness that no user would report anyway. Substitute: milestone corpus on
   every merge, generated conformance suites, differential testing.
   Non-negotiable, not aspirational.
2. **Tests-last.** Defensible (barely) with a human fleet; fatal here.
   SharpEmu's 1,038-green-tests-during-struct-corruption incident is the
   proof.
3. **Trusted-maintainer direct pushes / hotfix lanes.** Their "hot-fix the
   hot-fix" chains (8acefd25, 67a74a93/1dca54c1/57bdb6ca), "this is why you
   don't push local changes, shadow" (d1150ad3), and CI-by-trial-and-error
   (six windows.yml commits in a day, 25a9f72e..488c43be) are the noise of
   humans without local gates. Deterministic pre-merge gates (0-warn build +
   suites + differential), workflow changes tested off-main, formatting
   enforced at commit time eliminate this class entirely.
4. **The homebrew ladder as compatibility strategy.** PS5 has no
   OpenBOR/ps4nes tier. SharpEmu's ladder must be intra-title milestone
   markers plus early multi-title generality checks, with corpus rotation
   against overfitting - the disciplined version of what shadPS4 got for free
   from thousands of users playing different games.
5. **Per-function HLE grind validated by "does the game get further."**
   shadPS4 had no ground truth beyond behavior-under-game; SharpEmu has 565
   cleartext modules and can extract exact contracts. Copying the
   guess-and-boot loop when you own the reference implementation would be
   malpractice. (shadPS4 itself pivoted to LLE the moment keys existed -
   c898071b - validating contract-over-guess.)
6. **"Open a PR and we'll check it" human review as the gate.** Their real
   gate was one maintainer's judgment plus community soak. Model-on-model
   review is a strictly worse imitation; the conformance framework's "no LLM
   sign-off" rule is correct.
7. **Their GPU oracle situation.** shadPS4 could lean on validation layers
   plus a mature GCN knowledge base and other GCN emulators. PS5 NGG has no
   external oracle (Kyty aborts on it); nothing in shadPS4's history addresses
   this. A reference ISA interpreter has no precedent in their playbook and
   must be built anyway - do not let their "games as shader test suite" era
   (regression-fix chains bfe33229, d0d7ef06) look like a viable alternative.
8. **Release/community infrastructure** (auto-updater with its own bug tail
   f79da986 -> 7b16085c, i18n, ShadNet). Zero relevance at this stage and team
   shape. Cheap *checkpoints* (tags, screenshots as measured evidence) yes;
   release *engineering* no.

---

## 4. Stated process vs actual history: the contradictions

1. **The stated process is a style guide; the real methodology was never
   written down.** Everything load-bearing - revert-fast, game-named generic
   fixes, staged part-series, stabilization windows, hack-origin tracking - is
   uncodified norm enforced by 2-3 maintainers' taste. That works at their
   scale with human enculturation; it does not transfer to agents, who only
   follow what is written. SharpEmu's docs-first approach is the correct
   inversion.
2. **Their CI gates don't gate.** clang-format and REUSE are
   `continue-on-error: true`; the GCN shader tests - their flagship test
   investment - are *excluded from CI* (`-E 'GcnTest'`, presumably for
   flakiness, cf. 3aef2190). The only hard gate is "compiles on 3 OSes." So
   even after adopting tests they never closed the loop; the fleet remained
   the real gate.
3. **Rigor is demanded of bug reporters, not contributors.**
   game-bug-report.yaml requires clean dumps, mandatory unedited logs,
   code-located evidence; contributors get "Open a PR and we'll check it :)".
   The evidence bar exists - pointed outward.
4. **No stated hack policy despite the strongest de-facto anti-hack norm in
   the genre.** Per-game code is nearly absent from 4,125 commits, yet no
   document says so; per-game shader patching is even a documented *user*
   facility (patching-shader.md). The norm survived on culture alone. SharpEmu
   has codified it; keep it codified.
5. **Process arrived after the need, retroactively.** RFC template dated 2026,
   CONTRIBUTING's "no external deps in Core" written long after the
   transplants that violated its spirit. Process-follows-coordination-cost
   works for humans; for agents, process must precede work because agents have
   no institutional memory between sessions.

---

## 5. Applied to SharpEmu today

- **P1/P10 (loader + LLE infra):** done and ahead of shadPS4's curve - LLE
  game `.prx` + cleartext firmware in hand. The unexploited dividend is the
  self-differential oracle (LLE module vs our HLE); shadPS4 never had this.
  Build it.
- **P2 (stub lifecycle):** SharpEmu is at peak stub-debt (4,108 NIDs,
  `ASSUMED` values shipping). Make the debt enumerable via provenance, then
  schedule the d9f68b53 moment: a date after which unverified stubs fail
  loudly on corpus-touched surfaces.
- **P3 (generic fixes):** the surviving `SHARPEMU_ASTRO_*` flags and
  shader-address-keyed translator lines are exactly the early-shadPS4
  untracked-hack failure. They are already registered - better than shadPS4 -
  but each needs the 9e7df6ae treatment: general mechanism lands, flag
  deleted, commit cites the flag it kills.
- **P4 (instrumentation):** tooling with bringup, not after. The file-I/O
  blind spot mission.md admits is a September-2024-shadPS4 situation:
  debugging blind in a known-dark subsystem. Close it before the next Astro
  push, not during.
- **P6 (fix-storm trigger):** adopt the measurable rule - ~5 narrow fixes on
  one file in ~3 weeks triggers a contract-level redesign review. Their VMM
  shows what ignoring the trigger for years costs.
- **P7/P12 (oracles + harness-before-frontier):** generated conformance suites
  must be *hard* merge gates from day one - an excluded-from-CI test framework
  is theater (their own contradiction #2).
- **P8 (revert-fast):** adoptable only after a milestone corpus runs on merge;
  until then SharpEmu has no regression detector at all and "revert fast" has
  nothing to trigger it.
- **P9 (part-series):** maps directly onto the worktree-isolated worker
  protocol; also the structural fix for shared-tree lane collisions - separate
  trees, numbered mergeable parts, sequential merge with corpus run.
- **P5 (transplants):** shadPS4/Kyty remain architecture donors (recompiler
  pass structure, buffer/texture-cache shapes) under the existing
  inspiration-not-authority rule; every borrowed *value* is `ASSUMED` until
  traced to our own ground truth.
- **Sequencing:** keep graphics-first visibility, but treat kernel semantics
  with shadPS4's TLS scar in mind - their two costliest stalls were both
  kernel/threading, deferred because graphics was more visible. When a
  deadlock class recurs, that is the kernel bill arriving; pay it with a
  c4506da0-style owned, focused rewrite against FreeBSD 11 semantics, not
  band-aids.

**One-sentence summary:** shadPS4 proves the sequencing and the norms (generic
fixes, fast reverts, staged landings, oracle-hunger) but ran on a human
community substituting for every gate it never wrote down; SharpEmu must keep
the sequencing and norms while replacing the community with deterministic
oracles - and shadPS4's own late history (tests -> Neo campaign; keys -> LLE
wave) is the evidence that the oracle-first version is the stronger form, not
a bureaucratic weakening.
