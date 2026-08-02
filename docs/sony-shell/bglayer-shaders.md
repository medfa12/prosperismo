# PS5 home-screen background: shader-level ground truth

Every number below was read out of Sony's data with `tools/shader-recon`.
Each one cites firmware version, file and offset. **Anything not read out of
the data is marked UNRECOVERED and is not filled in.**

Source of every measurement in this document unless stated otherwise:

> firmware **4.03**, `system_ex/app/NPXS40087/eboot.bin`
> (16,449,968 bytes). All offsets are *file* offsets into that image.
> `vaddr = file_offset - 0x4000` for every `PT_LOAD` in it.

---

## 0. The shader set

The eboot embeds **160** AMDGPU ELFs (`e_machine == 224`). **122** of them are
named by the descriptor array described in `tools/shader-recon/README.md`; the
other 38 (the stereo-camera / video-decode compute kernels at
`0xc82874`-`0xca0374` and the display-pipeline shaders at `0xd500d0`-`0xd64100`)
are registered by a different mechanism and are UNNAMED.

Stage census: 101 ps, 38 gs/vs, 19 cs, 1 hs_back, 1 vs_ls.

The background-relevant ones:

| offset | stage | name | code | notes |
|---|---|---|---|---|
| `0xd6d720` | ps | `dual_wave_bg_p` | 528 | `Constants` cbuffer |
| `0xd6e080` | ps | `dual_wave_p` | 400 | |
| `0xd6e9b0` | vs | `dual_wave_vv` | 3136 | |
| `0xd70f00` | ps | `fxaa2_p` | 976 | |
| `0xd719b0` | vs | `fxaa2_vv` | 400 | |
| `0xd76550` | ps | `particle_boids_p` | 528 | *boids*, unrelated to BGLayer |
| `0xd76dc0` | vs | `particle_boids_vv` | 10512 | |
| `0xd7d0f0` / `0xd7d9e0` | ps/vs | `smaa_blend_*` | | SMAA, not FXAA |
| `0xd7e260` / `0xd7eba0` | ps/vs | `smaa_edge_*` | | |
| `0xd7f390` / `0xd801e0` | ps/vs | `smaa_weight_*` | | |
| `0xd81610` / `0xd81c20` | ps/vs | `wave2_depth_*` | | mesh depth pre-pass |
| `0xd82c20` | ps | `wave2_p` | 3152 | |
| `0xd84370` | vs | `wave2_vv` | 2976 | skinned mesh VS |
| **`0xd85790`** | **ps** | **`wave_bg_p`** | **1696** | **the M1 plate** |
| `0xd86510` | vs | `wave_bg_vv` | 288 | |
| `0xd86c80` / `0xd87700` | ps/vs | `wave_fxaa_*` | | |
| `0xd9bb40` | ps | `fw_fxaa_p` | 3008 | |
| **`0xd9f1b0`** | **cs** | **`particle_c`** | **29760** | **the particle simulation** |
| **`0xda78d0`** | **ps** | **`particle_p`** | **1840** | |
| **`0xda8ec0`** | **vs** | **`particle_vv`** | **1600** | |
| **`0xdaa420`** | **ps** | **`large_particle_p`** | **1968** | |
| **`0xdabbc0`** | **vs** | **`large_particle_vv`** | **1472** | |

Only the five bold `particle*` shaders carry the `BackgroundLayer::` C++
namespace in their reflection data. The `wave*` and `dual_wave*` shaders do not.

---

## 1. M4 - the wave-vs-particle question: **settled, it is particles**

Evidence, all from 4.03:

1. There **is** a particle compute shader: `particle_c` at `0xd9f1b0`, 29,760
   bytes of ISA - by a wide margin the largest shader in the eboot.
2. `particle_c`, `particle_vv`, `particle_p`, `large_particle_vv` and
   `large_particle_p` are the **only** shaders in the entire eboot whose
   reflection structs are namespaced `BackgroundLayer::`
   (`BackgroundLayer::SRTCs`, `::ResourcesCs`, `::ParticleProperty`,
   `::SRTVsPs`, `::ResourcesVsPs`, `::SRTLargeParticleVsPs`,
   `::ResourcesLargeParticleVsPs`, `::ParticleRendezVousParam`,
   `::ParticleBlendParam`, `::LightSourceProperty`, `::CameraProperty`).
   Nothing named `wave*` is in that namespace.
3. `large_particle_p` samples exactly two 2-D textures, `backgroundTex` and
   `backgroundTex2` - the pair `Sce.Vsh.ShellUI.BGLayer.Particle0.gnf` /
   `Particle1.gnf`, whose paths are at `0xb1a588` and `0xb273f9`.
