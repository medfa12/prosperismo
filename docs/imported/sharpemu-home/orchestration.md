# Orchestrating an unbounded agent workforce against a single GPU

The workforce is effectively unlimited (codex CLI, `gpt-5.6-sol`, xhigh, fast
tier). Three things are not: one V620, 16 logical cores, and one game at a time.
This document is the scheduling model that follows from that asymmetry.

## The governing insight

Do not spend the scarce resource per work item.

The instinct is one boot per change: implement an instruction, boot Astro, see
if it helped. At 240 instructions and roughly 8 minutes a boot that is 32 hours
of serialized GPU time, and it verifies almost nothing, because a single
instruction rarely changes a frame. Bun's loops never had this problem: their
queues drained against compiler errors, failing tests, and stack traces, all of
which are boot-free and individually verifiable.

So the whole design reduces to one rule:

> **Every work item must be verifiable without booting a game. The boot is a
> batched regression detector, never a per-item oracle.**

Manufacturing boot-free verification for a queue is therefore the precondition
for draining it in parallel. That is exactly what Track 0 builds, and it is why
the oracle work is the critical path rather than housekeeping.

## Tier A: boot-free, unbounded parallelism

Each of these queues has, or will have, a mechanical per-item gate. Workers can
run as wide as CPU allows.

| Queue | Items | Per-item boot-free gate | Blocked on |
|---|---:|---|---|
| Q1 ISA gaps | ~240 | contract table (T0.4) + SPIR-V inspection test + reference interpreter diff (T0.5) | T0.4, landing now |
| Q2 ISA audit findings | ~30 | doc citation plus file:line already attached to each finding | nothing |
| Q3 NID stubs | 4,108 | self-differential vs the cleartext 4.03 module (T0.6) | T0.6 |
| Q4 stub-census LIEs | from `stub_census.py` | firmware body says the stub lies; implement to the disassembly | nothing |

The isa-vop3 branch already proved the per-item recipe end to end in one
session: implement decode plus lowering, write a SPIR-V inspection test,
adversarial review, merge. No boot was involved and the reviewer still caught
three real MAJORs.

## Tier B: boot-gated, strictly serialized

One resource, one queue, one warden. Boots are spent on exactly two things:

1. **Merge-train regression runs.** Workers do not each get a boot. Merges
   accumulate on an integration branch; the warden boots the corpus once per
   batch. A green batch fast-forwards to master. A red batch bisects, which
   costs `log2(N)` boots rather than `N`.
