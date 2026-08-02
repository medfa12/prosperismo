# FirstWave background translation

This note records the directly implementable translation of the persistent
FirstWave plate in firmware 12.40. The corresponding portable C++ evaluator is
`frontend/ProsperismoLauncher/windows/Prosperismo/FirstWaveBackground.*`.

## Source and confidence

The source is the user-owned 12.40 image at
`system_ex/app/NPXS40087/eboot.bin`. The standalone pixel program begins at file
offset `0x11F9300` and ends at `0x11F952C`. A themed variant performs the same
evaluation at `0x11F8240` and applies packed colour overlays at
`0x11F8438..0x11F8494`. These ranges were decoded with Sony's SDK 10
`libSceShaderIsaP` disassembler.

The following is exact before the final fp16 export, subject only to normal
CPU/GPU transcendental precision differences:

```text
p.x = (2 * SV_Position.x / width  - 1) / projection[0][0]
p.y =-(2 * SV_Position.y / height - 1) / projection[1][1]
d    = normalize(float3(p, 1))
u    = saturate(0.5 * d.x + 0.5)
v    = saturate(0.5 * d.y + 0.5)

pixel = uint(width * SV_Position.y) + uint(SV_Position.x)
n0 = hash(pixel + int(23189 * fract(time)), 17387,  789221)
n1 = hash(pixel + int(13181 * fract(time)), 15731, 1300237)

foldY = 1.7 * v + n0 - 1.35
foldX = u + n1
angle = 2*pi * 0.047746479511260986 * time
lightCentre = -0.33 * float2(cos(angle), sin(angle)) - 0.66
r2 = square(foldX + lightCentre.x) + square(lightCentre.y - foldY)
light = exp2(-14.426950454711914 * r2)

colour = BackgroundColour0
       + (1 + foldY) * (BackgroundColour1 - BackgroundColour0)
       + BackgroundLightColour * light
colour = saturate(colour)
```

`v_exp_f32` is base-2. The Gaussian is therefore algebraically
`exp(-10*r2)`, not `exp(-14.42695045*r2)`.

The two hash functions are separate 32-bit wrapping integer polynomials:

```text
hash(seed, multiplier, addend):
    seed ^= seed << 13
    value = seed * (seed * seed * multiplier + addend) + 1376312589
    return float(value & 0x7fffffff) * 4.656612768993984e-12
```

The themed program then composites each packed premultiplied RGBA8 value in
order as `colour = colour * (1 - alpha) + rgb`. Opacity is applied last and is
also exported as alpha.

## Constant-buffer contract

The shader reads the projection diagonal at `Constants + 0x40` and `+0x54`,
BackgroundColour0/1 at `+0x110/+0x120`, BackgroundLightColour at `+0x130`,
opacity/time at `+0x180/+0x184`, and the integer render width/height at
`+0x190/+0x194`. The C++ API deliberately requires both projection values;
inventing a fixed field of view would no longer be a firmware translation.

The native reset path selects palette record 4 and uploads its signed channels
divided by 255:

| field | signed byte-domain vector |
|---|---|
| BackgroundColour0 | `(-20, -20, -10, 255)` |
| BackgroundColour1 | `(81, 160, 245, 255)` |
| BackgroundLightColour | `(22, 57, 79, 255)` |
| ReflectionColour | `(90, 60, 230, 255)` |
| EnvironmentMapColour | `(15, 15, 15, 255)` |
| EdgeColour | `(123, 123, 123, 255)` |

Only the first three vectors are consumed by `fw_background_p`; the others are
shared with the FirstWave mesh/OIT stages.

## Integration boundary

`RenderBackgroundBgra8Premultiplied` renders directly into the same BGRA8
premultiplied format used by the RNW drawing surface. It is a validated
FirstWave fallback/pass seam, not the primary SharpEmu shell plate. The
recovered SharpEmu route instead uses the separately ported Plane2
`wave_bg_p` evaluator; see
[`native-wave-plate-port.md`](native-wave-plate-port.md). The separately
recovered particle producer remains a Home-only additive layer above the
primary plate.

This module closes the plate math, not the entire FirstWave stack. The animated
folded mesh still requires `fw_flow_vl/h/dv`, OIT composition, blur, and FXAA.

## Firmware 12.40 program contract

