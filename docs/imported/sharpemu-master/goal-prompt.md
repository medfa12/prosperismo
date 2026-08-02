---
description: Resume the standing SharpEmu effort. Pick the next item, execute it end to end, land it, continue. Do not ask what to do next.
argument-hint: [optional objective, e.g. "astrobot menu" or "keep going until superliminal is in game"]
---

You are resuming a long-running effort, not answering a question. This file is
the answer to "what should I do next" - never ask it.

## 0. The objective, if one was given

$ARGUMENTS

**If the line above is non-empty it IS the objective, and it outranks section 2
entirely.** It is what the user asked for, so work on that: select, sequence and
finish whatever serves it, and keep going until it is achieved or you can state
the specific measured fact that blocks it. Do not silently substitute the
tracker's top item for what was asked; that substitution is the single most
common way this command fails the person who typed it. Section 2's ordering
applies only *within* the objective, and in full only when no objective was
given.

A named blocker is not permission to stop. "One missing function", "one
unresolved import", "one undecoded opcode" is a work item, not a dead end.
Implement it, or state precisely what makes it unimplementable. On 2026-07-28
the LLE chain was called a dead end while its remaining blocker was a single
named kernel export; implementing it resolved all 133 imports behind it.

If the objective is genuinely already met, say so with the measurement that
shows it, then continue with section 2.

## 1. Load state (always, before acting)

- `docs/methodology-execution.md` - the live tracker and work order. Authoritative.
- `docs/orchestration.md` - the scheduling model: what may run in parallel and
  what must serialize on the one GPU.
- Project map memory - current frontier state per title.
- Consult on demand: `docs/emulator-methodology.md` section 5,
  `docs/conformance-framework.md`.

Then run `git log --oneline -8` and `git status --short` to see what actually
landed since the tracker was last written. **The tree is ground truth; the
tracker is a claim about it.** If they disagree, fix the tracker first.

## 2. Select work (when section 0 gave no objective, or to order work within one)

1. A **BUILDING** item that stalled, or whose follow-ups are unfinished.
2. A **red gate**: if `scripts/premerge.py` or `scripts/corpus_gate.py` is
   failing on master, that outranks all new work. A broken gate means every
   worker downstream is producing unverifiable output.
3. The highest **TODO** in Track 0. Track 0 is the critical path: it
   manufactures the independent oracles, and no queue may be drained in
   parallel before its boot-free gate exists.
4. Track 1 queue draining, but only through the swarm, and only after that
   queue's oracle exists and the 3-item trial has passed.
5. Track 2 serial investigations, when GPU time is reserved and free.

If the top item is blocked, record why in the tracker and take the next
unblocked one. Never idle waiting for an answer.

## 3. Execute

Standing rule: run work on Fable workflows. Scouting, building, boot runs and
adversarial verification happen inside agent contexts; only distilled outcomes
reach the main chat. Trivial mechanical edits and conversational replies are
the only things done inline.

Parallel work uses **Claude workflows** (the `Workflow` tool), with
`isolation: "worktree"` when agents mutate files. Do NOT dispatch through
`scripts/codex_swarm.py`: on 2026-07-28 it needed three relaunches for one item,
each failure pre-flight and worth nothing (a brief naming the wrong hook, a
`files_owned` list missing the file the task had to edit, a Rules section
forbidding that same file, an orphaned worktree blocking provisioning). Respect both limits: workers are cheap, build slots are not (16
cores, roughly 4-6 concurrent builds). All game execution goes through
`scripts/boot_warden.py`; never boot a title outside it, and never run two
emulators concurrently unless the warden's slot count was raised by
measurement.

Per work item, the loop topology is fixed: **implementer, then two adversarial
reviewers in split contexts seeing only the diff and told to assume it is
wrong, then a fixer.** A reviewer that watched the implementation happen is not
a reviewer.

## 4. Discipline (non-negotiable, applies to every item)

- **Measurement before hypothesis.** No claim about a run without a log line,
  census row, or screenshot behind it. This project has produced six confidently
  wrong conclusions from unmeasured reasoning; assume you are about to produce
  the seventh.
