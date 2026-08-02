# SharpEmu — Mission

## Goal hook

**SharpEmu is not an emulator of a console. It is a reimplementation of an operating system.**

Wine for Prospero: every PS5 title runs on ordinary Windows hardware because every OS function
it calls is natively implemented here — *not* because we special-cased that title.

The second sentence is the falsifiable test. If a title runs only with a title-specific flag,
patch, or assert-skip, the general implementation underneath it is wrong and we have not
succeeded, we have papered over a defect.

---

## What we are building

A **compatibility runtime**, not a machine simulator. The user has a PS5 game and a Windows PC.
Nothing else — no console, no firmware on the target machine, no devkit. SharpEmu supplies
everything the game expected the console to supply:

- the kernel it syscalls into,
- the ~250 system libraries it links against,
- the graphics driver it submits command buffers to,
- the audio, input, network, save, and account services it queries.

The game binary is x86-64 and runs natively. Everything *around* it is what we implement.

---

## The five compensation layers

Each is "the PS5 assumed X; the target machine has Y; we bridge."

### 1. OS — FreeBSD 11.0 + Sony sysvec → Windows NT
Prospero syslibs and the game engine are compiled against FreeBSD 11.0 (`__FreeBSD_version`
1100122) with Sony's syscall extensions (dynlib 0x24d–0x257, evf 0x21a, osem 0x225, `_umtx_op`
0x1c6, aio 0x295, dmem 0x274). We do not port FreeBSD to Windows. We reproduce its **observable
semantics**: errno values, kernel-object behavior (equeue/evf/osem/umtx — edge vs level, EINTR,
ETIMEDOUT, waiter ordering), scheduling and blocking behavior, and the sandbox filesystem layout
(`/app0`, `/savedata`, `/system`).

Where a title ships its own `.prx` that we run LLE, that code calls straight into our kernel and
**must** be FreeBSD-faithful, not Win32-shaped.

### 2. System libraries — 565 cleartext `.sprx` → native reimplementation
We intercept at the NID boundary and replace the whole Sony function. So when reading a decrypted
module, extract the contract it presents **upward to the game** (struct layouts, valid field
values, return codes, error codes) — do not port its downward ioctl/syscall path.

`libkernel.sprx` alone exports 1605 NIDs. All of it is minable locally.

### 3. CPU — Zen 2 → any x86-64
Mostly native execution, so the ISA is not the problem. The real compensations are architectural:

- **Unified memory.** PS5 has 16 GB GDDR6 shared by CPU and GPU, with direct/flexible `dmem` the
  game allocates and maps itself. A PC has split system RAM and VRAM. `dmem` mapping, GPU-visible
  guest allocations, and coherency must be emulated with explicit host allocation + sync.
- **AMD-only instructions** and feature bits the game or its libs probe for (CPUID shims).
- **Offload processors** — Tempest (3D audio), ACM/AJM (audio codecs), the A53 I/O coprocessor.
  Their *interfaces* must exist and behave; their work runs on the host CPU.

### 4. GPU — Sony AGC + gfx1013-style ISA → any GPU (Vulkan)
Translate AGC command buffers to Vulkan and the title's Sony-custom shader binaries to SPIR-V.
Do not select an opcode table from the marketing label "RDNA 2". AMD/LLVM's own
[`gfx1013` documentation](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX1013.html)
delegates the baseline instruction set to
[`GFX10 RDNA1`](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX10.html),
then adds `image_bvh_intersect_ray`, `image_bvh64_intersect_ray`, and
`image_msaa_load`. Sony's SDK remains authoritative for Sony register fields,
shader metadata, and AGC contracts. NGG (Next-Gen Geometry) is the hard core;
use retained-draw replay and exact title shaders as the differential oracle
(see `conformance-framework.md`, E7).

Target for now: **NVIDIA and Intel on Vulkan**, which covers most desktops. AMD later; the
translation must not assume any of the three.

### 5. Services and peripherals — implement the shape, shortcut the ceremony
DualSense, audio out, video out, Np/account, save data, trophies, PSN, DRM. Implement the API
surface faithfully; replace the console-only ceremony behind it.

---

## Fidelity policy

**Faithful to the contract, not to the apparatus.**

Default to faithful — as faithful as the ground truth lets us be. Deviation is not a convenience,
it is a decision that must be justified against one of the two tests below. But we are also not
bound by measures Sony took because a PS5 is a locked-down retail appliance. Those are *console
proper*. A Windows machine is not that machine and does not inherit its obligations.

