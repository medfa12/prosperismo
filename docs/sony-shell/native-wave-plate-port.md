# Native Wave Plate port

The primary animated plate in the recovered SharpEmu shell is Sony's 4.03
`NPXS40087` Plane2 `wave_bg_p` pass. It is not the separate FirstWave
`fw_background_p` pixel fallback.

SharpEmu mounts this pass beneath title art and the particle layer in
`SystemAssets/Shell/ShellBackground.cs`; its HomeScreen preset (4) resolves
through the native state map to Plane2 record 2. The direct React Native
Windows port is:

- `windows/Prosperismo/NativeWavePlate.cpp`
- `windows/Prosperismo/NativeWavePlate.h`
- `ProsperismoNativeBackground.cpp` owns its 60 Hz source-frame clock and
  calls it into the persistent composition surface.

The C++ code preserves the original evaluator's authored 28-float record,
Hermite three-stop ramp, projection/light/specular equations, 256-entry
permutation, integer frame phase, and direct UNORM export. It precomputes the
same invariant terms as the SharpEmu evaluator and recalculates only the
frame-dependent grain. It does not use a copied Sony shader payload.

`fw_background_p` remains recovered and validated in the FirstWave toolchain.
It is not selected as the visible primary plate because that would replace the
actual SharpEmu Plane2 route with a different Sony renderer. The unresolved
FirstWave `fw_flow_vl/h/dv` tessellation path remains a future additional
renderer; it must not be mislabeled as Plane2 or a substitute for it.