4. **Nothing consumes a mesh named wave0/wave1.** The only two references are
   the literal strings `/app0/wave/wave1.fbxd` (`0xb118fb`) and
   `/app0/wave/wave0.fbxd` (`0xb38a04`). There is **no `wave/` directory under
   `system_ex/app/NPXS40087/`** in the 4.03 dump and **no `.fbxd` file anywhere
   in the dump**. The mesh path is dead in this firmware.
5. `wave_bg_vv` is 288 bytes with `numInputSemantics == 0` and
   `numOutputSemantics == 0`, and `wave_bg_p` has `numInputSemantics == 0`.
   That is a full-screen plate, not a mesh draw. (The mesh-based wave is
   `wave2_vv`, which does read a vertex `s_inputBuffer` and a `skinMat`.)

**Conclusion: the moving PS5 home-screen background is a compute-driven
particle field. M3 (`wave0.fbxd` / `wave1.fbxd`) is retired - the mesh is not
needed and is not present.** `wave_bg_p` remains relevant as the static
gradient plate the particles are drawn over.

---

## 2. M1 - the plate, `wave_bg_p` @ `0xd85790`

### 2.1 Constant buffer

Loaded as `s_buffer_load_dwordx16` from the shader's V# at `+0x00` and `+0x40`,
so the shader reads 128 bytes. Reflection member order is
`uCoff0, uCoff1, uLightColor, uLightPos, uCenterPos` and the register uses pin
them to `float4`s:

| offset | member | component use recovered from ISA |
|---|---|---|
| `+0x00` | `uCoff0.x` (s4) | multiplies `SV_Position.x` then `clamp` -> `1/renderWidth` |
| `+0x04` | `uCoff0.y` (s5) | multiplies `SV_Position.y` then `clamp` -> `1/renderHeight`; also the `u` for the 1-D ramp fetch |
| `+0x08` | `uCoff0.z` (s6) | scales the summed noise -> dither amplitude |
| `+0x0c` | `uCoff0.w` (s7) | not referenced |
| `+0x10` | `uCoff1.x` (s8) | horizontal world extent; also the radial scale for the noise table index |
| `+0x14` | `uCoff1.y` (s9) | vertical world extent |
| `+0x18` | `uCoff1.z` (s10) | plate plane Z |
| `+0x1c` | `uCoff1.w` (s11) | not referenced |
| `+0x20..0x28` | `uLightColor.rgb` (s12,s13,s14) | added as specular tint |
| `+0x2c` | `uLightColor.w` (s15) | specular exponent control, used as `exp2(10*w + 2)` |
| `+0x30..0x38` | `uLightPos.xyz` (s16,s17,s18) | light position |
| `+0x3c` | `uLightPos.w` (s19) | specular intensity |
| `+0x40..0x48` | `uCenterPos.xyz` (s20,s21,s22) | `xy` = noise-ring centre, `z` = noise sample offset |
| `+0x4c` | `uCenterPos.w` (s23) | final output scale (multiplies r,g,b **and** a) |

### 2.2 What it computes (from ISA at `0xd85790+0x100`, pc `0x00`-`0x1dc`)

```
uv     = saturate(uCoff0.xy * SV_Position.xy)
p      = uCoff1.xy * (uv - 0.5)                       // pc 0x5c..0x9c
base   = uTex.Sample1D(uv.y).rgb                       // pc 0x70, dmask 0x7
L      = normalize(float3(p, uCoff1.z) - uLightPos.xyz)
V      = normalize(float3(p, uCoff1.z))
spec   = pow(max(0, -dot(V, L)), exp2(10*uLightColor.w + 2)) * uLightPos.w
colour = base + uLightColor.rgb * saturate(spec)
r      = length(p - uCenterPos.xy)
n      = T[(int)(uCoff1.x * r) & 0xff]                 // pc 0xcc..0x100
n      = T[(int)(uCenterPos.z + SV_Position.y + SV_Position.x*n) & 0xff]
       + T[(int)(uCenterPos.z + SV_Position.x + SV_Position.y*n) & 0xff]
colour = colour + uCoff0.z * n * (1.0/510.0)           // 0x3b008081
out    = float4(colour, 1) * uCenterPos.w              // packed f16, MRT0
```

`1.0/510.0` is the literal `0x3b008081` at pc `0x1a0`/`0x1a8`/`0x1b0`.

### 2.3 The embedded table `T`