The missing stack is no longer being treated as an inferred visual effect.
`tools/Prosperismo.NativeBackgroundProducer/FirstWaveFirmwareProgram.cs` reads
all eleven FirstWave AGC programs directly from the user's decrypted
`NPXS40087/eboot.bin`. It validates the shader registry name, AGC `1234`/version
24 header, stage type, declared code length, and a SHA-256 fingerprint of every
declared instruction span before exposing any bytes to a renderer. Prosperismo
therefore contains offsets and fingerprints, but no copied Sony shader bytes.

The 12.40 pass order and resource contracts recovered from those programs are:

| pass | recovered resources / output |
|---|---|
| `fw_flow_vl` | FirstWave control-point input and shared constant buffer; local half of the tessellation pipeline |
| `fw_flow_h` | root pointer in `s[0:1]`, descriptors at `+0x20/+0x30`, LDS reads, buffer stores; hull half |
| `fw_flow_dv` | root `s[12:13]`, constants through `s[8:11]` at `+0x00/+0x80/+0xC0`, sixteen `buffer_load_dwordx4` reads from `s[0:3]`; exports position plus five parameters |
| `fw_oit_p` | constants through `s[8:11]` at `+0x110/+0x150/+0x170/+0x184/+0x188/+0x18C/+0x190`; OIT UAV `s[0:3]`, count UAV `s[4:7]`, atomics and MRT0 |
| `fw_comp_oit_p` | constants through `s[8:11]` at `+0x40/+0x110/+0x130/+0x180/+0x184/+0x18C`; OIT buffers `s[0:3]` and `s[4:7]`, MRT0 |
| `fw_blurh_p`, `fw_blurv_p` | image `s[0:7]`, sampler `s[8:11]`, constants `s[12:15]`, fourteen samples, MRT0 |
| `fw_fxaa_p` | image `s[0:7]`, sampler `s[8:11]`, gather/sample/resinfo, MRT0 |
| `fw_background_p` | constants `s[0:3]`: projection `+0x40`, colours `+0x110/+0x120/+0x130`, opacity/time `+0x180`, dimensions `+0x190`; MRT0 |

Run the validator without copying anything out of the oracle:

```powershell
Prosperismo.NativeBackgroundProducer --validate-firstwave <path-to-NPXS40087-eboot.bin>
```

`--compile-firstwave-post` additionally decodes and translates the six original
pixel programs (`blurh`, `blurv`, OIT write, OIT resolve, FXAA, and background)
to SPIR-V at runtime. The 12.40 oracle currently produces respectively
`0xB38C`, `0xB38C`, `0x2C4EC`, `0xFFF4`, `0x2C2F0`, and `0x74B8` bytes. Those
are translated Sony instruction streams, not rewritten look-alike shaders.

```powershell
Prosperismo.NativeBackgroundProducer --compile-firstwave-post <path-to-NPXS40087-eboot.bin>
```

The current Vulkan translator now accepts every FirstWave pixel stage, plus
ordinary vertex and compute programs. It does not yet expose the firmware's
local/hull/domain execution model, so the validated tessellation stages must
not be relabelled as ordinary vertex shaders or replaced with a hand-authored
mesh. Until that backend work lands, the native RNW view's `fw_background_p`
output is an exact plate-only fallback, not a claim that the full dark
folded-room renderer is complete.
Do not describe the result as the complete native background until those passes
are translated and visually validated.

## React Native Windows ownership

`ShellBackgroundSurface.tsx` is the persistent React owner. It checks RNW's
`hasViewManagerConfig` before evaluating the code-generated native component,
so a missing Fabric registration cannot replace the shell with a white client.
The native implementation similarly uses `try_as` for every optional
Composition interface and becomes a transparent no-op if the required bridge
is unavailable.

The native view is mounted at one stable location beneath every Big Picture
route. Its presentation contract is deliberately only two states:

| Shell state | Layer mask | Native result |
| --- | ---: | --- |
| unobscured Home | `3` | FirstWave plate plus additive particle stream |
| Settings, Library, or any modal | `1` | persistent FirstWave plate; particles hidden |

Selected-title artwork is a separate low-opacity image layer with the recovered
633.333ms linear handoff. It does not replace or recolour the native plate.
When the development producer is launched, its runtime particle inputs resolve
from `C:\prosperismo\ps5oracle`: the 4.03 firmware reconstruction supplies the
two BGLayer GNF textures and the oracle draw cache supplies the recovered
descriptors/buffers. None is copied into this repository or application
package. Automatic packaged-producer lifecycle remains a separate deployment
task; absence of that helper leaves the translated FirstWave plate running.
