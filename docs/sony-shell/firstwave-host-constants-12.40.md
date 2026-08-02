# FirstWave 12.40 host constants

This closes the literal host-data gap around `FirstWave::Initialize()` at VA
`0x000c41e0`, the constant-buffer builder at `0x000c5d00`, and the update/control
seed path at `0x000c6c70..0x000c792a` in the user-owned NPXS40087 12.40
executable. The full machine-readable evidence is
[`evidence/firstwave-host-constants-12.40.json`](evidence/firstwave-host-constants-12.40.json);
the bit-exact C++ form is
`frontend/ProsperismoLauncher/windows/Prosperismo/FirstWaveFirmware1240Constants.h`.

## Reset camera and projection upload

The native target is `3840 x 2160`. Executing the host builder through its
`0x19c`-byte upload boundary with the constructor/reset state produces these
row-major float32 bit patterns:

```text
worldViewMatrix @ +0x000
3f800000 00000000 80000000 00000000
80000000 3f800000 80000000 00000000
00000000 00000000 3f800000 00000000
80000000 80000000 c4611ccd 3f800000

worldProjectionMatrix @ +0x040
3fdbbf35 00000000 00000000 00000000
00000000 3fdbbf35 00000000 00000000
80000000 80000000 bf83759f bf800000
00000000 00000000 c3cab3e5 00000000

cameraPosition @ +0x100
00000000 00000000 44611ccd 3f800000
```

The important decimal values are projection X/Y `1.71677268`, projection Z
`-1.02702701`, depth translation `-405.405426`, view translation
`-900.450012`, and camera Z `900.450012`. Negative-zero words are retained
because this is an evidence table, not a visually equivalent approximation.
The JSON also records the exact world, world-view-projection, and screen words.

This is the exact **reset upload**. `0x000c5d00` subsequently derives animated
camera/model matrices from object time and transition state, so these reset
matrices must not be presented as constants for every frame.

## Palette upload

The constructor seeds six records of six vectors each at object offsets
`0x110..0x340`, then reset selects record `4`. Its six vectors are the already
identified signed-byte-domain values divided by 255:

```text
BG0        (-20, -20, -10, 255)
BG1        ( 81, 160, 245, 255)
light      ( 22,  57,  79, 255)
reflection ( 90,  60, 230, 255)
environment( 15,  15,  15, 255)
edge       (123, 123, 123, 255)
```

The transition step written at `0x000c4d0e` is bit pattern `0x3b5a740f`
(`0.0033333336468786`). The reset interpolator at VA `0x00efbb40` is exactly
`(0, 0.3000000119, 0.6399999857, 0.2000000030)`.

## Control seeds

The straight-line seed block at `0x000c6f33..0x000c7929` contains only
`vmovups` loads/stores. It materializes:

- a row-major `11 x 15` lattice: 165 vec4 values at stack offsets
  `-0xa80..-0x40`;
- a 13-pair boundary ring: 26 vec4 values at `-0xc20..-0xa90`, alternating the
  `z=-1` and `z=0` member of each pair.

Every entry in the JSON and C++ table carries its source VA and four exact
IEEE-754 words. The later code deforms these seeds before producing the
16-control-point tessellation patches; the seed table is not itself one patch.

## Verification

Run the evidence verifier against the preserved oracle executable:

```powershell
python scripts/verify_firstwave_host_constants.py `
  --eboot "C:\prosperismo\ps5oracle\sony\12.40 system dump\system_ex\app\NPXS40087\eboot.bin"
```

It checks the executable SHA-256, three instruction-range hashes, all 227
palette/control source vectors, reset immediates, table shapes, projection
sentinels, and JSON/C++ synchronization. The checked executable SHA-256 is
`18c9320be767a540578e54cb769f94996c3f37a4f158ef977ebfb798ffd6b04f`.
