<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->
# Superliminal debugging-session audit

Scope: read-only audit of Prosperismo at commit `2aa5c92`, except for this requested report. I did
not run the emulator, edit implementation files, or commit.

## Bottom line

- **VERIFIED — The latest conclusion is overclaimed.** Prosperismo can create two
  `PthreadCondState` instances from two distinct zero-valued guest slots, but that fact would not
  make the slots one logical condition variable. Kyty creates separate private condvars for
  separate zero-valued slots too
  (`inspiration/KytyPS5/src/kernel/pthread.cpp:1290-1331`,
  `inspiration/KytyPS5/src/kernel/pthread.cpp:2736-2749`).
- **UNVERIFIED — No evidence currently connects `0x100000BA0` causally to
  `PS5Manager.CurrentState`.** A thread parked in an infinite condvar wait proves that the current
  call is blocked. It does not prove that the thread should have been signalled, that it is on the
  load-critical dependency chain, or that waking it would let `Main::Initialize` return.
- **VERIFIED — The session is repeating the counting mistake behind the semaphore retraction.**
  Condvar waits and signals are not conserved one-for-one: signals with no waiter are discarded,
  broadcasts can release more than one waiter, and Prosperismo deliberately permits
  exception-induced spurious returns
  (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1912-1928`,
  `src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1984-2005`).
- **VERIFIED — There is a real resolver bug, but it is not the one presently claimed.**
  Lazy creation for the *same* zero address is not atomic. Two concurrent callers can both miss,
  both allocate, and return different states because Prosperismo does not re-check under
  `_stateGate` before publishing
  (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1601-1647`). Kyty takes a creation lock
  and re-checks the slot before initialization
  (`inspiration/KytyPS5/src/kernel/pthread.cpp:1295-1315`).
- **UNVERIFIED — That same-address race occurred in Superliminal.** The observed low address has
  wait traffic only, the eboot address has signal traffic only, and the busy stack condvar works.
  None is evidence of concurrent first use of one raw address.
- **VERIFIED — Retraction 2 was correct.** Prosperismo's semaphore signaller assigns
  `Outcome = Acquired`, deducts the tokens, and dequeues the specific waiter while holding the
  semaphore gate, before waking scheduler threads
  (`src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:834-860`,
  `src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:1157-1182`). Kyty uses the same durable
  hand-off
  (`inspiration/KytyPS5/src/kernel/semaphore.cpp:132-149`,
  `inspiration/KytyPS5/src/kernel/semaphore.cpp:172-179`).
- **VERIFIED — The GC attribution was correctly retracted, but the exception architecture is
  still defective.** The documented zero-stranding run falsifies that mechanism as this boot's
  gate (`docs/superliminal-boot.md:1472-1482`). It does not make delivery-at-HLE-boundaries sound.
  Kyty dispatches a pending signal *inside* `pthread_cond_wait`, before reacquiring the application
  mutex (`inspiration/KytyPS5/src/kernel/pthread.cpp:3043-3054`); Prosperismo's relevant host wait
  must first finish the cond wait and reacquire the mutex
  (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1920-1928`,
  `src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1959-1968`) before the import-boundary
  delivery site runs
  (`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs:1378-1383`).

The engineer is wrong to call the condvar finding “unambiguous”
(`docs/superliminal-boot.md:1640-1643`). It is a new parked-thread observation, not a demonstrated
load dependency.

## A. Can two zero-valued addresses mint two states?

**Answer: yes, mechanically. The proposed semantic interpretation does not follow.**

### The three resolution steps

1. **VERIFIED — Direct address hit.** `TryResolveCondState` first looks up the raw
   `condAddress` in `_condStates`. A hit returns that state immediately and reports the raw address
   as `resolvedAddress`
   (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1601-1607`).

2. **VERIFIED — Pointed-handle hit.** On a direct miss, it reads the qword at `condAddress`
   (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1610-1613`). If the qword is nonzero and
   is already a registry key, it aliases the raw address to that state and returns the pointed
   handle as the resolved address
   (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1615-1624`).

