# Prosperismo native-background producer

Local development helper for the RNW two-slot background surface. It renders
the user's recovered BGLayer particle draw through SharpEmu's persistent Vulkan
renderer and publishes frames through `Local\ProsperismoShellBackground`.

No firmware shader, texture, draw-cache, or rendered frame is part of this
project. Point `SharpEmuSourceRoot` at a checkout of the preserved
`codex/ps5-shell-integration` renderer and pass the oracle paths at runtime:

```powershell
dotnet build .\tools\Prosperismo.NativeBackgroundProducer `
  -p:SharpEmuSourceRoot=C:\path\to\sharpemu-renderer

dotnet run --project .\tools\Prosperismo.NativeBackgroundProducer `
  -p:SharpEmuSourceRoot=C:\path\to\sharpemu-renderer -- `
  --cache-root C:\path\to\native-small-bottom\draw-cache `
  --firmware-root C:\path\to\PS5_4.03_reconstructed
```

Add `--frame-limit 2` for a bounded renderer/protocol smoke test.

The producer publishes only the blue ripple/dust **overlay**. It subtracts the
renderer clear `(1,1,9)` and tags `FrameHeader.reserved0` with layer kind `2`.
This stream does not contain a persistent plate or room/ray base. The preserved
4.03 code proves Plane2 record 2 is blue, while the warm folded DDS candidates
are per-title hub artwork; neither is relabelled as the system Settings base.
The RNW consumer must use additive composition for these zero-alpha colour
deltas and hide this overlay in Settings. The room/ray owner remains an explicit
recovery gap rather than a host-authored substitute.

Run the asset-free contract test with:

```powershell
dotnet run --project .\tools\Prosperismo.NativeBackgroundProducer `
  -p:SharpEmuSourceRoot=C:\path\to\sharpemu-renderer -- --self-test
```
