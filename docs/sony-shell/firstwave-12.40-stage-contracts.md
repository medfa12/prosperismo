# FirstWave 12.40 shader-stage contracts

This note is the renderer-facing index for the byte-exact manifest in
[`firstwave-12.40-shader-contracts.json`](firstwave-12.40-shader-contracts.json).
The manifest describes code boundaries, resource descriptors, scalar constant
loads, exports, and opcode counts. It contains no copied firmware program
bytes.

## Evidence boundary

- Source image: the local 12.40 `NPXS40087/eboot.bin`, 21,695,212 bytes,
  SHA-256 `18c9320be767a540578e54cb769f94996c3f37a4f158ef977ebfb798ffd6b04f`.
- Decoder: SDK 10 `libSceShaderIsaP.dll`, raw-disassembly generation 2.
- Every instruction is accepted only when `sceShaderIsaGetInstructionSize`
  agrees with the size in Sony's JSON result.
- Every stage slice is separately SHA-256 pinned. The verifier checks the full
  eboot identity, every slice, instruction count, first and terminal opcode,
  every scalar load, resource-opcode counts, and every export.

Run the local oracle verification with:

```powershell
python scripts\inspect_firstwave_shader.py `
  --eboot "<12.40 NPXS40087 eboot.bin>" `
  --shader-isa-dll "<SDK 10 host_tools\bin\libSceShaderIsaP.dll>" `
  --manifest docs\sony-shell\firstwave-12.40-shader-contracts.json
```

The expected result lists all ten stages under `verified` and an empty
`failures` array.

## Exact entry map

| Firmware name | File offset | Code bytes | Instructions | Terminal contract |
|---|---:|---:|---:|---|
| `fw_blurh_p` | `0x11F4800` | `0x35C` | 122 | `s_endpgm` |
| `fw_blurv_p` | `0x11F4D00` | `0x35C` | 122 | `s_endpgm` |
| `fw_blur_vv` | `0x11F5200` | `0xCC` | 40 | `s_endpgm` |
| `fw_flow_dv` | `0x11F5500` | `0xF68` | 700 | `s_endpgm` |
| `fw_flow_h` | `0x11F6600` | `0x108` | 41 | `s_endpgm` |
| `fw_flow_vl` | `0x11F6900` | `0x72C` | 325 | `s_swappc_b64 null,s[6:7]` |
| `fw_oit_p` | `0x11F7200` | `0xC84` | 524 | `s_endpgm` |
| `fw_comp_oit_p` | `0x11F8100` | `0x3D0` | 180 | `s_endpgm` |
| `fw_fxaa_p` | `0x11F8700` | `0xA00` | 463 | `s_endpgm` |
| `fw_background_p` | `0x11F9300` | `0x230` | 90 | `s_endpgm` |

The `fw_flow_vl` boundary is important. It returns through `s[6:7]` at
`0x11F7028`; it does **not** run to the later `s_endpgm` at `0x11F7E80`.
The intervening bytes include shader metadata/padding, and `fw_oit_p` begins at
`0x11F7200`. Treating the next `s_endpgm` as the local-stage boundary merges
two different entries and produces a false contract.

## Renderer binding contracts

- Blur H/V: one 2D image at `s[0:7]`, one sampler at `s[8:11]`, a four-dword
  constant load through `s[12:15]`, 14 `image_sample` operations, compressed
  `MRT0` output. The two programs differ in coordinate axis, not bindings.
- Blur geometry: root pointer `s[12:13]` supplies vertex descriptors
  `s[0:3]` and `s[4:7]`; constants use `s[8:11]`. It exports primitive,
  position, and `param0`.
- Flow VL: root pointer `s[12:13]` supplies two vertex buffers in
  `s[16:23]`. It reads `time` at `+0x184`, writes two 128-bit LDS records at
  offsets `0` and `0x10`, then returns to the merged-stage continuation.
- Flow H: reads four LDS pairs and writes two storage-buffer descriptors loaded
  from root-table offsets `+0x30` and `+0x20`.
- Flow DV: one control-point buffer descriptor is loaded from root offset
  `+0x30`; 16 four-dword reads cover offsets `0x0..0x1E0` in `0x20` steps.
  It exports primitive, position, and five parameter records.
- OIT: `s[0:3]` is the indexed node buffer and `s[4:7]` is the atomic counter.
  Constants use `s[8:11]` and read the color/light block plus blur parameters,
  `time`, `waveOpacity`, `oitSliceOffset`, and screen dimensions.
- OIT composite: `s[0:3]` and `s[4:7]` are the node/head storage buffers;
  constants use `s[8:11]`; output is compressed `MRT0`.
- FXAA: one 2D image at `s[0:7]` and sampler at `s[8:11]`; the program uses
  resinfo, gather, gather-with-offset, and LZ samples and exports compressed
  `MRT0`.
- Persistent base: no sampled image or storage resource. `fw_background_p`
  reads the shared constant buffer through `s[0:3]` and procedurally exports
  the dark-room base to compressed `MRT0`.

The manifest is the source of truth for individual scalar load offsets and
sizes. Renderer code should build typed bindings from it, not infer descriptor
roles from register numbers shared across unrelated stages.

## Shared constant layout used by these entries

| Byte offset | Member |
|---:|---|
| `0x40` | `worldProjectionMatrix` |
| `0x110`, `0x120` | `BackgroundColour0`, `BackgroundColour1` |
| `0x130` | `BackgroundLightColour` |
| `0x140`, `0x150`, `0x160` | reflection, environment, edge colours |
| `0x170` | `BlurParameters` |
| `0x180`, `0x184`, `0x188` | `opacity`, `time`, `waveOpacity` |
| `0x18C` | `oitSliceOffset` |
| `0x190`, `0x194` | `screenDim.x`, `screenDim.y` |

The stage-specific `constant_reads` arrays record which part of this ABI each
program actually touches. A member's presence in the shared layout does not
mean every stage reads it.