Two axes. Both must be considered, in this order.

### Axis 1 — can the guest observe it?

Observable ⇒ exact, no discretion:
- struct layouts and every field the game reads (sizes, offsets, padding, reserved fields)
- return codes and error codes, including which errno for which failure
- state machines the game polls, and the ordering of transitions
- anything the game hashes, compares, or branches on

Unobservable ⇒ free. No code path in the guest can distinguish it, so it is not a contract.

### Axis 2 — who does this measure serve?

For anything observable, ask what it is *for*. Not every observable thing exists to serve the
running program.

**Serves the game** — it is part of the runtime the game was written against. Be faithful.
Kernel object semantics, memory mapping, graphics submission, audio port state, input, the
`/app0` path layout, timing the game measures.

**Serves the platform** — it exists because the console is a retail product, a DRM vessel, or a
networked service. Implement the *interface* exactly, satisfy the call, discard the apparatus:
- SELF/PKG encryption and signing, licensing, PlayReady, entitlement checks
- save-data and trophy encryption and signing (write plaintext; nothing inspects the ciphertext)
- the hypervisor, secure modules, and the isolation model — anti-tamper, not a game service
- mandatory PSN sign-in, store, patch/update, age gating, parental controls, region policy
- telemetry, crash reporting, certification-driven policy checks
- the hardware ceremony behind a driver interface — the actual doorbell, ring, and ioctl
  protocol on the far side of `/dev/gc`, and the A53's real firmware

Note the difference from Axis 1: these are often *fully observable*. The game calls the license
API and reads the result. We do not skip the call — we answer it, correctly shaped, with success.
What we discard is the mechanism behind it.

**Serves fixed hardware** — a policy that only makes sense because every console is identical:
OS-reserved cores and memory, fixed budgets, one display that is a 60 Hz TV, fixed resolution
tiers. Answer queries plausibly, because a game may size its allocators or pick a render target
from the answer — but do not *enforce* a limit that exists only because the appliance had one.
Where the PC is more capable, let it be, and treat any resulting deviation as a compatibility
risk to be measured rather than assumed safe.

### Guard against abuse of "console proper"

This axis is the easiest thing in this document to hide behind. The test: **name the
non-game beneficiary.** If you cannot say concretely who other than the running program the
measure serves — Sony's revenue, anti-piracy, PSN, certification, the fixed hardware — then it is
game-facing and you owe it fidelity. "It seemed like ceremony" is not an answer, and neither is
"the game probably doesn't check." Difficulty is never evidence that something is console proper.

Every deviation gets recorded with its justification, so the list is auditable when a title
breaks. A shortcut that a game turns out to check is not a shortcut — it is a bug, and the
record is how we find it quickly.

---

## Ground truth — select it by claim

A single total ranking is misleading: a title proves what it consumes, the SDK
defines the public contract, and firmware proves one implementation at one
version. The durable source ledger is `evidence-source-ledger.md`; its labels
   (`AUTHORITATIVE`, `PRIMARY IMPLEMENTATION EVIDENCE`, `CONFIRMED`,
   `CORROBORATED`, and `REFUTED/DEAD END`) are binding.

1. **The game's own binaries and exact runtime captures** settle which path and
   value the title actually uses. They do not, by themselves, define every
   legal value of an SDK field.
2. **Sony SDK material** is authoritative for the contract it documents:
   Prospero SDK 10.00.00.40 (`games/prospero-sdk-10.00/`) supplies public API
   signatures, layouts, AGC register fields, shader metadata, and samples;
   the SDK 12.000 GPU Shader Core ISA Specification and Instruction Reference
   under `games/gpu shit_forzen/` supply the complete shader encoding and
   instruction semantics.
3. **Decrypted Sony firmware**, symbolised by `st_size`, is authoritative for
   the implementation in that exact firmware version. The local 3.02, 4.03,
   9.00, and 12.40 module sets and the 11.00–13.42 kernels are distinct oracles;
   never transfer private RVAs or layouts across them without rechecking.
4. **AMD/LLVM ROCm documentation** is primary toolchain evidence for
   `gfx1013`/GFX10 instruction syntax. It is not a substitute for Sony's AGC
   register and metadata contract.
