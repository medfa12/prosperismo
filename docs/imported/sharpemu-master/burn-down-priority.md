# Burn-down priority — what a real title actually needs

Corrects the ranking in `docs/os-surface-gap.md`. Regenerate the underlying data with
`python3 scripts/nid_gap.py` (writes the gitignored `scripts/nid_gap.tsv`).

## The correction

`nid_gap.py` scores `UNSERVED` imports at **1000** — the highest weight in the ranking — on the
reasoning that a NID the title links but nobody serves must be the most urgent gap. That
reasoning is wrong here, and following the ranking as printed would waste a large amount of
effort on code no title ever executes.

Runtime observation from the boot log (recorded in project memory, not re-derived here):
**none of Astro Bot's 112 UNSERVED imports has ever been observed executing.** Two of the
largest families were already investigated and killed as leads:

- **libSceJson2** — 0 of 15 iterator NIDs ever called. Investigated, then dropped, *after* a
  container model had been designed for them.
- **libSceAmpr** (44 NIDs) + the 5 `sceKernelAprResolveFilepaths*` — dead linked glue with zero
  callers. `sceKernelAprResolveFilepaths*` is what the ranking puts at the very top, score 1280.

This is the project's own method rule #8 in action: *ask the import table whether the title even
calls something before implementing it* — and its companion, that a static "who calls this"
argument is not sufficient without a runtime hit count.

## The real split

Astro Bot's 305 distinct non-covered imports, by how they are actually served:

| Routing | Distinct NIDs | Worth implementing? |
|---|---|---|
| **HLE, but our implementation is a stub** | **78** | **Yes — these are called and silently do nothing** |
| UNSERVED | 111 | No — never observed executing |
| LLE (game ships its own `libc.prx`) | 116 | No — already served by the title's own module |

A stub is the dangerous category, not the missing one. A missing NID fails loudly at the import
table; a stub returns success and the guest proceeds on a false premise.

## The 78 that matter, by library

| Count | Library | Notes |
|---|---|---|
| 25 | libSceFont | The writing/renderer/text-source family: `sceFontCreateWritingLine`, `sceFontBindRenderer`, `sceFontSetupRenderScalePixel`, `sceFontTextSourceInit`, `sceFontGlyphDefineAttribute`, … This is text rendering, so it gates any menu with text. Note `libSceFreeType` (375 exports) has **zero** registrations. |
| 16 | libSceAudioPropagation | Every `*Init` in the file. Historically identified as a menu gate: `SystemQueryMemory` → `SystemCreate` → `RoomCreate` failing left the title waiting on a room-ready signal that never came. |
| 9 | libkernel | Includes `scePthreadRwlockTryrdlock`/`Trywrlock`. |
| 4 | libSceNpUniversalDataSystem | `Initialize` reads 16 bytes and returns 0, initialising nothing. |
| 3 each | libSceLibcInternal, libSceSigninDialog, libSceMouse | |
| 2 | libSceIme | |
| 1 each | libSceContentExport, libSceShare, libSceNpManager, libSceRudp, libSceNpCommerce, libSceSystemService, libSceAgcDriver, libSceNetCtl, libScePad, libSceAcm, libSceAgc, libSceSaveData_native, libSceNpSessionSignaling | |

## Caveat on the UNSERVED verdict

"Never observed executing" means *never observed in a boot that never reached the menu*. It is
evidence about the boot path, not proof the function is dead for all time. In particular the
5 UNSERVED `libSceAgc` command-buffer entries (`sceAgcDcbCopyData`, `sceAgcAcbCopyData`,
`sceAgcCbDispatchGetSize`, `sceAgcCbSetShRegisterRangeDirectGetSize`, `sceAgcDebugRaiseException`)
are plausibly reached only once the title does more GPU work than it currently gets to — and the
two `*GetSize` entries are sharp, because the guest sizes its command buffer from the return
value. Treat GPU-side UNSERVED entries as deferred, not dead.

Everything else in the UNSERVED bucket should stay untouched until a boot trace shows a call.

## The STUBBED bucket over-counts, and here is the proof

`nid_gap.py` classifies an export as STUBBED when its body has no detectable effect. That
heuristic cannot distinguish **"does nothing"** from **"correctly does nothing"**, and at least
two of the remaining reachable "stubs" are in the second category:

- **`_init_env`** (`bzQExy189ZI`, libSceLibcInternal) — the real export has `st_size == 1` and
  that byte is `0xC3`. A no-op is byte-exact.
- **`sceNetCtlTerm`** (`Z4wwCFiBELQ`) — same: dynsym size 1, body `0xC3`. Independently
  disassembled twice. Clearing state here would be the divergence, not the fidelity.

Several more of the eleven are pure getters that were implemented against firmware in this round
(`sceKernelGetTscFrequency`, `sceKernelGetProcessTimeCounterFrequency`, `sceKernelGetProcessTime`,
`sceKernelGetDirectMemorySize`, `scePthreadYield`) and still score as effect-free because they
return a value without writing guest memory.