2. **The serial investigations** (Astro's missing dispatch-args producer,
   Superliminal's `Main::Initialize` block). These are sequential evidence
   chains where each measurement changes the next question, so they get
   reserved GPU time and are never interleaved with the merge train.

Concurrency is configurable and defaults to 1. Two small titles at once is
plausible and worth measuring rather than assuming, so the warden takes
`--slots N` and we record measured evidence (FPS, draws per second, device
losses with 1 versus 2 concurrent instances) before ever raising the default.

## The real parallelism cap is CPU, not agents

Agents are free; the gate is not. Each worker needs its own worktree so
parallel `dotnet build` does not collide on bin/obj, and a Release build plus
test run is CPU-heavy. On 16 logical cores the honest ceiling is roughly 4 to 6
concurrent building workers. Beyond that, workers queue on the build rather
than on thinking, and wall-clock stops improving.

Practical consequence: run many codex workers in *analysis* phases (reading
firmware, deriving contracts, drafting implementations) and throttle the
*verification* phase through a build semaphore. Analysis is unbounded;
compilation is a bounded resource like the GPU, just less scarce.

## Worker protocol

Unchanged from the proven wave-1 recipe, adapted to Windows:

- One `git worktree add -b <queue>/<item> C:\sharpemu-workers\<item> master` per
  worker. `inspiration/` and `.dotnet-home` are gitignored so they are absent
  from worktrees; link them in with a directory junction (`New-Item -ItemType
  Junction`) and give absolute reference paths in the brief.
- All worktrees branch from the same base commit. Disjoint file ownership means
  `--no-ff` merges do not conflict.
- Non-interactive invocation:
  `codex exec -C <worktree> -m gpt-5.6-sol -c model_reasoning_effort='"xhigh"' -c notify='[]' --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check -o <wt>\CODEX_LAST.txt < prompt.md`
- Every brief carries the standing loop rules from `methodology-execution.md`:
  fail closed, never substitute zero or identity for an unknown, the
  workaround-comment rule, provenance tags, SPDX headers, no AI traces.
- **The orchestrator never trusts a worker's self-report.** Read the diff, scan
  for AI traces, verify headers, run the gate yourself. Read the BRANCH as well
  as the tree: poison committed and then reverted is absent from a net diff and
  permanent in `--no-ff` history, and the commit author and committer fields are
  where attribution lands by default.
- **The worker commits its work.** A branch left at the base commit merges as
  nothing, whatever the worktree contains.

## Loop topology per item

Bun's shape, which our n=1 already validated:

1. **Implementer** writes the change plus its boot-free test.
2. **Reviewer A** sees only the diff, is told to assume it is wrong, and tries
   to refute it.
3. **Reviewer B** checks conformance against the contract text specifically,
   not general code quality.
4. **Fixer** applies confirmed findings; disputed findings get rebutted in
   writing, not silently accepted.
5. **Gate** (deterministic): build 0 warnings, tests, contract conformance.
   No model sign-off.

Reviewers run in split contexts. A reviewer that watched the implementation
happen is not a reviewer.

## Rollout order

1. Land T0.1 corpus gate, so the merge train has a regression detector at all.
   Until it exists, "revert fast" has nothing to trigger it.
2. Land T0.4 contract table, which is Q1's per-item gate.
3. **Trial the loop on 3 instructions before scaling to 240.** Bun did 3 files
   before 1,448. If the trial produces garbage, fix the loop, not the output.
4. Scale Q1 corpus-first: instructions observed in Astro and Superliminal
   shaders before the long tail, so output stays immediately meaningful.
5. Build T0.6 self-differential, which unlocks Q3, the largest queue by far.

## Failure modes to watch, with their countermeasures

- **Workers silence errors to make the gate pass.** Bun hit this exactly:
  Claude stubbed functions to quiet the compiler. Countermeasure is the
  workaround-comment rule plus a no-new-stub check in the gate, and the fix
  goes in the prompt, never in the output.
- **Reviewers rubber-stamp.** Countermeasure: reviewers see only the diff, are
  told the code is wrong, and a review that finds nothing on a nontrivial diff
  is itself suspect.
- **Green tests, wrong behavior.** The failure we already shipped once. Only an
  externally authored oracle catches it, which is the entire Track 0 argument.
- **Merge-train poisoning.** A bad merge blocks everyone behind it.
  Countermeasure: bisect on red, evict same-day, re-land as take 2. A merge that
  does not APPLY is a different outcome from a batch that gates red: the warden
  exits 6, aborts the merge, restores the base and evicts nobody, because nothing
  was measured.

## Traps that have already cost real work

Each of these was measured, not predicted. The long version is `swarm-audit.md`.

- **`--base master` blames a worker for other people's commits.** Verification
  diffs the base against the branch, so once master advances and a worker merges
  it, every file anybody else changed appears in that worker's touched set.
  Always pass the pinned base commit the worktrees were cut from. The dispatcher
  now walks non-merge commits only, which removes most of the trap, but a stale
  `--base` still misreports.
- **A worker that verifies clean can merge as nothing.** `verify()` scores the
  working tree; `git merge` takes the branch. Uncommitted work passes every check
  and lands nothing. It is a loud warning in the summary now, and the warden
  refuses a candidate branch that adds no commits over the base rather than
  spending a boot on it.
- **`--no-gate` results read as passes.** They are reported as `PASS_UNGATED`, and
  a collect-only snapshot must never be quoted as a merge-ready verdict.
- **The AI-trace and dash scans are tripwires, not controls.** They catch
  carelessness. A string assembled at runtime walks straight through. Do not
  design a process that depends on them being complete.
- **The Metal backend is out of scope; the target is Windows.** The queue
  generator enforces it (`scope_check`), which is the only form of a scope rule
  that survives contact with a generator.