3. **VERIFIED — Zero lazy creation.** Only when the qword is zero and `createIfZero` is true does
   it allocate a `PthreadCondState` and a 0x100-byte opaque guest object, register the state under
   both the raw address and new handle, then write that handle into the guest slot
   (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1631-1662`).

**VERIFIED — Therefore two distinct addresses which are both still zero can each reach step 3 and
receive distinct states.** That is exactly what the current code does.

**VERIFIED — An initialized copy behaves differently.** If address B contains the handle previously
written at address A, B reaches step 2 and aliases to A's state
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1615-1624`).

**VERIFIED — An unknown nonzero qword is rejected, not lazily adopted.** The function reports that
qword as `resolvedAddress` and returns false
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1627-1628`). The final section correctly
noticed this separate defect (`docs/superliminal-boot.md:1725-1727`).

### Where the sharpened hypothesis goes wrong

**UNVERIFIED — “Copied while zero” has not been observed.** The report has no writer provenance,
copy instruction, shared owner object, matching call site, or shared predicate connecting
`0x80411A6A8` to `0x100000BA0`. Region type alone is not object identity
(`docs/superliminal-boot.md:1721-1723`).

**VERIFIED — Two zero slots do not receive cross-address identity from Kyty either.** Kyty's
`CreateObject` checks the qword at the supplied address, locks, re-checks that same qword, and calls
`PthreadCondInitNamed` for that address
(`inspiration/KytyPS5/src/kernel/pthread.cpp:1290-1331`). Initialization allocates a fresh
`PthreadCondPrivate` and stores its pointer in that slot
(`inspiration/KytyPS5/src/kernel/pthread.cpp:2736-2749`). A different zero slot repeats that process.

**UNVERIFIED — If Superliminal required two distinct zero slots to alias as one condvar, Kyty would
need an additional alias mechanism not present in this code.** Since Kyty boots the title, the
current “zero copy must retain identity” premise is contradicted by the chosen oracle unless the
engineer first demonstrates a materially different address/provenance path in Kyty.

### The resolver bug that is actually visible in source

**VERIFIED — Same-address lazy creation is racy.** Allocation happens after the initial locked
lookup and without a second lookup before `_condStates[condAddress] = createdState`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1601-1608`,
`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1637-1647`). Two first users of one zero slot
can execute:

1. both miss step 1;
2. both read zero;
3. both allocate different handles and states;
4. each overwrites the raw-address registry entry and guest qword;
5. each returns its own local `createdState`.

**VERIFIED — That race can strand a waiter and signaller on different states even though both calls
used the same raw guest address.** The registry's eventual winner does not change the state already
returned to the losing caller.

**VERIFIED — Kyty closes exactly this race.** It serializes lazy creation with `m_mutex` and
re-reads the slot after taking the lock
(`inspiration/KytyPS5/src/kernel/pthread.cpp:1306-1315`).

**UNVERIFIED — This source-level race explains the measured Superliminal addresses.** No trace
currently shows two state identities returned for one raw address during concurrent first use.

## B. Do the counts and frozen thread prove the two-address hypothesis?

**Answer: no. They prove one current host-side wait; they do not prove a lost or misrouted wake.**

### What is established

**VERIFIED — The frozen import counter proves the thread stopped crossing import boundaries.**
`PthreadCondWaitCore` hardcodes `cooperative = false`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1827-1837`), so this call takes the
host-parking loop rather than the scheduler-block path
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1867-1889`). A counter fixed at 150 is
consistent with the thread remaining inside that export.

**VERIFIED — `signal_epoch=0` proves only that this particular `PthreadCondState` has not received a
Prosperismo signal/broadcast call.** The epoch increments in `PthreadCondSignalCore`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1984-1988`). It does not prove that guest
logic was required to signal this condvar.

**VERIFIED — Four signals with zero waiters are legal edge notifications, not stored credits.**
Prosperismo increments the diagnostic epoch, scans the current waiter queue, and returns; it stores
no pending signal for a future waiter
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1984-2015`). Kyty likewise only marks an
existing waiter in `PthreadCondSignal`
(`inspiration/KytyPS5/src/kernel/pthread.cpp:2778-2786`).

