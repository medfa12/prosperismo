<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# Guest probes — asking questions without rebuilding

A hand-written diagnostic costs an edit, a rebuild, a redeploy and a boot to
answer **one** question about guest state. On the Astro Bot audio investigation
that loop measured 6–13 minutes per question, and each commit added a single
`TryReadUInt64`.

A probe spec is a JSON file read at startup. One boot answers every question the
file asks, and asking a new one costs a text edit.

```bash
SHARPEMU_PROBE_SPEC=probes/astro-sound.json SharpEmu /path/to/eboot.bin
```

Output goes to the normal log, one line per dump:

```
[PROBE][export:sceAudioOut2PortGetState#0] port=0x3(3)
[PROBE][sound-assert#0] descriptors=vector@0x3009A4920 begin=0x0 end=0x0 cap=0x0 count=0 EMPTY
[PROBE][sound-assert#0] buses=list@0x30061CE10 [0]@0x30061CE10{id=0x7 payload=0x300617900} … count=9 CIRCULAR
```

The shape is stable `key=value`, so two boots diff directly and any field greps
without a parser.

## Where probes fire

### Export sites — work today, no code change

Any HLE export is a site, named `export:<name>` or `export:<nid>`. It fires on
entry; append `:ret` for a site that fires after the call, with `ret` bound to
the return value.

```jsonc
{ "name": "export:sceAudioOut2PortGetState",
  "dumps": [ { "label": "port", "at": "arg0", "as": "value" },
             { "label": "state", "at": "arg1", "as": "hex", "len": "0x40" } ] }
```

Arguments are bound as `arg0`..`arg5` and by register name (`rdi`, `rsi`, …),
following the SysV integer argument order. An export that no site names keeps its
original delegate, so an uninstrumented boot pays nothing.

### CPU sites — one line where you want it

Anywhere you can build a scope, fire a named site:

```csharp
if (GuestProbeEngine.WillFire("sound-assert"))
{
    var scope = new GuestProbeScope(new GuestProbeMemory(context.Memory))
        .Define("r14", unwind.R14);
    GuestProbeEngine.Fire("sound-assert", scope);
}
```

That line never changes as the questions change — the spec carries the questions.

## Address expressions

`at` is an address expression, not a bare offset:

| Expression | Meaning |
| --- | --- |
| `0x800DC0500` | literal |
| `sound` | a site anchor, or a register bound by the call site |
| `sound+0x2660` | anchor plus offset |
| `[sound]` | dereference — here, the object's vtable pointer |
| `[sound+0x2770]+0x10` | chase a pointer, then offset into what it points at |
| `sound+0x2730-8` | subtraction (a `std::vector` helper receives `begin - 8`) |

Anchors are declared per site and resolve against the call site's own names, so
dumps read in terms of the object under study rather than whichever register
happens to hold it:

```jsonc
"anchors": { "sound": "r14" }
```

## Dump kinds

| `as` | Renders |
| --- | --- |
| `value` | the resolved expression itself, no memory read — for registers, handles, return codes |
| `u8` `u16` `u32` `u64` `i32` `i64` `f32` `f64` | scalars |
| `ptr` | a pointer, flagged `(not-a-pointer)` when the value cannot be one |
| `hex` | `len` bytes, 16 per group |
| `cstr` | NUL-terminated UTF-8 |
| `stdstring` | libc++ `std::string`, tagged `(sso)` or `(heap)` |
| `vector` | libc++ `std::vector`: begin/end/capacity, element count, `EMPTY`, `MISALIGNED` |
| `list` | intrusive list walked via `next`, bounded, reports `CIRCULAR` / `TRUNCATED` |
| `array` | `count` elements of `stride` bytes |
| `struct` | named `fields` at fixed offsets |

`vector`, `list`, `array` and `struct` take `fields`, whose `at` is relative to
the element (`"+0x10"`).

Deliberate behaviours, each of which has cost time to get wrong before:

- An empty vector reports `count=0 EMPTY`, never `<unreadable>` — "the builder
  appended nothing" and "the vector is unmapped" are different findings.
- `end - begin` not divisible by `stride` reports `MISALIGNED` rather than a
  rounded count, because it means the assumed element size is wrong.
- A list walk is bounded and detects revisits, so a corrupt list cannot spin
  inside a fault handler.
- A small integer in a pointer field is flagged rather than chased — `0xF` is a
  short-string capacity marker, not an address.
- A dump crossing the end of a mapping reports its readable prefix as
  `(partial N)` instead of failing whole.

## Budget

`maxHits` caps how often a site fires (default 4) and `everyNth` samples it. A
site on a per-frame path without a budget will flood the log and slow the boot it
was meant to explain.

## Cost profile — spending less per boot

Bring-up and presentation want opposite things. `SHARPEMU_FAST_BOOT=1` tells the
title it is on an SDR display and caps the display buffer to 1080p, so the title
itself takes the cheaper path instead of rendering pixels that are then discarded.

| Variable | Effect |
| --- | --- |
| `SHARPEMU_FAST_BOOT=1` | SDR + 1080p cap |
| `SHARPEMU_HDR=0` / `=1` | force the HDR capability reported to the title |
| `SHARPEMU_MAX_WIDTH`, `SHARPEMU_MAX_HEIGHT` | cap the display buffer |

Individual variables override the master switch, so "fast boot, but keep HDR" is
expressible. Defaults are full quality: unset, nothing changes. The profile is
printed once at the top of every log —

```
[COST] fastBoot=on hdr=off maxRes=1920x1080
```

— and a clamp is logged when it happens, so a reduced resolution is never
discovered from a screenshot.

## Tests

`tests/SharpEmu.Diagnostics.Tests` covers the engine against the layout it was
built to explain, and validates every spec under `probes/`. It runs on any host
in about 20 ms:

```bash
dotnet test tests/SharpEmu.Diagnostics.Tests/SharpEmu.Diagnostics.Tests.csproj
```

It is registered in `SharpEmu.slnx`, so CI runs it.

Note that `src/SharpEmu.Tests` is **not** in the solution and does not compile —
its memory fakes predate interface members added to `IGuestAddressSpace` and
`IGuestMemoryAllocator`, and several shader tests reference types removed by the
upstream presenter rewrite. The live suites are the four under `tests/`.