A verifier separately established that the `has_effect` detector misses every mutation of a
`_camelCase` static — the dominant convention in this codebase — misclassifying 294 of 733
"reads-and-validates-only" exports in one direction alone.

**So the honest reading of the remaining reachable list is closer to four genuinely open items:**
`sceAgcCbNopGetSize`, `sceAgcDriverRegisterResource`, `sceShareTerminate`, and
`sceSystemServiceHideSplashScreen` — the last of which is interesting, because a boot log that
reports `hid splash=0` is exactly the symptom of it not doing its job.

Do not burn effort "fixing" a function whose firmware body is a single `ret`. Check the export's
`st_size` first.

## Known-red: the 6 AudioOut2 tests

These have failed at `HEAD` for the whole of this work and are **not** caused by it. They are
listed here so the next person does not re-derive the situation from scratch.

| Test | Expects | Gets |
|---|---|---|
| `PortGetState_WritesFixedSizeIgnoringPollutedR9:42` | 1 | 192 |
| `PortGetState_MainPort_ReportsOneAvailableStereoEndpoint:117` | 1 | 192 |
| `PortGetState_BgmPort_IsNotBoundToPrimaryStereoEndpoint:132` | 0 | 192 |
| `PortGetState_SkipsGuestStackOutBuffer:60` | 0 | `0x8002000E` |
| `GetSpeakerInfo_WritesFixedSizeToRdiNotRsiTypeFlag:82` | 2 | 1 |
| `GetSpeakerInfo_WritesOnlyFixedTwentyByteDescriptor:186` | 2 | 1 |

Two separate disputes, and they do **not** resolve the same way:

1. **The 192 group.** `192 == 0xC0 ==` connected (bit 6, `0x40`) `|` ready (bit 7, `0x80`). That is
   the contract already recorded as firmware-verified: the engine does `shr al,6 / and al,1`, so
   connected is `0x40` and never `0x01`, and a healthy port arrives with the ready bit set because
   firmware only ever clears it. The tests encode the disproven pre-fix assumption. **The code
   looks right and the tests look stale** — but note the third row expects `0`, i.e. it asserts the
   port is *not* connected, which is a different claim from the other two and needs its own read.
2. **The speaker-info group (2 vs 1). CANNOT BE SETTLED STATICALLY — stop trying.**
   Disassembled in full. `sceAudioOut2GetSpeakerInfo` clears 0x50 bytes at `0x20461`
   (`mov esi,0x50`), then fills the descriptor with three `vmovups ymm` stores to `[r15+0]`,
   `[r15+0x20]` and `[r15+0x30]` from one of two rodata blocks:
   - **Default path (`0x2055e`)** sources `[r15+0]` from vaddr `0x9c24c`, which is **beyond
     `p_filesz` — it is not in the file image at all.** That memory is populated by relocations
     at load, so its contents are unknowable from the dump. This is the same trap the project
     already documented for vtables and RTTI.
   - **Fallback path (`0x205e1`)** sources `[r15+0]` from vaddr `0x558f8`, which *is* static:
     `03 00 00 00 ff 00 00 00 ...`, then overwrites `[r15+8]` with 1. A third path at `0x20617`
     writes `mov byte ptr [r15], 3`.

   So the two static paths both put **3** in byte 0 — which matches neither the test's 2 nor the
   code's 1. The note that "the engine accepts only 1 or 2" describes the *guest's* check, not
   what firmware writes. Settling this needs a RUNTIME read of the descriptor on the VM, not more
   disassembly. Anyone who "fixes" these two tests from static evidence is guessing.
3. `PortGetState_SkipsGuestStackOutBuffer` now surfaces `0x8002000E` rather than the old
   `0x80020101` purely because `ORBIS_GEN2_ERROR_MEMORY_FAULT` was corrected to EFAULT. It expected
   `0` before that change and still does; the failure is unchanged in kind.

**What settles it:** `libSceAudioOut.sprx` is cleartext at
`filesystems/system/common/lib/`. `sceAudioOut2GetSpeakerInfo` (NID `DImz2Ft9E2g`) is at vaddr
`0x203f0`, size 626, file offset `0x243f0`; `sceAudioOut2PortGetState` (NID `gatEUKG+Ea4`) is at
vaddr `0x41f80`, size 808, file offset `0x45f80`. Disassemble both in full and read what each
writes. One fact already extracted: `GetSpeakerInfo` clears **0x50 bytes** of the out buffer at
`0x20461` (`mov esi,0x50` ahead of the call), independently confirming the 0x50-byte SpeakerInfo
that a past refactor had shrunk to 0x20.

Do not "fix" these by adjusting whichever side is more convenient — a green suite protecting the
wrong contract is exactly how the invented-layout regression survived 1038 passing tests.

## Denominator honesty

The headline coverage figure (15.27%, capped at ~13.0% once validate-and-return no-ops are
excluded) is measured against the whole game-facing SDK export surface, 17904 NIDs. That is the
right denominator for "is the Prospero OS in the emulator". It is the *wrong* denominator for
"can this title run" — for that, the number is 78 reachable stubs plus whatever the boot
uncovers next. Do not conflate them: the first is the goal, the second is the path.
