# What running a PS5 game on Windows actually needs

> **Current-status note (2026-07-31):** this chapter mixes durable architecture
> with historical title checkpoints. For current measured alignment and the
> source-authority order, use `docs/source-alignment-audit.md`; do not promote
> an older Astro blocker below over newer title evidence.

## Scope and grounding

This document describes the compatibility boundary a Windows-hosted, high-level PlayStation 5 emulator must reproduce to run a commercial PS5 game. It is about the execution environment after the game has been lawfully obtained and made available as a loadable application image; it is not an installation, dumping, or circumvention guide.

The central point is that “emulating the PS5” does **not** mean cycle-emulating every chip. For a pure-HLE design such as Prosperismo, it means preserving every machine-visible contract on which the game depends:

1. execute the game's x86-64 code with the expected CPU and ABI semantics;
2. reproduce its virtual address space, direct-memory model, TLS, exceptions, clocks, and synchronization;
3. load and relocate its ELF/SELF image and resolve imports by Sony NID;
4. replace the kernel-facing and `libSce*` user libraries with compatible host implementations;
5. consume AGC/PM4 command streams, recompile Sony's gfx1013-style GFX10 shader ISA, and preserve GPU memory and synchronization semantics on Vulkan;
6. provide coherent audio, video, input, storage, network, user, NP, and other system-service behavior.

This is a status snapshot of the repository first written on **2026-07-23** and source-audited on **2026-07-30**. Local paths in backticks are direct evidence. Assertions marked **(general knowledge, not locally verified)** are architectural or public reverse-engineering knowledge not established by the checked-in source. PS4 references are called out as such; shadPS4 is valuable evidence for the shared Orbis/Prospero format and library lineage, but it is not direct proof that every field or behavior is identical on PS5. See `docs/evidence-source-ledger.md` for exact local revisions, claim-specific authority, and preserved dead ends.

## 1. PS5 hardware

### 1.1 What hardware fidelity means for HLE

| PS5 block | Hardware fact | What software can observe | HLE treatment |
|---|---|---|---|
| CPU | Custom AMD Zen 2, x86-64, 8 cores/16 threads | ISA, CPUID-like feature assumptions, TLS, atomics, faults, cache-line assumptions, clocks/TSC, ordering, thread progress | Execute compatible instructions directly where possible; fix unsupported instructions and virtualize ABI-visible state. Do not model pipelines or caches cycle by cycle. |
| GPU | Custom AMD GPU using a gfx1013-style GFX10 RDNA1 baseline plus gfx1013-specific BVH/image instructions; command processor consuming PM4-like packets | Command packets, registers, descriptors, shader ISA, memory layouts, barriers, labels, query results, render/compute output | Parse packets and state, translate shaders to SPIR-V, issue equivalent Vulkan work, and emulate memory/coherency behavior. Do not emulate the command processor's gates or CUs cycle by cycle. |
| Main memory | 16 GiB unified GDDR6 | Large address ranges, direct-memory allocation, CPU/GPU aliasing, page protections, resource visibility | Reserve compatible host virtual addresses and track guest physical/direct allocations. Mirror or stage data when the Windows GPU is discrete. |
| Custom I/O/SSD | 825 GB SSD, 5.5 GB/s raw, custom I/O path and decompression | File availability, async-completion order, throughput assumptions, PlayGo state, decompression APIs | Read extracted files from the host filesystem and HLE async/decompression services. Ignore physical SSD hardware unless timing or queue behavior is visible. |
| Tempest audio | Custom 3D-audio processing | Audio library APIs, graph semantics, codec jobs, buffer timing, device latency | HLE `AudioOut`, NGS2, AJM, and media APIs; mix or decode on host CPU/audio APIs. Do not reproduce the audio silicon. |
| Security processors | Secure boot and protected cryptographic services, commonly discussed as SAMU/secure modules and an MP0/secure-processor path | Normally only authenticated loading and service results | Bypass the console boot chain. Require a loadable game image and reimplement libraries. Do not emulate secrets or secure boot. |