`m_embeddedConstantBufferSizeInDQW == 64` -> 1024 bytes, reached by
`s_getpc_b64` at pc `0x28` + `0x1b4` = code offset `0x1e0`.

**File offset `0xd85a70`, 256 `float`s.** Measured: the multiset of values is
exactly `{0, 1, 2, ... 255}` - it is a **random permutation table** (a Ken-Perlin
style hash permutation stored as floats), not a colour ramp. First eight
entries: `151, 160, 137, 91, 90, 15, 131, 13`. (Those are the first eight
entries of Perlin's canonical reference permutation, so the whole table is that
permutation.)

### 2.4 M1 runtime values: **UNRECOVERED**

The three candidate offsets carried over from the previous session were checked
and none of them is the plate's uniform block:

- `0xbd0ed0` - a smooth monotonically increasing `float` table starting
  `0.943949, 0.944277, 0.944605, 0.944935, ...`. A transfer-curve LUT. **Not**
  `bgColor`/`bgCurve`.
- `0xebfe00` and `0xebfea0` - **all zero in the file image**. Zero-initialised
  storage written at runtime.

`bgColor0`, `bgColor1` and `bgCurve` are **not** members of `wave_bg_p`'s
cbuffer at all. They belong to the `Constants` cbuffer shared by
`dual_wave_bg_p` / `dual_wave_p` / `dual_wave_vv`
(`viewProjMatrix, worldMatrix, wavePathExpr, waveColor, waveParam, bgColor0,
bgColor1, bgCurve, invScreenHeight, currentTime, isVeil, usePath`). Pairing them
with `uCoff0/uCoff1/uLightColor/uLightPos/uCenterPos` in the original M1
statement was a mistake; they are two different shaders' constant buffers.

No literal block matching a filled-in `wave_bg_p` `UniformData` exists in the
image. `uCoff0.xy` is provably a reciprocal of the render-target size
(`1/1920` and `1/1080` appear in the image only inside SMAA shader literals at
`0xd7d2b4` / `0xd7d2bc` etc., never as a uniform block), so at least `uCoff0.xy`
is computed at runtime. **The runtime values of `bgColor0`, `bgColor1`,
`bgCurve`, `uCoff0`, `uCoff1`, `uLightColor`, `uLightPos` and `uCenterPos` are
UNRECOVERED.** Recovering them requires decompiling the C++ that fills the
buffer, which this pass did not do.

---

## 3. M2/M5 - the particle system

### 3.1 `BackgroundLayer::ParticleProperty` - **fully recovered, 0x44 bytes**

Cross-validated two ways: the loads in `particle_vv` (`0xda8ec0`) and the
stores in `particle_c` (`0xd9f1b0`) hit the same offsets.

| offset | type | member |
|---|---|---|
| `+0x00` | `float3` | `pos` |
| `+0x0c` | `float` | `blurBoundary` |
| `+0x10` | `float3` | `vel` |
| `+0x1c` | `float3` | `fore` |
| `+0x28` | `uint` | `transPatternFlag` (only the low 4 bits are tested) |
| `+0x2c` | `float3` | `right` |
| `+0x38` | `float` | `curLife` |
| `+0x3c` | `float` | `maxLife` |
| `+0x40` | `float` | `renLife` |

Element stride comes from the buffer V# at runtime and is UNRECOVERED
(`0x44` rounded up is the lower bound).

### 3.2 `BackgroundLayer::ParticleBlendParam` - **recovered, 32 bytes**

| offset | type | member |
|---|---|---|
| `+0x00` | `float3` | `center` |
| `+0x0c` | `float3` | `weight` |
| `+0x18` | `float` | `beginDist` |
| `+0x1c` | `float` | `endDist` |

Recovered from `particle_vv` pc `0x284`-`0x300`: the shader forms
`d = dot(weight, particlePos' - center)` (with `particlePos'.x` negated and
`.z` taken as `cameraZ - pos.z`), then smoothsteps it between `beginDist` and
`endDist`.

### 3.3 `BackgroundLayer::SRTCs` (the compute SRT) - **recovered**

| offset | type | member |
|---|---|---|
| `+0x00` | `ResourcesCs*` | resource table pointer |
| `+0x08` | `float` | `time` |
| `+0x0c` | `float` | `timeStep` |
| `+0x10` | `float` | `timeRateForLifeCountDown` |
| `+0x14` | `uint` | `isPreSimulation` |
| `+0x18` | `uint` | `transPatternFlag` |

### 3.4 `BackgroundLayer::SRTVsPs` and `SRTLargeParticleVsPs` - **recovered**