**VERIFIED — The address census is internally inconsistent.** It says “only three condvars” and
then lists four addresses, including `0x100000BD0`
(`docs/superliminal-boot.md:1662-1671`).

### A code-consistent explanation that does not require aliasing

**UNVERIFIED — The most economical explanation is three/four independent uses:**

- `0x7FFFB17FFF18` is an active condvar whose worker was parked at capture time;
- `0x100000BA0` is a worker waiting for a predicate or work item that never became true during the
  observation window;
- `0x100000BD0` completed one ordinary exchange;
- `0x80411A6A8` received four notifications when it had no current waiter.

**VERIFIED — Prosperismo provides a direct explanation for repeated waits on
`0x100000BA0` despite epoch zero.** For an untimed wait, a pending guest exception causes
`CompleteCondWaiterLocked(... timedOut: false)` and a normal/spurious return without incrementing
`SignalEpoch`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1912-1928`). The guest can re-test a false
predicate and enter the wait again. Fourteen prior wait returns followed by a fifteenth current wait
are therefore compatible with zero condvar signals.

**UNVERIFIED — The prior 14 returns were exception-induced spurious wakes.** The current trace
records `pending_exception=False`, but the report does not correlate each earlier wait exit with
the exception-delivery log. That correlation is needed before promoting this explanation.

**VERIFIED — The near equality `4173 waits / 4172 signals` is not a general “healthy baseline.”**
A broadcast can complete multiple waiters, a signal with no waiter completes none, and a spurious
return needs no condvar signal. Kyty's broadcast explicitly clears all current waiters after one
sequence increment
(`inspiration/KytyPS5/src/kernel/pthread.cpp:2677-2687`). The stack row shows sustained activity,
not a universal accounting invariant.

### Why the current hypothesis does not explain the load gate

**VERIFIED — The cond trace logs raw address, waiter count, epoch, timed flag, and result, but not
the resolved handle, guest qword, state identity, caller, or predicate
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:2544-2553`).** Opposite traffic on two raw
addresses therefore does not establish common identity.

**UNVERIFIED — The generic thread parked at import 150 is part of
`Main::Initialize`'s critical path.** No thread name, caller module+offset, owner object, predicate,
or before/after correlation with `CurrentState` is given.

**UNVERIFIED — Waking this waiter would advance the gate.** The investigation has not injected a
spurious wake and observed the predicate, nor shown what producer should make that predicate true.

**VERIFIED — The heading “The stall is one condition variable” repeats the previous error pattern.**
It promotes a normal blocking shape to a root cause before establishing what the healthy oracle
does at the equivalent call site. This is the same missing-baseline failure acknowledged in the
semaphore retraction (`docs/superliminal-boot.md:1599-1625`).

## C. Is the stack condvar suspicious, or an address-resolution artifact?

**Answer: no, not by itself. The census address is the guest's raw ABI argument.**

**VERIFIED — All condvar entry points derive `condAddress` directly from guest RDI.** This is true
for the sce wait/signal/broadcast entries
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:558-584`) and the POSIX wait/signal/broadcast
entries
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:586-594`,
`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:726-740`).

**VERIFIED — The trace prints that unmodified raw address.** It does not print `resolvedAddress`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:2544-2553`).

**VERIFIED — Prosperismo therefore did not normalize many different displayed addresses into
`0x7FFFB17FFF18`.** The guest repeatedly passed that same stack location. A local or thread-owned
`pthread_cond_t` slot on a stack is not intrinsically invalid.

**VERIFIED — One initialized condvar passed through multiple storage addresses is normally merged,
not split.** On a direct miss, an address containing a known nonzero handle aliases to the existing
state
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1615-1624`).

**VERIFIED — Distinct zero-valued slots are deliberately split.** Prosperismo and Kyty both lazily
initialize per storage address. That is not evidence of one object being split.