5. **Primary implementation projects**—both generations of the payload SDK,
   fail0verflow's Prosperous, and the PS5 Linux patches—are curated,
   high-value evidence from engineers who built and exercised concrete PS5
   systems. Their code can establish algorithms, ABI names, kernel internals,
   physical-memory/PCIe/IOMMU behavior, and hardware enablement for the
   implemented layer; each claim keeps its exact project, revision, target, and
   layer attached.
6. **FreeBSD source, psdevwiki, and other emulators** are comparative evidence
   and hypothesis generators; confirm Prospero-specific claims above them.
7. **Model intuition is never authority.** Any value not traceable to evidence
   is tagged `ASSUMED` and remains a known liability.

Precedent: a refactor once replaced real SDK struct layouts with invented blobs (SpeakerInfo
0x50→0x20, `sceAudioOutGetPortState` truncated to 0x10, Ngs2 0x40→0x18) and **all 1038 tests
stayed green**. Plausible-looking invention is the primary failure mode of this project.

---

## Method rules

- **Enumerate before implementing.** A title's requirement surface is knowable *before* writing
  code: parse its import table, resolve every NID to a name and module, and classify
  HLE / LLE / unserved. Astro Bot is 1732 imports = 690 HLE + 930 LLE + 112 unserved. Never
  implement something the title does not call.
- **Absence of log output is not evidence.** Verify the path would have logged before concluding
  it never ran.
- **A static "who calls this" argument is not sufficient.** Confirm with a runtime hit count.
- **Measure a specific field.** Do not eyeball screenshots.
- **General instrumentation, not per-bug probes.** See below.

## Instrumentation is a first-class subsystem

The recurring failure has been one-off probes compiled in to answer a single question, then
deleted. What is needed is a permanent, cheap, always-available facility:

- every import call traced — NID → name → arguments → return value — filterable, ring-buffered
- every struct written into guest memory validated against its extracted contract
- guest memory write-watch: "what wrote this address"
- call trace filtered by object pointer: "every method invoked on this instance"
- file I/O trace (currently a total blind spot — the title's loads are invisible to us)
- a diff mode: run a function HLE and LLE against the cleartext module, compare results

## VM policy

**Local-first.** Disassembly, contract extraction, NID resolution, unit tests, and the
self-differential harness all run on the Mac against local ground truth. The VM
(`astro-vm3`, spot T4) is only for executing a title and for GPU-dependent verification.
Go in with a batched queue of questions, come out with answers, stop the instance.

---

## Milestones

| | |
|---|---|
| **M0** | General instrumentation lands: import trace, struct-contract validation, memory write-watch, I/O trace. Every "what is it doing" question answerable without new code. |
| **M1** | Astro Bot reaches its main menu. |
| **M2** | Astro Bot is interactive. |
| **M3** | A second title (Superliminal) boots on the same code with **zero** title-specific handling. This is the real test of runtime vs. one-game hack. |
| **M4** | No `SHARPEMU_ASTRO_*` flag exists. Every one of them is an admission that a general implementation is wrong. |

### M4 status, measured 2026-07-25

`grep -rhoE 'SHARPEMU_ASTRO_[A-Z_]+' --include='*.cs' src/ | sort -u` → **three remain**:

| Flag | Where | What it admits |
|---|---|---|
| `SHARPEMU_ASTRO_ASSERT_SKIP` | `DirectExecutionBackend.cs` | We step over an engine assert instead of satisfying the condition it checks. |
| `SHARPEMU_ASTRO_DEFAULT_BUS_PROBE` | `DirectExecutionBackend.cs` | A diagnostic for the `defaultBusses.size() == 1` wall, not a fix — and documented as such. |
| `SHARPEMU_ASTRO_TONEMAP_FIX` | `VulkanVideoPresenter.cs` | A 0.25 exposure stopgap that may be *suppressing* a correct exposure rather than supplying a missing one. |

Separately, and the same defect in a different shape: the SPIR-V translator carries lines keyed to
one title's shader program addresses (`0x500780000` / `0x500781200`). They are env-gated and inert
by default, but a general runtime has no business knowing a specific program's address, and they
will rot the moment those addresses move.

Removing each of these means finding the general implementation it is standing in for. That is the
work, not the flag.

Current state, tooling, and the live open question are in the project map memory. Strategy and the
build-to-contract / verify-to-ground-truth plan are in `docs/conformance-framework.md`.