- **Read the guest's own output before profiling anything.** A commercial engine
  narrates its progress (`GAME:`, `PLAY:`, `StartLevel`, `Level has started`,
  `LevelDocument Loaded`) and asserts on its own failures (`ASSERT: D:\asobi\`).
  Those lines were written by the people who wrote the game, so they are the
  cheapest externally-authored oracle available, free in every log. The tell is
  a **missing** step in a sequence the same log shows working elsewhere. On
  2026-07-28 that located Astro's blocker in seconds after hours spent
  optimising a GPU pass that turned out to be its loading screen.
- **Absence is not evidence.** When a failure stops appearing, first prove the
  code that produces it still ran: same draw count, same milestone, same log
  volume. This one rule would have prevented a truncated boot read as a pass, an
  empty census read as "nothing qualifies", a probe that never ran read as "no
  fault", a deadlocked run read as healthy, a missing executable read as
  silence, and a corpus title whose baseline had no run length so its failure
  signature could never fire.
- **A count is not a finding until you know the healthy count.** Comparing 143
  failures against 11 proved nothing once it turned out both were normal
  probing. Establish the baseline rate before inferring from a rate.
- **Never abandon a branch before diffing it against master.** Findings and code
  accumulate on the branch you are about to leave. Run `git log --oneline
  master..<branch>` and rescue what is durable first.
- **Fail closed.** Never substitute zero or identity for an unknown value.
- **A paragraph-long comment justifying a workaround means the code is wrong.**
  Fix the code, not the comment.
- **Provenance on every non-obvious value:** EXTRACTED / DIFFERENTIAL /
  ASSUMED. `ASSUMED` is debt and must be enumerable.
- **Fixes stay generic.** The game name lives in the commit message only.
- **No model signs off on a model.** Only a deterministic oracle gates a merge.
- **Never trust a worker's self-report.** Read the diff, scan for AI traces,
  verify headers and file ownership, run the gate yourself.
- **When a loop produces garbage, fix the loop, not the output.** Edit the
  prompt; the generated code is a symptom.
- Run the gate before and after landing anything touching `src/`. A regression
  means revert fast and re-land as take 2.
- Commits: short human one-liner, bracketed prefix, no em dashes, no AI
  trailers, explicit paths (never `git add -A`; a sibling session may share the
  tree).

## 5. Close the loop on every item

When an item finishes: update its tracker status with a dated note carrying the
**measured numbers**, update the project map memory if the frontier moved,
commit, push, and immediately start the next item. Do not stop because the
session is long or the context is filling; state lives in the tracker, so a
fresh session can resume from it.

## 6. Definition of done (this is when the job ends)

The effort is complete when all of the following hold, each demonstrated by a
command whose output is recorded in the tracker:

1. `scripts/premerge.py` passes on master: 0-warning build, green suite, corpus
   gate green, no new stub-census LIEs.
2. At least two independent oracles are operational and gating merges: the ISA
   reference interpreter (T0.5) and the self-differential firmware oracle
   (T0.6). Oracle 1 too, if console access is ever confirmed.
3. Q1, Q2 and Q4 are drained, or every remaining item carries a written reason
   it cannot be.
4. Zero `SHARPEMU_ASTRO_*` flags and zero shader-address-keyed lines survive in
   the tree; each was deleted by a commit citing the general mechanism that
   replaced it.
5. Both Track 2 investigations are resolved to a root cause with a measured
   fix, or documented as blocked with the specific evidence that would unblock
   them.
6. The corpus baseline has advanced beyond its 2026-07-27 recording, and the
   advance is reproducible.

Until every one of those is true, there is more to do and you should be doing
it.

## 6b. Session boundaries are not failures

The six conditions are a multi-week arc. A single session will usually close
none of them, and that is expected, not a reason to grind. What a session owes
is a tracker whose top entry names the next concrete action, the command to run,
and the tooling already ruled in and out, so the next session spends its first
minute acting rather than re-deriving.

**Stop when further work would produce claims you cannot verify in the context
you have left.** Launching a boot you cannot read, or a worker whose diff you
cannot review, manufactures exactly the plausible-but-unverified output this
whole framework exists to prevent. Land what is verified, write the handoff,
stop. Do not stop merely because the session feels long.

## 7. Stop only for

- A destructive or irreversible action (deleting work, force-pushing, anything
  touching `games/`).
- A genuine scope fork the user must arbitrate.
- A fact only the user possesses (currently: whether console payload execution
  is available, which would promote oracle 1).

Everything else - errors, missing information, ambiguous requirements, a failed
run - you resolve yourself by measuring, retrying, or picking the next item.

## 8. Before ending a turn, check your last paragraph

If it is a plan, a question, a list of next steps, or a promise ("I'll...",
"let me know..."), you are not done. Do that work now with tool calls instead.
End the turn only when work is genuinely in flight or a section 7 condition is
met - and if work is in flight, say concretely what is running and what its
completion will prove.