**VERIFIED — The raw-address fast path can collapse different *lifetimes* if guest code overwrites
or reuses one stack slot without a matching destroy.** Once `_condStates[condAddress]` exists, step
1 returns it without re-reading the slot
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1601-1607`). Correct destruction removes
the raw-address entry and zeros the guest slot
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1731-1765`), so such collapse requires
unobserved reuse/overwrite or another initialization path.

**UNVERIFIED — The stack slot was reused for distinct condvars.** The current trace does not log the
qword at the slot over time, and sustained paired traffic is more consistent with one repeatedly
used condvar.

**VERIFIED — The more important internal split risk is the same-address first-use race described in
section A.** It can give two simultaneous callers different state objects even though the trace
shows the same raw stack address.

**UNVERIFIED — That race occurred at `0x7FFFB17FFF18`.** Its 4,000+ successful exchanges make a
persistent split unlikely, although a first-use identity trace is needed to rule it out.

## D. Were the two retractions correct?

### D1. GC suspend / exception type 30

**VERIFIED — The causal retraction was correct on the documented experiment.** A run with zero
stranded exceptions still left `CurrentState=1`; varying stranded counts did not vary the gate
(`docs/superliminal-boot.md:1472-1482`). That is a direct falsification of “a currently stranded
exception is necessary for this gate.”

**VERIFIED — The accounting argument in the retraction is weaker than stated.**
`guest_exception.safe_point_enter` is emitted before `TryCallGuestFunction`
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:5116-5135`), and this path has no matching
success/exit record. Counting `safe_point_enter` proves that a pending entry was removed and handler
invocation was attempted, not that the guest handler completed successfully.

**UNVERIFIED — Any safe-point handler failed in the measured boots.** The report shows no
corresponding delivery error. This weakness does not restore the old gate theory; it only means the
equation at `docs/superliminal-boot.md:1489-1494` is not, by itself, proof of completed delivery.

**VERIFIED — “A guest exception is only delivered after an HLE export returns” is not globally true
in current Prosperismo.** A cooperatively blocked thread with no active executor is delivered on
its execution runner
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:4834-4859`,
`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:5005-5022`).