| offset | type | member |
|---|---|---|
| `+0x00` | `Resources*` | resource table pointer |
| `+0x08` | `float` | `time` |
| `+0x0c` | `float` | `timeStep` |
| `+0x10` | `uint` | `transPatternFlag` |

### 3.5 `BackgroundLayer::ResourcesVsPs` - **recovered through `+0x64`**

| offset | member |
|---|---|
| `+0x00` | `particleProperties` (16-byte buffer V#) |
| `+0x10` | `particleIds` (16-byte buffer V#) |
| `+0x20` | `numParticles` (uint) |
| `+0x24` | `offsetParticle` (uint) |
| `+0x28` | `indexStridePerParticle` (uint) |
| `+0x2c` | `maxParticleId` (uint) |
| `+0x30` | `blendBlur` (`ParticleBlendParam`, 32 bytes) |
| `+0x50` | `distStartBlurAgain` (float) |
| `+0x54` | `distEndBlurAgain` (float) |
| `+0x58` | `randSeed` (uint) |
| `+0x5c` | `unblurMinSize` (float) |
| `+0x60` | `unblurMaxSize` (float) |
| `+0x64` | `blurMaxSize` (float) |
| `+0x68..` | `blendDistDarken`, `intensityDistDarken`, `numLights`, `lights`, `cameraZ`, `groupId` - `cameraZ` is read at `+0x138`; the members between `+0x68` and `+0x138` are UNRECOVERED |

`BackgroundLayer::LightSourceProperty` members are known by name only
(`pos, dir, enabled, diffuseIntensity, diffuseDistOrCosineBeginFading,
diffuseDistOrCosineEndFading, ambientIntensity, specularIntensity,
specularPowerK`); their offsets are **UNRECOVERED**.

### 3.6 `BackgroundLayer::ResourcesLargeParticleVsPs` - **recovered through `+0x70`**

| offset | member |
|---|---|
| `+0x00` | `particleProperties` (buffer V#) |
| `+0x10` | `particleIds` (buffer V#) |
| `+0x20` | `backgroundTex` (32-byte T#) - **`Sce.Vsh.ShellUI.BGLayer.Particle0.gnf`** |
| `+0x40` | `backgroundTex2` (32-byte T#) - **`Sce.Vsh.ShellUI.BGLayer.Particle1.gnf`** |
| `+0x60` | `textureOptions` (uint) |
| `+0x64` | `particleColorInHsv` (float3) |
| `+0x70` | `useCamera` (uint) |
| `+0x74..` | `camera` (`CameraProperty` = `fovY, aspect, near, far, pos, fore, up`), then `randSeed, numParticles, offsetParticle, indexStridePerParticle, maxParticleId, blendEdgeBlur, edgeBlurMaxSize, transparency, parMinSize, parMaxSize` - offsets **UNRECOVERED** |

`large_particle_p` at pc `0x2bc`/`0x2c4` issues two `image_sample ... dmask:0x7
dim:k2d` against those two T#s with the **same** uv, so `Particle0.gnf` and
`Particle1.gnf` are two co-registered RGB layers blended per pixel.

### 3.7 Per-frame vertex work - `particle_vv` @ `0xda8ec0`

Recovered from ISA, pc `0x3c`-`0x4e0`:

- Draw topology is **6 vertices per particle** (2 triangles, one billboard
  quad): `particleIndex = SV_VertexID / 6`, `cornerIndex = SV_VertexID % 6`
  (pc `0x3c`-`0xd8`, the `0xaaaaaaab` magic divide).
- The 6 corner offsets are an **embedded float2[6] at file offset `0xda94c0`**
  (`m_embeddedConstantBufferSizeInDQW == 3` = 48 bytes; `s_getpc` at pc `0x1b4`
  + `0x348`). Measured values:
  `(-1,-1) (1,-1) (-1,1) (1,-1) (-1,1) (1,1)`.
  `large_particle_vv` carries the identical table at file offset `0xdabbc0 +
  0x100 + 0x480 = 0xdac140`.
- Buffer index is `offsetParticle + indexStridePerParticle * particleIndex`
  (pc `0xf0`-`0x114`).
- Three cull tests, all producing a degenerate/discarded vertex (pc `0xfc`-`0x180`):
  `SV_VertexID >= numParticles*6`, `index >= maxParticleId`, and
  `(ParticleProperty.transPatternFlag & 15) != (SRT.transPatternFlag & 15)`.
- **Life latch** (pc `0x184`-`0x1b4`, executed only for `cornerIndex == 0`):
  `if (renLife < 0) renLife = curLife;`
- **Per-particle RNG - Park/Miller minimal standard, recovered exactly**
  (pc `0x2f4`-`0x364`):
  `seed = particleIds[index]; seed = (16807 * seed) % 2147483647; r = seed % 1000;`
  The literals are `0x41a7 = 16807`, `0x7fffffff = 2147483647`, and the
  `0x10624dd3`/`>>6` magic divide by 1000.
- **Particle size** (pc `0x3d0`):
  `size = unblurMinSize + (r / 1000.0) * (unblurMaxSize - unblurMinSize)`
  (`0.001f` is the literal `0x3a83126f`).
- Blend factors are `smoothstep`s: the `3.0` (`0x40400000`) / `-2.0` pair at
  pc `0x384`-`0x3c8` is the canonical `t*t*(3-2t)` on a `saturate`d
  `(d - beginDist) / (endDist - beginDist)`.
- Basis is built from `fore` and `right`: the shader forms
  `cross(fore, right)` (pc `0x298`-`0x2f0`), normalises it, and uses the
  three axes to place the quad corner.
- `exp pos0` at pc `0x4b4` scales clip x by `0x3f2dd2d0 = 0.678898` and clip y
  by `0x3f9a827b = 1.207100`; six `param` exports carry `centerPos`,
  `interPos`, `uvs`, `parNormal`, `vertAndParId`, `screenPos`.

### 3.8 `particle_p` @ `0xda78d0`

- **No `image_sample` anywhere.** The small dust particles are shaded entirely
  procedurally; they do **not** sample `Particle0/1.gnf`. Only
  `large_particle_p` does.
- `m_embeddedConstantBufferSizeInDQW == 3` -> 48 bytes at file offset
  `0xda78d0 + 0x100 + 0x600 = 0xda7fd0`, read back with
  `tbuffer_load_format_xyz`, i.e. **three `float3` colours** (measured):

  | index | value | note |
  |---|---|---|
  | 0 | `(0.693767, 0.459286, 0.204934)` | |
  | 1 | `(0.420054, 0.187302, 0.075132)` | |
  | 2 | `(0.501961, 0.329412, 0.211765)` | exactly `(128, 84, 54) / 255` |

  The fourth `float3` slot is `(0,0,0)` padding.

### 3.9 `particle_c` @ `0xd9f1b0` - the simulation

29,760 bytes of ISA; **only partially decoded in this pass.**

Recovered:
- It reads its `ResourcesCs*` from `SRTCs + 0x00`, then `particleIds1` as a
  buffer V# at `ResourcesCs + 0x00` and **`particleProperties` as the
  read-write buffer V# at `ResourcesCs + 0x10`**.
- Every `buffer_store` in it targets `particleProperties` at offsets `0x00`
  (`pos`), `0x0c` (`blurBoundary`), `0x10` (`vel`), `0x1c` (`fore`), `0x2c`
  (`right`), `0x38`/`0x3c` (`curLife`, `maxLife`) - independently confirming
  the `ParticleProperty` layout in 3.1.
- It contains a `dim:k3d` `image_load` (pc `0x73d8`), consistent with a 3-D
  curl-noise volume; the reflection names
  `particleCurlSizeP, particleCurlSpeedP, particleCurlTimeRateP,
  particleCurlSpeedInit` confirm curl-noise advection.
- `numRendezVousPoints` / `particleRendezVousPoints` (`ParticleRendezVousParam
  { rv, acceleration }`) drive an attractor set.

**UNRECOVERED:** the offsets of every `ResourcesCs` member past `+0x10`, the
spawn/respawn maths, the curl-noise kernel itself, the meaning of
`particleOptions`, and every numeric simulation parameter
(`particleMinLife`, `particleMaxLife`, `blurRadiusPowerFactor`,
`blurRadiusClearEdgeThreshold`, `particleSpawnRangeMax/Min`,
`particleMaxAcceleration1`, `particleMaxRotationSpeed`, the four `particleCurl*`
values, `numRendezVousPoints`). **None of those values is stored in the eboot as
a literal** - they arrive through the SRT at runtime. **M5 is UNRECOVERED.**

---

## 4. Not done

- **The 58-firmware database was not consulted by this shader-recovery pass.**
  The extracted database is present at
  `C:\Users\sharpemu\Downloads\system_system_ex_database.part\system_system_ex_database`;
  this document makes no claim based on its contents. The measurements above
  come from the named 4.03 ShellUI eboot only.
- `particle_c`'s simulation body (section 3.9).
- The C++ that fills `wave_bg_p`'s `UniformData` (section 2.4).