Sony's published base specification gives an x86-64 AMD Zen 2 CPU with 8 cores/16 threads up to 3.5 GHz, an RDNA 2-based GPU up to 2.23 GHz/10.3 TFLOPS, 16 GB GDDR6 at 448 GB/s, an 825 GB SSD at 5.5 GB/s raw, and Tempest 3D AudioTech. Those exact capacities and rates are **(general knowledge, not locally verified)**; the primary public source is Sony's [PS5 hardware specification](https://blog.playstation.com/2020/03/18/unveiling-new-details-of-playstation-5-hardware-technical-specs/).

### 1.2 CPU: compatible ISA is not a complete CPU environment

The PS5 and a Windows PC can both be x86-64, which makes direct execution possible. It does not make the CPU problem disappear.

Commercial PS5 code can use AMD-specific instructions that an Intel host does not implement. Prosperismo has a concrete example: `src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs` decodes and executes the register and immediate forms of SSE4a `EXTRQ` and `INSERTQ` after an illegal-instruction fault. The comment records that Astro Bot reaches `EXTRQ` in a color-unpack path. This is exactly the kind of incompatibility that “x86 on x86” hides.

The game can also observe:

- the guest calling convention, red zone, register preservation, stack shape, and signal/fault context;
- the FS base and PS5/FreeBSD-style TLS layout;
- atomic and memory-order behavior across threads;
- page faults, guard pages, executable protections, and illegal instructions;
- high-resolution clocks and TSC frequency;
- enough scheduler behavior that lock-free queues and timeout logic continue to progress;
- cache-line size or topology returned by APIs or direct CPU queries, even though it generally does not need a cycle-accurate cache.

Typical Zen 2 cores have separate 32 KiB L1 instruction/data caches, a private 512 KiB L2, and a shared L3 organized by core complex; public reports describe a reduced/custom L3 arrangement in the console. **(general knowledge, not locally verified)** Exact PS5 cache topology is not established by the checked-in sources. An HLE emulator normally preserves only observable cache-line/topology answers and memory-order semantics. It does not reproduce cache hits and misses. TSC is different: games use it as a clock, so the reported frequency and elapsed counter must be internally consistent even when the host CPU's physical frequency/topology differs.

Prosperismo's current engine is direct-execution only: `src/SharpEmu.Core/Cpu/CpuExecutionEngine.cs` exposes `NativeOnly`, while `src/SharpEmu.Core/Cpu/CpuDispatcher.cs` constructs the guest stack, TLS, FS/GS state, and process entry environment. On Windows, `src/SharpEmu.Core/Cpu/Native/Windows/WindowsFaultHandling.cs` installs process-wide vectored exception handling, and the direct backend switches away from the guest stack before running substantial host code. `src/SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs` exposes clocks and process-time counters and resolves a reported TSC frequency by override, host calibration, CPUID, or fallback. These are compatibility mechanisms, not Zen 2 microarchitecture simulation.

The remaining risk is precision. A direct backend inherits many host behaviors, including instructions and real host scheduling, but must interpose wherever the guest expectation differs. A dynarec would provide more control over instruction semantics and faults, at a large implementation and performance cost. Prosperismo currently chooses speed and lower translation complexity, then repairs mismatches at fault and import boundaries.

### 1.3 GPU: AGC, PM4, Gfx10, and NGG

The PS5 graphics API is AGC. AGC is low level: the application owns command-buffer memory, fills hardware-oriented state and resource descriptors, and submits command streams. The local tutorial makes the contrast explicit:

- `inspiration/ps5-agc-tutorial/Command-Buffer.md` describes application-owned command memory, ring segments, inserted jump commands, and waits when no segment is free;
- `Resource-Binding.md` describes descriptor blocks and a root-signature analogue;
- `Resource-Uploading.md` describes console unified memory and the need to transform image addresses for tiled layouts;
- `Cache-Synchronization.md` discusses color/depth caches, DCC/HTILE metadata, transitions, flushes, invalidations, and decompression;
- `Pipeline-State.md` maps the console model to modern Vulkan/D3D12 pipeline-state concepts.

At the hardware boundary, an on-die command processor reads PM4-family packet streams from memory, updates GPU registers/state, launches draws or dispatches, performs DMA/writes/waits, and signals labels or flip events. Do not derive the decoder from the commercial “RDNA 2” label. AMD/LLVM's primary
[`gfx1013` instruction documentation](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX1013.html)
explicitly directs readers to the
[`GFX10 RDNA1` instruction set](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX10.html)
for the baseline and lists gfx1013-specific `image_bvh_intersect_ray`,
`image_bvh64_intersect_ray`, and `image_msaa_load`. The Sony/AMD GPU Shader
Core ISA Specification and Instruction Reference from SDK 12.000, stored under
`games/gpu shit_forzen/`, are authoritative for the complete PS5 shader ISA.
Sony SDK 10.00 supplies the AGC register, stage, metadata, address, and sample
contracts. SharpEmu's decoder must combine those sources and exact title shader
evidence.

NGG, the “next-generation geometry” path, moves much of the classic vertex/geometry pipeline into primitive/mesh-like shader work and hardware export conventions. **(general knowledge, not locally verified)** A translator must reproduce primitive formation, culling, vertex/parameter exports, interpolation semantics, and stream-output behavior—not merely translate scalar and vector ALU opcodes.

The software-equivalent pipeline is:

```text
AGC calls and guest-owned buffers
        ↓
PM4 packet parser + Gfx10 register/state model
        ↓
draw/dispatch/resource description
        ↓
gfx1013/Sony shader decode → control-flow/IR analysis → SPIR-V
        ↓
Vulkan pipelines, descriptors, resources, barriers, queues
        ↓
VideoOut display buffer and host swapchain
```

KytyPS5 demonstrates the same partition in `inspiration/KytyPS5/src/graphics/guest_gpu`, `.../shader/recompiler`, and `.../host_gpu`. Representative files are `guest_gpu/command_processor/pm4Dispatch.cpp`, `shader/recompiler/ShaderDecoder.cpp`, `ShaderRecompiler.cpp`, `SpirvEmitter.cpp`, and `host_gpu/renderer/renderDraw.cpp`.

Prosperismo follows this architecture in C#. `src/SharpEmu.Libs/Agc/AgcExports.cs` contains the command/register ingestion layer; `Gen5ShaderTranslator.cs` and the `Gen5SpirvTranslator*.cs` files decode and emit shaders; `NggPrimitiveShader.cs`, `Gfx10Detiler.cs`, and `Gfx10UnifiedFormat.cs` cover important geometry, layout, and format work. `src/SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs` is the Vulkan execution and presentation backend.

### 1.4 Unified memory and “direct memory”

The PS5 CPU and GPU share one physical GDDR6 pool. **(general knowledge, not locally verified)** User software does not treat every byte as ordinary heap memory, however. It obtains direct-memory ranges, maps them into virtual space, applies protections, and uses those mappings for GPU-visible resources. Address identity and aliasing matter: a shader descriptor or PM4 packet may contain a guest GPU address that is also meaningful to the CPU.

On a Windows PC with a discrete GPU, guest unified memory becomes two domains:

- a host virtual-memory mapping used by natively executing guest CPU code;
- Vulkan buffers/images in device or host-visible memory.

The emulator therefore needs dirty tracking, upload/download, alias recognition, range ownership, and correct fence/barrier ordering. A naïve “copy before draw and copy after draw” scheme is both too slow and semantically wrong for resources that alias, remain GPU-resident, or are read by a later compute pass.

Prosperismo reserves guest-address-compatible mappings in `src/SharpEmu.Core/Memory/PhysicalVirtualMemory.cs`. The kernel HLE in `src/SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs` models a 16 GiB direct-memory capacity, 16 KiB Orbis/Prospero pages, a 448 MiB flexible-memory pool, direct allocations, mapping, unmapping/protection, and queries. Those constants and behaviors are emulator policy approximations unless independently confirmed; they should not be mistaken for a complete MMU model.

### 1.5 SSD, decompression, audio, and security

The custom SSD/I/O complex reduces copies and provides hardware decompression, widely reported to include RAD Game Tools' Kraken format. **(general knowledge, not locally verified)** An HLE emulator normally bypasses the physical pipeline: extracted `/app0` files are ordinary NTFS files, and async read/decompression requests can be serviced by host threads and software codecs. What cannot be ignored is the contract—completion callbacks, queue depth, error codes, data visibility, PlayGo availability, and timing assumptions.

Tempest similarly does not require transistor-level emulation. The emulator must reproduce the user-visible audio APIs and job graphs closely enough that engines initialize, allocate voices, decode assets, and advance clocks. The current Astro Bot gate in `docs/astrobot-bringup.md` is a warning: audio can block the graphics path before audible output matters.

Secure boot, SAMU/secure-module services, and protected key derivation are outside a pure-HLE game's runtime boundary. The emulator neither authenticates a retail boot chain nor possesses per-console secrets. It starts from a loadable game process, replaces system libraries, and ignores secure hardware unless the title makes an API call whose result must be faked.

## 2. Prospero OS

### 2.1 Kernel and userland layers

Prospero OS is FreeBSD-derived, but “it is FreeBSD” is too coarse. The observable interface is a Sony-forked kernel ABI plus Sony's user libraries and services.

The locally checked sources support a FreeBSD 11-era ABI resemblance:

- `inspiration/shadps5-rust/README.md` describes mapping FreeBSD 11 syscalls;
- `inspiration/shadps5-rust/src/kernel.rs` uses familiar FreeBSD syscall numbers such as `_umtx_op`, `thr_new`, and `mmap`;
- `inspiration/ps5-payload-sdk/include/freebsd` contains FreeBSD-derived headers; the preserved legacy checkout uses `inspiration/ps5-payload-sdk-legacy/include_bsd`.

This does **not** prove that every retail PS5 firmware is a stock FreeBSD 11 kernel. It proves that FreeBSD 11 structures and syscall numbering are useful reverse-engineering anchors.

The game-visible stack is approximately:

```text
game executable and game-supplied SPRX modules
        ↓ imports
libkernel / libc / libSceLibcInternal
        ↓
libSceAgc, VideoOut, AudioOut, Np, SaveData, Pad, Font, Net, ...
        ↓
kernel syscalls, devices, IPC, and privileged system services
        ↓
hardware
```

An HLE emulator can intercept at several levels. Prosperismo primarily implements exported user-library functions by NID and provides its own kernel/runtime layer. It does not boot Sony's kernel.

For inherited Orbis behavior, `inspiration/shadPS4/src/core/libraries` is the most mature local reference. Its kernel equeue, memory, thread, time, filesystem, and error tables demonstrate the difference between a function stub and a behavioral HLE; its libc, VideoOut, AudioOut, NP, and SaveData directories provide structures, state machines, error codes, and host mappings. This material is authoritative for shadPS4's PS4 target and often an excellent hypothesis for PS5, but PS5 behavior still needs a Gen5 binary, on-console probe, or title observation before it is treated as unchanged.

### 2.2 User-process boot chain

The retail console has a much longer authenticated boot chain, but the useful HLE process boot is:

1. identify `eboot.bin` and the application root;
2. unwrap a loadable SELF or accept a plain ELF;
3. reserve/map load segments at the expected addresses and apply permissions;
4. register the main TLS initialization image;
5. parse Sony dynamic tags, symbol/string tables, and relocations;
6. resolve imports and data symbols;
7. load game-supplied `.prx`/`.sprx` modules;
8. execute pre-initializers, module initializers, and `DT_INIT`/init arrays;
9. construct process stack, TLS, proc-param state, and enter the guest entry point;
10. let the game initialize libraries and its worker threads.

That sequence is visible in `src/SharpEmu.Core/Loader/SelfLoader.cs`, `src/SharpEmu.Core/Runtime/SharpEmuRuntime.cs`, and `src/SharpEmu.Core/Cpu/CpuDispatcher.cs`. The runtime binds the eboot directory as `SHARPEMU_APP0_DIR`, loads adjacent modules from `sce_module`, `sce_modules`, and `Media/Modules`, runs initializers, and then enters the process.

### 2.3 Processes, threads, pthreads, TLS, and events

A title expects a process with many host-like but not Windows-native primitives:

- kernel threads and pthread wrappers, attributes, join/detach, priorities, names, and affinity;
- mutexes, condition variables, semaphores, rwlocks, event flags, and address-based waits;
- TLS using the expected AMD64 variant and per-thread errno/runtime fields;
- equeues, Sony's kqueue-derived event mechanism, for user events, timers, I/O, GPU flips, and callbacks;
- signals/exceptions and interruptible waits;
- monotonic, realtime, process, and thread clocks.

`src/SharpEmu.Core/Cpu/GuestTlsImage.cs` identifies the FreeBSD/AMD64 Variant II TLS arrangement. `src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs` and `KernelPthreadExtendedCompatExports.cs` map pthread operations to host threading constructs. `KernelEventQueueCompatExports.cs`, `KernelEventFlagCompatExports.cs`, `KernelSemaphoreCompatExports.cs`, and `KernelSyncOnAddressCompatExports.cs` provide the corresponding event and synchronization surfaces.

Mapping one guest thread to one Windows thread is convenient for direct execution, but Windows scheduling is not Prospero scheduling. Correctness requires preserving wake predicates, timeout clocks, cancellation behavior, callback ordering, and the point at which writes become visible. `docs/astrobot-bringup.md` records a real lost-wakeup bug: a condition-variable signal could be consumed by the wrong waiter; adding a signal epoch fixed boot progression. That was a semantic error, not a missing function name.

### 2.4 Virtual memory and direct memory

The kernel contract includes two related resources:

- **virtual address space**: reserve/commit/map/protect/query operations at guest-chosen addresses;
- **direct memory**: physical-like ranges allocated separately, then mapped one or more times into virtual space.

Guest code and GPU descriptors can depend on fixed addresses and alias identity. A byte-array interpreter memory model is insufficient for native execution because the CPU must dereference the same addresses that the guest believes it owns. Prosperismo's production runtime therefore uses `PhysicalVirtualMemory`, not merely the simpler `VirtualMemory` abstraction.

### 2.5 HLE versus LLE

| Strategy | Loads | Advantages | Costs |
|---|---|---|---|
| HLE | Game executable plus emulator implementations of the imported contracts | No Sony firmware required; behavior can map directly to Windows/Vulkan; easier instrumentation | Every used function, structure, error, callback, timing rule, and side effect must be recovered |
| LLE | Decrypted Sony kernel or system modules, sometimes under a compatibility kernel | More original code and internal behavior | Requires lawful decrypted firmware, a much broader kernel/device environment, and still needs GPU/hardware translation |
| Hybrid | HLE for some modules, decrypted LLE modules for others | Can use original code where HLE is weak | Cross-boundary ABI, ownership, TLS, and callback behavior become more complex |

A commercial game can boot in a pure-HLE emulator **without decrypting or loading firmware** because the game's imports are satisfied by new implementations. It still needs its own executable and any required game modules in a loadable, decrypted form. `inspiration/KytyPS5/README.md` explicitly says that its booting titles require no external low-level emulation modules. Prosperismo's `SharpEmuRuntime` likewise registers `SharpEmu.Libs` exports rather than loading Sony system libraries.

This is why “no firmware required” does not mean “no reverse engineering required.” Firmware is an extremely useful behavioral oracle, but HLE substitutes the results rather than executing the sealed module.

## 3. Firmware and why it is sealed

> **⚠ CORRECTION 2026-07-24 — this section is true in general but NOT true of the dump we hold.**
> The general theory below (SELF/SPRX segment encryption, SAMU/MP0 key ladder, "cannot be decrypted
> purely in software") correctly describes *retail firmware as distributed*. But
> `games/PS5_4.03_reconstructed` is an **already-decrypted** reconstruction: **565 of 599 modules are
> cleartext ELF** with intact `.dynstr` NID export tables (`libkernel.sprx` alone exports 1605 NIDs),
> readable Sony build paths and asserts. Only **34** remain protected (`libc`, the `libSceDeci5*` debug
> family, `WebDriver`, the BD-J/Java runtime) — none of which block a mainline emulator.
> **Practical consequence:** exact per-module NID→symbol maps, struct offsets, error codes and
> behavior ARE minable today by disassembly, with no console and no keys. Do not plan around
> "we can only use proxies." See `docs/nid-firmware-audit.md`, `scripts/nid_firmware_audit.py`, and
> memory `decrypted-403-firmware`. (The tonemap root cause in `astrobot-bringup.md` was already
> firmware-confirmed this way on 2026-07-22 — the capability predates the correction.)

### 3.1 PUP and installed firmware

A PS5 update is distributed as a PUP. Public community documentation describes an outer `SLB2`/BLS-style container with a file table and multiple component images—for the kernel, system volumes, secure loaders, peripherals, and other firmware. Recovery PUPs contain a fuller reinstall image; update PUPs can be differential. Exact retail structures and component handling are **(general knowledge, not locally verified)**; a public starting point is the community [PS5 PUP format page](https://www.psdevwiki.com/ps5/PUP).

Parsing the outer container is not equivalent to obtaining plaintext userland modules. Nested filesystems and executables remain authenticated and/or encrypted for their intended secure-loading path.

### 3.2 SELF and SPRX

Sony executables use SELF, a signed/encrypted container around ELF-like program content. Shared/reloadable modules are commonly `.sprx` or `.prx`. A SELF contains a Sony header and segment table plus the ELF metadata needed after authentication/decryption; segment flags can express blocking, compression, and encryption.

Local direct evidence is intentionally limited:

- `src/SharpEmu.Core/Loader/SelfLoader.cs` recognizes PS5 SELF magic `0x5414F5EE`, PS4 SELF magic `0x4F153D1D`, and plain ELF;
- it reconstructs mappings from a SELF/ELF image but emits an error when the image appears encrypted or unresolved;
- `inspiration/shadps5-rust/README.md` explicitly requires `.elf` or decrypted `.self`;
- `inspiration/shadPS4/src/core/loader/elf.h` and `elf.cpp` document the closely related PS4 SELF/ELF segment and dynamic-tag family, including encrypted/compressed segment flags. This is PS4-family reference evidence, not proof of every PS5 field.

### 3.3 Why ordinary offline decryption stops

The simplified public model is:

1. immutable/early boot code establishes a hardware root of trust;
2. authenticated loaders bring up later security components;
3. encrypted metadata and segment keys are processed through protected security services;
4. AES operations and derived keys are kept behind secure-processor/SBL interfaces;
5. the x86 kernel or loader exchanges requests with those services through a mailbox/command path often described in community research as involving SAMU and AMD MP0.

This AES/SAMU/MP0 “key ladder” description is **(general knowledge, not locally verified)** and intentionally high level. The exact PS5 generations, mailbox commands, key slots, derivation inputs, and division of work are not established by the checked-in repositories and vary in public reports. It would be misleading to present a single precise ladder as settled fact.

The practical cryptographic point is firmer: having the ciphertext, file format, and AES implementation is not enough. Software also needs the secret key material or access to the device service that derives/uses it. Retail system modules therefore cannot generally be decrypted from a public PUP alone by reproducing AES on a PC. Researchers obtain plaintext through an exploited console/security-service oracle, memory or filesystem dumps after decryption, or recovered key material. The public [Byepervisor project](https://github.com/PS5Dev/Byepervisor), for example, lists code to decrypt system-library SELFs over TCP on supported early firmware.

“Cannot be decrypted purely in software” should be read as “cannot be derived from the sealed files alone with the publicly available inputs.” It is not a claim of mathematical impossibility if all necessary secrets are later recovered. [fail0verflow publicly claimed recovery of PS5 symmetric root keys](https://x.com/fail0verflow/status/1457499576676634625) in 2021, but that claim did not publish a universal, complete workflow for every retail module/title/firmware combination. **(general knowledge, not locally verified)**

### 3.4 Consequence for Prosperismo

Prosperismo does not need to reproduce the secure boot chain or decrypt `libSce*` firmware modules. It:

- accepts a loadable game ELF/decrypted SELF;
- creates import stubs from its dynamic tables;
- dispatches those NIDs to C# implementations;
- optionally uses lawfully obtained decrypted firmware as a reverse-engineering oracle, not as a runtime dependency.

`docs/astrobot-bringup.md` records a local decrypted 4.03 firmware oracle containing hundreds of modules and a generated export index. It was used to confirm shader behavior and replace guesses with firmware-shaped behavior. The document does not establish how that oracle was acquired, so no provenance or universal availability should be inferred.

The pinned `inspiration/ps5-payload-sdk/README.md` reinforces the boundary from the console side: generating stubs for a Sony library starts with a **decrypted** `.sprx`.

## 4. Game distribution and formats

### 4.1 From package to process image

| Artifact | Role | Emulator responsibility |
|---|---|---|
| Retail PKG | Authenticated/encrypted distribution and installation container | Usually handled outside the runtime. A pure HLE loader needs an extracted, lawful application tree; SharpEmu currently has no retail PKG installer/decrypter. |
| `eboot.bin` | Main application, normally a SELF | Recognize SELF/ELF, require loadable plaintext segments, map, relocate, establish TLS, and call entry. |
| `sce_module/*.sprx` and game modules | Game-shipped shared code | Discover, map, relocate, resolve imports/exports, and run module initializers. |
| `sce_sys/param.json` | PS5 title/content metadata | Parse title ID, content version, localized title, and other fields needed by services. |
| `param.sfo`/PSF | PS4-era metadata and a useful family analogue | Needed for PS4 compatibility paths or reference tooling; not the primary PS5 metadata file in SharpEmu. |
| `/app0` | Runtime mount of the installed application tree | Translate guest paths safely to the host root while preserving expected names and access behavior. |
| PlayGo metadata/chunks | Progressive-install and data-availability model | Report coherent chunk IDs, loci, progress, install speed, and availability; ensure requested data actually exists. |
| Save data | Per-user/title mutable storage with mount semantics and metadata | Provide isolated host-backed mounts, quotas/space, transactions, params, icons, and error behavior. |
| Trophy data | Trophy definitions, state, icons, and NP-facing APIs | Parse or synthesize coherent metadata and persist unlock state if required. |

PS5 PKG encryption/authentication details are **(general knowledge, not locally verified)**. The runtime requirement is clear regardless: by the time `SelfLoader` runs, it needs accessible bytes.

### 4.2 What Prosperismo's loader actually does

`src/SharpEmu.Core/Loader/ElfHeader.cs`, `ProgramHeader.cs`, `SelfImage.cs`, and `SelfLoader.cs` implement the current loader. It:

- distinguishes PS4 SELF, PS5 SELF, and raw ELF;
- recognizes ordinary load/dynamic/TLS segments and Sony `PT_SCE_*`-style program headers;
- maps a PS5 main image around its preferred `0x800000000` region and additional modules into managed ranges;
- registers `PT_TLS`;
- reads standard and `DT_SCE_*` dynamic data;
- processes x86-64 relocation types used by the current targets;
- creates high-address import stubs;
- resolves code and data imports;
- collects preinit/init arrays and module initializers;
- discovers adjacent game modules.

`src/SharpEmu.Core/Loader/ParamLoader.cs` parses `sce_sys/param.json`, while `SharpEmuRuntime.cs` binds the eboot directory as `/app0`. Path translation is implemented in `src/SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs` and media-specific normalization also appears in `src/SharpEmu.Libs/AvPlayer/AvPlayerExports.cs`.

This is not yet a complete content pipeline. It does not install a retail PKG, derive content keys, emulate the console license system, or decrypt an encrypted eboot.

### 4.3 What the references establish

shadPS4's `inspiration/shadPS4/src/core/loader/elf.h` and `elf.cpp` are strong reference material for the Orbis SELF/ELF lineage, Sony program headers, dynamic tags, and relocation conventions. `inspiration/shadPS4/src/core/file_format/psf.*`, `playgo_chunk.*`, `trp.*`, and `pfs.h` cover PS4-family metadata, PlayGo, trophies, and filesystem concepts.

The checked-in shadPS4 loader directory does **not** contain the PKG loader named in some project descriptions. It should be cited here for SELF/ELF and extracted-content formats, not as local evidence that Prosperismo can ingest a PS5 retail PKG.

### 4.4 Loader completion criteria

A loader is complete enough for a title only when all of these hold:

1. every executable segment is plaintext, correctly aligned, and mapped at a usable address;
2. RELRO and final page permissions are applied at the right time;
3. TLS offsets and initialization images match every loaded module;
4. code and data relocations have correct addends and symbol ownership;
5. weak, missing, and versioned imports behave correctly;
6. initialization order matches dependencies;
7. `/app0` and module search paths expose the exact case-sensitive/case-preserving tree expected by the title;
8. content metadata and services agree about title ID, content ID, version, user, and data availability.

Getting to the entry point is therefore a milestone, not proof of a correct loader.

## 5. The NID / dynamic-linking mechanism

### 5.1 Why imports are not ordinary symbol names

Prospero executables use compact Name Identifiers (NIDs) in dynamic symbol records. A dynamic symbol can appear in the form `nid#library-id#module-id`; the human-readable C/C++ symbol is often absent from the binary. The loader also reads module and library metadata from Sony dynamic tags.

The NID is not encryption. It is a deterministic, truncated hash encoding. It prevents the loader from recovering a function name by simply reading the import string, but known SDK symbols can be hashed and matched.

### 5.2 Actual NID generation

The maintained checkout at `inspiration/ps5-payload-sdk` revision
`a0d2bc60bdcc0a5ee9e790fa3b02fe5051a152d0` contains
`host/bin/prospero-nid.c`. The preserved legacy checkout at
`inspiration/ps5-payload-sdk-legacy` revision
`4bdc3fb919483a74199f09661692d9fb746e6b6b` does not. The earlier “absent”
finding was true only of that legacy checkout.

That source implements:

```text
salt = 51 8D 64 A6 35 DE D8 C1 E6 B0 39 B1 C3 E5 52 30
digest = SHA1(UTF8(symbol_name) || salt)
value = byte_swap_64(digest[0:8])
NID = base64(value, alphabet A-Z a-z 0-9 + -, no padding) truncated to 11 characters
```

More literally, it reverses the first eight SHA-1 bytes as a 64-bit value, zeroes the following working bytes, and encodes with `ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-` to produce eleven characters. The salt is shared with the PS4-era scheme. This exact sequence is grounded in the maintained local implementation.

### 5.3 Aerolib/nidDB

An aerolib or NID database stores pairs such as:

```text
NID → sceKernelSomeFunction
NID → mangled C++ export
```

Names are recovered from SDK import stubs, symbol lists, decrypted firmware exports, payload queries, strings and call-site analysis, and confirmed behavior. Unknown NIDs can still be linked to trap stubs, but no useful HLE exists until their ABI and behavior are known.

The maintained payload SDK's `inspiration/ps5-payload-sdk/sce_stubs/genstub.py` consumes `aerolib.csv`, reads dynamic symbols from a decrypted SPRX, splits `nid#lid#mid`, and emits named C stubs. It does not calculate the NID itself, and `aerolib.csv` is not present in the checkout. The generated `.c` files declare names but contain neither signatures nor behavior.

Prosperismo embeds its catalog in `src/SharpEmu.HLE/Aerolib/aerolib.bin`; `Aerolib.cs` maps in both directions. `docs/aerolib-catalog.md` documents the local lookup/catalog tooling.

### 5.4 Prosperismo dispatch path

The complete call path is:

```text
undefined ELF dynamic symbol
        ↓
extract NID + library/module identity
        ↓
relocation points at a SharpEmu import stub
        ↓
guest calls stub; direct backend crosses to host dispatch
        ↓
ModuleManager finds NID
        ↓
C# [SysAbiExport] implementation reads guest registers/memory
        ↓
implementation writes result, errno/output structures, events, or callbacks
        ↓
return to guest code
```

`src/SharpEmu.HLE/ModuleManager.cs` scans assemblies for `SysAbiExportAttribute`, associates exports with NIDs/names/libraries/generations, freezes the table, and warms static constructors/JIT paths before guest execution. Warming matters because host CLR initialization on a hijacked guest stack is unsafe. `SelfLoader.cs` creates `int3`/return-style import stub slots and applies relocation targets. The direct backend then handles the guest-to-host transition.

Correct resolution requires more than “same NID.” Library/module versions, object-versus-function symbols, weak imports, data relocations, C++ ABI, and structure ownership can all change behavior. An implementation that returns zero for every known NID may pass linking and still corrupt the title several seconds later.

## 6. The RE landscape / online knowledge

> **Grounding label:** This section is a public-knowledge orientation and is **(general knowledge, not locally verified)** except where it explicitly cites a local checkout. Exploit capability and firmware support change quickly; the linked project README is more reliable than a static compatibility claim.

### 6.1 Exploit history as a research pipeline

PS5 research has usually progressed through separate layers:

1. a userland entry point, often WebKit/browser, Blu-ray Java, a media application, or an emulated legacy-title bug;
2. a kernel vulnerability providing stronger read/write or code execution;
3. bypasses or exploits for hypervisor/execute-only/write-protection boundaries;
4. payload tooling for module enumeration, memory access, filesystem access, logging, and decrypted dumps.

Important public landmarks include:

- WebKit-based entry points followed by the IPv6 `ip6_pktopts` use-after-free chain. The public [PS5-IPV6-Kernel-Exploit](https://github.com/Cryptogenic/PS5-IPV6-Kernel-Exploit) supports selected 3.xx/4.xx firmware and documents arbitrary read/write and root privileges, while also documenting XOM, hypervisor write protection, CFI, and other limitations.
- TheFlow's BD-JB Blu-ray Java sandbox work, which supplied another userland route on affected firmware.
- `mast1c0re`, which escaped Sony's PS2-emulator environment on PS4/PS5 through vulnerable legacy titles.
- UMTX-based kernel work and early-firmware hypervisor research. [Byepervisor](https://github.com/PS5Dev/Byepervisor) documents a hypervisor exploit for 1.xx–2.xx-era firmware and includes system-SELF decryption support.
- [fail0verflow's 2021 public claim](https://x.com/fail0verflow/status/1457499576676634625) that it had recovered PS5 symmetric root keys and could obtain per-console key material from software. The claim demonstrated significant security understanding; it did not itself publish a turnkey universal decryption stack.

These are not interchangeable “jailbreaks.” A userland exploit may not patch the kernel; a kernel exploit may still be constrained by the hypervisor; an exploit that dumps one firmware does not automatically support later firmware. For emulator research, their importance is access to observations and lawful dumps, not the act of jailbreaking itself.

### 6.2 Emulator projects and what each contributes

- **shadPS4** is a PS4 emulator, not a PS5 emulator. Its mature kernel/library HLE, SELF/ELF loader, GCN graphics work, error codes, and system-library behavior are highly transferable to the shared Sony/FreeBSD lineage. The local checkout is `inspiration/shadPS4`; the current public project is [shadPS4](https://github.com/shadps4-emu/shadPS4).
- **RPCSX** is an experimental PS4/PS5 research project. Its public instructions have used decrypted console firmware and mount a firmware system tree, so it represents a more LLE-oriented research direction than Prosperismo. See [RPCSX's running notes](https://rpcsx.github.io/wiki/run/).
- **Kyty/KytyPS5** is a Windows C++ HLE emulator. The local `inspiration/KytyPS5` fork has active PS5-oriented PM4, RDNA 2-to-SPIR-V, tiling, and Vulkan code and states that no external LLE modules are currently required.
- **shadps5-rust** is a discontinued pure-HLE Rust base. Its local README is unusually explicit about the intended architecture: direct x86-64 execution, FreeBSD 11 syscall mapping, a 16 GB unified-address model, decrypted SELF loading, and AGC PM4-to-Vulkan translation.
- **Prosperismo** is the C#/.NET implementation analyzed here. Its distinguishing current evidence is not just a triangle: the local Astro Bot journal records process boot, system-library bring-up, 4K guest frame presentation, menu-asset loading, and deep shader/resource debugging, while also recording the remaining audio, exposure, input, and performance gates.

Public project status should never be inferred from screenshots alone. “Boots,” “renders,” “in game,” “interactive,” and “playable” are materially different milestones.

### 6.3 How names and behavior are recovered

Community reverse engineering generally combines:

- SDK headers and import libraries for structure and symbol names;
- hashing candidate symbol names and matching NIDs;
- decrypted firmware dynamic symbol tables and disassembly;
- on-console module enumeration and `dlsym`-like payload queries;
- call-site decompilation to infer argument layouts and ownership;
- differential probes against real hardware or firmware;
- known PS4 behavior where the ABI demonstrably carries forward;
- title traces, assertions, error paths, and output-memory snapshots;
- focused unit tests for each recovered invariant.

The hardest part is usually not finding a name. It is recovering the complete contract: accepted states, exact signed error code, which output fields are initialized on failure, whether a callback is immediate or queued, who owns the pointed-to memory, and what ordering is guaranteed.

The Astro work demonstrates a sound methodology. `docs/astrobot-bringup.md` correlates deterministic logs, firmware disassembly, shader dumps, scalar-buffer bindings, compute output, and Vulkan presentation rather than treating the last visible symptom as the root cause. The historical fork journal at `inspiration/acelogic-sharpemu/ASTRO_BOT_PROGRESS.md` and `inspiration/acelogic-sharpemu/docs/astro-bot/experiments.md` records decisive A/B experiments and rejected hypotheses.

## 7. What running a PS5 game on Windows actually requires

### 7.1 Definitive subsystem checklist

Difficulty here means difficulty of broad commercial-title compatibility, not the effort needed for a sample.

| Subsystem | Required contract | HLE/LLE choice | Difficulty | SharpEmu status on 2026-07-23 |
|---|---|---|---|---|
| CPU execution | Correct x86-64/SSE/AVX-family execution, guest ABI, TLS, atomics, faults, exceptions, self-modifying code policy, clocks | Direct execution with fault fixups; dynarec is an alternative | Very high | **Substantial, incomplete.** Native-only dispatcher exists; Windows VEH and AMD SSE4a `EXTRQ`/`INSERTQ` emulation are implemented. Precise guest exception/CPU-observation coverage is not complete. |
| Guest virtual/direct memory | Fixed-address reservations, mapping/protection/query, 16 KiB page expectations, direct allocations, aliases, CPU/GPU visibility | HLE memory manager using host VM | Very high | **Substantial, title-driven.** `PhysicalVirtualMemory` and kernel direct-memory APIs exist. Full aliasing, protection, and device-memory coherence remain ongoing. |
| ELF/SELF loader and dynamic linker | Decrypted SELF/ELF mapping, Sony headers/tags, TLS, relocations, module order, NID imports/exports/data symbols | HLE loader | High | **Functional for current decrypted targets.** No retail PKG install, license system, or encrypted-SELF decryption. |
| Kernel and pthreads | Threads, mutex/cond/rwlock/sema/eventflag/address wait, equeue, timers, callbacks, TLS/errno, file and VM syscalls | HLE on Windows threads/events | Extreme | **Broad partial coverage.** Enough for deep Astro boot; lost-wakeup and scheduler regressions show remaining semantic risk. |
| libc / `libSceLibcInternal` | Memory/string/math, allocation/mspace, stdio, errno, C++ guards, locale/runtime details | HLE using managed/native host facilities | High | **Partial.** `LibcMspaceExports.cs`, `LibcStdioExports.cs`, `LibcInternalExports.cs`, and `CxxAbiExports.cs` cover important paths, not a complete Sony libc. |
| AGC/PM4 command ingestion | Packets, registers, indirect buffers, waits/writes, queues, draw/dispatch/flip state | HLE command processor | Extreme | **Large title-driven implementation.** Many Gfx10/AGC packets and registers exist in `AgcExports.cs`; unsupported state and edge semantics remain. |
| gfx1013/Sony shader translation | Full scalar/vector/memory/image/control-flow ISA, EXEC masks, wave semantics, resources, NGG, interpolation and exports | Recompile to SPIR-V | Extreme | **Substantial, incomplete.** Decoder/IR/SPIR-V and NGG support render real guest work. Recent bugs in scalar operand 125 and SMEM bindings show fidelity gaps. |
| GPU resources and presentation | Tiling, formats, descriptors, aliases, render targets, compute writeback, barriers, labels, fences, VideoOut/vblank/flip | Vulkan HLE | Extreme | **First pixels and complex frames achieved.** `VulkanVideoPresenter` presents 4K guest frames. Auto-exposure writeback, some layout/coherency paths, correctness, and CPU-side object churn remain blockers. |
| Audio | AudioOut/AudioOut2 timing, formats and ports; NGS2 graphs; AJM codec jobs | HLE to Windows audio/software codecs | Very high | **Partial.** AJM ATRAC9 uses LibAtrac9 and AudioOut2 delivers real PCM; Astro's intro is audible. Selector audio is measured 37-43 dB quieter, not absent. Broader NGS2/AJM fidelity remains incomplete. |
| Video/media | AvPlayer lifecycle, demux/decode, callbacks, NV12 layout; Videodec jobs | HLE to FFmpeg/host codecs | High | **Mixed.** `AvPlayerExports.cs` uses in-process native FFmpeg by default and an external process only by explicit override. Astro's MP4 intro decodes; `Videodec*` remains much thinner compatibility behavior. |
| System services and offline platform | User profile, system state, NP/PSN, trophies, dialogs, app content, PlayGo | Usually faked offline HLE | High | **Broad compatibility layer.** NP signed-in state is now coherent enough for Astro's current path; many services still use synthetic success/state. |
| SaveData, Font, Net, Pad | Persistent mounts and metadata; glyph metrics/rasterization; sockets/HTTP/netctl; controller enumeration/input | HLE to host filesystem/fonts/network/HID | High | **Implemented in varying depth.** SaveData is host-backed, Font is extensive, Net has host adapters/stubs, Pad has XInput/DualSense readers. Astro currently does not load `libScePad`, so interaction is not yet proven. |
| Timing and scheduling | Monotonic/realtime/TSC consistency, timeout precision, fair progress, vblank/audio clocks, callback and fence order | HLE scheduler over host time/threads | Extreme | **Boot-capable but dominant risk.** Clock APIs, scheduler pumping, short-sleep policy, and TSC calibration exist; multiple bring-up failures were timing/order bugs. |

### 7.2 CPU: direct execution versus a dynarec

Direct execution is attractive because most ordinary game instructions run at native speed. The cost is that the guest is executing inside a Windows process:

- Prospero's AMD64 ABI is not identical to the Windows x64 host ABI;
- guest FS/TLS cannot be treated as CLR TLS;
- a guest access violation must become a guest-visible event or controlled emulator transition, not an unhandled Windows crash;
- illegal AMD-only instructions must be decoded or patched;
- import traps must preserve all guest state expected at the call boundary;
- host exceptions must be handled safely even when RSP points into a guest stack;
- page protection is simultaneously guest policy and a host fault mechanism.

Prosperismo's direct backend, import bridge, fault handler, and dispatcher address these problems. A dynarec would translate every basic block and could virtualize CPUID, faults, and instructions uniformly, but would need a complete decoder, code cache, invalidation, precise exceptions, and high-performance register allocation. There is no current dynarec in Prosperismo.

### 7.3 Memory: address identity before allocation convenience

For native guest code, the most important property is that a pointer value be usable and stable. The memory manager must therefore:

1. reserve the guest range without colliding with the CLR, DLLs, Vulkan mappings, or Windows internals;
2. honor fixed mappings and alignment;
3. distinguish reserved, committed, direct-allocated, and mapped state;
4. preserve aliases and per-mapping protection;
5. keep the loader, libc allocator, kernel VM APIs, and GPU resource tracker consistent;
6. commit large sparse ranges lazily;
7. turn host faults into actionable guest diagnostics.

The GPU makes this harder. A guest buffer may stay in ordinary mapped RAM; an image may be detiled and represented by a Vulkan image; a compute output may exist only in device memory until a later CPU read. The resource tracker must know which copy is authoritative and when a barrier or fence makes it visible.

### 7.4 Kernel HLE: signatures are the easy part

For each function, correctness includes:

- register/stack ABI and structure size/alignment;
- return value versus positive/negative errno conventions;
- output writes on success and failure;
- blocking and cancellation rules;
- recursive/error-checking mutex attributes;
- condvar predicate and wake behavior;
- absolute versus relative timeout and clock selection;
- handle lifetime and stale-handle errors;
- event coalescing and filter-specific payload;
- callback thread/context and reentrancy.

Host `Monitor`, `Semaphore`, `EventWaitHandle`, or `Thread` objects can implement the mechanism, but not automatically the guest semantics. Prosperismo's signal-epoch condvar repair is a concrete instance: all function names existed, yet one stolen signal stalled the engine.

Equeue deserves special emphasis. It is not just an I/O convenience. Games can route user events, GPU flip/vblank completion, timers, and service callbacks through the same wait loop. If registration, one-shot behavior, event data, or timeout units are wrong, an apparently unrelated subsystem stops progressing.

### 7.5 libc and C++ runtime

Many engines bring their own allocator and C++ runtime layers, but still depend on Sony libc for:

- mspace creation/allocation/free/reallocation and usable-size behavior;
- string/memory and secure variants;
- math and floating-point corner cases;
- FILE handles, formatted output, and filesystem-backed stdio;
- errno/TLS;
- locale and character conversion;
- `__cxa_guard_*` and destructor/atexit mechanics.

Returning success from allocation or stdio functions without correct output state creates delayed corruption. Prosperismo's current mspace and stdio implementations are real compatibility components, while `LibcInternalExports.cs` itself is still sparse. The relevant behavior is spread across libc and kernel compatibility files rather than one complete Sony-libc replacement.

### 7.6 GPU translation in detail

A commercial draw is a coupled state reconstruction problem:

1. follow indirect command buffers and packet lengths safely;
2. interpret writes to context, shader, and user-config registers;
3. reconstruct render targets, depth, viewport/scissor, blend, raster, topology, and index state;
4. find shader code and AGC headers;
5. decode gfx1013/Sony instructions and recover structured control flow;
6. track scalar provenance to discover descriptors and constants;
7. map buffer/image/sampler descriptors to Vulkan bindings;
8. reproduce wave/EXEC, subgroup, derivative, interpolation, export, and NGG semantics;
9. translate guest formats, swizzles, tiling, compression/metadata assumptions, and component order;
10. build/cache Vulkan pipelines and descriptor state;
11. issue equivalent barriers, draws, dispatches, copies, waits, and writes;
12. propagate GPU results back to guest memory when later CPU/GPU work requires them;
13. honor flip/vblank event ordering and present the correct display buffer.

An earlier Astro tonemap failure crossed several of these layers. `docs/astrobot-bringup.md` records three historical root feeds:

- scalar operand `125` is architectural NULL, not `s125`;
- the scalar evaluator recorded one `s_buffer_load` binding and zero-filled other constant buffers;
- a 1×1 auto-exposure compute result remained in device memory instead of becoming visible where the later pass expected it.

Each isolated mistake can produce the same black/grey final image. Those are
not the current complete root cause: later captures prove nonblack
full-resolution HDR and SMAA surfaces, and the old `0x50068FA00` black history
is pre-title transition work. This is why debugging at the final present call
alone is ineffective and why chronological checkpoints must be preserved.

Prosperismo also has a measured CPU-side presentation cost: the bring-up journal attributes roughly 570 ms per draw in one path to Vulkan object teardown while the GPU fence and present were comparatively small. Correctness and object-lifetime/cache design are therefore inseparable from usable boot progression.

### 7.7 Audio, video, and services

Audio is both data and scheduling. `src/SharpEmu.Libs/Audio/AudioOutExports.cs` can convert PCM and use a Windows multimedia backend, with silence pacing as a fallback. `Ngs2Exports.cs` models voices/graphs and `AjmExports.cs` exposes codec-job behavior. The implementation is not yet a complete Tempest/NGS2/AJM replacement. Astro's intro is audibly decoded; its selector signal is measured much quieter than the intro, so silence must not be inferred from perception alone.

`src/SharpEmu.Libs/AvPlayer/AvPlayerExports.cs` uses native FFmpeg libraries in-process by default, produces video/audio frames, and handles NV12 layout. Setting `SHARPEMU_FFMPEG_PATH` explicitly selects the external `ffmpeg`/`ffprobe` override. `docs/ffmpeg-bink2.md` documents the Bink2-capable host bridge and historical external build. The `Videodec` files currently provide more lifecycle and compatibility scaffolding than full Sony-codec job fidelity.

Offline platform services must form one coherent world:

- one current user and account identity;
- consistent NP initialized/signed-in/reachability state;
- callbacks that agree with synchronous getters;
- title/content IDs shared by AppContent, SaveData, trophies, and system service;
- PlayGo reporting installed chunks that actually exist;
- network and HTTP failures or offline success paths that the game expects.

“Offline” need not mean “no local PSN identity.” Many games expect a selected local user with an account ID and a logically signed-in profile while remote service reachability is unavailable. The HLE policy can choose that model or a fully signed-out model, but every synchronous query and callback must agree. Astro only advanced after NP state, callbacks, reachability, and user context told the same story. A mixture of “success,” “offline,” and “not initialized” is often worse than a consistently modeled state.

SaveData in `src/SharpEmu.Libs/SaveData/SaveDataExports.cs` maps guest mount points such as `/savedataN` to per-title host directories and implements metadata/memory/transaction behavior. Font has a large implementation in `FontExports.cs`. Network uses host facilities in places such as `Network/HttpExports.cs` while other paths remain synthetic. Pad support includes XInput and DualSense/HID readers under `src/SharpEmu.Libs/Pad`.

### 7.8 Why timing and scheduling dominate boot progression

Games initialize as graphs of asynchronous work. A typical boot can have:

- loader/module initialization on one thread;
- job-system workers waiting on address values or condvars;
- async filesystem and PlayGo callbacks;
- shader/pipeline compilation;
- GPU queue labels and fences;
- video decode callbacks;
- audio consumers pacing producers;
- vblank/flip equeue events;
- NP and user-service callbacks.

One missing wake, clock mismatch, callback on the wrong thread, or overlong host operation can leave all other code “correct” but idle. Conversely, an aggressive watchdog or fake wake can violate a predicate and create state corruption much later.

Prosperismo's kernel runtime pumps its scheduler around sleeps and treats very short `usleep` calls as yields instead of millisecond Windows sleeps. It calibrates TSC reporting, has monotonic/realtime clock paths, and queues synthetic events. The Astro journal supplies the decisive evidence:

- signal stealing in a condvar caused a lost wake;
- treating mutex acquisition as a generic yielding/import-loop boundary hid a real busy loop and starved scene registration in the fork journal;
- ordered flips needed a yield in the historical fork;
- expensive capture and Vulkan object teardown could starve visible progress.

Timing is therefore not polish applied after APIs and graphics. It is part of the ABI.

## 8. Minimal path to first pixels

### 8.1 Smallest viable subset

The shortest path to a real guest-generated frame is:

1. **Loadable content:** extracted `/app0`, plaintext/loadable `eboot.bin`, correct `param.json`, and required game SPRX files.
2. **Address space:** fixed mappings for code/data/TLS/stack, direct-memory allocation, and functional file paths.
3. **Dynamic link:** Sony dynamic tags, relocations, NID stubs, and enough code/data exports to finish constructors.
4. **Process runtime:** guest ABI, TLS/errno, clocks, basic libc, allocation, and exception containment.
5. **Thread progress:** thread creation plus the exact mutex/cond/sema/event/address-wait subset reached during startup.
6. **Coherent offline services:** user/system/NP/AppContent/PlayGo answers that let the title select an offline boot path.
7. **VideoOut:** open/register display buffers, create Vulkan, expose flip/vblank events, and present a cleared guest buffer.
8. **AGC queues:** allocate/submit command buffers, follow indirect buffers, and process waits/writes/draw/dispatch/flip packets.
9. **One complete graphics path:** reconstruct state, translate the first real shaders, bind resources, handle formats/tiling, and issue a Vulkan draw.
10. **Visibility and present:** make render/compute results visible in the right domain, complete fences/labels, and flip the correct guest display buffer.

Audio, video decode, SaveData, Font, Pad, and richer services are not intrinsically required for a triangle. A commercial game's boot graph may call or assert on any of them before its first draw, making them part of that title's practical minimum.

### 8.2 Typical ordered blockers

The useful order for investigating a non-rendering commercial title is:

1. **Encrypted or malformed input.** If load segments are not plaintext, stop; no HLE fix can make ciphertext executable.
2. **Mapping/TLS/relocation failure.** Confirm entry, module bases, TLS, GOT/PLT/data relocations, and initializer order.
3. **Unresolved NID.** Identify the exact import, library/module identity, signature, and whether it is code or data.
4. **ABI/output-shape error.** A “successful” stub that fails to initialize an output structure can poison later code.
5. **Allocator or filesystem failure.** Validate `/app0`, module paths, content metadata, alignment, and large direct-memory mappings.
6. **Thread/synchronization stall.** Inspect wait predicates, owning thread, event registration, clock basis, and the last producer that should wake it.
7. **Contradictory service state.** Make user, NP, network, content, and callbacks agree.
8. **Audio/media initialization gate.** Engines often assert on buses, decoders, or callback objects before rendering.
9. **Unsupported PM4/register state.** Find the first skipped packet or state write that changes a subsequent draw/dispatch.
10. **Shader decode/control-flow error.** Compare decoded operands, EXEC behavior, resource discovery, and exports against the ISA and a known-good oracle.
11. **Resource binding/format/tiling error.** Verify descriptor addresses, constant buffers, swizzles, pitch, image layout, and alias ownership.
12. **Synchronization/writeback error.** Prove whether the expected value exists in guest RAM, a Vulkan shadow, or device memory at each boundary.
13. **Presentation/performance starvation.** Confirm the frame was rendered before investigating swapchain output; separate GPU time from CPU object/capture overhead.
14. **Input gate.** Once a stable menu exists, verify that Pad is loaded, enumerated, and delivering events to the active user.

### 8.3 Astro Bot as the concrete path

`docs/astrobot-bringup.md` is the current authority. Its 2026-07-22 checkpoint says:

- the process boots stably and soft-continues past engine assertions;
- 4K guest frames are presented;
- menu assets load;
- the tonemap root cause is understood through both traces and a decrypted-firmware oracle;
- NP state and a condvar lost wake were repaired;
- the output is not yet a correctly rendered, interactive menu.

The two explicitly open functional gates are:

1. **audio/default bus:** `SoundManager.cpp:306` expects `defaultBusses.size() == 1`;
2. **real auto-exposure:** the 1×1 luminance compute result needs a correct GPU-to-guest/consumer visibility path rather than the diagnostic constant `0.25`.

Interaction is additionally unproven because the journal observes that `libScePad` never loads on the reached path. Performance work remains: CPU-side Vulkan object teardown is far more expensive than the measured fence/present in the cited capture.

The earlier acelogic fork journal is useful historical evidence, not the current Prosperismo status. `inspiration/acelogic-sharpemu/ASTRO_BOT_PROGRESS.md` and `inspiration/acelogic-sharpemu/docs/astro-bot/experiments.md` show earlier milestones—boot art, controller animation, PS Studios video, title start, a red frame—and document bugs in JSON ABI, resource coherency, shader semantics, selector scheduling, compute writeback, and Windows guest traps. The current tree has since moved to a different set of last-mile gates.

The practical lesson is that “first pixels” is a vertical slice, not a graphics-only task:

```text
decrypted image
  → loader/linker
  → TLS/libc/kernel
  → coherent services and worker scheduling
  → AGC command/state reconstruction
  → shader/resource translation
  → GPU/CPU visibility
  → VideoOut event and present
```

Every arrow is an observable contract. A commercial title runs only when the contracts form one coherent machine.

## Sources read locally

The following files and representative directories were actually inspected for this document.

### Prosperismo

- `docs/astrobot-bringup.md`
- `docs/env-flags.md`
- `docs/aerolib-catalog.md`
- `docs/ffmpeg-bink2.md`
- `src/SharpEmu.Core/Loader/ElfHeader.cs`
- `src/SharpEmu.Core/Loader/ProgramHeader.cs`
- `src/SharpEmu.Core/Loader/SelfImage.cs`
- `src/SharpEmu.Core/Loader/SelfLoader.cs`
- `src/SharpEmu.Core/Loader/ParamLoader.cs`
- `src/SharpEmu.Core/Runtime/SharpEmuRuntime.cs`
- `src/SharpEmu.Core/Cpu/CpuExecutionEngine.cs`
- `src/SharpEmu.Core/Cpu/CpuDispatcher.cs`
- `src/SharpEmu.Core/Cpu/GuestTlsImage.cs`
- `src/SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs` and its partials, especially `DirectExecutionBackend.Exceptions.cs` and `DirectExecutionBackend.Imports.cs`
- `src/SharpEmu.Core/Cpu/Native/Windows/WindowsFaultHandling.cs`
- `src/SharpEmu.Core/Memory/PhysicalVirtualMemory.cs`
- `src/SharpEmu.Core/Memory/VirtualMemory.cs`
- `src/SharpEmu.HLE/ModuleManager.cs`
- `src/SharpEmu.HLE/SysAbiExportAttribute.cs`
- `src/SharpEmu.HLE/Aerolib/Aerolib.cs`
- `src/SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelExtraCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelEventQueueCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelEventFlagCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelSyncOnAddressCompatExports.cs`
- `src/SharpEmu.Libs/Kernel/KernelModuleRegistry.cs`
- `src/SharpEmu.Libs/LibcInternalExports.cs`
- `src/SharpEmu.Libs/LibcMspaceExports.cs`
- `src/SharpEmu.Libs/LibcStdioExports.cs`
- `src/SharpEmu.Libs/CxxAbiExports.cs`
- `src/SharpEmu.Libs/Agc/AgcExports.cs`
- `src/SharpEmu.Libs/Agc/Gen5ShaderTranslator.cs`
- `src/SharpEmu.Libs/Agc/Gen5SpirvTranslator*.cs`
- `src/SharpEmu.Libs/Agc/NggPrimitiveShader.cs`
- `src/SharpEmu.Libs/Agc/Gfx10Detiler.cs`
- `src/SharpEmu.Libs/Agc/Gfx10UnifiedFormat.cs`
- `src/SharpEmu.Libs/VideoOut/VideoOutExports.cs`
- `src/SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs` and its partials
- `src/SharpEmu.Libs/Audio/AudioOutExports.cs`
- `src/SharpEmu.Libs/Audio/AudioOut2Exports.cs`
- `src/SharpEmu.Libs/Ngs2/Ngs2Exports.cs`
- `src/SharpEmu.Libs/Ajm/AjmExports.cs`
- `src/SharpEmu.Libs/AvPlayer/AvPlayerExports.cs`
- `src/SharpEmu.Libs/Videodec`
- `src/SharpEmu.Libs/Np`
- `src/SharpEmu.Libs/SaveData/SaveDataExports.cs`
- `src/SharpEmu.Libs/Font/FontExports.cs`
- `src/SharpEmu.Libs/Network`
- `src/SharpEmu.Libs/Pad`
- `src/SharpEmu.Libs/PlayGo`
- `src/SharpEmu.Libs/AppContent/AppContentExports.cs`
- `src/SharpEmu.Libs/UserService/UserServiceExports.cs`
- `src/SharpEmu.Libs/SystemService`

### Local reference projects

- `inspiration/KytyPS5/README.md`
- `inspiration/KytyPS5/src/graphics/guest_gpu`
- `inspiration/KytyPS5/src/graphics/shader/recompiler`
- `inspiration/KytyPS5/src/graphics/host_gpu`
- `inspiration/shadps5-rust/README.md`
- `inspiration/shadps5-rust/src/loader.rs`
- `inspiration/shadps5-rust/src/kernel.rs`
- `inspiration/shadps5-rust/src/kernel_hle.rs`
- `inspiration/shadps5-rust/src/gpu_queue.rs`
- `inspiration/shadps5-rust/src/graphics.rs`
- `inspiration/shadps5-rust/src/shader_translation.rs`
- `inspiration/shadPS4/src/core/loader/elf.h`
- `inspiration/shadPS4/src/core/loader/elf.cpp`
- `inspiration/shadPS4/src/core/loader/symbols_resolver.h`
- `inspiration/shadPS4/src/core/loader/symbols_resolver.cpp`
- `inspiration/shadPS4/src/core/libraries`
- `inspiration/shadPS4/src/core/libraries/kernel/equeue.cpp`
- `inspiration/shadPS4/src/core/libraries/kernel/memory.cpp`
- `inspiration/shadPS4/src/core/libraries/kernel/orbis_error.h`
- `inspiration/shadPS4/src/core/libraries/kernel/threads`
- `inspiration/shadPS4/src/core/libraries/libc_internal`
- `inspiration/shadPS4/src/common/types.h`
- `inspiration/shadPS4/src/common/assert.h`
- `inspiration/shadPS4/src/common/io_file.h`
- `inspiration/shadPS4/src/common/logging/log.h`
- `inspiration/shadPS4/src/core/file_format/psf.*`
- `inspiration/shadPS4/src/core/file_format/playgo_chunk.*`
- `inspiration/shadPS4/src/core/file_format/trp.*`
- `inspiration/shadPS4/src/core/file_format/pfs.h`
- `inspiration/shadPS4/src/core/libraries/libs.cpp`
- `inspiration/ps5-payload-sdk/README.md`
- `inspiration/ps5-payload-sdk/crt/kernel.c`
- `inspiration/ps5-payload-sdk/crt/rtld.c`
- `inspiration/ps5-payload-sdk/crt/syscall.h`
- `inspiration/ps5-payload-sdk/sce_stubs/genstub.py`
- `inspiration/ps5-payload-sdk/include/freebsd`
- `inspiration/ps5-payload-sdk/host/bin/prospero-nid.c`
- `inspiration/ps5-payload-sdk-legacy/include_bsd`
- `inspiration/ps5-agc-tutorial/README.md`
- `inspiration/ps5-agc-tutorial/Command-Buffer.md`
- `inspiration/ps5-agc-tutorial/Pipeline-State.md`
- `inspiration/ps5-agc-tutorial/Cache-Synchronization.md`
- `inspiration/ps5-agc-tutorial/Resource-Binding.md`
- `inspiration/ps5-agc-tutorial/Resource-Uploading.md`
- `inspiration/acelogic-sharpemu/ASTRO_BOT_PROGRESS.md`
- `inspiration/acelogic-sharpemu/docs/astro-bot/README.md`
- `inspiration/acelogic-sharpemu/docs/astro-bot/experiments.md`
- `inspiration/acelogic-sharpemu/docs/astro-bot/savedata-transferring-mount.md`

The maintained local `inspiration/ps5-payload-sdk/host/bin/prospero-nid.c` is
present. The absence recorded by the original audit applies only to
`inspiration/ps5-payload-sdk-legacy`.

## Further reading (general knowledge)

- [Sony: PS5 hardware technical specifications](https://blog.playstation.com/2020/03/18/unveiling-new-details-of-playstation-5-hardware-technical-specs/)
- [AMD/LLVM syntax of gfx1013 instructions](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX1013.html)
- [AMD/LLVM syntax of GFX10 RDNA1 instructions](https://rocm.docs.amd.com/projects/llvm-project/en/latest/LLVM/llvm/html/AMDGPU/AMDGPUAsmGFX10.html)
- [AMD GPUOpen announcement and ISA resources](https://gpuopen.com/news/rdna2-isa-available/)
- [PS5 Payload SDK successor repository](https://github.com/ps5-payload-dev/sdk)
- [Actual `prospero-nid.c` implementation](https://github.com/ps5-payload-dev/sdk/blob/master/host/bin/prospero-nid.c)
- [Cryptogenic PS5 IPv6 kernel exploit and research notes](https://github.com/Cryptogenic/PS5-IPV6-Kernel-Exploit)
- [Byepervisor hypervisor research](https://github.com/PS5Dev/Byepervisor)
- [Byepervisor hardwear.io 2024 presentation](https://hardwear.io/netherlands-2024/presentation/Byepervisor__Breaking_PS5_Hypervisor_Security.pdf)
- [fail0verflow's 2021 PS5 symmetric-root-key disclosure](https://x.com/fail0verflow/status/1457499576676634625)
- [PS5 Developer Wiki](https://www.psdevwiki.com/ps5/)
- [PS5 PUP community documentation](https://www.psdevwiki.com/ps5/PUP)
- [shadPS4](https://github.com/shadps4-emu/shadPS4)
- [RPCSX](https://github.com/RPCSX/rpcsx)
- [RPCSX running/decrypted-firmware notes](https://rpcsx.github.io/wiki/run/)
- [Original Kyty](https://github.com/InoriRus/Kyty)
- [FreeBSD `kqueue(2)`/`kevent(2)` manual](https://man.freebsd.org/cgi/man.cgi?query=kqueue&sektion=2)
- [Khronos SPIR-V specification](https://registry.khronos.org/SPIR-V/)
- [Khronos Vulkan specification](https://registry.khronos.org/vulkan/)