**VERIFIED — That statement is true for the relevant host-blocked condvar path.**
`PthreadCondWaitCore` hardcodes `cooperative=false`
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1827-1837`), so the backend sees an active
running executor and queues the exception
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:4800-4831`). The queued delivery is checked
after export dispatch returns
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs:579-584`,
`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs:1378-1383`).

**VERIFIED — That architecture is genuinely unsound for signal interruption.** A running guest
thread can execute indefinitely without an HLE boundary, and the backend's own warning documents
that exact failure mode
(`src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs:5160-5168`). A host-blocked condvar thread
can only break its cond wait, queue mutex reacquisition, acquire the mutex, and return before
delivery (`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1912-1928`,
`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:1959-1968`).

**VERIFIED — Kyty implements the sounder ordering.** It polls in the cond wait, temporarily releases
the cond's internal lock, dispatches the pending signal on the waiting thread, resumes the wait, and
only reacquires the application mutex after the cond predicate becomes ready
(`inspiration/KytyPS5/src/kernel/pthread.cpp:3039-3054`).

**VERIFIED — The committed mutex change did not repair in-place delivery.**
`WaitForHostMutexLock` now wakes in slices and emits a warning when an exception is pending, but it
continues looping until the mutex is granted
(`src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs:2149-2209`). The documented absence of that
warning is good evidence that this particular path did not hold this boot
(`docs/superliminal-boot.md:1503-1509`); it is not an architectural fix.

**VERIFIED — Other uninterruptible host waits remain.** The rwlock fallback paths still contain bare
`Monitor.Wait` calls
(`src/SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs:1630-1633`,
`src/SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs:1652-1665`).

**Conclusion:** **VERIFIED — Retracting “this is Superliminal's current gate” was correct.**
**VERIFIED — Treating the delivery architecture as resolved would be wrong.** The defect is real,
latent, and handled correctly by Kyty in a way Prosperismo does not yet implement.

### D2. Semaphore lost wakeup

**VERIFIED — Retraction 2 was correct.** At an arbitrary stop time, one outstanding infinite wait
per idle worker is expected. Entered-minus-returned counts do not distinguish a lost grant from a
thread currently parked (`docs/superliminal-boot.md:1601-1617`).

**VERIFIED — Prosperismo already uses durable hand-off.** Under `semaphore.Gate`, the signal adds
tokens and calls `GrantWaitersLocked`
(`src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:834-850`).
`GrantWaitersLocked` deducts each waiter's tokens, assigns `Outcome = Acquired`, removes that waiter
from the queue, and only then returns
(`src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:1157-1182`). Scheduler waking happens
after the gate is released
(`src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:857-860`).

**VERIFIED — The queue-to-scheduler registration race is separately closed.** After registering a
block, the wait path re-runs the durable predicate and consumes the block immediately if a signal
won the window
(`src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs:652-678`).

**VERIFIED — Kyty's oracle agrees.** Its signal path calls `WakeWaiters` while holding `m_mutex`,
and `WakeWaiters` deducts the count and sets `result` and `ready=true` before the condition variable
is notified
(`inspiration/KytyPS5/src/kernel/semaphore.cpp:132-149`,
`inspiration/KytyPS5/src/kernel/semaphore.cpp:172-179`).

**UNVERIFIED — Some other semaphore defect is impossible.** The audit only shows that the claimed
lost-grant mechanism and the entered/returned evidence were wrong. No real semaphore bug should be
resurrected from those counts.

## E. Highest-value next experiment

**Recommendation: perform one call-site/predicate differential against Kyty, not the proposed
state-identity-only trace.**

The experiment should capture, for every first use and every wait/signal involving the four census
addresses:

1. caller as `module + offset` and guest thread identity;
2. raw RDI (`condAddress`);
3. qword at RDI before and after resolution;
4. stable Prosperismo state identity and resolved handle;
5. mutex address;
6. wait exit reason: signal, timeout, pending-exception spurious return, or still blocked;
7. the predicate address/value tested by the guest wait loop, identified from the caller's
   disassembly;
8. `PS5Manager.CurrentState` and hits at the established managed-return points.

Capture the equivalent module-relative wait/signal call sites and predicate behavior in Kyty. Raw
addresses need not match across emulators; caller offsets and guest object/predicate flow do.

### Outcome matrix

- **VERIFIED IF OBSERVED — Same raw address, two Prosperismo state identities during concurrent
  zero first use:** the actual non-atomic lazy-creation race is confirmed. Kyty's locked re-check is
  the reference fix.
- **VERIFIED IF OBSERVED — Same known nonzero handle/provenance at both guest addresses, but
  different Prosperismo states:** a resolver alias bug is confirmed. This would contradict the
  intended step-2 behavior and localize the failure to registry corruption or an unknown-handle
  path.
- **VERIFIED IF OBSERVED — Both addresses are independently zero and Kyty creates two private
  condvars at the matching call sites:** the sharpened “one logical condvar copied while zero”
  hypothesis is falsified. Two Prosperismo states are expected, not a finding.
- **VERIFIED IF OBSERVED — The low wait predicate becomes true and the matching producer signal
  occurs, but the waiter remains blocked on another state:** a missed/misrouted wake is causal at
  this wait.
- **VERIFIED IF OBSERVED — The low predicate remains false in Prosperismo but becomes true in Kyty:**
  the defect is upstream in the producer/state transition; changing condvar keying would be the
  wrong fix.
- **VERIFIED IF OBSERVED — Kyty also leaves the equivalent low waiter parked while
  `CurrentState` reaches 2:** this waiter is normal idle state and the entire third conclusion is
  falsified.
- **VERIFIED IF OBSERVED — The low waiter wakes or spuriously returns, re-tests a false predicate,
  and `CurrentState` stays 1:** the wait is a symptom of missing producer progress, not a lost
  condvar edge.

**VERIFIED — Merely tracing that `0x80411A6A8` and `0x100000BA0` resolve to two states is not
decisive.** The current code and Kyty both predict two states for two independent zero slots.
The missing facts are guest-side provenance, predicate state, matching call sites, and correlation
with the gate.
